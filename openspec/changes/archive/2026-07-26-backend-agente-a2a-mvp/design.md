## Context

`apps/api` e `apps/workers` existem hoje só como scaffold (`estrutura-base-monorepo`,
já arquivada): Web API com um único endpoint de health check, Worker Service
que só loga início/fim. Nenhum dos dois tem EF Core, RabbitMQ, ou qualquer
dependência do Microsoft Agent Framework. `net10.0` já é o target framework
real dos quatro `.csproj` (`Api.sln`, `Workers.sln`), com SDK `10.0.301`
disponível — o `context` deste `openspec/config.yaml` ainda cita ".NET 9" e
está desatualizado; esta mudança não altera o config.yaml em si (fora do
escopo de código), mas todas as decisões de versão abaixo partem do estado
real (`net10.0`), não do texto desatualizado.

Verifiquei via NuGet (flatcontainer API) e Docker Hub, na data de hoje
(2026-07-25), as versões estáveis mais recentes compatíveis com `net10.0` —
nenhuma foi assumida de memória de treinamento:

| Pacote | Versão | Situação |
| --- | --- | --- |
| `Microsoft.Agents.AI` (só `apps/workers`) | 1.15.0 | Estável (GA) |
| `A2A` (core, `apps/api` e `apps/workers`) | 1.0.0-preview2 | **Preview** (SDK oficial a2aproject/a2a-dotnet) |
| `A2A.AspNetCore` (só `apps/api`) | 1.0.0-preview2 | **Preview** (SDK oficial a2aproject/a2a-dotnet) |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.10 | Estável |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 | Estável |
| `RabbitMQ.Client` | 7.2.1 | Estável |
| `Microsoft.Extensions.AI` / `.Abstractions` / `.OpenAI` (só `apps/workers`) | 10.8.1 | Estável (embrulha `OpenAI` 2.12.0) |
| Imagem `postgres` | `18` | Estável |
| Imagem `rabbitmq` | `4.3-management` | Estável (plugin management incluso) |

Decompilei os pacotes `A2A`, `A2A.AspNetCore`, `Microsoft.Agents.AI.Hosting.A2A`
e `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` (via `ilspycmd`) para entender
o mecanismo real de extensão. Isso levou a duas revisões de desenho ao longo
do trabalho, registradas nas Decisões 2 e 3 abaixo:

1. A forma "fácil" documentada (`AddA2AServer(agent)`) executa o LLM síncrono
   dentro do processo da API — não atende ao fluxo pedido.
2. **Indo além do que a exploração inicial havia identificado**: tanto
   `AddA2AServer` (`Microsoft.Agents.AI.Hosting.A2A.AspNetCore`) quanto o
   `AddA2AAgent` "easy path" do próprio `A2A.AspNetCore` registram um único
   `A2AServer` por aplicação **via DI, no startup** — a chave usada para
   resolução (nome do agente) precisa existir no momento de
   `services.Add...`, antes de `builder.Build()`. Isso é incompatível com
   agentes criados em runtime via `POST /agents`, sem restart da API. Por
   isso `apps/api` não usa `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` (nem
   `Microsoft.Agents.AI`) — ver Decisão 2.

## Goals / Non-Goals

**Goals:**
- Cadastrar um agente (nome + system prompt) via API REST, persistido em
  Postgres.
- Cada agente cadastrado responder em `/agents/{id}/a2a` via protocolo A2A.
- `SendMessage` na rota do agente cria uma task `submitted` no store durável
  e devolve controle imediatamente ao cliente — sem bloquear na chamada ao
  LLM.
- Workers consumir o job via RabbitMQ, montar o `AIAgent` com o system
  prompt cadastrado, chamar o LLM e escrever o resultado de volta no mesmo
  store, passando a task por `working` → `completed` (ou `failed`).
- Cliente conseguir consultar (`GetTask`, polling) o estado final da task
  pela API.
- Cobertura de testes automatizados para o CRUD de agente e para o fluxo de
  task até `completed`, com `IChatClient` mockado.

**Non-Goals:**
- MCP (tools, `AgentMcpBinding`).
- Adapters de canal (ChatWoot, Waha).
- Autenticação/autorização na API.
- Push notification (webhook) de task.
- Streaming SSE (`SubscribeToTask`) — ver Decisão 4, é uma limitação técnica
  real do desenho escolhido, não só uma escolha de escopo.
