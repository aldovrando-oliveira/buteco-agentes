## ADDED Requirements

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

## REMOVED Requirements

### Requirement: Login do operador único
**Reason**: O painel entra pelo emissor (`panel-issuer-session`), com e-mail e senha pelo `POST /auth/session` (`issuer-user-login`). A rota fica sem consumidor (convenção 25), e o operador da variável de ambiente deixa de existir.
**Migration**: Entrar no painel com o e-mail e a senha de um usuário local. O primeiro Admin é o do bootstrap (`Auth:BootstrapAdminEmail` e `Auth:OperatorPasswordHash`). `AUTH_OPERATOR_USERNAME` pode sair do `.env.prod`.

### Requirement: Token de operador assinado com TTL curto e configurável
**Reason**: Sem o `POST /auth/login`, nenhum token de operador é emitido, e `Auth:OperatorTokenLifetime` fica sem leitor. A validação do HMAC continua, pelo requisito "Token HMAC de operador só é validado, e nenhum é emitido por rota".
**Migration**: Nenhuma para quem usa o painel. `Auth:OperatorTokenLifetime` pode sair da configuração; os tokens já emitidos valem até o próprio prazo (no máximo 30 minutos depois do deploy).
