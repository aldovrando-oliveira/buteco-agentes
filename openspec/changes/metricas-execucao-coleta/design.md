## Context

Etapa 1 da linha `metricas-de-operacao`. O desenho de partida é o da exploração
de **20/09/2026** (V1–V5): duas tabelas — execução de task (pai) e chamada ao
provedor (filha) — e o `taskId` chegando a `LlmCallDurationChatClient` por
`AsyncLocal`, porque o client é compartilhado por `(provider, model)` durante a
vida do processo e wrapper por chamada reintroduz o vazamento que
`fix-vazamento-httpclient-chat` corrigiu. **O relatório da exploração não está
no repositório** — foi recuperado do transcript da sessão de 20/09 para esta
proposta; as citações dele aqui foram todas reconferidas no código de hoje.

Cinco changes entraram depois da exploração e mexeram nos arquivos que ela leu.
Número de linha dela não vale sem conferência; a tabela abaixo é a conferência,
feita em 21/09/2026 sobre `main` em `2ee34d3`.

### Reverificação — o que mudou desde a exploração

| # | ponto | o que a exploração dizia | o que o código diz hoje | efeito no desenho |
|---|---|---|---|---|
| 1 | caminhos sem chamada ao provedor | rejeição por profundidade em `AgentExecutionService.cs:117-137`, antes do `try` (`:158`); `catch` em `:301` | a `lock-de-contexto-falha-terminal` deu `try/catch` **próprio** à aquisição do lock (`:180-200`) e extraiu `FailTaskAsync` (`:373-388`). Profundidade agora em `:122-138`, `try` principal em `:204`, `catch` em `:344`. Dentro do `try`, antes de `RunAsync` (`:317`): resolução do client (`:229`, os quatro `throw` de `ChatClientResolver.cs:125,140,155,167` — linhas **iguais** às da exploração), das tools (`:236-255`) e carga da sessão (`:315`) | **três** caminhos terminais sem chamada: profundidade (`Rejected`), lock (`Failed`), e falha no `try` antes de `RunAsync` (`Failed`). A linha pai nasce **antes** de todos eles (D3) e carrega a **fase** da falha (D12) |
| 2 | origem da task delegada | `DelegationDepth.cs:16` só grava `delegationDepth`; `sourceAgentId` em escopo e não persistido (`AgentDelegationToolSetResolver.cs:99,122`) | a `delegacao-diagnostico` fez `ResolveAsync` receber o `sourceTaskId` (`IAgentDelegationToolSetResolver.cs:56-63`), que chega até `WaitForTerminalStateAsync` (`:134`) — **só para o log**. `CreateDelegatedTaskAsync` (`:155-194`) continua gravando **só** `delegationDepth` (`:187-190`) | a origem **não** está na task do alvo. Precisa de duas chaves novas no mesmo `Metadata`, no mesmo ponto (D6). O `sourceTaskId` já em mãos é o que torna a segunda chave gratuita |
| 3 | instante de enfileiramento | só considerou "quem publica grava" (`apps/api` escrevendo na tabela nova) | `ExecuteAsync` lê a task em `:107` (`GetTaskWithRetryAsync`) e só transiciona para `working` em `:140-146` (`StartWorkAsync`). Decompilado do `A2A 1.0.0-preview2`: `TaskUpdater.SubmitAsync` carimba `Timestamp = DateTimeOffset.UtcNow` com `State = Submitted`; `TaskProjection.Apply` num evento de **mensagem** só acrescenta ao `History` e **não toca** `Status` | o worker lê o instante do `submitted` **antes** de sobrescrevê-lo. A change fica inteira em `apps/workers` mais a migração (D4) |
| 4 | detector e log de desistência | — (não existiam) | `NonTerminalTaskDetectorService` e o log de `WaitForTerminalStateAsync` existem desde a `delegacao-diagnostico` (D8 dela: duplicação temporária consciente, sucessor nomeado — esta change) | **não removidos aqui.** Condição e momento da remoção em D11 |
| 5 | embedding | uma chamada por documento | a `indexacao-lote-de-fragmentos` loteou: `KnowledgeIndexingService.cs:185`, `fragments.Chunk(batchSize)`, uma requisição por lote | fora do escopo (etapa 2), mas a tabela filha precisa **comportar** uma linha por lote (D7) |

Conferidos também, sem mudança: `TaskJobConsumer.cs:42` (`prefetchCount: 1`),
`:52` (`ExecuteAsync` inline), `:64` (`autoAck: false`) — continua havendo no
máximo **uma** task de `agent-tasks` em voo por processo; `AgentDelegationToolOptions.cs:16-18`
(`PollInterval` 1 s, `Timeout` 120 s).

## Goals / Non-Goals

**Goals:**

- Toda task **consumida** por `apps/workers` produz uma linha pai, inclusive as
  que terminam sem nenhuma chamada ao provedor.
- Toda requisição HTTP ao provedor de LLM feita dentro de uma execução produz uma
  linha filha, inclusive a de compactação e as que falham.
- Toda delegação disparada produz uma linha de resultado, com o último estado
  observado do alvo — o dado que torna o `C` da `replicas-de-worker` consultável
  e que dá fonte ao motivo "delegação expirada" do card aprovado.
- Nulo preservado de ponta a ponta nos tokens (convenção 13).
- Nenhuma falha de telemetria altera o estado terminal da task (convenção 4).
- Custo de DI **zero** nos harness de teste (nenhum construtor muda).

**Non-Goals:** os da proposta. Em particular: nenhuma rota, nenhuma tela, nada de
embedding, detector e log de desistência **ficam**, `a2a_tasks` não muda.

## Decisions

### D1 — Três tabelas, com o grão de cada uma

`apps/api` migra, `apps/workers` escreve (precedente `KnowledgeFragment`: o
`migrator` de produção só empacota bundles de `apps/api` e `apps/inbox`). Nomes de
tabela em `snake_case`, colunas em `PascalCase` — o idioma de `knowledge_*`.
Enumerações gravadas como **texto**, nunca ordinal (convenção 12).

**`task_executions`** — uma linha por execução de task consumida.

| coluna | tipo | nulo | significado |
|---|---|---|---|
| `TaskId` | `text` PK | não | id da task A2A |
| `AgentId` | `uuid` | não | agente que executou — **sem FK** para `agents` (D13) |
| `ContextId` | `text` | não | |
| `Provider`, `Model` | `text` | **sim** | snapshot do agente no início; nulo = agente sem provedor ("precisa de reconfiguração") |
| `Origin` | `text` | não | `External` \| `Delegation` |
| `SourceAgentId` | `uuid` | sim | só quando `Delegation` |
| `SourceTaskId` | `text` | sim | só quando `Delegation` |
| `DelegationDepth` | `integer` | não | lida de `delegationDepth` |
| `SubmittedAt` | `timestamptz` | **sim** | instante do `submitted` (D4); nulo = a task não estava em `Submitted` quando lida |
| `StartedAt` | `timestamptz` | não | início da execução no worker |
| `LockAcquiredAt` | `timestamptz` | sim | nulo = o lock não foi adquirido (profundidade, falha do lock, ou execução aberta) |
| `EndedAt` | `timestamptz` | sim | nulo = execução **aberta** (sem estado terminal gravado por ela) |
| `TerminalState` | `text` | sim | `Completed` \| `Failed` \| `Rejected`; nulo junto com `EndedAt` |
| `FailurePhase` | `text` | sim | só em `Failed`/`Rejected` (D12) |

