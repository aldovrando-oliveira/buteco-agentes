## Why

A linha de caixas de entrada (inboxes) precisa de um app próprio, capaz de
receber webhooks de canais externos (ChatWoot, Waha, e futuros) via HTTP —
natureza diferente de `apps/workers` (Worker Service, sem listener HTTP).
Antes de qualquer lógica de canal, catálogo ou CRM, é preciso estabelecer a
fundação de código: uma quarta solução .NET isolada, seguindo exatamente o
mesmo padrão de scaffold já usado por `apps/api` e `apps/workers` na change
`estrutura-base-monorepo`. Sem essa base, nenhuma das changes futuras da
linha de inboxes (catálogo de canais, orquestrador, adapters) tem onde
pousar.

## What Changes

- Nova solução .NET `apps/inbox/Inbox.sln`, com solution folders `src/` e
  `tests/`, mesmo padrão de `apps/api/Api.sln`.
- Novo projeto `apps/inbox/src/Buteco.Inbox/`: ASP.NET Core Web API mínimo
  (`Microsoft.NET.Sdk.Web`), com endpoint de health check
  (`AddHealthChecks()` / `MapHealthChecks("/health")`) e `Program.cs`
  terminando em `public partial class Program;` para permitir
  `WebApplicationFactory<Program>` nos testes.
- Novo projeto `apps/inbox/tests/Buteco.Inbox.Tests/`: testes xunit cobrindo
  o health check via `WebApplicationFactory<Program>` puro, sem fixture
  customizada (sem persistência nesta fatia).
- Atualização de `Directory.Packages.props` (raiz) apenas se algum pacote
  necessário ainda não estiver pinado — os candidatos
  (`Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.NET.Test.Sdk`, `xunit`,
  `xunit.runner.visualstudio`, `coverlet.collector`) já existem, então não
  se espera mudança real aqui.
- Atualização do `README.md`: tabela "Stack", seção "Estrutura" (árvore de
  pastas), seção "Como subir cada app" (bloco `### apps/inbox`) e seção
  "Como testar cada app" (`dotnet test apps/inbox/Inbox.sln`).

## Capabilities

### New Capabilities
- `inbox-scaffold`: solução .NET isolada para `apps/inbox`, Web API mínima
  com health check, e testes automatizados cobrindo esse endpoint —
  espelhando a estrutura já formalizada em `api-scaffold`.

### Modified Capabilities

(nenhuma — `api-scaffold` e `workers-scaffold` não mudam; `apps/inbox` é
isolado deles)

## Impact

- Código novo apenas em `apps/inbox/` (nenhum arquivo fora dele muda, exceto
  `Directory.Packages.props` se necessário e `README.md`).
- Nenhuma mudança em `apps/api`, `apps/workers`, `apps/frontend` ou
  `docker-compose.yml`.
- Nenhum `ProjectReference` entre `apps/inbox` e qualquer outro app.
- Nenhuma persistência (Postgres/EF Core) nesta fatia.
