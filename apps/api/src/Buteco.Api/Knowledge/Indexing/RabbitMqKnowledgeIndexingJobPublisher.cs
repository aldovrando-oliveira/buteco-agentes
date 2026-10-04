using System.Text.Json;
using Buteco.Api.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// Publisher de pedidos de indexação, no molde de
/// <c>RabbitMqTaskJobPublisher</c>: canal preguiçoso protegido por
/// <see cref="SemaphoreSlim"/>, mensagem persistente e <c>mandatory</c>.
///
/// <para>
/// <b>Com confirmação do broker</b> (design.md da change indexacao-sem-job-orfao,
/// D5), e é nisso que deixa de seguir o molde. O despacho apaga o pedido de
/// indexação depois de publicar; sem confirmação, o retorno de
/// <c>BasicPublishAsync</c> só diria que a mensagem foi escrita no socket, e o
/// pedido seria apagado por uma mensagem que o broker pode não ter recebido.
/// Conferido no 7.2.1 decompilado: com as duas opções, a chamada espera o
/// <c>ack</c> e lança <c>PublishException</c> em <c>nack</c> e em
/// <c>basic.return</c> — este último só correlacionado com o <i>tracking</i>
/// ligado, por isso as duas.
/// </para>
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

    public Task PublishAsync(KnowledgeIndexingJobMessage message, CancellationToken cancellationToken = default) =>
        PublishToAsync(KnowledgeIndexingQueues.Main, message, cancellationToken);

    public Task PublishToWaitQueueAsync(KnowledgeIndexingJobMessage message, int attempt, CancellationToken cancellationToken = default) =>
        PublishToAsync(KnowledgeIndexingQueues.WaitQueueForAttempt(attempt), message, cancellationToken);

    private async Task PublishToAsync(string queue, KnowledgeIndexingJobMessage message, CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(message);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: true,
            basicProperties: new BasicProperties { Persistent = true },
            body: body,
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

            _channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);
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
