## 1. `libs/ProviderCatalog` (novo)

- [x] 1.1 [libs] Criar `libs/ProviderCatalog/Buteco.ProviderCatalog.csproj` (class library, `net10.0`, `ManagePackageVersionsCentrally` herdado da raiz).
- [x] 1.2 [libs] Implementar `LlmProviderDefinition`, `LlmProviders` (constantes `OpenAi`/`Anthropic`/`Gemini` + `All`) e `IsConfigured(IConfiguration)` conforme Decision 2 do design.md.
- [x] 1.3 [libs] Criar `libs/ProviderCatalog.Tests/Buteco.ProviderCatalog.Tests.csproj` referenciando `Buteco.ProviderCatalog` e `xunit`.
- [x] 1.4 [libs] Testes: `IsConfigured` retorna `true`/`false` corretamente a partir de um `IConfiguration` construído em memória (com e sem a chave `ApiKey` na seção do provedor), para os três provedores.
- [x] 1.5 [libs] Adicionar `libs/ProviderCatalog/Buteco.ProviderCatalog.csproj` como `ProjectReference` em `apps/api/src/Buteco.Api/Buteco.Api.csproj` e `apps/workers/src/Buteco.Workers/Buteco.Workers.csproj`.

## 2. `apps/api` — dependências e pacotes

> Nota de execução: os pacotes abaixo (`Anthropic`, `Google.GenAI`) são consumidos por `apps/workers` (constrói os `IChatClient`), não por `apps/api` (só checa presença via `IConfiguration`, nunca importa esses SDKs — ver Decision 2 do design.md). O cabeçalho desta seção estava rotulado `apps/api` por engano; os `PackageReference` foram adicionados em `apps/workers/src/Buteco.Workers/Buteco.Workers.csproj`, e as versões em `Directory.Packages.props` (compartilhado, correto de qualquer forma).

- [x] 2.1 [workers] Verificar no NuGet a versão estável atual do pacote `Anthropic` (oficial, beta) e adicionar em `Directory.Packages.props` sem wildcard — não usar de memória, conferir no momento da implementação (Decision 1). Confirmado 12.39.0 em jul/2026.
- [x] 2.2 [workers] Verificar no NuGet a versão estável atual do pacote `Google.GenAI` (oficial) e adicionar em `Directory.Packages.props` sem wildcard. Confirmado 1.15.0 em jul/2026.

## 3. `apps/api` — catálogo de provedores (`GET /providers`)

- [x] 3.1 [api] Criar `Providers/ProviderModelCatalog.cs`: catálogo estático `IReadOnlyDictionary<string, IReadOnlyList<string>>` (provider → modelos), levantando a lista atual de modelos suportados por OpenAI, Anthropic e Gemini no momento da implementação (Decision 3 — não copiar nomes de modelo de memória).
- [x] 3.2 [api] Criar `Providers/ProviderValidationOutcome.cs`: enum `{ Valid, ProviderNotConfigured, ModelUnavailable }` (Decision 4 — tipo de domínio, sem referência a `Microsoft.AspNetCore.Http.HttpResults`).
- [x] 3.3 [api] Criar `Providers/ProviderCatalogService.cs`: `GetAvailableProviders()` (usa `LlmProviders.All` + `IsConfigured` + `ProviderModelCatalog`, para `GET /providers`), `Validate(string provider, string model)` → `ProviderValidationOutcome` (para os handlers de Create/Update, Decision 4), e `IsProviderConfigured(string provider)` (para `EnqueueingAgentHandler`, Decision 5).
- [x] 3.4 [api] Criar `Providers/Responses/ProviderResponse.cs` (`Id`, `Models`).
- [x] 3.5 [api] Criar `Providers/Queries/ListProviders/ListProvidersQuery.cs` + `ListProvidersQueryHandler.cs` (usa `ProviderCatalogService.GetAvailableProviders`), seguindo o padrão Mediator já usado em `Agents/Queries/ListAgents`.
- [x] 3.6 [api] Criar `Providers/Endpoints/ProviderEndpoints.cs`: `GET /providers`, mapeado em `Program.cs` junto de `MapAgentEndpoints()`.
- [x] 3.7 [api] Registrar `ProviderCatalogService` no DI (`Program.cs`) — sem dependência de banco, seguro como singleton.
- [x] 3.8 [api] Teste de integração: provider com variável de ambiente configurada aparece em `GET /providers` junto com a lista de modelos desse provider.
- [x] 3.9 [api] Teste de integração: provider sem variável de ambiente configurada não aparece em `GET /providers` (nem ele, nem seus modelos).
- [x] 3.10 [api] Teste de integração: nenhum provider configurado no ambiente → `GET /providers` responde HTTP 200 com lista vazia, sem erro.

