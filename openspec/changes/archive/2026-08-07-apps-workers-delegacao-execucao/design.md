## Context

O catálogo de delegação já existe (`backend-agente-delegacao-catalogo-vinculo`,
aplicada): `AgentDelegation` é um vínculo unidirecional `Agent → Agent`
(Source → Target), tabela `agent_delegations` (`SourceAgentId`,
`TargetAgentId`, chave composta), cadastrado via
`PUT /agents/{id}/delegations` em `apps/api`. Essa change deixou
explicitamente registrado, como pendência para a change de execução
(esta): checar `Agent.IsActive` do lado Target (e "provavelmente também
do lado Source") antes de delegar de fato (Decision 4 daquele
design.md).

`apps/workers` já resolve tools MCP em tempo de execução
(`IMcpToolSetResolver`, change `apps-workers-execucao-mcp`) e já mantém
histórico de conversa por `contextId` via sessão serializada em
`AgentTask.Metadata` (`apps-workers-historico-conversa`), com
serialização entre mensagens do mesmo `(agentId, contextId)` garantida
por um `pg_advisory_lock` (`ConversationContextLock`), adquirido no
início de `AgentExecutionService.ExecuteAsync` e liberado só no fim do
método.

Investigação prévia (via `/opsx:explore`) leu a implementação real do
consumidor RabbitMQ (`TaskJobConsumer.cs`), do publisher de `apps/api`
(`RabbitMqTaskJobPublisher.cs`), do handler que hoje cria a task inicial
(`EnqueueingAgentHandler.cs`), do resolver de tools MCP
(`IMcpToolSetResolver`/`McpToolSetResolver`) e do mecanismo de slug/dedupe
já usado para `Skill.Name` (`AgentSkillMapper.Slugify`, em `apps/api`) —
ver Decisions abaixo para o raciocínio completo e as alternativas
descartadas.

## Goals / Non-Goals

**Goals:**
- Tool de delegação exposta ao LLM do agente Source, uma por
  `AgentDelegation` vinculado, nome estável mesmo com `Agent.Name`
  colidente entre Targets.
- Round-trip completo: chamada da tool → task nova para o Target (mesmo
  `contextId`) → publicada → processada (possivelmente por outra
  instância de `apps/workers`) → resultado de volta para o LLM do
  Source → task do Source conclui.
- Controle de profundidade da cadeia de delegação, com teto configurado
  (constante global) e falha graciosa quando excedido.
- Timeout de espera pela task delegada, com falha graciosa (sem derrubar
  a task do Source) em caso de expiração ou de falha do Target.
- Validação fresca de `IsActive`/`Provider`/`Model` de ambos os lados
  (Target e Source) no momento da delegação.
- Documentar e provar em teste o requisito operacional de concorrência
  descoberto na investigação bloqueante (mínimo 2 réplicas de
  `apps/workers`).

**Non-Goals:**
- Nenhum cliente A2A real (HTTP) contra a rota pública de outro agente —
  mecanismo interno (Decision 1).
- Nenhuma detecção de ciclo indireto/bidirecional no cadastro (já
  decidido como non-goal na change de catálogo) — só o controle de
  profundidade em runtime previne loop infinito de fato.
- Nenhuma UI.
- Nenhuma mudança em `apps/api`.
- Nenhuma configuração de profundidade máxima (ou timeout) por agente —
  constantes globais nesta primeira fatia, mesmo espírito de
  `MaxHistoryMessages`/`SummarizationTurnThreshold` em
  `AgentExecutionService`.

## Decisions

### Decision 1 (bloqueante): `apps/workers` precisa de ≥ 2 réplicas — deadlock estrutural com instância única

`TaskJobConsumer.cs` configura o canal com
`BasicQosAsync(0, prefetchCount: 1, global: false, ...)`. Isso não é um
detalhe do loop de consumo local — é *enforcement do broker*: o RabbitMQ
não entrega uma segunda mensagem não confirmada a um canal que já tem uma
mensagem em voo. O `await executionService.ExecuteAsync(message,
stoppingToken)` dentro do `ReceivedAsync` só devolve controle (e só
dispara o `BasicAckAsync`) quando a task inteira termina.

