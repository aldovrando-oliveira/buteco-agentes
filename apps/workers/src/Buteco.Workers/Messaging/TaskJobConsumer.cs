using System.Text.Json;
using Buteco.Workers.Agents;
using Buteco.Workers.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Buteco.Workers.Messaging;

public sealed class TaskJobConsumer(
    IOptions<RabbitMqOptions> options,
    AgentExecutionService executionService,
    TimeProvider timeProvider,
    ILogger<TaskJobConsumer> logger) : BackgroundService
{
    public const string QueueName = "agent-tasks";

    // Prazo PRÓPRIO do cancelamento do consumidor na parada, e não o token de
    // StopAsync: aquele é o ShutdownTimeout do host (30 s por padrão), maior que
    // os 10 s do stop_grace_period padrão do Compose. Preso a ele, um cancel-ok
    // lento seguraria o base.StopAsync e a execução voltaria a ser cancelada
    // tarde — a #49 por outro caminho. Com o broker saudável o cancel-ok é uma
    // ida e volta de milissegundos (design.md da change
    // workers-parada-com-execucao-em-voo, D5).
    private static readonly TimeSpan ConsumerCancelTimeout = TimeSpan.FromSeconds(2);

    private IConnection? _connection;
    private IChannel? _channel;
    private string? _consumerTag;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker starting at: {time}", timeProvider.GetLocalNow());
        return base.StartAsync(cancellationToken);
    }

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
        await _channel.QueueDeclareAsync(QueueName, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, deliverEventArgs) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize<TaskJobMessage>(deliverEventArgs.Body.Span);
                if (message is not null)
                {
                    await executionService.ExecuteAsync(message, stoppingToken);
                }

                await _channel.BasicAckAsync(deliverEventArgs.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Execução interrompida pela PARADA, não falha (design.md da change
                // workers-parada-com-execucao-em-voo, D2): a task ficou em `working`
                // e o job volta à fila para outro worker. `requeue: true` e
                // CancellationToken.None são o ponto — antes desta change o `nack`
                // de baixo recebia o token cancelado, lançava, e a mensagem voltava
                // à fila só POR ACIDENTE; se não lançasse, `requeue: false` a teria
                // descartado com a task presa em `working`. Sem laço de reentrega
                // porque StopAsync já cancelou este consumidor no broker (D5).
                logger.LogInformation(
                    "Execução da task interrompida pela parada do worker; job devolvido à fila {Queue}",
                    QueueName);
                await _channel.BasicNackAsync(deliverEventArgs.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar mensagem da fila {Queue}", QueueName);
                await _channel.BasicNackAsync(deliverEventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        _consumerTag = await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    /// <summary>
    /// Para de consumir, cancela a execução em voo, e SÓ ENTÃO fecha canal e
    /// conexão — a ordem é o conteúdo deste método (#49; design.md da change
    /// workers-parada-com-execucao-em-voo, D1 e D5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por que cancelar antes de fechar.</b> No RabbitMQ.Client 7.2.1,
    /// <c>Channel.CloseAsync</c> espera o callback do consumidor terminar
    /// (<c>ConsumerDispatcher.WaitForShutdownAsync</c>), e quem cancela o
    /// <c>stoppingToken</c> que o callback observa é <c>base.StopAsync</c>. Na
    /// ordem antiga (fechar, depois cancelar) o fechamento esperava o callback até
    /// o ShutdownTimeout do host (30 s), a conexão esperava mais 30 s
    /// (<c>DefaultConnectionCloseTimeout</c>) e lançava, <c>base.StopAsync</c>
    /// nem rodava, e a execução só era cancelada no Dispose do host — com o
    /// IServiceProvider já descartado. Medido: 60,1 s. Agora o fechamento do canal
    /// continua esperando o callback, mas o callback já foi cancelado, e a escrita
    /// final acontece com o host vivo.
    /// </para>
    /// <para>
    /// <b>Por que cancelar o consumidor no broker primeiro.</b> A execução
    /// interrompida devolve o job com <c>nack(requeue: true)</c>; com o consumidor
    /// ainda registrado e <c>prefetchCount: 1</c>, o broker o entrega de novo a
    /// ESTE consumidor, que já está cancelado, e assim por diante até o
    /// <c>Quiesce</c> do fechamento. É corrida: passou 10 de 10 rodadas sem
    /// espera, e deu 649 voltas em 2 s com a janela aberta. Depois do
    /// <c>cancel-ok</c> o broker não entrega nada aqui, e o job vai para outro
    /// worker. Falha ou estouro do prazo é aviso, e a parada SEGUE: se impedisse o
    /// <c>base.StopAsync</c>, seria o defeito de volta.
    /// </para>
    /// </remarks>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker stopping at: {time}", timeProvider.GetLocalNow());

        if (_channel is not null && _consumerTag is not null)
        {
            try
            {
                using var timeout = new CancellationTokenSource(ConsumerCancelTimeout);
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: timeout.Token);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao cancelar o consumidor da fila {Queue} na parada — a parada segue", QueueName);
            }
        }

        await base.StopAsync(cancellationToken);

        if (_channel is not null)
        {
            await _channel.CloseAsync(cancellationToken);
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync(cancellationToken);
        }
    }
}
