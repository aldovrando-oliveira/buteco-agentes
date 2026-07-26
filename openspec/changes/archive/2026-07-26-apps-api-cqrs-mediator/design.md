## Context

`apps/api/src/Buteco.Api/Agents/Endpoints/AgentEndpoints.cs` hoje tem três
handlers HTTP (`CreateAgentAsync`, `ListAgentsAsync`, `GetAgentByIdAsync`)
que injetam `AppDbContext` diretamente e fazem tudo inline: validação de
shape do request, regra de negócio (criar entidade, checar existência) e
acesso a dados. Lido o código real (não assumido):

- `CreateAgentAsync`: valida `Name`/`Instructions` (dictionary manual de
  erros, retorna `TypedResults.ValidationProblem`), cria `new
  Entities.Agent(name, instructions)`, `dbContext.Agents.Add` +
  `SaveChangesAsync`, e **também** chama `registry.Register(agent.Id)`
  (`AgentA2AServerRegistry`, singleton) para pré-popular o `A2AServer` em
  memória do agente recém-criado, antes de devolver `TypedResults.Created`.
- `ListAgentsAsync`/`GetAgentByIdAsync`: só leem via `AppDbContext`
  (`AsNoTracking`), sem nenhum outro efeito colateral.
- `Requests/CreateAgentRequest.cs` já existe como DTO HTTP com `Name`/
  `Instructions` `string?` (nullable), usado hoje só para permitir a
  checagem de campo faltando antes de construir a entidade.
- `Program.cs` registra hoje `AddInfrastructure` (EF Core/Npgsql),
  `AddHttpContextAccessor`, `AgentA2AServerRegistry` e
  `RoutingA2ARequestHandler` como singletons. Nenhum mediator registrado.
- Testes existentes batem contra os endpoints HTTP via
  `WebApplicationFactory<Program>` + Postgres real via Testcontainers
  (`AgentEndpointsTests`, 5 casos), e `A2ATaskLifecycleTests` cria um
  agente via `POST /agents` como setup antes de exercitar a rota A2A — não
  mockam `AppDbContext` nem `AgentA2AServerRegistry`, então continuam
  válidos desde que o comportamento observável do `POST/GET /agents` não
  mude.