- Qualquer consumo disso pelo `apps/frontend`.

## Decisions

### 1. `Agent` como catálogo simples em `apps/api`

Tabela `agents` (Id `uuid`, Name `text`, Instructions `text`, CreatedAt,
UpdatedAt). Endpoints minimal API: `POST /agents`, `GET /agents`,
`GET /agents/{id}`. Sem paginação/filtro nesta fatia (YAGNI — lista é
pequena no MVP).

**Alternativa considerada**: modelar já com campos de MCP bindings/skills
(previstos no `context` do projeto). Rejeitada — non-goal explícito, e
adicionar colunas não usadas agora viola YAGNI; a migration seguinte que
trouxer MCP adiciona o que precisar.

### 2. Registro em memória de `A2AServer` por agente, resolvido dinamicamente por uma única rota — sem `AddA2AServer`, sem `AIAgent` em `apps/api`

A forma documentada mais simples, `services.AddA2AServer(agent)` (ou a
sobrecarga por nome), monta internamente um `A2AAgentHandler` que chama
`AIAgent.RunAsync(...)` — ou seja, **executa o LLM de forma síncrona dentro
do processo da API**, no mesmo `Task.Run` que atende o `SendMessage`. Isso
já contradiz o fluxo pedido (API só enfileira; quem chama o LLM é o Worker,
em outro processo).

Decompilando `A2AServerServiceCollectionExtensions.CreateA2AServer` (o
código por trás de `AddA2AServer`) para contornar isso, achei que o pacote
resolve `ITaskStore` e `IAgentHandler` como **keyed services por nome do
agente** — um ponto de extensão real, só que **keyed por um nome que
precisa ser conhecido em `services.Add...`, antes de `builder.Build()`**:

```csharp
services.AddKeyedSingleton(agentName, (sp, _) =>
{
    AIAgent requiredKeyedService = sp.GetRequiredKeyedService<AIAgent>(agentName);
    return CreateA2AServer(sp, requiredKeyedService, options);
});
```

Isso é fundamentalmente incompatível com agentes cadastrados em runtime via
`POST /agents`: não existe API para adicionar uma nova chave a um
`IServiceCollection` depois que o container já foi construído. O mesmo vale
para o "easy path" do `A2A.AspNetCore` puro (`AddA2AAgent<THandler>`), que
registra `IAgentHandler`/`ITaskStore`/`IA2ARequestHandler` como singletons
**não-keyed, um por app inteiro** — ainda mais rígido.

Decisão: não usar nenhuma das duas camadas de conveniência. `A2AServer` tem
um construtor público simples (`new A2AServer(IAgentHandler, ITaskStore,
ChannelEventNotifier, ILogger<A2AServer>, A2AServerOptions?)`) que não
depende de DI nem de `AIAgent`. `apps/api` mantém um registro próprio em
memória, `AgentA2AServerRegistry` (`ConcurrentDictionary<Guid, A2AServer>`),
que constrói e cacheia um `A2AServer` por agente sob demanda — na primeira
requisição à sua rota, ou imediatamente após `POST /agents` criar o
registro no banco (para já responder na primeira chamada sem esperar um
cache miss). Cada `A2AServer` usa:

- Um `ITaskStore` próprio (`PostgresTaskStore`), uma instância por agente
  (mesma tabela `a2a_tasks` para todos, `taskId` já é globalmente único) —
  vinculada ao `agentId` porque `SaveTaskAsync(taskId, task)` não recebe o
  id do agente como parâmetro, e a tabela grava esse valor como FK; a
  instância usa `IServiceScopeFactory` para abrir um `AppDbContext`
  (escopado) por operação, já que o próprio `PostgresTaskStore` vive
  cacheado no registry por muito mais tempo que uma requisição HTTP.
- Um `IAgentHandler` próprio (`EnqueueingAgentHandler`) cujo `ExecuteAsync`:
  1. `new TaskUpdater(eventQueue, context.TaskId, context.ContextId).SubmitAsync()`
     — task nasce `submitted`.
  2. Publica no RabbitMQ `{ TaskId, AgentId, ContextId }` (payload mínimo —
     o Worker busca a mensagem/histórico completo no Postgres, a fila não
     duplica o estado da task).
  3. Retorna, sem chamar LLM.

