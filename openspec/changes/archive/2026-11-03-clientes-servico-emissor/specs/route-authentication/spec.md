## MODIFIED Requirements

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

O subject usado pela autorização SHALL vir de uma tabela fechada `client_id` → subject, com as entradas:

- `buteco-painel` → `operator`;
- `buteco-servico-inbox` → `service:inbox`;
- `buteco-servico-connectors` → `service:connectors`.

O `sub` do token SHALL NOT ser usado como subject. Token válido com `client_id` ausente ou fora da tabela SHALL receber `403`. O subject mapeado de um cliente de serviço SHALL seguir a mesma lista de rotas que o subject tem com o token HMAC. O boot SHALL falhar se um subject de destino da tabela não existir na tabela de subjects do `apps/api`.

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

#### Scenario: Token do cliente de serviço do inbox só nas rotas do service:inbox
- **WHEN** um access token `client_credentials` do `buteco-servico-inbox` chama `GET /agents/{id}` de um agente existente, `GET /agents` e `GET /sync/knowledge-bases/`
- **THEN** as respostas são `200 OK`, `403 Forbidden` e `403 Forbidden`

#### Scenario: Token do cliente de serviço do connectors só nas rotas do service:connectors
- **WHEN** um access token `client_credentials` do `buteco-servico-connectors` chama `GET /sync/knowledge-bases/` e `GET /agents`
- **THEN** as respostas são `200 OK` e `403 Forbidden`
