# operator-authentication Specification

## Purpose

TBD - defined by change auth-login-e-servico. Update Purpose after archive.
## Requirements
### Requirement: Token HMAC de operador só é validado, e nenhum é emitido por rota
`apps/api` SHALL NOT expor rota que emita token HMAC de operador. `POST /auth/login` SHALL responder como rota inexistente, inclusive para quem envia credencial válida do Admin do bootstrap: `401 Unauthorized` sem `Authorization`, como qualquer rota inexistente, o que não revela quais rotas existem; e `404 Not Found` com um access token válido de usuário do emissor.

As rotas autenticadas dos três apps SHALL continuar aceitando o token HMAC de operador assinado com `Auth:TokenSigningKey` dentro do prazo do próprio token, e SHALL recusar com `401` o que estiver expirado, até a #184 recusar o subject `operator` no HMAC e a #182 remover o HMAC. `Auth:OperatorUsername` e `Auth:OperatorTokenLifetime` SHALL NOT ser lidas.

#### Scenario: A rota de login não existe mais
- **WHEN** um cliente envia `POST /auth/login` com `{"username":"admin","password":"<a senha do bootstrap>"}` e o access token de um usuário obtido do emissor
- **THEN** a resposta é `404 Not Found`, e nenhum token é devolvido

#### Scenario: Sem token, a rota de login responde como qualquer rota inexistente
- **WHEN** um cliente envia `POST /auth/login` com `{"username":"admin","password":"<a senha do bootstrap>"}`, sem `Authorization`
- **THEN** a resposta é `401 Unauthorized`, igual à de `GET /rota-que-nao-existe` sem token, e nenhum token é devolvido

#### Scenario: Token HMAC de operador dentro do prazo continua aceito
- **WHEN** um token HMAC de operador assinado com `Auth:TokenSigningKey`, ainda no prazo, chama `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token HMAC de operador expirado é recusado
- **WHEN** um token HMAC de operador cujo instante de expiração já passou chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`