O SDK (`A2AServer`) continua cuidando de resolução de contexto, guarda de
estado terminal, e persistência via `SaveTaskAsync` — só a *execução* muda
de lugar.

Uma única rota `/agents/{id}/a2a` é mapeada **uma vez**, no startup, via
`A2A.AspNetCore.A2ARouteBuilderExtensions.MapA2A(endpoints, IA2ARequestHandler,
path)` — que, ao contrário da conveniência `MapA2AHttpJson`, recebe o
`IA2ARequestHandler` **diretamente como parâmetro**, não via DI keyed. Passamos
um `RoutingA2ARequestHandler` que implementa `IA2ARequestHandler` lendo o
route value `{id}` da requisição atual (via `IHttpContextAccessor`,
registrado com `builder.Services.AddHttpContextAccessor()`), resolve o
`A2AServer` correspondente no `AgentA2AServerRegistry`, e delega todos os
10 métodos da interface a ele — lançando `A2AException` (`InvalidRequest`)
se o `id` não corresponder a nenhum agente cadastrado, que o
`A2AJsonRpcProcessor` do próprio SDK converte automaticamente em um erro
JSON-RPC do protocolo A2A.

Como nada nesse caminho constrói ou executa um `AIAgent`, `apps/api` **não
referencia `Microsoft.Agents.AI` nem `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`**
— só `A2A` (core, para `ITaskStore`/`IAgentHandler`/`A2AServer`/`TaskUpdater`/
`AgentEventQueue`) e `A2A.AspNetCore` (para `MapA2A`). O motor de execução
real (`AIAgent` + `IChatClient` de verdade) vive só em `apps/workers`.

**Alternativas consideradas**:
- Deixar o `AddA2AServer(agent)` padrão chamar o LLM direto na API, e mover
  só a "escrita durável" para RabbitMQ→Workers de forma assíncrona depois
  do fato. Rejeitada — contraria explicitamente o fluxo pedido (task teria
  que nascer `working`/`completed` na hora, sem desacoplamento real).
- Manter `AddA2AServer` e registrar um `AIAgent` "placeholder" por agente
  só para satisfazer a assinatura de DI, atualizando/registrando de novo a
  cada agente criado. Descartada assim que ficou claro que
  `AddKeyedSingleton` não pode ser chamado após `builder.Build()` — não é
  uma limitação contornável com um placeholder, é uma incompatibilidade
  estrutural com registro de agentes em runtime.
- Reimplementar o parsing/dispatch JSON-RPC do zero (os tipos `JsonRpcRequest`/
  `JsonRpcResponse` são públicos no pacote `A2A`), evitando `A2A.AspNetCore`
  por completo. Rejeitada — `A2AJsonRpcProcessor` (usado internamente por
  `MapA2A`) já trata streaming, erros de protocolo e content negotiation;
  reescrever isso à mão é trabalho duplicado sem ganho, já que `MapA2A`
  aceita um `IA2ARequestHandler` construído por nós livremente.

### 3. `apps/workers` também referencia o pacote `A2A` (core) para reusar o modelo de tarefa

Assim como `apps/api` (Decisão 2), `apps/workers` toma uma dependência
direta do pacote **`A2A`** — pelo mesmo motivo de higiene de dependência, e
para escrever de volta no Postgres com a mesma semântica de transição de
estado do protocolo A2A (e o mesmo formato JSON que a API espera ao ler).
`apps/workers` referencia só o **`A2A`** (core), nunca `A2A.AspNetCore`
(que traz hosting ASP.NET Core desnecessário no Worker).
Esse pacote expõe publicamente `AgentTask`, `TaskState`, `ITaskStore`,
`TaskUpdater`, `AgentEventQueue` e `TaskProjection.Apply` (função estática
pura que projeta um `StreamResponse` em cima de um `AgentTask`).

Fluxo do Worker por job consumido:

