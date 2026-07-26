## Why

O monorepo ainda não existe em código: precisamos da estrutura base de `apps/api`,
`apps/workers` e `apps/frontend` para que o time possa começar a implementar
funcionalidades de negócio (A2A server, consumo do RabbitMQ, telas do produto)
sobre uma fundação já validada em termos de build, testes e isolamento entre apps.
Sem esse esqueleto, cada app seria iniciado de forma ad-hoc e divergente.

## What Changes

- Criar `apps/api`: solução .NET própria (`Api.sln`, `src/`, `tests/`) com um
  projeto ASP.NET Core Web API mínimo, expondo um endpoint de health check,
  estruturado para receber o `A2AServer` (Microsoft.Agents.AI.Hosting.A2A) depois.
- Criar `apps/workers`: solução .NET própria (`Workers.sln`, `src/`, `tests/`)
  com um projeto Worker Service mínimo, estruturado para depois consumir o
  RabbitMQ e executar agentes via Microsoft Agent Framework.
- Criar `apps/frontend`: projeto Vite + React + TypeScript, com Mantine
  (`core`, `hooks`, `form`, `notifications`) e um `AppShell` inicial no estilo
  de https://ui.mantine.dev, com ESLint + Prettier configurados.
- Garantir isolamento estrito: nenhum dos três apps referencia código interno
  dos outros (sem project references cruzadas no .NET, sem imports entre
  `apps/frontend` e os projetos .NET).
- Nenhum conteúdo é criado em `libs/` nesta mudança.

## Capabilities

### New Capabilities
- `api-scaffold`: estrutura base da solução .NET de `apps/api` (Web API mínima
  com health check), pronta para receber o A2A server.
- `workers-scaffold`: estrutura base da solução .NET de `apps/workers` (Worker
  Service mínimo), pronto para consumir RabbitMQ.
- `frontend-scaffold`: estrutura base de `apps/frontend` (Vite + React + TS +
  Mantine, AppShell inicial, ESLint + Prettier).

### Modified Capabilities
(nenhuma — não há specs existentes sendo alteradas)

## Impact

- Novo código apenas em `apps/api/`, `apps/workers/` e `apps/frontend/`.
- Nenhuma dependência ou referência cruzada entre os três apps.
- Sem lógica de negócio, sem RabbitMQ, PostgreSQL, A2A, MCP ou LLM implementados
  ainda — apenas o esqueleto de build/execução/testes de cada app.
- `libs/` permanece vazio nesta mudança.
