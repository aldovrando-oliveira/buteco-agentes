## MODIFIED Requirements

### Requirement: As rotas de 2 etapas atendem só o usuário real do token
As rotas sob `/account/two-factor` e a rota `POST /users/{id}/two-factor/reset` SHALL identificar quem chama pelo `sub` do access token do emissor, e SHALL exigir que ele seja um usuário existente e ativo. As rotas sob `/account/two-factor` SHALL atender todo papel. `POST /users/{id}/two-factor/reset` SHALL exigir também o papel `admin` gravado e o `role` `admin` no token (`route-roles`), e o papel do alvo SHALL NOT restringi-la. Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário), usuário desativado com access token ainda válido e, na redefinição, usuário cujo papel gravado não é `admin` SHALL receber `403`, sem efeito nenhum.

A verificação em 2 etapas SHALL continuar obrigatória para todo usuário, de qualquer papel (decisão do dono de 08/11/2026, #184 Q2).

#### Scenario: Operador sem usuário em Minha conta
- **WHEN** `GET /account/two-factor` chega com um access token do `buteco-painel` cujo `sub` não é id de usuário
- **THEN** a resposta é `403`

#### Scenario: Operador sem usuário na redefinição
- **WHEN** `POST /users/{id}/two-factor/reset` chega com um access token do `buteco-painel` cujo `sub` não é id de usuário, para um usuário com a verificação ligada
- **THEN** a resposta é `403`, e o usuário continua com a verificação, os códigos e o mesmo `security_stamp`

#### Scenario: Usuário desativado com access token ainda válido
- **WHEN** um usuário é desativado e, dentro da validade do access token dele, chama `GET /account/two-factor` ou `POST /users/{id}/two-factor/reset`
- **THEN** as duas respostas são `403`

#### Scenario: Leitor na própria verificação e na redefinição de outro
- **WHEN** um usuário ativo de papel `reader` chama `GET /account/two-factor` e `POST /users/{id}/two-factor/reset` para outro usuário com a verificação ligada
- **THEN** a primeira responde `200` e a segunda `403`, e o outro continua com a verificação

#### Scenario: Admin redefine a verificação de outro Admin
- **WHEN** um Admin redefine a verificação de outro Admin que a tem ligada
- **THEN** a resposta é `204`