```
GetTaskAsync(taskId)                          // via PostgresTaskStore próprio do worker
var updater = new TaskUpdater(queue, taskId, contextId);
await updater.StartWorkAsync();               // enfileira StatusUpdate(Working)
task = TaskProjection.Apply(task, evento);     // projeta em memória
await SaveTaskAsync(taskId, task);             // persiste "working"

var response = await agent.RunAsync(...)      // chamada real ao LLM

await updater.AddArtifactAsync(...);
await updater.CompleteAsync();                 // enfileira StatusUpdate(Completed)
task = TaskProjection.Apply(task, evento);
await SaveTaskAsync(taskId, task);             // persiste "completed"
```

Isso significa que `apps/api` e `apps/workers` compartilham *comportamento*
de transição de estado sem compartilhar *código próprio* — ambos dependem
do mesmo pacote NuGet público `A2A`, cada um com sua própria implementação
independente de `ITaskStore` contra o mesmo schema Postgres. Não há
violação da regra de isolamento: não é criada nenhuma pasta `libs/`.

**Alternativa considerada**: `apps/workers` escrever um JSON ad-hoc próprio
sem usar os tipos do pacote `A2A`. Rejeitada — reimplementar
`TaskProjection`/`TaskState`/formatos de `Artifact` e `Message` à mão é
trabalho duplicado e arriscado (divergência de formato faria a API não
conseguir mais desserializar o que o Worker escreveu).

### 4. Store de tasks: uma tabela `a2a_tasks` com o `AgentTask` serializado em `jsonb`

Tabela `a2a_tasks`: `task_id` (PK, text), `agent_id` (uuid, FK para
`agents`), `context_id` (text, indexado), `state` (text, indexado — espelha
`AgentTask.Status.State` para filtro/paginação em `ListTasksAsync`),
`status_timestamp` (timestamptz), `payload` (jsonb — o `AgentTask` completo,
serializado com `A2AJsonUtilities.DefaultOptions`, a mesma configuração
`System.Text.Json` usada pelo `InMemoryTaskStore` do próprio SDK).

`PostgresTaskStore` (implementado de forma independente em `apps/api` e em
`apps/workers`, mesma tabela, dois `DbContext`s distintos) faz upsert do
`payload` inteiro em `SaveTaskAsync`, e espelha `state`/`status_timestamp`/
`context_id` nas colunas simples só para permitir `ListTasksAsync` fazer
filtro/paginação sem precisar de índice `jsonb` nesta fatia.

**Confirmação técnica**: `SubscribeToTask`/streaming SSE do SDK usa um
`ChannelEventNotifier` **puramente in-process** (um `Channel<T>` em
memória, dentro do processo da API) para live-push. Como o Worker roda em
processo separado e escreve direto no Postgres, ele nunca alimenta esse
canal — uma eventual assinatura SSE ficaria presa depois do snapshot
inicial. Isso confirma, na prática, que a decisão de adiar SSE (non-goal)
não é só corte de escopo: com a arquitetura desacoplada API/Workers, SSE
exigiria plumbing adicional (ex. `LISTEN`/`NOTIFY` do Postgres, ou o Worker
chamando de volta um endpoint da API) que fica fora desta fatia. `GetTask`
(polling), por ler direto do `ITaskStore` compartilhado, funciona
corretamente sem nenhum plumbing extra.

### 5. RabbitMQ: uma fila única `agent-tasks`, mensagem mínima

Fila durável `agent-tasks`, publisher confirms habilitado no lado da API.
Mensagem: `{ TaskId, AgentId, ContextId }` (JSON). O Worker busca o
histórico/mensagem completa no Postgres pelo `TaskId` — a fila carrega só
a referência, o Postgres é a fonte de verdade do conteúdo. Isso evita
inconsistência entre o que está na fila e o que está no store caso a task
seja re-processada.

**Alternativa considerada**: colocar o conteúdo da mensagem inteira no
corpo do RabbitMQ. Rejeitada — duplicaria estado entre fila e store durável,
indo contra o requisito explícito de "nada de store em memória"/estado
único de verdade.

### 6. Docker Compose de desenvolvimento

`postgres:18` (volume nomeado `buteco-postgres-data`, porta `5432`
exposta, `POSTGRES_USER`/`POSTGRES_PASSWORD`/`POSTGRES_DB` via env com
defaults de dev) e `rabbitmq:4.3-management` (volume nomeado
`buteco-rabbitmq-data`, portas `5672` AMQP + `15672` UI, `RABBITMQ_DEFAULT_USER`/
`RABBITMQ_DEFAULT_PASS` via env com defaults de dev). `.env.example` na raiz
documenta todas as variáveis; `apps/api` e `apps/workers` leem connection
string/URI via `IConfiguration` (appsettings + variáveis de ambiente),
nunca hardcoded — consistente com a regra de segurança do projeto.

