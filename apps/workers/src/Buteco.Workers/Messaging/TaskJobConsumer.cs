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
    ILogger<TaskJobConsumer> logger) : BackgroundService
{
    public const string QueueName = "agent-tasks";

    private IConnection? _connection;
    private IChannel? _channel;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker starting at: {time}", DateTimeOffset.Now);
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
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao processar mensagem da fila {Queue}", QueueName);
                await _channel.BasicNackAsync(deliverEventArgs.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
        };

        await _channel.BasicConsumeAsync(QueueName, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker stopping at: {time}", DateTimeOffset.Now);

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
