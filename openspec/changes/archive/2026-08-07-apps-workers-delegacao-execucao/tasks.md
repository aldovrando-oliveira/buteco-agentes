## 1. `apps/workers` — mirror de `AgentDelegation` e ajuste do mirror de `Agent`

- [x] 1.1 Adicionar `IsActive` ao mirror `Agent`
      (`apps/workers/src/Buteco.Workers/Agents/Entities/Agent.cs`) e ao
      mapeamento fluente correspondente em `AppDbContext` — hoje esse
      mirror não tem essa propriedade (achado da Decision 3 do
      design.md).
- [x] 1.2 Criar `AgentDelegation`
      (`apps/workers/src/Buteco.Workers/AgentDelegations/Entities/AgentDelegation.cs`),
      somente-leitura, mesmo padrão de `McpServer`/`AgentMcpServer`.
- [x] 1.3 Mapear `AgentDelegation` no `AppDbContext` de `apps/workers`
      contra a tabela `agent_delegations` já existente (chave composta
      `SourceAgentId`/`TargetAgentId`, `HasOne<Agent>` para os dois
      lados) — nenhuma migration nova em produção (Decision 3); gerada
      uma migration própria de `apps/workers`
      (`20260807021330_AddAgentDelegationAndIsActive`) só para
      ferramental do EF Core/schema de teste, nunca executada em
      runtime, mesmo padrão das migrations existentes nesse contexto.

## 2. `apps/workers` — publisher novo

- [x] 2.1 Criar `ITaskJobPublisher`/`RabbitMqTaskJobPublisher`
      (`apps/workers/src/Buteco.Workers/Messaging/`), mirror do par
      equivalente em `apps/api` (Decision 4).
- [x] 2.2 Registrar o publisher no DI de `apps/workers/src/Buteco.Workers/Program.cs`.

## 3. `apps/workers` — contador de profundidade

- [x] 3.1 Definir a chave `Metadata["delegationDepth"]` e o helper de
      leitura/escrita (`AgentDelegations/DelegationDepth.cs`), mesmo
      mecanismo de codificação já usado para `conversationSession`
      (Decision 6).
- [x] 3.2 Definir `DelegationDepthLimit = 5` como constante global em
      `AgentExecutionService` (mesmo estilo de `MaxHistoryMessages`/
      `SummarizationTurnThreshold`).
- [x] 3.3 Em `AgentExecutionService.ExecuteAsync`, logo após
      `GetTaskWithRetryAsync` (antes de `StartWorkAsync`/do
      `ConversationContextLock`), ler a profundidade da task recebida e,
      se exceder o teto, transicionar a task para `Rejected` (mesmo
      estado terminal já usado para "agente inativo"/"sem provider" em
      `EnqueueingAgentHandler`) em vez de `failed` — a tool de
      delegação que está esperando (Decision 8) já trata qualquer
      estado terminal não-`Completed` como falha graciosa, sem código
      especial para profundidade — ver Decision 6 e o Requirement
      "Controle de profundidade" do spec.

## 4. `apps/workers` — tool de delegação e resolver

- [x] 4.1 Criar `DelegationToolNameSlugifier`
      (`apps/workers/src/Buteco.Workers/AgentDelegations/`), mirror de
      `AgentSkillMapper.Slugify` (`apps/api`) + dedupe determinístico por
      sufixo numérico (Decision 9).
- [x] 4.2 Criar `IAgentDelegationToolSetResolver`/
      `AgentDelegationToolSetResolver`
      (`apps/workers/src/Buteco.Workers/AgentDelegations/`): dado o
      `Agent` do Source já carregado, lê `AgentDelegation` vinculados e
      monta uma `AITool` por Target via `AIFunctionFactory.Create`, nome
      `delegate_to_{slug}` sanitizado — o sanitizador de caracteres de
      `McpToolSetResolver.BuildSafeToolName` foi extraído para
      `Mcp/ToolNameSanitizer.cs` (novo) para ser reaproveitado aqui de
      fato, em vez de duplicado (Decision 9/10; `McpToolSetResolver`
      editado para usar o mesmo utilitário).
