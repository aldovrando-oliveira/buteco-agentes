## 1. Configuração (apps/api)

- [x] 1.1 (apps/api) Criar `Options/PublicUrlOptions.cs` com `SectionName = "PublicUrl"` e `BaseUrl` (string, default `""`), seguindo o padrão de `CorsOptions.cs`/`McpCryptoOptions`.
- [x] 1.2 (apps/api) Registrar `PublicUrlOptions` em `Program.cs` via `builder.Services.Configure<PublicUrlOptions>(builder.Configuration.GetSection(PublicUrlOptions.SectionName))`.
- [x] 1.3 (apps/api) Adicionar seção `PublicUrl` em `appsettings.json` (vazio) e `appsettings.Development.json` (`BaseUrl = "http://localhost:5017"`, batendo com `launchSettings.json`/`docs/a2a-integration.md`).
- [x] 1.4 (apps/api) Documentar `PublicUrl__BaseUrl` em `.env.example`, seguindo o bloco de comentário já usado para `Mcp__CredentialEncryptionKey` (propósito, formato, default de dev).

## 2. Mapeamento Skill → AgentSkill (apps/api)

- [x] 2.1 (apps/api) Implementar função de slug determinístico (`Slugify`) a partir de `Skill.Name` (lowercase, não-alfanumérico → `-`, colapsar hífens repetidos, trim de `-` nas pontas).
- [x] 2.2 (apps/api) Implementar dedupe determinístico de slugs colidentes dentro da mesma lista de `Skills` (sufixo numérico na ordem de `Agent.Skills`, ex. `consulta-cep`, `consulta-cep-2`).
- [x] 2.3 (apps/api) Implementar mapeamento `Skill` → `AgentSkill` (`Id` = slug deduplicado, `Name`, `Description` = `Skill.Description ?? ""`, `Tags = []`, demais campos omitidos).
- [x] 2.4 (apps/api) Testes unitários do slug/dedupe: nomes simples, nomes com acentos/caracteres especiais, nomes colidentes (2+ skills gerando o mesmo slug base), lista vazia.

## 3. Endpoint do AgentCard (apps/api)

- [x] 3.1 (apps/api) Criar `A2A/AgentCardEndpoints.cs` com extensão `MapAgentCardEndpoint(this IEndpointRouteBuilder)` mapeando `GET /agents/{id}/.well-known/agent-card.json`.
- [x] 3.2 (apps/api) Implementar handler: resolver `id` (Guid) da rota, ler `Agent` fresco via `AppDbContext` (`AsNoTracking`, novo `IServiceScopeFactory` scope, mesmo padrão de `EnqueueingAgentHandler.GetAgentStateAsync`), 404 se não existir.
- [x] 3.3 (apps/api) Montar `AgentCard`: `Name`, `Description` (`agent.Description ?? ""`), `Version = "1.0.0"`, `DefaultInputModes = ["text/plain"]`, `DefaultOutputModes = ["text/plain"]`, `Capabilities = { Streaming = false, PushNotifications = false }`, `Provider = null`, `Skills` mapeadas (task 2.3), `SupportedInterfaces = [new AgentInterface { Url = $"{PublicUrlOptions.BaseUrl.TrimEnd('/')}/agents/{agent.Id}/a2a", ProtocolBinding = "JSONRPC", ProtocolVersion = "1.0" }]`.
- [x] 3.4 (apps/api) Registrar `app.MapAgentCardEndpoint()` em `Program.cs`, ao lado do `MapA2A(...)` já existente (linha 49).

## 4. Testes de integração (apps/api)

- [x] 4.1 (apps/api) Criar `AgentCardEndpointTests.cs` reaproveitando a `WebApplicationFactory`/fixture já usada em `A2ATaskLifecycleTests.cs`.
- [x] 4.2 (apps/api) Teste: agente recém-criado com uma `Skill` com `Description` preenchida → `GET` card → confirma `Name`/`Description`/`Skills` mapeadas corretamente (cobre o caso "com Description").
- [x] 4.3 (apps/api) Teste do Scenario "Skill sem Description mapeada como string vazia": criar agente com uma `Skill` cujo `Description` é nulo → `GET` card → confirma que o `AgentSkill` correspondente tem `Description === ""`.
- [x] 4.4 (apps/api) Teste do Scenario "Skills com Name repetido geram Id únicos", via endpoint real (não só a unidade da task 2.4): criar agente com duas ou mais `Skills` cujo `Name` gera o mesmo slug base → `GET /agents/{id}/.well-known/agent-card.json` → confirma que os `AgentSkill` correspondentes na resposta real têm `Id` distintos entre si.
- [x] 4.5 (apps/api) Teste: `id` inexistente → `404`.
- [x] 4.6 (apps/api) Teste de staleness: criar agente → `GET` card → `PUT /agents/{id}` com `Name`/`Description`/`Skills` diferentes → `GET` card de novo → confirma que os campos mudaram.
- [x] 4.7 (apps/api) Teste: agente desativado (`POST /agents/{id}/deactivate`) → card ainda retorna `200`.
- [x] 4.8 (apps/api) Teste: agente sem `Provider`/`Model` (estado "precisa de reconfiguração") → card ainda retorna `200`.
- [x] 4.9 (apps/api) Teste: agente sem nenhuma `Skill` → card com `Skills: []`.
- [x] 4.10 (apps/api) Teste de shape: confirma `Capabilities.Streaming === false`, `Capabilities.PushNotifications === false`, `SupportedInterfaces[0].Url` aponta para `/agents/{id}/a2a`, `Skills[].Id` presente e estável entre duas chamadas consecutivas para o mesmo agente, **e** os três campos estáticos da Decision 8: `Version === "1.0.0"`, `DefaultInputModes === ["text/plain"]`, `DefaultOutputModes === ["text/plain"]`.
- [x] 4.11 (apps/api) Teste de regressão: chamar o método JSON-RPC `GetExtendedAgentCard` na rota `/agents/{id}/a2a` existente continua lançando erro `ExtendedAgentCardNotConfigured` (documenta que esse método permanece fora do contrato suportado, não foi tocado por esta change).

## 5. Documentação (apps/api)

- [x] 5.1 (apps/api) Atualizar `docs/a2a-integration.md` com uma seção sobre o novo endpoint de descoberta (`GET /agents/{id}/.well-known/agent-card.json`), incluindo exemplo de request/response e a nota de que `GetExtendedAgentCard` via JSON-RPC não é suportado.
