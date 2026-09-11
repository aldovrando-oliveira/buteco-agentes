using System.Collections.Concurrent;
using Buteco.Api.Knowledge.Indexing;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Duplo do publisher de indexação, no molde de <see cref="FakeTaskJobPublisher"/>.
///
/// <para>
/// <b>Não é conveniência — sem ele a suíte inteira de conhecimento reprova.</b>
/// A partir da change knowledge-base-indexacao, **todo** `POST` e `PUT` de
/// documento publica na fila, e o fixture de `apps/api` não sobe RabbitMQ: o
/// publisher real tentaria abrir conexão e estouraria em cada teste que cria
/// documento. Foi o que aconteceu ao ligar a publicação — 12 testes que existiam
/// antes desta change passaram a reprovar, e nenhum deles tem a ver com
/// indexação.
/// </para>
///
/// <para>
/// Ele também é o que torna verificável o par da regra de `ContentHash`:
/// "conteúdo idêntico NÃO enfileira" é asserção sobre o que **não** foi
/// publicado, e sem registrar o publicado não há como afirmá-la.
/// </para>
/// </summary>
public sealed class FakeKnowledgeIndexingJobPublisher : IKnowledgeIndexingJobPublisher
{
    private readonly ConcurrentQueue<KnowledgeIndexingJobMessage> _published = new();

    public IReadOnlyCollection<KnowledgeIndexingJobMessage> Published => _published.ToArray();

    public IReadOnlyCollection<KnowledgeIndexingJobMessage> PublishedFor(Guid documentId) =>
        _published.Where(message => message.KnowledgeDocumentId == documentId).ToArray();

    public Task PublishAsync(KnowledgeIndexingJobMessage message, CancellationToken cancellationToken = default)
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishToWaitQueueAsync(KnowledgeIndexingJobMessage message, int attempt, CancellationToken cancellationToken = default)
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }
}