Índices: `AgentId`, `StartedAt`. As consultas óbvias das duas telas filtram por
período e por agente; índice é barato de acrescentar depois, mas estes dois têm
consumidor certo.

**`provider_calls`** — uma linha por requisição HTTP ao provedor.

| coluna | tipo | nulo | significado |
|---|---|---|---|
| `Id` | `uuid` PK | não | |
| `TaskId` | `text` | não | FK → `task_executions`, `Restrict` |
| `Provider`, `Model` | `text` | não | snapshot do client que fez a chamada (D13) |
| `Purpose` | `text` | não | `Turn` \| `Compaction` (D8); texto, para a etapa 2 acrescentar valor sem migrar |
| `DurationMs` | `double precision` | não | mesma medida que o log de hoje |
| `InputTokens`, `OutputTokens`, `CachedInputTokens` | `bigint` | **sim** | nulo = o provedor não reportou; **nunca** normalizado para zero |
| `Failed` | `boolean` | não | a requisição lançou |
| `HttpStatus` | `integer` | sim | só quando o SDK o expõe **tipado** (D12); nulo = não se sabe |

Índice: `TaskId`.

**`delegation_outcomes`** — uma linha por delegação disparada (D5).

| coluna | tipo | nulo | significado |
|---|---|---|---|
| `Id` | `uuid` PK | não | |
| `SourceTaskId` | `text` | não | FK → `task_executions`, `Restrict` |
| `SourceAgentId`, `TargetAgentId` | `uuid` | não | sem FK (D13) |
| `TargetTaskId` | `text` | sim | nulo = a task do alvo nunca foi criada (`NotStarted`) |
| `Outcome` | `text` | não | `Completed` \| `TargetUnsuccessful` \| `Expired` \| `NotStarted` |
| `LastObservedTargetState` | `text` | sim | último estado **lido** do alvo; nulo = nenhuma leitura devolveu linha |
| `LastObservedAt` | `timestamptz` | sim | quando foi lido |
| `SuccessfulReadCount` | `integer` | não | separa "não sei" (0) de "a linha não estava lá" (> 0 com estado nulo) |
| `DurationMs` | `double precision` | não | da criação da task alvo ao desfecho; é a parte do "tempo em tools" que é espera de delegação |

Índices: `SourceAgentId`, `TargetAgentId` — são as duas seções do card de
delegação.

**Alternativas recusadas:** uma tabela só (grão de chamada perde a falha com zero
chamadas; grão de task perde duração por chamada e distribuição por modelo —
V3 da exploração); gravar em `a2a_tasks.Payload` (`jsonb` sem índice GIN, e
`Update` reescreve o payload inteiro a cada transição — §3 da exploração).

### D2 — Captura por `AsyncLocal`, e nenhum construtor muda

Um `ExecutionMetricsScope` guardado num `AsyncLocal` estático é aberto em
`AgentExecutionService.ExecuteAsync` e acumula, em memória, as chamadas ao
provedor (escritas por `LlmCallDurationChatClient`) e os resultados de delegação
(escritos pela tool de delegação). Acesso sob `lock`: `FunctionInvokingChatClient`
pode invocar duas tools do mesmo turno em paralelo — o próprio
`AgentExecutionService` já registra isso para `knowledgeTools`.

**`ExecuteAsync` sobrescreve o escopo, nunca herda.** Se o `ExecutionContext` de
um callback do consumidor algum dia vier contaminado (hoje não vem: o dispatcher
do RabbitMQ roda com o contexto de quando o consumidor foi registrado), a execução
seguinte ainda grava no escopo dela. Chamada **fora** de qualquer escopo (teste
unitário do client, por exemplo) é ignorada, sem lançar.

**Custo de DI: zero, por desenho.** O escopo é estático; a escrita usa o
`IServiceScopeFactory` e o `ILogger` que `AgentExecutionService` **já** recebe; o
client lê o `AsyncLocal` sem parâmetro novo; a tool de delegação idem. Nenhum
construtor de `AgentExecutionService`, `ChatClientResolver`,
`LlmCallDurationChatClient` ou `AgentDelegationToolSetResolver` muda. **Medição
por compilação, não por `grep`** (régua da oitava e da décima medições): a
tarefa 6.1 compila a solução inteira e confere que **nenhum registro de DI
existente muda** nos harness que registram `AgentExecutionService` — **14
arquivos de teste com 15 registros** — 13 arquivos em `apps/workers/tests/` (14 registros:
`TaskJobConsumerTests` tem dois) mais `tests/InboxOrchestratorRoundTrip.Tests/Support/RoundTripFixture.cs`
—, contados em 21/09/2026 sobre `2ee34d3`, por busca que exclui comentário.
*(A régua circulava como "13 sítios, 12 + 1" — convenção 22 do `01` e
`02`. Está defasada por um arquivo: a `lock-de-contexto-falha-terminal`
(`ff882ce`, 20/09) criou `ConversationContextLockFailureTests`, que registra o
serviço, e ninguém recalibrou. É a própria convenção 22 falhando sobre o exemplo
dela: recalibrar era tarefa da change que mudou o estado.)* Lembrete de por que
a compilação sozinha não bastaria se houvesse dependência nova: dependência de
DI quebra em *runtime*, não em compilação (exploração, §3) — aqui não há, e a
suíte verde é a segunda metade da conferência.

**Alternativa recusada:** `IExecutionMetricsWriter` injetado. Custaria os 14
sítios sem factory compartilhada, e não há segundo implementador.

### D3 — Duas escritas: abrir cedo, fechar depois do estado terminal

1. **Abre** (INSERT da linha pai, em escopo de DI próprio) logo depois de
   `GetTaskWithRetryAsync` devolver a task (`:107`) — antes da checagem de
   profundidade, do lock e do `try`. É o primeiro ponto em que se sabe
   `SubmittedAt` e origem, e é anterior a **todos** os caminhos terminais do
   item 1 da reverificação.
2. **Fecha** num `finally` no fim de `ExecuteAsync`, depois do `SaveTaskAsync`
   terminal e do push notification: atualiza a linha pai (`EndedAt`,
   `TerminalState`, `FailurePhase`) e insere filhas e delegações num único
   `SaveChangesAsync`. Se a abertura tinha falhado, o fechamento **insere** a
   linha pai.

**Por que depois do estado terminal, e não antes:** qualquer exceção da escrita
acontece quando o estado já está no banco — não há ordem de execução em que ela
altere o que a task terminou sendo. As duas escritas ficam em `try/catch` que
registra `LogWarning` com chave estruturada e **não relança**.

**O que um crash no meio deixa:** a linha pai aberta (`EndedAt` nulo) e nenhuma
filha. É **o** dado de "execução sem estado terminal", e ele não existiria se a
linha só nascesse no fim. Filhas perdidas nesse caso são o custo aceito de não
fazer uma ida ao banco por requisição ao provedor.

**Dois caminhos continuam sem linha, e são nomeados:** agente não encontrado
(`:87-91`) e task não encontrada (`:108-112`) retornam **antes** de haver task
lida — e sem gravar estado terminal nenhum. Não são "falha antes de chamada ao
provedor": são tasks que nunca foram executadas, e ficam visíveis pelo mesmo
lugar que as nunca consumidas (D11).

**Cancelamento de shutdown:** o fechamento roda com `CancellationToken.None` e
prazo curto próprio, para não ser abortado pelo mesmo token que encerrou a
execução.

