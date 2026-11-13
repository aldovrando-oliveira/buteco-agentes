using Buteco.Api.Identity;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Buteco.Api.Users.AccessLinks;
using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.TwoFactor;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.ExternalApplications;

/// <summary>
/// As aplicações externas, sob <c>/applications</c> (design.md da change aplicacoes-externas-credencial,
/// D3 e D4; spec <c>external-applications</c>). Só o Admin, inclusive para ler, pelo papel
/// <b>gravado</b> (<see cref="CurrentUser.FindAdminAsync"/>), como em <c>/users</c>: o rebaixado perde
/// Aplicações na requisição seguinte, e não em até 15 min. Uma credencial permanente é o pior caso do
/// token velho.
///
/// <list type="bullet">
/// <item><b>Criar e gerar segredo pedem a senha de quem chama</b> (<see cref="CallerPassword"/>; Q7): as
/// duas transformam um access token roubado em acesso permanente, o critério escrito lá. Revogar,
/// desativar, reativar e editar não pedem: só tiram acesso, ou não devolvem credencial. O bloqueio
/// (<c>423</c>) vem antes de tudo, e o corpo inválido não conta no bloqueio, como no convite;</item>
/// <item><b>a criação é uma transação só</b>: a aplicação, o cliente do OpenIddict (que grava pelo
/// mesmo <see cref="AppDbContext"/>) e o primeiro segredo. Uma falha no meio não deixa cliente sem
/// cadastro, nem cadastro sem cliente;</item>
/// <item><b>desativar revoga os segredos vivos na mesma transação</b> (Q13), e o emissor ainda recusa a
/// aplicação desativada (<see cref="ButecoApplicationManager{TApplication}"/>): duas barreiras.
/// Reativar volta sem segredo vivo, e o Admin gera um novo, com a senha;</item>
/// <item><b>gerar segredo e desativar travam a linha da aplicação</b> (<c>FOR UPDATE</c>), para duas
/// gerações simultâneas não passarem do limite de três vivos, nem uma geração escapar de uma
/// desativação;</item>
/// <item><b>desativar, reativar e revogar são idempotentes</b>: o estado já pedido responde <c>200</c> sem
/// mudar nada.</item>
/// </list>
/// </summary>
public static class ExternalApplicationEndpoints
{
    public const string GroupPath = "/applications";

    public sealed record CreateApplicationRequest(string? Nome, string? Descricao, string? Senha, string? Codigo = null);

    public sealed record UpdateApplicationRequest(string? Nome, string? Descricao);

    public sealed record GenerateSecretRequest(string? Senha, string? Codigo = null);

    private const int ClientIdAttempts = 3;

