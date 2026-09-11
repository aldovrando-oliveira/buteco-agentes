using RabbitMQ.Client;

namespace Buteco.Workers.Knowledge.Indexing;

/// <summary>
/// Espelho de <c>Buteco.Api.Knowledge.Indexing.KnowledgeIndexingQueues</c>.
/// Os nomes e a topologia precisam bater exatamente — quem publica é
/// <c>apps/api</c>, quem consome é este app.
/// </summary>
public static class KnowledgeIndexingQueues
{
    public const string Main = "knowledge-indexing";
    public const string Wait60s = "knowledge-indexing-wait-60s";
    public const string Wait300s = "knowledge-indexing-wait-300s";

    public const int MaxAttempts = 3;

    public static string WaitQueueForAttempt(int attempt) => attempt switch
    {
        1 => Wait60s,
        _ => Wait300s,
    };

    /// <summary>
    /// Declara a topologia. Idempotente, e declarada pelos dois lados — qualquer
    /// um pode subir primeiro.
    ///
    /// <para>
    /// <b>TTL fixo por fila, nunca por mensagem.</b> RabbitMQ só expira mensagem
    /// quando ela chega à <b>cabeça</b> da fila: com TTL por mensagem numa fila
    /// única, uma de 300 s na frente segura uma de 60 s atrás. Bloqueio de
    /// cabeça de fila, silencioso — é a alternativa recusada em D3.
    /// </para>
    /// </summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.QueueDeclareAsync(
            Main, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);

        await DeclareWaitQueueAsync(channel, Wait60s, 60_000, cancellationToken);
        await DeclareWaitQueueAsync(channel, Wait300s, 300_000, cancellationToken);
    }

    private static Task DeclareWaitQueueAsync(IChannel channel, string name, int ttlMilliseconds, CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(
            name,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-message-ttl"] = ttlMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = Main,
            },
            cancellationToken: cancellationToken);
}