Consequência direta: se a tool de delegação faz `await` até a task do
Target chegar a um estado terminal, e a instância que está esperando é a
**única** rodando, ela está, no momento do `await`, seguindo com sua
única mensagem em voo não confirmada (a do Source) — o RabbitMQ nunca
entrega a mensagem do Target a essa mesma instância (prefetch esgotado),
e não há nenhuma outra instância para entregá-la. Autodeadlock garantido,
resolvido só pelo timeout da Decision 8 (a task do Source nunca falha,
mas a delegação sempre expira).

```
Instância única, prefetch=1
┌───────────────────────────────────────────────────────┐
│ consumindo "Source task" (unacked, slot 1/1 ocupado)    │
│   ExecuteAsync(Source) → RunAsync → tool "delegar"      │
│     → publica "Target task" na fila agent-tasks          │
│     → await até Target terminal ──────────────┐          │
│         (Ack do Source NUNCA acontece antes    │          │
│          disso — slot nunca libera — Target     │          │
│          NUNCA é entregue a esta instância)     ▼          │
└───────────────────────────────────────────────────────┘
     Target fica "submitted" até o timeout da Decision 8
```

Com **N ≥ 2** réplicas: a instância A, com seu único slot ocupado pela
mensagem do Source, é pulada pelo round-robin de entrega do RabbitMQ
(sem capacidade disponível); a mensagem do Target vai para uma instância
B livre, que processa e conclui independentemente; A observa o estado
terminal via polling (Decision 8) e segue.

Hoje **não existe nenhuma configuração de múltiplas réplicas** no
projeto — `docker-compose.yml` só declara `postgres` e `rabbitmq`;
`apps/workers` roda como processo único (`dotnet run`). Isso significa
que o caminho mais natural de rodar isso localmente hoje garante o
deadlock em qualquer teste manual de delegação.

**Decisão**: documentar como requisito operacional real — `apps/workers`
precisa de ≥ 2 réplicas para que delegação funcione, ponto. Provado em
teste dedicado (task 5 em `tasks.md`): duas instâncias reais processando
Source e Target concorrentemente, e um teste complementar de instância
única com timeout curto sobrescrito, provando empiricamente que o
deadlock acontece e expira pelo timeout (não trava o test runner).

**Alternativas descartadas**:
- **Redesenhar o consumidor para processar mensagens concorrentemente
  dentro do mesmo processo** (aumentar `prefetchCount`, ou múltiplos
  consumidores/canais por instância). Rejeitada nesta fatia: aumentar
  prefetch reabre exatamente a corrida entre mensagens do mesmo
  `contextId` que o lock consultivo de `apps-workers-historico-conversa`
  foi desenhado para evitar (aquele design assumia processamento
  sequencial por instância como premissa) — mudar essa premissa é uma
  mudança arquitetural maior que esta fatia, focada em delegação, não
  deveria carregar de carona.
- **Delegação não-bloqueante** (a tool retorna imediatamente, a task do
  Source é resumida quando o Target concluir, via algum callback/evento).
  Resolveria o deadlock na raiz, mas contradiz o modelo de tool-calling
  síncrono do `FunctionInvokingChatClient` (a tool precisa devolver um
  resultado para o LLM continuar o mesmo turno) — exigiria reestruturar
  o ciclo de vida da task do Source em duas fases (pausada/retomada), fora
  de escopo desta fatia.
- **Aceitar como risco documentado, sem exigir 2 réplicas** (mesmo
  padrão usado para `AgentA2AServerRegistry` em memória). Rejeitada: ali
  a consequência era duplicação de objeto em memória; aqui é uma
  delegação que **nunca** teria chance de completar com sucesso em
  instância única — não é risco raro, é 100% dos casos.

### Decision 2: Delegação é mecanismo interno de `apps/workers`, não um cliente A2A real

