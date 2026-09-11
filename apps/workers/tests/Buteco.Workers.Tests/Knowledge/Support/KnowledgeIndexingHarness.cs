using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Chunking;
using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Knowledge.Indexing;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Workers.Tests.Knowledge.Support;

/// <summary>
/// Monta um <see cref="KnowledgeIndexingService"/> contra Postgres real, com o
/// provedor de embedding substituído — nenhuma chamada de rede.
/// </summary>
internal sealed class KnowledgeIndexingHarness
{
    public const string Provider = "openai";
    public const string Model = "modelo-de-teste";
    /// <summary>
    /// 4096 e não um número pequeno de conveniência: a coluna é
    /// <c>vector(4096)</c>, e um arnês com dimensão menor não exercitaria o
    /// tipo real — passaria no teste e falharia em produção.
    /// </summary>
    public const int Dimensions = 4096;

    public required IServiceScopeFactory ScopeFactory { get; init; }

    public required FakeEmbeddingGenerator Embeddings { get; init; }

    public required FakeTimeProvider Time { get; init; }

    public required KnowledgeIndexingService Service { get; init; }

    public static KnowledgeIndexingHarness Build(
        string connectionString,
        IKnowledgeChunker? chunker = null,
        int dimensions = Dimensions,
        string model = Model)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(connectionString));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var embeddings = new FakeEmbeddingGenerator(dimensions);
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-11T03:00:00Z"));
        var options = Microsoft.Extensions.Options.Options.Create(new EmbeddingOptions
        {
            Provider = Provider,
            Model = model,
            Dimensions = dimensions,
        });

        var service = new KnowledgeIndexingService(
            scopeFactory,
            chunker ?? new KnowledgeChunker(),
            new StubEmbeddingResolver(embeddings),
            options,
            time,
            NullLogger<KnowledgeIndexingService>.Instance);

        return new KnowledgeIndexingHarness
        {
            ScopeFactory = scopeFactory,
            Embeddings = embeddings,
            Time = time,
            Service = service,
        };
    }

    public AppDbContext NewDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options);

    /// <summary>
    /// Semeia base e documento por SQL. A entidade de documento em
    /// <c>apps/workers</c> é espelho de leitura, sem construtor público — semear
    /// por SQL é honesto com esse papel e não abre escrita que o app não tem.
    /// </summary>
    public static async Task<(Guid BaseId, Guid DocumentId)> SeedDocumentAsync(
        AppDbContext dbContext, string extractedText, int contentRevision = 1)
    {
        var baseId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt")
            VALUES ({0}, 'Base de teste', 'Descrição', true, {2}, {2});
            """.Replace("{0}", $"'{baseId}'").Replace("{2}", $"'{now:O}'"));

        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO knowledge_documents
              ("Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus",
               "IndexedAt", "FailureReason", "ContentRevision", "ContentHash",
               "FragmentCount", "IndexingAttempts", "LastAttemptAt", "CreatedAt", "UpdatedAt")
            VALUES ({documentId}, {baseId}, 'Documento', 'markdown', {extractedText}, 'Pending',
                    NULL, NULL, {contentRevision}, 'hash-de-teste', 0, 0, NULL, {now}, {now});
            """);

        return (baseId, documentId);
    }
}

/// <summary>
/// Gerador de embedding determinístico. Não fala com rede: devolve um vetor por
/// entrada, derivado do texto, para que o mesmo texto dê sempre o mesmo vetor.
/// </summary>
internal sealed class FakeEmbeddingGenerator(int dimensions) : IEmbeddingGenerator<string, Embedding<float>>
{
    public int CallCount { get; private set; }

    public int LastBatchSize { get; private set; }

    /// <summary>Quando definido, a chamada seguinte lança isto.</summary>
    public Func<Exception>? ThrowOnNextCall { get; set; }

    /// <summary>Quando definido, o vetor sai com esta dimensão em vez da declarada.</summary>
    public int? OverrideDimensions { get; set; }

    /// <summary>
    /// Executado ANTES de produzir os vetores. Existe para os testes de
    /// concorrência abrirem a janela de corrida exatamente onde ela existe na
    /// vida real: durante o trabalho lento, entre ler o documento e gravar.
    /// </summary>
    public Func<Task>? BeforeGenerate { get; set; }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (BeforeGenerate is { } gate)
        {
            await gate();
        }

        if (ThrowOnNextCall is { } factory)
        {
            ThrowOnNextCall = null;
            throw factory();
        }

        var texts = values.ToList();
        LastBatchSize = texts.Count;
        var size = OverrideDimensions ?? dimensions;

        var result = new GeneratedEmbeddings<Embedding<float>>(texts.Select(text =>
        {
            var vector = new float[size];
            for (var i = 0; i < size; i++)
            {
                vector[i] = (text.GetHashCode(StringComparison.Ordinal) % 97 + i) / 100f;
            }

            return new Embedding<float>(vector);
        }));

        return result;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class StubEmbeddingResolver(IEmbeddingGenerator<string, Embedding<float>> generator)
    : IEmbeddingGeneratorResolver
{
    public IEmbeddingGenerator<string, Embedding<float>> Resolve() => generator;
}
