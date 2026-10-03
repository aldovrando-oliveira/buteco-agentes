## ADDED Requirements

### Requirement: Subject diferente de operator é recusado em apps/inbox
`apps/inbox` SHALL autorizar, entre os tokens válidos, apenas o subject
`operator` nas rotas que exigem autenticação. Um token validamente assinado e não
expirado com qualquer outro subject, inclusive `service:inbox` e
`service:connectors`, SHALL receber `403 Forbidden` em toda rota que exige
autenticação, sem executar nenhuma lógica de negócio da rota. Token ausente,
malformado, com assinatura inválida ou expirado SHALL continuar recebendo
`401 Unauthorized`.

#### Scenario: Operador continua com acesso às rotas do operador
- **WHEN** o operador, autenticado pelo login de `apps/api`, chama
  `GET /channels` em `apps/inbox`
- **THEN** `apps/inbox` responde `200 OK`

#### Scenario: Token de serviço do apps/connectors recusado
- **WHEN** uma requisição `GET /channels` chega a `apps/inbox` com token
  validamente assinado e não expirado com `sub` `service:connectors`
- **THEN** `apps/inbox` responde `403 Forbidden`

#### Scenario: Token de serviço do próprio apps/inbox recusado
- **WHEN** uma requisição `GET /channels` chega a `apps/inbox` com token
  validamente assinado e não expirado com `sub` `service:inbox`
- **THEN** `apps/inbox` responde `403 Forbidden`

#### Scenario: Subject desconhecido recusado
- **WHEN** uma requisição `GET /channels` chega a `apps/inbox` com token
  validamente assinado e não expirado com `sub` `service:desconhecido`
- **THEN** `apps/inbox` responde `403 Forbidden`

#### Scenario: Recusa vale para toda rota autenticada mapeada
- **WHEN** cada rota autenticada mapeada no host de `apps/inbox` recebe uma
  requisição com token validamente assinado com `sub` `service:connectors`
- **THEN** todas respondem `403 Forbidden`

#### Scenario: Escrita recusada não altera o estado
- **WHEN** uma requisição `POST /channels` com corpo válido chega a `apps/inbox`
  com token validamente assinado com `sub` `service:connectors`
- **THEN** `apps/inbox` responde `403 Forbidden` e nenhum canal é criado

### Requirement: Rotas anônimas de apps/inbox não mudam com token de serviço presente
`apps/inbox` SHALL continuar processando as rotas classificadas como anônimas
(`/health`, `/webhooks/{channelId:guid}`, `/internal/push-notifications`) com a
mesma autenticação própria de antes, com ou sem token, e SHALL NOT responder
`403 Forbidden` nelas por causa do subject de um token presente na requisição.

#### Scenario: Webhook com token de serviço é processado pela rota
- **WHEN** uma requisição `POST /webhooks/{channelId}` para um canal inexistente
  chega com token validamente assinado com `sub` `service:connectors`
- **THEN** `apps/inbox` responde `404 Not Found`, decidido pela rota, e não `403`

#### Scenario: Sonda de saúde com token de serviço
- **WHEN** uma requisição `GET /health` chega com token validamente assinado com
  `sub` `service:inbox`
- **THEN** `apps/inbox` responde `200 OK`
