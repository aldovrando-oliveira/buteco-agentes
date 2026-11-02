## MODIFIED Requirements

### Requirement: Rotas classificadas como anônimas permanecem acessíveis sem token
`apps/api` e `apps/inbox` SHALL manter acessível, sem nenhum header
`Authorization`, toda rota explicitamente classificada como anônima: as sondas
de saúde, o webhook de canal, o `AgentCard`, as push notifications e os pontos de
entrada do emissor (`POST /auth/session`, `POST /auth/session/second-factor`,
`/connect/authorize` e `/connect/logout`), que são anônimos por definição, porque não podem exigir o que
ainda não emitiram. `POST /auth/login` SHALL NOT existir.

#### Scenario: Descoberta do AgentCard continua anônima
- **WHEN** um cliente envia `GET /agents/{id}/.well-known/agent-card.json`
  para um agente existente, sem o header `Authorization`
- **THEN** a API responde `200 OK` com o `AgentCard`, sem exigir token

#### Scenario: Webhook de canal continua anônimo
- **WHEN** um canal externo envia `POST /webhooks/{channelId}` para um
  canal existente, sem o header `Authorization`
- **THEN** `apps/inbox` processa a requisição normalmente, sem responder
  `401 Unauthorized`

#### Scenario: Sonda de saúde de apps/api continua anônima
- **WHEN** um cliente envia `GET /health` para `apps/api`, sem o header
  `Authorization`
- **THEN** a API responde `200 OK`, sem exigir token

#### Scenario: Sonda de saúde de apps/inbox continua anônima
- **WHEN** um cliente envia `GET /health` para `apps/inbox`, sem o header
  `Authorization`
- **THEN** a API responde `200 OK`, sem exigir token

#### Scenario: Abrir a sessão do emissor é acessível sem token
- **WHEN** um cliente envia `POST /auth/session` com credenciais válidas, da
  origem do painel e sem o header `Authorization`
- **THEN** a API responde `204 No Content`, sem rejeitar a requisição por
  ausência do header `Authorization`

#### Scenario: O segundo passo do login é acessível sem token
- **WHEN** um cliente envia `POST /auth/session/second-factor` com o cookie da
  sessão pendente, um código válido, da origem do painel e sem o header
  `Authorization`
- **THEN** a API responde `204 No Content`, sem rejeitar a requisição por
  ausência do header `Authorization`

#### Scenario: As rotas de 2 etapas da própria pessoa não são anônimas
- **WHEN** um cliente envia `GET /account/two-factor` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized`

#### Scenario: A rota de login antiga não é anônima, porque não existe
- **WHEN** um cliente envia `POST /auth/login`, sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized`, como a qualquer rota inexistente
  sem token, e não processa credencial nenhuma

#### Scenario: Push notification continua anônimo para o middleware novo
- **WHEN** uma requisição `POST /internal/push-notifications` é enviada
  sem o header `Authorization`, mas com um `X-A2A-Notification-Token`
  válido para uma `PendingDispatch` existente
- **THEN** `apps/inbox` processa a requisição normalmente (`200 OK`), sem
  rejeitá-la por ausência do header `Authorization`
