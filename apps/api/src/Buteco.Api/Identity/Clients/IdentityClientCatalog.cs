using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Buteco.Api.Identity.Clients;

/// <summary>Um passo do aquecimento (design.md D3.13); trocável por DI para o teste de falha.</summary>
public interface IIdentityClientCatalog
{
    Task SyncAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sincroniza o <c>buteco-painel</c> e os três escopos com o descritor do código
/// (design.md da change usuarios-locais-login-emissor, D3.1, D3.2 e D3.13), no aquecimento
/// e a cada tique da rotação, nunca no boot:
/// <list type="bullet">
/// <item>cria o que falta e <b>corrige</b> qualquer divergência: um cliente adulterado para
/// confidencial, com segredo ou com <c>client_credentials</c>, volta ao descritor;</item>
/// <item>um cliente <c>buteco-*</c> que o código não declara gera log de erro e fica como
/// está: apagar ou mexer em cliente que alguém cadastrou não é decisão daqui;</item>
/// <item>sob o <c>pg_advisory_xact_lock</c> <see cref="AdvisoryLockKey"/>: duas instâncias
/// não criam o cliente duas vezes.</item>
/// </list>
/// </summary>
public sealed class IdentityClientCatalog(
    AppDbContext dbContext,
    IOpenIddictApplicationManager applications,
    IOpenIddictScopeManager scopes,
    IdentityIssuerState issuer,
    IOptions<IdentityIssuerOptions> options,
    ILogger<IdentityClientCatalog> logger) : IIdentityClientCatalog
{
    public static readonly EventId SyncedEvent = new(1191, "IdentityClientCatalogSynced");
    public static readonly EventId UndeclaredClientEvent = new(1192, "IdentityClientUndeclared");

    /// <summary>
    /// Chave do <c>pg_advisory_xact_lock</c> da sincronização: os bytes ASCII de
    /// <c>BUTECOC1</c> lidos como inteiro big-endian, na forma <c>bigint</c>, pelo mesmo
    /// motivo da <c>IdentityKeyRotationPolicy.AdvisoryLockKey</c> (<c>BUTECOK1</c>).
    /// </summary>
    public const long AdvisoryLockKey = 4779819235982394161;

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var panelBase = PanelBase();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", cancellationToken);

        var changed = new List<string>();

        foreach (var name in IdentityClients.Scopes)
        {
            var wanted = IdentityClients.Scope(name);
            var current = await scopes.FindByNameAsync(name, cancellationToken);
            if (current is null)
            {
                await scopes.CreateAsync(wanted, cancellationToken);
                changed.Add($"escopo {name} criado");
                continue;
            }

            var stored = new OpenIddictScopeDescriptor();
            await scopes.PopulateAsync(stored, current, cancellationToken);
            if (!stored.Resources.SetEquals(wanted.Resources) || stored.DisplayName != wanted.DisplayName)
            {
                await scopes.UpdateAsync(current, wanted, cancellationToken);
                changed.Add($"escopo {name} corrigido");
            }
        }

        var panel = IdentityClients.Panel(panelBase);
        var application = await applications.FindByClientIdAsync(IdentityClients.PanelClientId, cancellationToken);
        if (application is null)
        {
            await applications.CreateAsync(panel, cancellationToken);
            changed.Add($"cliente {IdentityClients.PanelClientId} criado");
        }
        else if (await DivergesAsync(application, panel, cancellationToken))
        {
            await applications.UpdateAsync(application, panel, cancellationToken);
            changed.Add($"cliente {IdentityClients.PanelClientId} corrigido");
        }

        await foreach (var other in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            var clientId = await applications.GetClientIdAsync(other, cancellationToken);
            if (clientId is not null && clientId.StartsWith(IdentityClients.ReservedPrefix, StringComparison.Ordinal) &&
                clientId != IdentityClients.PanelClientId)
            {
                logger.LogError(
                    UndeclaredClientEvent,
                    "Cliente {ClientId} usa o prefixo reservado buteco- e não é declarado pelo apps/api; ficou como estava.",
                    clientId);
            }
        }

        await transaction.CommitAsync(cancellationToken);

        if (changed.Count > 0)
        {
            logger.LogInformation(SyncedEvent, "Catálogo de clientes do emissor sincronizado: {Changes}.", string.Join(", ", changed));
        }
    }

    private async Task<bool> DivergesAsync(object application, OpenIddictApplicationDescriptor wanted, CancellationToken cancellationToken)
    {
        var stored = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(stored, application, cancellationToken);

        return stored.ClientType != wanted.ClientType ||
            !string.IsNullOrEmpty(stored.ClientSecret) ||
            stored.ConsentType != wanted.ConsentType ||
            stored.DisplayName != wanted.DisplayName ||
            stored.ApplicationType != wanted.ApplicationType ||
            stored.JsonWebKeySet is not null ||
            stored.Settings.Count > 0 ||
            !stored.Permissions.SetEquals(wanted.Permissions) ||
            !stored.Requirements.SetEquals(wanted.Requirements) ||
            !stored.RedirectUris.SetEquals(wanted.RedirectUris) ||
            !stored.PostLogoutRedirectUris.SetEquals(wanted.PostLogoutRedirectUris);
    }

    private Uri PanelBase() =>
        Uri.TryCreate(options.Value.PanelBaseUrl, UriKind.Absolute, out var panel)
            ? panel
            : issuer.Issuer ?? throw new InvalidOperationException("Emissor sem url pública: o catálogo de clientes não tem base.");
}
