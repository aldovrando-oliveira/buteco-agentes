## 1. Modelo e persistência (apps/api)

- [x] 1.1 [apps/api] Adicionar `IsActive` a `Agents/Entities/Agent.cs`: propriedade `bool IsActive { get; private set; }`, `true` no construtor.
- [x] 1.2 [apps/api] Adicionar métodos mutadores em `Agent`: `UpdateDetails(string name, string instructions)`, `Activate()`, `Deactivate()`, atualizando `UpdatedAt`.
- [x] 1.3 [apps/api] Configurar `IsActive` em `Infrastructure/AppDbContext.cs` (`OnModelCreating`, entidade `Agent`), coluna `is_active`.
- [x] 1.4 [apps/api] Gerar nova migration EF Core (`is_active boolean not null default true`) e validar que aplica limpo sobre o schema atual.

## 2. Commands (apps/api)

- [x] 2.1 [apps/api] Criar `Agents/Commands/UpdateAgent/UpdateAgentCommand.cs` (`ICommand<AgentResponse?>` com `Id`, `Name`, `Instructions`) seguindo o padrão de `CreateAgentCommand`.
- [x] 2.2 [apps/api] Criar `UpdateAgentCommandHandler`: busca o agente por id (retorna `null` se não existir), chama `UpdateDetails`, salva, retorna `AgentResponse` atualizado.
- [x] 2.3 [apps/api] Criar `Agents/Commands/ActivateAgent/ActivateAgentCommand.cs` + handler: busca por id (`null` se não existir), chama `Activate()` (idempotente), salva, retorna `AgentResponse`.
- [x] 2.4 [apps/api] Criar `Agents/Commands/DeactivateAgent/DeactivateAgentCommand.cs` + handler: busca por id (`null` se não existir), chama `Deactivate()` (idempotente), salva, retorna `AgentResponse`.

## 3. Endpoints e responses (apps/api)

- [x] 3.1 [apps/api] Adicionar `IsActive` a `Agents/Responses/AgentResponse.cs` e ao `FromEntity`.
- [x] 3.2 [apps/api] Adicionar `PUT /agents/{id}` em `AgentEndpoints.cs`: valida nome/instructions (mesmas regras de `POST /agents`), monta `UpdateAgentCommand`, mapeia `null` → `NotFound`, senão `Ok`.
- [x] 3.3 [apps/api] Adicionar `POST /agents/{id}/activate` em `AgentEndpoints.cs`: monta `ActivateAgentCommand`, mapeia `null` → `NotFound`, senão `Ok`.
- [x] 3.4 [apps/api] Adicionar `POST /agents/{id}/deactivate` em `AgentEndpoints.cs`: monta `DeactivateAgentCommand`, mapeia `null` → `NotFound`, senão `Ok`.

## 4. Rejeição de SendMessage para agente inativo (apps/api)

- [x] 4.1 [apps/api] Adicionar dependência `IServiceScopeFactory` ao construtor de `A2A/EnqueueingAgentHandler.cs` (mesmo padrão de `PostgresTaskStore`).
- [x] 4.2 [apps/api] Em `ExecuteAsync`, logo após `updater.SubmitAsync(cancellationToken)`, abrir escopo e consultar `IsActive` do agente no `AppDbContext`.
- [x] 4.3 [apps/api] Se `IsActive == false`: chamar `updater.RejectAsync(...)` e retornar, sem chamar `eventQueue.EnqueueMessageAsync` nem `taskJobPublisher.PublishAsync`.
- [x] 4.4 [apps/api] Atualizar `A2A/AgentA2AServerRegistry.cs` (`BuildServer`) para repassar `IServiceScopeFactory` ao construir `EnqueueingAgentHandler`.

## 5. Testes de integração — CRUD (apps/api, `Buteco.Api.Tests`, `ApiFactoryFixture`)

- [x] 5.1 [apps/api] Teste: `PUT /agents/{id}` com sucesso atualiza nome/instructions e retorna 200 com o agente atualizado.
- [x] 5.2 [apps/api] Teste: `PUT /agents/{id}` sem nome ou sem instructions retorna 400 e não altera o registro.
- [x] 5.3 [apps/api] Teste: `PUT /agents/{id}` para id inexistente retorna 404.
- [x] 5.4 [apps/api] Teste: `POST /agents/{id}/activate` e `POST /agents/{id}/deactivate` com sucesso retornam 200 com `isActive` refletido.
- [x] 5.5 [apps/api] Teste: chamar `activate`/`deactivate` duas vezes seguidas é idempotente (200 nas duas chamadas, sem erro).
- [x] 5.6 [apps/api] Teste: `activate`/`deactivate` para id inexistente retorna 404.
- [x] 5.7 [apps/api] Teste: `GET /agents` e `GET /agents/{id}` continuam retornando agentes inativos, com `isActive: false` refletido (sem filtro escondendo).

## 6. Teste de integração — rejeição via A2A (apps/api, `Buteco.Api.Tests`)

- [x] 6.1 [apps/api] Criar fixture (variante de `A2ATaskLifecycleFixture`) que substitui `ITaskJobPublisher` por um fake/spy in-memory via `ConfigureWebHost`, registrando as chamadas recebidas, em vez de depender do RabbitMQ real do Testcontainers.
- [x] 6.2 [apps/api] Teste: criar agente, desativá-lo (`POST /agents/{id}/deactivate`), enviar `SendMessage` na rota `/agents/{id}/a2a` — resposta síncrona traz `Task.Status.State == TASK_STATE_REJECTED`.
- [x] 6.3 [apps/api] No mesmo teste, verificar que o spy de `ITaskJobPublisher` **não** foi chamado.
- [x] 6.4 [apps/api] Teste: após a rejeição, `GetTask` para o mesmo `taskId` retorna HTTP 200 com `TASK_STATE_REJECTED` (prova que a task foi persistida, não só a resposta síncrona).
- [x] 6.5 [apps/api] Teste: reativar o agente (`POST /agents/{id}/activate`) e enviar novo `SendMessage` — volta ao fluxo normal (`TASK_STATE_SUBMITTED` + job publicado), provando que o cache do `AgentA2AServerRegistry` não interfere na checagem de `IsActive`.

## 7. Verificação final

- [x] 7.1 [apps/api] Rodar toda a suíte de testes (`dotnet test`) e confirmar que os testes existentes (`A2ATaskLifecycleTests`, `AgentEndpointsTests`, `CreateAgentCommandHandlerTests`, `CorsTests`, `HealthCheckTests`) continuam passando.
- [x] 7.2 [apps/api] Revisar que nenhuma alteração desta change vazou para `apps/workers` ou `apps/frontend`.
