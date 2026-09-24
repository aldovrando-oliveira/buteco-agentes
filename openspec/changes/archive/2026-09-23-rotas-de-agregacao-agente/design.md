## Context

Etapa 3 da linha `metricas-de-operacao`, change **B** de duas. A change **A**
(`rotas-de-agregacao-sistema`) está aplicada, deployada e **verificada em
produção** em 23/09/2026, e com ela o contrato inteiro de janela, fuso, balde,
nulo e regime está fechado. A B **usa** esse contrato; não o reabre.

O catálogo de 27 métricas é referência viva no `02:4926`, e ele declara as duas
superfícies da linha: *"página Insights do sistema e aba Insights no detalhe do
agente (protótipos aprovados, nos três cenários de delegação: só delega, só é
delegado, os dois)"* (`02:4938-4941`). **O catálogo não diz quais métricas são de
qual superfície** — ele numera, e cada etapa monta o seu mapa. É por isso que o
mapa deste documento é montado contra as tabelas, e não herdado.

### O que a A decidiu, verificou, e esta change não redecide

| herdado | onde está, verificado nesta change |
|---|---|
| `TZ` entregue ao `api` com `:?`, lido como **configuração** | `MetricsOptions.TimeZone`; produção anunciou `America/Sao_Paulo, -03:00:00` no boot |
| `TimeProvider.System` registrado, e `Microsoft.Extensions.TimeProvider.Testing` no `.csproj` de teste | `InsightsEndpointsTests.InsightsFixture` já o usa |
| Janela: `from`/`to` como `string?`, ISO 8601, `AssumeUniversal \| AdjustToUniversal`, obrigatórios, inclusivos, problemas acumulados, inversão depois do parse | `InsightsPeriod.cs:31-106` |
| Balde local em SQL, **sem** índice de expressão | `GetSystemInsightsQueryHandler.cs:152`, `:168` |
| Nulo preservado, `0` reservado a contagem medida | `SystemInsightsResponse.cs:14-19` |
| Mapa de regimes, com a fonte sendo o **instante do deploy** | `MetricsOptions.Regimes` |
| Série omite dias anteriores ao regime | `Later()`, `GetSystemInsightsQueryHandler.cs:110-111` |
| Autenticação: **nada a declarar** — o `FallbackPolicy` fecha por padrão | `InsightsEndpoints.cs:14-21` |

**E `InsightsPeriod` foi extraída pela A com esta change declarada no
comentário:** *"Separado do endpoint porque é a única parte do contrato de janela
que a change B (`/insights/agents/{id}`) herda literalmente: ela reusa esta classe
e não redecide nada sobre limites, formato ou inversão"* (`InsightsPeriod.cs:8-13`).
A B cumpre isso literalmente.

### O que foi lido do código nesta change (convenção 6)

Nada abaixo veio por analogia com a change A. As seis tabelas foram lidas nas
entidades e no `AppDbContext`, **procurando coluna de agente**:

| tabela | coluna que permite recortar por agente | evidência |
|---|---|---|
| `task_executions` | **`AgentId`** e **`SourceAgentId`** (anulável) | `ExecutionMetrics/Entities/TaskExecution.cs:27`, `:37` |
| `delegation_outcomes` | **`SourceAgentId`** e **`TargetAgentId`** | `ExecutionMetrics/Entities/DelegationOutcome.cs:15-17` |
| `a2a_tasks` | **`agent_id`**, com FK para `agents` | `AppDbContext.cs:105`, `:112` |
| `provider_calls` | **nenhuma** — `TaskId` e mais nada de identidade | `ExecutionMetrics/Entities/ProviderCall.cs:10-37` |
| `embedding_calls` | **nenhuma** — tem `KnowledgeBaseId`, `TaskId?` e `KnowledgeIndexingAttemptId?` | `ExecutionMetrics/Entities/EmbeddingCall.cs:26-103` |
| `knowledge_indexing_attempts` | **nenhuma** — `KnowledgeDocumentId` e `KnowledgeBaseId` | `ExecutionMetrics/Entities/KnowledgeIndexingAttempt.cs:32-81` |

**Índices existentes, lidos no `AppDbContext`** — e é essa leitura, não uma
suposição, que sustenta a decisão de não criar índice nenhum:

| índice | linha | o que ele cobre nesta change |
|---|---|---|
| `task_executions(AgentId)` | `:332` | **o filtro central da rota** |
| `task_executions(StartedAt)` | `:333` | a janela |
| `delegation_outcomes(SourceAgentId)` | `:356` | o lado "Delega para" |
| `delegation_outcomes(TargetAgentId)` | `:357` | o lado inverso, se um dia for pedido |
| `provider_calls(TaskId)` | `:344` | o join ao pai |
| `embedding_calls(TaskId)` | `:404` | o caminho de busca de M19 |
| `a2a_tasks(State)` | `:111` | M32, segunda população |

**Vocabulário fechado, lido em `ExecutionMetricsValues.cs`** — e ele é o que dá
forma ao guarda da assimetria:

- `Origin`: **`External`**, **`Delegation`** — dois valores, e só dois;
- `DelegationOutcome`: **`Completed`**, **`TargetUnsuccessful`**, **`Expired`**,
  **`NotStarted`**.

**`Agent.IsActive` existe** (`Agents/Entities/Agent.cs:11`), e é o que obriga a
separar "inexistente" de "inativo" nesta change.

**O `nginx.conf` não precisa de nada, e isto foi conferido, não suposto:**
`apps/frontend/deploy/nginx.conf:59` traz
`location ~ ^/(agents|providers|mcp-servers|knowledge-bases|knowledge-index|insights|auth)(/|$)`,
e `openspec/specs/server-deployment/spec.md:217-222` tem o cenário *"Prefixo de
insights cobre as rotas de escopo abaixo dele"* citando **`/insights/agents/{id}`
pelo nome**. Supor que precisa custaria a segunda edição do mesmo arquivo em duas
semanas.

