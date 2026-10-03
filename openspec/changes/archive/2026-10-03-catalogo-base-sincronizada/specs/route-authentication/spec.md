## ADDED Requirements

### Requirement: Subject desconhecido é recusado em apps/api
`apps/api` SHALL autorizar, entre os tokens válidos, apenas o subject `operator`
em todas as rotas e cada subject de serviço conhecido (`service:inbox`,
`service:connectors`) nas rotas da sua lista. Um token válido com qualquer outro
subject SHALL receber `403 Forbidden` em toda rota que exige autenticação.

#### Scenario: Subject desconhecido recusado em rota do operador
- **WHEN** uma requisição `GET /knowledge-bases` chega com token validamente
  assinado e não expirado com `sub` `service:desconhecido`
- **THEN** a API responde `403 Forbidden`

#### Scenario: Operador continua com acesso total
- **WHEN** o operador, autenticado pelo login, chama `GET /knowledge-bases` e
  `GET /sync/knowledge-bases`
- **THEN** a API responde `200` nas duas

### Requirement: Lista de rotas dos subjects de serviço conferida no startup
`apps/api` SHALL verificar, no startup, que cada par de método e padrão de rota
das listas dos subjects de serviço corresponde a um endpoint mapeado, e SHALL
falhar o boot quando uma entrada não tiver rota correspondente.

#### Scenario: Entrada sem rota derruba a checagem
- **WHEN** a checagem roda com uma lista que contém um padrão inexistente entre os
  endpoints mapeados
- **THEN** ela falha nomeando o subject, o método e o padrão sem rota

#### Scenario: Composição real sobe
- **WHEN** `apps/api` sobe com as listas de produção
- **THEN** a checagem passa e a aplicação responde
