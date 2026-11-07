## ADDED Requirements

### Requirement: O 401 de token recusado não traz corpo problem+json
Nos três apps que aceitam o access token do emissor (`apps/api`, `apps/inbox` e `apps/connectors`), o `401` de token ausente ou recusado (assinatura, emissor, audiência ou validade) SHALL vir com o desafio `WWW-Authenticate: Bearer` e SHALL NOT trazer corpo com `Content-Type: application/problem+json`. O painel depende disso para separar o `401` de token do `401` de recusa da própria rota (`panel-issuer-session`), até a #247 trocar o critério por um campo de tipo. Cada app SHALL ter o teste `TokenRejection_401_HasNoProblemBody_ThePanelRenewsOnThis`, que nomeia essa dependência.

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