### 7. Central Package Management adotado desde já

Decisão (fecha a Open Question levantada na primeira versão deste
documento): adotar Central Package Management (CPM) nesta mudança, em vez
de adiar. Adiciona-se `global.json` (fixando o SDK `10.0.301`),
`Directory.Build.props` (propriedades comuns — `Nullable`,
`ImplicitUsings`, etc.) e `Directory.Packages.props`
(`ManagePackageVersionsCentrally=true`) na raiz do monorepo, com a versão
de cada pacote declarada uma única vez ali. Os quatro `.csproj` existentes
e os dois novos referenciam os pacotes sem `Version=`.

Justificativa: esta mudança introduz ~10 pacotes novos, vários com a
mesma versão exigida em `apps/api` e `apps/workers` ao mesmo tempo (ex.
`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Microsoft.Agents.AI`
1.15.0, `A2A` 1.0.0-preview2, `Microsoft.EntityFrameworkCore.Design`
10.0.10). Sem CPM, nada impede que um bump futuro atualize a versão em um
`.csproj` e esqueça o outro, o que é exatamente o tipo de divergência
silenciosa que o Risco de "duas implementações independentes do mesmo
schema" (ver Risks/Trade-offs) já se preocupa em evitar — é mais barato
adotar CPM agora, com 2 projetos de produção, do que depois de o catálogo
de pacotes crescer com MCP/canais.

**Alternativa considerada**: manter versão declarada em cada `.csproj`
como está hoje no scaffold. Rejeitada — o scaffold atual só tem 1 pacote
de produção total (`Microsoft.Extensions.Hosting` em `apps/workers`); essa
mudança muda esse cenário radicalmente, e o skill `dotnet-standards` do
projeto já recomenda essa estrutura.

### 8. Teste automatizado de compatibilidade de schema entre as duas implementações de `ITaskStore`

O Risco "duas implementações independentes de `ITaskStore`/`AppDbContext`
podem divergir de schema" (ver Risks/Trade-offs) estava coberto só por
revisão manual de migration. Decisão: adicionar um projeto de teste
dedicado, `tests/CrossAppTaskStoreCompatibility.Tests`, **fora** de
`Api.sln` e de `Workers.sln`, que referencia `Buteco.Api` e
`Buteco.Workers` só para este teste. O teste grava uma task via
`PostgresTaskStore` de `apps/api` contra o Postgres do `docker-compose` e
lê a mesma task de volta via `PostgresTaskStore` de `apps/workers` (e
vice-versa), assertando que `Status.State`, `ContextId`, `History` e
`Artifacts` batem nos dois sentidos.

Isso **não** viola a regra de isolamento do monorepo: a regra é
"`apps/api` e `apps/workers` não referenciam projeto um do outro" — nenhum
dos dois app passa a depender do outro. `tests/CrossAppTaskStoreCompatibility.Tests`
é um terceiro projeto, só de verificação, nunca publicado/empacotado com
nenhum dos dois apps, e roda apenas em CI/local como parte da suíte de
testes — nunca faz parte do artefato de deploy de `apps/api` nem de
`apps/workers`.

**Alternativa considerada**: em vez de um projeto novo, escrever no
`apps/api/tests` um teste que grava via `PostgresTaskStore` da API e lê de
volta com uma query SQL crua (via `NpgsqlConnection`), desserializando o
`payload` manualmente com `A2AJsonUtilities.DefaultOptions` — sem nenhuma
dependência cruzada de projeto. Essa alternativa respeitaria o isolamento
de forma mais conservadora, mas não exercita de fato o código de
`apps/workers` (só o formato dos dados) — não pegaria, por exemplo, um bug
de mapeamento EF Core que só existe no `AppDbContext` do Worker. Rejeitada
em favor do teste cruzado real, que verifica o comportamento das duas
implementações, não só o formato JSON.

### 9. Estrutura de pastas proposta

