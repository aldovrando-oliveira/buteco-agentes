## MODIFIED Requirements

### Requirement: Subject desconhecido é recusado em apps/api
`apps/api` SHALL autorizar, entre os tokens válidos, apenas o subject `operator` (o token do painel), nas rotas em cuja entrada da tabela de papéis esteja o `role` dele (`route-roles`), cada subject de serviço conhecido (`service:inbox`, `service:connectors`) nas rotas da sua lista, e o subject `application` da aplicação externa nas rotas da lista dele (`route-roles`; vazia até a #187). O `operator` SHALL NOT ter acesso implícito a todas as rotas. Um token válido com qualquer outro subject SHALL receber `403 Forbidden` em toda rota que exige autenticação.

#### Scenario: Subject desconhecido recusado em rota do painel
- **WHEN** uma requisição `GET /knowledge-bases` chega com token validamente
  assinado e não expirado com `sub` `service:desconhecido`
- **THEN** a API responde `403 Forbidden`

#### Scenario: O painel não passa nas rotas de sincronização
- **WHEN** um usuário Admin, autenticado pelo login, chama `GET /knowledge-bases` e
  `GET /sync/knowledge-bases`
- **THEN** a API responde `200` na primeira e `403` na segunda

#### Scenario: Token de aplicação externa recusado em rota do painel
- **WHEN** uma requisição `GET /knowledge-bases` chega com o token de uma aplicação externa ativa
- **THEN** a API responde `403 Forbidden`
