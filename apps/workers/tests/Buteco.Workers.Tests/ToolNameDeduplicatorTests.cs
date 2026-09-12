using Buteco.Workers.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre o dedupe global de nomes de tool da change dedupe-global-nome-de-tool.
/// </summary>
/// <remarks>
/// Este arquivo nasceu com um teste só — a reprodução do defeito de estouro de
/// 64 caracteres que o dedupe local de <c>AgentDelegationToolSetResolver</c>
/// tem hoje (design.md, V2) —, escrito e visto passar ANTES de
/// <see cref="ToolNameDeduplicator"/> existir. Os testes do deduplicador em si
/// vieram depois, no grupo 2 das tarefas: um guarda que não pode reprovar contra
/// o código de hoje (porque o tipo que ele testa não compila ainda) não é guarda
/// de convenção 15, e por isso a verificação dele é outra — reintroduzir o
/// defeito no deduplicador de propósito, já com ele no lugar.
/// </remarks>
public class ToolNameDeduplicatorTests
{
    private static ToolNameDeduplicator Deduplicator() =>
        new(NullLogger<ToolNameDeduplicator>.Instance);

    private static AITool Named(string name) =>
        AIFunctionFactory.Create(() => "ok", name: name);

    private static List<string> NamesOf(IReadOnlyList<AITool> tools) =>
        tools.Select(tool => tool.Name).ToList();

    [Fact]
    public void Deduplicate_NoCollision_PreservesEveryNameAndOrder()
    {
        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [Named("Reservas__search"), Named("Reservas__create")],
            [Named("delegate_to_financeiro")],
            [Named("search_politicas")]);