### D4 — O instante do `submitted` é lido pelo worker

**Decidido:** `SubmittedAt = task.Status.Timestamp` **quando**
`task.Status.State == Submitted` na leitura de `:107`; nulo caso contrário.

A ordem foi conferida (reverificação, item 3): a leitura acontece em `:107`, a
transição em `:140-146`. Entre as duas, a única escrita na task é a de mensagem
em `apps/api` (`EnqueueingAgentHandler.cs:57`), que `TaskProjection` aplica ao
`History` sem tocar `Status` — então o carimbo lido é o do `SubmitAsync`
(`EnqueueingAgentHandler.cs:18`, ou `AgentDelegationToolSetResolver.cs:161` para
task delegada).

**Por que nulo, e não o carimbo que houver:** numa reentrega (worker morreu
depois do `StartWorkAsync`), a task lida já está em `Working`, e o carimbo é o
do início da tentativa anterior. Gravá-lo como `SubmittedAt` afirmaria um tempo
de fila que não foi medido (convenção 13).

**Relógios:** `SubmittedAt` vem do relógio de quem submeteu (`apps/api`, ou o
worker do Source); `StartedAt` do `TimeProvider` do worker que executa. Todos os
processos rodam no mesmo host em produção; a diferença é de relógio de container,
não de máquina.

**Alternativa recusada:** `apps/api` gravar na tabela nova. Põe o segundo app
escrevendo, e a task delegada (criada no worker) exigiria um segundo escritor
para a mesma coluna.

### D5 — Resultado de delegação é terceira tabela — a condição da reordenação

**Decidido:** tabela própria, `delegation_outcomes`. **A condição da
reordenação da fila está satisfeita**, e ficou registrada no `02`.

- **Coluna na linha pai: recusada.** Uma task do Source pode disparar **N**
  delegações, inclusive em paralelo no mesmo turno. Coluna é 1:1.
- **Linha na tabela filha: recusada.** A filha é uma requisição HTTP ao
  provedor; uma delegação não é, e misturar os grãos faria toda soma de
  `DurationMs` ou de tokens precisar de filtro para não contar espera de
  delegação como chamada.

A tool de delegação tem **cinco** saídas, e cada uma vira um desfecho:

| saída hoje | `Outcome` | `LastObservedTargetState` |
|---|---|---|
| Source inativo/sem provedor (`:101-108`) | `NotStarted` | nulo, `SuccessfulReadCount = 0` |
| Target indisponível (`:113-120`) | `NotStarted` | idem |
| falha ao criar a task alvo (`:126-130`) | `NotStarted` | idem |
| alvo terminou `Completed` (`:275-279`) | `Completed` | `Completed` |
| alvo terminou `Failed`/`Rejected`/`Canceled` (`:281-292`) | `TargetUnsuccessful` | o estado terminal lido |
| timeout (`:298-306`) | `Expired` | o último lido, ou nulo |

**`Submitted` como último estado de uma `Expired` é contenção** — é o `C`, pela
mesma leitura que a `delegacao-diagnostico` fixou para o detector (D10 dela: a
coluna que responde é a de `Submitted`, nunca a de `Working`).

**O que esta tabela não cobre, e é nomeado:** cancelamento da execução inteira
(shutdown) propaga a `OperationCanceledException` e **não** gera linha — a
delegação estava em curso e não teve desfecho.

### D6 — Origem gravada no `Metadata` da task delegada

**Decidido:** `CreateDelegatedTaskAsync` grava, ao lado de `delegationDepth`,
`delegationSourceAgentId` e `delegationSourceTaskId`. Um codec
`DelegationOrigin` no idioma de `DelegationDepth`. O worker do alvo lê e grava
`Origin = Delegation` com os dois campos; ausência das chaves é `External`.

**Por que não inferir da profundidade:** `DelegationDepth.Read` devolve 0 para
chave ausente **por desenho**, então profundidade não distingue "raiz" de algo
que nunca gravou a chave (exploração, §3). **Por que não derivar de
`delegation_outcomes`:** a linha de resultado é escrita pelo **Source**, no fim
da execução **dele** — pode chegar depois da linha do alvo, ou nunca (crash do
Source). V5 pede a origem **na mesma linha** do tempo de fila.

**Por que chave no `Metadata` da task, e não no da mensagem:** é o `Metadata`
que o worker já lê para a profundidade, e `apps/api` nunca o preenche a partir
do cliente — `EnqueueingAgentHandler` só chama `SubmitAsync`. Uma chave de
mensagem viria de um cliente A2A externo.

### D7 — A tabela filha comporta o lote de embedding (etapa 2), sem construir para ele

O grão é **uma requisição**, e um lote é uma requisição. `OutputTokens` já é
anulável — embedding não tem saída, e nulo é o que ele tem de gravar, não zero.
`Purpose` é texto. **O que a etapa 2 vai precisar mudar, e é barato:** indexação
não roda dentro de task, então `TaskId` passa a anulável e ganha uma referência
de documento. `DROP NOT NULL` não reescreve a tabela. Fazer isso agora seria
coluna sem escritor (convenção 2). A busca de conhecimento **dentro** de uma task
(um vetor por mensagem) já cabe sem mudança: roda no fluxo da execução, e o
`AsyncLocal` a alcança.

### D8 — Finalidade: marcador explícito para a chamada de compactação

`AgentExecutionService` passa o **mesmo** `chatClient` à
`SummarizationCompactionStrategy` (`:308-310`), e o client não tem como saber
quem o chamou. **Decidido:** a estratégia recebe um `CompactionCallChatClient`
fino que marca `Purpose = Compaction` no `AsyncLocal` durante a chamada e
delega.

**Ele implementa `IChatClient` direto, NÃO deriva de `DelegatingChatClient`, e
`Dispose` é no-op:** `DelegatingChatClient.Dispose()` descarta o `InnerClient` em
cascata, e o inner aqui é o client **compartilhado** do processo
(`ChatClientResolver.cs:114-117`). Hoje nada o descartaria — decompilado,
`SummarizationCompactionStrategy` guarda o client e só chama
`GetResponseAsync(list, null, ct)` (`:164`), sem `Dispose` —, mas a garantia não
pode depender de ninguém escrever `using` nele no futuro.

**Alternativa recusada:** distinguir por `options is null` (a compactação passa
`null`, o turno passa `ChatOptions`). É propriedade da versão instalada do
pacote, não contrato; a próxima versão pode passar opções, e a finalidade
mudaria de valor em silêncio.

### D9 — Os dois caminhos do client são instrumentados (D6 da exploração)

`GetStreamingResponseAsync` é caminho morto no fluxo do agente (`RunCoreAsync`
usa `GetResponseAsync`). **Decidido: instrumentar os dois.** A classe já trata
os dois simetricamente para duração; deixar tokens só num deles é a assimetria
pronta para enganar quem ligar streaming. No streaming, tokens vêm do
`UsageContent` que aparece nos updates — somados com a mesma semântica de nulo
de `UsageDetails.Add`. Custo: um cenário de teste.

### D10 — Índice em `a2a_tasks.status_timestamp` não entra (D4 da exploração)

Esta etapa **não consulta** `a2a_tasks` — lê a task pelo `ITaskStore` que já lia.
O índice fica para a etapa 3, e **ela vai precisar dele**, por dois motivos que
o mapa abaixo nomeia: recusas de `apps/api` e tasks nunca consumidas só existem
em `a2a_tasks`.