### Uma limitação de fonte que esta change declara em vez de esconder

**Os protótipos aprovados continuam sem ser abertos.** O MCP do Claude Design
recusou a autenticação de novo nesta sessão (`FIRST_PARTY_AUTH_REJECTED`, HTTP
403, pedindo `/design-login`, que só o dono roda) — é a mesma limitação que a A
declarou, no mesmo estado. Tudo que aqui se afirma sobre as telas vem da **D16 do
`design.md` arquivado de `metricas-execucao-coleta`**, que é registro de primeira
mão da decisão e cita os números do protótipo, e do catálogo do `02`. **Onde o
protótipo contrariar este documento, o protótipo vence**, e a divergência vira
registro pela convenção 9.

## Goals / Non-Goals

**Goals:**

- Uma rota, `GET /insights/agents/{id}`, servindo o agregado do escopo de um
  agente, com o contrato da A herdado literalmente.
- **A assimetria dos dois lados da delegação implementada e guardada** — é a
  decisão central, e a que se erra sozinha.
- O mapa das 27 **refeito por escopo de agente**, contra as seis tabelas, com
  veredito por métrica.
- As lacunas próprias deste escopo nomeadas **agora**, antes da tela.
- `404` para agente inexistente, decidido e guardado.

**Non-Goals:**

- **Não tocar o `nginx.conf`** — já coberto, com cenário de spec afirmando.
- **Nenhuma tela** — etapas 4 e 5.
- **Não corrigir o motivo das recusas de `apps/api`** (M29) — change própria, com
  posição antes da etapa 4. **E não corrigir a subcontagem de M27 tampouco**, que
  este escopo torna quantificável sem torná-la resolvida (D8).
- **Nenhum índice**, nenhuma migração, nenhuma coluna, nenhum `AT TIME ZONE` em
  índice de expressão.
- Não remover o detector de tasks não-terminais; não mexer em instâncias.
- **Não alterar a rota do sistema nem o seu DTO.** O único arquivo de produção da
  A que esta change edita é `InsightsEndpoints.cs`, e só para mapear a rota nova.
- Não reabrir nada do contrato herdado da A.

## Decisions

### D1 — A rota do agente **não** é a do sistema filtrada, e a prova é o mapa

A D16 de `metricas-execucao-coleta` já tinha fixado o mecanismo, e vale citada:

> **"Acionado por"** (lado do alvo) lê **`task_executions`**: linhas com
> `Origin = Delegation` do agente, agrupadas por `SourceAgentId` — o que de fato
> **rodou** ali. […] **"Delega para"** (lado do Source) lê
> **`delegation_outcomes`**: linhas com `SourceAgentId` do agente, agrupadas por
> `TargetAgentId` — o que ele **tentou**.

E fixou a consequência: *"com `NotStarted` ou `Expired` no meio, **os dois lados
da mesma relação mostram números diferentes**"*, e *"o card não pode se parecer
com um espelho"*.

**Como isso foi decidido lá, e por que fecha:** o protótipo aprovado mostra, no
cenário 3 (`Cobrança`), *"Tasks executadas 97 · 97 por delegação"* e *"Acionado
por: Triagem 70, Atendente 27"* — e 70 + 27 = 97. Só fecha contando **execuções
do alvo**; contar delegações disparadas incluiria as `NotStarted`, que não criam
task, e a soma quebraria na mesma tela. No cenário 2 (`Financeiro`),
65 = 51 + 14.

**O que esta change acrescenta é que a assimetria não é um caso de M34 — é a
forma do escopo inteiro.** O mapa abaixo mostra sete métricas que não transferem
por filtro e duas que não existem. Uma rota que fosse `/insights/system` com
`where AgentId = ...` produziria, nessas nove, número plausível e errado.

**Alternativa recusada:** parametrizar a rota do sistema com um `agentId?`
opcional. Recusada porque o corpo da resposta teria campos que só existem num dos
modos — `byAgent` presente quando ausente o parâmetro, ausente quando presente;
os dois lados da delegação existindo só num modo — e um DTO com metade dos campos
condicional é o mesmo defeito da rota única repartida, invertido.

### D2 — A divergência tem **duas** causas independentes, e a segunda é achado desta change

A D16 nomeou uma: **resultado que não produz execução**. `NotStarted` nunca cria
task (`DelegationOutcome.TargetTaskId` é nulo, e o XML doc diz exatamente isso:
*"Nulo quando a task do alvo nunca foi criada (`NotStarted`)"*); `Expired` com
último estado `Submitted` criou uma que talvez nunca rode.

**A segunda causa é a janela, e ela aparece quando se lê de onde cada lado tira o
seu tempo:**

| lado | fonte | de onde vem a janela |
|---|---|---|
| "Delega para" | `delegation_outcomes` | `task_executions.StartedAt` da execução **de origem**, por `SourceTaskId` |
| "Acionado por" | `task_executions` | `StartedAt` da execução **de destino**, que é a própria linha |

`delegation_outcomes` **não tem coluna temporal utilizável** — só
`LastObservedAt?`, que é a última leitura e não o instante do evento —, então a
janela dela é emprestada do pai. **São dois relógios.** Uma delegação disparada às
23:58 cuja task de destino só é consumida às 00:03 tem os seus dois lados em
**dias diferentes** do balde local, e uma disparada perto do limite pode ter um
lado dentro da janela e o outro fora.

**Consequência que o desenho carrega:** a divergência não some com um período
maior, e não é ruído de borda que se possa arredondar. Ela é estrutural nas duas
causas, e a resposta declara isso como parcialidade nomeada em vez de deixar o
cliente descobrir.

**Alternativa recusada:** situar os dois lados pelo mesmo relógio, usando a
execução de origem também para "Acionado por" (por `SourceTaskId`). Recusada
porque quebraria a soma do protótipo: *"Tasks executadas 97 · 97 por delegação"*
compara com as **execuções do próprio agente na janela**, e essas são situadas
pelo `StartedAt` delas. Trocar o relógio de um lado faria o card discordar do
número logo acima dele, na mesma tela.

