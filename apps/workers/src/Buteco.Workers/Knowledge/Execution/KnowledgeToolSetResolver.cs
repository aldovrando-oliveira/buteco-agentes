using System.ComponentModel;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Embedding;
using Buteco.Workers.Mcp;
using Buteco.Workers.Naming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Buteco.Workers.Knowledge.Execution;

public sealed class KnowledgeToolSetResolver(
    IServiceScopeFactory scopeFactory,
    IEmbeddingGeneratorResolver embeddingResolver,
    ILogger<KnowledgeToolSetResolver> logger) : IKnowledgeToolSetResolver
{
    private const string ToolNamePrefix = "search_";

    /// <summary>
    /// Quantos trechos a busca devolve.
    ///
    /// <para>
    /// <b>5 por regra declarada antes de medir</b>, na etapa <c>0c</c>: <c>k = 3</c>
    /// só valeria se R@3 fosse ≥ 95% de R@5, e mediu-se 71,1/77,1 = <b>92,2%</b>.
    /// O custo está registrado junto e é real: os dois resultados a mais compram
    /// ~6 pontos de recall por <b>67% mais contexto em toda chamada de tool</b> —
    /// medido no índice real, 1.146 caracteres por trecho em média, ~5,2 KB por
    /// chamada, ~1.400-1.600 tokens.
    /// </para>
    ///
    /// <para>
    /// Constante nomeada e não <c>Options</c>, mesmo idioma de
    /// <c>KnowledgeChunker</c> e de <c>AgentDelegationToolOptions</c>: pela
    /// convenção 2, a opção de configuração nasce no dia em que alguém precisar
    /// de outro valor. <b>Gatilho para revisitar:</b> log real de uso, que é o
    /// que <c>0c</c> pediu para esta etapa.
    /// </para>
    /// </summary>
    private const int ResultCount = 5;

    public async Task<IReadOnlyList<AITool>> ResolveAsync(
        AppDbContext dbContext, Guid agentId, CancellationToken cancellationToken)
    {
        // `where knowledgeBase.IsActive` — idioma de McpToolSetResolver, e NÃO o
        // de AgentDelegationToolSetResolver, que relê o Target fresco do banco a
        // cada invocação. O motivo é verificável e não estilístico (design.md,
        // D6): a delegação relê porque WaitForTerminalStateAsync pode segurar a
        // tool por MINUTOS, e uma desativação no meio disso é cenário real. Aqui
        // a janela entre resolver o conjunto e invocar a tool é a mesma
        // RunAsync — segundos. Reler seria uma consulta a mais por invocação
        // para cobrir uma janela que não existe.
        //
        // Decisão herdada da etapa 3, que a declarou explicitamente fora da sua
        // spec: "o estado inativo da base afeta apenas o momento em que o
        // conhecimento é oferecido ao agente em execução".
        //
        // `orderby` por Id e não por Name pelo mesmo motivo da Decisão 8 de
        // dedupe-global-nome-de-tool: o nome é editável, e renomear uma base não
        // deve reordenar o conjunto nem mudar qual tool o deduplicador desempata.
        var bindings = await (
            from binding in dbContext.AgentKnowledgeBases.AsNoTracking()
            join knowledgeBase in dbContext.KnowledgeBases.AsNoTracking()
                on binding.KnowledgeBaseId equals knowledgeBase.Id
            where binding.AgentId == agentId && knowledgeBase.IsActive
            orderby binding.KnowledgeBaseId
            select new { knowledgeBase.Id, knowledgeBase.Name, knowledgeBase.Description }
        ).ToListAsync(cancellationToken);

        // Sem dedupe local: quem garante unicidade é o ToolNameDeduplicator, no
        // ponto que une os TRÊS conjuntos (design.md, D8). Manter um dedupe aqui
        // seria dois mecanismos para a mesma invariante — e foi exatamente o
        // defeito que dedupe-global-nome-de-tool corrigiu no resolvedor de
        // delegação, onde o sufixo local estourava os 64 caracteres.
        var tools = new List<AITool>(bindings.Count);

        foreach (var binding in bindings)
        {
            var toolName = ToolNameSanitizer.Sanitize($"{ToolNamePrefix}{ToolNameSlugifier.Slugify(binding.Name)}");
            tools.Add(BuildSearchTool(toolName, agentId, binding.Id, binding.Name, binding.Description));
        }

        return tools;
    }

    private AITool BuildSearchTool(
        string toolName, Guid agentId, Guid knowledgeBaseId, string knowledgeBaseName, string knowledgeBaseDescription)
    {
        async Task<KnowledgeSearchToolResult> InvokeSearchAsync(
            [Description("O que buscar na base de conhecimento.")] string consulta,
            CancellationToken cancellationToken) =>
            await SearchAsync(agentId, knowledgeBaseId, knowledgeBaseName, consulta, cancellationToken);

        return AIFunctionFactory.Create(
            (Func<string, CancellationToken, Task<KnowledgeSearchToolResult>>)InvokeSearchAsync,
            name: toolName,
            description: KnowledgeToolDescription.Build(knowledgeBaseName, knowledgeBaseDescription));
    }

    private async Task<KnowledgeSearchToolResult> SearchAsync(
        Guid agentId, Guid knowledgeBaseId, string knowledgeBaseName, string consulta, CancellationToken cancellationToken)
    {
        try
        {
            // ESCOPO PRÓPRIO, e não o AppDbContext de quem resolveu o conjunto.
            // Não é simetria com a delegação: é porque FunctionInvokingChatClient
            // pode invocar VÁRIAS tool calls do mesmo turno em paralelo, e
            // DbContext não é thread-safe. Um agente com duas bases vinculadas é
            // exatamente o caso em que o modelo chama as duas de uma vez.
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Construído por invocação e NÃO descartado — mesmo tratamento de
            // KnowledgeIndexingService. Seguro no caminho `openai` porque
            // System.ClientModel serve o transporte de
            // HttpClientPipelineTransport.Shared, com um HttpClient estático por
            // processo (verificado por decompilação, C4 da change
            // fix-vazamento-httpclient-chat). O gatilho para quando isso deixar
            // de valer está escrito no `default:` de EmbeddingGeneratorResolver,
            // que é onde alguém acrescentaria o segundo provedor.
            //
            // Resolvido AQUI, dentro da invocação, e não na montagem do conjunto
            // (design.md, D9): montar gastaria uma construção por mensagem mesmo
            // quando o modelo não chama tool nenhuma, que é o caso comum.
            var generator = embeddingResolver.Resolve();
            var queryVector = new Vector(await generator.GenerateVectorAsync(consulta, cancellationToken: cancellationToken));

            var results = await (
                from fragment in dbContext.KnowledgeFragments.AsNoTracking()
                join document in dbContext.KnowledgeDocuments.AsNoTracking()
                    on fragment.KnowledgeDocumentId equals document.Id
                where fragment.KnowledgeBaseId == knowledgeBaseId
                orderby fragment.Embedding.CosineDistance(queryVector)
                select new KnowledgeSearchResult(
                    document.Title,
                    fragment.Text,
                    Math.Round(fragment.Embedding.CosineDistance(queryVector), 4))
            ).Take(ResultCount).ToListAsync(cancellationToken);

            if (results.Count > 0)
            {
                return new KnowledgeSearchToolResult(results);
            }

            // SEM LIMIAR, um índice povoado devolve SEMPRE min(k, n). Então
            // lista vazia significa "esta base não tem fragmento nenhum", e
            // nunca "não achei nada relevante" — os dois estados são
            // distinguíveis, e conflatá-los seria o sistema afirmando ter
            // procurado onde não havia onde procurar (design.md, D5; convenção
            // 13 na direção forte).
            logger.LogInformation(
                "Agente {AgentId} consultou a base de conhecimento {KnowledgeBaseId} ({KnowledgeBaseName}), que não tem nenhum fragmento indexado.",
                agentId, knowledgeBaseId, knowledgeBaseName);

            return new KnowledgeSearchToolResult(
                [],
                $"A base '{knowledgeBaseName}' ainda não tem conteúdo indexado. Nenhuma busca foi realizada nela — "
              + "isto não significa que o assunto não esteja coberto, significa que não há o que consultar aqui.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Degradação graciosa (convenção 4): o resultado de falha volta para
            // o modelo, a task do agente NÃO falha por isso. Mesmo idioma de
            // AgentDelegationToolSetResolver.DelegateToTargetAsync.
            logger.LogWarning(
                exception,
                "Busca na base de conhecimento {KnowledgeBaseId} ({KnowledgeBaseName}) falhou para o agente {AgentId}.",
                knowledgeBaseId, knowledgeBaseName, agentId);

            return new KnowledgeSearchToolResult(
                [],
                $"Não foi possível consultar a base '{knowledgeBaseName}' no momento.");
        }
    }
}
