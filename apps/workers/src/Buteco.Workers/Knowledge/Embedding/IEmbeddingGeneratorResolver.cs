using Microsoft.Extensions.AI;

namespace Buteco.Workers.Knowledge.Embedding;

/// <summary>
/// Resolve o <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> do provedor
/// configurado, construído por chamada — mesmo molde de
/// <c>IChatClientResolver</c>, e pelo mesmo motivo: a interface existe
/// especificamente para ser testável isolando "qual tipo de gerador é
/// construído para qual provedor" sem chamada de rede real.
/// </summary>
public interface IEmbeddingGeneratorResolver
{
    IEmbeddingGenerator<string, Embedding<float>> Resolve();
}