### D3 — `404` para inexistente, `200` para inativo, `200` para existente e vazio

A A não tem este caso: `/insights/system` não recebe id.

**Três respostas, três proveniências** — é a tabela da convenção 13 aplicada a um
código de status:

| caso | resposta | por quê |
|---|---|---|
| id não existe em `agents` | **`404`** | não há sujeito sobre o qual medir; um agregado zerado afirmaria medição sobre quem não existe |
| agente existe, sem ocorrência na janela | **`200`**, contagens `0`, não coletados ausentes | período **medido e vazio** — é o `0` legítimo |
| agente existe e está **inativo** | **`200`** | inatividade é estado de cadastro; o que ele executou enquanto ativo foi medido |

**A checagem é uma consulta de existência, e é a única leitura que a rota faz de
`agents`** — nenhum campo do agente entra no agregado. Precedente direto da casa:
`GetAgentByIdAsync` devolve `Results<Ok<AgentResponse>, NotFound>` com o handler
retornando nulo (`Agents/Endpoints/AgentEndpoints.cs`).

**Alternativa recusada:** `200` com agregado vazio, "porque a tela sabe lidar".
Recusada porque é exatamente o quarto estado da convenção 13 — *"sei que não
existe"* — colapsado no segundo. Um id inexistente e um agente ocioso
produziriam o mesmo corpo, e a tela perderia a única informação que distingue os
dois.

**Consequência de ordem a fixar:** a existência é checada **antes** de qualquer
agregação, e a validação da janela **antes** da existência. Uma janela inválida
para um id inexistente responde `400`, não `404` — o cliente corrige um problema
por vez, e o formato do erro de janela continua sendo o mesmo do resto da casa.

### D4 — Reuso literal de `InsightsPeriod` e `MetricsOptions`, sem extrair nada novo

`InsightsPeriod` é usada como está, e `MetricsOptions` também. **Nada de comum
entre as duas rotas é extraído nesta change** — nem uma classe base de handler,
nem um "mapeador de regime" compartilhado.

**Convenção 2**, e o gatilho é repetição **já observada**: com duas rotas, as
partes que realmente se repetem são a resolução de regime (`RegimeStart`/`Later`,
duas funções de uma linha) e a montagem da janela de resposta. Duas cópias de uma
função de uma linha não pagam uma abstração; o que elas pagam é o comentário
apontando o gêmeo, que é o mesmo mecanismo que a casa já usa entre as entidades
espelhadas de `apps/api` e `apps/workers`.

**Alternativa recusada:** uma `InsightsQueryHandlerBase`. Recusada porque as
consultas dos dois escopos divergem em nove métricas — o que a base compartilharia
é justamente o que já está em `InsightsPeriod` e `MetricsOptions`, e o resto seria
herança com dois membros.

### D5 — DTO próprio, e **não** o `SystemInsightsResponse` reaproveitado

`AgentInsightsResponse`, com os seus próprios sub-records.

**O motivo não é estilo.** Reaproveitar o DTO do sistema obrigaria a deixar
`ByAgent` lá dentro (M13, que **não existe** neste escopo) e `IndexingFailures`
(M30, idem), servidos como listas vazias — e lista vazia é o texto de *"medi e não
achei nada"*, que afirmaria medição onde não há fonte. É a convenção 13 furada
pela forma do tipo, antes de qualquer consulta rodar.

Pelo mesmo motivo, o DTO do agente **não tem campo** para o que este escopo não
sustenta: a forma do record é a primeira barreira, como já é na A
(`SystemInsightsResponse.cs:6-12`).

### D6 — O que **não existe** no escopo do agente, e por que não se resolve por vínculo de base

Duas métricas não têm fonte alguma com agente, e a causa é a mesma: **indexação é
trabalho da base, não de um agente.**

- **M30 — falhas de indexação.** `knowledge_indexing_attempts` tem
  `KnowledgeDocumentId` e `KnowledgeBaseId`, e **nenhuma coluna de agente**.
- **M19, metade de indexação.** `embedding_calls` com `Purpose = Indexing`
  pendura em `knowledge_indexing_attempts` — e, pelo item acima, não alcança
  agente.

**A saída aparente é o vínculo `AgentKnowledgeBases`, e ela é recusada.** O
vínculo é de **muitos para muitos**: uma indexação que aconteceu **uma vez**
seria contada em cada agente vinculado à base. O total por agente somaria mais que
o total do sistema, e cada número afirmaria como trabalho daquele agente um
trabalho que não foi dele — nenhum agente pediu aquela indexação.

**O que fica:** M19 existe no escopo do agente **só pelo caminho de busca**
(`embedding_calls.TaskId` → `task_executions.AgentId`, e o índice
`embedding_calls(TaskId)` o cobre), **declarado como parcialidade**, porque um
número com o rótulo "tokens de embedding" que só conta busca afirmaria o todo.

**Alternativa recusada:** omitir M19 inteira no escopo do agente. Recusada porque
a busca vetorial **é** trabalho do agente, feita dentro da execução dele, e
omiti-la perderia dado real por causa da metade que falta.

### D7 — M13 sai; M14, M15, M16a, M16b e M26 mudam de **significado**, não de consulta

- **M13 (por agente) não existe neste escopo.** O recorte já é o agente;
  reproduzi-la seria um `GROUP BY` de um elemento só com o nome de uma comparação.
- **M14 (por provedor) muda de consulta**, porque o total conversa + embedding
  perde a metade de indexação (D6). O nível continua sendo o único em que os dois
  regimes se somam, e a soma passa a ser honesta só sobre a busca.
- **M15, M16a, M16b (por modelo) mudam de significado.** No sistema, mais de um
  modelo significa **agentes diferentes** com configurações diferentes; no escopo
  do agente, significa que **aquele agente mudou** de provedor ou de modelo dentro
  da janela. Isso é possível e é informação: `Provider`/`Model` de
  `task_executions` são **snapshot do agente no início da execução**, e a entidade
  registra por escrito que é assim de propósito, sem FK para `agents`
  (`TaskExecution.cs:17-21`).
