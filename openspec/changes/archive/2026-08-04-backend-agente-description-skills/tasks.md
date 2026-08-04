## 1. Entidade e persistência (apps/api)

- [x] 1.1 Adicionar o record/tipo `Skill` (`Name: string`, `Description: string?`) em `Agents/Entities/`
- [x] 1.2 Adicionar `Description` (`string?`) e `Skills` (`IReadOnlyList<Skill>`, default `[]`) em `Agent` ([Agent.cs](../../../apps/api/src/Buteco.Api/Agents/Entities/Agent.cs))
- [x] 1.3 Estender `Agent.UpdateDetails` para receber e atualizar `Description` e `Skills` (replace completo da lista)
- [x] 1.4 Mapear `Description` (nullable) e `Skills` (jsonb, `HasDefaultValueSql("'[]'::jsonb")`, `HasConversion` + `ValueComparer` explícito por igualdade estrutural) em `AppDbContext` ([AppDbContext.cs](../../../apps/api/src/Buteco.Api/Infrastructure/AppDbContext.cs)), seguindo o padrão de `AllowedTools`
- [x] 1.5 Gerar a migration EF Core aditiva (`dotnet ef migrations add AddAgentDescriptionAndSkills`) e revisar o SQL gerado

## 2. Comandos de criação e atualização (apps/api)

- [x] 2.1 Adicionar `Description` e `Skills` em `CreateAgentCommand` e repassar para o construtor/entidade em `CreateAgentCommandHandler`
- [x] 2.2 Adicionar `Description` e `Skills` em `UpdateAgentCommand` e repassar para `Agent.UpdateDetails` em `UpdateAgentCommandHandler`

## 3. Requests, response e validação HTTP (apps/api)

- [x] 3.1 Adicionar `Description` (`string?`) e `Skills` (`IReadOnlyList<SkillRequest>?`) em `CreateAgentRequest` e `UpdateAgentRequest`
- [x] 3.2 Adicionar `SkillRequest` (`Name: string?`, `Description: string?`) em `Agents/Requests/`
- [x] 3.3 Adicionar `Description` e `Skills` (`IReadOnlyList<SkillResponse>`) em `AgentResponse.FromEntity`, com `SkillResponse` análogo a `McpServerSummaryResponse`
- [x] 3.4 Estender `AgentEndpoints.ValidateShape` para validar que cada item de `Skills`, se houver, tem `Name` não vazio — erro por item no formato `skills[{index}].name`, mesmo padrão de `mcpServers[{id}].allowedTools`
- [x] 3.5 Repassar `Description`/`Skills` do request para `CreateAgentCommand`/`UpdateAgentCommand` em `CreateAgentAsync`/`UpdateAgentAsync`

## 4. Testes (apps/api)

- [x] 4.1 `CreateAgentCommandHandlerTests`: criar agente com `Description` e `Skills`, criar sem nenhum dos dois (defaults `null`/`[]`)
- [x] 4.2 `AgentEndpointsTests`: rejeitar `POST`/`PUT` com skill de `Name` vazio (HTTP 400); atualizar agente substituindo o conjunto inteiro de `Skills`; `GET`/list refletindo `Description`/`Skills` corretamente para um agente criado normalmente
- [x] 4.3 `AgentEndpointsTests`: cobrir os dois `Scenario` do spec.md sobre agente legado ("Lista inclui agente legado sem description e sem skills", "Consulta de agente legado sem description e sem skills") como testes de integração de verdade — adicionar `SeedLegacyAgentWithoutDescriptionOrSkillsAsync`, seguindo exatamente o padrão de `SeedLegacyAgentWithoutProviderOrModelAsync` (linha 231): insere a linha via `ExecuteSqlInterpolatedAsync` sem as colunas `description`/`skills` (deixando o `DEFAULT` da coluna preencher), contra o `ApiFactoryFixture` já migrado — não um `PostgreSqlContainer` isolado. Depois chama `GET /agents` e `GET /agents/{id}` via `_client` de verdade e confirma no JSON deserializado que `description` é `null` e `skills` é `[]`. Isto é o que passa pelo caminho real de serialização (`HasConversion`/`ValueComparer`) e fecha a lacuna que a 4.4 (nível de schema) não cobre.
- [x] 4.4 Novo teste de migration (`AgentDescriptionAndSkillsMigrationTests`, mesmo padrão de `AgentMcpServerAllowedToolsMigrationTests`): `PostgreSqlContainer` isolado, migra até a migration anterior, insere linha, migra até a última, confirma via `DbContext` direto (sem HTTP) que `description = NULL` e `skills = []` no schema — continua existindo para cobrir a migration em si (comportamento do `ALTER TABLE`/`DEFAULT` durante o `MigrateAsync`), papel diferente e complementar ao teste de API da 4.3, não duplicado por ele

