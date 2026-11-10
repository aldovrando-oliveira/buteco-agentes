namespace Buteco.Api.ExternalApplications;

/// <summary>
/// O vínculo de uma aplicação externa com um agente, com as operações liberadas para ela nele (design.md
/// da change agentes-por-aplicacao, D1 e D5; spec <c>external-application-agents</c>). No máximo um por
/// par (aplicação, agente), sempre com ao menos uma operação: a lista vazia é recusada pela rota e pelo
/// <c>CHECK</c> do banco. A aplicação nasce sem nenhum.
///
/// <para>
/// O vínculo sobrevive a desativar a aplicação e a desativar o agente: quem corta a chamada é o filtro
/// da rota A2A (<see cref="Buteco.Api.A2A.ApplicationA2AFilter"/>), que lê o estado da aplicação a cada
/// requisição, e o <c>EnqueueingAgentHandler</c>, que rejeita a task do agente inativo.
/// </para>
/// </summary>
public sealed class ExternalApplicationAgent
{
    public Guid ApplicationId { get; private set; }

    public Guid AgentId { get; private set; }

    /// <summary>Os valores de <see cref="ExternalApplicationOperations"/>, sem repetição, na ordem da tabela.</summary>
    public List<string> Operations { get; private set; } = [];

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    private ExternalApplicationAgent()
    {
    }

    public static ExternalApplicationAgent Create(Guid applicationId, Guid agentId, IReadOnlyList<string> operations, DateTimeOffset now) => new()
    {
        ApplicationId = applicationId,
        AgentId = agentId,
        Operations = [.. operations],
        GrantedAt = now,
        UpdatedAt = now,
    };

    public void Replace(IReadOnlyList<string> operations, DateTimeOffset now)
    {
        Operations = [.. operations];
        UpdatedAt = now;
    }

    public bool Allows(string operation) => Operations.Contains(operation);
}

/// <summary>
/// As três operações liberáveis (D1). <b>Não há cancelamento</b>: o <c>CancelTask</c> de hoje grava
/// <c>canceled</c> e o <c>apps/workers</c> o sobrescreve quando a task já está em <c>working</c>
/// (decisão do dono de 09/11/2026, Q1; o cancelamento real é a #264). Para a aplicação, ele cai no
/// <c>403</c> do método fora das operações, como <c>ListTasks</c>.
/// </summary>
public static class ExternalApplicationOperations
{
    public const string SendMessage = "enviar-mensagem";

    public const string GetTask = "consultar-task";

    public const string PushNotification = "notificacao-push";

    /// <summary>A ordem da tabela da spec, que é a ordem em que a lista é gravada e devolvida.</summary>
    public static IReadOnlyList<string> All { get; } = [SendMessage, GetTask, PushNotification];

    public const string EmptyMessage = "Marque ao menos uma operação.";

    public const string AgentNotFoundMessage = "Agente não encontrado.";

    public const string AlreadyGrantedMessage = "Este agente já está liberado para a aplicação.";

    public const string NotGrantedMessage = "Agente não liberado para esta aplicação.";

    public static string UnknownMessage(string value) => $"Operação desconhecida: {value}.";

    /// <summary>
    /// A lista recebida, sem repetição e na ordem da tabela, ou o erro do campo <c>operacoes</c>.
    /// </summary>
    public static Dictionary<string, string[]>? Validate(IReadOnlyList<string?>? raw, out IReadOnlyList<string> operations)
    {
        operations = [];
        if (raw is null || raw.Count == 0)
        {
            return new Dictionary<string, string[]> { ["operacoes"] = [EmptyMessage] };
        }

        foreach (var value in raw)
        {
            if (value is null || !All.Contains(value))
            {
                return new Dictionary<string, string[]> { ["operacoes"] = [UnknownMessage(value ?? "")] };
            }
        }

        operations = All.Where(raw.Contains).ToList();
        return null;
    }
}
