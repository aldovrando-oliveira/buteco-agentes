## MODIFIED Requirements

### Requirement: apps/inbox autentica suas chamadas a apps/api com token de serviço
`apps/inbox` SHALL anexar a toda requisição que fizer contra `apps/api` o access
token de serviço obtido no emissor do `apps/api` pelo cliente
`buteco-servico-inbox`, que a tabela de clientes do `apps/api` mapeia para
`service:inbox`. `apps/inbox` SHALL NOT assinar token de serviço próprio.
`apps/api` SHALL aceitar esse token nas rotas que `apps/inbox` de fato consome
(`POST /agents/{id}/a2a`, `GET /agents/{id}`), autenticando a requisição
sem exigir nenhuma interação do operador. Com o emissor indisponível ou o
segredo recusado, a requisição SHALL falhar como falha o `apps/api` fora do ar.

#### Scenario: Round-trip SendMessage com token de serviço
- **WHEN** `apps/inbox` envia `SendMessage` via `POST /agents/{id}/a2a`
  para `apps/api`, autenticado com seu token de serviço
- **THEN** `apps/api` aceita a requisição e processa o `SendMessage`
  normalmente, sem responder `401 Unauthorized`, e o token enviado tem
  `client_id` `buteco-servico-inbox` e `aud` `buteco-api`

#### Scenario: Validação de referência de agente com token de serviço
- **WHEN** `apps/inbox` envia `GET /agents/{id}` para `apps/api`,
  autenticado com seu token de serviço, para validar um `AgentId`
  referenciado por um canal
- **THEN** `apps/api` aceita a requisição e responde de acordo com a
  existência do agente, sem responder `401 Unauthorized`

#### Scenario: Segredo divergente no cadastro de canal
- **WHEN** o `apps/inbox` tem um `ServiceClient:Secret` diferente do
  cadastrado no `apps/api` e um canal é cadastrado
- **THEN** o emissor responde `invalid_client`, o cadastro responde `502`
  como na falha de comunicação com o `apps/api`, e nenhum canal é criado

#### Scenario: Segredo divergente no despacho
- **WHEN** o `apps/inbox` tem um `ServiceClient:Secret` diferente do
  cadastrado no `apps/api` e uma mensagem de canal é despachada
- **THEN** o despacho segue o caminho de falha de transporte, como com o
  `apps/api` fora do ar: cada tentativa falha como transporte e a mensagem
  volta a pendente, até o limite de tentativas de despacho, e não é
  encerrada como rejeição de protocolo A2A