```
buteco-agents/
├── docker-compose.yml
├── .env.example
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── apps/
│   ├── api/
│   │   ├── Api.sln
│   │   ├── src/Buteco.Api/
│   │   │   ├── Buteco.Api.csproj
│   │   │   ├── Program.cs
│   │   │   ├── appsettings.json
│   │   │   ├── appsettings.Development.json
│   │   │   ├── Agents/
│   │   │   │   ├── Endpoints/AgentEndpoints.cs
│   │   │   │   ├── Entities/Agent.cs
│   │   │   │   ├── Requests/CreateAgentRequest.cs
│   │   │   │   └── Responses/AgentResponse.cs
│   │   │   ├── A2A/
│   │   │   │   ├── A2ATaskRecord.cs
│   │   │   │   ├── EnqueueingAgentHandler.cs
│   │   │   │   ├── PostgresTaskStore.cs
│   │   │   │   ├── AgentA2AServerRegistry.cs
│   │   │   │   └── RoutingA2ARequestHandler.cs
│   │   │   ├── Messaging/
│   │   │   │   ├── ITaskJobPublisher.cs
│   │   │   │   ├── RabbitMqTaskJobPublisher.cs
│   │   │   │   └── TaskJobMessage.cs
│   │   │   ├── Infrastructure/
│   │   │   │   ├── AppDbContext.cs
│   │   │   │   ├── InfrastructureServiceCollectionExtensions.cs
│   │   │   │   └── Migrations/
│   │   │   └── Options/
│   │   │       └── RabbitMqOptions.cs
│   │   └── tests/Buteco.Api.Tests/
│   │       ├── Buteco.Api.Tests.csproj
│   │       ├── HealthCheckTests.cs
│   │       ├── AgentEndpointsTests.cs
│   │       ├── A2ATaskLifecycleTests.cs
│   │       └── Support/
│   │           ├── ApiFactoryFixture.cs
│   │           └── A2ATaskLifecycleFixture.cs
│   ├── workers/
│   │   ├── Workers.sln
│   │   ├── src/Buteco.Workers/
│   │   │   ├── Buteco.Workers.csproj
│   │   │   ├── Program.cs
│   │   │   ├── appsettings.json
│   │   │   ├── appsettings.Development.json
│   │   │   ├── Agents/
│   │   │   │   ├── Entities/Agent.cs
│   │   │   │   └── AgentExecutionService.cs
│   │   │   ├── A2A/
│   │   │   │   ├── A2ATaskRecord.cs
│   │   │   │   └── PostgresTaskStore.cs
│   │   │   ├── Messaging/
│   │   │   │   ├── TaskJobConsumer.cs
│   │   │   │   └── TaskJobMessage.cs
│   │   │   ├── Infrastructure/
│   │   │   │   ├── AppDbContext.cs
│   │   │   │   ├── InfrastructureServiceCollectionExtensions.cs
│   │   │   │   └── Migrations/
│   │   │   └── Options/
│   │   │       ├── RabbitMqOptions.cs
│   │   │       └── ChatClientOptions.cs
│   │   └── tests/Buteco.Workers.Tests/
│   │       ├── Buteco.Workers.Tests.csproj
│   │       ├── WorkerTests.cs
│   │       ├── TaskJobConsumerTests.cs
│   │       └── Support/WorkerInfrastructureFixture.cs
│   └── frontend/            # inalterado nesta mudança
├── tests/
│   └── CrossAppTaskStoreCompatibility.Tests/
│       ├── CrossAppTaskStoreCompatibility.Tests.csproj
│       └── PostgresTaskStoreCompatibilityTests.cs
└── openspec/
```

Duas pequenas simplificações em relação à árvore originalmente proposta,
descobertas durante a implementação: `Agents/Extensions/AgentServiceCollectionExtensions.cs`
e `A2A/Extensions/A2AHostingExtensions.cs` não foram criados — não havia
nenhum conteúdo real para colocar neles (toda a composição de DI de
`apps/api` cabe em `Program.cs` + `InfrastructureServiceCollectionExtensions.cs`
de forma legível); criar arquivos vazios só para bater com a árvore
violaria a regra do projeto de evitar placeholders. O `Worker.cs` original
do scaffold foi removido — seu papel (host que inicia/loga/para) passou a
ser cumprido pelo `TaskJobConsumer`, que já precisa fazer exatamente isso
mais o consumo real da fila.

