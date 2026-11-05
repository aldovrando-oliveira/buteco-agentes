# operator-authentication Specification

## Purpose

TBD - defined by change auth-login-e-servico. Update Purpose after archive.
## Requirements
### Requirement: Nenhuma rota emite token de operador fora do emissor
`apps/api` SHALL NOT expor rota que emita token de operador fora do emissor OIDC. `POST /auth/login` SHALL responder como rota inexistente, inclusive para quem envia credencial válida do Admin do bootstrap: `401 Unauthorized` sem `Authorization`, igual a qualquer rota inexistente, o que não revela quais rotas existem; e `404 Not Found` com um access token válido de usuário do emissor.

Nenhum dos três apps SHALL aceitar token que não seja o access token do emissor. `Auth:OperatorUsername` e `Auth:OperatorTokenLifetime` SHALL NOT ser lidas, e nenhum app SHALL ler chave simétrica de assinatura de token.

#### Scenario: A rota de login não existe mais
- **WHEN** um cliente envia `POST /auth/login` com `{"username":"admin","password":"<a senha do bootstrap>"}` e o access token de um usuário obtido do emissor
- **THEN** a resposta é `404 Not Found`, e nenhum token é devolvido

#### Scenario: Sem token, a rota de login responde como qualquer rota inexistente
- **WHEN** um cliente envia `POST /auth/login` com `{"username":"admin","password":"<a senha do bootstrap>"}`, sem `Authorization`
- **THEN** a resposta é `401 Unauthorized`, igual à de `GET /rota-que-nao-existe` sem token, e nenhum token é devolvido

#### Scenario: Token assinado com a chave antiga é recusado
- **WHEN** um token de operador no formato antigo (`payload.assinatura`), assinado com a chave simétrica que os apps usavam antes e ainda no prazo, chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`