### D11 — Detector e log de desistência: condição de remoção, não remoção

Registrado em **21/09/2026**, no comentário de `NonTerminalTaskDetectorService`
e aqui.

**O detector sai quando** a rota da etapa 3 que serve **M32** (tasks sem estado
terminal) cobrir as **duas** populações que ele cobre hoje: execução aberta
(`task_executions.EndedAt` nulo, desta change) **e** task `Submitted` nunca
consumida — que não tem linha pai, porque a linha nasce no consumo. A segunda
população só existe em `a2a_tasks`, e é exatamente a que responde o `C`. Logo a
rota M32 lê `a2a_tasks`, com o índice de D10.

**O log de desistência sai quando** `delegation_outcomes` tiver todo campo que
ele tem — por isso `LastObservedAt` e `SuccessfulReadCount` estão na tabela — **e**
existir consulta que o substitua. Mesma etapa 3.

**Momento:** na change da etapa 3, **não antes de a `replicas-de-worker` ter
fechado sua decisão de capacidade.** A decisão dela vai ser tomada sobre o `C`
lido das duas fontes em paralelo; é essa coexistência que prova que as duas leem
o mesmo número. Remover antes seria trocar o instrumento no meio da medição.

### D12 — Motivo da falha classificado por fase, não por mensagem

A exploração sugeriu partir de `KnowledgeIndexingFailure.Describe`. **Recusado
para esta etapa:** ele produz **texto de operador**, não categoria, e distingue
os quatro `throw` de `ChatClientResolver` por `Message.Contains` — é
exatamente o parse de string que a pergunta pedia para evitar.

**Decidido:** `AgentExecutionService` registra a **fase** em que estava quando
falhou, numa variável local atualizada antes de cada passo. Estável por
construção:

| `FailurePhase` | caminho |
|---|---|
| `DelegationDepthExceeded` | `:122-138` (`Rejected`) |
| `ContextLock` | `:186-200` |
| `ChatClientResolution` | `:229` |
| `ToolResolution` | `:236-255` |
| `SessionLoad` | `:315` |
| `AgentRun` | `:317` — inclui as chamadas ao provedor |
| `Persistence` | `:324-340` |

O detalhe da chamada que falhou fica na **filha** (`Failed`, `HttpStatus`).
`HttpStatus` só quando tipado: `HttpRequestException.StatusCode` e
`System.ClientModel.ClientResultException.Status` (verificados na exploração);
as exceções próprias do SDK da Anthropic ficam com nulo — não se sabe, e o nulo
diz isso.

### D13 — Snapshot de provedor e modelo, sem join com `agents`

Provedor e modelo são gravados **na linha**: na filha, os do client que fez a
chamada (`LlmCallDurationChatClient` já os recebe); na pai, os do agente lido no
início. **Nenhuma das três tabelas tem FK para `agents`**, de propósito: o
consumo de ontem pertence ao modelo de ontem, e uma FK convidaria a consulta a
fazer join e ler o modelo **de hoje**. O guarda é duplo (convenção 15, quinta
forma): o comportamental (agente troca de modelo, a linha antiga não muda) e o
estrutural (o modelo do EF não tem chave estrangeira dessas tabelas para
`Agent`).

### D14 — O regime na série

A métrica não é retroativa (o `submitted` é destruído; o payload não guarda
histórico de status — exploração, §3). **O início da série é a data do deploy
desta change em produção**, registrado no `02` no dia, com o fuso, no texto que a
tela aprovada exibe: *"medindo desde DD/MM/AAAA, `America/Sao_Paulo`"*. A data
também é derivável do dado (`min(StartedAt)` convertido para o fuso) — a
consulta confere o registro, não o substitui.

**Segundo marco de regime, já previsto:** o deploy da `replicas-de-worker`. O
tempo de fila de task delegada muda de significado ali, e a série se divide
pela data, registrada no `02` por **aquela** change (convenção 22: recalibrar é
tarefa de quem muda o estado).

### D15 — Guardas vermelhos, e onde cada um mora

Convenção 15: vermelho contra `HEAD` **pela propriedade**, no componente que a
correção toca. Contra `HEAD` puro os guardas nem compilam (as entidades não
existem) — vermelho de compilação não prova nada. **Por isso a ordem:** o schema
entra primeiro (grupo 2 do `tasks.md`), e os guardas são vistos reprovando
contra `HEAD` + schema, com as tabelas **vazias** — é a propriedade que falta.

Cada guarda mora na classe que **já tem o arranjo**, e não numa classe nova da
`WorkerHostCollection`: são **14** classes, medidas por busca ancorada em
`^\[Collection(` (que exclui comentário — régua textual da décima medição), e a
15ª obrigaria a recalibrar o limiar de carga da suíte (convenção 22, ocorrências
1 e 4). Os testes novos que não precisam de host (escopo, espelho de schema) são
classes fora da coleção.

**Dado de teste de delegação:** o banco de dev tinha **zero** delegações e o do
piloto tem poucas. Os guardas de origem e de resultado **semeiam o grafo** —
agentes e `agent_delegations` inseridos direto, como os testes de delegação de
`apps/workers` já fazem, no precedente do helper de semeadura direta da
`delegacao-ciclo-no-cadastro`. Os quatro estados de desfecho reaproveitam o
arranjo dos quatro guardas de log da `delegacao-diagnostico`
(`AgentDelegationConcurrencyTests.cs:199,247,314,374`): mesmo cenário, e a linha
é o gêmeo durável do log.

### D16 — "Acionado por" conta execuções do alvo; "Delega para" conta tentativas do Source

**Fechada na proposta, e não na etapa 4, porque o protótipo aprovado já decide
— e a decisão diz o que esta etapa precisa gravar.** No cenário 3 aprovado
(`Cobrança`), a aba mostra *"Tasks executadas 97 · 97 por delegação"* e
*"Acionado por: Triagem 70, Atendente 27"*; no cenário 2 (`Financeiro`),
65 = 51 + 14. Os números **somam** com "Tasks executadas" nas duas telas, e isso
só acontece contando **execuções do alvo**.

**Decidido:**

- **"Acionado por"** (lado do alvo) lê **`task_executions`**: linhas com
  `Origin = Delegation` do agente, agrupadas por `SourceAgentId` — o que de fato
  **rodou** ali. Conferido contra o desenho: toda linha com
  `Origin = Delegation` carrega `SourceAgentId`, porque as duas saem das mesmas
  chaves de `Metadata` (D6), gravadas juntas na criação da task delegada, e o
  requisito "Origem gravada na linha da execução" da spec as exige juntas.
- **"Delega para"** (lado do Source) lê **`delegation_outcomes`**: linhas com
  `SourceAgentId` do agente, agrupadas por `TargetAgentId` — o que ele
  **tentou**.

**A assimetria é o que a etapa 4 vai estranhar, e por isso fica escrita aqui:**
com `NotStarted` ou `Expired` no meio, **os dois lados da mesma relação mostram
números diferentes** — "Triagem delega para Cobrança: 75" e "Cobrança é acionado
por Triagem: 70" podem estar os dois certos. Não é defeito: são perguntas
diferentes (tentado × executado). `NotStarted` não cria task, e `Expired` com
último estado `Submitted` criou uma que talvez nunca rode. **O card não pode se
parecer com um espelho** — se as duas seções forem desenhadas como a mesma
relação vista de dois lados, o operador lê a diferença como erro. A etapa 4
recebe isto como restrição de desenho, antes de desenhar.