Verifiquei agora (não de memória de treinamento) via NuGet flatcontainer API
que `3.0.2` é a versão estável mais recente de `Mediator.Abstractions` e
`Mediator.SourceGenerator` ([martinothamar/Mediator](https://github.com/martinothamar/Mediator));
existe `3.1.0-preview.*`/`3.1.0-rc.1`, ainda em prerelease, não usado. Também
confirmei via a documentação do próprio repositório que o Mediator define
tipos de mensagem **distintos** propositalmente para CQRS —
`ICommand<TResponse>` e `IQuery<TResponse>` (além de `IRequest<TResponse>`,
genérico) — com handlers correspondentes também distintos,
`ICommandHandler<TCommand, TResponse>` e `IQueryHandler<TQuery, TResponse>`.
Estrutural/funcionalmente equivalentes por baixo, mas nomeados separado
exatamente para deixar visível, na leitura do código, o que é escrita e o
que é leitura — usamos essa distinção: Commands usam `ICommand`/
`ICommandHandler`, Queries usam `IQuery`/`IQueryHandler`. `IMediator` é o
tipo injetado para enviar as duas. `Mediator.SourceGenerator` só precisa ser
referenciado no projeto executável final (aqui, `Buteco.Api` — não há
nenhuma lib intermediária nesta fatia) com o snippet:

```xml
<PackageReference Include="Mediator.SourceGenerator">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

## Goals / Non-Goals

**Goals:**
- Cada handler HTTP em `AgentEndpoints.cs` passa a só: validar o shape do
  request (quando aplicável), montar um Command/Query, chamar
  `IMediator.Send(...)` e mapear o resultado para o `TypedResults` já
  existente — sem referenciar `AppDbContext` nem `AgentA2AServerRegistry`.
- `CreateAgentCommandHandler` é o único lugar que toca `AppDbContext` **e**
  `IAgentA2AServerRegistry` para a operação de criar agente — preserva o
  pré-aquecimento do `A2AServer` em memória como parte do mesmo efeito
  colateral atômico do comando, e essa chamada passa a ser coberta por um
  teste automatizado (ver Decisão 6), não só por revisão manual.
- Zero mudança de contrato HTTP: mesmas rotas, mesmo shape de
  request/response JSON, mesmos status codes (`201`, `200`, `400`, `404`).
- Todos os testes existentes (`HealthCheckTests`, `AgentEndpointsTests`,
  `A2ATaskLifecycleTests`) continuam passando sem alterar nenhum assert.

**Non-Goals:**
- Nenhum pipeline behavior de validação (ex. `FluentValidation` acoplado ao
  Mediator) nesta fatia — fica para quando houver necessidade concreta de
  reutilizar a mesma regra de validação em mais de um caller.
- Nenhuma mudança em `apps/workers` nem nos componentes de hospedagem A2A
  (`A2A/EnqueueingAgentHandler`, `A2A/PostgresTaskStore`,
  `A2A/RoutingA2ARequestHandler`) — são pontos de extensão exigidos pela
  assinatura do SDK A2A, não endpoints HTTP com lógica solta.
  `A2A/AgentA2AServerRegistry` é a única exceção parcial: ganha uma
  interface extraída (`IAgentA2AServerRegistry`, ver Decisão 6) só para
  testabilidade — nenhum comportamento de A2A muda.
- Nenhum `ProjectReference` novo nem conteúdo em `libs/` — esta mudança é
  inteiramente interna a `apps/api`.

## Decisions

### 1. Estrutura de pastas

```
apps/api/src/Buteco.Api/Agents/
├── Entities/
│   └── Agent.cs                            (inalterado)
├── Requests/
│   └── CreateAgentRequest.cs               (inalterado)
├── Commands/
│   └── CreateAgent/
│       ├── CreateAgentCommand.cs           (ICommand<AgentResponse>)
│       └── CreateAgentCommandHandler.cs    (ICommandHandler<CreateAgentCommand, AgentResponse>)
├── Queries/
│   ├── GetAgentById/
│   │   ├── GetAgentByIdQuery.cs            (IQuery<AgentResponse?>)
│   │   └── GetAgentByIdQueryHandler.cs     (IQueryHandler<GetAgentByIdQuery, AgentResponse?>)
│   └── ListAgents/
│       ├── ListAgentsQuery.cs              (IQuery<IReadOnlyList<AgentResponse>>)
│       └── ListAgentsQueryHandler.cs       (IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>)
├── Responses/
│   └── AgentResponse.cs                    (inalterado)
└── Endpoints/
    └── AgentEndpoints.cs                   (só rota + validação de shape + mediator.Send() + mapeamento HTTP)

apps/api/src/Buteco.Api/A2A/
└── IAgentA2AServerRegistry.cs               (novo — extraído de AgentA2AServerRegistry, ver Decisão 6)

apps/api/tests/Buteco.Api.Tests/
└── CreateAgentCommandHandlerTests.cs        (novo — ver Decisão 6)
```

Queries usam `IQuery<TResponse>`/`IQueryHandler<TQuery, TResponse>`, não
`ICommand<TResponse>`/`ICommandHandler<TCommand, TResponse>` — só o Command
de escrita (`CreateAgent`) usa os tipos de `ICommand`. É a distinção que o
próprio Mediator documenta para CQRS (ver Context) e que motiva esta
mudança inteira: tornar visível, na assinatura do tipo, o que é escrita e o
que é leitura.

**Alternativa considerada**: um único arquivo `Agents/Handlers.cs` com todos
os Commands/Queries/Handlers juntos. Rejeitada — a árvore por operação
(`Commands/CreateAgent/`, `Queries/GetAgentById/`, `Queries/ListAgents/`) é o
padrão que o próprio Mediator documenta e escala melhor conforme mais
regras entrarem nesta camada (motivação original do refactor).

### 2. `CreateAgentCommand` é `ICommand<AgentResponse>`, não um tipo union de resultado — validação continua no endpoint

`CreateAgentCommand(string Name, string Instructions)` tem campos
não-nuláveis: só é construído depois que `AgentEndpoints.CreateAgentAsync`
já validou (mesma checagem inline de hoje — dictionary manual de erros,
`TypedResults.ValidationProblem`). `CreateAgentCommandHandler` sempre
sucede e sempre devolve um `AgentResponse`.

**Alternativa considerada**: mover a validação de campos obrigatórios para
dentro do handler, com o handler devolvendo um tipo de resultado union
(ex. `CreateAgentResult { AgentResponse? Agent; IDictionary<string,
string[]>? Errors }`) que o endpoint então mapeia para `Created` ou
`ValidationProblem`. Rejeitada por dois motivos: (1) introduz um tipo novo
só para esta fatia sem necessidade concreta — a validação de shape de
request já vive naturalmente no endpoint hoje, e não há nenhum outro
caller do comando "criar agente" que precise da mesma regra; (2)
`TypedResults.ValidationProblem` é um tipo de `Microsoft.AspNetCore.Http.HttpResults`
— fazer o handler devolver algo que carrega esse formato (ou um substituto
equivalente) aproximaria o command handler de detalhes HTTP, o que é
exatamente o acoplamento que este refactor busca remover. Fica registrado
como Non-Goal explícito ("nenhum pipeline behavior de validação nesta
fatia") — se um dia outro caller (ex. um comando de seed, ou uma futura
rota gRPC) precisar da mesma regra, é o gatilho concreto para revisitar
essa decisão.

### 3. `registry.Register(agent.Id)` move para dentro de `CreateAgentCommandHandler`

Hoje `AgentEndpoints.CreateAgentAsync` chama `registry.Register(agent.Id)`
(`AgentA2AServerRegistry`) logo após persistir, para que a primeira
chamada A2A ao agente recém-criado não precise do fallback de
`AgentA2AServerRegistry.GetOrCreateAsync` (que faz uma query extra no
Postgres para confirmar que o agente existe antes de montar o
`A2AServer`). Esse call passa a viver dentro de
`CreateAgentCommandHandler`, que recebe `IAgentA2AServerRegistry` via DI
junto com `AppDbContext` — o handler é o dono de todos os efeitos
colaterais do comando "criar agente", não só da escrita no Postgres.

**Risco identificado, agora coberto por teste automatizado** (ver Decisão
6): o fallback de `GetOrCreateAsync` faz esse pré-aquecimento **não** ser
observável pelos testes de integração existentes — se `registry.Register`
fosse perdido silenciosamente durante a implementação, nenhum assert de
`AgentEndpointsTests` ou `A2ATaskLifecycleTests` detectaria a regressão (o
fallback garante que a primeira chamada A2A ainda funcione, só com uma
query a mais). Isolar `CreateAgentCommandHandler` como classe própria
(o próprio benefício direto deste refactor) torna possível testá-lo
isoladamente com um mock de `IAgentA2AServerRegistry` — ver Risks/Trade-offs.

**Alternativa considerada**: manter `registry.Register` em
`AgentEndpoints.cs`, chamado depois de `mediator.Send(...)` retornar.
Rejeitada — contraria a meta de que o endpoint só faça rota + send +
mapeamento HTTP; deixaria o endpoint com uma dependência de infraestrutura
(`AgentA2AServerRegistry`) mesmo depois do refactor.

### 4. `CreateAgentRequest.cs` continua separado de `CreateAgentCommand`

Não se funde o DTO HTTP com o Command. `CreateAgentRequest` (campos
`string?`) existe para permitir o estado "faltando" antes da validação;
`CreateAgentCommand` (campos `string` não-nuláveis) representa dados já
validados. São tipos com propósitos diferentes agora, e mantê-los
separados é consistente com a Decisão 2 (validação permanece no endpoint).

**Alternativa considerada**: fazer `CreateAgentCommand` ser o próprio tipo
vinculado do corpo da requisição (bind direto pelo minimal API),
eliminando `CreateAgentRequest.cs`. Tecnicamente funcionaria (mesmo shape
JSON), mas obrigaria `CreateAgentCommand` a ter campos nuláveis só para
suportar a validação de shape — poluindo o Command com uma preocupação de
binding HTTP. Rejeitada.

### 5. Registro do Mediator em `Program.cs` — `ServiceLifetime.Scoped` explícito

`builder.Services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);`
adicionado depois de `AddInfrastructure`. Escaneamento de handlers é
automático (mesmo assembly, via source generator) — não é necessário
declarar `options.Assemblies` explicitamente nesta fatia (um único
projeto, `Buteco.Api`).

**Descoberto durante a implementação** (não previsto na exploração
inicial): o Mediator registra `IMediator` e os handlers como `Singleton`
por padrão quando `AddMediator` é chamado sem configurar
`ServiceLifetime`. Como `CreateAgentCommandHandler`,
`GetAgentByIdQueryHandler` e `ListAgentsQueryHandler` dependem de
`AppDbContext` (`Scoped`, via `AddDbContext`), isso falha a validação de
DI do ASP.NET Core no `Build()` — "Cannot consume scoped service
`AppDbContext` from singleton" — reproduzido de fato rodando
`A2ATaskLifecycleTests`/`AgentEndpointsTests` (que sobem a aplicação real
via `WebApplicationFactory`). Corrigido configurando
`options.ServiceLifetime = ServiceLifetime.Scoped` explicitamente,
alinhando o ciclo de vida dos handlers ao do `AppDbContext` que eles
consomem. Depois da correção, a suíte completa (`HealthCheckTests`,
`AgentEndpointsTests`, `A2ATaskLifecycleTests`,
`CreateAgentCommandHandlerTests`) passa sem nenhum assert alterado.

### 6. Extrair `IAgentA2AServerRegistry` e testar `CreateAgentCommandHandler` em isolamento

`AgentA2AServerRegistry` (`A2A/`) é hoje uma `sealed class` concreta —
`Register(Guid)` e `GetOrCreateAsync(Guid, CancellationToken)` não são
`virtual`, e a classe é `sealed`, então não é mockável (nem por Moq, que
precisa de uma interface ou de membros virtuais numa classe não-selada).
Antes deste refactor isso não importava: a lógica de criar agente vivia
inline em `AgentEndpoints.CreateAgentAsync`, só testável via
`WebApplicationFactory` + Postgres real (`AgentEndpointsTests`). Depois do
refactor, `CreateAgentCommandHandler` é uma classe isolada — o benefício
direto da mudança — e isso o torna testável unitariamente, **se**
`IAgentA2AServerRegistry` existir como interface.

Decisão: extrair `IAgentA2AServerRegistry` (`Register`, `GetOrCreateAsync`)
de `AgentA2AServerRegistry`, que passa a implementá-la
(`AgentA2AServerRegistry : IAgentA2AServerRegistry`, continua `sealed`,
nenhuma outra mudança nela). `CreateAgentCommandHandler` depende da
interface, não da classe concreta. `Program.cs` registra os dois apontando
para a mesma instância singleton:

```csharp
builder.Services.AddSingleton<AgentA2AServerRegistry>();
builder.Services.AddSingleton<IAgentA2AServerRegistry>(
    sp => sp.GetRequiredService<AgentA2AServerRegistry>());
```

Isso mantém `RoutingA2ARequestHandler` (que depende do tipo concreto
`AgentA2AServerRegistry`) **inalterado** — não precisa saber da interface.
Novo teste `CreateAgentCommandHandlerTests` (`apps/api/tests/`):
`AppDbContext` via `Microsoft.EntityFrameworkCore.InMemory` (10.0.10 —
mesma versão dos demais pacotes EF Core já usados nesta fatia, confirmada
via NuGet), `IAgentA2AServerRegistry` mockado com `Moq` (já versionado
centralmente em `Directory.Packages.props`, só não referenciado ainda em
`Buteco.Api.Tests.csproj`); o teste instancia o handler diretamente (sem
container de DI), chama `Handle(command, CancellationToken.None)` e
verifica `mockRegistry.Verify(r => r.Register(agent.Id), Times.Once)`.
Isso fecha a lacuna descrita na Decisão 3/Risks: uma omissão de
`registry.Register` agora quebra este teste, não só passa despercebida.

**Escopo**: esta é a única mudança em `A2A/*` nesta change (fora disso,
`A2A/EnqueueingAgentHandler`, `A2A/PostgresTaskStore` e
`A2A/RoutingA2ARequestHandler` continuam intocados — non-goal explícito).
Justificativa para abrir essa exceção pontual: sem ela, o risco
identificado na Decisão 3 ficaria sem mitigação automatizada — a extração
de interface é puramente estrutural (nenhum comportamento de
`AgentA2AServerRegistry` muda), então não contraria o espírito do
non-goal ("nenhuma mudança em componentes de hospedagem A2A"), só sua
letra mais estrita.

**Alternativa considerada**: manter a mitigação só como revisão manual do
diff (como na primeira versão deste design), sem teste automatizado.
Rejeitada depois de reconhecer que a própria estrutura introduzida por
este refactor (handler isolado) torna o teste automatizado viável a custo
baixo (uma interface pequena, sem mudança de comportamento) — deixar
passar essa oportunidade manteria o risco sem a mitigação mais forte
disponível.

**Alternativa considerada**: usar Postgres real via `Testcontainers`
(como `AgentEndpointsTests`) em vez de EF Core InMemory para este teste.
Rejeitada — o teste verifica só que o handler chama `Register`, não
comportamento específico de Postgres/EF Core; `InMemory` é suficiente e
mais rápido, consistente com o objetivo de um teste unitário isolado (não
mais um teste de integração).

## Risks / Trade-offs

- **[Risco] `registry.Register(agent.Id)` pode ser perdido silenciosamente
  durante a migração de código sem que os testes de integração existentes
  detectem** (ver Decisão 3 — o fallback de `GetOrCreateAsync` mascara a
  ausência do pré-aquecimento nos testes ponta a ponta). → Mitigação
  primária: teste unitário automatizado
  (`CreateAgentCommandHandlerTests`, Decisão 6) que falha se essa chamada
  for removida — não depende mais só de revisão manual. Revisão manual do
  diff continua como camada adicional (`tasks.md`), mas deixa de ser o
  único guard-rail.
- **[Trade-off] `Mediator` usa source generator (compila handlers em
  tempo de build)** — qualquer erro de assinatura (ex. handler não
  implementa a interface esperada) só aparece como erro de compilação, não
  em tempo de execução. Aceitável — é o comportamento padrão do pacote e
  falha cedo (build quebra) em vez de tarde (runtime).
- **[Trade-off] Nenhuma validação reutilizável entre handlers nesta
  fatia** (ver Decisão 2, Non-Goals) — se uma segunda regra de negócio
  similar aparecer em outro Command, a duplicação da checagem inline é
  aceitável até que um segundo caso concreto justifique um pipeline
  behavior de validação.

## Migration Plan

Refactor interno, sem migração de dado nem de infraestrutura — nenhuma
migration EF Core nova (mesma tabela `agents`, mesmo schema). Não há passo
de rollout gradual: a mudança troca a implementação interna dos três
handlers de uma vez, validada pela suíte de testes existente antes do
merge. Rollback, se necessário, é reverter o commit — não há estado
persistido incompatível entre versões.

## Open Questions

(nenhuma — as únicas incertezas técnicas desta fatia, versões de pacote e
formato de registro do Mediator, foram verificadas nesta exploração via
NuGet e documentação oficial, não ficam como pergunta aberta)
