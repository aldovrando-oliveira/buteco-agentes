## RENAMED Requirements

- FROM: `### Requirement: apps/api aceita o access token do emissor ao lado do HMAC`
- TO: `### Requirement: apps/api aceita só o access token do emissor`

## MODIFIED Requirements

### Requirement: Rotas de negócio exigem token válido por padrão
`apps/api` e `apps/inbox` SHALL exigir um token válido (`Authorization:
Bearer <token>`) em toda rota HTTP, exceto as explicitamente
classificadas como anônimas. Uma requisição sem o header `Authorization`
ou com um token malformado, com assinatura inválida, ou expirado SHALL
ser rejeitada com `401 Unauthorized`, sem executar nenhuma lógica de
negócio da rota. O `401` SHALL trazer o cabeçalho `WWW-Authenticate: Bearer`.

No `apps/api`, a requisição sem token a uma rota inexistente SHALL receber a
mesma resposta que a requisição sem token a uma rota que existe: `401`, corpo
vazio e `WWW-Authenticate: Bearer`, para não revelar quais rotas existem. Com
token válido, a rota inexistente SHALL responder `404`.

#### Scenario: apps/api rejeita requisição sem token
- **WHEN** um cliente envia `GET /agents` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized` com `WWW-Authenticate: Bearer`

#### Scenario: apps/api rejeita requisição com token inválido
- **WHEN** um cliente envia `GET /agents` com `Authorization: Bearer
  token-invalido`
- **THEN** a API responde `401 Unauthorized`

#### Scenario: Rota inexistente sem token responde como a que existe
- **WHEN** um cliente envia `GET /rota-que-nao-existe` e `GET /agents`, os dois sem
  o header `Authorization`
- **THEN** as duas respostas são `401 Unauthorized`, com corpo vazio e
  `WWW-Authenticate: Bearer`

#### Scenario: apps/inbox rejeita requisição sem token
- **WHEN** um cliente envia `GET /channels` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized` com `WWW-Authenticate: Bearer`

#### Scenario: apps/inbox rejeita requisição com token inválido
- **WHEN** um cliente envia `GET /channels` com `Authorization: Bearer
  token-invalido`
- **THEN** a API responde `401 Unauthorized`

### Requirement: apps/api aceita só o access token do emissor
O `apps/api` SHALL autenticar cada requisição com um esquema só, o do access token do próprio emissor, e SHALL aceitar o token que satisfaça todas as condições abaixo, recusando com `401` o que falhar qualquer uma, inclusive o que não seja um JWS compacto:

- assinatura RS256 verificável por uma chave do anel de chaves do próprio processo, sem chamada HTTP;
- `typ` `at+jwt`;
- `iss` igual ao issuer;
- `aud` contendo `buteco-api`;
- `exp` não vencido, com tolerância de no máximo 30 segundos.

O subject usado pela autorização SHALL vir de uma tabela fechada `client_id` → subject, com as entradas:

- `buteco-painel` → `operator`;
- `buteco-servico-inbox` → `service:inbox`;
- `buteco-servico-connectors` → `service:connectors`.

O `sub` do token SHALL NOT ser usado como subject. Token válido com `client_id` ausente ou fora da tabela SHALL receber `403`. O subject mapeado de um cliente de serviço SHALL seguir a lista de rotas do subject na tabela de subjects do `apps/api`. O boot SHALL falhar se um subject de destino da tabela não existir na tabela de subjects do `apps/api`.

#### Scenario: Token do painel aceito no apps/api
- **WHEN** um access token do fluxo do `buteco-painel`, com `aud` contendo `buteco-api`, chama `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token sem a audience do apps/api é recusado
- **WHEN** um access token do emissor com `aud` só `buteco-inbox` chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: sub operator forjado não vira operador
- **WHEN** um access token do emissor válido, com `aud` `buteco-api`, `sub` `operator` e `client_id` de um cliente fora da tabela, chama `GET /agents`
- **THEN** a resposta é `403 Forbidden`

#### Scenario: Token no formato de dois segmentos é recusado
- **WHEN** um token `payload.assinatura`, com um ponto só, assinado com a chave simétrica que os apps usavam antes, chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`, com `WWW-Authenticate: Bearer`

#### Scenario: Cliente apontando para subject inexistente derruba a checagem
- **WHEN** a checagem da tabela de clientes roda com uma entrada cujo subject não está na tabela de subjects do `apps/api`
- **THEN** ela falha nomeando o `client_id` e o subject

#### Scenario: Token do cliente de serviço do inbox só nas rotas do service:inbox
- **WHEN** um access token `client_credentials` do `buteco-servico-inbox` chama `GET /agents/{id}` de um agente existente, `GET /agents` e `GET /sync/knowledge-bases/`
- **THEN** as respostas são `200 OK`, `403 Forbidden` e `403 Forbidden`

#### Scenario: Token do cliente de serviço do connectors só nas rotas do service:connectors
- **WHEN** um access token `client_credentials` do `buteco-servico-connectors` chama `GET /sync/knowledge-bases/` e `GET /agents`
- **THEN** as respostas são `200 OK` e `403 Forbidden`

## REMOVED Requirements

### Requirement: Token de operador emitido por apps/api é aceito por apps/inbox sem chamada de rede entre os processos
**Reason**: A #182 remove o token assinado com a chave compartilhada. Nenhuma rota o emite desde a #217, e nenhum serviço o manda desde a #235.
**Migration**: O `apps/inbox` aceita o access token do emissor, validado localmente pelo JWKS obtido em segundo plano (`issuer-token-validation`). Nenhuma requisição espera o `apps/api` para ser autenticada.
