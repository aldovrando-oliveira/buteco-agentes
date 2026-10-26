namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/IssuerKeysSchedule.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

/// <summary>
/// Intervalos do ciclo de vida das chaves do emissor (change
/// validacao-jwks-inbox-connectors, D4). Valores de produção, sem opção de
/// configuração; registrado no DI para os testes trocarem por intervalos curtos.
/// </summary>
public sealed class IssuerKeysSchedule
{
    /// <summary>Tentativa em segundo plano até a primeira obtenção das chaves.</summary>
    public TimeSpan FirstFetchRetryInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Tique depois de obtidas: chama <c>GetConfigurationAsync</c>, que dispara a
    /// renovação quando o cache passou do <see cref="AutomaticRefreshInterval"/>. Sem
    /// ele, a renovação dependeria de tráfego.
    /// </summary>
    public TimeSpan RefreshTickInterval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Idade a partir da qual o <c>ConfigurationManager</c> renova as chaves (mínimo da
    /// biblioteca: 5 min). Com o tique e o jitter de até 5%, o cache fica com no máximo
    /// cerca de 1h15.
    /// </summary>
    public TimeSpan AutomaticRefreshInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Intervalo mínimo entre buscas disparadas por <c>kid</c> desconhecido
    /// (<c>RefreshInterval</c> do <c>ConfigurationManager</c>; mínimo da biblioteca: 1 s).
    /// </summary>
    public TimeSpan UnknownKidRefreshInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Depois de uma busca que falhou, as buscas seguintes falham na hora, sem HTTP,
    /// até passar este intervalo. É também o <c>Retry-After</c> do <c>503</c>.
    /// </summary>
    public TimeSpan FailedFetchBackoff { get; init; } = TimeSpan.FromSeconds(10);
}
