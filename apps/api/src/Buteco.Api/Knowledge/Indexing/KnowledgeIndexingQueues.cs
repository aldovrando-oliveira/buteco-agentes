using RabbitMQ.Client;

namespace Buteco.Api.Knowledge.Indexing;

/// <summary>
/// Nomes e topologia das filas de indexação. Compartilhado entre o publisher de
/// <c>apps/api</c> e o consumidor de <c>apps/workers</c>, por cópia — os dois
/// apps não referenciam um ao outro, mesma disciplina dos dois
/// <c>AppDbContext</c>.
///
/// <para>
/// <b>Fila própria, não <c>agent-tasks</c></b> (design.md, D2). O consumidor de
/// tarefas tem um número limitado de slots de execução por instância
/// (<c>TaskConsumer:MaxConcurrentExecutions</c> de apps/workers, change
/// workers-consumo-concorrente), e uma delegação que espera a task do alvo segura um
/// deles. Indexação de minutos ali não é "fila mais lenta": ocupa slots de execução
/// de agente, e é a mesma classe de bloqueio, com a conversa atrás.
/// </para>
/// </summary>
public static class KnowledgeIndexingQueues
{
    public const string Main = "knowledge-indexing";

    /// <summary>
    /// Filas de espera da política de tentativas. Cada uma tem
    /// <c>x-message-ttl</c> <b>fixo</b> e <c>x-dead-letter-exchange</c> vazio com
    /// <c>x-dead-letter-routing-key</c> apontando de volta para
    /// <see cref="Main"/>: a mensagem expira e é reentregue na principal.
    /// </summary>
    public const string Wait60s = "knowledge-indexing-wait-60s";

    /// <inheritdoc cref="Wait60s"/>
    public const string Wait300s = "knowledge-indexing-wait-300s";

    /// <summary>
    /// Número máximo de execuções por revisão de conteúdo. Uma execução é uma
    /// tentativa — não uma chamada HTTP (design.md, D3).
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// Fila de espera para a próxima execução depois da tentativa
    /// <paramref name="attempt"/>. Espaçamento de 1 minuto e 5 minutos.
    /// </summary>
    public static string WaitQueueForAttempt(int attempt) => attempt switch
    {
        1 => Wait60s,
        _ => Wait300s,
    };

    /// <summary>
    /// Declara a topologia inteira. Idempotente, e declarada tanto por quem
    /// publica quanto por quem consome — qualquer um dos dois pode subir
    /// primeiro.
    ///
    /// <para>
    /// <b>TTL fixo por fila, nunca TTL por mensagem.</b> RabbitMQ só expira
    /// mensagem quando ela chega à <b>cabeça</b> da fila: numa fila única com
    /// TTL por mensagem, uma mensagem de 300 s na frente segura uma de 60 s
    /// atrás até a primeira expirar. É bloqueio de cabeça de fila, conhecido e
    /// silencioso, e é a alternativa recusada em D3. Duas filas de TTL fixo
    /// custam uma declaração a mais e não têm o problema.
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
