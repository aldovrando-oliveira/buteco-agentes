using System.Diagnostics;
using Buteco.Workers.ExecutionMetrics;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.EmbeddingMetrics;

/// <summary>
/// O que uma chamada ao gateway de embedding deixou medido. Existe como tipo
/// próprio para que <see cref="MeasuredEmbeddingGenerator"/> não precise saber
/// de qual das duas finalidades é a chamada nem qual é o pai da linha — quem
/// sabe disso é o sítio de chamada, que monta a
/// <c>EmbeddingCall</c> a partir daqui.
/// </summary>
/// <param name="InputCount">Quantas entradas foram nesta chamada.</param>
/// <param name="DurationMs">Duração da chamada, medida do mesmo jeito que <c>LlmCallDurationChatClient</c> mede a de chat.</param>
/// <param name="InputTokens">Nulo = o provedor não reportou. <b>Nunca</b> normalizado para zero (convenção 13).</param>
/// <param name="Failed">A chamada lançou.</param>
/// <param name="HttpStatus">Só quando o SDK o expõe tipado; nulo = não se sabe.</param>
public readonly record struct EmbeddingCallMeasurement(
    int InputCount,
    double DurationMs,
    long? InputTokens,
    bool Failed,
    int? HttpStatus);

/// <summary>
/// Embrulho fino sobre o <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> do
/// provedor: cronometra a chamada, lê o uso reportado, captura o status HTTP
/// quando tipado e <b>relança</b>, entregando a medida ao <paramref name="record"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aplicado no SÍTIO DE CHAMADA, e NÃO dentro de
/// <c>EmbeddingGeneratorResolver.Resolve()</c></b> (design.md, D4), por um motivo
/// de verificação: os duplos dos testes substituem o <b>resolvedor</b>
/// (<c>StubEmbeddingResolver</c>, <c>FakeEmbeddingGeneratorResolver</c>).
/// Embrulhar dentro do resolvedor faria todo cenário de teste passar por fora da
/// medição, e os guardas ficariam verdes sem nada medido.
/// </para>
///
/// <para>
/// <b>Um braço só cobre os dois caminhos.</b> <c>GenerateVectorAsync</c>, que a
/// busca usa, é <b>método de extensão</b> sobre a interface e desemboca em
/// <see cref="GenerateAsync"/> com uma entrada só — diferente do
/// <c>IChatClient</c> da etapa 1, cujos dois caminhos são membros da interface e
/// precisaram ser instrumentados separadamente (D9 de lá).
/// </para>
///
/// <para>
/// <b><see cref="Dispose"/> é no-op</b>, pelo mesmo motivo que
/// <c>CompactionCallChatClient</c> não deriva de <c>DelegatingChatClient</c>: o
/// inner é o cliente que o resolvedor construiu sobre o transporte estático de
/// <c>HttpClientPipelineTransport.Shared</c>, e descarte em cascata é o
/// vazamento que <c>fix-vazamento-httpclient-chat</c> corrigiu.
/// </para>
///
/// <para>
/// <b>Medir nunca derruba quem mede</b> (convenção 4): a exceção da chamada é
/// registrada e <b>relançada</b> sem alteração, e o <paramref name="record"/> é
/// acumulação em memória — não vai ao banco aqui.
/// </para>
/// </remarks>
public sealed class MeasuredEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner,
    Action<EmbeddingCallMeasurement> record) : IEmbeddingGenerator<string, Embedding<float>>
{
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Materializado antes da chamada: `values` é IEnumerable, e contá-lo
        // depois poderia enumerar uma segunda vez — ou nenhuma, se o inner já o
        // tiver consumido.
        var inputs = values as IList<string> ?? [.. values];
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var generated = await inner.GenerateAsync(inputs, options, cancellationToken);
            stopwatch.Stop();

            // Nulo NÃO é normalizado para zero: `Usage` ausente e
            // `InputTokenCount` ausente são os dois "o provedor não reportou", e
            // zero só entra quando o provedor reportar zero (convenção 13).
            record(new EmbeddingCallMeasurement(
                inputs.Count,
                stopwatch.Elapsed.TotalMilliseconds,
                generated.Usage?.InputTokenCount,
                Failed: false,
                HttpStatus: null));

            return generated;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            // HttpStatusOf é REUSADO, não copiado (D6): ele já cobre os três
            // SDKs, incluindo o ClientResultException do caminho `openai`. Duas
            // cópias do switch divergiriam no próximo SDK acrescentado, e nada
            // reprovaria.
            record(new EmbeddingCallMeasurement(
                inputs.Count,
                stopwatch.Elapsed.TotalMilliseconds,
                InputTokens: null,
                Failed: true,
                ExecutionMetricsScope.HttpStatusOf(exception)));

            throw;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : inner.GetService(serviceType, serviceKey);

    /// <summary>
    /// No-op deliberado — ver o comentário da classe. O inner é compartilhado no
    /// processo e descartá-lo aqui reintroduziria o vazamento de pool.
    /// </summary>
    public void Dispose()
    {
    }
}
