namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Republica pedidos de indexação nas filas de espera da política de tentativas.
/// Interface separada da implementação para que os testes possam observar o
/// reagendamento sem broker.
/// </summary>
public interface IKnowledgeIndexingJobPublisher
{
    Task PublishToWaitQueueAsync(KnowledgeIndexingJobMessage message, int attempt, CancellationToken cancellationToken = default);
}