- [x] 4.3 Implementar a orquestração da tool (método C# invocado pela
      `AIFunction`): valida Target fresco (`IsActive`, `Provider`/
      `Model` — Decision 4) e Source, também fresco (Decision 5 —
      corrigido durante a implementação: a versão original do design
      previa reaproveitar o `Agent` já carregado no início de
      `ExecuteAsync`, mas isso nunca detectaria uma desativação
      ocorrida entre esse carregamento e a chamada da tool, exatamente
      o cenário que o Requirement "Validação do Source antes de
      delegar" promete cobrir — ver nota na Decision 5 do design.md);
      cria a task `submitted` do Target
      via `PostgresTaskStore` próprio escopado ao `targetAgentId`, com
      `Metadata["delegationDepth"]` (profundidade do Source + 1) já
      gravado na criação e o texto da delegação como mensagem de
      usuário no `History`; publica o job na fila `agent-tasks` via
      `ITaskJobPublisher`; faz polling (intervalo de 1s) até estado
      terminal ou timeout de 120s (Decision 8) — valores expostos via
      `IOptions<AgentDelegationToolOptions>` (novo, `Options/`) em vez
      de `const`, especificamente para que os testes de concorrência
      (task 6) possam sobrescrever um timeout curto sem depender de um
      deadlock real de 120s; nunca vinculado a nenhuma seção de
      configuração em `Program.cs`, então o comportamento em produção é
      idêntico a uma constante — retorna o resultado
      (texto produzido pelo Target) ou uma falha descritiva. O
      enforcement do teto de profundidade em si fica inteiramente do
      lado de quem processa a task (task 3.3) — a tool sempre cria e
      publica, sem checagem própria de profundidade (mesmo tratamento
      genérico de estado terminal não-`Completed` cobre o caso).
- [x] 4.4 Registrar `IAgentDelegationToolSetResolver` no DI de
      `Program.cs`.
- [x] 4.5 Integrar o resolver em `AgentExecutionService.cs` —
      `ChatOptions.Tools` passa a ser a concatenação das tools MCP
      (`mcpToolSetResolver`) com as tools de delegação
      (`delegationToolSetResolver`), resolvidas na mesma chamada.
      Também adicionado `NullAgentDelegationToolSetResolver`
      (`tests/Support/`) e registrado nos hosts de teste existentes
      (`WorkerTests`, `TaskJobConsumerTests`, `ConversationHistoryTests`,
      `HistorySummarizationTests`) — necessário porque
      `AgentExecutionService` ganhou uma dependência nova no
      construtor, mesmo padrão de `NullMcpToolSetResolver`.

## 5. `apps/workers/tests/Buteco.Workers.Tests` — testes de execução (IChatClient mockado)

- [x] 5.1 Round-trip completo: LLM do Source chama a tool de delegação
      (mock configurado para emitir a tool call), task nova nasce pro
      Target no mesmo `contextId`, é processada (mock do Target
      responde), resultado volta pro Source, LLM do Source usa o
      resultado na resposta final, task do Source conclui
      (`AgentDelegationExecutionTests.RoundTrip_...`).
- [x] 5.2 Profundidade excedida: task do Source já com
      `Metadata["delegationDepth"] = 5` (o teto) — a task criada pela
      tool para o Target nasce com profundidade 6, é rejeitada
      (`Rejected`, task 3.3) sem nunca chamar o LLM do Target (verificado
      com `Times.Never`); task do Source segue seu fluxo normal
      (`DelegationDepthExceeded_...`).
- [x] 5.3 Target ativo mas sem `Provider`/`Model` configurados no
      momento da chamada — degradação graciosa, task do Source não
      falha, nenhuma task criada para o Target
      (`TargetActiveButMissingProviderModel_...`).
- [x] 5.4 Target inativo (`IsActive = false`) mas com `Provider`/`Model`
      preenchidos no momento da chamada — degradação graciosa, nenhuma
      task criada para o Target (`TargetInactiveButProviderModelConfigured_...`).
- [x] 5.5 Source desativado durante o próprio processamento — degradação
      graciosa antes de criar a task do Target
      (`SourceDeactivatedDuringOwnProcessing_...`; só passou a ser
      detectável de verdade depois da correção da Decision 5 na task 4.3
      — sem ela, este teste não tinha como falhar de propósito).
- [x] 5.6 Timeout expirando sem a task do Target concluir — task do
      Source não falha, tool retorna resultado de falha
      (`TimeoutExpires_...`, timeout sobrescrito para 3s via
      `AgentDelegationToolOptions`).
- [x] 5.7 Task do Target chega a `Failed` — tratada pela tool como falha
      comum, sem tratamento especial no LLM do Source
      (`TargetTaskFails_...`).
- [x] 5.8 Nome de tool estável e sem colisão para dois Targets com
      `Agent.Name` colidente vinculados ao mesmo Source
      (`DelegationToolNameSlugifierTests`, incluindo estabilidade entre
      execuções e slug puro via `[Theory]`).
- [x] 5.9 Agente sem nenhuma delegação cadastrada não recebe tool de
      delegação — dois níveis: unitário
      (`DelegationToolNameSlugifierTests.ResolveAsync_SourceWithoutAnyDelegation_ReturnsEmptyList`)
      e integração, capturando `ChatOptions.Tools` de fato passado ao
      mock (`AgentDelegationExecutionTests.AgentWithoutAnyDelegation_ReceivesNoDelegationTool`).

**Achado importante durante a escrita destes testes, não previsto no
design original**: os dois hosts de um cenário Source+Target consomem da
MESMA fila `agent-tasks`, sem nenhuma afinidade por agente — o RabbitMQ
entrega para qualquer instância livre. Dar a cada instância um mock de
`IChatClient` fixo (como se "esta instância sempre processa o Source")
causava falhas espúrias sempre que o RabbitMQ entregava a mensagem
errada para a instância errada. Corrigido fazendo as duas instâncias
IDÊNTICAS, com um resolver mock que despacha por `(provider, model)`
— exatamente como o `ChatClientResolver` real — usando `Provider`/`Model`
distintos para Source e Target no seed de dados. Nenhuma mudança de
produção, só do harness de teste.

## 6. `apps/workers/tests/Buteco.Workers.Tests` — prova da investigação bloqueante (Decision 1)

- [x] 6.1 **Duas instâncias separadas**, ambas com seu próprio
      `IServiceScopeFactory`/container e seu próprio `TaskJobConsumer`
      real consumindo da mesma fila `agent-tasks` do RabbitMQ do fixture
      de teste (exercitando o consumo real com `prefetchCount: 1`, não
      só chamando `ExecuteAsync` diretamente): a task delegada do Target
      é publicada na fila real; um `TaskCompletionSource` sinalizado
      dentro do mock do Target prova que ele foi invocado dentro de uma
      janela curta (10s, bem menor que os 30s do timeout configurado) —
      estruturalmente só possível se uma instância DIFERENTE da que
      processa o Source o tiver consumido; delegação conclui com sucesso
      (`AgentDelegationConcurrencyTests.TwoInstances_...`).
- [x] 6.2 **Instância única**: timeout sobrescrito para 3s via
      `AgentDelegationToolOptions`; assertado que o tempo decorrido é
      >= o timeout configurado (prova de que bloqueou de verdade até
      expirar, não que falhou rápido por outro motivo) e bem abaixo do
      timeout de produção (120s); task do Source conclui normalmente,
      nunca `failed` (`SingleInstance_DelegationTimesOutGracefully_...`).
      Não afirma que a task do Target "fica Submitted para sempre" — uma
      vez que o Source finalmente confirma (Ack) sua própria mensagem
      (só depois que o timeout expira), a MESMA instância única fica
      livre de novo e naturalmente pode consumir a mensagem do Target
      ainda na fila; isso é comportamento correto do sistema real, não
      um bug — a asserção original desta forma se mostrou uma corrida
      espúria durante a execução dos testes, removida em favor da prova
      por tempo decorrido.

**Segundo achado, também via teste real**: `McpToolExecutionEndToEndTests.cs`
(pré-existente, `apps-workers-execucao-mcp`) monta seu próprio host com
`AgentExecutionService` e não tinha `IAgentDelegationToolSetResolver`
registrado — `AgentExecutionService` ganhou essa dependência nova no
construtor (task 4.5) e esse arquivo não fazia parte do grep inicial
(que buscava por `NullMcpToolSetResolver`, ausente ali porque esse teste
usa o resolver MCP real). Corrigido registrando
`NullAgentDelegationToolSetResolver` também lá — confirmado rodando a
suíte completa de `apps/workers` (67 testes) três vezes seguidas, sem
falhas.

## 7. Documentação operacional

- [x] 7.1 Requisito de ≥ 2 réplicas documentado no `README.md` raiz do
      monorepo, na seção `### apps/workers` — inclui a instrução prática
      de rodar um segundo `dotnet run --project src/Buteco.Workers` para
      testar delegação localmente.
