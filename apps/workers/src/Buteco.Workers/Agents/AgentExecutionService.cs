using System.Globalization;
using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.AgentDelegations;
using Buteco.Workers.ExecutionMetrics;
using Buteco.Workers.ExecutionMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Knowledge.Execution;
using Buteco.Workers.Mcp;
using Buteco.Workers.Messaging;
using Buteco.Workers.Notifications;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Agents;

public sealed class AgentExecutionService(
    IServiceScopeFactory scopeFactory,
    IChatClientResolver chatClientResolver,
    IMcpToolSetResolver mcpToolSetResolver,
    IAgentDelegationToolSetResolver delegationToolSetResolver,
    IKnowledgeToolSetResolver knowledgeToolSetResolver,
    ToolNameDeduplicator toolNameDeduplicator,
    PushNotificationSender pushNotificationSender,
    TimeProvider timeProvider,
    ILogger<AgentExecutionService> logger,
    // O CompactionProvider precisa de um ILoggerFactory para que a falha da
    // chamada de resumo apareça (change compactacao-historico, D3) — ver o
    // comentário na composição dele, abaixo.
    //
    // ESTE PARÂMETRO NÃO CUSTA OS 14 HARNESS DE TESTE, e o contraste com a D2 da
    // change metricas-execucao-coleta é deliberado: lá, injetar um COLETOR por
    // construtor foi recusado porque quebraria em runtime nos harness que
    // registram este serviço sem registrar o coletor. ILoggerFactory sai do
    // MESMO registro que já serve o ILogger<AgentExecutionService> acima — todo
    // harness constrói o host por Host.CreateApplicationBuilder(), que registra
    // logging. Se algum dia um harness montar ServiceCollection cru, a
    // resolução falha e este comentário é o primeiro lugar a olhar.
    ILoggerFactory loggerFactory)
{
    // Teto de segurança, não um limiar concorrente com
    // SummarizationTurnThreshold — ver design.md da change
    // apps-workers-resumo-historico-conversa, Decisão 9. Decompilando
    // CompactionMessageIndex.Update, confirmou-se que RecentMessageChatReducer
    // truncando ativamente invalida o bookkeeping incremental do
    // CompactionProvider a partir do momento em que a conversa ultrapassa
    // esse valor — por isso ele precisa ficar bem acima da faixa de operação
    // normal do resumo (SummarizationTurnThreshold), não próximo dela.
    // Constante global, não configurável por agente (non-goal explícito —
    // ver design.md da change apps-workers-historico-conversa,
    // Goals/Non-Goals e Decisão 4). Ajustar aqui depois de observar
    // custo/latência real (ver Open Questions dos dois design.md).
    private const int MaxHistoryMessages = 200;

    // Limiar de interações (turnos de usuário) que dispara o resumo do
    // histórico — constante global, não configurável por agente (non-goal
    // explícito, ver design.md da change apps-workers-resumo-historico-conversa,
    // Goals/Non-Goals). Precisa ficar maior que MinimumPreservedGroups do
    // SummarizationCompactionStrategy (padrão do pacote: 8), senão não há
    // conteúdo elegível para resumir no primeiro gatilho — ver Open
    // Questions daquele design.md.
    private const int SummarizationTurnThreshold = 10;

    private const string ConversationSessionMetadataKey = "conversationSession";

    // Chaves de contexto de canal (design.md da change inbox-contexto-canal,
    // D3/D4) — mesmo nome usado do lado da escrita em apps/inbox
    // (DebounceSweepService), duplicado deliberadamente (apps isolados sem
    // ProjectReference cruzado). Sem classe Codec dedicada: ao contrário de
    // DelegationDepth/ConversationSessionCodec/MessageInstantCodec, não há
    // parsing a encapsular — os dois valores são strings cruas (D4).
    private const string ChannelTypeMetadataKey = "channelType";
    private const string ContactExternalIdMetadataKey = "contactExternalId";

    // Teto de saltos de delegação numa mesma cadeia desde a mensagem
    // original — constante global, não configurável por agente (non-goal
    // explícito, ver design.md da change apps-workers-delegacao-execucao,
    // Decision 6), mesmo estilo de MaxHistoryMessages/SummarizationTurnThreshold
    // acima. Cada nível a mais na cadeia ocupa mais uma instância de
    // apps/workers presa em espera (ver Decision 1 daquele design.md).
    private const int DelegationDepthLimit = 5;

    // Ver design.md, Decisão 9: absorve a corrida entre o publish no RabbitMQ
    // e o commit do SaveTaskAsync do evento "submitted" em apps/api.
    private const int GetTaskMaxAttempts = 5;
    private static readonly TimeSpan GetTaskRetryDelay = TimeSpan.FromMilliseconds(100);

    public async Task ExecuteAsync(TaskJobMessage message, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agent = await dbContext.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == message.AgentId, cancellationToken);

        if (agent is null)
        {
            logger.LogError("Agente {AgentId} não encontrado para a task {TaskId}", message.AgentId, message.TaskId);
            return;
        }

        var taskStore = new PostgresTaskStore(scopeFactory, message.AgentId);

        // Retry curto: EnqueueingAgentHandler grava a task em DOIS eventos
        // separados — SubmitAsync() cria a linha (History nulo), e só depois
        // (após uma leitura de estado do agente) EnqueueMessageAsync grava a
        // mensagem do usuário nela — enquanto, concorrentemente, publica no
        // RabbitMQ. Nenhuma dessas escritas espera a outra nem o publish. Um
        // worker pode consumir a mensagem e achar a task já criada mas ainda
        // sem a mensagem do usuário em History. Por isso o retry exige task
        // encontrada E com uma mensagem de usuário utilizável, não só task
        // encontrada. Ver design.md da change apps-workers-historico-conversa,
        // Decisão 9 (achado via teste manual, não causado por esta change,
        // corrigido aqui por decisão explícita — non-goal original não se
        // aplica a este fix porque ele não toca apps/api).
        var task = await GetTaskWithRetryAsync(taskStore, message.TaskId, cancellationToken);
        if (task is null)
        {
            logger.LogError("Task {TaskId} não encontrada no store (ou sem mensagem do usuário) após retries", message.TaskId);
            return;
        }

        // Task JÁ TERMINAL não é executada de novo (design.md da change
        // metricas-execucao-coleta, D17). Chega aqui quando o RabbitMQ REENTREGA a
        // mensagem: o worker parou entre gravar o estado terminal e confirmá-la. Sem
        // esta guarda a task era reexecutada a partir de `completed` — LLM chamado
        // outra vez, estado reescrito, e (medido na suíte) uma delegação inteira
        // refeita com timeout de 120 s. O `return` faz o consumidor confirmar a
        // mensagem, e ela não volta.
        //
        // É SEGURO porque nenhum caminho legítimo entrega task terminal para
        // executar: o A2AServer recusa mensagem para task terminal
        // (GuardTerminalState) e a conversa continua em task NOVA, em `submitted`;
        // a delegação sempre cria task nova. Os dois lados têm guarda. O
        // predicado é o MESMO do protocolo (TaskStateExtensions.IsTerminal), para
        // as duas pontas não divergirem sobre o que é terminal.
        //
        // `working` NÃO entra: reentrega de task em `working` é recuperação de
        // worker que morreu no meio, e continua executando (com SubmittedAt nulo,
        // D4).
        //
        // Antes da linha de métrica de propósito: a execução que já tem a sua
        // linha não ganha uma segunda.
        if (task.Status.State.IsTerminal())
        {
            logger.LogWarning(
                "Task {TaskId} já está em estado terminal ({State}) — mensagem reentregue, não executada de novo.",
                message.TaskId,
                task.Status.State);
            return;
        }

        // Métricas de execução (change metricas-execucao-coleta, D3/D4). A linha
        // pai é aberta AQUI, e não depois: é o primeiro ponto em que a task foi
        // lida, e é anterior a TODOS os caminhos que a terminam — a rejeição por
        // profundidade logo abaixo, a falha do lock e o `catch` grande. Uma task
        // que termina sem nenhuma chamada ao provedor tem de aparecer na tela de
        // erros, e só aparece se a linha já existir quando ela terminar.
        //
        // SubmittedAt: é AQUI que o instante do `submitted` ainda existe. A
        // transição para `working` (StartWorkAsync, logo abaixo) o sobrescreve em
        // a2a_tasks, e o payload não guarda histórico de status. Só vale se a task
        // lida estiver em `Submitted`: numa reentrega ela já está em `Working`, e
        // o carimbo seria o do início da tentativa anterior — gravar esse valor
        // afirmaria um tempo de fila que não foi medido (convenção 13), então
        // fica nulo.
        var delegationOrigin = DelegationOrigin.Read(task);
        var execution = new TaskExecution(
            message.TaskId,
            agent.Id,
            message.ContextId,
            agent.Provider,
            agent.Model,
            delegationOrigin is null ? ExecutionMetricsValues.Origin.External : ExecutionMetricsValues.Origin.Delegation,
            delegationOrigin?.SourceAgentId,
            delegationOrigin?.SourceTaskId,
            DelegationDepth.Read(task),
            task.Status.State == TaskState.Submitted ? task.Status.Timestamp : null,
            timeProvider.GetUtcNow());

        using var metrics = ExecutionMetricsScope.Begin(message.TaskId);
        var metricsWriter = new ExecutionMetricsWriter(scopeFactory, logger);
        await metricsWriter.OpenAsync(execution, cancellationToken);

        DateTimeOffset? lockAcquiredAt = null;
        DateTimeOffset? endedAt = null;
        string? terminalState = null;
        string? failurePhase = null;

        // Chamado só DEPOIS de o estado terminal estar gravado — se a gravação
        // lançar, nada é marcado e a linha fica aberta, que é o que ela é.
        void MarkTerminal(TaskState state, string? phase)
        {
            endedAt = timeProvider.GetUtcNow();
            terminalState = state.ToString();
            failurePhase = phase;
        }

        // O fechamento vai num `finally` EXTERNO a tudo, e roda depois do estado
        // terminal e do push notification: não há ordem de execução em que uma
        // falha de métrica mude o que a task terminou sendo (o escritor também
        // não lança). O `await using` do lock continua dentro deste bloco, no
        // MESMO lugar relativo ao `try/catch` da execução — ver o comentário
        // "NÃO MOVER" mais abaixo.
        try
        {
            // Controle de profundidade da cadeia de delegação (design.md,
            // Decisão 6): checado antes de StartWorkAsync/do lock consultivo —
            // uma task além do teto nunca chega a rodar o LLM. Rejeitada (não
            // failed) para não passar pelo tratamento de erro genérico do catch
            // abaixo — mesmo estado terminal já usado para "agente inativo"/
            // "sem provider" em EnqueueingAgentHandler, que a tool de delegação
            // que está esperando (Decisão 8) já trata como qualquer outra
            // falha graciosa, sem código especial para profundidade.
            var delegationDepth = execution.DelegationDepth;
            if (delegationDepth > DelegationDepthLimit)
            {
                logger.LogWarning(
                    "Task {TaskId} excede a profundidade máxima de delegação ({Limit}) — rejeitada.",
                    message.TaskId,
                    DelegationDepthLimit);

                await ApplyStepAsync(
                    taskStore,
                    message.TaskId,
                    message.ContextId,
                    task,
                    updater => updater.RejectAsync(cancellationToken: cancellationToken),
                    cancellationToken);
                MarkTerminal(TaskState.Rejected, ExecutionMetricsValues.FailurePhase.DelegationDepthExceeded);
                return;
            }

            task = await ApplyStepAsync(
                taskStore,
                message.TaskId,
                message.ContextId,
                task,
                updater => updater.StartWorkAsync(cancellationToken: cancellationToken),
                cancellationToken);

            if (task is null)
            {
                logger.LogError("Task {TaskId} ficou nula após transição para working", message.TaskId);
                return;
            }

            // Serializa o processamento de mensagens do mesmo (agentId,
            // contextId) entre instâncias concorrentes do worker — ver
            // design.md, Decisão 7. Cobre a seção crítica inteira: leitura da
            // sessão anterior → RunAsync → escrita da nova.
            //
            // TRATAMENTO PRÓPRIO, SEPARADO DO `catch` GRANDE LÁ EMBAIXO, e não por
            // gosto de simetria — ver design.md da change
            // lock-de-contexto-falha-terminal, D1. A aquisição pode falhar: ela
            // espera pelo lock e essa espera é limitada pelo `CommandTimeout` do
            // Npgsql (30 s por default), então estourar é caminho de operação, não
            // curiosidade. Antes desta change a exceção escapava de `ExecuteAsync`
            // inteiro e o `catch` de TaskJobConsumer descartava a mensagem — a task
            // ficava em `working` PARA SEMPRE, e com ela a PendingDispatch de
            // apps/inbox ficava em `Dispatching` e a mensagem do usuário sumia sem
            // erro nenhum. Medido, não deduzido.
            //
            // NÃO MOVER O `await using` PARA DENTRO DO `try` ABAIXO para "unificar"
            // os dois caminhos. É a correção que parece óbvia e destrói dado:
            // `await using` dentro de um bloco dispara o DisposeAsync ao sair dele,
            // e numa exceção o finally implícito roda ANTES do catch externo. O
            // DisposeAsync solta o lock numa conexão que pode ter morrido, e aí o
            // catch pegaria essa falha DEPOIS de a task já estar gravada como
            // `completed` — e ApplyStepAsync não tem guarda de estado terminal,
            // então regravaria como `failed`, perdendo a resposta do agente junto.
            // UnlockFailingAfterCompletion_LeavesTaskCompleted_WithArtifactPreserved
            // é o guarda que prende isto.
            ConversationContextLock acquiredLock;
            try
            {
                acquiredLock = await ConversationContextLock.AcquireAsync(
                    scopeFactory, message.AgentId, message.ContextId, cancellationToken);
            }
            catch (Exception ex)
            {
                // ContextId no log não é enfeite: é o único campo que liga esta
                // falha à conversa que estava segurando o lock, que é a única coisa
                // acionável aqui.
                logger.LogError(
                    ex,
                    "Falha ao adquirir o lock de contexto do agente {AgentId} no contexto {ContextId} para a task {TaskId}",
                    message.AgentId,
                    message.ContextId,
                    message.TaskId);

                await FailTaskAsync(taskStore, message, task, cancellationToken);
                MarkTerminal(TaskState.Failed, ExecutionMetricsValues.FailurePhase.ContextLock);
                return;
            }

            lockAcquiredAt = timeProvider.GetUtcNow();
            await using var contextLock = acquiredLock;

            // Fase corrente, para o motivo da falha (D12): atualizada ANTES de cada
            // passo, lida no `catch`. Estável por construção — não depende do texto
            // de exceção nenhuma, que é de quem a lança e muda sem aviso.
            var phase = ExecutionMetricsValues.FailurePhase.ChatClientResolution;

            try
            {
                var userText = ExtractLatestUserText(task);
                var messageInstant = ExtractMessageInstant(task, message.TaskId);
                var (channelType, contactExternalId) = ExtractChannelContext(task, message.TaskId);

                // agent.Provider/agent.Model só ficam nulos para um agente "precisa de
                // reconfiguração" — apps/api já rejeita SendMessage nesse caso antes de
                // publicar o job (EnqueueingAgentHandler), então nunca deveriam chegar
                // aqui; o operador nulo-tolerante (!) documenta essa garantia, não a
                // ignora — se ela falhar, o resolver/aiAgent lançará e cairá no catch
                // abaixo, terminando a task como failed (Decision 5).
                // COMPARTILHADO E NÃO DESCARTÁVEL: o resolver devolve a MESMA
                // instância para o mesmo (provider, model) durante toda a vida do
                // processo (change fix-vazamento-httpclient-chat). Não acrescentar
                // `using`/`await using` aqui nem no aiAgent montado abaixo — o
                // contraste com o `await using` do toolSet logo adiante é
                // deliberado, e é justamente a assimetria que engana:
                // DelegatingChatClient.Dispose() descarta o InnerClient em cascata,
                // e ChatClientAgent empilha middleware delegante sobre este client,
                // então um `using` aqui quebraria TODA mensagem seguinte daquele par
                // com ObjectDisposedException. Hoje nada descarta (ChatClientAgent
                // não é IDisposable), e é essa garantia que
                // TaskJobConsumerTests.Consumer_ProcessesTwoTasksInSequence_SharedChatClientIsNeverDisposed
                // prende.
                var chatClient = chatClientResolver.Resolve(agent.Provider!, agent.Model!);

                phase = ExecutionMetricsValues.FailurePhase.ToolResolution;

                // Precisa ficar vivo durante todo o RunAsync abaixo, não só
                // durante a resolução — cada AITool devolvido encapsula uma
                // conexão MCP viva (design.md da change apps-workers-execucao-mcp,
                // Decision 4). await using cobre tanto o caminho de sucesso
                // quanto uma exceção propagando para o catch abaixo.
                await using var toolSet = await mcpToolSetResolver.ResolveAsync(dbContext, message.AgentId, cancellationToken);

                // Sem conexão externa viva por trás (diferente de McpToolSet) —
                // não precisa de await using, ver design.md, Decision 10.
                var delegationTools = await delegationToolSetResolver.ResolveAsync(
                    dbContext, agent, message.TaskId, message.ContextId, delegationDepth, messageInstant, cancellationToken);

                // Terceiro conjunto, e também SEM await using — pelo mesmo motivo da
                // delegação e não por simetria com ela: não há conexão externa viva
                // por trás. A busca usa um escopo próprio aberto dentro da invocação
                // (FunctionInvokingChatClient pode chamar duas tools do mesmo turno
                // em paralelo, e DbContext não é thread-safe), e o gerador de
                // embedding é resolvido lá dentro, não aqui (design.md da change
                // knowledge-tool-resolver, D7/D9).
                //
                // Depois desta change são DOIS resolvedores sem `using` contra UM
                // com — a assimetria virou maioria, e o único `await using` do
                // método é o do toolSet MCP, que tem conexão viva de verdade.
                var knowledgeTools = await knowledgeToolSetResolver.ResolveAsync(
                    dbContext, message.AgentId, cancellationToken);

                // Bloco de contexto temporal concatenado às Instructions do
                // operador — sem tocar Agent.Instructions no banco, sem entrar
                // no histórico de conversa (design.md da change
                // apps-workers-contexto-temporal, Decisões 1 e 2). Instructions
                // vazia (Non-Goal: sem validação de não-vazio em apps/api) não
                // pode deixar separador órfão — usa só o bloco nesse caso.
                var temporalContextBlock = TemporalContextBlockBuilder.Build(timeProvider, messageInstant);
                var instructionsWithTemporalContext = TemporalContextBlockBuilder.Concatenate(agent.Instructions, temporalContextBlock);

                // Bloco de contexto de canal, concatenado depois do bloco
                // temporal — reaproveita TemporalContextBlockBuilder.Concatenate
                // uma segunda vez em vez de estender sua assinatura (design.md,
                // D5). Omitido inteiramente quando os dois campos estão
                // ausentes (Build retorna null) — Concatenate não é chamado
                // nesse caso, preservando as Instructions com só o bloco
                // temporal.
                var channelContextBlock = ChannelContextBlockBuilder.Build(channelType, contactExternalId);
                var instructionsWithContext = channelContextBlock is null
                    ? instructionsWithTemporalContext
                    : TemporalContextBlockBuilder.Concatenate(instructionsWithTemporalContext, channelContextBlock);

                // SEM loggerFactory E SEM services AQUI, DE PROPÓSITO — e o que
                // isso esconde está medido, não suposto (change
                // compactacao-historico, D3 e Open Question 1). Decompilado
                // (Microsoft.Agents.AI 1.15.0): o logger do próprio
                // ChatClientAgent sai do parâmetro `loggerFactory`, mas o
                // middleware que ele empilha por WithDefaultAgentMiddleware —
                // inclusive o FunctionInvokingChatClient, que loga invocação de
                // tool — resolve ILoggerFactory do parâmetro `services`, que é
                // outro. Passar só o primeiro acende as linhas do agente e não
                // as do middleware.
                //
                // Ligar o log do middleware é decisão de VOLUME de log, não de
                // correção: o gatilho registrado é a próxima investigação de
                // tool que dependa de saber qual foi chamada e com quê. O
                // CompactionProvider abaixo recebe o loggerFactory porque ali
                // havia um defeito — falha engolida em silêncio —, e aqui não.
                var aiAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
                {
                    Name = agent.Name,
                    // Dedupe global no ponto de concatenação (design.md da change
                    // dedupe-global-nome-de-tool, Decisão 1): é o único ponto que
                    // sabe que os dois conjuntos dividem espaço de nome. Antes era
                    // `toolSet.Tools.Concat(delegationTools)` cru, e um nome
                    // duplicado era sombreado em silêncio por
                    // FunctionInvokingChatClient.FindTool. A ordem dos argumentos é
                    // a precedência declarada (Decisão 5, estendida em
                    // knowledge-tool-resolver D8): MCP mantém o nome, a tool de
                    // delegação é renomeada contra MCP, e a de conhecimento é
                    // renomeada contra as duas.
                    ChatOptions = new ChatOptions
                    {
                        Instructions = instructionsWithContext,
                        Tools = [.. toolNameDeduplicator.Deduplicate(message.AgentId, toolSet.Tools, delegationTools, knowledgeTools)],
                    },
                    ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
                    {
                        ChatReducer = new RecentMessageChatReducer(MaxHistoryMessages),
                    }),
                    // Compaction (Microsoft.Agents.AI.Compaction) é
                    // [Experimental("MAAI001")] — risco aceito, ver design.md da
                    // change apps-workers-resumo-historico-conversa, Decisão 1/8.
                    // Reaproveita o mesmo chatClient resolvido acima para a
                    // chamada de resumo — sem client dedicado (Decisão 4).
    #pragma warning disable MAAI001
                    AIContextProviders = new AIContextProvider[]
                    {
                        // CompactionCallChatClient marca a chamada de resumo como
                        // `Compaction` nas métricas (change
                        // metricas-execucao-coleta, D8) — o client por baixo é o
                        // MESMO compartilhado, e o marcador não é dono dele. Ele
                        // também garante que a requisição de resumo não termine
                        // em turno de modelo (change compactacao-historico, D1).
                        //
                        // O loggerFactory NÃO É ENFEITE: sem ele o provider cai
                        // em NullLoggerFactory (decompilado,
                        // CompactionProvider.GetLoggerFactory), e o aviso que a
                        // estratégia emite ao ENGOLIR uma falha de resumo
                        // (CompactCoreAsync captura, restaura os grupos e segue)
                        // não chega a lugar nenhum. Foi esse silêncio que fez a
                        // compactação ficar quebrada contra o Gemini sem ninguém
                        // ver, do primeiro deploy até o piloto.
                        new CompactionProvider(
                            new SummarizationCompactionStrategy(
                                new CompactionCallChatClient(chatClient),
                                CompactionTriggers.TurnsExceed(SummarizationTurnThreshold)),
                            stateKey: null,
                            loggerFactory: loggerFactory),
                    },
    #pragma warning restore MAAI001
                });

                phase = ExecutionMetricsValues.FailurePhase.SessionLoad;
                var session = await LoadSessionAsync(aiAgent, taskStore, message.ContextId, cancellationToken);

                phase = ExecutionMetricsValues.FailurePhase.AgentRun;
                var response = await aiAgent.RunAsync(userText, session, options: null, cancellationToken);

                phase = ExecutionMetricsValues.FailurePhase.Persistence;

                // Serializado antes do SaveTaskAsync final (não no catch — ver
                // design.md, Decisões 1 e 6): se DeserializeSessionAsync tivesse
                // falhado antes, ou se RunAsync lançar, cai no catch abaixo sem
                // nunca chegar aqui, e a última sessão persistida (de uma task
                // completed anterior) permanece intacta.
                var serializedSession = await aiAgent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
                var conversationSessionValue = ConversationSessionCodec.Encode(serializedSession);

                var parts = new List<Part> { Part.FromText(response.Text) };

                var savedTask = await ApplyStepAsync(
                    taskStore,
                    message.TaskId,
                    message.ContextId,
                    task,
                    async updater =>
                    {
                        await updater.AddArtifactAsync(parts, cancellationToken: cancellationToken);
                        await updater.CompleteAsync(cancellationToken: cancellationToken);
                    },
                    cancellationToken,
                    completedTask => completedTask.Metadata = BuildTerminalMetadata(conversationSessionValue, message.PushNotificationConfig));
                MarkTerminal(TaskState.Completed, phase: null);

                await SendPushNotificationIfConfiguredAsync(message, savedTask, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao executar o agente {AgentId} para a task {TaskId}", message.AgentId, message.TaskId);

                await FailTaskAsync(taskStore, message, task, cancellationToken);
                MarkTerminal(TaskState.Failed, phase);
            }
        }
        finally
        {
            execution.Close(lockAcquiredAt, endedAt, terminalState, failurePhase);
            await metricsWriter.CloseAsync(execution, metrics);
        }
    }

    /// <summary>
    /// Termina a task em <c>failed</c> e dispara a push notification, se houver
    /// — a sequência terminal de falha, compartilhada pelos <b>dois</b> caminhos
    /// que falham: a aquisição do <see cref="ConversationContextLock"/> e a
    /// execução em si.
    /// </summary>
    /// <remarks>
    /// Extraída porque os dois caminhos precisam gravar o mesmo estado terminal
    /// e disparar a mesma notificação, e duplicar isso é convidar os dois a
    /// divergirem. <b>O que NÃO é compartilhado é o log</b>: cada caminho
    /// nomeia os campos que tornam a sua falha acionável, e o da aquisição
    /// precisa do <c>ContextId</c>, que o da execução não usa.
    ///
    /// <para>
    /// <c>conversationSessionValue: null</c> nos dois casos, de propósito: uma
    /// task que falhou não tem sessão válida para publicar, e a última sessão
    /// persistida (de uma task <c>completed</c> anterior) tem que permanecer
    /// intacta — ver design.md da change apps-workers-historico-conversa,
    /// Decisões 1 e 6.
    /// </para>
    /// </remarks>
    private async Task FailTaskAsync(
        ITaskStore taskStore, TaskJobMessage message, AgentTask task, CancellationToken cancellationToken)
    {
        var savedTask = await ApplyStepAsync(
            taskStore,
            message.TaskId,
            message.ContextId,
            task,
            updater => updater.FailAsync(cancellationToken: cancellationToken),
            cancellationToken,
            message.PushNotificationConfig is not null
                ? failedTask => failedTask.Metadata = BuildTerminalMetadata(conversationSessionValue: null, message.PushNotificationConfig)
                : null);

        await SendPushNotificationIfConfiguredAsync(message, savedTask, cancellationToken);
    }

    /// <summary>
    /// Dispara o webhook (design.md, Decision 2) só quando a mensagem trouxe
    /// um <c>PushNotificationConfig</c> — a task já está persistida com seu
    /// estado terminal real (<paramref name="task"/> é o retorno de
    /// <see cref="ApplyStepAsync"/>, chamado depois do <c>SaveTaskAsync</c>)
    /// antes desta chamada começar.
    /// </summary>
    private async Task SendPushNotificationIfConfiguredAsync(TaskJobMessage message, AgentTask? task, CancellationToken cancellationToken)
    {
        if (message.PushNotificationConfig is not null && task is not null)
        {
            await pushNotificationSender.SendAsync(message.PushNotificationConfig, task, cancellationToken);
        }
    }

    /// <summary>
    /// Recupera o <see cref="AgentSession"/> da task completed mais recente
    /// do mesmo contextId (a sessão serializada já é cumulativa — não é
    /// necessário paginar por várias tasks, ver design.md, Decisão 3), ou
    /// cria uma sessão nova se não houver nenhuma (contextId novo, ou task
    /// gravada antes desta mudança, sem o campo).
    /// </summary>
    private static async Task<AgentSession> LoadSessionAsync(
        AIAgent aiAgent,
        ITaskStore taskStore,
        string contextId,
        CancellationToken cancellationToken)
    {
        var previousCompleted = await taskStore.ListTasksAsync(new ListTasksRequest
        {
            ContextId = contextId,
            Status = TaskState.Completed,
            PageSize = 1,
        }, cancellationToken);

        var metadata = previousCompleted.Tasks.FirstOrDefault()?.Metadata;
        if (metadata is not null && metadata.TryGetValue(ConversationSessionMetadataKey, out var conversationSessionValue))
        {
            var decoded = ConversationSessionCodec.Decode(conversationSessionValue);
            return await aiAgent.DeserializeSessionAsync(decoded, cancellationToken: cancellationToken);
        }

        return await aiAgent.CreateSessionAsync(cancellationToken);
    }

    /// <summary>
    /// Tenta ler a task algumas vezes, com um pequeno atraso entre tentativas,
    /// até encontrar uma task que já tenha uma mensagem de usuário utilizável
    /// — não basta a task existir, ver comentário em <see cref="ExecuteAsync"/>
    /// e design.md, Decisão 9.
    /// </summary>
    private static async Task<AgentTask?> GetTaskWithRetryAsync(
        ITaskStore taskStore, string taskId, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= GetTaskMaxAttempts; attempt++)
        {
            var task = await taskStore.GetTaskAsync(taskId, cancellationToken);
            if (task is not null && !string.IsNullOrWhiteSpace(ExtractLatestUserText(task)))
            {
                return task;
            }

            if (attempt < GetTaskMaxAttempts)
            {
                await Task.Delay(GetTaskRetryDelay, cancellationToken);
            }
        }

        return null;
    }

    private static string ExtractLatestUserText(AgentTask task)
    {
        var lastUserMessage = task.History?.LastOrDefault(m => m.Role == Role.User);
        return lastUserMessage?.Parts.FirstOrDefault(part => part.Text is not null)?.Text ?? string.Empty;
    }

    /// <summary>
    /// Lê <c>Message.Metadata[MessageInstantCodec.MetadataKey]</c> da última
    /// mensagem do usuário no histórico da task — mesmo filtro de
    /// <see cref="ExtractLatestUserText"/> (design.md da change
    /// inbox-instante-mensagem, Decisão D3: invariante verificado contra o
    /// código real, não assumido — o Message que uma task delegada recebe
    /// satisfaz este mesmo filtro). Três casos tratados como "sem instante
    /// de mensagem", nunca como erro de task (Decisão D4): Metadata nulo,
    /// chave ausente, ou valor presente mas não parseável como ISO 8601 —
    /// esse terceiro caso, e só ele, emite um log de aviso, porque um valor
    /// presente e ilegível é sinal de bug em algum produtor da chave.
    /// </summary>
    private DateTimeOffset? ExtractMessageInstant(AgentTask task, string taskId)
    {
        var lastUserMessage = task.History?.LastOrDefault(m => m.Role == Role.User);
        if (lastUserMessage?.Metadata is null || !lastUserMessage.Metadata.TryGetValue(MessageInstantCodec.MetadataKey, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
        {
            return instant;
        }

        logger.LogWarning(
            "Task {TaskId} tem {MetadataKey} presente em Message.Metadata, mas com um valor ilegível como instante ISO 8601: {RawValue}",
            taskId,
            MessageInstantCodec.MetadataKey,
            value.GetRawText());
        return null;
    }

    /// <summary>
    /// Lê <c>channelType</c>/<c>contactExternalId</c> de
    /// <c>Message.Metadata</c> da última mensagem do usuário no histórico da
    /// task — mesmo ponto de leitura de <see cref="ExtractMessageInstant"/>,
    /// sem caminho de extração separado para tasks delegadas (design.md,
    /// D6: contexto de canal não se propaga na delegação).
    /// </summary>
    private (string? ChannelType, string? ContactExternalId) ExtractChannelContext(AgentTask task, string taskId)
    {
        var lastUserMessage = task.History?.LastOrDefault(m => m.Role == Role.User);
        var metadata = lastUserMessage?.Metadata;

        return (
            ExtractChannelContextField(metadata, ChannelTypeMetadataKey, taskId),
            ExtractChannelContextField(metadata, ContactExternalIdMetadataKey, taskId));
    }

    /// <summary>
    /// Por campo (design.md, D6): metadata nulo, chave ausente, ou string
    /// vazia — ausência silenciosa, sem log (caminho normal, ex. cliente A2A
    /// externo que não conhece a chave). Valor presente com
    /// <see cref="JsonValueKind"/> diferente de <see cref="JsonValueKind.String"/>
    /// — tratado como ausência para o bloco, mas registra log de aviso: é
    /// sinal de bug em algum produtor da chave (nunca <c>apps/inbox</c>, que
    /// só grava strings — mas um cliente A2A externo pode), mesmo padrão de
    /// <see cref="ExtractMessageInstant"/> para valor ilegível.
    /// </summary>
    private string? ExtractChannelContextField(IReadOnlyDictionary<string, JsonElement>? metadata, string key, string taskId)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var stringValue = value.GetString();
            return string.IsNullOrEmpty(stringValue) ? null : stringValue;
        }

        logger.LogWarning(
            "Task {TaskId} tem {MetadataKey} presente em Message.Metadata, mas com um valor que não é string: {RawValue}",
            taskId,
            key,
            value.GetRawText());
        return null;
    }

    /// <summary>
    /// Monta o Metadata gravado numa transição terminal (completed/failed) —
    /// <c>conversationSession</c> só no caminho de sucesso (sessão só existe
    /// quando o LLM respondeu), <c>pushNotificationConfig</c> em qualquer um
    /// dos dois quando presente na mensagem (design.md, Decision 1).
    /// </summary>
    private static Dictionary<string, JsonElement> BuildTerminalMetadata(
        JsonElement? conversationSessionValue, PushNotificationConfig? pushNotificationConfig)
    {
        var metadata = new Dictionary<string, JsonElement>();

        if (conversationSessionValue is not null)
        {
            metadata[ConversationSessionMetadataKey] = conversationSessionValue.Value;
        }

        if (pushNotificationConfig is not null)
        {
            metadata[PushNotificationConfigCodec.MetadataKey] = PushNotificationConfigCodec.Encode(pushNotificationConfig);
        }

        return metadata;
    }

    private static async Task<AgentTask?> ApplyStepAsync(
        ITaskStore taskStore,
        string taskId,
        string contextId,
        AgentTask? current,
        Func<TaskUpdater, ValueTask> step,
        CancellationToken cancellationToken,
        Action<AgentTask>? beforeSave = null)
    {
        var queue = new AgentEventQueue();
        var updater = new TaskUpdater(queue, taskId, contextId);

        await step(updater);
        queue.Complete();

        await foreach (var streamEvent in queue.WithCancellation(cancellationToken))
        {
            current = TaskProjection.Apply(current, streamEvent);
        }

        if (current is not null)
        {
            beforeSave?.Invoke(current);
            await taskStore.SaveTaskAsync(taskId, current, cancellationToken);
        }

        return current;
    }
}
