## 0. Central Package Management

- [x] 0.1 [infra] Criar `global.json` na raiz fixando o SDK do .NET
  (`10.0.301` ou faixa compatível)
- [x] 0.2 [infra] Criar `Directory.Build.props` na raiz com propriedades
  comuns (`Nullable`, `ImplicitUsings`, etc.) aplicáveis aos projetos de
  `apps/api` e `apps/workers`
- [x] 0.3 [infra] Criar `Directory.Packages.props` na raiz com
  `ManagePackageVersionsCentrally=true`, declarando a versão de cada
  pacote usado nesta mudança (`Microsoft.Agents.AI`,
  `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`, `A2A`,
  `Microsoft.EntityFrameworkCore.Design`,
  `Npgsql.EntityFrameworkCore.PostgreSQL`, `RabbitMQ.Client`,
  `Microsoft.Extensions.AI.OpenAI`) e dos pacotes de teste já existentes
  (`coverlet.collector`, `Microsoft.AspNetCore.Mvc.Testing`,
  `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`)
- [x] 0.4 [infra] Migrar as `PackageReference` já existentes em
  `Buteco.Api.csproj`, `Buteco.Api.Tests.csproj`, `Buteco.Workers.csproj` e
  `Buteco.Workers.Tests.csproj` para omitir `Version` (a versão passa a
  vir só do `Directory.Packages.props`), confirmando que `dotnet build`/
  `dotnet test` continuam passando após a migração

## 1. Infraestrutura de desenvolvimento

- [x] 1.1 [infra] Criar `docker-compose.yml` na raiz com serviço `postgres`
  (`postgres:18`), volume nomeado, porta `5432` exposta,
  `POSTGRES_USER`/`POSTGRES_PASSWORD`/`POSTGRES_DB` via variáveis de
  ambiente com defaults de dev
- [x] 1.2 [infra] Adicionar serviço `rabbitmq` (`rabbitmq:4.3-management`)
  ao `docker-compose.yml`, volume nomeado, portas `5672` (AMQP) e `15672`
  (UI) expostas, `RABBITMQ_DEFAULT_USER`/`RABBITMQ_DEFAULT_PASS` via
  variáveis de ambiente com defaults de dev
- [x] 1.3 [infra] Criar `.env.example` na raiz documentando todas as
  variáveis usadas pelo `docker-compose.yml`, `apps/api` e `apps/workers`
  (connection string Postgres, host/porta/usuário/senha RabbitMQ,
  base URL/chave do provedor de LLM)
- [x] 1.4 [infra] Confirmar que `.gitignore` já cobre `.env` sem cobrir
  `.env.example` (validar, não deveria precisar de mudança)
- [x] 1.5 [infra] Rodar `docker compose up` localmente e validar que
  Postgres e RabbitMQ (com management UI acessível) sobem com sucesso e
  persistem dados após `docker compose down && docker compose up`
  (validado via `podman compose`, já que este ambiente não tem o CLI
  `docker`; achado real: a imagem `postgres:18` mudou a convenção de
  volume — o volume nomeado precisa montar em `/var/lib/postgresql`, não
  mais em `/var/lib/postgresql/data` como em versões anteriores — corrigido
  no `docker-compose.yml`)

## 2. apps/api — catálogo de agentes

- [x] 2.1 [apps/api] Adicionar `PackageReference` (sem `Version` — vem do
  `Directory.Packages.props` da tarefa 0.3) a `Microsoft.EntityFrameworkCore.Design`
  e `Npgsql.EntityFrameworkCore.PostgreSQL` em `Buteco.Api.csproj`
- [x] 2.2 [apps/api] Criar entidade `Agent` (Id `Guid`, Name, Instructions,
  CreatedAt, UpdatedAt) e `AppDbContext` com `DbSet<Agent>`
- [x] 2.3 [apps/api] Configurar `AppDbContext` para ler a connection string
  do Postgres via `IConfiguration`/variável de ambiente (Options Pattern),
  nunca hardcoded
- [x] 2.4 [apps/api] Gerar migration inicial (`dotnet ef migrations add`)
  para a tabela `agents` e aplicar contra o Postgres do docker-compose
