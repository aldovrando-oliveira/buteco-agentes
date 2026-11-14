using System.Text.Json;
using Buteco.Api.Identity.Flow;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Buteco.Api.Users.Federated;
using Buteco.Api.Users.Login;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// O aceite de convite pelo provedor (design.md da change aceite-convite-google, D1 a D4): o início a partir do
/// link do convite e a decisão do retorno com a intenção <c>convite</c>.
///
/// <list type="bullet">
/// <item><b>o state token leva o id do convidado e o hash do link, nunca o link</b>: o payload do state token
/// fica gravado em <c>openiddict_tokens</c> até a limpeza, e o link seria uma credencial em repouso;</item>
/// <item><b>a ordem do retorno</b>: o domínio, o convite (com o hash), o e-mail verificado, o e-mail igual, o
/// par sem vínculo; depois o consumo pela condição do aceite por senha (<see cref="InvitationConsumption"/>),
/// mais o e-mail normalizado, e o vínculo, na mesma transação;</item>
/// <item><b>as recusas voltam ao convite</b> (<c>/convite?recusa=…</c>), sem sessão e sem vínculo.</item>
/// </list>
/// </summary>
public static partial class FederatedLoginEndpoints
{
    public static readonly EventId InvitationRefusedEvent = new(1202, "FederatedInvitationRefused");
    public static readonly EventId InvitationAcceptedEvent = new(1203, "FederatedInvitationAccepted");

    public sealed record InvitationStartRequest(string? Token);

    private static async Task<IResult> StartInvitationAsync(
        string chave,
        HttpContext httpContext,
        SessionRequestGuards guards,
        FederatedProviderCatalog catalog,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        InvitationStartRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<InvitationStartRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.Problem(title: "Corpo do pedido inválido.", statusCode: StatusCodes.Status400BadRequest);
        }

        // A chave antes do convite: não custa banco, e a lista dos provedores já é pública.
        if (catalog.Find(chave) is not { } provider)
        {
            return NotConfigured();
        }

        // O convite antes de ir ao provedor, pela mesma busca do aceite por senha; o inválido tem a resposta
        // única dos links, sem cookie do fluxo e sem pedido ao provedor.
        if (await AccessLinkPublicEndpoints.FindAsync(dbContext, request?.Token, AccessLinkPurpose.Invitation, UserStatus.Invited, timeProvider, cancellationToken) is not { } link)
        {
            return AccessLinkPublicEndpoints.InvalidLink();
        }

