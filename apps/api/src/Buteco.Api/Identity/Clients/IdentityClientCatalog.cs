using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace Buteco.Api.Identity.Clients;

/// <summary>Um passo do aquecimento (design.md D3.13); trocável por DI para o teste de falha.</summary>
public interface IIdentityClientCatalog
{
    Task SyncAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sincroniza o <c>buteco-painel</c>, os dois clientes de serviço e os três escopos com o
/// descritor do código (design.md da change usuarios-locais-login-emissor, D3.1, D3.2 e
/// D3.13; design.md da change clientes-servico-emissor, D2), no aquecimento e a cada tique
/// da rotação, nunca no boot:
/// <list type="bullet">
/// <item>cria o que falta e <b>corrige</b> qualquer divergência: um cliente adulterado para
/// confidencial, com segredo ou com <c>client_credentials</c>, volta ao descritor;</item>
/// <item>num cliente de serviço, o segredo configurado é conferido contra o hash gravado e,
/// se não validar, regravado. Toda correção dele leva o segredo no descritor: sem ele, o
/// OpenIddict recusa a atualização de um cliente confidencial (<c>ID2113</c>), a transação
/// volta e a sincronização inteira falha, a do painel junto;</item>
/// <item>um cliente <c>buteco-*</c> que o código não declara gera log de erro e fica como
/// está: apagar ou mexer em cliente que alguém cadastrou não é decisão daqui;</item>
/// <item>cada cliente e cada escopo isolado: a falha de um vira log de erro, e os outros
/// seguem no mesmo ciclo;</item>
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
    public static readonly EventId ItemFailedEvent = new(1195, "IdentityClientCatalogItemFailed");

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
        var failed = new List<string>();

        foreach (var name in IdentityClients.Scopes)
        {
            await SyncItemAsync(transaction, failed, $"escopo {name}", async () =>
            {
                var wanted = IdentityClients.Scope(name);
                var current = await scopes.FindByNameAsync(name, cancellationToken);
                if (current is null)
                {
                    await scopes.CreateAsync(wanted, cancellationToken);
                    changed.Add($"escopo {name} criado");
                    return;
                }

                var stored = new OpenIddictScopeDescriptor();
                await scopes.PopulateAsync(stored, current, cancellationToken);
                if (!stored.Resources.SetEquals(wanted.Resources) || stored.DisplayName != wanted.DisplayName)
                {
                    await scopes.UpdateAsync(current, wanted, cancellationToken);
                    changed.Add($"escopo {name} corrigido");
                }
            }, cancellationToken);
        }

        await SyncItemAsync(transaction, failed, $"cliente {IdentityClients.PanelClientId}", async () =>
        {
            var panel = IdentityClients.Panel(panelBase);
            var application = await applications.FindByClientIdAsync(IdentityClients.PanelClientId, cancellationToken);
            if (application is null)
            {
                await applications.CreateAsync(panel, cancellationToken);
                changed.Add($"cliente {IdentityClients.PanelClientId} criado");
            }
            else if (await DivergesAsync(application, panel, compareSecret: true, cancellationToken))
            {
                await applications.UpdateAsync(application, panel, cancellationToken);
                changed.Add($"cliente {IdentityClients.PanelClientId} corrigido");
            }
        }, cancellationToken);

        foreach (var service in ServiceClients())
        {
            await SyncItemAsync(transaction, failed, $"cliente {service.ClientId}", async () =>
            {
                var stored = await applications.FindByClientIdAsync(service.ClientId!, cancellationToken);
                if (stored is null)
                {
                    await applications.CreateAsync(service, cancellationToken);
                    changed.Add($"cliente {service.ClientId} criado");
                    return;
                }

                // Corrigir regrava o segredo junto; só sem divergência o segredo é conferido.
                if (await DivergesAsync(stored, service, compareSecret: false, cancellationToken))
                {
                    await applications.UpdateAsync(stored, service, cancellationToken);
                    changed.Add($"cliente {service.ClientId} corrigido");
                }
                else if (!await applications.ValidateClientSecretAsync(stored, service.ClientSecret!, cancellationToken))
                {
                    await applications.UpdateAsync(stored, service, cancellationToken);
                    changed.Add($"segredo do cliente {service.ClientId} trocado");
                }
            }, cancellationToken);
        }

