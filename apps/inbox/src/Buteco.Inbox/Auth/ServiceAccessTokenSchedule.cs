namespace Buteco.Inbox.Auth;

// Duplicado deliberadamente de
// apps/connectors/src/Buteco.Connectors/Auth/ServiceAccessTokenSchedule.cs — sem libs/
// (design.md da change servicos-token-emissor, D3). Qualquer mudança aqui precisa ser
// espelhada lá, e vice-versa.

/// <summary>
/// Intervalos do token de serviço (design.md da change servicos-token-emissor, D3). Valores
/// de produção, sem opção de configuração (convenção 2); registrado no DI para os testes
/// trocarem por intervalos curtos, como o <see cref="IssuerKeysSchedule"/>.
/// </summary>
public sealed class ServiceAccessTokenSchedule
{
    /// <summary>
    /// O token deixa de servir quando falta isto para vencer. A mesma margem do painel (#217).
    /// </summary>
    public TimeSpan RenewalMargin { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Depois de um pedido que falhou, os pedidos seguintes falham na hora, sem HTTP, até
    /// passar este intervalo. O mesmo idioma do <see cref="IssuerKeysSchedule.FailedFetchBackoff"/>.
    /// </summary>
    public TimeSpan FailedRequestBackoff { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Prazo do pedido ao <c>/connect/token</c>.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
