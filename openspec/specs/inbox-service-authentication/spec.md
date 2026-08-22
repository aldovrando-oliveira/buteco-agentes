# inbox-service-authentication Specification

## Purpose

TBD - defined by change auth-login-e-servico. Update Purpose after archive.

## Requirements

### Requirement: apps/inbox autentica suas chamadas a apps/api com token de serviço
`apps/inbox` SHALL anexar um token de serviço assinado (`sub:
"service:inbox"`) a toda requisição que fizer contra `apps/api`. `apps/api`
SHALL aceitar esse token nas rotas que `apps/inbox` de fato consome
(`POST /agents/{id}/a2a`, `GET /agents/{id}`), autenticando a requisição
sem exigir nenhuma interação do operador.

#### Scenario: Round-trip SendMessage com token de serviço
- **WHEN** `apps/inbox` envia `SendMessage` via `POST /agents/{id}/a2a`
  para `apps/api`, autenticado com seu token de serviço
- **THEN** `apps/api` aceita a requisição e processa o `SendMessage`
  normalmente, sem responder `401 Unauthorized`

#### Scenario: Validação de referência de agente com token de serviço
- **WHEN** `apps/inbox` envia `GET /agents/{id}` para `apps/api`,
  autenticado com seu token de serviço, para validar um `AgentId`
  referenciado por um canal
- **THEN** `apps/api` aceita a requisição e responde de acordo com a
  existência do agente, sem responder `401 Unauthorized`

### Requirement: Token de serviço é restrito às rotas que apps/inbox consome
`apps/api` SHALL rejeitar, com `403 Forbidden`, uma requisição autenticada
com um token cujo `sub` seja `"service:inbox"` quando dirigida a qualquer
rota diferente de `POST /agents/{id}/a2a` e `GET /agents/{id}`, mesmo
quando o token for estruturalmente válido e não expirado.

#### Scenario: Token de serviço rejeitado fora do escopo permitido
- **WHEN** uma requisição `POST /agents` (criação de agente) é enviada a
  `apps/api` com `Authorization: Bearer <token de serviço válido>`
- **THEN** a API responde `403 Forbidden`, não processando a criação do
  agente