- **M26 (profundidade) muda de significado.** No sistema é a maior profundidade
  que o sistema alcançou; no agente é **a maior profundidade em que aquele agente
  executou** — uma posição na cadeia, não um tamanho de cadeia. As duas são
  `max(DelegationDepth)` e respondem perguntas diferentes.

**Consequência para a etapa 4:** os rótulos destas cinco não podem ser copiados da
tela do sistema. É a mesma restrição de desenho que M25 já carrega, pelo mesmo
motivo — um rótulo que afirma mais do que o número sabe.

### D8 — As parcialidades herdadas valem aqui, e uma delas fica **quantificável sem ser corrigida**

As quatro parcialidades da A continuam valendo no escopo do agente, com a causa
inalterada, e os códigos de `InsightsCaveats` são reusados como estão.

**E há um fato novo que é preciso registrar sem agir sobre ele:** `a2a_tasks`
**tem `agent_id`**, então, no escopo do agente, a parte que falta de M27 — as
recusas feitas por `apps/api`, que nunca produzem linha de execução — é
**contável**. A tentação é contá-la aqui.

**Decidido: não.** Três razões, e a terceira decide:

1. A A **não** a contou no escopo do sistema, embora pudesse pelo mesmo caminho —
   `a2a_tasks` tem `state`, e todo `Rejected` está lá. Contá-la só de um lado
   faria os dois escopos medirem coisas diferentes com o mesmo rótulo.
2. Contar a recusa **não** traz o motivo, nem provedor, nem modelo. A lacuna que a
   tela precisa continua aberta.
3. É correção de coleta feita dentro de uma change de agregação, decidida no meio
   do caminho — o que a convenção 1 recusa. A change de coleta já está na fila com
   posição **antes da etapa 4**.

**Registrado como item com gatilho:** quando a change de coleta fechar, decidir se
M27 passa a somar as duas fontes **nos dois escopos** ao mesmo tempo.

### D9 — M32 recorta as duas populações, e as duas têm coluna de agente

- **execução aberta** — `task_executions.EndedAt` nulo, `AgentId` do agente;
- **task nunca consumida** — `a2a_tasks` sem linha correspondente em
  `task_executions`, `agent_id` do agente.

O conjunto de estados não-terminais continua sendo **o mesmo** do detector de
`apps/workers` — `{Submitted, Working}` —, pelo motivo da D9 da A: as duas fontes
precisam medir o mesmo conjunto enquanto a `replicas-de-worker` as compara. Esta
change **não** muda esse conjunto, e a cópia do comentário aponta o original.

A métrica continua sendo leitura **do instante da consulta**, e não série, porque
`a2a_tasks` sobrescreve o carimbo a cada transição.

### D10 — A armadilha que a A deixou: o `AdjustToUniversal` no instante de **regime**

A A descobriu, com o guarda da 2.3, que o defeito não estava no `from`/`to` do
cliente — que `InsightsPeriod` normaliza — mas no **valor de configuração**. O
comentário de `GetSystemInsightsQueryHandler.cs:85-105` registra o mecanismo: o
binder liga `"2026-09-01T00:00:00-03:00"` a um `DateTimeOffset` com deslocamento
`-03:00` **intacto**; quando o regime é mais tarde que o pedido, é **ele** que
vira parâmetro da consulta, e o Npgsql recusa deslocamento diferente de zero para
coluna de instante (`ArgumentException` → `500`).

**A B carrega os mesmos instantes de regime, pelo mesmo caminho.** A lição vale
para **todo** `DateTimeOffset` que chega ao driver, e valor de configuração entra
por um caminho que nenhum parse cobre. O `RegimeStart` do handler novo aplica
`ToUniversalTime()` pela mesma razão, com o comentário apontando o gêmeo — e o
guarda correspondente é cenário de spec, não confiança na cópia.

**E o regime errado por ambiente continua sendo item aberto.** Os instantes são
valores do **piloto** num arquivo versionado; num ambiente cuja coleta começou
depois, a rota emite `0` para dias dentro do regime declarado e fora da medição
real. A B **não resolve** e **não redescobre**: o item está no `02`, com gatilho
(o primeiro ambiente novo a subir a rota, dev incluído) e posição.

### D11 — Fixture própria, e esta change faz a **31ª** classe de contêiner de `apps/api`

O arquivo de teste novo tem o seu **próprio** fixture, no molde de
`InsightsEndpointsTests.InsightsFixture`, com o mesmo fuso e os mesmos regimes
fixados.

**Por que não reusar o fixture da A.** `ApiFactoryFixture` constrói um
`PostgreSqlContainer` como campo de **instância**
(`Support/ApiFactoryFixture.cs:30-34`), e o xUnit cria uma instância de
`IClassFixture<T>` **por classe de teste**. Reusar o tipo não compartilharia o
banco — daria um segundo contêiner de qualquer modo —, e a semeadura da A tem
guarda de idempotência que retorna cedo se já houver linha
(`InsightsEndpointsTests.cs:377-383`). Duas classes semeando a mesma base com
esse guarda produziriam resultado **dependente da ordem de execução**, que é a
quinta forma da convenção 15: verde ou vermelho conforme o plano do runner.

**O custo, medido e não estimado:** `apps/api` tem hoje **30 classes de teste que
sobem contêiner** — 26 por `IClassFixture` (15 de `ApiFactoryFixture`, 4 de
`McpConnectionTestFixture`, 2 de `A2ATaskLifecycleFixture`, e 5 de fixtures de um
consumidor cada) e 4 que constroem o seu direto (os testes de migração). Lido em
23/09/2026. **Esta change faz a 31ª.**

**É a condição observável da convenção 22, e a recalibração é tarefa desta
change, não descoberta da próxima.** A régua passa a ser escrita com o estado
colado: **31 classes de contêiner em `apps/api`, medido em 23/09/2026**.

