namespace Buteco.Api.ExecutionMetrics.Entities;

/// <summary>
/// Uma requisição HTTP ao provedor de LLM feita dentro de uma execução — a linha
/// filha de <see cref="TaskExecution"/>. Só mapeamento: quem escreve é
/// <c>apps/workers</c> (ver o comentário de <see cref="TaskExecution"/>).
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
    /// Os três contadores de token são anuláveis: nulo = o provedor não reportou.
    /// Nunca normalizados para zero (convenção 13) — a tela distingue célula
    /// vazia de <c>0</c>.
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
}
