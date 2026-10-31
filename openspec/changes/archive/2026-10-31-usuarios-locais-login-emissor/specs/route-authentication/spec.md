## MODIFIED Requirements

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
  - `/auth/login`;
  - `/auth/session`;
  - `/connect/authorize`;
  - `/connect/logout`;
  - `/agents/{id:guid}/.well-known/agent-card.json`.

  Toda outra rota mapeada continua exigindo token válido.

## ADDED Requirements

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
- **WHEN** o operador faz login em `POST /auth/login` e chama `GET /agents` com o token HMAC, e um token HMAC inválido chama a mesma rota
- **THEN** as respostas são `200 OK` e `401 Unauthorized`, como antes

#### Scenario: Cliente apontando para subject inexistente derruba a checagem
- **WHEN** a checagem da tabela de clientes roda com uma entrada cujo subject não está na tabela de subjects do `apps/api`
- **THEN** ela falha nomeando o `client_id` e o subject