**Recusado:** contar "Acionado por" por delegações disparadas. Incluiria
`NotStarted`, que não cria task, e a soma com "Tasks executadas" quebraria na
mesma tela aprovada.

### D17 — Task lida em estado terminal não executa (escopo 2 desta change)

**Escopo 2, declarado na proposta: defeito pré-existente que esta change
alargou.** Não é coleta; está aqui porque a coleta o tornou visível e porque a
suíte desta change não fecha sem ele.

**O que apareceu no apply.** A suíte completa de `apps/workers` deu 317/319, e as
duas classes de delegação isoladas reprovaram com um conjunto de testes que
**mudava a cada rodada** — contra **17/17 em três rodadas** das mesmas classes em
`HEAD` (worktree limpa, `2ee34d3`, 21/09/2026). Regressão desta change, não
pré-existente.

**A hipótese foi isolada por experimento antes de virar causa** (não por leitura
— esta base já registrou hipótese plausível tomada como causa). Instrumentação
**temporária**, ligada por variável de ambiente e revertida depois: por mensagem
consumida, o `TaskId`, a flag `Redelivered` do próprio RabbitMQ e o estado lido;
mais os avisos de falha de gravação de métrica. Três rodadas das duas classes, e o
resultado:

| rodada | resultado | reentregas | falhas de gravação de métrica |
|---|---|---|---|
| 1 | 15/20 | 10 | 12 |
| 2 | 17/20 | 5 | 6 |
| 3 | 19/20 | 3 | 3 |

- **Toda falha de gravação foi `23505` (chave duplicada) na abertura da linha
  pai, e TODA coincidiu com uma reentrega** — nenhuma sem. É a assinatura que
  discrimina da alternativa (vazamento de `AsyncLocal` entre execuções no mesmo
  processo): vazamento atribuiria chamadas ao `TaskId` errado **sem** segunda
  execução; reentrega produz segunda execução do mesmo `TaskId`, e a chave
  primária da linha pai a denuncia.
- **As tasks reentregues foram lidas em `Completed` (ou `Failed`) e executadas de
  novo** — o `Working` que aparece depois nos estados lidos é a reexecução
  gravando. O `TaskId` reentregue é sempre do **teste anterior** da classe (ou do
  próprio), e cai no teste seguinte, que o executa com os **mocks dele** (a chave
  provedor/modelo é a mesma entre testes) — o mock do Source gasta a chamada de
  delegação com a task velha, e o teste novo vê "nenhuma task do alvo".
- **As 9 falhas das três rodadas têm, todas, uma reentrega dentro da janela do
  teste.**

**O mecanismo:** os testes param o host assim que a task fica terminal. Esta change
acrescentou, entre gravar o estado terminal e confirmar a mensagem, a escrita de
fechamento da D3 — a janela passou de ~1 ms para dezenas de ms, e o host parado
dentro dela devolve a mensagem à fila. **O defeito é anterior**: nada impedia
reexecutar uma task que o RabbitMQ reentregasse já terminal; em produção, basta o
worker parar nessa janela.

**E o travamento de ~70 s no `StopAsync` é o mesmo gatilho mais um SEGUNDO achado,
pré-existente e não corrigido aqui.** Sequência medida (rodada 2): a task
reentregue é um Source `Completed` que delega; reexecutada num host de instância
única e prazo de delegação padrão (120 s), espera um alvo que nunca será
consumido. O teste chama `StopAsync` às 07.481; a execução só termina **60 s
depois** (30:07.492). A causa dos 60 s: `TaskJobConsumer.StopAsync` **fecha o
canal ANTES de chamar `base.StopAsync`**, que é quem cancela o token da execução —
o fechamento espera o callback em voo, e o callback espera um cancelamento que só
viria depois. É muito provavelmente o mesmo "1m10s em `Connection.CloseAsync`"
que a `delegacao-diagnostico` atribuiu à contenção da máquina. Registrado no `02`
como item aberto; não corrigido (fora do escopo, e com a guarda o gatilho de teste
some).

**Decidido:** task lida em estado terminal — `Completed`, `Failed`, `Rejected`,
`Canceled`, pelo predicado **do próprio protocolo** (`TaskStateExtensions.IsTerminal`,
o mesmo que o `A2AServer` usa) — **não executa**: log de aviso e `return`, e o
consumidor confirma a mensagem. **Antes** de abrir a linha pai, para a execução
que já tem a sua não ganhar outra. Reentrega de `Working` **continua executando**
— é a recuperação de worker que morreu no meio, e o `SubmittedAt` nulo (D4) já a
suporta.

**Por que é seguro, e a premissa virou guarda.** O risco oposto ao defeito é pior
que ele: se a guarda engolir mensagem legítima, **a conversa para de responder em
silêncio**. A redação do pedido supunha que a continuação de uma task concluída
chega como `Submitted` porque `SubmitAsync` sobrescreve o status. **A verificação
mostrou outra coisa, mais forte:** decompilado do A2A 1.0.0-preview2, o
`A2AServer.GuardTerminalState` **recusa** mensagem para task terminal
(*"Task is in a terminal state and cannot accept messages"*,
`UnsupportedOperation`) antes do `EnqueueingAgentHandler` rodar; e o inbox manda
só o `contextId`, nunca o `taskId`. A conversa continua em task **nova**, em
`Submitted`. Três guardas:

| guarda | onde | lado |
|---|---|---|
| task terminal reentregue não reexecuta (`[Theory]`, 4 estados) | `TaskJobConsumerTests` | **vermelho em `HEAD`**: *"Expected invocation once, but was 2 times"* nos 4 |
| a mensagem seguinte depois de uma `Completed` é task nova e executa | `TaskJobConsumerTests` | passa nos dois lados |
| mensagem para task terminal é recusada e não publica job (a premissa) | `A2ATaskLifecycleTests` (`apps/api`) | passa nos dois lados — reprova se uma versão do pacote deixar de recusar |
| reentrega de `Working` executa | `TaskJobConsumerTests` (já existia, 3.4) | passa nos dois lados |

**A âncora do guarda vermelho é uma task sentinela**, publicada depois da terminal
no mesmo host: com `prefetchCount: 1`, quando a sentinela termina, a terminal já
foi processada e confirmada — o mock tem de ter sido chamado **uma** vez. A
primeira redação ancorava em "fila vazia" e foi descartada antes de rodar: a
mensagem em voo sai da contagem de prontas ao ser entregue, e o guarda olharia
antes da reexecução, passando verde com o defeito presente (convenção 15).

**Resultado:** as duas classes de delegação **20/20 em três rodadas seguidas**
(~47 s cada, contra 40 s de `HEAD` com 17 testes).

**Consequência para a spec desta change:** o requisito "Linha de execução para
toda task consumida" dizia *uma linha por task que consumir e conseguir ler* — e
reentrega terminal é consumida e lida, então, como escrito, exigia segunda linha.
Redigido para "consumir, ler **e executar**", com cenário novo.

## Mapa: cada métrica do catálogo → a coluna que a produz

**A numeração é a do catálogo registrado no `02`**, seção "Linha de trabalho
`metricas-de-operacao` — catálogo de métricas (REFERÊNCIA VIVA)". Este mapa cita
contra aquela lista, e não o contrário: ele vai arquivado com a change, a lista
fica. *(A primeira redação deste mapa usava nomes em vez de números, porque o
catálogo só existia em conversa; registrá-lo no `02` fechou a tarefa de
reconciliação.)* **O catálogo tem 27 entradas** — M1 conta (gravada e com
fórmula, sem card) e M16a/M16b são duas. O "25" que abriu esta change é o número
da lista original, fixado antes de o catálogo crescer (tabela de passos no `02`;
convenção 22).