A tool de delegação escreve a task do Target diretamente no mesmo banco
Postgres (via `PostgresTaskStore` próprio, mesma tabela `a2a_tasks` já
usada por `apps/workers` e `apps/api`) e publica o job na mesma fila
`agent-tasks` — nunca faz uma chamada HTTP contra a rota A2A pública de
outro agente.

**Alternativa descartada, nomeadamente**: cliente A2A real (`A2AClient`
ou HTTP direto) contra o endpoint público `SendMessage` do agente
Target, como um consumidor externo do protocolo faria. Descartada:
- Sem rede, nenhum ponto de falha adicional (DNS, TLS, timeout de
  socket) além do que já existe entre `apps/workers` e o Postgres/
  RabbitMQ.
- Sem superfície de autenticação nova para proteger — o endpoint A2A
  público (`apps/api`) não tem autenticação hoje; expor delegação por
  esse caminho seria abrir (ou depender de) uma superfície que
  precisaria ser protegida assim que auth for implementada, sem ganho
  correspondente (o Target já está no mesmo banco, na mesma rede
  confiável).
- `PostgresTaskStore` (`apps/workers/src/Buteco.Workers/A2A/`) já é,
  pelo próprio comentário no código, "implementação independente da de
  `apps/api`, mesma tabela `a2a_tasks`" — infraestrutura já pronta para
  esse uso, sem trabalho extra de protocolo.

### Decision 3: Mirror de `AgentDelegation` em `apps/workers`

Mesmo padrão já usado para `McpServer`/`AgentMcpServer`
(`apps/workers/src/Buteco.Workers/Mcp/Entities/`): entidade
somente-leitura, mapeada no `AppDbContext` próprio de `apps/workers`
contra a tabela `agent_delegations` já existente (criada pela migration
de `apps/api`), sem `ProjectReference` entre os dois apps — nenhuma
migration nova em `apps/workers` (mesmo comentário já presente no
`AppDbContext.cs` de lá: as migrations desse contexto existem só para
ferramental do EF Core, nunca executadas em runtime).

```csharp
public class AgentDelegation
{
    public Guid SourceAgentId { get; private set; }
    public Guid TargetAgentId { get; private set; }
}
```

Chave composta `(SourceAgentId, TargetAgentId)`, `HasOne<Agent>` para os
dois lados com `DeleteBehavior.Cascade` — espelha exatamente o
mapeamento já existente em `apps/api/src/Buteco.Api/Infrastructure/AppDbContext.cs`.

**Achado durante a investigação, não opcional**: o mirror de `Agent` em
`apps/workers` (`apps/workers/src/Buteco.Workers/Agents/Entities/Agent.cs`)
**não tem `IsActive`** hoje — só `Name`, `Instructions`, `Provider`,
`Model`, `CreatedAt`, `UpdatedAt`. Checar `IsActive` do Target (Decision
4) e do Source (Decision 5) exige adicionar essa propriedade ao mirror
(e ao mapeamento fluente correspondente no `AppDbContext`) — não é
"só ler um campo que já existe".

### Decision 4: Publisher novo + validação fresca do Target

Mirror de `RabbitMqTaskJobPublisher` (`apps/api`), implementando um
`ITaskJobPublisher` próprio de `apps/workers` — hoje `apps/workers` só
consome do RabbitMQ, nunca publica; esta é a primeira vez que precisa.

