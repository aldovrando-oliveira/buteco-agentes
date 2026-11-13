## ADDED Requirements

### Requirement: Rotas anônimas do login federado
As rotas do login federado que não podem exigir token SHALL ser anônimas, classificadas com o motivo `AuthEntryPoint`: `POST /auth/federated/{chave}/start`, `GET /auth/federated/callback`, `GET /auth/federated/providers` e `POST /auth/federated/refusal`. As que mudam estado (`start` e `refusal`) SHALL ter as barreiras do login (só JSON, `Origin` conferida). As rotas de conectar e desconectar (`/account/federated/*`, `/users/{id}/federated/*`) e `GET /identity/federated-providers` SHALL NOT ser anônimas.

#### Scenario: O início do login federado é acessível sem token
- **WHEN** um cliente envia `POST /auth/federated/corporativo/start`, JSON, da origem do painel e sem `Authorization`
- **THEN** a API responde `200`, sem rejeitar o pedido por ausência do header

#### Scenario: Conectar não é anônimo
- **WHEN** um cliente envia `POST /account/federated/corporativo/connect` sem `Authorization`
- **THEN** a API responde `401`

#### Scenario: As portas de entrada são as conhecidas
- **WHEN** o app sobe
- **THEN** as rotas mapeadas classificadas como `AuthEntryPoint` são exatamente as de "Rotas anônimas do app são as conhecidas" menos `/health`, `/connect/logout` e o `AgentCard`

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
- **THEN** a API responde `200 OK` com `{ "segundoPasso": "totp" }` ou
  `{ "segundoPasso": "cadastro" }` (a verificação em 2 etapas é obrigatória
  desde a #220, `issuer-user-login`), sem rejeitar a requisição por ausência do
  header `Authorization`

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

### Requirement: Endpoints do emissor OIDC são anônimos, classificados e conferidos no boot
Os endpoints do emissor SHALL ser anônimos e SHALL ser conferidos no boot por uma checagem própria:

- `/.well-known/openid-configuration` e `/.well-known/jwks`;
- `/connect/token`;
- `/connect/authorize` e `/connect/logout`;
- desde a #189, o endpoint de retorno do cliente OIDC dos provedores federados, `/auth/federated/callback`.

Eles são atendidos pelo OpenIddict (o servidor e, no retorno, o cliente) dentro do middleware de autenticação, e por isso a checagem de rotas anônimas existente não os enxerga. O authorize, o logout e o retorno do cliente funcionam em modo passthrough: depois de o OpenIddict validar o pedido, uma rota mapeada do app decide a resposta.

O `apps/api` SHALL manter uma lista esperada desses endpoints, cada um com:

- **motivo:** `PublicDiscovery` para discovery e JWKS, `AuthEntryPoint` para o token, o authorize e o retorno do cliente, `SessionEnd` para o logout;
- **marcação de passthrough.**

O boot SHALL comparar essa lista, nos dois sentidos, com as URIs de endpoint que a composição real configura no servidor OIDC e no cliente OIDC, e SHALL falhar se houver:

- endpoint configurado fora da lista;
- item da lista sem endpoint configurado;
- endpoint **sem** passthrough que coincide com uma rota mapeada do app;
- endpoint **com** passthrough sem rota mapeada de mesmo caminho, anônima e classificada com o mesmo motivo.

A checagem SHALL NOT consultar o banco.

#### Scenario: Endpoint configurado fora da lista derruba o boot
- **WHEN** a composição habilita um endpoint do servidor OIDC que não está na lista esperada (por exemplo, o de introspecção)
- **THEN** o boot falha com uma mensagem que cita a URI do endpoint

#### Scenario: Item da lista sem endpoint derruba o boot
- **WHEN** a lista esperada contém `/connect/token` e a composição não configura endpoint de token
- **THEN** o boot falha com uma mensagem que cita `/connect/token`

#### Scenario: Passthrough sem rota derruba o boot
- **WHEN** a composição configura `/connect/authorize` em passthrough e a rota `GET /connect/authorize` não está mapeada
- **THEN** o boot falha com uma mensagem que cita `/connect/authorize`

#### Scenario: Composição real passa na checagem
- **WHEN** o app sobe com a composição do `Program.cs`
- **THEN** o boot conclui, e remover a chamada da checagem do `Program.cs` reprova o teste que monta a composição real com um endpoint a mais

#### Scenario: Rotas anônimas do app são as conhecidas
- **WHEN** o app sobe
- **THEN** as rotas mapeadas classificadas como anônimas são exatamente:
  - `/health`;
  - `/auth/session`, `/auth/session/second-factor`, `/auth/session/enrollment/setup` e `/auth/session/enrollment/confirm`;
  - `/auth/invitation`, `/auth/invitation/accept`, `/auth/password-reset` e `/auth/password-reset/complete`;
  - `/auth/federated/{chave}/start`, `/auth/federated/callback`, `/auth/federated/providers` e `/auth/federated/refusal`;
  - `/connect/authorize`;
  - `/connect/logout`;
  - `/agents/{id:guid}/.well-known/agent-card.json`.

  Toda outra rota mapeada continua exigindo token válido.

#### Scenario: Allowlist com rota que não existe derruba o boot
- **WHEN** a allowlist de rotas anônimas contém um caminho que nenhuma rota mapeada tem
- **THEN** o boot falha com uma mensagem que cita esse caminho

#### Scenario: Retorno do cliente sem rota derruba o boot
- **WHEN** a composição configura `/auth/federated/callback` no cliente OIDC em passthrough e a rota `GET /auth/federated/callback` não está mapeada
- **THEN** o boot falha com uma mensagem que cita `/auth/federated/callback`
