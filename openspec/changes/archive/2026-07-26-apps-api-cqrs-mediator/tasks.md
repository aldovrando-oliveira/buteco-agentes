## 1. Pacotes e configuração (apps/api)

- [x] 1.1 (apps/api) Adicionar `Mediator.Abstractions` (3.0.2) e
      `Mediator.SourceGenerator` (3.0.2) a `Directory.Packages.props` (raiz
      do monorepo).
- [x] 1.2 (apps/api) Adicionar as duas referências a
      `apps/api/src/Buteco.Api/Buteco.Api.csproj`, com
      `Mediator.SourceGenerator` usando
      `PrivateAssets=all` e
      `IncludeAssets=runtime; build; native; contentfiles; analyzers`.
- [x] 1.3 (apps/api) Adicionar `Microsoft.EntityFrameworkCore.InMemory`
      (10.0.10 — mesma versão dos demais pacotes EF Core já usados) a
      `Directory.Packages.props` e a
      `apps/api/tests/Buteco.Api.Tests/Buteco.Api.Tests.csproj`.
- [x] 1.4 (apps/api) Adicionar `<PackageReference Include="Moq" />` a
      `apps/api/tests/Buteco.Api.Tests/Buteco.Api.Tests.csproj` (versão já
      declarada centralmente em `Directory.Packages.props`, só não
      referenciada neste projeto ainda).
- [x] 1.5 (apps/api) Rodar `dotnet build` em `Api.sln` e confirmar que o
      source generator do Mediator compila sem erro antes de escrever
      qualquer Command/Query.

## 2. A2A/IAgentA2AServerRegistry — extração de interface (apps/api)

- [x] 2.1 (apps/api) Criar `A2A/IAgentA2AServerRegistry.cs` com a
      interface extraída de `AgentA2AServerRegistry`:
      `void Register(Guid agentId)` e
      `Task<A2AServer?> GetOrCreateAsync(Guid agentId, CancellationToken cancellationToken)`.
- [x] 2.2 (apps/api) Atualizar `A2A/AgentA2AServerRegistry.cs`: adicionar
      `: IAgentA2AServerRegistry` na declaração da classe (continua
      `sealed`). Nenhuma outra mudança neste arquivo — nem em
      `RoutingA2ARequestHandler` (continua dependendo do tipo concreto).
- [x] 2.3 (apps/api) Confirmar que esta é a única mudança em `A2A/*` nesta
      change (ver Decisão 6 em `design.md`) — `EnqueueingAgentHandler`,
      `PostgresTaskStore` e `RoutingA2ARequestHandler` permanecem
      intocados.

## 3. Commands/CreateAgent (apps/api)

- [x] 3.1 (apps/api) Criar
      `Agents/Commands/CreateAgent/CreateAgentCommand.cs`:
      `CreateAgentCommand(string Name, string Instructions) : ICommand<AgentResponse>`
      (campos não-nuláveis — só é construído após validação no endpoint).
- [x] 3.2 (apps/api) Criar
      `Agents/Commands/CreateAgent/CreateAgentCommandHandler.cs`:
      `ICommandHandler<CreateAgentCommand, AgentResponse>`, recebe
      `AppDbContext` e `IAgentA2AServerRegistry` via DI (a interface, não a
      classe concreta — ver tarefa 2.1); reproduz exatamente a lógica hoje
      inline em `AgentEndpoints.CreateAgentAsync` (a partir de
      `new Entities.Agent(...)`): cria a entidade, adiciona ao `DbContext`,
      `SaveChangesAsync`, chama `registry.Register(agent.Id)`, devolve
      `AgentResponse.FromEntity(agent)`.
- [x] 3.3 (apps/api) Conferir por leitura (não só pela suíte de testes)
      que `registry.Register(agent.Id)` está presente no handler — mesmo
      com o teste unitário da tarefa 8.1 cobrindo isso agora, a leitura
      continua sendo uma camada adicional de revisão (ver Risks/Trade-offs
      em `design.md`).

