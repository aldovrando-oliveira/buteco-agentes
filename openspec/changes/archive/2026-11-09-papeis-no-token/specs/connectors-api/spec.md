## MODIFIED Requirements

### Requirement: Autorização por tabela explícita de subjects
Toda rota não anônima SHALL exigir que o subject do token esteja numa tabela
explícita e que a rota esteja na lista desse subject. Para o subject `operator` (o token do painel), a lista SHALL ser a da tabela de papéis (`route-roles`), e o token SHALL passar só se o `role` dele estiver na entrada da rota:

| subject | rotas |
|---|---|
| `operator` | `GET /connectors/providers`, `GET /connectors/providers/{providerKey}/folders`, `POST /connectors/knowledge-bases/{knowledgeBaseId}/sync`, todas para os papéis `admin` e `operator`; o papel `reader` SHALL receber `403` nas três |
| `service:api` | `GET /connectors/providers/{providerKey}/folder` |

Subject fora da tabela, inclusive `service:inbox` e `service:connectors`, SHALL
receber `403` em toda rota, mesmo com token validamente assinado. Subject da tabela
SHALL receber `403` em rota fora da sua lista. `operator` SHALL NOT ter acesso
implícito a todas as rotas.

#### Scenario: Subject desconhecido
- **WHEN** um token validamente assinado com `sub` igual a `service:inbox`,
  `service:connectors` ou `qualquer` chama cada uma das quatro rotas
- **THEN** todas as respostas são `403`

#### Scenario: Operador em rota exclusiva do apps/api
- **WHEN** um token do painel com `role` `admin` chama `GET /connectors/providers/google-drive/folder?id=x`
- **THEN** a resposta é `403`

#### Scenario: apps/api em rota do operador
- **WHEN** um token de `service:api` chama `GET /connectors/providers`,
  `GET /connectors/providers/google-drive/folders` e
  `POST /connectors/knowledge-bases/{id}/sync`
- **THEN** as três respostas são `403`

#### Scenario: Subjects nas próprias rotas
- **WHEN** um token do painel com `role` `admin`, e outro com `role` `operator`, chamam as três rotas do painel, e `service:api` chama a dele
- **THEN** nenhuma das respostas é `401` nem `403`

#### Scenario: Leitor nas rotas do painel
- **WHEN** um token do painel com `role` `reader` chama as três rotas do painel
- **THEN** as três respostas são `403`, e nenhum ciclo de sincronização é disparado

### Requirement: Tabela de subjects conferida no boot nos dois sentidos
No boot, depois de todos os `Map*`, o sistema SHALL conferir que toda entrada
(método, padrão de rota) da tabela de subjects e da tabela de papéis corresponde a um endpoint mapeado, comparando
pelo `RoutePattern.RawText`, que todo endpoint mapeado que exige autenticação
aparece na lista de pelo menos um subject ou na tabela de papéis, e que nenhuma entrada da tabela de papéis tem lista de papéis vazia. Qualquer divergência SHALL derrubar o
processo com mensagem que nomeia a entrada ou o endpoint.

#### Scenario: Entrada da tabela sem rota
- **WHEN** a tabela lista `GET /connectors/providers/{providerKey}/pastas`, que não é
  mapeada
- **THEN** a inicialização falha nomeando o subject, o método e o padrão

#### Scenario: Rota autenticada sem dono
- **WHEN** um endpoint autenticado é mapeado sem constar em nenhuma lista nem na tabela de papéis
- **THEN** a inicialização falha nomeando o padrão do endpoint

#### Scenario: Composição real
- **WHEN** o host de produção é construído
- **THEN** a checagem passa sem divergência
