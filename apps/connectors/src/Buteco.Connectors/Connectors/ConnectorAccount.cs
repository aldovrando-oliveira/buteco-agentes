namespace Buteco.Connectors.Connectors;

/// <summary>
/// A conta com que o provedor da chave <see cref="Key"/> acessa: o terceiro registro
/// obrigatório de cada conector, ao lado dos dois contratos keyed.
/// </summary>
/// <remarks>
/// Registrado como singleton <b>não keyed</b>, carregando a própria chave, e não como
/// serviço keyed (design.md, divergência da implementação): em tempo de execução o
/// contêiner não enumera as chaves registradas, e a rota de provedores precisa
/// listá-las. <c>IEnumerable&lt;ConnectorAccount&gt;</c> é essa lista.
/// </remarks>
public sealed record ConnectorAccount(string Key, string Email);
