namespace Buteco.Workers.ExecutionMetrics.Entities;

/// <summary>
/// Espelho de <c>Buteco.Api.ExecutionMetrics.Entities.ProviderCall</c> — uma
/// linha por requisição HTTP ao provedor de LLM feita dentro de uma execução.
///
/// <para>
/// <b>O grão é a requisição, e isso já comporta a etapa 2</b> (D7): um lote de
/// embedding é uma requisição, e embedding não tem saída — <see cref="OutputTokens"/>
/// nulo é o que ele tem de gravar, não zero. O que a etapa 2 muda é só
/// <see cref="TaskId"/>, que passa a anulável (indexação não roda dentro de
/// task) e ganha referência de documento; <c>DROP NOT NULL</c> não reescreve a
/// tabela.
/// </para>
/// </summary>
public class ProviderCall
{
    public Guid Id { get; private set; }

    public string TaskId { get; private set; } = null!;

    public string Provider { get; private set; } = null!;

    public string Model { get; private set; } = null!;

    public string Purpose { get; private set; } = null!;

    public double DurationMs { get; private set; }

    /// <summary>
    /// Nulo = o provedor não reportou. <b>Nunca normalizado para zero</b>
    /// (convenção 13): zero só quando o provedor reportou zero.
    /// </summary>
    public long? InputTokens { get; private set; }

    /// <inheritdoc cref="InputTokens"/>
    public long? OutputTokens { get; private set; }

    /// <inheritdoc cref="InputTokens"/>
    public long? CachedInputTokens { get; private set; }

    public bool Failed { get; private set; }

    public int? HttpStatus { get; private set; }

    private ProviderCall()
    {
    }

    public ProviderCall(
        string taskId,
        string provider,
        string model,
        string purpose,
        double durationMs,
        long? inputTokens,
        long? outputTokens,
        long? cachedInputTokens,
        bool failed,
        int? httpStatus)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        Provider = provider;
        Model = model;
        Purpose = purpose;
        DurationMs = durationMs;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CachedInputTokens = cachedInputTokens;
        Failed = failed;
        HttpStatus = httpStatus;
    }
}