## 4. `apps/api` — Agent ganha Provider/Model

- [x] 4.1 [api] `Agents/Entities/Agent.cs`: adicionar `Provider`/`Model` (`string?`, nullable), ajustar construtor e `UpdateDetails` (ou método equivalente) para recebê-los.
- [x] 4.2 [api] Nova migration EF Core: `Provider`/`Model` nullable em `agents` (Decision 6 — sem passo de backfill). Nomes de coluna em PascalCase (`Provider`, `Model`), não `provider`/`model` como o texto original da Decision sugeria — segue a convenção real já usada em `Name`/`Instructions`/`IsActive` nesta tabela (ver migrations existentes).
- [x] 4.3 [api] `Agents/Requests/CreateAgentRequest.cs` e `UpdateAgentRequest.cs`: adicionar `Provider`/`Model`.
- [x] 4.4 [api] `Agents/Responses/AgentResponse.cs`: adicionar `Provider`/`Model` (nullable no shape de saída).
- [x] 4.5 [api] Criar `Agents/Commands/CreateAgent/CreateAgentResult.cs` (`AgentResponse? Agent`, `ProviderValidationOutcome Validation`) e alterar `CreateAgentCommand`/`CreateAgentCommandHandler`: comando aceita `Provider`/`Model`; handler injeta `ProviderCatalogService`, chama `Validate(Provider, Model)` antes de persistir — se `Valid`, persiste e devolve `CreateAgentResult` com `Agent` preenchido; caso contrário, devolve `CreateAgentResult` com `Agent` nulo e o `ProviderValidationOutcome` correspondente, sem persistir nada (Decision 4).
- [x] 4.6 [api] Criar `Agents/Commands/UpdateAgent/UpdateAgentResult.cs` (`AgentResponse? Agent`, `bool Found`, `ProviderValidationOutcome Validation`) e alterar `UpdateAgentCommand`/`UpdateAgentCommandHandler`: mesma checagem via `ProviderCatalogService.Validate`, distinguindo id inexistente (`Found = false`) de provider/model indisponível (`Found = true`, `Validation != Valid`) — só persiste quando `Found = true` e `Validation = Valid` (Decision 4).
- [x] 4.7 [api] `Agents/Endpoints/AgentEndpoints.cs`: `CreateAgentAsync`/`UpdateAgentAsync` continuam validando só shape (nome, instruções, `provider`, `model` presentes — nenhuma checagem de disponibilidade no endpoint, Decision 4). Após `mediator.Send`, mapear o resultado: `UpdateAgentResult.Found == false` → `NotFound`; `ProviderValidationOutcome.ProviderNotConfigured` → `ValidationProblem` com erro no campo `provider`; `ModelUnavailable` → `ValidationProblem` com erro no campo `model`; `Valid` → `Created`/`Ok` com o `AgentResponse`.
- [x] 4.8 [api] Teste de integração: criar agente com sucesso (provider/model válidos e disponíveis).
- [x] 4.9 [api] Teste de integração: criar agente sem `provider` ou sem `model` é rejeitado (400, shape).
- [x] 4.10 [api] Teste de integração: criar agente com `provider` não configurado no ambiente é rejeitado (400), independentemente do `model` informado.
- [x] 4.11 [api] Teste de integração: criar agente com `provider` configurado mas `model` fora do catálogo desse provider é rejeitado (400).
- [x] 4.12 [api] Teste de integração: atualizar agente com sucesso (provider/model válidos e disponíveis), incluindo agente legado (provider/model nulos) saindo do estado de reconfiguração.
- [x] 4.13 [api] Teste de integração: atualizar agente sem `provider` ou sem `model` é rejeitado (400, shape); atualizar agente inexistente retorna 404.
- [x] 4.14 [api] Teste de integração: atualizar agente com `provider` não configurado no ambiente é rejeitado (400), independentemente do `model` informado.
- [x] 4.15 [api] Teste de integração: atualizar agente com `provider` configurado mas `model` fora do catálogo desse provider é rejeitado (400).
- [x] 4.16 [api] Testes de integração: `GET /agents` e `GET /agents/{id}` retornam `provider`/`model` (incluindo caso nulo para agente legado).

