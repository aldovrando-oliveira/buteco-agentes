using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// <c>GET /knowledge-bases/indexing-summary</c> — o estado de indexação agregado
/// por base, numa requisição para o conjunto inteiro.
///
/// <para>
/// Recurso próprio, e não campos em <c>KnowledgeBaseResponse</c> (design.md,
/// D1). A propriedade central, e a razão de ele existir, é o guarda de custo
/// abaixo: contagem por base a uma requisição cada foi o que fez a etapa 5a-1
/// recusar as colunas do catálogo com 100+ bases declaradas.
/// </para>
/// </summary>
public class KnowledgeBaseIndexingSummaryTests(ApiFactoryFixture fixture) : IClassFixture<ApiFactoryFixture>
{
    private const string Route = "/knowledge-bases/indexing-summary";

    private async Task<IReadOnlyList<KnowledgeBaseIndexingSummaryResponse>> GetSummaryAsync(HttpClient client)
    {
        var response = await client.GetAsync(Route);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<KnowledgeBaseIndexingSummaryResponse>>())!;
    }

    [Fact]
    public async Task Summary_AggregatesIndexingStatesPerBase()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base resumo agregação");

        for (var i = 0; i < 3; i++)
        {
            var indexed = await client.CreateDocumentAsync(knowledgeBase.Id, $"Indexado {i}");
            await KnowledgeTestClient.ForceIndexedAsync(fixture.Services, indexed.Id);
        }

        var failed = await client.CreateDocumentAsync(knowledgeBase.Id, "Falhou");
        await KnowledgeTestClient.ForceFailedAsync(fixture.Services, failed.Id);

        // Um quinto documento fica em Pending, que é o estado de nascimento.
        await client.CreateDocumentAsync(knowledgeBase.Id, "Pendente");

        var summary = await GetSummaryAsync(client);
        var row = Assert.Single(summary, item => item.KnowledgeBaseId == knowledgeBase.Id);

        Assert.Equal(5, row.DocumentCount);
        Assert.Equal(3, row.IndexedCount);
        Assert.Equal(1, row.FailedCount);

        // O não terminal é o complemento exato, por subtração — e é por isso que
        // Pending e Indexing não têm campo próprio (design.md, D2).
        Assert.Equal(1, row.DocumentCount - row.IndexedCount - row.FailedCount);
    }

    /// <summary>
    /// O par "sem item" obrigatório da convenção 5, e o guarda de R2: o
    /// <c>GroupBy</c> não emite grupo para base sem documento, então projetar
    /// sobre os grupos em vez de sobre as bases perderia esta linha em silêncio.
    /// A asserção é sobre a <b>presença</b> da linha, não só sobre o valor.
    /// </summary>
    [Fact]
    public async Task BaseWithNoDocuments_IsPresentWithZeros()
    {
        var client = fixture.CreateClient();
        var empty = await client.CreateBaseAsync("Base resumo vazia");

        var summary = await GetSummaryAsync(client);

        var row = Assert.Single(summary, item => item.KnowledgeBaseId == empty.Id);
        Assert.Equal(0, row.DocumentCount);
        Assert.Equal(0, row.IndexedCount);
        Assert.Equal(0, row.FailedCount);
    }

    /// <summary>
    /// Guarda de R5. Filtrar por <c>IsActive</c> "para ficar coerente com o
    /// agente" deixaria em branco exatamente as linhas que mais pedem atenção —
    /// e o catálogo inclui bases inativas de propósito.
    /// </summary>
    [Fact]
    public async Task InactiveBase_IsPresentWithRealCounts()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base resumo inativa");
        var indexed = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento de base inativa");
        await KnowledgeTestClient.ForceIndexedAsync(fixture.Services, indexed.Id);

        (await client.PostAsync($"/knowledge-bases/{knowledgeBase.Id}/deactivate", content: null))
            .EnsureSuccessStatusCode();

        var summary = await GetSummaryAsync(client);

        var row = Assert.Single(summary, item => item.KnowledgeBaseId == knowledgeBase.Id);
        Assert.Equal(1, row.DocumentCount);
        Assert.Equal(1, row.IndexedCount);
    }

    [Fact]
    public async Task DeletingDocument_LowersTheCount()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base resumo exclusão");
        var kept = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento mantido");
        var removed = await client.CreateDocumentAsync(knowledgeBase.Id, "Documento removido");

        var before = Assert.Single(await GetSummaryAsync(client), item => item.KnowledgeBaseId == knowledgeBase.Id);
        Assert.Equal(2, before.DocumentCount);

        (await client.DeleteAsync($"/knowledge-bases/{knowledgeBase.Id}/documents/{removed.Id}"))
            .EnsureSuccessStatusCode();

        var after = Assert.Single(await GetSummaryAsync(client), item => item.KnowledgeBaseId == knowledgeBase.Id);
        Assert.Equal(1, after.DocumentCount);
        Assert.NotNull(kept);
    }

    /// <summary>
    /// <b>O guarda de R1.</b> O defeito que ele pega — contagem por base dentro
    /// do <c>Select</c> — produz resultado <b>correto</b>, só caro, então nenhuma
    /// asserção de conteúdo o encontra. A asserção é sobre o SQL <b>emitido pela
    /// requisição real</b> (convenção 11), e o critério é a independência do
    /// número de bases: com três bases cadastradas, o número de consultas é o
    /// mesmo que com uma.
    /// </summary>
    [Fact]
    public async Task SummaryQuery_CostDoesNotGrowWithNumberOfBases()
    {
        var client = fixture.CreateClient();

        var first = await client.CreateBaseAsync("Base custo 1");
        await client.CreateDocumentAsync(first.Id, "Documento custo 1");

        var baseline = await fixture.SqlCapture.CaptureAsync(async () =>
        {
            (await client.GetAsync(Route)).EnsureSuccessStatusCode();
        });

        var second = await client.CreateBaseAsync("Base custo 2");
        await client.CreateDocumentAsync(second.Id, "Documento custo 2");
        await client.CreateBaseAsync("Base custo 3 sem documento");

        var grown = await fixture.SqlCapture.CaptureAsync(async () =>
        {
            (await client.GetAsync(Route)).EnsureSuccessStatusCode();
        });

        Assert.Equal(baseline.Count, grown.Count);

        // E o número é o projetado: duas consultas, uma pelas bases e uma pela
        // agregação. Afirmar só a igualdade deixaria passar um N+1 que já
        // estivesse presente nas duas capturas.
        Assert.Equal(2, grown.Count);
    }

    /// <summary>
    /// Metade comportamental do guarda de ordenação. Reprova só
    /// probabilisticamente — sem o <c>ThenBy</c> o Postgres às vezes já devolve
    /// em ordem de id sozinho —, e por isso vem em par com a asserção sobre o
    /// SQL logo abaixo (convenção 15, quinta forma).
    /// </summary>
    [Fact]
    public async Task SummaryWithEqualCreatedAt_IsTieBrokenById()
    {
        var client = fixture.CreateClient();
        var first = await client.CreateBaseAsync("Base Empate Resumo 1");
        var second = await client.CreateBaseAsync("Base Empate Resumo 2");
        var third = await client.CreateBaseAsync("Base Empate Resumo 3");

        var tied = new[] { first.Id, second.Id, third.Id };
        await CreatedAtTie.ForceAsync(fixture.Services, "knowledge_bases", tied);

        var summary = await GetSummaryAsync(client);

        var observed = summary.Where(item => tied.Contains(item.KnowledgeBaseId))
            .Select(item => item.KnowledgeBaseId)
            .ToList();
        Assert.Equal(tied.Order().ToList(), observed);
    }

    /// <summary>
    /// Metade determinística: reprova em 100% das execuções no instante em que o
    /// <c>ThenBy</c> sai, e é sobre a consulta de produção.
    /// </summary>
    [Fact]
    public async Task SummaryQuery_EmitsTieBreakAsLastOrderByTerm()
    {
        var client = fixture.CreateClient();
        await client.CreateBaseAsync("Base SQL resumo");

        var commands = await fixture.SqlCapture.CaptureAsync(async () =>
        {
            (await client.GetAsync(Route)).EnsureSuccessStatusCode();
        });

        var query = EmittedSqlCapture.SingleCommandContaining(commands, "FROM knowledge_bases", "ORDER BY");
        EmittedSqlCapture.AssertOrderByEndsWithTieBreak(query);
    }

    /// <summary>
    /// Formato de fio (convenção 12), inspecionando o <b>texto</b> da resposta
    /// HTTP real — nunca desserializando para o mesmo tipo, que passaria pela
    /// mesma política de nomes na ida e na volta e sempre casaria.
    ///
    /// <para>
    /// <c>knowledgeBaseId</c> é o nome que mais pede este guarda: a propriedade
    /// termina em sigla/maiúsculas consecutivas (<c>Id</c>), e a política
    /// camelCase minúscula apenas a primeira letra — a mesma classe de defeito
    /// que mordeu em <c>agente-enderecos-a2a</c>, onde <c>A2A</c> saiu como
    /// <c>a2A</c> e o consumidor recebeu campo ausente.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SummaryResponse_UsesCamelCaseFieldNamesOnTheWire()
    {
        var client = fixture.CreateClient();
        var knowledgeBase = await client.CreateBaseAsync("Base formato de fio");
        await client.CreateDocumentAsync(knowledgeBase.Id, "Documento formato de fio");

        var json = await client.GetStringAsync(Route);

        using var parsed = JsonDocument.Parse(json);
        var row = parsed.RootElement.EnumerateArray()
            .Single(item => item.GetProperty("knowledgeBaseId").GetGuid() == knowledgeBase.Id);

        Assert.Equal(JsonValueKind.Number, row.GetProperty("documentCount").ValueKind);
        Assert.Equal(JsonValueKind.Number, row.GetProperty("indexedCount").ValueKind);
        Assert.Equal(JsonValueKind.Number, row.GetProperty("failedCount").ValueKind);

        // Asserção negativa: nenhuma variante de caixa alternativa no fio.
        Assert.DoesNotContain("KnowledgeBaseId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("knowledgeBaseID", json, StringComparison.Ordinal);
    }
}
