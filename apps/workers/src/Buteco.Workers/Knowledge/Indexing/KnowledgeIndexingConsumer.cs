using System.Text.Json;
using Buteco.Workers.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Consome a fila de indexação. <b>Fila própria, nunca <c>agent-tasks</c></b>
/// (design.md, D2): aquele consumidor roda com <c>prefetchCount: 1</c>, e
/// <c>AgentDelegationConcurrencyTests</c> existe para provar que isso serializa
/// o consumo dentro de uma instância. Indexação de minutos ali não é "fila mais
/// lenta" — é a mesma classe de bloqueio, com execução de agente atrás.
/// </summary>
public sealed class KnowledgeIndexingConsumer(
    IOptions<RabbitMqOptions> options,
    KnowledgeIndexingService indexingService,
    IKnowledgeIndexingJobPublisher publisher,
    ILogger<KnowledgeIndexingConsumer> logger) : BackgroundService
{
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Value.Host,
            Port = options.Value.Port,
            UserName = options.Value.Username,
            Password = options.Value.Password,
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await KnowledgeIndexingQueues.DeclareAsync(_channel, stoppingToken);
        await _channel.BasicQosAsync(0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, deliverEventArgs) =>
        {
            // O try abre ANTES da desserialização e envolve a leitura do banco e
            // a chamada ao provedor — molde de TaskJobConsumer, convenção 4.
            // A cláusula já mordeu duas vezes nesta base, e aqui ela importa
            // ainda mais: a chamada ao provedor é justamente a que falha.
            try
            {
                var message = JsonSerializer.Deserialize<KnowledgeIndexingJobMessage>(deliverEventArgs.Body.Span);
                if (message is not null)
                {
                    var outcome = await indexingService.IndexAsync(message, stoppingToken);
                    await HandleOutcomeAsync(outcome, message, stoppingToken);
                }

                await _channel.BasicAckAsync(deliverEventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                // Rede de segurança do que escapou do serviço — inclusive a
                // falha ao GRAVAR o estado de falha (banco fora, por exemplo).
                //
                // Confirma a mensagem assim mesmo, e NÃO a devolve para a fila:
                // devolver criaria um laço que vai falhar de novo pelo mesmo
                // motivo. O pior caso é o documento ficar em Indexing até a
                // próxima atualização ou reindexação — visível na tela, e
                // preferível à mensagem em limbo.
                logger.LogError(ex, "Falha ao processar mensagem da fila {Queue}", KnowledgeIndexingQueues.Main);
                await _channel.BasicNackAsync(deliverEventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(KnowledgeIndexingQueues.Main, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>
    /// As <b>duas saídas</b> do caminho de falha, ramificadas onde dá para ver.
    /// Só <see cref="KnowledgeIndexingOutcome.RetryScheduled"/> republica; só
    /// <see cref="KnowledgeIndexingOutcome.Failed"/> significa que o documento
    /// ficou em <c>Failed</c>, e a tentativa já foi contada nos dois casos
    /// dentro do serviço.
    /// </summary>
    private async Task HandleOutcomeAsync(
        KnowledgeIndexingOutcome outcome, KnowledgeIndexingJobMessage message, CancellationToken cancellationToken)
    {
        switch (outcome)
        {
            case KnowledgeIndexingOutcome.RetryScheduled:
                await publisher.PublishToWaitQueueAsync(
                    message with { Attempt = message.Attempt + 1 }, message.Attempt, cancellationToken);
                logger.LogWarning(
                    "Indexação do documento {DocumentId} reagendada: tentativa {Next} de {Max}",
                    message.KnowledgeDocumentId, message.Attempt + 1, KnowledgeIndexingQueues.MaxAttempts);
                break;

            case KnowledgeIndexingOutcome.Failed:
                logger.LogError(
                    "Indexação do documento {DocumentId} esgotou as {Max} tentativas e terminou em Failed",
                    message.KnowledgeDocumentId, KnowledgeIndexingQueues.MaxAttempts);
                break;

            case KnowledgeIndexingOutcome.Discarded:
            case KnowledgeIndexingOutcome.Indexed:
            default:
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }
}
