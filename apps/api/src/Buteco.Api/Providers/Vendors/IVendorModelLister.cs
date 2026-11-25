namespace Buteco.Api.Providers.Vendors;

/// <summary>
/// A listagem de modelos de um fornecedor, registrada por DI keyed com o id do provedor de
/// <c>LlmProviders.All</c> (design.md da change modelos-curados-por-provedor, D4, D5 e a
/// checagem de boot de <see cref="VendorModelListerRegistrationExtensions"/>).
///
/// <para>
/// Cada implementação lê uma página e diz qual é a seguinte; quem percorre as páginas, aplica
/// o orçamento de 30 s e o teto de 10 páginas e traduz as falhas é o
/// <see cref="VendorModelCatalog"/>. A chave chega por parâmetro e vai SÓ em cabeçalho, posto
/// na própria requisição, nunca em URL nem no <c>DefaultRequestHeaders</c> do cliente.
/// </para>
///
/// <para>
/// Uma resposta não <c>200</c> volta em <see cref="VendorPage.NonSuccessStatus"/>; um
/// <c>200</c> fora do contrato lança <see cref="VendorContractException"/>. Falha de rede e
/// timeout propagam e são tratados pelo catálogo.
/// </para>
/// </summary>
public interface IVendorModelLister
{
    /// <summary>O critério que esta listagem aplica (<see cref="VendorModelFilters"/>).</summary>
    string Filter { get; }

    Task<VendorPage> FetchPageAsync(string apiKey, string? cursor, CancellationToken cancellationToken);
}
