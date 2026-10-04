using System.Collections.Concurrent;
using Buteco.Api.Knowledge.Indexing;
using RabbitMQ.Client.Exceptions;

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
///
/// <para>
/// <b>Os dois modos de falha</b> (change indexacao-sem-job-orfao, #138) reproduzem
/// o broker fora do ar sem RabbitMQ. <see cref="PublisherMode.Unavailable"/> lança
/// a mesma <see cref="BrokerUnreachableException"/> que o publisher real lança sem
/// broker, medida na #105. <see cref="PublisherMode.Blocking"/> não responde até o
/// cancelamento: é o broker num endereço que não devolve nada, o caso do limite de
/// despacho. Nos dois, a tentativa é contada em <see cref="Attempts"/> e nada vai
/// para <see cref="Published"/>. O modo é da fixture inteira: quem o troca o
/// devolve a <see cref="PublisherMode.Available"/> num <c>finally</c>.
/// </para>
/// </summary>
public sealed class FakeKnowledgeIndexingJobPublisher : IKnowledgeIndexingJobPublisher
{
    private readonly ConcurrentQueue<KnowledgeIndexingJobMessage> _published = new();
    private int _attempts;
    private volatile PublisherMode _mode = PublisherMode.Available;

    public IReadOnlyCollection<KnowledgeIndexingJobMessage> Published => _published.ToArray();

    public IReadOnlyCollection<KnowledgeIndexingJobMessage> PublishedFor(Guid documentId) =>
        _published.Where(message => message.KnowledgeDocumentId == documentId).ToArray();

    /// <summary>Toda chamada a <see cref="PublishAsync"/>, com sucesso ou não.</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>Atraso de cada publicação bem-sucedida, para alargar janelas de corrida.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public PublisherMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public async Task PublishAsync(KnowledgeIndexingJobMessage message, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _attempts);

        switch (_mode)
        {
            case PublisherMode.Unavailable:
                throw new BrokerUnreachableException(new InvalidOperationException("Broker indisponível (duplo de teste)."));

            case PublisherMode.Blocking:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                break;
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        _published.Enqueue(message);
    }

    public Task PublishToWaitQueueAsync(KnowledgeIndexingJobMessage message, int attempt, CancellationToken cancellationToken = default)
    {
        _published.Enqueue(message);
        return Task.CompletedTask;
    }

    public enum PublisherMode
    {
        Available,
        Unavailable,
        Blocking,
    }
}
