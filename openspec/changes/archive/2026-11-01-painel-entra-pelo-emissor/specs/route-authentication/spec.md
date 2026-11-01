## MODIFIED Requirements

### Requirement: Rotas classificadas como anônimas permanecem acessíveis sem token
`apps/api` e `apps/inbox` SHALL manter acessível, sem nenhum header
`Authorization`, toda rota explicitamente classificada como anônima: as sondas
de saúde, o webhook de canal, o `AgentCard`, as push notifications e os pontos de
entrada do emissor (`POST /auth/session`, `/connect/authorize` e
`/connect/logout`), que são anônimos por definição, porque não podem exigir o que
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

### Requirement: Token de operador emitido por apps/api é aceito por apps/inbox sem chamada de rede entre os processos
`apps/inbox` SHALL aceitar, em suas rotas autenticadas, um token HMAC de
operador assinado com a chave compartilhada (`Auth:TokenSigningKey`),
validando-o localmente sem realizar nenhuma chamada de rede a `apps/api` para
validar esse token. Isso SHALL valer também com a validação do token do emissor
ligada (`Identity:Issuer` configurado) e nenhuma chave do emissor obtida: o token
HMAC SHALL NOT depender das chaves do emissor. Nenhuma rota emite esse token
depois da #217; ele vale enquanto o prazo dele durar, e até a #184 e a #182.

#### Scenario: Token HMAC de operador é aceito por apps/inbox
- **WHEN** um token HMAC de operador assinado com a chave compartilhada, dentro do
  prazo, é usado numa rota autenticada de `apps/inbox` (ex. `GET /channels`),
  com `apps/inbox` sem conseguir alcançar `apps/api` pela rede nesse instante
- **THEN** `apps/inbox` responde de acordo com a rota (`200 OK`), sem
  `401 Unauthorized`

#### Scenario: Token HMAC aceito com o emissor ligado e sem chaves
- **WHEN** `apps/inbox` sobe com `Identity:Issuer` configurado e o `apps/api`
  inalcançável desde o boot, e o operador usa o token HMAC em `GET /channels`
- **THEN** `apps/inbox` responde `200 OK`, e nenhuma requisição foi feita ao
  discovery nem ao JWKS por causa dessa chamada

### Requirement: Endpoints do emissor OIDC são anônimos, classificados e conferidos no boot
Os endpoints do emissor SHALL ser anônimos e SHALL ser conferidos no boot por uma checagem própria:

- `/.well-known/openid-configuration` e `/.well-known/jwks`;
- `/connect/token`;
- `/connect/authorize` e `/connect/logout`.

Eles são atendidos pelo OpenIddict dentro do middleware de autenticação, e por isso a checagem de rotas anônimas existente não os enxerga. O authorize e o logout funcionam em modo passthrough: depois de o OpenIddict validar o pedido, uma rota mapeada do app decide a resposta.

O `apps/api` SHALL manter uma lista esperada desses endpoints, cada um com:

- **motivo:** `PublicDiscovery` para discovery e JWKS, `AuthEntryPoint` para o token e o authorize, `SessionEnd` para o logout;
- **marcação de passthrough.**

O boot SHALL comparar essa lista, nos dois sentidos, com as URIs de endpoint que a composição real configura no servidor OIDC, e SHALL falhar se houver:

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
  - `/auth/session`;
  - `/connect/authorize`;
  - `/connect/logout`;
  - `/agents/{id:guid}/.well-known/agent-card.json`.

  Toda outra rota mapeada continua exigindo token válido.

#### Scenario: Allowlist com rota que não existe derruba o boot
- **WHEN** a allowlist de rotas anônimas contém um caminho que nenhuma rota mapeada tem
- **THEN** o boot falha com uma mensagem que cita esse caminho

### Requirement: apps/api aceita o access token do emissor ao lado do HMAC
O `apps/api` SHALL autenticar cada requisição com `Authorization: Bearer` por exatamente um esquema, escolhido pela forma do token antes de qualquer validação:

- token com exatamente três segmentos separados por ponto vai ao esquema do emissor;
- qualquer outro, e a requisição sem token, vai ao esquema HMAC, que SHALL continuar aceitando e recusando exatamente como antes.

Um token recusado por um esquema SHALL NOT ser tentado no outro.

O esquema do emissor SHALL aceitar o access token que satisfaça todas as condições abaixo, e SHALL recusar com `401` o que falhar qualquer uma:

- assinatura RS256 verificável por uma chave do anel de chaves do próprio processo, sem chamada HTTP;
- `typ` `at+jwt`;
- `iss` igual ao issuer;
- `aud` contendo `buteco-api`;
- `exp` não vencido, com tolerância de no máximo 30 segundos.

O subject usado pela autorização SHALL vir de uma tabela fechada `client_id` → subject, com a entrada `buteco-painel` → `operator`. O `sub` do token SHALL NOT ser usado como subject. Token válido com `client_id` ausente ou fora da tabela SHALL receber `403`. O boot SHALL falhar se um subject de destino da tabela não existir na tabela de subjects do `apps/api`.

#### Scenario: Token do painel aceito no apps/api
- **WHEN** um access token do fluxo do `buteco-painel`, com `aud` contendo `buteco-api`, chama `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token sem a audience do apps/api é recusado
- **WHEN** um access token do emissor com `aud` só `buteco-inbox` chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: sub operator forjado não vira operador
- **WHEN** um access token do emissor válido, com `aud` `buteco-api`, `sub` `operator` e `client_id` de um cliente fora da tabela, chama `GET /agents`
- **THEN** a resposta é `403 Forbidden`

#### Scenario: JWT inválido não passa pelo HMAC
- **WHEN** um token de três segmentos com assinatura inválida chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`, e o validador HMAC não foi chamado

#### Scenario: Token HMAC continua igual
- **WHEN** um token HMAC de operador assinado com `Auth:TokenSigningKey`, dentro do prazo, chama `GET /agents`, e um token HMAC inválido chama a mesma rota
- **THEN** as respostas são `200 OK` e `401 Unauthorized`, como antes

#### Scenario: Cliente apontando para subject inexistente derruba a checagem
- **WHEN** a checagem da tabela de clientes roda com uma entrada cujo subject não está na tabela de subjects do `apps/api`
- **THEN** ela falha nomeando o `client_id` e o subject
