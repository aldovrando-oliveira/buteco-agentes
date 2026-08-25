## Why

Um agente hoje não sabe que dia é: nenhum carimbo de tempo chega à chamada
do LLM, então uma pergunta como "tem lugar disponível amanhã?" é respondida
por adivinhação. Com debounce e fila entre o recebimento da mensagem e a
execução da task, isso deixa de ser inconveniência ocasional e vira
ambiguidade estrutural a cada execução.

Esta é a etapa 1 de uma linha de duas (a etapa 2,
`inbox-contexto-canal-metadata`, trará o instante real da mensagem via
metadata A2A). Esta etapa entrega o mecanismo de montagem do contexto
temporal e o único espaço que `apps/workers` já tem disponível hoje: o
instante de processamento. O valor central — resolver "amanhã"
corretamente sob represamento — só fecha na etapa 2; esta etapa entrega o
agente sabendo que dia é hoje, mais o espaço já preparado para o instante
da mensagem.

## What Changes

- `apps/workers` passa a montar, a cada execução de task, um bloco de
  contexto temporal concatenado às `Instructions` do agente — sem tocar
  `Agent.Instructions` no banco, sem entrar no histórico de conversa
  persistido, sem virar `ChatMessage`.
- O bloco carrega o instante de PROCESSAMENTO (relógio do worker no momento
  da execução, sempre presente) e reserva o espaço para o instante da
  MENSAGEM (opcional, ausente nesta etapa — nenhum cliente A2A ainda o
  envia). O bloco inclui uma regra de precedência em linguagem natural:
  expressões relativas ("amanhã", "sexta que vem") se resolvem contra o
  instante da mensagem quando ele existir; sem ele, contra o instante de
  processamento — o comportamento atual, agora explícito.
- Dia da semana por extenso (pt-BR fixo, não herdado de `CurrentCulture`) e
  a defasagem entre os dois instantes (quando ambos existirem e a diferença
  ultrapassar um limiar constante) são calculados em código, nunca
  delegados ao modelo.
- Carimbo em ISO 8601 com offset — o fuso viaja no valor, então a etapa 2
  não vai exigir que `apps/inbox` rode sob o mesmo fuso de `apps/workers`.
- Fuso é lido de `TZ` do SO, sistema inteiro, sem opção por agente
  (descartado, não adiado) e sem configuração em `appsettings.json`.
  `apps/workers` ganha checagem de boot: falha a inicialização se `TZ` não
  resolver para o fuso que declara (variável ausente, vazia, ou com valor
  inválido que hoje cai em UTC de forma silenciosa).
- `TimeProvider` passa a ser o único ponto de acesso a relógio/fuso em todo
  `apps/workers` (produção: `TimeProvider.System`; testes:
  `FakeTimeProvider`) — não só no código novo desta change: os dois sites
  pré-existentes que ainda liam `DateTimeOffset.Now` diretamente
  (`TaskJobConsumer`) também passam a usar o `TimeProvider` injetado.

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `a2a-task-lifecycle`: o requisito "Workers processam a task até um
  estado terminal" ganha comportamento novo — a chamada ao LLM passa a
  incluir um bloco de contexto temporal (instante de processamento, dia da
  semana, regra de precedência) junto com as `Instructions` do agente.
- `workers-scaffold`: o requisito "Worker Service mínimo" ganha
  comportamento novo — o worker deixa de subir se o fuso do sistema (`TZ`)
  não resolver exatamente para o valor declarado, e passa a logar o fuso
  resolvido e o offset atual na inicialização. É comportamento de boot do
  processo, não de ciclo de vida de task A2A — por isso entra aqui, não em
  `a2a-task-lifecycle`.

## Impact

- **apps/workers** (único app afetado — non-goal explícito: nenhuma
  mudança em `apps/api`, `apps/inbox` ou `apps/frontend`):
  - Novo tipo (nome a definir na implementação, ex.
    `TemporalContextBlockBuilder`) em `Buteco.Workers/Agents/` — função
    pura, testável isoladamente, que recebe um `TimeProvider` e um instante
    de mensagem opcional e devolve o texto do bloco. Não depende de nada
    montado em `AgentExecutionService`.
  - `AgentExecutionService.cs`: no único ponto de montagem do
    `ChatClientAgentOptions` (`ExecuteAsync`, dentro do `try`), o texto do
    bloco passa a ser concatenado a `agent.Instructions` antes de virar
    `ChatOptions.Instructions`.
  - `Program.cs`: registro de `TimeProvider` no DI (`TimeProvider.System`)
    e nova checagem de startup (fuso resolvido vs. `TZ` declarado),
    seguindo o molde síncrono de `throw`-no-boot já usado em
    `apps/inbox` (`ValidateRouteAuthenticationClassification`) — primeira
    checagem de startup de `apps/workers`, que hoje não tem nenhuma.
  - `Messaging/TaskJobConsumer.cs`: as duas chamadas diretas a
    `DateTimeOffset.Now` (`StartAsync`/`StopAsync`, hoje só num log
    informativo) passam a usar o `TimeProvider` injetado
    (`timeProvider.GetLocalNow()`). Site pré-existente, não introduzido por
    esta change — fechado porque uma varredura completa de `apps/workers`
    pedida em revisão o encontrou como a única exceção à regra acima
    (design.md, Achado 9).
  - Nenhuma tabela, coluna ou migration nova — o bloco nunca é persistido,
    só existe no momento da chamada ao LLM.
  - Nenhuma dependência de produção nova (`TimeProvider` é BCL). Dependência
    de teste nova: `Microsoft.Extensions.TimeProvider.Testing` 10.9.0 (mais
    recente estável no NuGet.org na data desta proposta, restaurada e
    decompilada para confirmar a API usada — ver design.md, Decisão 8) em
    `Buteco.Workers.Tests`, para `FakeTimeProvider`.
  - Testes: `Buteco.Workers.Tests` ganha três frentes de cobertura, de
    natureza diferente. Unitários puros do construtor do bloco, sem
    infraestrutura (design.md, Decisão 1). Extensão do harness já existente
    de `ConversationHistoryTests.cs`/`WorkerInfrastructureFixture`
    (Testcontainers Postgres+RabbitMQ) para o teste de carimbo por sessão e
    para a entrega ao `IChatClient` (design.md, Decisão 7). Classe própria
    para a checagem de boot de `TZ`, com host mínimo
    (`Host.CreateApplicationBuilder()` registrando só `TimeProvider
    .System`) — **não** reaproveita `BuildHost`/`WorkerInfrastructureFixture`,
    sem Postgres nem RabbitMQ (design.md, Decisão 9).