## 5. `apps/api` — rejeição A2A por falta de provider utilizável

- [x] 5.1 [api] `A2A/EnqueueingAgentHandler.cs`: adicionar checagem, logo após a de `IsActive`, para `Provider`/`Model` nulos (estado de reconfiguração) → `RejectAsync`.
- [x] 5.2 [api] `A2A/EnqueueingAgentHandler.cs`: adicionar checagem via `ProviderCatalogService.IsProviderConfigured(Provider)` (mesmo serviço usado por Create/Update e `GET /providers`, Decision 5) → `RejectAsync` caso o provider não esteja mais configurado.
- [x] 5.3 [api] `A2A/AgentA2AServerRegistry.cs`: injetar `ProviderCatalogService` via DI (singleton, sem dependência de banco) e repassá-lo na construção manual de `EnqueueingAgentHandler` em `BuildServer`, mesma forma como `taskJobPublisher` já é repassado hoje.
- [x] 5.4 [api] Teste de integração: `SendMessage` para agente sem `provider`/`model` é rejeitado (`TASK_STATE_REJECTED`), sem publicar no RabbitMQ; `GetTask` subsequente reflete o estado rejeitado.
- [x] 5.5 [api] Teste de integração: `SendMessage` para agente com provider que deixou de estar configurado no ambiente é rejeitado, variando env entre a criação do agente e o `SendMessage` (mesmo padrão de teste já usado para agente inativo).

## 6. `apps/workers` — resolução de IChatClient por provedor

- [x] 6.1 [workers] Criar `Options/AnthropicOptions.cs` e `Options/GeminiOptions.cs` (`ApiKey`), seguindo o padrão de `ChatClientOptions`/`RabbitMqOptions`.
- [x] 6.2 [workers] Criar `Agents/IChatClientResolver.cs` (interface `Resolve(string provider, string model)`).
- [x] 6.3 [workers] Criar `Agents/ChatClientResolver.cs`: implementação com `switch` sobre `LlmProviders.OpenAi/Anthropic/Gemini`, construindo `OpenAIClient`/`AnthropicClient`/`Google.GenAI.Client` conforme o provedor; lança para provedor desconhecido ou configuração ausente (Decision 5/7 — sem engolir a exceção). API real de cada SDK verificada via decompilação dos pacotes restaurados (não assumida de memória): `AnthropicClient { ApiKey = ... }.AsIChatClient(model)`, `new Google.GenAI.Client(apiKey: ...).AsIChatClient(model)`.
- [x] 6.4 [workers] `Program.cs`: registrar `AnthropicOptions`/`GeminiOptions` e `IChatClientResolver`, removendo o registro do `IChatClient` singleton fixo.
- [x] 6.5 [workers] `Agents/AgentExecutionService.cs`: receber `IChatClientResolver` em vez de `IChatClient`; resolver o client dentro de `ExecuteAsync` a partir de `agent.Provider`/`agent.Model`, antes de montar o `ChatClientAgent`. Sem outra mudança no fluxo (`RunAsync`, streaming/não-streaming inalterados).
- [x] 6.6 [workers] Testes de unidade: `ChatClientResolver` constrói o tipo de client esperado para cada provider configurado, sem chamada de rede real; lança para provider não configurado e para provider desconhecido.
- [x] 6.7 [workers] Ajustar `WorkerTests.cs`/`TaskJobConsumerTests.cs` (que hoje mockam `IChatClient` direto) para mockar `IChatClientResolver` em vez de registrar `IChatClient` como singleton.
- [x] 6.8 [workers] Teste cobrindo o caso de config drift: provider ausente no ambiente do worker leva a task a `failed` via o `catch (Exception)` já existente em `AgentExecutionService.ExecuteAsync` (Decision 5, camada complementar). Implementado usando o `ChatClientResolver` real (não mockado), para provar o comportamento de ponta a ponta.

