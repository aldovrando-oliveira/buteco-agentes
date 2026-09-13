using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeFragments.Responses;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Contrato de <c>GET /knowledge-index/diagnostics</c> — a proveniência gravada
/// no índice de conhecimento.
///
/// <para>
/// <b>Fragmentos são semeados por SQL cru</b>, no molde de
/// <c>KnowledgeDocumentIndexingContractTests</c>, porque <c>apps/api</c>
/// <b>nunca escreve</b> nessa tabela: quem escreve é <c>apps/workers</c>, e a
/// tabela nasce em <c>apps/api</c> por razão de deploy, não de domínio.
/// </para>
///
/// <para>
/// <b>Cada teste limpa a tabela antes de semear, e isso não é zelo decorativo.</b>
/// A rota é <b>global</b>: ela agrega o índice inteiro, sem filtro de base. Os
/// testes de uma classe compartilham a mesma <c>ApiFactoryFixture</c> — logo o
/// mesmo Postgres — e o xUnit não garante a ordem entre eles, então sem a limpeza
/// o cenário de índice vazio passaria ou reprovaria conforme quem rodasse antes.
/// É o mecanismo exato que hoje faz <c>AgentDeactivationTests</c> reprovar em
/// suíte e passar isolado: estado compartilhado de fixture sem reset, com
/// asserção que depende da ordem.
/// </para>
/// </summary>
public class KnowledgeIndexDiagnosticsTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    private const string Route = "/knowledge-index/diagnostics";
    private const string Provider = "openai";
    private const string Model = "qwen-qwen3-embedding-8b";
    private const int Dimensions = 4096;

    [Fact]
    public async Task EmptyIndex_ReturnsEmptyList_AndAssertsNoProvenance()
    {
        await ClearFragmentsAsync();

        var response = await factory.CreateClient().GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var provenance = await ReadAsync(response);

        Assert.Empty(provenance);

        // Asserção NEGATIVA, e é a que importa: índice vazio não tem proveniência,
        // e a rota não preenche o vazio com o que a configuração declara. Ela não
        // poderia nem se quisesse — apps/api não tem a seção Embedding —, e este
        // par de asserções guarda a construção.
        Assert.DoesNotContain(Provider, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qwen", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4096", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleCombination_ReturnsOneItemWithExactCount()
    {
        await ClearFragmentsAsync();
        var document = await CreateDocumentAsync("Base proveniência única");
        await SeedFragmentsAsync(document, Provider, Model, Dimensions, count: 3);

        var provenance = await GetAsync();

        var only = Assert.Single(provenance);
        Assert.Equal(Provider, only.Provider);
        Assert.Equal(Model, only.Model);
        Assert.Equal(Dimensions, only.Dimensions);
        Assert.Equal(3, only.FragmentCount);
    }

    /// <summary>
    /// O estado que reprova o boot de <c>apps/workers</c> — vetores de modelos
    /// incomparáveis no mesmo índice — e que <b>esta rota serve normalmente</b>,
    /// porque é nele que o operador abre a tela. Com três campos escalares este
    /// cenário não teria como passar.
    /// </summary>
    [Fact]
    public async Task TwoCombinations_ReturnsBothWithTheirOwnCounts()
    {
        await ClearFragmentsAsync();
        var document = await CreateDocumentAsync("Base índice corrompido");
        await SeedFragmentsAsync(document, Provider, Model, Dimensions, count: 5);
        await SeedFragmentsAsync(document, Provider, "nomic-embed-text-v1.5", Dimensions, count: 2);

        var response = await factory.CreateClient().GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var provenance = await ReadAsync(response);

        Assert.Equal(2, provenance.Count);
        Assert.Equal(2, provenance.Single(row => row.Model == "nomic-embed-text-v1.5").FragmentCount);
        Assert.Equal(5, provenance.Single(row => row.Model == Model).FragmentCount);
    }

    /// <summary>
    /// A agregação é do <b>índice</b>, não da base. Este é o cenário que reprova
    /// se alguém acrescentar um filtro por base à consulta.
    /// </summary>
    [Fact]
    public async Task TwoBasesWithSameCombination_ReturnOneItemWithTheSum()
    {
        await ClearFragmentsAsync();
        var first = await CreateDocumentAsync("Base agregada A");
        var second = await CreateDocumentAsync("Base agregada B");
        await SeedFragmentsAsync(first, Provider, Model, Dimensions, count: 4);
        await SeedFragmentsAsync(second, Provider, Model, Dimensions, count: 6);

        var provenance = await GetAsync();

        var only = Assert.Single(provenance);
        Assert.Equal(10, only.FragmentCount);
    }

    /// <summary>
    /// Ordem determinística, e o par de nomes é <b>medido</b>, não inventado:
    /// <c>suporte-alfa</c> e <c>Suporte Alfa</c> são ordenados em ordens
    /// <b>opostas</b> pela collation do PostgreSQL e pelo comparador de
    /// <c>string</c> do .NET/ICU — medido nos dois runtimes reais em
    /// <c>openspec/changes/archive/2026-09-09-ordenacao-desempate-listas-vinculo/design.md</c>
    /// (linhas 59-64), e já exercido nesta suíte por
    /// <c>AgentKnowledgeBindingEndpointsTests.KnowledgeBases_OrderedByDatabaseCollation_...</c>.
    ///
    /// <para>
    /// O banco devolve <c>suporte-alfa</c> primeiro; o .NET devolveria
    /// <c>Suporte Alfa</c>. Por isso a asserção é sobre a <b>ordem do banco</b>: ela
    /// reprova no instante em que a ordenação migrar para memória, que é o defeito
    /// que este guarda existe para pegar. Afirmar apenas "duas chamadas concordam"
    /// passaria com o defeito presente.
    /// </para>
    ///
    /// <para>
    /// Semeado na ordem <b>oposta</b> à esperada, para que a ordem de inserção não
    /// produza o resultado certo por acidente.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Items_AreOrderedByDatabaseCollation_NotByDotNetComparer()
    {
        await ClearFragmentsAsync();
        var document = await CreateDocumentAsync("Base ordenação");
        await SeedFragmentsAsync(document, Provider, "Suporte Alfa", Dimensions, count: 1);
        await SeedFragmentsAsync(document, Provider, "suporte-alfa", Dimensions, count: 1);

        var provenance = await GetAsync();

        Assert.Equal(["suporte-alfa", "Suporte Alfa"], provenance.Select(row => row.Model));
    }

    /// <summary>
    /// O custo é de <b>uma</b> consulta, independente do número de bases e de
    /// documentos. A asserção é sobre o SQL <b>de produção</b> emitido pelo caminho
    /// real da requisição, nunca uma consulta remontada no teste.
    /// </summary>
    [Fact]
    public async Task Route_EmitsASingleQuery_RegardlessOfBasesAndDocuments()
    {
        await ClearFragmentsAsync();
        var first = await CreateDocumentAsync("Base custo A");
        var second = await CreateDocumentAsync("Base custo B");
        var third = await CreateDocumentAsync("Base custo C");
        await SeedFragmentsAsync(first, Provider, Model, Dimensions, count: 2);
        await SeedFragmentsAsync(second, Provider, Model, Dimensions, count: 2);
        await SeedFragmentsAsync(third, Provider, Model, Dimensions, count: 2);

        var client = factory.CreateClient();
        var commands = await factory.SqlCapture.CaptureAsync(async () =>
        {
            var response = await client.GetAsync(Route);
            response.EnsureSuccessStatusCode();
        });

        // Lança se nenhum ou se mais de um casar — uma consulta por base seria
        // três, e o resultado sairia correto, só caro.
        var command = EmittedSqlCapture.SingleCommandContaining(commands, "knowledge_fragments", "GROUP BY");

        // A ordenação está na CONSULTA, não no processo (api-response-ordering).
        Assert.Contains("ORDER BY", command, StringComparison.Ordinal);
    }

    /// <summary>
    /// Convenção 12: a asserção é sobre o <b>texto</b> do JSON. Desserializar para o
    /// mesmo tipo é cego a nome de campo — a chave passa pela mesma política de
    /// nomes na ida e na volta e sempre casa.
    /// </summary>
    [Fact]
    public async Task WireFieldNames_AreTheDeclaredOnes()
    {
        await ClearFragmentsAsync();
        var document = await CreateDocumentAsync("Base nomes no fio");
        await SeedFragmentsAsync(document, Provider, Model, Dimensions, count: 3);

        var body = await factory.CreateClient().GetStringAsync(Route);

        Assert.Contains("\"provider\":", body, StringComparison.Ordinal);
        Assert.Contains("\"model\":", body, StringComparison.Ordinal);
        // Sem aspas no valor: os dois são números no fio, não strings.
        Assert.Contains("\"dimensions\":4096", body, StringComparison.Ordinal);
        Assert.Contains("\"fragmentCount\":3", body, StringComparison.Ordinal);
    }

    // --- Arranjo -------------------------------------------------------------

    private async Task<KnowledgeDocumentRef> CreateDocumentAsync(string baseName)
    {
        var client = factory.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync(baseName);
        var document = await client.CreateDocumentAsync(knowledgeBase.Id, $"Documento de {baseName}");
        return new KnowledgeDocumentRef(knowledgeBase.Id, document.Id);
    }

    private async Task SeedFragmentsAsync(
        KnowledgeDocumentRef document, string provider, string model, int dimensions, int count)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var embedding = "[" + string.Join(',', Enumerable.Repeat("0.1", 4096)) + "]";

        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            await dbContext.Database.ExecuteSqlRawAsync($"""
                INSERT INTO knowledge_fragments
                  ("Id", "KnowledgeDocumentId", "KnowledgeBaseId", "Ordinal", "Text", "Embedding",
                   "EmbeddingProvider", "EmbeddingModel", "EmbeddingDimensions", "CreatedAt")
                VALUES ('{Guid.NewGuid()}', '{document.DocumentId}', '{document.KnowledgeBaseId}', {ordinal},
                        'trecho {ordinal}', '{embedding}'::vector,
                        '{provider}', '{model}', {dimensions}, now());
                """);
        }
    }

    private async Task ClearFragmentsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM knowledge_fragments;");
    }

    private async Task<IReadOnlyList<KnowledgeIndexProvenanceResponse>> GetAsync()
    {
        var response = await factory.CreateClient().GetAsync(Route);
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    private static async Task<IReadOnlyList<KnowledgeIndexProvenanceResponse>> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<List<KnowledgeIndexProvenanceResponse>>())!;

    private sealed record KnowledgeDocumentRef(Guid KnowledgeBaseId, Guid DocumentId);
}