        return await FederatedChallenge.BeginAsync(httpContext, provider, new Dictionary<string, string?>
        {
            [FederatedLogin.Properties.Intent] = FederatedLogin.Intents.Invitation,
            [FederatedLogin.Properties.UserId] = link.UserId.ToString(),
            [FederatedLogin.Properties.LinkHash] = WebEncoders.Base64UrlEncode(link.Hash),
        });
    }

    /// <summary>O retorno do aceite (spec <c>federated-login</c>, "Decisão do aceite de convite no retorno"), na ordem.</summary>
    private static async Task<IResult> InvitationAsync(
        HttpContext httpContext,
        FederatedProvider provider,
        ProviderIdentity identity,
        IDictionary<string, string?> items,
        AppDbContext dbContext,
        FederatedRefusalCookie refusal,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Guid? invitedId = Guid.TryParse(Item(items, FederatedLogin.Properties.UserId), out var parsed) ? parsed : null;

        // 1. O domínio vale para todo retorno.
        if (!identity.MatchesHostedDomain(provider.HostedDomain))
        {
            return RefuseInvitation(logger, provider, FederatedLogin.Refusals.DomainNotAllowed, invitedId);
        }

        // 2. O convite ainda pendente e com o mesmo hash, antes do e-mail: com o convite morto, "entre com a
        // conta certa" não serve (Q4 da #274). Esta leitura escolhe o motivo; o UPDATE do 6 é que decide.
        var hash = DecodeLinkHash(Item(items, FederatedLogin.Properties.LinkHash));
        var now = timeProvider.GetUtcNow();
        var invited = invitedId is { } id && hash is not null
            ? await InvitationConsumption.Pending(dbContext.Users.AsNoTracking(), id, hash, now)
                .Select(user => new { user.Id, user.NormalizedEmail })
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        if (invited is null || hash is null)
        {
            return RefuseInvitation(logger, provider, FederatedLogin.Refusals.InvalidInvitation, invitedId);
        }

        // 3. Um e-mail que o provedor não confirmou não diz nada, nem igual nem diferente.
        if (!identity.EmailVerified || identity.Email is null)
        {
            return RefuseInvitation(logger, provider, FederatedLogin.Refusals.EmailNotVerified, invited.Id);
        }

        // 4. O e-mail confirmado igual ao do convite; o aviso leva o confirmado, pelo cookie, nunca pela URL.
        var normalized = EmailNormalization.Normalize(identity.Email);
        if (normalized != invited.NormalizedEmail)
        {
            refusal.Write(httpContext.Response, FederatedLogin.Refusals.OtherEmail, provider.Key, identity.Email);
            return RefuseInvitation(logger, provider, FederatedLogin.Refusals.OtherEmail, invited.Id);
        }

        // 5. O par ainda sem vínculo.
        var issuer = provider.Issuer.AbsoluteUri;
        if (await dbContext.UserFederatedIdentities.AnyAsync(candidate => candidate.Issuer == issuer && candidate.Subject == identity.Subject, cancellationToken))
        {
            return RefuseInvitation(logger, provider, FederatedLogin.Refusals.InUse, invited.Id);
        }

        await httpContext.RequestServices.GetRequiredService<FederatedInvitationPause>().BeforeConsumeAsync(invited.Id, cancellationToken);

        // 6. O consumo e o vínculo juntos: sem vínculo órfão, e sem convite consumido sem vínculo.
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var consumed = await InvitationConsumption.Pending(dbContext.Users, invited.Id, hash, now)
                .Where(user => user.NormalizedEmail == normalized)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(user => user.Status, UserStatus.Active)
                        .SetProperty(user => user.AccessLinkHash, (byte[]?)null)
                        .SetProperty(user => user.AccessLinkPurpose, (AccessLinkPurpose?)null)
                        .SetProperty(user => user.AccessLinkExpiresAt, (DateTimeOffset?)null),
                    cancellationToken);

            // Um aceite por senha, outro retorno, um link novo, o cancelamento, o vencimento ou o e-mail editado
            // entre a leitura e o consumo.
            if (consumed == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return RefuseInvitation(logger, provider, FederatedLogin.Refusals.InvalidInvitation, invited.Id);
            }

            dbContext.UserFederatedIdentities.Add(UserFederatedIdentity.Connect(invited.Id, issuer, identity.Subject, identity.Email, now));
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Outro usuário ganhou o par entre a conferência e a gravação: o rollback devolve o convite.
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                return RefuseInvitation(logger, provider, FederatedLogin.Refusals.InUse, invited.Id);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        // 7. Depois do commit: o bloqueio zerado (quem tem o link controla a conta, como no aceite por senha),
        // o log, e a sessão pendente com o usuário relido. O passo é o cadastro: o convidado nunca tem a
        // verificação, mas o cálculo é o do login.
        await httpContext.RequestServices.GetRequiredService<LoginThrottleStore>().ResetAsync(invited.NormalizedEmail, cancellationToken);
        LogInvitationAccepted(logger, invited.Id, provider.Key);

        var user = await dbContext.Users.AsNoTracking().SingleAsync(candidate => candidate.Id == invited.Id, cancellationToken);
        await IssuerSession.SignInAsync(httpContext, user, twoFactorDone: false);
        var step = user.IsTwoFactorEnabled ? SessionEndpoints.CodeStep : SessionEndpoints.EnrollmentStep;
        return Results.Redirect($"{AuthorizeEndpoint.LoginPath}?federado={Uri.EscapeDataString(provider.Key)}&segundoPasso={step}&convite=aceito");
    }

    private static byte[]? DecodeLinkHash(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return WebEncoders.Base64UrlDecode(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IResult RefuseInvitation(ILogger logger, FederatedProvider provider, string reason, Guid? userId)
    {
        LogInvitationRefused(logger, provider.Key, reason, userId);
        return InvitationRefusal(reason, provider.Key);
    }

    private static IResult InvitationRefusal(string reason, string? providerKey) =>
        Results.Redirect($"{FederatedLogin.InvitationPagePath}?recusa={reason}{ProviderQuery(providerKey)}");

    [LoggerMessage(EventId = 1202, EventName = "FederatedInvitationRefused", Level = LogLevel.Warning,
        Message = "Aceite de convite pelo provedor recusado: provedor {ProviderKey}, motivo {Reason}, convidado {UserId}.")]
    public static partial void LogInvitationRefused(ILogger logger, string providerKey, string reason, Guid? userId);

    [LoggerMessage(EventId = 1203, EventName = "FederatedInvitationAccepted", Level = LogLevel.Information,
        Message = "Convite aceito pelo provedor {ProviderKey}: usuário {UserId}.")]
    public static partial void LogInvitationAccepted(ILogger logger, Guid userId, string providerKey);
}