| M | métrica | fonte | etapa | observação |
|---|---|---|---|---|
| M1 | tasks de origem externa (legenda de M2) | `task_executions.Origin = External` | 1 | |
| M2 | tasks executadas | `task_executions` | 1 | só as **consumidas**; recusa de `apps/api` não entra (ver M27) |
| M6 | mapa de calor por dia da semana | `task_executions.StartedAt` | 1 (balde na 3) | fuso explícito na agregação (Q2 da exploração); sem hora, por definição |
| M7 | calendário do período | idem | 1 | |
| M9 | dia da semana de pico | derivada de M6 | 3 | nenhuma coluna própria |
| M10 | série diária de tasks e tokens | `StartedAt` da pai; tokens por Σ das filhas da task | 1 | o dia de um token é o dia em que a **task** começou |
| M11 | tokens de conversa | Σ `provider_calls` | 1 | |
| M12 | entrada × saída | `InputTokens`, `OutputTokens` | 1 | nulo ≠ zero |
| M13 | por agente | filhas ⋈ `task_executions.AgentId` | 1 | |
| M14 | por provedor (total conversa + embedding só aqui) | `provider_calls.Provider` | 1 **+ 2** | a parcela de embedding é da etapa 2 |
| M15 | por modelo | `provider_calls.Model` | 1 | snapshot, D13 |
| M16a | ranking de modelos por tokens | Σ por `Model` | 1 | |
| M16b | ranking de modelos por número de chamadas | `count(*)` por `Model` | 1 | |
| M17 | tokens por task, média e p95 | Σ por `TaskId`, agregado na 3 | 1 | inclui compactação; separável por `Purpose` |
| M19 | tokens de embedding | — | **2** | Non-Goal; D7 |
| — | cache lido (coluna da tabela de modelos) | `CachedInputTokens` | 1 | **sem número nem card**; célula **vazia** quando nulo, nunca `0` |
| ~~M18~~ | ~~cache escrito~~ | — | — | recusado por verificação de dado; **nenhuma coluna** |
| M21 | duração da task, `submitted` → terminal | `EndedAt − SubmittedAt` | 1 | **nulo em reentrega**, porque `SubmittedAt` é nulo (D4) |
| M22 | tempo de fila, `submitted` → `working` | `StartedAt − SubmittedAt` | 1 | **separar por `Origin`** (V5). `StartedAt` precede o carimbo de `working` por milissegundos (os dois no mesmo worker, sem ida ao banco entre eles além da abertura da linha). Rejeitada por profundidade nunca vira `working`: sai da conta por `FailurePhase = DelegationDepthExceeded` |
| M23 | duração da chamada ao provedor | `provider_calls.DurationMs` | 1 | |
| M24 | chamadas por task | `count(*)` por `TaskId` | 1 | separável por `Purpose` (Q3 da exploração) |
| M25 | tempo em tools | `(EndedAt − LockAcquiredAt) − Σ DurationMs` | 1 | **resíduo nomeado:** inclui carga/serialização da sessão e a gravação final; espera de delegação separável por `delegation_outcomes.DurationMs` |
| M26 | profundidade de delegação observada | `DelegationDepth` | 1 | |
| M27 | `failed` e `rejected` separados | `TerminalState` **e** `a2a_tasks` | 1 **+ 3** | **lacuna nomeada:** recusa de `apps/api` (agente inativo, sem provedor, provedor não configurado — `EnqueueingAgentHandler.cs:27-48`) nunca chega ao worker. A fonte é `a2a_tasks` (`state = Rejected`, estado terminal, carimbo não sobrescrito). A etapa 3 lê as duas |
| M28 | falhas por agente e por provedor/modelo | `AgentId`, `Provider/Model` da **pai** | 1 | a pai carrega o modelo porque falha com zero chamadas não tem filha |
| M29 | motivo da falha | `FailurePhase` + `delegation_outcomes.Outcome` | 1 | **"delegação expirada"** do card do cenário 3 sai de `Outcome = Expired` — a fonte que faltava |
| M30 | falhas de indexação | `knowledge_documents` (`IndexingStatus`, `FailureReason`) | já existe | fora desta etapa |
| M32 | tasks sem estado terminal | `EndedAt is null` **e** `a2a_tasks` | 1 **+ 3** | **lacuna nomeada:** `Submitted` nunca consumida não tem linha pai (D11) |
| M34 | delegações, par origem → destino | "Delega para": `delegation_outcomes`; "Acionado por": `task_executions` | 1 | **as duas seções leem fontes diferentes e não espelham** — D16 |

**Divergências achadas na conferência contra a lista, e corrigidas neste mapa:**

- **M21 estava errado.** A redação anterior media `EndedAt − StartedAt`; o
  catálogo define `submitted` → terminal. Corrigido para `EndedAt − SubmittedAt`
  — e herda o nulo de reentrega. As colunas já existiam; só a fórmula mudou.
- **"Chamadas sem reporte de cache" não é métrica do catálogo.** Entrou na
  primeira redação a partir do pedido da exploração de 20/09; o catálogo fechado
  diz que cache lido é **coluna sem número próprio**. Saiu do mapa. O dado
  continua derivável da mesma coluna, e o nulo continua preservado — é o que faz
  a célula vazia ser honesta.
- **M16b não tinha linha** (estava diluída em "tokens por provedor e modelo").
- **M9 e M14 não tinham linha própria**; M14 tem parcela na etapa 2.

**Nenhuma divergência pediu cenário novo na spec:** toda fórmula acima usa
colunas que a spec já exige.

## Árvore de pastas

`(novo)` marca arquivo criado; `(gerado)` marca saída do `dotnet ef`; o resto é
modificado.