- [x] 2.5 [apps/api] Implementar endpoints minimal API: `POST /agents`,
  `GET /agents`, `GET /agents/{id}`, com validação de nome/instruções
  obrigatórios
- [x] 2.6 [apps/api] Escrever testes de integração do CRUD de agente
  (`AgentEndpointsTests`) cobrindo criação válida, criação inválida,
  listagem e consulta por id (existente e inexistente) — via Testcontainers
  (Postgres efêmero por classe de teste, não o Postgres do docker-compose);
  achado real: o `WebApplicationFactory` inicialmente tentou ligar no
  Postgres errado (o `appsettings.Development.json` vencia a configuração
  de override), corrigido substituindo o `DbContextOptions<AppDbContext>`
  via `ConfigureServices` em vez de `ConfigureAppConfiguration`

## 3. apps/api — hospedagem A2A e handoff para RabbitMQ

- [x] 3.1 [apps/api] Adicionar `PackageReference` (sem `Version` — vem do
  `Directory.Packages.props` da tarefa 0.3) a `A2A` (core — usado
  diretamente por `PostgresTaskStore`/`EnqueueingAgentHandler`), `A2A.AspNetCore`
  (para `MapA2A`) e `RabbitMQ.Client` em `Buteco.Api.csproj`. **Não**
  adicionar `Microsoft.Agents.AI` nem `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`
  a `apps/api` — revisão de design feita durante a implementação, ver
  Decisão 2 em `design.md` (registro dinâmico por agente é incompatível
  com o registro de `AIAgent` via DI que esses pacotes exigiriam)
- [x] 3.2 [apps/api] Criar tabela/entidade `a2a_tasks` (`task_id`,
  `agent_id`, `context_id`, `state`, `status_timestamp`, `payload jsonb`,
  FK para `agents`) e incluir na migration EF Core
- [x] 3.3 [apps/api] Implementar `PostgresTaskStore : ITaskStore`
  (Get/Save/Delete/ListTasks) serializando `AgentTask` com
  `A2AJsonUtilities.DefaultOptions`; usa `IServiceScopeFactory` para criar
  um `AppDbContext` por operação (instância de vida longa, cacheada no
  registry, não pode segurar um `DbContext` scoped diretamente)
- [x] 3.4 [apps/api] Implementar `ITaskJobPublisher`/`RabbitMqTaskJobPublisher`
  que publica `{ TaskId, AgentId, ContextId }` na fila durável
  `agent-tasks`
- [x] 3.5 [apps/api] Implementar `EnqueueingAgentHandler : IAgentHandler`
  cujo `ExecuteAsync` faz `TaskUpdater.SubmitAsync` e publica o job via
  `ITaskJobPublisher`, sem chamar nenhum LLM
- [x] 3.6 [apps/api] Implementar `AgentA2AServerRegistry`
  (`ConcurrentDictionary<Guid, A2AServer>`) que constrói e cacheia um
  `A2AServer` por agente sob demanda (`EnqueueingAgentHandler` +
  `PostgresTaskStore` — uma instância por agente, vinculada ao `agentId`
  para gravar a FK corretamente + `ChannelEventNotifier`), verificando
  existência do agente no banco antes de criar; e
  `RoutingA2ARequestHandler : IA2ARequestHandler` que lê o route value
  `{id}` via `IHttpContextAccessor`, resolve o `A2AServer` no registry
  (lançando `A2AException` se o agente não existir) e delega todos os 10
  métodos da interface a ele
- [x] 3.7 [apps/api] Registrar `IHttpContextAccessor`
  (`AddHttpContextAccessor`), o `AgentA2AServerRegistry` (singleton) e
  mapear a rota `/agents/{id}/a2a` uma única vez no startup via
  `endpoints.MapA2A(routingHandler, "/agents/{id}/a2a")`; ao criar um
  agente (`POST /agents`), pré-popular o registry para essa rota já
  responder na primeira chamada sem esperar um cache miss
