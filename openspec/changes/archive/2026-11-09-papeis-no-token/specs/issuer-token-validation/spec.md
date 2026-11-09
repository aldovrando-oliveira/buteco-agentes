## ADDED Requirements

### Requirement: O principal leva o papel do token do painel
Nos três apps, o principal do token do painel (`client_id` `buteco-painel`) SHALL levar o claim `role` do access token, sem renomear, ao lado do subject da tabela de clientes. O `role` SHALL NOT virar subject, e o `sub` continua fora do subject. O principal de um cliente de serviço SHALL NOT levar `role`, mesmo que o token o traga.

#### Scenario: Papel do painel no principal
- **WHEN** um token do `buteco-painel` com `role` `reader` é validado
- **THEN** o principal tem o subject `operator` e o claim `role` `reader`

#### Scenario: Cliente de serviço sem papel no principal
- **WHEN** um token do `buteco-servico-api` que traz `role` `admin` é validado no `apps/connectors`
- **THEN** o principal tem o subject `service:api` e nenhum claim `role`

## MODIFIED Requirements

### Requirement: O 401 de token recusado não traz corpo problem+json
Nos três apps que aceitam o access token do emissor (`apps/api`, `apps/inbox` e `apps/connectors`), o `401` de token ausente ou recusado (assinatura, emissor, audiência ou validade, ou token do painel sem `role` válido, `route-roles`) SHALL vir com o desafio `WWW-Authenticate: Bearer` e SHALL NOT trazer corpo com `Content-Type: application/problem+json`. O painel depende disso para separar o `401` de token do `401` de recusa da própria rota (`panel-issuer-session`), até a #247 trocar o critério por um campo de tipo. Cada app SHALL ter o teste `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis`, que nomeia essa dependência, e um caso no mesmo molde para o token do painel sem `role`.

#### Scenario: Sem token, no apps/api
- **WHEN** uma rota autenticada do `apps/api` recebe um pedido sem `Authorization`
- **THEN** a resposta é `401` com `WWW-Authenticate: Bearer`, sem `Content-Type: application/problem+json` e com o corpo vazio

#### Scenario: Token inválido, no apps/api
- **WHEN** uma rota autenticada do `apps/api` recebe um token que não valida
- **THEN** a resposta é `401` com `WWW-Authenticate: Bearer`, sem `Content-Type: application/problem+json` e com o corpo vazio

#### Scenario: Sem token e com token inválido, no apps/inbox
- **WHEN** uma rota autenticada do `apps/inbox` recebe um pedido sem token, ou com um token que não valida
- **THEN** a resposta é `401` com `WWW-Authenticate: Bearer`, sem `Content-Type: application/problem+json` e com o corpo vazio

#### Scenario: Sem token e com token inválido, no apps/connectors
- **WHEN** uma rota autenticada do `apps/connectors` recebe um pedido sem token, ou com um token que não valida
- **THEN** a resposta é `401` com `WWW-Authenticate: Bearer`, sem `Content-Type: application/problem+json` e com o corpo vazio

#### Scenario: Token do painel sem role, nos três apps
- **WHEN** uma rota autenticada de cada um dos três apps recebe um token do `buteco-painel` válido em tudo, mas sem o claim `role`
- **THEN** a resposta é `401` com `WWW-Authenticate: Bearer`, sem `Content-Type: application/problem+json` e com o corpo vazio