## 4. Queries/GetAgentById (apps/api)

- [x] 4.1 (apps/api) Criar
      `Agents/Queries/GetAgentById/GetAgentByIdQuery.cs`:
      `GetAgentByIdQuery(Guid Id) : IQuery<AgentResponse?>` (não
      `ICommand` — é leitura).
- [x] 4.2 (apps/api) Criar
      `Agents/Queries/GetAgentById/GetAgentByIdQueryHandler.cs`:
      `IQueryHandler<GetAgentByIdQuery, AgentResponse?>`; reproduz
      exatamente a query hoje inline em `AgentEndpoints.GetAgentByIdAsync`
      (`AsNoTracking().FirstOrDefaultAsync`), devolvendo
      `AgentResponse.FromEntity(agent)` ou `null`.

## 5. Queries/ListAgents (apps/api)

- [x] 5.1 (apps/api) Criar
      `Agents/Queries/ListAgents/ListAgentsQuery.cs`:
      `ListAgentsQuery : IQuery<IReadOnlyList<AgentResponse>>` (sem
      parâmetros; não `ICommand` — é leitura).
- [x] 5.2 (apps/api) Criar
      `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs`:
      `IQueryHandler<ListAgentsQuery, IReadOnlyList<AgentResponse>>`;
      reproduz exatamente a query hoje inline em
      `AgentEndpoints.ListAgentsAsync`
      (`AsNoTracking().OrderBy(CreatedAt).Select(FromEntity).ToListAsync`).

## 6. AgentEndpoints.cs (apps/api)

- [x] 6.1 (apps/api) Atualizar `CreateAgentAsync`: manter a validação
      inline (dictionary de erros, `TypedResults.ValidationProblem`)
      exatamente como está hoje; após validar, montar
      `new CreateAgentCommand(request.Name!, request.Instructions!)` e
      chamar `await mediator.Send(command, cancellationToken)`; mapear o
      `AgentResponse` retornado para `TypedResults.Created($"/agents/{response.Id}", response)`.
      Remover a injeção de `AppDbContext` e `AgentA2AServerRegistry` deste
      handler (o `registry.Register` já não é mais chamado aqui — foi
      para o handler, tarefa 3.2).
- [x] 6.2 (apps/api) Atualizar `ListAgentsAsync`: substituir o corpo por
      `await mediator.Send(new ListAgentsQuery(), cancellationToken)`,
      mapeado para `TypedResults.Ok<IReadOnlyList<AgentResponse>>(...)`.
      Remover a injeção de `AppDbContext`.
- [x] 6.3 (apps/api) Atualizar `GetAgentByIdAsync`: substituir o corpo por
      `await mediator.Send(new GetAgentByIdQuery(id), cancellationToken)`,
      mapeando `null` para `TypedResults.NotFound()` e valor não-nulo para
      `TypedResults.Ok(...)`. Remover a injeção de `AppDbContext`.
- [x] 6.4 (apps/api) Confirmar que `AgentEndpoints.cs` não tem mais
      nenhum `using Microsoft.EntityFrameworkCore;` nem referência a
      `AppDbContext`/`AgentA2AServerRegistry` — só `IMediator` e os tipos
      de Command/Query/Response.

## 7. Registro do Mediator e da interface de registry (apps/api)

- [x] 7.1 (apps/api) Em `Program.cs`, adicionar
      `builder.Services.AddMediator(options => { });` (depois de
      `AddInfrastructure`), permitindo o registro automático dos handlers
      do assembly `Buteco.Api` via source generator.
- [x] 7.2 (apps/api) Em `Program.cs`, ao lado do já existente
      `builder.Services.AddSingleton<AgentA2AServerRegistry>();`,
      adicionar
      `builder.Services.AddSingleton<IAgentA2AServerRegistry>(sp => sp.GetRequiredService<AgentA2AServerRegistry>());`
      — os dois devem resolver para a mesma instância singleton.

