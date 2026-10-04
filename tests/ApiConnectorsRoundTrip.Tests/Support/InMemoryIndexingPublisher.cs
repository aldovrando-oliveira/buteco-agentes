extern alias ApiAssembly;

using System.Collections.Concurrent;
using ApiIndexingJobMessage = ApiAssembly::Buteco.Api.Knowledge.Indexing.KnowledgeIndexingJobMessage;
using ApiIndexingPublisher = ApiAssembly::Buteco.Api.Knowledge.Indexing.IKnowledgeIndexingJobPublisher;

namespace ApiConnectorsRoundTrip.Tests.Support;

/// <summary>
/// Publicador de indexação em memória do <c>apps/api</c> da ida e volta (design.md da
/// change ciclo-de-sincronizacao, D11, corrigido na tarefa 1.2). A fixture não sobe
/// RabbitMQ, e o upsert publica depois de gravar: com o publicador real, o primeiro
/// upsert responde <c>500</c> com o documento gravado (#138). Cópia do
/// <c>FakeKnowledgeIndexingJobPublisher</c> dos testes do <c>apps/api</c>, sem referência
/// entre projetos de teste. Registrar o que foi publicado é o que permite afirmar que cada
/// documento criado ou alterado gerou uma publicação, e que o inalterado não gerou.
/// </summary>
public sealed class InMemoryIndexingPublisher : ApiIndexingPublisher
{
    private readonly ConcurrentQueue<ApiIndexingJobMessage> _published = new();

    public IReadOnlyCollection<ApiIndexingJobMessage> Published => _published.ToArray();

    public Task PublishAsync(ApiIndexingJobMessage message, CancellationToken cancellationToken = default)
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }

    public Task PublishToWaitQueueAsync(ApiIndexingJobMessage message, int attempt, CancellationToken cancellationToken = default)
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }
}