`Agent.cs` e `TaskJobMessage.cs` aparecem em ambos os apps porque cada um
mantém sua própria projeção de entidade contra o mesmo schema — não é um
`ProjectReference`, é duplicação intencional e pequena (poucos campos),
consistente com a regra de isolamento do monorepo. `PostgresTaskStore.cs`
também é implementado independentemente nos dois apps pelo mesmo motivo
(Decisão 3). `tests/CrossAppTaskStoreCompatibility.Tests` é o único
projeto do monorepo que referencia `Buteco.Api` e `Buteco.Workers` ao
mesmo tempo — propositalmente, só para o teste de compatibilidade de
schema descrito na Decisão 8; nenhum dos dois apps o referencia de volta.

Nenhum conteúdo é colocado em `libs/` nesta mudança — os únicos pontos de
reuso de código entre os apps (modelo de task A2A, transições de estado)
já são resolvidos por dependerem do mesmo pacote público `A2A`, sem
necessidade de código próprio compartilhado.

## Risks / Trade-offs

- **[Risco] `A2A` e `A2A.AspNetCore` não têm nenhum release estável (só
  preview)** — a API pode mudar de forma incompatível em atualizações
  futuras. (Não usamos mais `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`
  nem `Microsoft.Agents.AI.Hosting.A2A` em nenhum app — ver Decisão 2 — o
  que também elimina a necessidade de suprimir o diagnostic experimental
  `MEAI001` que esses pacotes exigiam.)
  → Mitigação: isolar o uso desses pacotes atrás de abstrações próprias
  (`ITaskJobPublisher`, `EnqueueingAgentHandler`, `PostgresTaskStore`,
  `AgentA2AServerRegistry`) para minimizar a superfície de código que
  precisa mudar se a API do pacote quebrar; fixar as versões exatas (sem
  wildcard) e revisar o changelog do `a2aproject/a2a-dotnet` antes de
  qualquer bump.
- **[Risco] Registro dinâmico por agente depende de compor `A2AServer` e
  `IA2ARequestHandler` manualmente, fora do caminho "fácil" documentado
  pelo SDK** — embora os pontos usados sejam públicos (construtor de
  `A2AServer`, `MapA2A(endpoints, IA2ARequestHandler, path)`), é uma
  composição própria, não um cenário com exemplo oficial. → Mitigação:
  cobrir esse caminho com teste de integração (`A2ATaskLifecycleTests`)
  que falha alto e cedo se um futuro bump de pacote quebrar essa
  composição.
- **[Risco] Duas implementações independentes de `ITaskStore`/`AppDbContext`
  (API e Workers) podem divergir de schema ao longo do tempo** → Mitigação:
  coberto automaticamente pelo teste de compatibilidade cruzada
  (`tests/CrossAppTaskStoreCompatibility.Tests`, Decisão 8), que grava via
  um app e lê via o outro a cada execução da suíte de testes — não depende
  só de revisão manual de migration. Migrations EF Core de cada app
  continuam versionadas e revisadas juntas nesta mudança como camada
  adicional de segurança.
- **[Trade-off] Sem SSE nesta fatia** — cliente precisa fazer polling em
  `GetTask` para saber quando a task terminou. Aceitável para o MVP; fica
  registrado como trabalho futuro caso o produto precise de atualização em
  tempo real.
- **[Trade-off] `AgentA2AServerRegistry` é um cache em memória, por
  instância do processo da API** — se a API rodar com múltiplas réplicas,
  cada uma constrói seu próprio `A2AServer` por agente sob demanda (todos
  apontando para o mesmo Postgres/RabbitMQ, então não há inconsistência de
  dados, só duplicação de objetos em memória entre réplicas). Aceitável
  para o MVP (single-instance); se o produto escalar horizontalmente, vale
  revisitar.

## Open Questions

- O nome/rota do agente (`/agents/{id}/a2a`) usa o `Id` (uuid) do agente. Se
  o produto quiser uma rota mais amigável (slug) no futuro, isso é uma
  mudança de contrato de API — vale confirmar que `id` numérico/uuid é
  aceitável para os consumidores A2A que já existem hoje (nenhum, pelo que
  foi levantado) antes de fixar.
