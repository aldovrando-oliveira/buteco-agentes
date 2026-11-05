## RENAMED Requirements

- FROM: `### Requirement: O emissor convive com a validação HMAC sem mudá-la`
- TO: `### Requirement: O access token do emissor é a credencial das rotas do apps/api`

## MODIFIED Requirements

### Requirement: O access token do emissor é a credencial das rotas do apps/api
Com o emissor ligado, as rotas autenticadas do `apps/api` SHALL aceitar o access token do emissor que satisfaça a capability `route-authentication` (audience `buteco-api`, principal pela tabela de clientes), e nenhum outro token. Nenhuma rota do `apps/api` SHALL emitir token de operador fora do emissor.

#### Scenario: Token do emissor do painel é credencial das rotas do app
- **WHEN** um access token emitido pelo fluxo do `buteco-painel`, com a audience `buteco-api`, é enviado como `Bearer` em `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token do emissor de cliente fora da tabela não é credencial
- **WHEN** um access token emitido por `client_credentials` para um cliente de teste fora da tabela de clientes, com a audience `buteco-api`, é enviado em `GET /agents`
- **THEN** a resposta é `403 Forbidden`