        Assert.Equal(
            new[] { "Reservas__search", "Reservas__create", "delegate_to_financeiro", "search_politicas" },
            NamesOf(result));
    }

    [Fact]
    public void Deduplicate_CollisionBetweenSets_RenamesTheDelegationToolAndKeepsBoth()
    {
        // Decisão 5: MCP mantém o nome pretendido, delegação é a renomeada.
        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [Named("shared_name")],
            [Named("shared_name")],
            []);

        Assert.Equal(new[] { "shared_name", "shared_name-2" }, NamesOf(result));
    }

    [Fact]
    public void Deduplicate_CollisionWithinTheMcpSet_RenamesTheLaterOne()
    {
        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [Named("Zendesk_MCP__search"), Named("Zendesk_MCP__search"), Named("Zendesk_MCP__search")],
            [],
            []);

        Assert.Equal(
            new[] { "Zendesk_MCP__search", "Zendesk_MCP__search-2", "Zendesk_MCP__search-3" },
            NamesOf(result));
    }

    [Fact]
    public void Deduplicate_NamesDifferingOnlyInCase_AreNotTreatedAsCollision()
    {
        // Decisão 9. A fonte do critério é
        // FunctionInvokingChatClient.FindTool, que casa com
        // string.Equals(..., StringComparison.Ordinal) — sensível a caixa. Com
        // OrdinalIgnoreCase (ou com o HashSet<string>/Dictionary default
        // implícito) este teste reprova, e o dano seria renomear tool de agente
        // que estava correto.
        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [Named("Search"), Named("search")],
            [],
            []);

        Assert.Equal(new[] { "Search", "search" }, NamesOf(result));
    }

    [Fact]
    public void Deduplicate_CollisionOnANameAlreadyAtTheLimit_KeepsEveryNameWithinTheLimit()
    {
        // A correção do defeito reproduzido no teste abaixo: a base encurta para
        // o sufixo caber, em vez de concatenar e estourar (Decisão 4).
        var atLimit = ToolNameSanitizer.Sanitize($"delegate_to_{new string('a', 52)}");
        Assert.Equal(ToolNameSanitizer.MaxToolNameLength, atLimit.Length);

        var result = Deduplicator().Deduplicate(Guid.NewGuid(), [Named(atLimit)], [Named(atLimit)], []);

        var names = NamesOf(result);
        Assert.Equal(2, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.True(
            name.Length <= ToolNameSanitizer.MaxToolNameLength,
            $"Nome '{name}' tem {name.Length} caracteres."));
        Assert.Equal(atLimit, names[0]);
    }

    [Fact]
    public void Deduplicate_ThreeCollidingNamesAtTheLimit_ProducesThreeDistinctNamesWithinTheLimit()
    {
        // Contraparte de R7 na tabela de Risks... na verdade de R2: o
        // encurtamento acontece três vezes e o loop precisa fechar em nomes
        // distintos, todos <= 64. É o caso em que um encurtamento poderia
        // recriar uma colisão já resolvida.
        var atLimit = ToolNameSanitizer.Sanitize($"delegate_to_{new string('a', 52)}");

        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [Named(atLimit), Named(atLimit), Named(atLimit)],
            [],
            []);

        var names = NamesOf(result);
        Assert.Equal(3, names.Count);
        Assert.Equal(3, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.True(name.Length <= ToolNameSanitizer.MaxToolNameLength, $"'{name}' = {name.Length}"));
    }

    [Fact]
    public void Deduplicate_RenamedDelegationTool_StillInvokesTheOriginalImplementation()
    {
        // O ponto de V6: renomear encapsula, não reconstrói — o closure da
        // função original continua vivo dentro do wrapper, e o schema vem do
        // inner. Uma tool renomeada que perde a invocação ou o schema é pior que
        // uma sombreada.
        var invoked = false;
        var original = AIFunctionFactory.Create(
            () => { invoked = true; return "resultado"; },
            name: "shared_name",
            description: "descrição original");

        var result = Deduplicator().Deduplicate(Guid.NewGuid(), [Named("shared_name")], [original], []);

        var renamed = Assert.IsAssignableFrom<AIFunction>(result[1]);
        Assert.Equal("shared_name-2", renamed.Name);
        Assert.Equal("descrição original", renamed.Description);
        Assert.Equal(((AIFunction)original).JsonSchema.ToString(), renamed.JsonSchema.ToString());

        renamed.InvokeAsync(new AIFunctionArguments(), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        Assert.True(invoked, "A tool renomeada não chamou a implementação original.");
    }

    [Fact]
    public void CurrentDelegationSuffixIdiom_OnANameAlreadyAtTheLimit_OverflowsTheProviderLimit()
    {
        // Reprodução do defeito real, com a expressão exata de
        // AgentDelegationToolSetResolver.cs:53 antes desta change:
        //     toolName = $"{baseName}-{suffix}";
        // Sem re-truncagem, um baseName já nos 64 produz 66 — acima do limite de
        // FunctionObject.name do OpenAI (design.md, V4). Alcançável com dois
        // Targets de Agent.Name colidente e longo: "delegate_to_" consome 12 dos
        // 64, então basta um slug de 52 caracteres.
        var baseName = ToolNameSanitizer.Sanitize($"delegate_to_{new string('a', 52)}");
        Assert.Equal(ToolNameSanitizer.MaxToolNameLength, baseName.Length);

        var withSuffix = $"{baseName}-2";

        Assert.Equal(66, withSuffix.Length);
        Assert.True(
            withSuffix.Length > ToolNameSanitizer.MaxToolNameLength,
            "Se esta asserção falhar, o defeito de estouro não existe mais e este teste perdeu o objeto.");
    }

    // ------------------- terceiro conjunto (change knowledge-tool-resolver) ----

    /// <summary>
    /// GUARDA NO COMPONENTE QUE A CORREÇÃO TOCA (convenção 15, segunda forma).
    /// A etapa 4 acrescenta um terceiro conjunto ao mesmo espaço de nome, e a
    /// unicidade continua sendo global — não do resolvedor novo. Remover o
    /// terceiro <c>AppendAll</c> do deduplicador faz estes testes reprovarem;
    /// um guarda escrito dentro de <c>KnowledgeToolSetResolver</c> não
    /// reprovaria, e foi exatamente esse o erro que `0a` cometeu três vezes.
    /// </summary>
    [Fact]
    public void Deduplicate_CollisionBetweenMcpAndKnowledge_RenamesTheKnowledgeTool()
    {
        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [Named("search__cobranca")],
            [],
            [Named("search__cobranca")]);

        Assert.Equal(new[] { "search__cobranca", "search__cobranca-2" }, NamesOf(result));
    }

    [Fact]
    public void Deduplicate_CollisionBetweenDelegationAndKnowledge_RenamesTheKnowledgeTool()
    {
        // Precedência estendida: conhecimento é o mais fraco dos três, então
        // perde também para delegação.
        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [],
            [Named("shared_name")],
            [Named("shared_name")]);

        Assert.Equal(new[] { "shared_name", "shared_name-2" }, NamesOf(result));
    }

    [Fact]
    public void Deduplicate_CollisionWithinTheKnowledgeSet_RenamesTheLaterOne()
    {
        // Duas bases cujos nomes slugificam igual — "Informações Gerais" e
        // "Informacoes Gerais" são o caso real.
        var result = Deduplicator().Deduplicate(
            Guid.NewGuid(),
            [],
            [],
            [Named("search_informacoes-gerais"), Named("search_informacoes-gerais")]);

        Assert.Equal(
            new[] { "search_informacoes-gerais", "search_informacoes-gerais-2" },
            NamesOf(result));
    }

    /// <summary>
    /// O aviso de renomeação nomeia o terceiro conjunto. Sem isto, o operador vê
    /// "cadastrei a base e o agente chama outra tool" sem rastro que o explique
    /// — que é a razão de <c>ToolOrigin</c> existir.
    /// </summary>
    [Fact]
    public void Deduplicate_RenamedKnowledgeTool_LogsTheKnowledgeOrigin()
    {
        var logger = new CapturingLogger();
        var deduplicator = new ToolNameDeduplicator(logger);

        deduplicator.Deduplicate(
            Guid.NewGuid(),
            [Named("shared_name")],
            [],
            [Named("shared_name")]);

        var aviso = Assert.Single(logger.Warnings);
        Assert.Contains(nameof(ToolOrigin.Knowledge), aviso, StringComparison.Ordinal);
        Assert.Contains(nameof(ToolOrigin.Mcp), aviso, StringComparison.Ordinal);
        Assert.Contains("shared_name-2", aviso, StringComparison.Ordinal);
    }

    [Fact]
    public void Deduplicate_NoCollisionAcrossTheThreeSets_LogsNothing()
    {
        var logger = new CapturingLogger();
        var deduplicator = new ToolNameDeduplicator(logger);

        deduplicator.Deduplicate(
            Guid.NewGuid(),
            [Named("Reservas__search")],
            [Named("delegate_to_financeiro")],
            [Named("search_politicas")]);

        Assert.Empty(logger.Warnings);
    }

    private sealed class CapturingLogger : ILogger<ToolNameDeduplicator>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

}