```
buteco-agents/
├── apps/
│   ├── api/
│   │   ├── src/Buteco.Api/
│   │   │   ├── ExecutionMetrics/Entities/                 (novo)
│   │   │   │   ├── TaskExecution.cs                       (novo) só mapeamento
│   │   │   │   ├── ProviderCall.cs                        (novo)
│   │   │   │   └── DelegationOutcome.cs                   (novo)
│   │   │   └── Infrastructure/
│   │   │       ├── AppDbContext.cs                        # três entidades
│   │   │       └── Migrations/
│   │   │           ├── <ts>_AddExecutionMetrics.cs          (gerado)
│   │   │           ├── <ts>_AddExecutionMetrics.Designer.cs (gerado)
│   │   │           └── AppDbContextModelSnapshot.cs         (gerado)
│   │   └── tests/Buteco.Api.Tests/
│   │       └── ExecutionMetricsMigrationTests.cs          (novo)
│   └── workers/
│       ├── src/Buteco.Workers/
│       │   ├── ExecutionMetrics/                          (novo)
│       │   │   ├── ExecutionMetricsScope.cs               (novo) AsyncLocal + acumulador
│       │   │   ├── ExecutionMetricsWriter.cs              (novo) abrir/fechar, degradação
│       │   │   ├── ExecutionMetricsValues.cs              (novo) vocabulário gravado como texto
│       │   │   ├── CompactionCallChatClient.cs            (novo) marcador de finalidade
│       │   │   └── Entities/
│       │   │       ├── TaskExecution.cs                   (novo)
│       │   │       ├── ProviderCall.cs                    (novo)
│       │   │       └── DelegationOutcome.cs               (novo)
│       │   ├── AgentDelegations/
│       │   │   ├── DelegationOrigin.cs                    (novo) codec das duas chaves
│       │   │   └── AgentDelegationToolSetResolver.cs      # origem + desfechos
│       │   ├── Agents/
│       │   │   ├── AgentExecutionService.cs               # escopo, fase, abrir/fechar
│       │   │   └── LlmCallDurationChatClient.cs           # tokens, falha, status
│       │   ├── Diagnostics/NonTerminalTaskDetectorService.cs  # condição de remoção
│       │   └── Infrastructure/
│       │       ├── AppDbContext.cs                        # espelho
│       │       └── Migrations/
│       │           ├── <ts>_AddExecutionMetrics.cs          (gerado)
│       │           ├── <ts>_AddExecutionMetrics.Designer.cs (gerado)
│       │           └── AppDbContextModelSnapshot.cs         (gerado)
│       └── tests/Buteco.Workers.Tests/
│           ├── ExecutionMetrics/
│           │   ├── ExecutionMetricsScopeTests.cs          (novo) unitário, fora da coleção
│           │   └── ExecutionMetricsSchemaMirrorTests.cs   (novo) IClassFixture, fora da coleção
│           ├── Support/ExecutionMetricsReader.cs          (novo) leitura das três tabelas
│           ├── Agents/LlmCallDurationChatClientTests.cs
│           ├── TaskJobConsumerTests.cs
│           ├── ConversationContextLockFailureTests.cs
│           ├── AgentDelegationExecutionTests.cs
│           ├── AgentDelegationConcurrencyTests.cs
│           └── HistorySummarizationTests.cs
├── 02-HISTORICO_E_STATUS.md
└── CHANGELOG.md
```

**Nada em `libs/`.** As entidades existem duas vezes (`apps/api` mapeia,
`apps/workers` escreve) pela mesma disciplina de espelho de `KnowledgeFragment`,
conferida por teste de schema dos dois lados. Um tipo compartilhado em `libs/`
seria o primeiro acoplamento de modelo EF entre apps, e o isolamento é regra.

**`Support/ExecutionMetricsReader.cs` não é abstração prematura:** cinco classes
de teste leem as mesmas três tabelas desde o dia um.

## Projeção (convenção 18) — décima primeira medição

Feita **depois** de fechar a verificação e **antes** de código. O fechamento só
compara.

**Blast radius de assinatura: zero**, e é por compilação que se confere (D2):
nenhum construtor muda, nenhuma assinatura pública existente muda. O que se
acrescenta a tipos existentes: duas chaves de metadata, membros privados.

**Unidades públicas** (acertou três vezes seguidas — mantida):

| unidade | quantas |
|---|---|
| tipo novo em `apps/workers` | 8 (`ExecutionMetricsScope`, `ExecutionMetricsWriter`, `ExecutionMetricsValues`, `CompactionCallChatClient`, `DelegationOrigin`, 3 entidades) |
| tipo novo em `apps/api` | 3 (entidades) |
| tipo aninhado de vocabulário | 4 (`Origin`, `Purpose`, `FailurePhase`, `DelegationOutcomeKind`) |
| membro público novo em tipo existente | **6** (três `DbSet` em cada `AppDbContext`) |
| assinatura existente alterada | **0** |

**Arquivos, criados e modificados separados, em pares com o teste:**

| | arquivos | linhas projetadas |
|---|---|---|
| criados (produção, à mão) | 11 (8 workers + 3 api) | ~875 |
| modificados (produção, à mão) | 6 (5 workers + 1 api) | ~410 |
| criados (teste) | 4 (3 workers + 1 api) | ~550 |
| modificados (teste) | 6 | ~780 |
| registro (`02`, `CHANGELOG`) | 2 | ~165 |
| **total à mão, ex-`openspec/`** | **29** | **~2.780** (faixa 2.450–3.150) |
| gerados (fora da conta à mão) | 4 criados + 2 snapshots | não projetado: o `.Designer.cs` carrega o snapshot **inteiro** (a razão de headline de 3,1x) |

**Comentário é produto — em produção E em teste** (a lição da décima). Em
produção, os registros classificados antes de multiplicar, com os três tipos de
custo da série:

| registro | tipo | custo |
|---|---|---|
| por que `AsyncLocal` e não construtor (os 14 harness) | contrafactual | ~31 |
| por que `ExecuteAsync` sobrescreve o escopo | contrafactual | ~31 |
| por que fechar depois do estado terminal | contrafactual | ~31 |
| o que um crash deixa, e os dois caminhos sem linha | contrafactual | ~31 |
| por que `SubmittedAt` nulo em reentrega (com a ordem `:107`→`:140`) | evidência com linha | ~14 |
| por que `IChatClient` direto e `Dispose` no-op | contrafactual | ~31 |
| por que não `options is null` | contrafactual | ~31 |
| fase e não mensagem | contrafactual | ~31 |
| snapshot sem FK (em cada lado do espelho) | contrafactual ×2 | ~62 |
| origem não inferida da profundidade | contrafactual | ~31 |
| nulo ≠ zero nos tokens | evidência | ~14 |
| a etapa 2 relaxa `TaskId` | evidência | ~14 |
| último estado observado ≠ estado na desistência | evidência (cita o existente) | ~14 |
| condição de remoção do detector (D11) | contrafactual | ~31 |
| **medição com regime colado** | — | **nenhum previsto** em produção |

~420 de comentário de registro, mais ~215 de doc de tipo/membro, contra ~650 de
lógica: **~1:1** em produção — abaixo das changes de puro registro (2,3:1 e
3,15:1) porque aqui há lógica de verdade (três entidades, um acumulador, um
escritor).

**Em teste, comentário como item próprio**, custo por guarda cuja razão não é
auto-evidente: o de degradação (por que renomear tabela e não mockar), o de
snapshot (por que o par estrutural), o de `SubmittedAt` nulo, o de
contaminação entre execuções e o de compactação — **5 × ~31 ≈ 155**, dentro das
~1.330 de teste. Proporção esperada ~0,5:1 (a décima mediu 0,90:1 numa change em
que **todo** guarda tinha razão não óbvia; aqui metade é asserção de coluna).

**Cenários, contados pelos estados observáveis da delta de spec — e a unidade de
entrega do xUnit é o CASO, não o método:**

| classe | casos novos |
|---|---|
| `ExecutionMetricsScopeTests` | 7 `[Fact]` + 1 `[Theory]` de 3 casos (status HTTP) = **10** |
| `ExecutionMetricsSchemaMirrorTests` | 2 `[Fact]` + 1 `[Theory]` de 12 casos (tipo e nulidade) = **14** |
| `TaskJobConsumerTests` | **7** (completa; linha aberta durante a execução; `SubmittedAt` nulo em reentrega; nulo persistido; snapshot; degradação; falha na resolução do client) |
| `ConversationContextLockFailureTests` | **1** (falha do lock, zero filhas) |
| `AgentDelegationExecutionTests` | **3** (profundidade; chaves de origem; `NotStarted`) |
| `AgentDelegationConcurrencyTests` | **0** novos — **5** testes existentes ganham a asserção da linha (os quatro de desfecho + o de duas instâncias, que ganha origem e contaminação) |
| `HistorySummarizationTests` | **1** (compactação) |
| `LlmCallDurationChatClientTests` | **3** (turno; streaming; falha com status) |
| **`apps/workers`** | **+39** |
| `ExecutionMetricsMigrationTests` (`apps/api`) | 2 `[Fact]` + 1 `[Theory]` de 6 casos = **+8** |