## 5. Verificação final obrigatória (apps/api, apps/workers, cross-app)

**Bloqueio de ambiente**: este sandbox não tem Docker instalado (`docker: command not found`, sem `/var/run/docker.sock`). `ApiFactoryFixture`, `AgentDescriptionAndSkillsMigrationTests`, `AgentMcpServerAllowedToolsMigrationTests` e o equivalente em `CrossAppTaskStoreCompatibility.Tests` dependem de `Testcontainers.PostgreSql`, que precisa de um daemon Docker acessível para subir o Postgres efêmero — sem isso, `dotnet test` falha na construção da fixture (`DockerUnavailableException`), não por erro de lógica. Confirmado que isso já afetava a suíte **antes** desta change (testes pré-existentes não tocados por ela, como `HealthCheckTests`, `CorsTests`, `ProviderEndpointsTests`, falham do mesmo jeito) — não é uma regressão introduzida aqui.

O que foi possível verificar sem Docker:
- `dotnet build` limpo (0 erros) em `Api.sln`, `Workers.sln` e `CrossAppTaskStoreCompatibility.Tests` com as mudanças desta change.
- Ao buildar `CrossAppTaskStoreCompatibility.Tests`, o compilador pegou um call site real de `new Agent(...)` em `PostgresTaskStoreCompatibilityTests.cs:95` que quebrava com a assinatura nova — corrigido (`null, []` para `Description`/`Skills`). Sem Docker, esse erro só apareceria em CI, não localmente.
- Os 4 testes de `CreateAgentCommandHandlerTests` (não dependem de Postgres, usam `UseInMemoryDatabase`) passam, incluindo os 2 novos desta change (`Handle_WithDescriptionAndSkills_PersistsThem`, `Handle_WithoutDescriptionOrSkills_UsesDefaults`).
- Os demais testes desta change (`AgentEndpointsTests.*`, `AgentDescriptionAndSkillsMigrationTests`) compilam corretamente contra o `ApiFactoryFixture`/`PostgreSqlContainer` reais, mas **não foram executados** — mesma limitação que já afeta toda a suíte de integração do repositório neste ambiente.

- [x] 5.1 Rodar `dotnet test` em `Api.sln` (apps/api) e confirmar suíte completa passando, não só os testes novos — bloqueado neste sandbox (sem Docker); **confirmado manualmente pelo usuário em ambiente com Docker disponível**
- [x] 5.2 Rodar `dotnet test` em `Workers.sln` (apps/workers) e confirmar que nada quebrou, mesmo sem mudança de código nesse app — build limpo confirmado neste sandbox; execução completa **confirmada manualmente pelo usuário**
- [x] 5.3 Rodar `dotnet test` em `tests/CrossAppTaskStoreCompatibility.Tests` e confirmar compatibilidade preservada — build limpo confirmado neste sandbox (após corrigir o call site de `Agent`); execução completa **confirmada manualmente pelo usuário**
- [x] 5.4 Só marcar a change como pronta para revisão depois que 5.1, 5.2 e 5.3 passarem — cumprido: usuário confirmou execução manual bem-sucedida da suíte completa em 2026-08-04
