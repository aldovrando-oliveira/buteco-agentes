using System.Text;
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

    /// <summary>
    /// Tamanho de lote dos cenários que NÃO exercitam loteamento. Alto o
    /// bastante para que qualquer documento de teste caiba numa chamada só —
    /// preserva o comportamento que esses cenários sempre tiveram.
    /// </summary>
    public const int NoBatching = 10_000;

    public static KnowledgeIndexingHarness Build(
        string connectionString,
        IKnowledgeChunker? chunker = null,
        int dimensions = Dimensions,
        string model = Model,
        int batchSize = NoBatching)
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
            BatchSize = batchSize,
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

    /// <summary>
    /// Markdown que produz <paramref name="sections"/> fragmentos — um por
    /// seção —, com <b>texto distinto em cada uma</b>.
    ///
    /// <para>
    /// <b>Por que não um documento de 442 fragmentos</b>, que é o tamanho do
    /// caso real: a propriedade que os guardas afirmam é escala-livre ("mais
    /// fragmentos que o lote"), e não depende de o lote ser 250. Semear 442
    /// fragmentos gravaria ~7 MB de vetor por cenário num Postgres real e
    /// empurraria a suíte na direção da referência de duração (6m37s com 14
    /// classes e zero containers de dev, recalibrada em 20/09/2026), que é onde
    /// a convenção 22 manda não mexer sem recalibrar. Os cenários usam lote
    /// pequeno contra documento pequeno.
    /// </para>
    ///
    /// <para>
    /// <b>Texto distinto por seção não é enfeite</b>: o guarda de alinhamento
    /// compara o vetor gravado de cada fragmento com o vetor do texto daquele
    /// fragmento, e o duplo deriva o vetor do texto. Seções repetidas deixariam
    /// esse guarda verde com dois vetores trocados entre si.
    /// </para>
    ///
    /// <para>
    /// O corpo de cada seção passa de <c>TargetMin</c> (900), o que faz o
    /// fragmentador emitir uma seção por fragmento em vez de juntá-las. A
    /// contagem resultante é <b>afirmada como precondição</b> em cada cenário
    /// que a usa — se o fragmentador mudar, o cenário diz isso em vez de
    /// silenciosamente deixar de exercitar o lote.
    /// </para>
    /// </summary>
    public static string MultiFragmentMarkdown(int sections)
    {
        var builder = new StringBuilder("# Manual de operação\n");

        for (var section = 1; section <= sections; section++)
        {
            builder.Append($"\n## Procedimento {section}\n\n");

            // Frases numeradas pela seção: nenhum par de seções compartilha
            // texto, e o corpo passa de 900 caracteres.
            for (var sentence = 1; sentence <= 7; sentence++)
            {
                builder.Append(
                    $"No procedimento {section}, o passo {sentence} descreve a conferência "
                  + $"número {section}-{sentence} do roteiro, com a anotação correspondente "
                  + $"registrada pelo operador responsável pelo turno {section}. ");
            }

            builder.Append('\n');
        }

        return builder.ToString();
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

    /// <summary>
    /// Tamanho de <b>cada</b> chamada, na ordem em que aconteceram. É o que os
    /// guardas de loteamento afirmam — número e tamanho das chamadas, nunca
    /// texto de mensagem de erro.
    ///
    /// <para>
    /// Convive com <see cref="LastBatchSize"/> em vez de substituí-lo: o
    /// cenário do par (documento menor que o lote) já o usava, e trocá-lo seria
    /// blast radius sem ganho.
    /// </para>
    /// </summary>
    public IReadOnlyList<int> BatchSizes => batchSizes;

    private readonly List<int> batchSizes = [];

    /// <summary>
    /// Quando definido, a chamada devolve esta quantidade de vetores em vez de
    /// um por entrada — para exercitar a conferência de contagem por lote.
    /// </summary>
    public int? OverrideResultCount { get; set; }

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
        batchSizes.Add(texts.Count);
        var size = OverrideDimensions ?? dimensions;

        if (OverrideResultCount is { } forced)
        {
            texts = [.. texts.Take(forced)];
        }

        var result = new GeneratedEmbeddings<Embedding<float>>(
            texts.Select(text => new Embedding<float>(VectorFor(text, size))));

        return result;
    }

    /// <summary>
    /// O vetor que este duplo produz para <paramref name="text"/>. Público
    /// porque o guarda de <b>alinhamento</b> precisa recomputar a expectativa
    /// pelo mesmo caminho: ele afirma que o vetor gravado de cada fragmento é o
    /// do <b>próprio texto</b> daquele fragmento, um a um.
    ///
    /// <para>
    /// O vetor deriva do texto, então textos diferentes tendem a vetores
    /// diferentes — mas "tendem" não basta para um guarda. Quem o usar afirma
    /// antes que as expectativas do cenário são <b>duas a duas distintas</b>:
    /// um conjunto com repetição deixaria a asserção verde com os vetores
    /// trocados, que é exatamente o defeito a pegar.
    /// </para>
    /// </summary>
    public static float[] VectorFor(string text, int size)
    {
        var vector = new float[size];
        for (var i = 0; i < size; i++)
        {
            vector[i] = (text.GetHashCode(StringComparison.Ordinal) % 97 + i) / 100f;
        }

        return vector;
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