**Suíte, com o regime colado:**

- `apps/workers`: baseline **280/280** (fechamento da
  `indexacao-lote-de-fragmentos`, 5m41s, `podman ps` = 0, **14 classes** na
  `WorkerHostCollection`). Projeção **319/319**, **14 classes continuam 14**.
  A baseline é **remedida** na tarefa 1.1 antes de tocar em arquivo.
- `apps/api`: **medida em 21/09/2026 às 01:24 (-03), sobre `2ee34d3`**, antes
  de qualquer arquivo de código tocado: **335/335**, 1m09s de suíte (88,5s de
  relógio com o build), `podman ps` = 0, load average ~3,0. Bate com o
  fechamento da `delegacao-ciclo-no-cadastro`, que corrigiu
  `AgentDeactivationTests` (318/318) e acrescentou 17. Projeção **343/343**
  (+8). A 1.1 remede de novo no dia do apply; se não der 335/335, a divergência
  é achado a explicar antes de seguir.

**De onde veio o número defasado, para a próxima etapa não repetir.** A
primeira redação desta projeção dizia **317/318 → 325/326**. O número **não** veio
do relatório da exploração recuperado do transcript — veio da **memória
persistente do assistente** (`agent-deactivation-tests-ordem`, escrita em
20/09/2026 às 05:54, antes da `delegacao-ciclo-no-cadastro`), que o carrega em
toda sessão como "`Buteco.Api.Tests` fecha em 317/318". É a convenção 22 no
sentido literal, com uma fonte que o `01` não previa: **memória de sessão
carregada automaticamente é referência medida sem estado colado e sem gatilho**,
e chega com a mesma autoridade que o `02`. A memória foi corrigida. **Régua para
as etapas seguintes:** número de suíte citado em projeção sai de medição do dia
ou do fechamento mais recente no `02`, nunca de memória nem de transcript.

**Os outros números de estado usados nesta change, conferidos pela mesma
régua** (o relatório recuperado e a memória carregam números anteriores às cinco
changes):

| número | origem | conferido hoje | resultado |
|---|---|---|---|
| 280/280 de `apps/workers` | fechamento da `indexacao-lote-de-fragmentos` no `02` (a mais recente) | não remedido — é o fechamento mais recente; a 1.1 remede | válido até a 1.1 |
| 14 classes na `WorkerHostCollection` | contado nesta proposta | `grep -rn "^\[Collection("` ancorado | **14**, confirmado |
| "13 sítios" de DI | convenção 22 do `01` e exploração | contado de novo | **defasado** — 14 arquivos / 15 registros (ver D2) |
| linhas de código citadas | todas lidas nesta proposta | — | as da exploração só aparecem rotuladas como "o que a exploração dizia" |
| censo do dev (188 de 244 tasks em `openai/llama3.2:3b`) | exploração, Postgres de dev em 20/09 | não remedido | só usado no Risco do Ollama, com a data; a 7.2 é execução real e não depende dele |

**Direções de erro, nomeadas sabendo que a terceira medição errou as duas**
(isto não substitui a contagem acima):

- **para cima em teste modificado**, se os testes de log da
  `delegacao-diagnostico` capturarem o log por um harness que não expõe o
  banco — aí as cinco asserções viram cinco testes novos, cada um com arranjo
  próprio (~35);
- **para cima em produção**, se o upsert do fechamento (D3) não couber no EF e
  precisar de SQL cru com `ON CONFLICT`.

### Escopos 2 e 3 — fora da projeção, comparados à parte

A projeção acima foi fechada para o **escopo 1** (a coleta). Os escopos 2 e 3
nasceram no apply e **entram no fechamento como linhas próprias, nunca somados
ao projetado** — somá-los faria a projeção parecer errada por trabalho que ela
não tinha como conter (convenção 18: *medição de método só compara o escopo que
estava projetado*).

| escopo | o que é | arquivos |
|---|---|---|
| 2 | guarda de estado terminal (D17) | `AgentExecutionService.cs` (mesmo arquivo do escopo 1), `TaskJobConsumerTests.cs` (idem), `A2ATaskLifecycleTests.cs` (novo na lista), delta de `a2a-task-lifecycle` |
| 3 | duplo do resolver no round-trip | `tests/InboxOrchestratorRoundTrip.Tests/Support/NullAgentDelegationToolSetResolver.cs` |

## Risks / Trade-offs

- **[O endpoint que atende a maior fatia pode não preencher `Usage`]** — Q5 da
  exploração; 188 de 244 tasks do dev eram `openai/llama3.2:3b`. Se não
  preencher, essa fatia fica com token **nulo**, e a tela mostra vazio, que é o
  certo. → A verificação é **execução real** (tarefa 7.2, convenção 6), com o
  resultado registrado no `02` com o regime: modelo, endpoint, data.
  *(Correção de 21/09/2026: este risco nomeava "Ollama pelo endpoint
  compatível" — dedução da exploração a partir do nome do modelo, nunca medida.
  O dono confirmou que não há modelo local rodando. A referência à "tarefa 8.2"
  também estava errada — é a 7.2.)* **Risco encerrado em 22/09/2026:** o
  `llama3.2:3b` era Ollama local de teste pontual, descartado pelo dono em
  20/09/2026, e não está em uso em produção — não há o que medir. A decisão
  existia desde 20/09, mas só em conversa; ver "Decisão tomada fora do
  repositório" no `02`.
- **[Crash no meio perde as filhas]** → aceito (D3); a linha pai aberta é o
  sinal, e é o que a métrica de sem-estado-terminal lê.
- **[Contaminação do `AsyncLocal` entre execuções]** → `ExecuteAsync`
  sobrescreve (D2); guarda de contaminação no teste de duas instâncias (as
  filhas do alvo pertencem ao alvo).
- **[A numeração do catálogo não foi conferida contra a fonte]** → resolvido na
  proposta: o catálogo foi registrado no `02` com o dono (27 entradas) e o mapa
  foi conferido contra ele (tarefa 1.2).
- **[Linha por chamada é escrita a mais no banco]** → uma única ida por execução
  no fechamento, não uma por chamada.
- **[A2A é preview (`1.0.0-preview2`)]** → a leitura do `submitted` depende de
  `TaskProjection` não tocar `Status` num evento de mensagem. Verificado por
  decompilação hoje; o guarda de `SubmittedAt` quebra se a versão mudar isso.

## Migration Plan

1. A migração de `apps/api` roda pelo `migrator` do compose de produção, como
   toda migração. Só cria tabelas; nada em `a2a_tasks`.
2. Ordem de deploy: `migrator` → `api` → `workers`. Um worker novo contra banco
   sem as tabelas **não falha task** — a escrita degrada e loga (D3). É a
   degradação que o guarda prende.
3. **Rollback:** reverter `workers` desliga a coleta; as tabelas ficam, vazias de
   linhas novas, sem efeito em nada. Reverter a migração só se for descartar a
   linha de trabalho.
4. **No dia do deploy:** registrar no `02` *"medindo desde DD/MM/AAAA,
   `America/Sao_Paulo`"* (D14).

## Open Questions

Nenhuma de produto. A que havia — "Acionado por" conta execuções ou delegações —
foi fechada pelo protótipo aprovado (D16).