#### Para que essa referência serve — porque um número que ninguém consulta não é régua

Recalibrar um valor sem dizer o que se faz com ele é atualizar um registro morto,
e aí a terceira quebra é questão de tempo. Então:

**Quando ela é consultada.** Três situações, e só três: suíte de `apps/api`
**lenta**; suíte de `apps/api` **reprovando em bloco na inicialização de
fixture** (o sintoma de contenção, que parece falha de teste); e a decisão de
**acrescentar mais uma classe com contêiner**.

**O que ela permite concluir hoje: nada além de registrar o crescimento — e isso
é resultado, não lacuna.** O número sozinho não tem limiar contra o qual ser
comparado, porque **a duração da suíte de `apps/api` nunca foi registrada com a
contagem de classes ao lado**. Existe duração medida — `1 m 21 s` e `2 m 18 s`
em 23/09/2026 (`02:5955-5956`) —, mas ela foi anotada para explicar o flake de
`apps/inbox`, sem dizer quantas classes `apps/api` tinha naquele instante. É a
convenção 22 na forma exata que ela descreve: número certo, sem o estado colado.

**O que ela explicitamente NÃO permite concluir: que `apps/api` esteja perto da
parede que `apps/workers` encontrou.** A tentação é transportar o número de lá —
`apps/workers` quebrou ao entrar a **13ª** classe de host, e `apps/api` está em
30. O transporte é inválido, e o mecanismo diz por quê:

| | `apps/workers` | `apps/api` |
|---|---|---|
| contêineres por classe | **2** — Postgres **e** RabbitMQ (`WorkerInfrastructureFixture.cs:11`, `:17`) | **1** — só Postgres; o fixture **não** sobe RabbitMQ (`ApiFactoryFixture.cs:67`) |
| classes rodam | **serializadas** por `WorkerHostCollection` | **em paralelo** — `apps/api` não tem `CollectionDefinition` nenhuma |
| contagem atual | 14 classes na coleção | 30 classes, 31 com esta change |

Trinta classes de um contêiner em paralelo e treze de dois contêineres em
paralelo **não são a mesma grandeza**, e os 13 de lá não são um teto para cá.
Usar um no lugar do outro é a ocorrência 3 da convenção 22 — o mesmo número
respondendo a outra pergunta.

**O que tornaria a referência acionável, e não é decidido aqui.** O molde já
existe, do outro lado: `apps/workers` **abandonou** o critério de carga (*load <
5,0*) porque ele não discrimina — a mesma suíte fechou `268/268 em 6m37s com load
3,10` e reprovou `2 de 268 em 38m07s com load 3,24` — e recalibrou, em
20/09/2026, para **contagem de contêineres antes de rodar mais duração da
rodada**: `podman ps` em zero, e uma rodada acima de ~10 min é contenção, não
medição, com a referência `6m37s com 14 classes e zero contêineres de dev`
(`WorkerHostCollection.cs:25-47`). O equivalente para `apps/api` é **a duração da
suíte registrada com a contagem de classes ao lado** — que o fechamento desta
change (8.4) produz pela primeira vez, com 31.

A alternativa estrutural é a mesma dos dois lados, e também não é desta change:
uma `ICollectionFixture` compartilhando **um** par de contêineres, que
`WorkerHostCollection.cs:20-23` já nomeia como *"a correção"* contra o paliativo
da serialização, com item próprio no `02`.

**E as duas são a mesma família de referência, mantida à mão em dois apps.**
`apps/workers` recalibrou a sua depois de duas quebras; `apps/api` está crescendo
sem que ninguém tenha dito qual é o critério. Quem mexer numa das duas olha a
outra — a menção cruzada existe para que a próxima pessoa não as trate como
assuntos separados, que é como elas chegaram a este estado.

### D12 — Nenhum índice, e a leitura que sustenta isso

O filtro central da rota é `task_executions.AgentId`, que **tem índice**
(`AppDbContext.cs:332`); a janela é um range sobre `StartedAt`, que **tem índice**
(`:333`); o lado "Delega para" filtra `delegation_outcomes.SourceAgentId`, que
**tem índice** (`:356`); os joins ao pai caem em `provider_calls(TaskId)` (`:344`)
e `embedding_calls(TaskId)` (`:404`).

**Nenhum índice novo, e nenhum índice de expressão sobre o `AT TIME ZONE`** —
congelaria o nome do fuso no schema, que é o oposto de mantê-lo em configuração
(D7 da A).

O par `(AgentId, StartedAt)` seria o composto natural se algum dia for preciso.
**Nenhuma medição o justifica**, e criá-lo agora seria projetar tamanho
(convenção 18). Entra no mesmo item de gatilho que a A já abriu: remedir os planos
contra o piloto depois do primeiro mês da rota em produção.

### D13 — Autenticação: nada a declarar, pelo mesmo mecanismo

`apps/api` fecha por `FallbackPolicy`, e a validação de startup varre **só** o
caminho anônimo. **Pôr `/insights/agents/{id}` na allowlist derrubaria o boot** —
aquela lista é de rotas que precisam estar anônimas. Nada a escrever, e o registro
existe para que ninguém "conserte" isso depois.

### D14 — Um defeito de prosa na change A, corrigido aqui porque esta change copia o molde

O XML doc de `InsightsEndpointsTests.cs:75-79` afirma que `2026-09-15T02:30Z` é
*"**domingo** 14/09 às 23:30"* em `America/Sao_Paulo`. **14/09/2026 é
segunda-feira** — conferido com `date`, e as constantes do próprio arquivo já
dizem isso: `ExpectedLocalWeekday = DayOfWeek.Monday` e `UtcWeekday =
DayOfWeek.Tuesday`. A suíte passa; o defeito é só na frase.

