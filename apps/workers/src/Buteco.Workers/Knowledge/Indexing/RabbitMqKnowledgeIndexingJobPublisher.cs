using System.Text.Json;
using Buteco.Workers.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Espelho de <c>RabbitMqTaskJobPublisher</c> para a fila de indexação. Aqui ele
/// só republica nas filas de espera: quem publica o pedido original é
/// <c>apps/api</c>, no handler que cria ou atualiza o documento.
/// </summary>
public sealed class RabbitMqKnowledgeIndexingJobPublisher : IKnowledgeIndexingJobPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _channelLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqKnowledgeIndexingJobPublisher(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
    }

    public async Task PublishToWaitQueueAsync(KnowledgeIndexingJobMessage message, int attempt, CancellationToken cancellationToken = default)
    {
        var channel = await GetChannelAsync(cancellationToken);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: KnowledgeIndexingQueues.WaitQueueForAttempt(attempt),
            mandatory: true,
            basicProperties: new BasicProperties { Persistent = true },
            body: JsonSerializer.SerializeToUtf8Bytes(message),
            cancellationToken: cancellationToken);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await _channelLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is { IsOpen: true })
            {
                return _channel;
            }

            if (_connection is not { IsOpen: true })
            {
                var factory = new ConnectionFactory
                {
                    HostName = _options.Host,
                    Port = _options.Port,
                    UserName = _options.Username,
                    Password = _options.Password,
                };

                _connection = await factory.CreateConnectionAsync(cancellationToken);
            }

            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await KnowledgeIndexingQueues.DeclareAsync(_channel, cancellationToken);
            return _channel;
        }
        finally
        {
            _channelLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _channelLock.Dispose();
    }
}