## 8. Testes automatizados novos (apps/api)

- [x] 8.1 (apps/api) Criar
      `apps/api/tests/Buteco.Api.Tests/CreateAgentCommandHandlerTests.cs`:
      `AppDbContext` construído com
      `Microsoft.EntityFrameworkCore.InMemory` (nome de banco único por
      teste), `IAgentA2AServerRegistry` mockado com `Moq`; instanciar
      `CreateAgentCommandHandler` diretamente (sem container de DI);
      chamar `Handle(command, CancellationToken.None)`; assertar que o
      `AgentResponse` devolvido tem `Name`/`Instructions` iguais ao
      command, que a entidade foi persistida no `DbContext`, e
      `mockRegistry.Verify(r => r.Register(response.Id), Times.Once)`.

## 9. Verificação — critério de aceite central (apps/api)

- [x] 9.1 (apps/api) Rodar a suíte completa de testes
      (`dotnet test apps/api/tests/Buteco.Api.Tests`) e confirmar que
      `HealthCheckTests`, `AgentEndpointsTests` (5 testes) e
      `A2ATaskLifecycleTests` (2 testes) passam **sem nenhuma alteração
      de assert** em relação ao estado atual — qualquer assert que
      precisar mudar para passar é sinal de mudança de comportamento
      indevida (parar e revisar antes de ajustar o teste). O novo
      `CreateAgentCommandHandlerTests` (tarefa 8.1) deve passar também.
      **Resultado**: 9/9 testes passando (`dotnet test`, via Testcontainers
      sobre Podman — `DOCKER_HOST` apontando pro socket do
      `podman-machine-default`, já que Docker Desktop não está instalado
      neste ambiente), nenhum assert alterado. Bug real encontrado e
      corrigido no processo: `AddMediator` sem configurar
      `ServiceLifetime` registra handlers como `Singleton`, incompatível
      com o `AppDbContext` `Scoped` que eles consomem — falhava a
      validação de DI do `WebApplicationFactory` no `Build()`. Corrigido
      em `Program.cs` com `options.ServiceLifetime = ServiceLifetime.Scoped`
      (ver Decisão 5 em `design.md`, atualizada).
- [x] 9.2 (apps/api) Revisão manual final: diff de
      `Agents/Endpoints/AgentEndpoints.cs` não deve conter nenhuma
      ocorrência de `AppDbContext` ou `AgentA2AServerRegistry`; diff de
      `A2A/*` deve conter só a mudança da tarefa 2.2 (implementar a
      interface) — nada de `EnqueueingAgentHandler`, `PostgresTaskStore`
      ou `RoutingA2ARequestHandler` alterado. **Confirmado**: `grep` por
      `AppDbContext|AgentA2AServerRegistry` em `AgentEndpoints.cs` não
      retorna nenhuma ocorrência; `git diff` de `AgentA2AServerRegistry.cs`
      contém só a adição de `: IAgentA2AServerRegistry` na assinatura da
      classe.
- [x] 9.3 (apps/api) Confirmar que nenhum arquivo fora de
      `apps/api/src/Buteco.Api/Agents/`,
      `apps/api/src/Buteco.Api/A2A/IAgentA2AServerRegistry.cs`,
      `apps/api/src/Buteco.Api/A2A/AgentA2AServerRegistry.cs` (só a
      assinatura da classe), `Buteco.Api.csproj`, `Program.cs`,
      `Directory.Packages.props`,
      `apps/api/tests/Buteco.Api.Tests/Buteco.Api.Tests.csproj` e o novo
      `CreateAgentCommandHandlerTests.cs` foi alterado (escopo estrito
      desta change — `apps/workers` permanece intocado). **Confirmado**
      via `git status`/`git diff --stat`: só os arquivos listados acima
      (mais os novos Commands/Queries) foram tocados; `apps/workers` sem
      nenhuma mudança.