**É exatamente o alvo da convenção 6:** *"a frase em prosa que descreve o que o
código faz é a que decide a próxima change, e é a que ninguém abre o arquivo para
checar"*. E a próxima change é **esta** — que copia esse molde de instante
noturno para o seu guarda de balde local. Corrigir a palavra custa uma linha;
copiá-la propagaria a afirmação errada para o segundo arquivo, com a autoridade de
aparecer em dois lugares.

**Escopo:** uma palavra, num comentário, sem mudança de comportamento e sem teste
novo. Declarada aqui para que a conferência de escopo da 8.5 a encontre **como
alteração declarada**, nunca como achado de origem desconhecida.

## O mapa das 27 no escopo do agente

`TE` = `task_executions` · `PC` = `provider_calls` · `DO` = `delegation_outcomes`
· `KIA` = `knowledge_indexing_attempts` · `EC` = `embedding_calls` ·
`AT` = `a2a_tasks`

**Montado contra as tabelas, não por analogia com o mapa da A.** O veredito de
cada linha é um de quatro: **filtro** (a mesma consulta com `AgentId`),
**significado** (a mesma consulta, outra pergunta — o rótulo muda),
**outra consulta**, **não existe**.

| | métrica | como fica no escopo do agente | veredito |
|---|---|---|---|
| M1 | origem externa | `TE.Origin='External'` + `AgentId` | filtro |
| M2 | tasks executadas | `count(TE)` + `AgentId` | filtro |
| M6 | calor por dia da semana | `TE.StartedAt` + `AgentId`, balde local | filtro |
| M7 | calendário | `TE.StartedAt` + `AgentId` | filtro |
| M9 | dia da semana de pico | derivada de M6 | filtro |
| M10 | série diária tasks e tokens | `TE` + `PC` por join, `e.AgentId` | filtro |
| M11 | tokens de conversa | `PC` join `TE`, `e.AgentId` | filtro |
| M12 | entrada × saída | `PC` join `TE` | filtro |
| **M13** | **por agente** | — | **não existe** |
| **M14** | **por provedor, + total conversa+embedding** | conversa filtra pelo join; embedding **só a metade de busca** | **outra consulta** |
| M15 | por modelo | `PC.Model` + `e.AgentId` — **histórico daquele agente**, não comparação | **significado** |
| M16a | ranking por tokens | idem | **significado** |
| M16b | ranking por nº de chamadas | idem | **significado** |
| M17 | tokens por task, média e p95 | `PC` por `TaskId`, `e.AgentId` | filtro |
| **M19** | **tokens de embedding** | **só `Purpose='Search'`**, por `EC.TaskId` → `TE.AgentId` | **outra consulta** (parcial nova) |
| M21 | duração `submitted`→terminal | `TE` + `AgentId` | filtro (parcial herdada) |
| M22 | tempo de fila | `TE` + `AgentId` | filtro (parcial herdada) |
| M23 | duração da chamada ao provedor | `PC.DurationMs` join `TE` | filtro |
| M24 | chamadas por task | `count(PC)` por task, `e.AgentId` | filtro |
| M25 | resíduo não atribuído ao provedor | `TE` − `sum(PC)`, `AgentId` | filtro (parcial herdada) |
| M26 | profundidade de delegação | `max(TE.DelegationDepth)` + `AgentId` — **posição na cadeia** | **significado** |
| M27 | `failed` e `rejected` | `TE.TerminalState` + `AgentId` | filtro (parcial herdada, **quantificável** — D8) |
| **M28** | **falhas por agente e por provedor/modelo** | "por agente" **some**; resta por provedor/modelo daquele agente | **outra consulta** |
| M29 | motivo da falha | `TE.FailurePhase` + `AgentId` | filtro (sem fonte para parte) |
| **M30** | **falhas de indexação** | — `KIA` não tem agente | **não existe** |
| M32 | tasks sem estado terminal | `TE.EndedAt` nulo + `AgentId`; `AT` por `agent_id` | filtro |
| **M34** | **delegações, par origem → destino** | **dois conjuntos, duas fontes, dois relógios** | **outra consulta — a assimetria** |

**Contagem: 17 por filtro, 4 mudam de significado, 4 são outra consulta, 2 não
existem.** Somam 27.

**Nove das 27 não transferem por analogia** — e é esse número, não a D16 sozinha,
que sustenta a D1. Se o mapa tivesse sido herdado, M13 e M30 sairiam como listas
vazias, M19 e M14 sairiam com o rótulo do todo sobre metade do dado, M28 sairia
com um agrupamento de um elemento, M15/M16a/M16b/M26 sairiam com o rótulo da
comparação errada, e M34 sairia **com um lado só**.

## Risks / Trade-offs

Cada risco tem contraparte verificável (convenção 10).

- **[Alguém "conserta" os dois lados da delegação para baterem]** → o guarda que
  **afirma a divergência**, num cenário com `NotStarted` ou `Expired`. É o guarda
  central desta change, e o único que reprova a correção bem-intencionada.
- **[A segunda causa da divergência — os dois relógios — é lida como bug de
  borda e "arredondada"]** → declarada como parcialidade nomeada na resposta, e
  cenário de spec que a separa da primeira causa.
- **[O balde sai em UTC no escopo do agente, embora esteja certo no do sistema]**
  → guarda próprio que reprova contra balde em UTC, com instante noturno que muda
  de dia **e** de dia da semana. Não herdado: é consulta nova.
- **[O instante de regime com deslocamento vira `500` na rota nova]** → guarda com
  janela que começa **antes** do regime, forçando o instante de configuração a
  virar parâmetro. É o defeito que a A pegou, por um caminho que nenhum parse
  cobre.
- **[Nulo normalizado para zero em algum ponto do caminho novo]** → guarda
  **negativo**, afirmando que o valor não é `0` onde a fonte é nula.
- **[Agente inexistente responde `200` com agregado vazio]** → guarda de `404`, e
  o par que o torna discriminante: agente **existente e vazio** responde `200`.
  Sem o par, um `404` para tudo passaria.
