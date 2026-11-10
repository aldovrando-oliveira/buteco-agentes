using System.Security.Cryptography;
using Buteco.Api.ExternalApplications;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;

namespace Buteco.Api.Identity.Clients;

/// <summary>
/// O gerente de aplicações do OpenIddict com a conferência do segredo estendida (design.md da change
/// aplicacoes-externas-credencial, D4; spec <c>oidc-issuer</c>, "A aplicação externa é autenticada
/// pelos segredos dela"). Registrado por <c>ReplaceApplicationManager(typeof(...&lt;&gt;))</c>: o
/// <see cref="IOpenIddictApplicationManager"/> do EF Core resolve pelo gerente tipado
/// (<c>OpenIddictEntityFrameworkCoreBuilder.cs</c> l. 98–99), e o servidor confere o segredo do pedido
/// de token por ele (<c>OpenIddictServerHandlers.cs</c> l. 1224; tag 7.7.2).
///
/// <para>
/// <b>Só a conferência do segredo muda, e ela vale para todo cliente:</b>
/// <list type="bullet">
/// <item><b>cliente que está no cadastro de aplicações externas</b> → só os segredos dele, vivos, e só
/// com a aplicação ativa. <b>Nunca o <c>base</c></b>: o <c>ClientSecret</c> que o OpenIddict guarda para
/// ele é o enchimento (<see cref="ExternalApplicationSecretValue.GenerateFiller"/>), que ninguém deveria
/// conhecer e que, mesmo assim, nunca é aceito;</item>
/// <item><b>qualquer outro cliente</b> (o <c>buteco-servico-*</c>, o cliente de teste) → o <c>base</c>, sem
/// consultar o cadastro. Um cliente de serviço não ganha caminho novo: um segredo de aplicação com o
/// mesmo texto do dele não o autentica nem é marcado como usado.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>O último uso é gravado aqui</b> (D6), no segredo que conferiu e na aplicação, por um <c>UPDATE</c>
/// condicional: no máximo uma escrita por minuto por segredo, mesmo com um cliente que pede token a
/// cada chamada. Nada disso entra no caminho de uma chamada A2A.
/// </para>
/// </summary>
public sealed class ButecoApplicationManager<TApplication>(
    IOpenIddictApplicationCache<TApplication> cache,
    ILogger<OpenIddictApplicationManager<TApplication>> logger,
    IOptionsMonitor<OpenIddictCoreOptions> options,
    IOpenIddictApplicationStore<TApplication> store,
    AppDbContext dbContext,
    TimeProvider timeProvider)
    : OpenIddictApplicationManager<TApplication>(cache, logger, options, store)
    where TApplication : class
{
    /// <summary>A escrita do último uso é pulada se a anterior tem menos que isto.</summary>
    public static readonly TimeSpan LastUseResolution = TimeSpan.FromMinutes(1);

    public override async ValueTask<bool> ValidateClientSecretAsync(
        TApplication application, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var clientId = await GetClientIdAsync(application, cancellationToken);
        var external = clientId is null
            ? null
            : await dbContext.ExternalApplications.AsNoTracking()
                .Where(candidate => candidate.ClientId == clientId)
                .Select(candidate => new { candidate.Id, candidate.DeactivatedAt })
                .SingleOrDefaultAsync(cancellationToken);

        if (external is null)
        {
            return await base.ValidateClientSecretAsync(application, secret, cancellationToken);
        }

        if (external.DeactivatedAt is not null)
        {
            return false;
        }

        var hash = ExternalApplicationSecretValue.Hash(secret);
        var now = timeProvider.GetUtcNow();
        var match = await dbContext.ExternalApplicationSecrets.AsNoTracking()
            .Where(candidate => candidate.Hash == hash && candidate.ApplicationId == external.Id)
            .Select(candidate => new { candidate.Id, candidate.Hash, candidate.RevokedAt, candidate.ExpiresAt })
            .SingleOrDefaultAsync(cancellationToken);

        // O índice já acha pelo hash; a comparação em tempo constante fica como a das outras
        // credenciais da casa, e o filtro de vivo é o da spec: não revogado e não vencido.
        if (match is null ||
            !CryptographicOperations.FixedTimeEquals(match.Hash, hash) ||
            match.RevokedAt is not null ||
            match.ExpiresAt <= now)
        {
            return false;
        }

        await RecordUseAsync(external.Id, match.Id, now, cancellationToken);
        return true;
    }

    private async Task RecordUseAsync(Guid applicationId, Guid secretId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var threshold = now - LastUseResolution;
        await dbContext.ExternalApplicationSecrets
            .Where(candidate => candidate.Id == secretId && (candidate.LastUsedAt == null || candidate.LastUsedAt < threshold))
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.LastUsedAt, now), cancellationToken);
        await dbContext.ExternalApplications
            .Where(candidate => candidate.Id == applicationId && (candidate.LastUsedAt == null || candidate.LastUsedAt < threshold))
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.LastUsedAt, now), cancellationToken);
    }
}
