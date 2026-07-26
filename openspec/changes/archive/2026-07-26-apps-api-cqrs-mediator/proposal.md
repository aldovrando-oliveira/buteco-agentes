## Why

`apps/api/src/Buteco.Api/Agents/Endpoints/AgentEndpoints.cs` acessa `AppDbContext`
diretamente dentro dos três handlers HTTP (criar, listar, consultar agente),
misturando roteamento, regra de negócio e persistência na mesma classe. Isso
prejudica testabilidade unitária do handler (só é possível testar via
`WebApplicationFactory` + Postgres real) e tende a piorar conforme mais
regras/endpoints entrarem nesta camada. Introduzir CQRS via
[Mediator](https://github.com/martinothamar/Mediator) separa "o que o endpoint
faz" (rota, mapeamento HTTP) de "o que a operação faz" (handler), sem mudar
nenhum comportamento observável.

## What Changes

- Adiciona os pacotes NuGet `Mediator.Abstractions` e `Mediator.SourceGenerator`
  (3.0.2 — última versão estável no NuGet hoje; `3.1.0-rc.1` existe mas é
  prerelease, não usada) a `Directory.Packages.props` e a
  `apps/api/src/Buteco.Api/Buteco.Api.csproj`. `Mediator.SourceGenerator` entra
  com `PrivateAssets=all` e `IncludeAssets=runtime;build;native;contentfiles;analyzers`
  (snippet recomendado pelo próprio pacote) — é ferramenta de build, nunca uma
  dependência transitiva de quem referenciar `Buteco.Api`.
- **apps/api**: reestrutura `Agents/` para separar cada operação em um
  Command/Query com seu handler, usando os tipos de mensagem distintos que o
  Mediator define especificamente para CQRS:
  - `Commands/CreateAgent/` — `CreateAgentCommand` (`ICommand<AgentResponse>`)
    + `CreateAgentCommandHandler` (`ICommandHandler<CreateAgentCommand, AgentResponse>`),
    único ponto que toca `AppDbContext` e `IAgentA2AServerRegistry` para esta
    operação.
  - `Queries/GetAgentById/` e `Queries/ListAgents/` — `IQuery<TResponse>` +
    `IQueryHandler<TQuery, TResponse>` por consulta (não `ICommand`/
    `ICommandHandler` — são leituras, e o Mediator nomeia os dois tipos de
    mensagem separadamente exatamente para deixar essa distinção visível na
    revisão de código), cada um só lendo do `AppDbContext`.
  - `AgentEndpoints.cs` deixa de referenciar `AppDbContext` e
    `AgentA2AServerRegistry` diretamente: cada handler HTTP passa a validar o
    shape do request (guard clause, igual hoje), montar o Command/Query
    correspondente, chamar `IMediator.Send(...)` e mapear o resultado para o
    `TypedResults` já existente.
  - `Program.cs` passa a registrar o Mediator (`services.AddMediator(...)`).
- **apps/api**: extrai `IAgentA2AServerRegistry` de `AgentA2AServerRegistry`
  (`A2A/`) — só a interface, nenhuma mudança de comportamento — para que
  `CreateAgentCommandHandler` dependa da interface, não da classe concreta, e
  possa ser testado com um mock. É o único ponto em que esta change toca
  `A2A/*` (ver Impact); `RoutingA2ARequestHandler` continua dependendo da
  classe concreta, sem alteração.
- Registro em `AgentA2AServerRegistry` (pré-aquecimento do `A2AServer` em
  memória logo após criar um agente) passa a acontecer dentro de
  `CreateAgentCommandHandler`, não mais em `AgentEndpoints.cs` — é um efeito
  colateral do comando "criar agente", não do roteamento HTTP.
- Nenhuma outra pasta muda: `Entities/Agent.cs`, `Requests/CreateAgentRequest.cs`
  e `Responses/AgentResponse.cs` continuam com a mesma forma e mesmo papel.
- **apps/api/tests**: novo teste unitário de `CreateAgentCommandHandler`
  (`AppDbContext` via EF Core InMemory, `IAgentA2AServerRegistry` mockado com
  Moq) que falha caso a chamada a `Register(agent.Id)` seja removida —
  substitui "revisão manual do diff" como mitigação do risco descrito em
  `design.md` (Decisão 3).
- **BREAKING**: nenhuma. Mesmas rotas, mesmos request/response JSON, mesmos
  status codes. Refactor interno, sem consumidores externos afetados.

## Capabilities

### New Capabilities
(nenhuma — esta mudança não introduz nenhuma funcionalidade nova)

### Modified Capabilities
(nenhuma — `agent-catalog` (cadastro, listagem, consulta de agentes) mantém
exatamente os mesmos requisitos e cenários já arquivados em
`openspec/specs/agent-catalog/spec.md`: mesmas rotas, mesmos critérios de
validação, mesmos status codes, mesma garantia de persistência em Postgres.
CQRS/Mediator é uma decisão de como o código é estruturado internamente
dentro de `apps/api`, não uma mudança de comportamento observável do sistema
— não há requisito ou cenário novo, alterado ou removido. Por isso esta
change não tem specs delta; é tratada como `tasks.md`-only, com o desenho
registrado em `design.md`.

**Nota sobre `openspec validate`**: o schema `spec-driven` desta instalação
do CLI exige mecanicamente pelo menos um delta real (`## ADDED/MODIFIED/
REMOVED Requirements` com Scenario) em `specs/` para qualquer change —
mesmo quando a própria instrução de autoria do artefato `proposal` diz
"leave empty if no requirement changes". Testado: tanto uma nota
explicativa sem headers de delta quanto `specs/` vazio fazem
`openspec validate` falhar com "Change must have at least one delta". Essa
change mantém `specs/` intencionalmente vazio — o erro de `validate` é um
falso positivo conhecido para uma mudança que genuinamente não altera
nenhum requisito, não um problema a corrigir. `/opsx:apply` continua
funcionando normalmente (o `schema.yaml` só exige `tasks.md` para
implementar); só `openspec validate` fica com esse erro esperado.)

## Impact

- **apps/api**: `Agents/Commands/`, `Agents/Queries/` (pastas novas);
  `AgentEndpoints.cs` perde toda referência a `AppDbContext`/
  `AgentA2AServerRegistry`; `Program.cs` ganha registro do Mediator e da
  interface `IAgentA2AServerRegistry`; `Buteco.Api.csproj` e
  `Directory.Packages.props` ganham as duas referências de pacote do
  Mediator.
- **apps/api — `A2A/AgentA2AServerRegistry.cs`**: ganha `: IAgentA2AServerRegistry`
  na declaração da classe (novo arquivo `A2A/IAgentA2AServerRegistry.cs` com a
  interface); nenhuma outra mudança nesse arquivo. É a única exceção ao "fora
  de escopo" abaixo, motivada só por testabilidade (ver Decisão 6 em
  `design.md`).
- **apps/api/tests**: nenhuma mudança de assert esperada em
  `HealthCheckTests`, `AgentEndpointsTests`, `A2ATaskLifecycleTests` — são o
  critério de aceite desta change (continuam passando sem alterar nenhum
  assert). `Buteco.Api.Tests.csproj` e `Directory.Packages.props` ganham
  `Moq` (já versionado centralmente, só não referenciado neste projeto ainda)
  e `Microsoft.EntityFrameworkCore.InMemory` (10.0.10, mesma versão dos
  demais pacotes EF Core já usados) para o novo teste unitário de
  `CreateAgentCommandHandler`.
- **Fora de escopo**: `A2A/EnqueueingAgentHandler`, `A2A/PostgresTaskStore`,
  `A2A/RoutingA2ARequestHandler` continuam como estão — são pontos de
  extensão exigidos pela assinatura do SDK A2A, não endpoints HTTP com
  lógica solta. `apps/workers` não é afetado.
