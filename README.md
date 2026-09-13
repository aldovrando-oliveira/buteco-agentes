# Buteco Agents

A self-hosted platform for building, configuring and operating AI agents that
talk to real people on real messaging channels.

You register an agent, give it instructions and a model, connect it to
external tools over [MCP](https://modelcontextprotocol.io/), let it delegate
to other agents, and attach it to a WhatsApp or Telegram inbox. Every agent is
also a first-class [A2A](https://a2a-protocol.org/latest/) endpoint, so other
systems can discover and invoke it over an open protocol instead of a
proprietary API.

> **Status: pre-release.** No official version has been cut yet. The platform
> is functional end to end — see the [changelog](CHANGELOG.md) for what is
> already built and [open items](#project-status) for what is not.

> **Language note.** This README, the license and the security policy are in
> English. The technical documentation in [`docs/`](docs/README.md) and the
> [contribution guide](CONTRIBUTING.md) are in Portuguese, the working
> language of the project.

---

## Why it exists

Most agent platforms make you choose between a hosted product you cannot
inspect and a framework you have to assemble yourself. This one is a running
system you own: four small services, a Postgres database, a message broker,
and a web panel — deployable on a single VM with `docker compose`.

The design commitment that shapes everything else: **an agent is reachable
over an open protocol, not just through this platform's own UI.** Every agent
publishes an A2A agent card, accepts `SendMessage`/`GetTask`, and can push
results back over webhooks.

---

## How it works

```
   messaging channel        apps/inbox            apps/api          apps/workers
  (WhatsApp / Telegram)
         │                      │                     │                   │
         │  webhook             │                     │                   │
         ├─────────────────────▶│                     │                   │
         │                      │ debounce per        │                   │
         │                      │ session             │                   │
         │                      │  SendMessage (A2A)  │                   │
         │                      ├────────────────────▶│                   │
         │                      │                     │  job (RabbitMQ)   │
         │                      │                     ├──────────────────▶│
         │                      │                     │                   │ calls the LLM
         │                      │                     │                   │ resolves MCP tools
         │                      │                     │                   │ delegates
         │                      │  push notification  │                   │
         │                      │◀────────────────────────────────────────┤
         │  reply               │                     │                   │
         │◀─────────────────────┤                     │                   │
```

| App | Role | Stack |
|---|---|---|
| **`apps/api`** | Agent catalog, MCP catalog, delegation, knowledge bases, A2A endpoint and agent card, operator login. Never calls the LLM — it persists the task and publishes a job | .NET 10, ASP.NET Core Minimal API, EF Core, RabbitMQ publisher |
| **`apps/workers`** | Executes tasks: calls the LLM, resolves MCP tools, runs delegation, fires push notifications | .NET 10 Worker Service, `Microsoft.Agents.AI` |
| **`apps/inbox`** | Inbound channel catalog, contact/session CRM, message history, debounce orchestrator, channel adapters | .NET 10 Minimal API, own isolated database |
| **`apps/frontend`** | Operations panel for agents, MCP, delegation, channels, sessions and conversation history | React 19, TypeScript, Vite, Mantine |

The four apps never reference each other's code. Anything crossing an app
boundary goes over authenticated HTTP. See
[architecture](docs/architecture.md) for the reasoning.

---

## Features

- **Agent catalog** — instructions, description, skills, per-agent LLM
  provider and model, activate/deactivate.
- **Multiple LLM providers** — OpenAI, Anthropic (Claude) and Google (Gemini);
  a provider appears only when its credential is configured.
- **A2A protocol** — `SendMessage`, `GetTask`, per-agent agent card at
  `/.well-known/agent-card.json`, and push notifications over webhooks.
- **MCP tools** — server catalog with encrypted credentials, per-binding tool
  selection, and real tool calls during execution.
- **Agent-to-agent delegation** — one-directional links with depth control.
- **Messaging channels** — WhatsApp (via [WAHA](https://waha.devlike.pro/))
  and Telegram, behind a plugin contract that new adapters implement.
- **Conversation memory** — sessions persisted by context, with incremental
  summarization as conversations grow.
- **Knowledge bases** — base and document catalog with markdown extraction,
  chunked and embedded into a vector index in the background. Operators manage
  documents from the panel: upload or write them, update, delete, watch each
  one move through indexing, and retry a document whose indexing failed. The
  base catalog shows, per base, how many documents it holds and how many are
  indexed, in progress or failed, and can filter down to the bases that have a
  failure. At execution time each linked, active base becomes a search tool the
  agent can call; results carry the distance, and the agent decides what is
  relevant.
- **Operations panel** — a web UI for all of the above.

---

## Quickstart

Requires .NET SDK 10, Node.js 20+, and Docker or Podman.

```bash
# 1. infrastructure (Postgres, RabbitMQ, WAHA)
cp .env.example .env
docker compose up -d

# 2. database schemas
(cd apps/api/src/Buteco.Api     && dotnet ef database update)
(cd apps/inbox/src/Buteco.Inbox && dotnet ef database update)

# 3. run the services, each in its own terminal
dotnet run --project apps/api/src/Buteco.Api          # http://localhost:5017
dotnet run --project apps/workers/src/Buteco.Workers
dotnet run --project apps/inbox/src/Buteco.Inbox      # http://localhost:5027

# 4. the panel
cd apps/frontend && npm install && npm run dev        # http://localhost:5173
```

Log in with the development operator: `operator` / `changeme`.

`apps/workers` requires the `TZ` environment variable set to a canonical IANA
name — it refuses to start otherwise. Using Podman? Read the
[Testcontainers notes](docs/development.md#testcontainers-com-podman) before
running the test suites; the most common failure looks like broken code and is
not.

Full setup, per-app details and troubleshooting:
[`docs/development.md`](docs/development.md).

---

## Documentation

| Document | For |
|---|---|
| [Development](docs/development.md) | Running everything locally, testing, building images |
| [Configuration](docs/configuration.md) | Every environment variable, per process |
| [Architecture](docs/architecture.md) | The four apps, domain model, business rules |
| [Conventions](docs/conventions.md) | House style and the assumptions behind it |
| [A2A integration](docs/a2a-integration.md) | Calling an agent from an external client |
| [Deployment](docs/deployment.md) | Running the stack on a server |
| [Contributing](CONTRIBUTING.md) | The spec-driven workflow, commits, pull requests |
| [Changelog](CHANGELOG.md) | What has been built so far |

Index: [`docs/README.md`](docs/README.md).

Two files in the repository root — `01-ARQUITETURA_E_CONVENCOES.md` and
`02-HISTORICO_E_STATUS.md` — are the maintainer's working notes, in
Portuguese: decision memory, per-change history and open items. They are the
source `docs/` is written from, not project documentation themselves.

---

## Project status

Everything listed under [Features](#features) is implemented and running.
Known gaps, tracked deliberately:

- **No continuous integration.** There is no `.github/workflows/`; tests and
  the documentation integrity check run locally. See
  [CONTRIBUTING.md](CONTRIBUTING.md).
- **WAHA webhooks are not authenticated.** An accepted, explicitly classified
  risk — Telegram webhooks are verified. See [SECURITY.md](SECURITY.md).
- **Single operator, no RBAC.** Authentication is one operator configured
  through environment variables, with no user table.

---

## Development workflow

Changes here are **spec-driven**. Every behavioral change gets a proposal, a
design document and specifications before implementation, all versioned
alongside the code under `openspec/`:

```
explore → propose → review → apply → sync → archive
```

If you are opening a pull request, start with
[CONTRIBUTING.md](CONTRIBUTING.md) and
[`docs/conventions.md`](docs/conventions.md) — a contribution that ignores the
house conventions will need to be reworked even if it functions.

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md)
first, and note that participation is covered by the
[Code of Conduct](CODE_OF_CONDUCT.md).

Found a security vulnerability? **Do not open a public issue** — follow
[SECURITY.md](SECURITY.md).

---

## License

Licensed under the Apache License, Version 2.0. See [LICENSE](LICENSE) and
[NOTICE](NOTICE).

Copyright 2026 Aldovrando Oliveira.
