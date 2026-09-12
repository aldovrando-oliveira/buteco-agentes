using Buteco.Workers.Knowledge.Embedding;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.Tests.Knowledge.Support;

/// <summary>
/// Devolve sempre o mesmo vetor, de dimensão 4096 — a mesma da coluna
/// <c>vector(4096)</c>. Existe para que a distância seja previsível e as
/// asserções de busca não dependam de chamada de rede nem de chave de provedor.
/// </summary>
public sealed class FakeEmbeddingGeneratorResolver(float[] vector) : IEmbeddingGeneratorResolver
{
    public IEmbeddingGenerator<string, Embedding<float>> Resolve() => new FixedGenerator(vector);

    private sealed class FixedGenerator(float[] vector) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(vector)).ToList()));

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Gerador que sempre estoura, para o cenário de degradação graciosa: a tool
/// devolve resultado de falha ao modelo e a task do agente não cai junto
/// (convenção 4).
/// </summary>
public sealed class ThrowingEmbeddingGeneratorResolver : IEmbeddingGeneratorResolver
{
    public IEmbeddingGenerator<string, Embedding<float>> Resolve() =>
        throw new InvalidOperationException("Provedor de embedding indisponível (simulado).");
}
