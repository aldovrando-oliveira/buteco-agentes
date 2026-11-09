## MODIFIED Requirements

### Requirement: Subject desconhecido é recusado em apps/api
`apps/api` SHALL autorizar, entre os tokens válidos, apenas o subject `operator` (o token do painel), nas rotas em cuja entrada da tabela de papéis esteja o `role` dele (`route-roles`), e cada subject de serviço conhecido (`service:inbox`, `service:connectors`) nas rotas da sua lista. O `operator` SHALL NOT ter acesso implícito a todas as rotas. Um token válido com qualquer outro subject SHALL receber `403 Forbidden` em toda rota que exige autenticação.

#### Scenario: Subject desconhecido recusado em rota do painel
- **WHEN** uma requisição `GET /knowledge-bases` chega com token validamente
  assinado e não expirado com `sub` `service:desconhecido`
- **THEN** a API responde `403 Forbidden`

#### Scenario: O painel não passa nas rotas de sincronização
- **WHEN** um usuário Admin, autenticado pelo login, chama `GET /knowledge-bases` e
  `GET /sync/knowledge-bases`
- **THEN** a API responde `200` na primeira e `403` na segunda

### Requirement: Subject diferente de operator é recusado em apps/inbox
`apps/inbox` SHALL autorizar, entre os tokens válidos, apenas o subject
`operator` nas rotas que exigem autenticação, e só nas rotas em cuja entrada da tabela de papéis esteja o `role` do token (`route-roles`). Um token validamente assinado e não
expirado com qualquer outro subject, inclusive `service:inbox` e
`service:connectors`, SHALL receber `403 Forbidden` em toda rota que exige
autenticação, sem executar nenhuma lógica de negócio da rota. Token ausente,
malformado, com assinatura inválida, expirado ou do painel sem `role` SHALL continuar recebendo
`401 Unauthorized`.

#### Scenario: Operador continua com acesso às rotas do operador
- **WHEN** um usuário de papel `operator`, autenticado pelo login de `apps/api`, chama
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