A tool de delegação, ao ser invocada, replica as checagens que
`EnqueueingAgentHandler` já faz para o agente antes de aceitar
`SendMessage`: `Agent.IsActive`, `Provider`/`Model` não nulos — lidas
frescas do banco no momento da chamada (não de nenhum cache), mesmo
raciocínio já usado ali ("Estado do agente é lido do banco a cada
execução... para que uma mudança de estado depois do primeiro
`SendMessage` seja recusada a partir da próxima chamada").

**Nota registrada explicitamente, por não ser óbvia**: mesmo *sem*
nenhuma checagem prévia, uma falha de configuração do Target já
degradaria graciosamente — `ChatClientResolver.Resolve` já lança
`InvalidOperationException` para provider ausente/mal configurado
(inclusive quando `Provider`/`Model` chegam `null`, via
`agent.Provider!`, que passa `null` de fato para o switch, cai no `_ =>
throw`), e isso já cai no `catch` genérico de
`AgentExecutionService.ExecuteAsync`, terminando a task do Target como
`failed` — que a tool de delegação já trataria como falha (Decision 8).
Ou seja: a checagem prévia **não é a única rede de segurança**, é uma
otimização — evita gastar um round-trip inteiro de fila + `pg_advisory_lock`
+ ciclo de vida de task (`submitted → working → failed`) num Target já
sabidamente inválido. Vale a pena de qualquer forma: falha rápida,
sem consumir o timeout da Decision 8 esperando por algo que nunca
teria sucesso.

**IsProviderConfigured não é replicado**: `EnqueueingAgentHandler`
também checa `providerCatalogService.IsProviderConfigured(...)` (chave
de API presente no ambiente de `apps/api`). `apps/workers` não tem esse
serviço — o equivalente, para o Target, é `ChatClientResolver.Resolve`
lançar se a chave não estiver configurada no ambiente do *worker*, já
coberto pelo mesmo `catch` genérico. Checar antecipadamente exigiria
expor `ApiKey` configurado por provedor num serviço próprio de
`apps/workers` só para esse fim — descartado por duplicar, com código
novo, uma verificação que o caminho de falha já cobre message a message
(diferente do caso `IsActive`/`Provider`/`Model`, que evita todo o
round-trip; aqui o ganho seria só evitar *uma* chamada ao provedor que
já falharia local).

### Decision 5: Validação também do lado Source

Pendência explícita da change de catálogo (Decision 4 daquele design.md:
"precisará checar `Agent.IsActive` do lado Target e provavelmente também
do lado Source"). Resolvida aqui: sim, checa também.

**Correção feita durante a implementação** (a versão original desta
Decision, abaixo em itálico, continha uma contradição lógica): o Source
também é lido **fresco** do banco no momento da chamada, exatamente
como o Target (Decision 4) — mesma query, mesmo `AsNoTracking`, só
trocando o id.

_Versão original (incorreta) desta Decision, mantida aqui só para
rastrear o que mudou_: propunha reaproveitar o `Agent` do Source já
carregado no início de `AgentExecutionService.ExecuteAsync`
(`dbContext.Agents...FirstOrDefaultAsync`), sem nova query dentro da
tool, sob a justificativa de que "a janela entre o início de
`ExecuteAsync` e a chamada da tool... não é um intervalo que valha a
pena otimizar". O problema: esse objeto é um snapshot fixo em memória —
reaproveitá-lo *estruturalmente* não consegue detectar uma desativação
que aconteça depois desse carregamento e antes da chamada da tool, que
é precisamente o cenário "Source desativado durante o processamento da
própria task" que este mesmo Decision (e o Requirement correspondente
em `specs/agent-delegation-execution/spec.md`) descreve como coberto.
Não é uma questão de custo/otimização como a justificativa original
sugeria — é uma questão de a checagem, como desenhada, nunca poder
disparar para esse cenário. Descoberto ao escrever o teste
`SourceDeactivatedDuringOwnProcessing_...` (task 5.5): não havia como
montar um cenário onde a checagem antiga falhasse de verdade. Corrigido
trocando a leitura em memória por uma query fresca idêntica à do
Target — custo desprezível (um `SELECT` a mais por chave primária,
indexado) pelo ganho de a checagem realmente cobrir o que o spec promete.

Se o Source estiver inativo ou sem `Provider`/`Model` (cenário raro —
teria sido pego por `EnqueueingAgentHandler` antes de a task do Source
sequer ser publicada, a menos que o agente tenha sido desativado
*durante* o processamento), a tool retorna falha para o LLM continuar,
mesmo tratamento da Decision 8 — nenhuma tentativa de derrubar a task do
Source por fora do fluxo normal de falha de tool.

### Decision 6: Contador de profundidade em `AgentTask.Metadata`, escrito na criação

`AgentTask.Metadata` hoje só é escrito no caminho de **conclusão** de uma
task (`AgentExecutionService.cs`, `ApplyStepAsync(..., completedTask =>
completedTask.Metadata = ...)`, chamado só depois de `RunAsync` retornar
com sucesso). O contador de profundidade precisa existir **antes** de a
task do Target ser processada — para o próprio `ExecuteAsync` do Target
checar o teto no início, antes de gastar uma chamada ao LLM. Isso exige
abrir um caminho de escrita de `Metadata` que hoje não existe: a tool de
delegação grava `Metadata["delegationDepth"]` (chave nova, mesmo
mecanismo de codificação já usado para `conversationSession`) já no
momento em que cria a task `submitted` do Target — não espera a
conclusão.

`AgentExecutionService.ExecuteAsync`, imediatamente após
`GetTaskWithRetryAsync` (a task só existe localmente a partir desse
ponto — é o próprio `Metadata` dela que carrega a profundidade) e antes
de `StartWorkAsync`/de adquirir o `ConversationContextLock`, lê
`task.Metadata["delegationDepth"]` (ausente = profundidade 0, task
raiz) e transiciona a task para `Rejected` — não `Failed` — se
`profundidade > DelegationDepthLimit` (constante global, mesmo estilo de
`MaxHistoryMessages`/`SummarizationTurnThreshold`), mesmo estado
terminal já usado para "agente inativo"/"sem provider" em
`EnqueueingAgentHandler`. A tool de delegação (Decision 8) não faz
nenhuma checagem própria de profundidade — sempre cria e publica a task
do Target com `profundidade do Source + 1` no `Metadata`; o enforcement
do teto acontece inteiramente do lado de quem processa essa task, e o
resultado (`Rejected`) já cai no mesmo tratamento genérico de "estado
terminal não-`Completed`" que qualquer outra falha do Target.

**Decisão de valor**: `DelegationDepthLimit = 5`. Não há precedente
numérico no código para extrair (diferente de `MaxHistoryMessages`, que
tinha uma motivação técnica documentada — aqui é uma escolha de produto
nova). Cinco níveis cobre cadeias de delegação razoáveis (Source →
especialista → sub-especialista, etc.) sem permitir uma cadeia
indefinidamente longa consumindo N réplicas de `apps/workers`
simultaneamente presas em espera (ver Decision 1 — cada nível a mais na
cadeia é, na prática, mais uma instância ocupada e bloqueada).

**Alternativa descartada**: profundidade viajando em `TaskJobMessage`
(campo novo na mensagem do RabbitMQ) em vez de `AgentTask.Metadata`.
Rejeitada — `Metadata` já é o mecanismo estabelecido para dado que
precisa sobreviver e ser lido de volta a partir do store durável (mesmo
raciocínio de `conversationSession`); colocar em `TaskJobMessage`
duplicaria a fonte de verdade (mensagem efêmera vs. estado persistido) e
tornaria o dado invisível para quem inspecionar a task depois pelo
`GetTask` (útil para debug de uma cadeia de delegação).

### Decision 7: `contextId` compartilhado, isolamento por `AgentId` — confirmado sem trabalho adicional

`PostgresTaskStore.ListTasksAsync` já filtra por `AgentId` além de
`ContextId` (comentário explícito no código: "sem esse filtro, um
`contextId` teoricamente reusado por dois agentes diferentes recuperaria
a sessão errada" — corrigido em `apps-workers-historico-conversa`).
`LoadSessionAsync` usa uma instância de `PostgresTaskStore` já escopada
a um `agentId` fixo (o construtor recebe `agentId` e grava/filtra por
ele em toda operação).

A tool de delegação usa uma **segunda** instância de `PostgresTaskStore`,
escopada ao `targetAgentId` (`new PostgresTaskStore(scopeFactory,
targetAgentId)`), para criar e consultar a task do Target — o isolamento
de histórico por agente dentro do mesmo `contextId` é automático, sem
nenhuma mudança em `PostgresTaskStore` ou em `LoadSessionAsync`.
Confirmado por leitura de código, não é uma decisão nova, só registrado
aqui para fechar o ponto levantado na investigação.

### Decision 8: Timeout via polling + degradação graciosa

Mecanismo de espera: polling, mesmo idioma já usado em
`GetTaskWithRetryAsync` (`AgentExecutionService.cs`) — loop com
`Task.Delay` entre tentativas, checando `taskStore.GetTaskAsync(targetTaskId)`
até `Status.State` ser terminal (`Completed`/`Failed`/`Rejected`/
`Canceled`) ou o timeout expirar.

Intervalo de poll: 1 segundo (diferente do intervalo de
`GetTaskRetryDelay`, 100ms — aquele cobre uma corrida de escrita que
resolve em milissegundos; aqui a espera é por um turno de LLM inteiro,
tipicamente segundos, então um intervalo maior evita centenas de leituras
supérfluas ao Postgres sem perder responsividade percebida).

**Decisão de valor do timeout**: 120 segundos. Alternativa considerada:
30 segundos (mais responsivo, mas curto demais para um Target que também
usa tools MCP ou tem seu próprio histórico longo a processar — um falso
timeout deixaria delegações legítimas falhando por lentidão, não por
problema real) e 300 segundos (rejeitada — o `pg_advisory_lock` do
Source fica seguro por todo esse tempo, ver Decision 10; um timeout
muito longo amplifica o impacto do risco aceito ali).

Ao expirar o timeout, ou se o Target chegar a `Failed`/`Rejected`: a
tool retorna um resultado de falha (texto simples, ex. "delegação para
{Target} não completou: {motivo}") para o `FunctionInvokingChatClient`
— tratado pelo LLM do Source como qualquer outra falha de tool
(`ToolException`/resultado de erro), sem `try/catch` especial em
`AgentExecutionService`. A task do Source **nunca** falha por causa de
uma delegação malsucedida — mesma filosofia já aplicada a falha de tool
MCP (Decision 5 de `apps-workers-execucao-mcp`) e a falha de resumo
(Decision de `apps-workers-resumo-historico-conversa`).

### Decision 9: Nome estável da tool por Target — mirror de `AgentSkillMapper.Slugify`

`Agent.Name` não tem unicidade garantida — mesma lacuna já registrada
para `Skill.Name` na change do AgentCard (`backend-a2a-agent-card`).
Reaproveita o mesmo mecanismo: `Slugify` (lowercase, diacríticos
removidos, não-alfanumérico vira `-`, hífens colapsados/aparados) +
dedupe determinístico por sufixo numérico (`-2`, `-3`, ...), na ordem
em que os `AgentDelegation` aparecem para o Source.

Mirror do método em `apps/workers` (não extração para lib compartilhada
— ver pergunta resolvida durante a exploração: mesmo padrão de
duplicação intencional já usado no projeto inteiro para entidades,
estendido aqui pela primeira vez a um método de lógica pura, sem estado
e sem dependência de framework, então o custo de duplicar é baixo e
consistente com o restante do design).

Nome final da tool: `delegate_to_{slug}` (prefixo fixo, mesmo espírito
do prefixo incondicional de `McpToolSetResolver.BuildSafeToolName`, que
evita colisão entre o namespace de tools de delegação e o de tools MCP
do mesmo agente) — passa pelo mesmo sanitizador de caracteres/tamanho
(`MaxToolNameLength = 64`, regra mais restritiva entre os três
provedores suportados) já usado em `McpToolSetResolver`. Para que isso
fosse reaproveitamento de fato (não duplicação), o sanitizador foi
extraído do corpo privado de `BuildSafeToolName` para
`Mcp/ToolNameSanitizer.cs` (classe estática nova, pública), e
`McpToolSetResolver.BuildSafeToolName` passou a chamá-lo também — única
forma de as duas resoluções de tool (MCP e delegação) compartilharem a
mesma regra sem duplicar a lógica de sanitização caractere a caractere.

### Decision 10: `IAgentDelegationToolSetResolver` — `AITool` via `AIFunctionFactory.Create`

Mesmo papel de `IMcpToolSetResolver`: dado o `agentId` do Source, monta
o conjunto de tools disponível para aquela execução.

```csharp
public interface IAgentDelegationToolSetResolver
{
    Task<IReadOnlyList<AITool>> ResolveAsync(
        AppDbContext dbContext, Agent sourceAgent, string contextId, int currentDepth, CancellationToken cancellationToken);
}
```

Recebe o `Agent` do Source já carregado (Decision 5 — evita nova query)
em vez de só o `agentId`, diferindo levemente da assinatura de
`IMcpToolSetResolver.ResolveAsync` (que só recebe `agentId`) por essa
razão específica. Também recebe `contextId` e `currentDepth` — ambos
per-execução, não disponíveis na construção do resolver (singleton) —
capturados pelo closure de cada `AIFunction` para propagar para a task
delegada sem precisar redescobri-los dentro da tool (achado durante a
implementação: a versão inicial deste sketch de interface, sem esses
dois parâmetros, não tinha como a tool saber em qual `contextId`
propagar a delegação nem qual profundidade usar como base).

Cada tool é um `AIFunction` construído via `AIFunctionFactory.Create`
sobre um método C# de instância que orquestra as Decisions 4, 6 e 8 (
valida Target fresco → checa profundidade → cria task → publica → espera
→ retorna resultado) — não há SDK de terceiro envolvido, ao contrário de
`McpClientTool` (que estende `AIFunction` mas delega `InvokeAsync` para
`McpClient.CallToolAsync`). `ChatOptions.Tools` já é `IList<AITool>`
(`AgentExecutionService.cs:131`, hoje populado só com
`toolSet.Tools.ToList()` de MCP) — as tools de delegação entram na
mesma lista, resolvidas na mesma chamada:

```csharp
var mcpTools = await mcpToolSetResolver.ResolveAsync(dbContext, message.AgentId, cancellationToken);
var delegationTools = await delegationToolSetResolver.ResolveAsync(dbContext, agent, cancellationToken);
ChatOptions = new ChatOptions { Instructions = agent.Instructions, Tools = mcpTools.Tools.Concat(delegationTools).ToList() }
```

Diferente de `McpToolSet`, o retorno de `IAgentDelegationToolSetResolver`
não encapsula nenhuma conexão externa viva (não há transporte MCP
equivalente) — não precisa ser `IAsyncDisposable`, só uma lista de
`AITool`.

### Decision 11: Lock consultivo segurado durante toda a espera — risco aceito

`ConversationContextLock.AcquireAsync` é adquirido em
`AgentExecutionService.ExecuteAsync` **antes** do `try` que envolve
`RunAsync`, e só libera no `await using` no fim do método — cobre
literalmente toda a duração da chamada ao LLM, incluindo qualquer tool
call, incluindo a espera pela delegação (Decision 8).

Consequência: o Source segura seu próprio lock `(agentId, contextId)`
durante a cadeia de delegação inteira (potencialmente múltiplos
segundos a até o timeout de 120s, multiplicado pela profundidade da
cadeia se cada nível também delegar). Uma segunda mensagem de usuário
para o mesmo Source, no mesmo `contextId`, fica esperando na fila até a
cadeia inteira terminar (ou o Postgres aceitar o próximo
`pg_advisory_lock` na mesma chave).

**Risco aceito, não bug** — mesmo padrão de registro já usado em toda
decisão de lock anterior deste projeto. Justificativa: resolver isso
exigiria desacoplar "segurar o lock" de "estar dentro de `RunAsync`",
o que não é possível sem também resolver a Decision 1 (tool bloqueante
== instância presa) de um jeito não-bloqueante — mesma alternativa já
descartada ali pelo mesmo motivo (fora de escopo desta fatia).

## Estrutura de Arquivos

```
apps/workers/
├── src/Buteco.Workers/
│   ├── AgentDelegations/                                    (novo módulo)
│   │   ├── Entities/
│   │   │   └── AgentDelegation.cs                           (novo: mirror read-only, Decision 3)
│   │   ├── IAgentDelegationToolSetResolver.cs                (novo: Decision 10)
│   │   ├── AgentDelegationToolSetResolver.cs                 (novo: Decision 10)
│   │   ├── DelegationToolNameSlugifier.cs                    (novo: mirror de AgentSkillMapper.Slugify, Decision 9)
│   │   └── DelegationDepth.cs                                (novo: leitura/escrita de Metadata["delegationDepth"], Decision 6)
│   ├── Agents/
│   │   ├── AgentExecutionService.cs                          (editado: integra resolver de delegação, checa profundidade no início)
│   │   └── Entities/
│   │       └── Agent.cs                                      (editado: +IsActive, ver achado da Decision 3)
│   ├── Messaging/
│   │   ├── ITaskJobPublisher.cs                               (novo: mirror de apps/api)
│   │   └── RabbitMqTaskJobPublisher.cs                        (novo: mirror de apps/api, Decision 4)
│   ├── Infrastructure/
│   │   └── AppDbContext.cs                                    (editado: +DbSet<AgentDelegation>, +mapeamento, +Agent.IsActive)
│   └── Program.cs                                              (editado: registro de DI dos itens acima)
└── tests/Buteco.Workers.Tests/
    ├── AgentDelegationExecutionTests.cs                        (novo: round-trip, profundidade, timeout, degradação)
    ├── AgentDelegationConcurrencyTests.cs                      (novo: duas instâncias + instância única com timeout curto, Decision 1)
    └── DelegationToolNameSlugifierTests.cs                     (novo: colisão de Agent.Name, Decision 9)
```

## Risks / Trade-offs

- **[Risco] Requisito de ≥ 2 réplicas não é enforced em nenhuma
  configuração hoje** → Mitigação: documentado nesta Decision 1 e no
  `proposal.md`; teste de concorrência (task 5) prova o comportamento
  correto com 2 instâncias e o timeout gracioso com 1. Fica registrado
  como Open Question se vale adicionar um health-check/aviso em runtime
  quando só uma instância está ativa (fora de escopo detectar isso sem
  coordenação externa — nenhum mecanismo de descoberta de réplicas
  existe no projeto).
- **[Risco] Cadeias de delegação profundas consomem N réplicas
  simultaneamente presas em espera** (cada nível ocupa uma instância
  até resolver) → Mitigação: `DelegationDepthLimit = 5` (Decision 6)
  limita o pior caso; documentado como trade-off consciente, não
  resolvido operacionalmente (equivale a dizer que o ambiente precisa
  de réplicas suficientes para a profundidade máxima configurada, não
  só 2).
- **[Risco] Lock do Source seguro durante toda a cadeia de delegação**
  (Decision 11) → Mitigação: nenhuma nesta fatia, risco aceito e
  documentado; usuário do Source percebe como latência na segunda
  mensagem, não como erro.
- **[Trade-off] Checagem fresca de `IsActive`/`Provider`/`Model` do
  Target duplica uma verificação que o `catch` genérico já cobre**
  (Decision 4) → Aceito conscientemente: custo de manutenção pequeno
  (mesmas três checagens já replicadas em outro lugar do projeto),
  ganho de latência real (evita um round-trip inteiro de fila+lock+task
  para um Target já sabidamente inválido).

## Open Questions

- Vale expor `DelegationDepthLimit` e o timeout de 120s como
  `IOptions` configurável por ambiente (não por agente) em vez de
  `const`, para permitir ajuste em produção sem rebuild? Mantido como
  `const` nesta fatia, mesmo padrão de `MaxHistoryMessages`/
  `SummarizationTurnThreshold` — decisão de produto, não bloqueia o
  desenho técnico.
- Se o número de réplicas de `apps/workers` precisar ser imposto por
  infraestrutura (ex. `docker-compose.yml`/orquestrador) em vez de só
  documentado em prosa, isso é trabalho de uma change de infraestrutura
  separada — fora do escopo desta fatia, que é sobre `apps/workers` em
  si.
