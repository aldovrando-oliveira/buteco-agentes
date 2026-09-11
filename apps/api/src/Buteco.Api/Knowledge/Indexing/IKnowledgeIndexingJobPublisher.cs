namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// Publica pedidos de indexação. Interface separada da implementação RabbitMQ
/// pelo mesmo motivo de <c>ITaskJobPublisher</c>: os testes de handler
/// substituem por um duplo que registra o publicado, sem broker.
/// </summary>
public interface IKnowledgeIndexingJobPublisher
{
    Task PublishAsync(KnowledgeIndexingJobMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Republica numa fila de espera, para que a mensagem volte à fila
    /// principal depois do TTL daquela fila. Usado pela política de tentativas
    /// (design.md, D3).
    /// </summary>
    Task PublishToWaitQueueAsync(KnowledgeIndexingJobMessage message, int attempt, CancellationToken cancellationToken = default);
}
