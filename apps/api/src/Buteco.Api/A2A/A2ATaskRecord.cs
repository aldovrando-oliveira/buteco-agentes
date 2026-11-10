namespace Buteco.Api.A2A;

public class A2ATaskRecord
{
    public string TaskId { get; private set; } = null!;

    public Guid AgentId { get; private set; }

    public string ContextId { get; private set; } = null!;

    public string State { get; private set; } = null!;

    public DateTimeOffset? StatusTimestamp { get; private set; }

    public string Payload { get; private set; } = null!;

    /// <summary>
    /// A aplicação externa que criou a task, ou nulo para o que é do Buteco (design.md da change
    /// agentes-por-aplicacao, D3). Gravado só aqui, na criação: <see cref="Update"/> não o recebe, e nenhuma
    /// transição muda o dono.
    /// </summary>
    public Guid? ApplicationId { get; private set; }

    private A2ATaskRecord()
    {
    }

    public A2ATaskRecord(string taskId, Guid agentId, string contextId, string state, DateTimeOffset? statusTimestamp, string payload, Guid? applicationId = null)
    {
        ApplicationId = applicationId;
        TaskId = taskId;
        AgentId = agentId;
        ContextId = contextId;
        State = state;
        StatusTimestamp = statusTimestamp;
        Payload = payload;
    }

    public void Update(string contextId, string state, DateTimeOffset? statusTimestamp, string payload)
    {
        ContextId = contextId;
        State = state;
        StatusTimestamp = statusTimestamp;
        Payload = payload;
    }
}
