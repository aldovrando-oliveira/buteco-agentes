## ADDED Requirements

### Requirement: O emissor convive com a validação HMAC sem mudá-la
Com o emissor ligado, toda rota autenticada do `apps/api` SHALL continuar aceitando os tokens HMAC de operador e de serviço exatamente como antes. Ao lado deles, as rotas autenticadas do `apps/api` SHALL aceitar o access token do emissor que satisfaça a capability `route-authentication` (audience `buteco-api`, principal pela tabela de clientes). Nenhuma rota do `apps/api` SHALL emitir token HMAC de operador.

#### Scenario: Token HMAC do operador continua aceito com o emissor ligado
- **WHEN** o emissor está ligado e um token HMAC de operador assinado com `Auth:TokenSigningKey`, dentro do prazo, chama `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token do emissor do painel é credencial das rotas do app
- **WHEN** um access token emitido pelo fluxo do `buteco-painel`, com a audience `buteco-api`, é enviado como `Bearer` em `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token do emissor de cliente fora da tabela não é credencial
- **WHEN** um access token emitido por `client_credentials` para um cliente de teste fora da tabela de clientes, com a audience `buteco-api`, é enviado em `GET /agents`
- **THEN** a resposta é `403 Forbidden`

## REMOVED Requirements

### Requirement: O emissor convive com o login e a validação HMAC sem mudá-los
**Reason**: O `POST /auth/login` sai nesta change (`operator-authentication`), e o requisito exigia que ele continuasse emitindo o HMAC. A parte da validação continua, no requisito "O emissor convive com a validação HMAC sem mudá-la".
**Migration**: Nenhuma. O painel entra pelo emissor (`panel-issuer-session`).