- [x] 3.8 [apps/api] Configurar `RabbitMqOptions` (host, porta, usuário,
  senha) via `IConfiguration`/variável de ambiente, nunca hardcoded
- [x] 3.9 [apps/api] Escrever teste de integração (`A2ATaskLifecycleTests`)
  cobrindo: `SendMessage` cria task `submitted` no store, mensagem é
  publicada no RabbitMQ, `GetTask` reflete o estado persistido, e
  `SendMessage` para agente inexistente retorna erro do protocolo A2A —
  validado manualmente contra o docker-compose real (curl) antes de
  automatizar, e via Testcontainers (Postgres + RabbitMQ efêmeros) no
  teste automatizado

## 4. apps/workers — infraestrutura compartilhada

- [x] 4.1 [apps/workers] Adicionar `PackageReference` (sem `Version` — vem
  do `Directory.Packages.props` da tarefa 0.3) a `Microsoft.Agents.AI`,
  `A2A` (core — nunca `A2A.AspNetCore`, que não se aplica a um Worker sem
  hosting ASP.NET Core), `Microsoft.EntityFrameworkCore.Design`,
  `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.Extensions.AI.OpenAI`
  e `RabbitMQ.Client` em `Buteco.Workers.csproj`
- [x] 4.2 [apps/workers] Criar `AppDbContext` próprio com `DbSet<Agent>` e
  a tabela `a2a_tasks` (mesmo schema de `apps/api`, projeto independente,
  sem `ProjectReference`), lendo a connection string via
  `IConfiguration`/variável de ambiente. Gera sua própria migration
  (`InitialCreate`, schema idêntico ao de `apps/api`) só para
  ferramental/detecção de divergência do EF Core — só `apps/api` chama
  `Database.MigrateAsync`/aplica migration em runtime, já que precisa
  existir um agente antes de qualquer task referenciá-lo por FK
- [x] 4.3 [apps/workers] Implementar `PostgresTaskStore : ITaskStore`
  (mesma responsabilidade da versão em `apps/api`, implementação
  independente, também vinculada a um `agentId` por instância)
- [x] 4.4 [apps/workers] Configurar `ChatClientOptions` (base URL, chave,
  modelo) via `IConfiguration`/variável de ambiente e registrar um
  `IChatClient` (`Microsoft.Extensions.AI.OpenAI`, via `OpenAIClient` +
  `AsIChatClient()`) apontando para o endpoint configurado
- [x] 4.5 [apps/workers] Configurar `RabbitMqOptions` (host, porta,
  usuário, senha) via `IConfiguration`/variável de ambiente

## 5. apps/workers — consumo e execução

- [x] 5.1 [apps/workers] Implementar `TaskJobConsumer` (`BackgroundService`,
  substitui o `Worker.cs` placeholder do scaffold) que consome a fila
  `agent-tasks` do RabbitMQ (`AsyncEventingBasicConsumer`, prefetch 1) e
  faz `ack`/`nack` conforme o resultado do processamento
- [x] 5.2 [apps/workers] Implementar `AgentExecutionService`: busca a task e
  o agente cadastrado no Postgres, monta um `ChatClientAgent` com o system
  prompt do agente, faz `TaskUpdater.StartWorkAsync` + persiste `working`
  (via `AgentEventQueue`+`TaskProjection.Apply`, já que não há `A2AServer`
  do lado do worker para drenar a fila de eventos automaticamente).
  **Achado real**: a mensagem original do usuário não fica em
  `Task.History` a menos que a API a insira explicitamente (o SDK só
  auto-adiciona histórico em continuações) — corrigido em
  `EnqueueingAgentHandler` (tarefa 3.5) para gravá-la ao enfileirar, senão
  o Worker não teria como saber o que o usuário pediu
- [x] 5.3 [apps/workers] Chamar `AIAgent.RunAsync` com o `IChatClient`
  configurado e, em caso de sucesso, `TaskUpdater.AddArtifactAsync` +
  `CompleteAsync`, persistindo o resultado como `completed`
- [x] 5.4 [apps/workers] Em caso de falha na chamada ao LLM,
  `TaskUpdater.FailAsync` e persistir a task como `failed`, sem deixá-la
  presa em `working`