## 6b. `apps/workers` — projeção independente de Agent (não previsto explicitamente no tasks.md original, necessário para 6.5)

- [x] 6b.1 [workers] `Agents/Entities/Agent.cs`: adicionar `Provider`/`Model` (`string?`, nullable) à projeção independente do agente (mesmo padrão já usado para `Name`/`Instructions` — sem `ProjectReference` a `apps/api`).
- [x] 6b.2 [workers] `Infrastructure/AppDbContext.cs`: mapear as duas novas propriedades como opcionais.
- [x] 6b.3 [workers] Nova migration EF Core em `apps/workers` (schema/ferramental do EF Core, nunca executada em runtime pelo app — mas executada pela suíte de testes de `apps/workers`, que usa as migrations de `apps/workers` para provisionar seu próprio Postgres efêmero).

## 7. Documentação e verificação final

- [x] 7.1 [repo] Atualizar `README.md`: seção `apps/workers` (stack) mencionando suporte a múltiplos provedores; seção "Como subir cada app" com as novas variáveis de ambiente (`Anthropic__ApiKey`, `Gemini__ApiKey`); seção "Estrutura" incluindo `libs/ProviderCatalog`; seção "Como testar cada app" incluindo `dotnet test libs/ProviderCatalog.Tests`; seção "Convenções" atualizada com `libs/ProviderCatalog` como primeiro uso real de `libs/`.
- [x] 7.2 [repo] Atualizar `.env.example` com as novas variáveis por provedor (`Anthropic__ApiKey`, `Gemini__ApiKey`), incluindo nota sobre a necessidade de configurar a mesma chave em `apps/api` e `apps/workers`.
- [x] 7.3 [repo] Atualizar `appsettings.Development.json` de `apps/workers` com seções de exemplo para `Anthropic`/`Gemini` (valores placeholder, mesmo padrão de `ChatClient`). Também adicionado `ChatClient:ApiKey` em `appsettings.Development.json` de `apps/api` (necessário para `apps/api` conseguir checar "provider configurado" para OpenAI em dev/testes — não estava explícito no tasks.md original, mas decorre diretamente de `GET /providers` viver em `apps/api`).
- [x] 7.4 [repo] Rodar `dotnet test apps/api/Api.sln`, `dotnet test apps/workers/Workers.sln`, `dotnet test libs/ProviderCatalog.Tests` e `dotnet test tests/CrossAppTaskStoreCompatibility.Tests` — todos passando (6 + 35 + 11 + 2 = 54 testes, 0 falhas). `tests/CrossAppTaskStoreCompatibility.Tests` precisou de um ajuste de 1 linha (`SeedAgentAsync`) por causa da assinatura nova do construtor de `Agent`, não previsto explicitamente no tasks.md original.
