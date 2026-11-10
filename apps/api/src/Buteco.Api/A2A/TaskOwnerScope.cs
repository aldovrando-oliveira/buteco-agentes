namespace Buteco.Api.A2A;

/// <summary>
/// O dono da task que está sendo criada (design.md da change agentes-por-aplicacao, D3; spec
/// <c>a2a-task-lifecycle</c>, "Cada task guarda a aplicação que a criou"). O
/// <see cref="RoutingA2ARequestHandler"/> o põe antes de repassar o <c>SendMessage</c> de uma aplicação ao
/// <c>A2AServer</c>, e o <see cref="PostgresTaskStore"/> o lê na <b>inserção</b> da linha.
///
/// <para>
/// <b>Valor assíncrono local, e não parâmetro</b>: o store é o <c>ITaskStore</c> do SDK, chamado por ele com
/// uma assinatura fixa, e é um por agente, compartilhado entre chamadores. O valor atravessa os
/// <c>await</c> e o <c>Task.Run</c> em que o <c>A2AServer</c> roda o handler, porque o contexto de execução
/// é capturado. Medido no <c>A2AServer</c> do <c>A2A</c> <c>1.0.0-preview2</c> (0.2 do tasks.md): a primeira
/// <c>SaveTaskAsync</c> da task nova acontece no fluxo do próprio <c>SendMessageAsync</c>, e é ela que
/// insere a linha. O guarda é o caso "Dono gravado na criação".
/// </para>
/// </summary>
public static class TaskOwnerScope
{
    private static readonly AsyncLocal<Guid?> Owner = new();

    public static Guid? Current => Owner.Value;

    /// <summary>Põe o dono até o <see cref="IDisposable.Dispose"/>, que devolve o anterior.</summary>
    public static IDisposable Begin(Guid? applicationId)
    {
        var previous = Owner.Value;
        Owner.Value = applicationId;
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => Owner.Value = previous;
    }
}