    public static IEndpointRouteBuilder MapExternalApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(GroupPath);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateAsync);
        group.MapPost("/{id:guid}/activate", ActivateAsync);
        group.MapPost("/{id:guid}/secrets", GenerateSecretAsync);
        group.MapPost("/{id:guid}/secrets/{secretId:guid}/revoke", RevokeSecretAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        string? busca,
        string? estado,
        HttpContext httpContext,
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        var query = dbContext.ExternalApplications.AsNoTracking();
        switch (estado)
        {
            case null or "":
                break;
            case "ativas":
                query = query.Where(application => application.DeactivatedAt == null);
                break;
            case "desativadas":
                query = query.Where(application => application.DeactivatedAt != null);
                break;
            default:
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["estado"] = [ExternalApplicationFields.StateMessage] });
        }

        if (busca?.Trim() is { Length: > 0 } text)
        {
            var upper = text.ToUpperInvariant();
            query = query.Where(application =>
                application.NormalizedName.Contains(upper) || application.ClientId.ToUpper().Contains(upper));
        }

        var applications = await query
            .OrderBy(application => application.DeactivatedAt != null)
            .ThenBy(application => application.NormalizedName)
            .ThenBy(application => application.Id)
            .ToListAsync(cancellationToken);

        // Os agentes liberados de cada aplicação da página, numa consulta (agentes-por-aplicacao, Q11).
        var ids = applications.Select(application => application.Id).ToList();
        var chips = (await dbContext.ExternalApplicationAgents
                .AsNoTracking()
                .Where(link => ids.Contains(link.ApplicationId))
                .Join(dbContext.Agents, link => link.AgentId, agent => agent.Id, (link, agent) => new { link.ApplicationId, agent.Id, agent.Name })
                .OrderBy(row => row.Name)
                .ThenBy(row => row.Id)
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.ApplicationId, row => new ApplicationAgentChip(row.Id, row.Name));

        return TypedResults.Ok(applications.Select(application => ApplicationSummary.From(application, chips[application.Id].ToList())).ToList());
    }

    private static async Task<IResult> CreateAsync(
        CreateApplicationRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        TwoFactorVerifier verifier,
        IOpenIddictApplicationManager applications,
        IExternalApplicationClientIdGenerator clientIds,
        IdentityIssuerState issuer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, caller, cancellationToken) is { } locked)
        {
            return locked;
        }

        // Antes da senha: o corpo inválido e o nome repetido não contam no bloqueio de quem chama.
        if (ExternalApplicationFields.Validate(request.Nome, request.Descricao, out var name, out var description) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var normalized = ExternalApplication.Normalize(name);
        if (await dbContext.ExternalApplications.AnyAsync(application => application.NormalizedName == normalized, cancellationToken))
        {
            return ExternalApplicationFields.NameInUse();
        }

        if (await CallerPassword.RejectAsync(caller, request.Senha, request.Codigo, hasher, verifier, throttle, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        var clientId = await NewClientIdAsync(dbContext, applications, clientIds, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var application = ExternalApplication.Create(clientId, name, description, now);
        var secretValue = ExternalApplicationSecretValue.Generate();
        var secret = ExternalApplicationSecret.Create(application.Id, secretValue, now);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await applications.CreateAsync(Descriptor(clientId, name, ExternalApplicationSecretValue.GenerateFiller()), cancellationToken);
                dbContext.ExternalApplications.Add(application);
                dbContext.ExternalApplicationSecrets.Add(secret);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (ExternalApplicationFields.IsNameUniqueViolation(exception))
            {
                // Outra criação gravou o mesmo nome entre a conferência e esta escrita; a transação
                // volta inteira, o cliente do OpenIddict junto.
                return ExternalApplicationFields.NameInUse();
            }
        }

        return TypedResults.Created(
            $"{GroupPath}/{application.Id}",
            new CreatedApplication(
                application.Id,
                application.Name,
                application.Description,
                application.ClientId,
                application.IsActive,
                application.CreatedAt,
                application.LastUsedAt,
                TokenEndpoint(issuer),
                CreatedSecret.From(secret, secretValue)));
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, IdentityIssuerState issuer, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        return await dbContext.ExternalApplications.FindAsync([id], cancellationToken) is { } application
            ? await DetailAsync(application, dbContext, issuer, timeProvider, cancellationToken)
            : ExternalApplicationFields.NotFound();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateApplicationRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        IOpenIddictApplicationManager applications,
        IdentityIssuerState issuer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (ExternalApplicationFields.Validate(request.Nome, request.Descricao, out var name, out var description) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await dbContext.ExternalApplications.FindAsync([id], cancellationToken) is not { } application)
        {
            return ExternalApplicationFields.NotFound();
        }

        var normalized = ExternalApplication.Normalize(name);
        if (await dbContext.ExternalApplications.AnyAsync(other => other.Id != id && other.NormalizedName == normalized, cancellationToken))
        {
            return ExternalApplicationFields.NameInUse();
        }

        application.Rename(name, description);
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                // O nome vai também ao DisplayName do cliente no OpenIddict. Só o DisplayName muda: o
                // descritor gravado é relido e regravado, sem tocar no segredo de enchimento.
                if (await applications.FindByClientIdAsync(application.ClientId, cancellationToken) is { } client)
                {
                    var stored = new OpenIddictApplicationDescriptor();
                    await applications.PopulateAsync(stored, client, cancellationToken);
                    stored.DisplayName = name;
                    await applications.PopulateAsync(client, stored, cancellationToken);
                    await applications.UpdateAsync(client, cancellationToken);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (ExternalApplicationFields.IsNameUniqueViolation(exception))
            {
                return ExternalApplicationFields.NameInUse();
            }
        }

        return await DetailAsync(application, dbContext, issuer, timeProvider, cancellationToken);
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, IdentityIssuerState issuer, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await LockAsync(dbContext, id, cancellationToken) is not { } application)
        {
            return ExternalApplicationFields.NotFound();
        }

        if (application.IsActive)
        {
            // A revogação e a desativação na mesma transação (Q13): uma falha entre as duas não deixa a
            // aplicação desativada com segredo vivo, nem segredos revogados numa aplicação ativa.
            var now = timeProvider.GetUtcNow();
            await dbContext.ExternalApplicationSecrets
                .Where(secret => secret.ApplicationId == id && secret.RevokedAt == null && secret.ExpiresAt > now)
                .ExecuteUpdateAsync(set => set.SetProperty(secret => secret.RevokedAt, now), cancellationToken);
            application.Deactivate(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(application, dbContext, issuer, timeProvider, cancellationToken);
    }

    private static async Task<IResult> ActivateAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, IdentityIssuerState issuer, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (await dbContext.ExternalApplications.FindAsync([id], cancellationToken) is not { } application)
        {
            return ExternalApplicationFields.NotFound();
        }

        if (!application.IsActive)
        {
            application.Activate();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await DetailAsync(application, dbContext, issuer, timeProvider, cancellationToken);
    }

    private static async Task<IResult> GenerateSecretAsync(
        Guid id,
        GenerateSecretRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        LoginThrottleStore throttle,
        PasswordHasher hasher,
        TwoFactorVerifier verifier,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is not { } caller)
        {
            return CurrentUser.Forbidden();
        }

        if (await LoginThrottleResponses.LockedAsync(httpContext, throttle, caller, cancellationToken) is { } locked)
        {
            return locked;
        }

        // As conferências antes da senha, sem trava, para o 404 e os 409 não contarem no bloqueio de
        // quem chama. A senha fica FORA da transação: o LoginThrottleStore grava a falha pelo mesmo
        // AppDbContext, e a transação desfeita levaria a contagem junto.
        if (await GenerateSecretRefusalAsync(dbContext, id, timeProvider.GetUtcNow(), lockRow: false, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (await CallerPassword.RejectAsync(caller, request.Senha, request.Codigo, hasher, verifier, throttle, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        // De novo, sob a trava da linha: uma desativação ou outra geração pode ter confirmado entre a
        // primeira conferência e aqui.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (await GenerateSecretRefusalAsync(dbContext, id, now, lockRow: true, cancellationToken) is { } refusedLocked)
        {
            return refusedLocked;
        }

        var alive = await dbContext.ExternalApplicationSecrets
            .Where(secret => secret.ApplicationId == id && secret.RevokedAt == null && secret.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var previous in alive)
        {
            previous.Supersede(now);
        }

        var value = ExternalApplicationSecretValue.Generate();
        var secret = ExternalApplicationSecret.Create(id, value, now);
        dbContext.ExternalApplicationSecrets.Add(secret);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return TypedResults.Created($"{GroupPath}/{id}", CreatedSecret.From(secret, value));
    }

    // O 404, o 409 da aplicação desativada e o 409 do limite de segredos vivos, nesta ordem.
    private static async Task<IResult?> GenerateSecretRefusalAsync(
        AppDbContext dbContext, Guid id, DateTimeOffset now, bool lockRow, CancellationToken cancellationToken)
    {
        var application = lockRow
            ? await LockAsync(dbContext, id, cancellationToken)
            : await dbContext.ExternalApplications.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (application is null)
        {
            return ExternalApplicationFields.NotFound();
        }

        if (!application.IsActive)
        {
            return TypedResults.Problem(title: ExternalApplicationFields.DeactivatedMessage, statusCode: StatusCodes.Status409Conflict);
        }

        var aliveCount = await dbContext.ExternalApplicationSecrets
            .CountAsync(secret => secret.ApplicationId == id && secret.RevokedAt == null && secret.ExpiresAt > now, cancellationToken);
        return aliveCount >= ExternalApplicationSecret.MaxAlive
            ? TypedResults.Problem(title: ExternalApplicationFields.SecretLimitMessage, statusCode: StatusCodes.Status409Conflict)
            : null;
    }

    private static async Task<IResult> RevokeSecretAsync(
        Guid id,
        Guid secretId,
        HttpContext httpContext,
        AppDbContext dbContext,
        IdentityIssuerState issuer,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (await dbContext.ExternalApplications.FindAsync([id], cancellationToken) is not { } application)
        {
            return ExternalApplicationFields.NotFound();
        }

        if (await dbContext.ExternalApplicationSecrets.SingleOrDefaultAsync(secret => secret.Id == secretId && secret.ApplicationId == id, cancellationToken) is not { } target)
        {
            return ExternalApplicationFields.SecretNotFound();
        }

        // Revogar o já revogado, ou o vencido, não muda nada: o vencido já é recusado pela data, e
        // gravar revogado_em nele trocaria "expirado" por "revogado" na lista sem nada ter acontecido.
        var now = timeProvider.GetUtcNow();
        if (target.IsAliveAt(now))
        {
            target.Revoke(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await DetailAsync(application, dbContext, issuer, timeProvider, cancellationToken);
    }

    private static async Task<IResult> DetailAsync(
        ExternalApplication application, AppDbContext dbContext, IdentityIssuerState issuer, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var secrets = await dbContext.ExternalApplicationSecrets.AsNoTracking()
            .Where(secret => secret.ApplicationId == application.Id)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(ApplicationDetail.From(application, secrets, TokenEndpoint(issuer), timeProvider.GetUtcNow()));
    }

    // A linha da aplicação travada até o fim da transação, e relida, já rastreada.
    private static async Task<ExternalApplication?> LockAsync(AppDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.ExternalApplications
            .FromSqlInterpolated($"SELECT * FROM external_applications WHERE id = {id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    // O issuer é o da url pública, obrigatória no boot: no app que atende, nunca é nulo.
    private static string TokenEndpoint(IdentityIssuerState issuer) => new Uri(issuer.Issuer!, "connect/token").AbsoluteUri;

    private static async Task<string> NewClientIdAsync(
        AppDbContext dbContext, IOpenIddictApplicationManager applications, IExternalApplicationClientIdGenerator clientIds, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var candidate = ExternalApplicationClientId.Ensure(clientIds.Next());
            var taken = await dbContext.ExternalApplications.AnyAsync(application => application.ClientId == candidate, cancellationToken) ||
                await applications.FindByClientIdAsync(candidate, cancellationToken) is not null;
            if (!taken)
            {
                return candidate;
            }

            if (attempt == ClientIdAttempts)
            {
                throw new InvalidOperationException($"Nenhum client_id livre em {ClientIdAttempts} tentativas.");
            }
        }
    }

    /// <summary>
    /// O cliente do OpenIddict da aplicação (D3): confidencial, consentimento implícito, e só o
    /// endpoint de token, o <c>client_credentials</c> e o escopo <c>buteco-api</c>. <b>Sem
    /// <c>gt:refresh_token</c></b> (herança da #209, D2: com ela, cada pedido criaria autorização e
    /// refresh token gravados), e sem <c>buteco-inbox</c> nem <c>buteco-connectors</c>: o
    /// <c>apps/inbox</c> e o <c>apps/connectors</c> mapeiam <c>buteco-painel</c> para <c>operator</c>, e a
    /// aplicação nunca entra neles.
    /// </summary>
    public static OpenIddictApplicationDescriptor Descriptor(string clientId, string name, string filler)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = ExternalApplicationClientId.Ensure(clientId),
            ClientSecret = filler,
            ClientType = ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = name,
        };

        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        descriptor.Permissions.Add(Permissions.Prefixes.Scope + IdentityClients.ApiScope);

        return descriptor;
    }
}
