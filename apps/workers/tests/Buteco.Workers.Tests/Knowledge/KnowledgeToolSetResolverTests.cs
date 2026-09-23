using Buteco.Workers.ExecutionMetrics;
using System.Text.RegularExpressions;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Knowledge.Support;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Workers.Tests.Knowledge;

/// <summary>
/// Cobre a capability <c>knowledge-tool-execution</c>: quais bases viram tool,
/// o que a busca devolve, e os dois casos de "sem trecho" que não podem ser
/// confundidos um com o outro.
///
/// <para>
/// Testa o resolvedor <b>direto</b>, sem <c>AgentExecutionService</c> nem
/// <c>ChatClientAgent</c> — mesmo molde de <c>McpToolSetResolverTests</c>, e as
/// tabelas são semeadas por <b>SQL cru</b> porque as entidades espelho de
/// <c>apps/workers</c> são read-only (sem setters públicos).
/// </para>
///
/// <para>
/// <b>Exige Postgres real, e não é preferência:</b> <c>AppDbContext</c> mapeia
/// <c>KnowledgeFragment</c> dentro de <c>if (Database.IsNpgsql())</c> e o ignora
/// fora disso, porque o EF valida o modelo inteiro e <c>Vector</c> não tem
/// construtor vinculável fora do provider relacional.
/// </para>
/// </summary>
public class KnowledgeToolSetResolverTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private const int Dimensions = 4096;

    // ---------------------------------------------------------------- resolução

    [Fact]
    public async Task ResolveAsync_LinkedActiveBase_ProducesOneTool()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Políticas de Cobrança", "Prazos, descontos e parcelamento.");
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var tools = await ResolveAsync(agentId);

        var tool = Assert.Single(tools);
        Assert.Equal("search_politicas-de-cobranca", tool.Name);
    }

    /// <summary>
    /// GUARDA DO FILTRO <c>IsActive</c>, e o arranjo é o ponto: a base inativa
    /// está vinculada <b>e tem fragmentos gravados</b>. Sem isso o teste mediria
    /// "base sem conteúdo não produz tool", que é outra coisa — é a primeira
    /// forma da convenção 15, guarda verde com o defeito presente. Com este
    /// arranjo, remover o <c>where knowledgeBase.IsActive</c> do resolvedor faz
    /// este teste reprovar.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_LinkedInactiveBaseWithFragments_IsNotOffered()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Base Desativada", "Assunto qualquer.", isActive: false);
        var documentId = await SeedDocumentAsync(baseId, "Documento indexado");
        await SeedFragmentAsync(baseId, documentId, 0, "conteúdo que existe de verdade", Vectors.Of(0.10));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var tools = await ResolveAsync(agentId);

        Assert.Empty(tools);
    }

    [Fact]
    public async Task ResolveAsync_TwoLinkedBases_ProducesTwoDistinctTools()
    {
        var agentId = Guid.NewGuid();
        var firstId = await SeedBaseAsync("Cobrança", "Faturas em atraso.");
        var secondId = await SeedBaseAsync("Reservas", "Mesas e horários.");
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, firstId);
        await SeedLinkAsync(agentId, secondId);

        var tools = await ResolveAsync(agentId);

        Assert.Equal(2, tools.Count);
        Assert.Equal(2, tools.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task ResolveAsync_AgentWithoutAnyLink_ProducesNoTool()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);

        Assert.Empty(await ResolveAsync(agentId));
    }

    [Fact]
    public async Task ResolveAsync_SameCatalog_ProducesSameToolNames_AcrossExecutions()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        foreach (var nome in new[] { "Zebra", "Alfa", "Meio" })
        {
            await SeedLinkAsync(agentId, await SeedBaseAsync(nome, $"Base {nome}."));
        }

        var first = await ResolveAsync(agentId);
        var second = await ResolveAsync(agentId);

        Assert.Equal(first.Select(t => t.Name), second.Select(t => t.Name));
    }

    // ------------------------------------------------------------- a descrição

    [Fact]
    public async Task ResolveAsync_ToolDescription_CarriesTheRegisteredBaseDescription()
    {
        const string descricao = "Regras de negociação de faturas em atraso: prazos e descontos por faixa.";
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, await SeedBaseAsync("Cobrança", descricao));

        var tool = Assert.Single(await ResolveAsync(agentId));

        Assert.Contains(descricao, tool.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// ASSERÇÃO NEGATIVA (convenção 13): a descrição da tool <b>não</b> pode
    /// prescrever um corte de distância. Seria o limiar que <c>0c</c> reprovou
    /// com 20 negativas, reintroduzido em prosa e sem o benefício de ser
    /// testável — e este é o guarda que reprova a regressão bem-intencionada de
    /// "deixar a tool mais útil" acrescentando "acima de 0,45 ignore".
    ///
    /// <para>
    /// A varredura é por <b>dígito</b> no bloco fixo, não por um valor
    /// específico, porque o modo de falha é alguém escrever qualquer número, não
    /// um número em particular.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ToolDescription_NeverPrescribesADistanceCutoff()
    {
        var agentId = Guid.NewGuid();
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, await SeedBaseAsync("Cobrança", "Prazos e descontos."));

        var tool = Assert.Single(await ResolveAsync(agentId));
        var blocoFixo = tool.Description[tool.Description.IndexOf("Esta ferramenta devolve", StringComparison.Ordinal)..];

        Assert.DoesNotMatch(new Regex(@"\d"), blocoFixo);
    }

    // ------------------------------------------------------------------ a busca

    [Fact]
    public async Task Invoke_BaseWithManyFragments_ReturnsFiveNearestInAscendingDistance()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var documentId = await SeedDocumentAsync(baseId, "Política de prazos");
        // Oito fragmentos afastando-se progressivamente da consulta.
        for (var i = 0; i < 8; i++)
        {
            await SeedFragmentAsync(baseId, documentId, i, $"trecho {i}", Vectors.Of(i * 0.05));
        }

        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var result = await InvokeAsync(agentId, "qualquer coisa", Vectors.Of(0.0));

        Assert.Null(result.Aviso);
        Assert.Equal(5, result.Trechos.Count);
        Assert.Equal(
            result.Trechos.Select(t => t.Distancia).Order().ToList(),
            result.Trechos.Select(t => t.Distancia).ToList());
    }

    [Fact]
    public async Task Invoke_BaseWithFewerFragmentsThanK_ReturnsWhatExists()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var documentId = await SeedDocumentAsync(baseId, "Política de prazos");
        await SeedFragmentAsync(baseId, documentId, 0, "primeiro", Vectors.Of(0.10));
        await SeedFragmentAsync(baseId, documentId, 1, "segundo", Vectors.Of(0.20));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var result = await InvokeAsync(agentId, "qualquer coisa", Vectors.Of(0.0));

        Assert.Equal(2, result.Trechos.Count);
        Assert.Null(result.Aviso);
    }

    [Fact]
    public async Task Invoke_TwoLinkedBases_NeverLeaksFragmentsFromTheOther()
    {
        var agentId = Guid.NewGuid();
        var minhaId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var outraId = await SeedBaseAsync("Reservas", "Mesas e horários.");
        var minhaDoc = await SeedDocumentAsync(minhaId, "Documento de cobrança");
        var outraDoc = await SeedDocumentAsync(outraId, "Documento de reservas");

        // O fragmento da OUTRA base é o mais próximo da consulta: se o filtro
        // por base sumir, ele aparece em primeiro e o teste reprova.
        await SeedFragmentAsync(minhaId, minhaDoc, 0, "trecho de cobrança", Vectors.Of(0.40));
        await SeedFragmentAsync(outraId, outraDoc, 0, "trecho de reservas", Vectors.Of(0.01));

        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, minhaId);
        await SeedLinkAsync(agentId, outraId);

        var result = await InvokeAsync(agentId, "qualquer coisa", Vectors.Of(0.0), toolName: "search_cobranca");

        Assert.All(result.Trechos, t => Assert.Equal("Documento de cobrança", t.Documento));
    }

    [Fact]
    public async Task Invoke_Result_CarriesCatalogTitleTrechoAndDistance()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var documentId = await SeedDocumentAsync(baseId, "Política de prazos");
        await SeedFragmentAsync(baseId, documentId, 0, "O prazo de compensação é de três dias úteis.", Vectors.Of(0.10));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var trecho = Assert.Single((await InvokeAsync(agentId, "prazo", Vectors.Of(0.0))).Trechos);

        Assert.Equal("Política de prazos", trecho.Documento);
        Assert.Equal("O prazo de compensação é de três dias úteis.", trecho.Trecho);
        Assert.InRange(trecho.Distancia, 0.0, 2.0);
    }

    /// <summary>
    /// ASSERÇÃO NEGATIVA sobre o formato entregue ao modelo (convenção 13): o
    /// resultado não afirma relevância, acerto nem confiança, porque o sistema
    /// não sabe nada disso. A asserção é sobre o <b>JSON serializado</b>, que é
    /// o artefato que o modelo realmente recebe — não sobre o record, que é só
    /// a representação interna (convenção 11).
    /// </summary>
    [Fact]
    public async Task Invoke_Result_NeverClaimsRelevance()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var documentId = await SeedDocumentAsync(baseId, "Política de prazos");
        await SeedFragmentAsync(baseId, documentId, 0, "qualquer conteúdo", Vectors.Of(0.10));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var (_, json) = await InvokeRawAsync(agentId, "x", Vectors.Of(0.0));

        foreach (var proibido in new[] { "relevan", "confian", "acerto", "resposta", "score", "match" })
        {
            Assert.DoesNotContain(proibido, json, StringComparison.OrdinalIgnoreCase);
        }
    }


    /// <summary>
    /// PAR DETERMINÍSTICO da ordenação (convenção 15, quinta forma). O teste
    /// acima afirma que os trechos vêm em distância crescente — e com poucas
    /// linhas o PostgreSQL pode devolver nessa ordem sozinho, pela ordem física
    /// de inserção. Este afirma sobre o <b>SQL que a invocação real emitiu</b>,
    /// e reprova em 100% das execuções no instante em que o <c>ORDER BY</c> ou o
    /// <c>LIMIT</c> saírem.
    /// </summary>
    [Fact]
    public async Task Invoke_EmittedSql_OrdersByVectorDistanceAndLimits()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var documentId = await SeedDocumentAsync(baseId, "Política de prazos");
        await SeedFragmentAsync(baseId, documentId, 0, "trecho", Vectors.Of(0.10));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var capture = new EmittedSqlCapture();
        var comandos = await capture.CaptureAsync(async () =>
            await InvokeAsync(agentId, "prazo", Vectors.Of(0.0), capture: capture));

        var busca = EmittedSqlCapture.SingleCommandContaining(comandos, "knowledge_fragments", "<=>");
        EmittedSqlCapture.AssertOrdersByVectorDistanceWithLimit(busca);
    }

    // -------------------------------------- os DOIS casos de "nenhum trecho"

    /// <summary>
    /// PAR DA CONVENÇÃO 5, primeira ponta. Sem limiar, um índice povoado devolve
    /// sempre <c>min(k, n)</c> — então consulta irrelevante <b>continua</b>
    /// devolvendo trechos, e o resultado não pode dizer que a base está sem
    /// conteúdo.
    /// </summary>
    [Fact]
    public async Task Invoke_PopulatedBaseWithIrrelevantQuery_StillReturnsTrechosWithDistance()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var documentId = await SeedDocumentAsync(baseId, "Política de prazos");
        await SeedFragmentAsync(baseId, documentId, 0, "prazo de compensação bancária", Vectors.Of(0.10));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        // Consulta deliberadamente distante de tudo o que existe na base.
        var result = await InvokeAsync(agentId, "quem descobriu o Brasil", Vectors.Of(0.99));

        Assert.NotEmpty(result.Trechos);
        Assert.Null(result.Aviso);
    }

    /// <summary>
    /// PAR DA CONVENÇÃO 5, segunda ponta, e é a que a convenção 13 protege: base
    /// vinculada sem nenhum fragmento indexado <b>não</b> é "a busca não achou
    /// nada". Não havia onde procurar, e dizer que procurou seria o sistema
    /// afirmando mais do que sabe.
    ///
    /// <para>
    /// Os dois testes juntos são o que impede conflatá-los — um sozinho passaria
    /// com a implementação errada.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Invoke_BaseWithoutAnyFragment_SaysSoInsteadOfSayingNothingWasFound()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Base Vazia", "Assunto ainda não carregado.");
        await SeedDocumentAsync(baseId, "Documento pendente");   // documento existe, fragmento não
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var result = await InvokeAsync(agentId, "qualquer coisa", Vectors.Of(0.0));

        Assert.Empty(result.Trechos);
        Assert.NotNull(result.Aviso);
        Assert.Contains("não tem conteúdo indexado", result.Aviso, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ degradação

    [Fact]
    public async Task Invoke_EmbeddingProviderFails_ReturnsFailureResultWithoutThrowing()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        var resolver = BuildResolver(new ThrowingEmbeddingGeneratorResolver());
        await using var dbContext = CreateDbContext();
        var tool = (AIFunction)Assert.Single(await resolver.ResolveAsync(dbContext, agentId, CancellationToken.None));

        var (result, _) = await CallAsync(tool, "qualquer coisa");

        Assert.Empty(result.Trechos);
        Assert.NotNull(result.Aviso);
        Assert.Contains("Não foi possível consultar", result.Aviso, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ apoio

    private async Task<IReadOnlyList<AITool>> ResolveAsync(Guid agentId)
    {
        var resolver = BuildResolver(new FakeEmbeddingGeneratorResolver(Vectors.Of(0.0)));
        await using var dbContext = CreateDbContext();
        return await resolver.ResolveAsync(dbContext, agentId, CancellationToken.None);
    }

    private async Task<(KnowledgeSearchToolResult Result, string Json)> InvokeRawAsync(
        Guid agentId, string consulta, float[] queryVector, string? toolName = null, EmittedSqlCapture? capture = null)
    {
        var resolver = BuildResolver(new FakeEmbeddingGeneratorResolver(queryVector), capture);
        await using var dbContext = CreateDbContext();
        var tools = await resolver.ResolveAsync(dbContext, agentId, CancellationToken.None);
        var tool = (AIFunction)(toolName is null ? tools.Single() : tools.Single(t => t.Name == toolName));
        return await CallAsync(tool, consulta);
    }

    /// <summary>
    /// O par "sem item" da convenção 5 no lado da <b>coleta de embedding</b>
    /// (change <c>metricas-embedding-coleta</c>): <b>fora</b> de qualquer
    /// execução de task, registrar é no-op — e a busca <b>não falha</b> por
    /// isso. Mesma regra que <c>RecordProviderCall</c> da etapa 1 já segue.
    /// </summary>
    [Fact]
    public async Task Invoke_OutsideAnyExecution_WritesNoEmbeddingCall_AndDoesNotFail()
    {
        var agentId = Guid.NewGuid();
        var baseId = await SeedBaseAsync("Cobrança", "Prazos e descontos.");
        var documentId = await SeedDocumentAsync(baseId, "Política");
        await SeedFragmentAsync(baseId, documentId, 0, "Carência de onze dias úteis.", Vectors.Of(0.0));
        await SeedAgentAsync(agentId);
        await SeedLinkAsync(agentId, baseId);

        // PRECONDIÇÃO AFIRMADA, e não suposta pelo arranjo: não estamos dentro
        // de execução nenhuma. Sem ela o cenário passaria por vacuidade se o
        // escopo estivesse aberto por outro teste da mesma coleção.
        Assert.Null(ExecutionMetricsScope.Current);

        var before = await CountEmbeddingCallsAsync();

        var result = await InvokeAsync(agentId, "prazo de carência", Vectors.Of(0.0));

        // A busca FUNCIONOU — não é o caso de "não gravou porque nem rodou".
        Assert.NotEmpty(result.Trechos);

        Assert.Equal(before, await CountEmbeddingCallsAsync());
    }

    private async Task<long> CountEmbeddingCallsAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.EmbeddingCalls.CountAsync();
    }

    private async Task<KnowledgeSearchToolResult> InvokeAsync(
        Guid agentId, string consulta, float[] queryVector, string? toolName = null, EmittedSqlCapture? capture = null) =>
        (await InvokeRawAsync(agentId, consulta, queryVector, toolName, capture)).Result;

    /// <summary>
    /// Invoca a tool pelo mesmo caminho que o <c>FunctionInvokingChatClient</c>
    /// usa e devolve <b>as duas formas</b>: o objeto tipado, para as asserções de
    /// conteúdo, e o <b>JSON cru</b>, que é o artefato que o modelo de fato
    /// recebe — e é sobre ele que a asserção negativa afirma (convenção 11).
    ///
    /// <para>
    /// A desserialização usa <see cref="System.Text.Json.JsonSerializerDefaults.Web"/>
    /// porque é o que <c>AIFunctionFactory</c> usa para serializar o retorno:
    /// com as opções default (sensíveis a caixa) as propriedades voltam nulas e
    /// o teste falha por artefato do apoio, não por defeito do código — foi o
    /// que aconteceu na primeira execução destes testes.
    /// </para>
    /// </summary>
    private static async Task<(KnowledgeSearchToolResult Result, string Json)> CallAsync(AIFunction tool, string consulta)
    {
        var raw = await tool.InvokeAsync(new AIFunctionArguments { ["consulta"] = consulta });

        var json = raw switch
        {
            null => "null",
            System.Text.Json.JsonElement element => element.GetRawText(),
            _ => System.Text.Json.JsonSerializer.Serialize(raw, WebJson),
        };

        return (System.Text.Json.JsonSerializer.Deserialize<KnowledgeSearchToolResult>(json, WebJson)!, json);
    }

    private static readonly System.Text.Json.JsonSerializerOptions WebJson =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    private KnowledgeToolSetResolver BuildResolver(
        Buteco.Workers.Knowledge.Embedding.IEmbeddingGeneratorResolver embeddings, EmittedSqlCapture? capture = null)
    {
        var services = new ServiceCollection();
        if (capture is not null)
        {
            // O sink precisa estar no MESMO container que constrói o DbContext
            // da invocação — o resolvedor abre escopo próprio a partir deste
            // IServiceScopeFactory (ver o comentário de SearchAsync), então
            // logar por fora não capturaria nada.
            services.AddLogging(logging => logging.AddProvider(capture).SetMinimumLevel(LogLevel.Information));
        }

        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()));
        var provider = services.BuildServiceProvider();

        // EmbeddingOptions entrou no construtor na change
        // metricas-embedding-coleta: a linha de embedding_calls grava provedor,
        // modelo e dimensão em snapshot, e o resolvedor não tinha essa
        // informação. Este é o ÚNICO sítio de construção manual — todo o resto
        // passa pela interface registrada em DI.
        return new KnowledgeToolSetResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            embeddings,
            Microsoft.Extensions.Options.Options.Create(new EmbeddingOptions
            {
                Provider = "openai",
                Model = "modelo-de-teste",
                Dimensions = Dimensions,
            }),
            NullLogger<KnowledgeToolSetResolver>.Instance);
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task SeedAgentAsync(Guid agentId)
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {"Agente de teste"}, {"Instruções."}, {true}, {"openai"}, {"gpt-5.6-sol"}, {now}, {now})
             """);
    }

    private async Task<Guid> SeedBaseAsync(string name, string description, bool isActive = true)
    {
        var id = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO knowledge_bases ("Id", "Name", "Description", "IsActive", "CreatedAt", "UpdatedAt")
             VALUES ({id}, {name}, {description}, {isActive}, {now}, {now})
             """);
        return id;
    }

    private async Task<Guid> SeedDocumentAsync(Guid knowledgeBaseId, string title)
    {
        var id = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO knowledge_documents
               ("Id", "KnowledgeBaseId", "Title", "SourceType", "ExtractedText", "IndexingStatus",
                "ContentRevision", "FragmentCount", "IndexingAttempts", "CreatedAt", "UpdatedAt")
             VALUES ({id}, {knowledgeBaseId}, {title}, {"markdown"}, {"texto extraído"}, {0}, {1}, {0}, {0}, {now}, {now})
             """);
        return id;
    }

    private async Task SeedFragmentAsync(Guid knowledgeBaseId, Guid documentId, int ordinal, string text, float[] embedding)
    {
        await using var dbContext = CreateDbContext();
        var vector = new Pgvector.Vector(embedding);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO knowledge_fragments
               ("Id", "KnowledgeDocumentId", "KnowledgeBaseId", "Ordinal", "Text", "Embedding",
                "EmbeddingProvider", "EmbeddingModel", "EmbeddingDimensions", "CreatedAt")
             VALUES ({Guid.NewGuid()}, {documentId}, {knowledgeBaseId}, {ordinal}, {text}, {vector},
                     {"openai"}, {"qwen-qwen3-embedding-8b"}, {Dimensions}, {DateTimeOffset.UtcNow})
             """);
    }

    private async Task SeedLinkAsync(Guid agentId, Guid knowledgeBaseId)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agent_knowledge_bases ("AgentId", "KnowledgeBaseId")
             VALUES ({agentId}, {knowledgeBaseId})
             """);
    }

    /// <summary>
    /// Vetores determinísticos de <see cref="Dimensions"/> posições: o primeiro
    /// eixo carrega o valor e o resto é zero, então a distância de cosseno entre
    /// dois vetores é previsível e as asserções não dependem de chamada de rede.
    /// </summary>
    private static class Vectors
    {
        public static float[] Of(double primeiroEixo)
        {
            var v = new float[Dimensions];
            v[0] = 1f;
            v[1] = (float)primeiroEixo;
            return v;
        }
    }
}