- **[Agente inativo responde `404` por engano]** → guarda próprio, porque
  `IsActive` existe e a consulta de existência é onde o erro cabe.
- **[Um guarda de agregação fica verde sobre banco vazio]** → é a forma da
  vacuidade (convenção 8). Todo cenário roda sobre **banco povoado**, com asserção
  da precondição contando linhas antes de asserir, no molde de `SeededRowCounts`.
- **[A semeadura das duas classes de teste colide e o resultado passa a depender
  da ordem]** → fixture própria, banco próprio (D11), e semeadura com dados
  próprios.
- **[A 31ª classe de contêiner derruba a suíte por contenção e é lida como
  regressão]** → a baseline é **remedida** nesta change, com o alvo anterior
  registrado, e o número de classes passa a ser escrito com o estado colado.
- **[M19 do agente é lida como "tokens de embedding" e comparada com a do
  sistema]** → parcialidade declarada na resposta, cenário de spec, e a etapa 4
  recebe a restrição de rótulo.
- **[Tempo de resposta inaceitável com volume real]** → **não mitigado, e
  declarado**, como na A: o volume de dev e do piloto não mede latência. O escopo
  por agente é estritamente menor que o do sistema, e cai num índice existente, o
  que torna este risco **menor** que o da A — não inexistente.

## Migration Plan

**Nenhuma migração de banco.** A change é somente leitura, e não cria coluna,
tabela nem índice.

**O que o deploy exige:** nada além da imagem. `TZ` e os instantes de regime já
estão configurados desde a A, e são os mesmos valores — a rota nova lê a mesma
seção `Metrics`.

**Rollback:** reverter a imagem. Sem estado novo. A rota do sistema não é tocada,
então um rollback não afeta a tela que a etapa 4 já vier consumindo.

**Conferência pós-deploy** (não é tarefa de código):

- `GET /insights/agents/{id}` com um id real devolve `200` e o agregado;
- o mesmo com um `Guid` inventado devolve `404`;
- para um agente que delega e é delegado, os dois lados chegam preenchidos, e a
  soma de "Acionado por" bate com as tasks executadas por delegação daquele
  agente — que é a conferência que o protótipo do cenário 3 descreve;
- um agente com delegação `NotStarted` ou `Expired` mostra os dois lados
  **divergindo**, e a divergência é o resultado esperado.

## Projeção de tamanho (convenção 18 — décima quinta medição)

A projeção fica aqui; o fechamento **só compara**.

**Método, com as lições que a série acumulou:**

- **três níveis separados** — escrito à mão, duplo, gerado;
- **o par não é universal**, e este é o item que a décima quarta deixou. Ela
  superprojetou casos em **62%** (34 previstos, 21 reais) por contar par em todo
  requisito; a décima terceira subprojetou em **34%** por contar guarda e não par.
  **Aqui o par é projetado só onde o comportamento errado é exprimível.**
  Requisito cujo oposto é inobservável — *"a rota devolve o agregado"*, *"os dois
  lados chegam separados"* — não tem guarda negativo a escrever;
- **duplo classificado por natureza, não por arquivo**: a semeadura e o fixture
  moram dentro do arquivo de casos, e contam como duplo;
- **unidade declarada: casos, não métodos** — uma `[Theory]` de quatro conta
  quatro. Para spec, a contagem é de **linha escrita**, não de linha de arquivo;
- **`git diff -w`** para modificado;
- **gerado deve ser 0 de novo** — não há migração, e foi a diferença de regime que
  mais separou a décima quarta da etapa 2, onde gerado foi 45% do diff.

**Âncora unitária, medida na change A e não estimada:**
`InsightsEndpointsTests.cs` tem **475 linhas** para **15 casos**, dos quais
**~100 linhas de fixture** e **~130 de semeadura e apoio** — ou seja **~230 de
duplo** e **~16 linhas por caso**. `TimeZoneStartupValidationTests` acrescentou os
outros 6 dos 21 casos entregues.

| nível | arquivos | linhas | como foi projetado |
|---|---|---|---|
| escrito à mão — `src` | 3 criados, 1 modificado | ~700 | query, handler (as consultas do mapa, com as nove que não transferem) e DTO próprio; `InsightsEndpoints.cs` ganha a rota e o método |
| escrito à mão — teste | 1 criado | ~250 | 15 casos × ~16 linhas, **contados com o par só onde o oposto é exprimível** |
| duplo / infraestrutura de teste | 0 arquivos próprios | ~290 | fixture própria + semeadura dos **três cenários do protótipo** mais o agente vazio e o inexistente — mais rica que a da A (~230), que semeia um agente só |
| **gerado** | 0 | **0** | **não há migração** |
| comentário de correção (D14) | 1 modificado | ~1 | uma palavra em `InsightsEndpointsTests.cs` |
| documentação/config | 2 modificados | ~170 | `02` e `CHANGELOG` — **o `01` não entra**, ver abaixo |

**Totais projetados:** **4 arquivos criados**, **4 modificados**, **~1.410
linhas**, das quais **0 geradas**.

**O `01` fica de fora, e a exclusão foi conferida, não suposta.** A A o modificou
porque a D1 dela mudou a seção *"Fuso horário do sistema"*; esta change não mexe
em fuso. E o `01` **não tem inventário de rotas** — conferido: as únicas rotas
citadas lá aparecem dentro da descrição de um mecanismo (`:31`, `:188`, `:412`,
`:481`, `:494`, `:610`), nenhuma delas de agregação, e a palavra `insights` não
aparece no arquivo. **Pôr o `01` na lista por simetria com a A seria repetir o
erro que a A cometeu com o `docker-compose.yml`**, que entrou na lista fechada por
analogia com o de produção e não foi tocado.

**E `Program.cs` também fica de fora, pelo mesmo tipo de conferência:**
`Program.cs:133` já chama `app.MapInsightsEndpoints()`, e a rota nova entra pelo
mapeamento que já existe. A A modificou `Program.cs` para registrar
`TimeProvider` e a checagem de boot; nada disso se repete aqui.

