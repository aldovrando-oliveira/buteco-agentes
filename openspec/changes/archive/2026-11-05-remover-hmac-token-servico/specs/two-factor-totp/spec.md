## MODIFIED Requirements

### Requirement: As rotas de 2 etapas atendem só o usuário real do token
As rotas sob `/account/two-factor` e a rota `POST /users/{id}/two-factor/reset` SHALL identificar quem chama pelo `sub` do access token do emissor, e SHALL exigir que ele seja um usuário existente e ativo. Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário) e usuário desativado com access token ainda válido SHALL receber `403`, sem efeito nenhum.

#### Scenario: Operador sem usuário em Minha conta
- **WHEN** `GET /account/two-factor` chega com um access token do `buteco-painel` cujo `sub` não é id de usuário
- **THEN** a resposta é `403`

#### Scenario: Operador sem usuário na redefinição
- **WHEN** `POST /users/{id}/two-factor/reset` chega com um access token do `buteco-painel` cujo `sub` não é id de usuário, para um usuário com a verificação ligada
- **THEN** a resposta é `403`, e o usuário continua com a verificação, os códigos e o mesmo `security_stamp`

#### Scenario: Usuário desativado com access token ainda válido
- **WHEN** um usuário é desativado e, dentro da validade do access token dele, chama `GET /account/two-factor` ou `POST /users/{id}/two-factor/reset`
- **THEN** as duas respostas são `403`