        await foreach (var other in applications.ListAsync(count: null, offset: null, cancellationToken))
        {
            var clientId = await applications.GetClientIdAsync(other, cancellationToken);
            if (clientId is not null && clientId.StartsWith(IdentityClients.ReservedPrefix, StringComparison.Ordinal) &&
                !DeclaredClientIds.Contains(clientId))
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

        // Os itens que deram certo já foram gravados. A falha sobe depois deles para o
        // aquecimento tentar de novo no ciclo seguinte, e não só no próximo tique: sem isso, um
        // catálogo que falhou inteiro antes da migração contaria como concluído.
        if (failed.Count > 0)
        {
            throw new InvalidOperationException(
                $"Catálogo de clientes do emissor incompleto: {string.Join(", ", failed)} não sincronizou neste ciclo.");
        }
    }

    // Cada cliente e cada escopo com o próprio try/catch (convenção 4; design.md da change
    // clientes-servico-emissor, D2): um descritor que o OpenIddict recusa, ou uma falha do
    // banco num item, vira log de erro com o item, e os outros seguem no mesmo ciclo. O
    // savepoint desfaz só o item, para a transação continuar válida no Postgres, e o change
    // tracker é limpo porque o OpenIddict já aplicou o descritor na entidade antes de recusar:
    // sem isso, o próximo SaveChanges gravaria o item recusado. O item que falhou tenta de
    // novo: o SyncAsync lança no fim, depois de gravar os outros. A mensagem nomeia o item,
    // nunca o segredo.
    private async Task SyncItemAsync(
        IDbContextTransaction transaction,
        List<string> failed,
        string item,
        Func<Task> sync,
        CancellationToken cancellationToken)
    {
        const string savepoint = "catalog_item";
        await transaction.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            await sync();
            await transaction.ReleaseSavepointAsync(savepoint, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
            dbContext.ChangeTracker.Clear();
            failed.Add(item);
            logger.LogError(
                ItemFailedEvent,
                exception,
                "Catálogo de clientes do emissor: {Item} não sincronizou; os outros seguem, e ele tenta de novo no próximo ciclo.",
                item);
        }
    }

    private static readonly HashSet<string> DeclaredClientIds = new(StringComparer.Ordinal)
    {
        IdentityClients.PanelClientId,
        IdentityClients.InboxServiceClientId,
        IdentityClients.ConnectorsServiceClientId,
    };

    // O segredo de um cliente de serviço é conferido à parte, por ValidateClientSecretAsync:
    // o descritor gravado traz o hash, e o desejado, o texto. No painel, qualquer segredo
    // gravado é divergência, porque ele é público.
    private async Task<bool> DivergesAsync(
        object application,
        OpenIddictApplicationDescriptor wanted,
        bool compareSecret,
        CancellationToken cancellationToken)
    {
        var stored = new OpenIddictApplicationDescriptor();
        await applications.PopulateAsync(stored, application, cancellationToken);

        return stored.ClientType != wanted.ClientType ||
            (compareSecret && !string.IsNullOrEmpty(stored.ClientSecret)) ||
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

    private Uri PanelBase() => PanelBaseUrl.Resolve(options.Value, issuer);

    private IEnumerable<OpenIddictApplicationDescriptor> ServiceClients()
    {
        var secrets = options.Value.ServiceClients;
        yield return IdentityClients.Service(IdentityClients.InboxServiceClientId, "Serviço apps/inbox", secrets.Inbox.Secret!);
        yield return IdentityClients.Service(IdentityClients.ConnectorsServiceClientId, "Serviço apps/connectors", secrets.Connectors.Secret!);
    }
}
