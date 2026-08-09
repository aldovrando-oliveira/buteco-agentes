## 1. Solução .NET (apps/inbox)

- [x] 1.1 [apps/inbox] Criar `apps/inbox/Inbox.sln` com solution folders `src/` e `tests/`, mesmo padrão de `apps/api/Api.sln`
- [x] 1.2 [apps/inbox] Criar `apps/inbox/src/Buteco.Inbox/Buteco.Inbox.csproj` (SDK `Microsoft.NET.Sdk.Web`, sem `TargetFramework`/`Nullable`/`ImplicitUsings`/`LangVersion` explícitos — herdados de `Directory.Build.props`), e adicionar o projeto à solução
- [x] 1.3 [apps/inbox] Criar `apps/inbox/tests/Buteco.Inbox.Tests/Buteco.Inbox.Tests.csproj` (SDK `Microsoft.NET.Sdk`, `IsPackable=false`), referenciando `Buteco.Inbox.csproj` via `ProjectReference`, e adicionar o projeto à solução

## 2. Web API mínima e health check

- [x] 2.1 [apps/inbox] Criar `apps/inbox/src/Buteco.Inbox/Program.cs`: `WebApplication.CreateBuilder`, `builder.Services.AddHealthChecks()`, `app.MapHealthChecks("/health")`, `app.Run()`, terminando com `public partial class Program;`
- [x] 2.2 [apps/inbox] Criar `apps/inbox/src/Buteco.Inbox/appsettings.json` e `appsettings.Development.json` mínimos (só `Logging` + `AllowedHosts`)
- [x] 2.3 [apps/inbox] Criar `apps/inbox/src/Buteco.Inbox/Properties/launchSettings.json` com `applicationUrl` fixo (perfil `http`: `http://0.0.0.0:5027`; perfil `https`: `https://localhost:7172;http://localhost:5027`), mesmo formato de `apps/api/src/Buteco.Api/Properties/launchSettings.json`

## 3. Testes automatizados

- [x] 3.1 [apps/inbox] Criar `apps/inbox/tests/Buteco.Inbox.Tests/HealthCheckTests.cs`: teste xunit usando `WebApplicationFactory<Program>` puro (sem fixture customizada) validando que `GET /health` retorna HTTP 200
- [x] 3.2 [apps/inbox] Rodar `dotnet test apps/inbox/Inbox.sln` e confirmar que a suíte passa

## 4. Central Package Management

- [x] 4.1 [raiz] Conferir em `Directory.Packages.props` se `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` e `coverlet.collector` já cobrem as necessidades de `Buteco.Inbox.Tests`; adicionar `PackageVersion` apenas se algo estiver faltando
- [x] 4.2 [apps/inbox] Confirmar que nenhum `.csproj` novo declara `Version` em `PackageReference` (CPM cuida disso)

## 5. Validação de isolamento

- [x] 5.1 [apps/inbox] Rodar `dotnet build apps/inbox/Inbox.sln` e confirmar build isolado, sem depender de `apps/api`, `apps/workers` ou `apps/frontend`
- [x] 5.2 [apps/inbox] Inspecionar os `.csproj` de `apps/inbox/src` e `apps/inbox/tests` e confirmar que nenhum contém `ProjectReference` para fora de `apps/inbox`

## 6. Documentação

- [x] 6.1 [raiz] Atualizar tabela "Stack" do `README.md` com a linha de `apps/inbox`
- [x] 6.2 [raiz] Atualizar seção "Estrutura" do `README.md` incluindo a árvore de pastas de `apps/inbox`
- [x] 6.3 [raiz] Atualizar seção "Como subir cada app" do `README.md` com bloco `### apps/inbox` (`cd apps/inbox && dotnet run --project src/Buteco.Inbox`, `curl -i http://localhost:5027/health`)
- [x] 6.4 [raiz] Atualizar seção "Como testar cada app" do `README.md` incluindo `dotnet test apps/inbox/Inbox.sln`
