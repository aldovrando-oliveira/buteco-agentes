## MODIFIED Requirements

### Requirement: O principal do emissor é mapeado pelo cliente, nunca pelo sub
O subject usado pela autorização de um token do emissor SHALL vir de uma tabela fechada `client_id` → subject de cada app:

- no `apps/inbox`, a entrada `buteco-painel` → `operator`;
- no `apps/connectors`, as entradas `buteco-painel` → `operator` e `buteco-servico-api` → `service:api`.

O `sub` do token SHALL NOT ser usado como subject. Token do emissor válido cujo `client_id` esteja ausente ou fora da tabela SHALL receber `403 Forbidden` em toda rota autenticada. O subject mapeado SHALL seguir as mesmas regras de rota que o subject tem com o token HMAC.

#### Scenario: Cliente fora da tabela é recusado
- **WHEN** um token do emissor válido, com a `aud` do app e `client_id` `cliente-desconhecido`, chama uma rota autenticada
- **THEN** a resposta é `403 Forbidden`

#### Scenario: sub operator forjado não vira operador
- **WHEN** um token do emissor válido, com a `aud` do app, `sub` `operator` e `client_id` `cliente-desconhecido`, chama `GET /channels` no `apps/inbox`
- **THEN** a resposta é `403 Forbidden`

#### Scenario: Token sem client_id é recusado
- **WHEN** um token do emissor válido, com a `aud` do app e sem o claim `client_id`, chama uma rota autenticada
- **THEN** a resposta é `403 Forbidden`

#### Scenario: Subject mapeado segue a tabela de rotas do apps/connectors
- **WHEN** um token do emissor com `aud` `buteco-connectors` e `client_id` `buteco-painel` chama `GET /connectors/providers/google-drive/folder?id=x`
- **THEN** a resposta é `403 Forbidden`, como para o token HMAC `operator`

#### Scenario: Cliente de serviço do apps/api só na rota de descrever pasta
- **WHEN** um token do emissor com `aud` `buteco-connectors` e `client_id` `buteco-servico-api` chama `GET /connectors/providers/google-drive/folder?id=x` e `GET /connectors/providers`
- **THEN** a primeira não recebe `403` nem `401`, e a segunda recebe `403 Forbidden`

#### Scenario: Cliente de serviço do apps/api sem a audience do connectors
- **WHEN** um token do emissor com `aud` `buteco-api` e `client_id` `buteco-servico-api` chama `GET /connectors/providers/google-drive/folder?id=x`
- **THEN** a resposta é `401 Unauthorized`
