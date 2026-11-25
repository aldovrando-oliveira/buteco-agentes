using Buteco.ProviderCatalog;

namespace Buteco.Api.Providers.Vendors;

/// <summary>
/// A consulta à listagem de modelos do fornecedor, na hora e sem cache (design.md da change
/// modelos-curados-por-provedor, D4). Percorre as páginas do lister keyed do provedor, ordena
/// por <c>id</c> ordinal e traduz as falhas.
///
/// <para>
/// <b>Só as rotas de opções e de habilitar chamam isto.</b> Nunca o boot, <c>GET /providers</c>,
/// <c>GET /providers/models</c>, a retirada, a validação de agente, o <c>SendMessage</c> nem a
/// delegação (convenção 4).
/// </para>
///
/// <para>
/// <b>Orçamento:</b> 30 s para a listagem inteira, abaixo dos 60 s de
/// <c>proxy_read_timeout</c> do nginx do painel; cada página tem 15 s no
/// <c>HttpClient</c>; no máximo <see cref="MaxPages"/> páginas. Só o cancelamento do próprio
/// chamador propaga.
/// </para>
///
/// <para>
/// <b>A chave nunca sai daqui:</b> vai para o lister, que a põe em cabeçalho. O log de falha
/// nomeia provedor, status e código, e nada da resposta do fornecedor (D5, convenção 3).
/// </para>
/// </summary>
public sealed class VendorModelCatalog(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<VendorModelCatalog> logger)
{
    public const int MaxPages = 10;

    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A listagem do <paramref name="provider"/>, que precisa estar em <c>LlmProviders.All</c>
    /// e configurado (quem chama confere antes, para responder <c>404</c> e <c>409</c>).
    /// </summary>
    public async Task<VendorModelListing> ListAsync(string provider, CancellationToken cancellationToken)
    {
        var definition = LlmProviders.All.Single(p => p.Id == provider);
        var apiKey = configuration[$"{definition.ConfigSectionName}:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"Provedor '{provider}' sem chave: quem chama confere antes.");
        }

        var lister = services.GetRequiredKeyedService<IVendorModelLister>(provider);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);

        VendorModelListing result;
        try
        {
            result = await FetchAllAsync(lister, apiKey, budget.Token);
        }
        catch (HttpRequestException)
        {
            result = Failure(lister, VendorFailureKind.Unavailable, vendorStatus: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout da página (TaskCanceledException do HttpClient) ou orçamento estourado, e
            // não cancelamento do chamador: o .NET representa os dois do mesmo jeito, e só o
            // segundo deve propagar (mesmo tratamento de ConnectorsFolderClient).
            result = Failure(lister, VendorFailureKind.Unavailable, vendorStatus: null);
        }
        catch (VendorContractException)
        {
            result = Failure(lister, VendorFailureKind.Error, vendorStatus: null);
        }

        if (result.Failure is { } failure)
        {
            logger.LogWarning(
                "Listagem de modelos do fornecedor recusada: provedor {Provider}, falha {Failure}, status do fornecedor {VendorStatus}.",
                provider,
                failure.Kind,
                failure.VendorStatus);
        }

        return result;
    }

    private static async Task<VendorModelListing> FetchAllAsync(IVendorModelLister lister, string apiKey, CancellationToken cancellationToken)
    {
        var models = new List<VendorModel>();
        string? cursor = null;

        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await lister.FetchPageAsync(apiKey, cursor, cancellationToken);
            if (result.NonSuccessStatus is { } status)
            {
                return Failure(lister, VendorFailureKind.Error, status);
            }

            models.AddRange(result.Models);
            if (result.NextCursor is null)
            {
                var ordered = models
                    .DistinctBy(model => model.Id, StringComparer.Ordinal)
                    .OrderBy(model => model.Id, StringComparer.Ordinal)
                    .ToList();

                return new VendorModelListing(lister.Filter, ordered, Failure: null);
            }

            cursor = result.NextCursor;
        }

        throw new VendorContractException($"mais de {MaxPages} páginas");
    }

    private static VendorModelListing Failure(IVendorModelLister lister, VendorFailureKind kind, int? vendorStatus) =>
        new(lister.Filter, [], new VendorListingFailure(kind, vendorStatus));
}
