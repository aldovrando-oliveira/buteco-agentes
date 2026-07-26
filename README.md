# Buteco Agents

Plataforma de gestão de agentes de IA. Monorepo com três apps independentes —
`apps/api`, `apps/workers` e `apps/frontend` — que nunca referenciam código
interno umas das outras. Compartilhamento real de código só acontece via
`libs/` explícita, pequena e versionada, criada apenas quando houver
necessidade concreta (ver `openspec/changes/*/design.md` para o histórico de
decisões).

## Stack

| App | Stack |
| --- | --- |
| `apps/api` | .NET 10, ASP.NET Core Web API (futuro host do `A2AServer`, endpoints A2A e webhooks dos canais) |
| `apps/workers` | .NET 10, Worker Service (futuro consumidor do RabbitMQ, execução dos agentes via Microsoft Agent Framework) |
| `apps/frontend` | Vite + React 19 + TypeScript + Mantine 9 (ESLint + Prettier) |

Backend futuro (fora do escopo do scaffold atual): RabbitMQ como broker entre
`api` e `workers`; PostgreSQL + EF Core para o catálogo (agentes, servidores
MCP, bindings, inboxes) e para o store durável de tasks/eventos do protocolo
[A2A](https://a2a-protocol.org/latest/). Canais de entrada suportados hoje:
ChatWoot e Waha, via adapters.

## Estrutura

```
apps/
  api/                    # ASP.NET Core Web API
    Api.sln
    src/Buteco.Api/
    tests/Buteco.Api.Tests/
  workers/                # .NET Worker Service
    Workers.sln
    src/Buteco.Workers/
    tests/Buteco.Workers.Tests/
  frontend/               # Vite + React + TS + Mantine
    src/
    package.json
openspec/                 # Propostas, specs, design e tasks de cada mudança
```

## Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version` deve reportar `10.x`)
- [Node.js](https://nodejs.org/) 20+ e npm

## Como subir cada app

### apps/api

```bash
cd apps/api
dotnet run --project src/Buteco.Api
```

A API sobe em uma porta aleatória do Kestrel (veja a linha `Now listening on...`
no console). Endpoint de health check:

```bash
curl -i http://localhost:<porta>/health
```

Esperado: `HTTP/1.1 200 OK`.

### apps/workers

```bash
cd apps/workers
dotnet run --project src/Buteco.Workers
```

Esperado: log `Worker starting at: ...` no console. `Ctrl+C` para encerrar
(deve logar `Worker stopping at: ...` antes de sair).

### apps/frontend

```bash
cd apps/frontend
npm install   # primeira vez
npm run dev
```

Abra `http://localhost:5173`. Deve renderizar o `AppShell` (header + navbar +
área principal) sem erros no console do navegador.

## Como testar cada app

```bash
# apps/api
dotnet test apps/api/Api.sln

# apps/workers
dotnet test apps/workers/Workers.sln

# apps/frontend
cd apps/frontend
npm run lint
npm run format:check
npm run build
```

## Convenções

- **Isolamento entre apps**: nenhum `.csproj` ou arquivo do frontend pode
  referenciar código de outro app. `libs/` só existe quando houver
  necessidade real de compartilhamento, com justificativa explícita no
  `design.md` da mudança que a criar.
- **Mudanças planejadas com OpenSpec**: propostas, specs, design e tasks de
  cada mudança ficam em `openspec/changes/<nome-da-mudança>/`. Use
  `openspec status --change "<nome>"` para ver o progresso de uma mudança em
  andamento.