**Casos de teste: 15.** Derivação, com o par explícito e **discriminado**:

| bloco | casos | par? |
|---|---|---|
| rota responde o agregado do agente | 1 | não — oposto inobservável |
| recorte exclui o que é de outro agente | 1 | não |
| `404` inexistente / `200` existente e vazio | 2 | **sim** — é o par que torna o `404` discriminante |
| `200` para agente inativo | 1 | não |
| assimetria: divergência afirmada | 1 | não — a asserção **é** a negativa |
| os dois lados coexistem | 1 | não |
| só delega / só é delegado | 2 | **sim** — dois estados observáveis do mesmo requisito |
| M30 e M19-indexação não aparecem | 1 | não |
| M19 declara a parcialidade da busca | 1 | não |
| profundidade é a do agente, não a do sistema | 1 | **sim** — o oposto é exprimível |
| balde local por agente | 1 | não — o guarda já reprova contra UTC |
| instante de regime com deslocamento | 1 | não |
| nulo negativo | 1 | **sim**, e o par positivo está no `200` vazio acima |
| autenticação sem token | 1 | não |

**A projeção de registro é a que decide o tamanho em change pequena, e é a que
mais erra**, porque cresce com os achados. Esta change produziu **três achados
próprios** — a segunda causa da divergência (D2), a recusa do caminho por vínculo
de base (D6), e o defeito de prosa da A (D14) — e as ~190 linhas de documentação
já os contêm. Se o apply produzir um quarto, o número sobe, e isso é registro, não
erro de projeção.

**Baselines — NÃO herdadas.** Os últimos registrados são `apps/api` **372/372**,
`apps/workers` **386/386** e `apps/inbox` **202/203** em suíte cheia
(`02:5513-5516`, fechamento da change A). Todos serão **remedidos**, e a medição
dirá **qual alvo rodou antes** — o flake de `apps/inbox` reprova sob contenção e
passa isolado, e o `02:5969-5974` registra que *"a régua 202/203 engana"*.

**E a régua "`podman ps` em zero" colide com o stack de dev de pé**, o que já
aconteceu no fechamento da A (`02:5519-5522`: *"`podman ps` NÃO estava em zero"*,
com três contêineres de desenvolvimento no ar). A medição desta change **declara
o estado do `podman ps`** em vez de afirmar zero.

## Árvore de pastas proposta

Só `apps/api`, e só dentro de `Insights/`:

```
apps/api/
├── src/Buteco.Api/
│   ├── Insights/
│   │   ├── InsightsPeriod.cs                                    (reusado, intocado)
│   │   ├── Endpoints/
│   │   │   └── InsightsEndpoints.cs                             (MODIFICADO: + 1 rota)
│   │   ├── Queries/
│   │   │   ├── GetSystemInsights/                               (intocado)
│   │   │   └── GetAgentInsights/                                (NOVO)
│   │   │       ├── GetAgentInsightsQuery.cs
│   │   │       └── GetAgentInsightsQueryHandler.cs
│   │   └── Responses/
│   │       ├── SystemInsightsResponse.cs                        (intocado)
│   │       └── AgentInsightsResponse.cs                         (NOVO)
│   └── Options/MetricsOptions.cs                                (reusado, intocado)
└── tests/Buteco.Api.Tests/
    ├── AgentInsightsEndpointsTests.cs                           (NOVO — casos + fixture + semeadura)
    └── InsightsEndpointsTests.cs                                (MODIFICADO: uma palavra, D14)
```

**Nada em `libs/`.** Não há nada a compartilhar entre apps aqui: a rota é de
`apps/api`, e o que se repete entre as duas rotas é interno ao mesmo projeto
(D4).

## Open Questions

1. **O rótulo das cinco que mudam de significado** (M15, M16a, M16b, M26 e o
   M14 parcial). A D7 decidiu que a métrica declara o que é; **o texto** é decisão
   da tela. *Gatilho:* o desenho da etapa 5. *Posição:* etapa 5, com esta restrição
   como entrada.
2. **M27 somar as duas fontes.** Este escopo torna a lacuna quantificável sem
   torná-la resolvida (D8). *Gatilho:* a change de coleta do motivo das recusas
   fechar. *Posição:* dentro dela, decidindo para **os dois escopos ao mesmo
   tempo**.
3. **O composto `task_executions(AgentId, StartedAt)`.** *Gatilho:* remedir os
   planos contra o piloto depois do primeiro mês das rotas em produção.
   *Posição:* junto do item de índice que a A já abriu no `02`.
4. **Os protótipos continuam sem ser abertos**, e a recusa de autenticação do
   Claude Design se repetiu nesta sessão. *Gatilho:* `/design-login` rodado pelo
   dono. *Posição:* **antes de a etapa 5 começar** — e, se o protótipo contrariar
   este documento, a correção vem por convenção 9.
5. **O instante de regime é valor de um ambiente num arquivo que vale para
   todos** — item herdado, que esta change **não** resolve e **não** redescobre.
   *Gatilho:* o primeiro ambiente novo a subir as rotas, dev incluído. *Posição:*
   item próprio, já no `02`.
6. **A régua de contenção de `apps/api` não tem critério, só contagem** — e esta
   change a deixa em 31 classes com a primeira duração registrada ao lado, sem
   decidir o limiar. O que falta decidir: se o critério passa a ser **duração com
   a contagem colada**, no molde que `apps/workers` recalibrou em 20/09/2026, ou
   se as duas suítes vão direto para a `ICollectionFixture` que dispensa a régua.
   **Item de método, não da linha `metricas-de-operacao`.** *Gatilho:* a primeira
   das três situações de consulta a acontecer — suíte de `apps/api` lenta, suíte
   reprovando em bloco na inicialização de fixture, ou a 32ª classe de contêiner.
   *Posição:* item próprio no `02`, **junto do item da `ICollectionFixture` de
   `apps/workers`**, porque são a mesma família e separá-los é o que produziu
   este estado.