- [x] 5.5 [apps/workers] Escrever testes (`TaskJobConsumerTests`) cobrindo o
  fluxo completo consumo→working→completed com `IChatClient` mockado
  (Moq), e o cenário de falha levando a `failed` — via Testcontainers
  (Postgres + RabbitMQ efêmeros)
- [x] 5.6 [apps/workers] Adaptar `WorkerTests` existente (referenciava o
  `Worker.cs` removido) para cobrir a inicialização do
  `TaskJobConsumer`/host com RabbitMQ real (Testcontainers)

## 6. Verificação de compatibilidade de schema entre apps

- [x] 6.1 [infra] Criar o projeto `tests/CrossAppTaskStoreCompatibility.Tests`
  (fora de `Api.sln`/`Workers.sln`), referenciando `Buteco.Api` e
  `Buteco.Workers` só para este teste — não é `apps/api` referenciando
  `apps/workers` nem o contrário, é um terceiro projeto de verificação (ver
  Decisão 8 em `design.md`)
- [x] 6.2 [infra] Escrever teste que grava uma task via `PostgresTaskStore`
  de `apps/api` (contra um Postgres efêmero via Testcontainers, migrado
  com as migrations de `apps/api`) e lê a mesma task de volta via
  `PostgresTaskStore` de `apps/workers`, e vice-versa, assertando que
  `Status.State`, `ContextId`, `History` e `Artifacts` batem nos dois
  sentidos — 2/2 testes passando, confirmando que as duas implementações
  independentes concordam no schema
- [x] 6.3 [infra] `tests/CrossAppTaskStoreCompatibility.Tests` roda via
  `dotnet test` isoladamente (não faz parte de `Api.sln`/`Workers.sln`);
  incluído explicitamente na validação da tarefa 7.4

## 7. Validação ponta a ponta e documentação

- [x] 7.1 [infra] Com o docker-compose real rodando (via `podman compose`
  nesta máquina — sem `docker` CLI disponível), subir `apps/api` e
  `apps/workers` localmente e validar manualmente o fluxo completo:
  cadastrar agente → `SendMessage` na rota A2A → observar task
  `submitted` → `working` (confirmado no log do worker e pelas duas
  chamadas de `UPDATE a2a_tasks`) → estado terminal via `GetTask`.
  **Limitação do ambiente**: sem uma chave de API real da OpenAI
  disponível, o passo final observado foi `failed` (com
  `System.ClientModel.ClientResultException: HTTP 401 invalid_api_key` no
  log — confirma que o worker chamou a API real da OpenAI de verdade, não
  um stub), não `completed`. O caminho `completed` já está coberto de
  ponta a ponta pelos testes automatizados com `IChatClient` mockado
  (`TaskJobConsumerTests`); só a chamada real ao provedor de LLM não pôde
  ser demonstrada manualmente neste ambiente
- [x] 7.2 [apps/api] `dotnet test apps/api/Api.sln`: 8/8 testes passando
  (health check, CRUD de agente, ciclo de vida A2A)
- [x] 7.3 [apps/workers] `dotnet test apps/workers/Workers.sln`: 3/3 testes
  passando (host com `TaskJobConsumer`, consumo/execução de task
  completed e failed)
- [x] 7.4 [infra] `dotnet test tests/CrossAppTaskStoreCompatibility.Tests`:
  2/2 testes passando (schema compatível nos dois sentidos)
- [x] 7.5 [infra] Atualizar `README.md` na raiz com instruções de
  `docker compose up`, variáveis de ambiente necessárias e o novo fluxo de
  cadastro/uso de agente via A2A
- [x] 7.6 [infra] Atualizar `context` de `openspec/config.yaml` para
  refletir `.NET 10` (estava desatualizado citando ".NET 9") e corrigir a
  menção a `Microsoft.Agents.AI.Hosting.A2A` para refletir a Decisão 2
  revisada (`apps/api` compõe `A2AServer` diretamente via `A2A`/`A2A.AspNetCore`)
