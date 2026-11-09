## MODIFIED Requirements

### Requirement: GET /identity responde ao usuário ativo e recusa quem não é pessoa
O `apps/api` SHALL expor `GET /identity`. A rota SHALL responder `200` só a um usuário real, ativo e de papel `admin` gravado, identificado pela `CurrentUser` (o `sub` do access token do emissor é um usuário existente e ativo), com o `role` `admin` no token (`route-roles`). SHALL responder `401` sem token. SHALL responder `403` ao token de serviço (`service:inbox`, `service:connectors`), ao token do painel cujo `sub` não é usuário, ao usuário desativado com access token ainda no prazo e ao usuário de papel `operator` ou `reader`.

#### Scenario: Admin lê a identidade
- **WHEN** um usuário ativo de papel `admin`, com o access token do fluxo real do emissor, chama `GET /identity`
- **THEN** a resposta é `200` com os objetos `emissor` e `chavesDeAssinatura`

#### Scenario: Operador e Leitor são recusados
- **WHEN** um usuário ativo de papel `operator`, e outro de papel `reader`, chamam `GET /identity`
- **THEN** as duas respostas são `403`

#### Scenario: Sem token
- **WHEN** `GET /identity` chega sem `Authorization`
- **THEN** a resposta é `401`

#### Scenario: Token de serviço é recusado
- **WHEN** `GET /identity` chega com o token de `service:inbox` ou de `service:connectors`
- **THEN** a resposta é `403`

#### Scenario: Token do painel sem usuário é recusado
- **WHEN** `GET /identity` chega com um token do `buteco-painel` com `role` `admin` cujo `sub` não é id de usuário
- **THEN** a resposta é `403` com o título da `CurrentUser`

#### Scenario: Usuário desativado é recusado
- **WHEN** um usuário é desativado e chama `GET /identity` com o access token emitido antes, ainda no prazo
- **THEN** a resposta é `403`
