# connectors-api Specification

## Purpose

As rotas HTTP do `apps/connectors` sob `/connectors`, o formato de fio delas, o
status de cada falha e quem pode chamar cada uma: uma tabela explícita de subjects em
que o operador não passa em tudo e qualquer subject fora dela recebe `403`, conferida
no boot nos dois sentidos.
## Requirements
### Requirement: Rotas sob o prefixo /connectors
`apps/connectors` SHALL servir as rotas abaixo, todas sob `/connectors`, com JSON em
`camelCase` e enum como string:

| método e rota | resposta de sucesso |
|---|---|
| `GET /connectors/providers` | `200`, lista de `{ "key", "accountEmail" }` ordenada por `key` |
| `GET /connectors/providers/{providerKey}/folders?parentId=` | `200`, lista de `{ "id", "name", "kind", "webUrl" }`, `kind` igual a `SharedDrive` ou `Folder`, ordenada por `name` (comparação ordinal) e desempate por `id` |
| `GET /connectors/providers/{providerKey}/folder?id=` | `200`, `{ "id", "name", "webUrl" }` |
| `POST /connectors/knowledge-bases/{knowledgeBaseId}/sync` | `202`, sem corpo |

`parentId` é opcional; `id` é obrigatório.

#### Scenario: Provedores configurados com e-mail
- **WHEN** o operador faz `GET /connectors/providers` com o Google configurado
- **THEN** a resposta é `200` com `[{"key":"google-drive","accountEmail":"<client_email>"}]`,
  e o texto do JSON contém as chaves `key` e `accountEmail`

#### Scenario: Descrição sem id
- **WHEN** `service:api` faz `GET /connectors/providers/google-drive/folder` sem `id`
- **THEN** a resposta é `400`

#### Scenario: Sincronização pedida
- **WHEN** o operador faz `POST /connectors/knowledge-bases/{id}/sync` para uma base
  `Synced`
- **THEN** a resposta é `202`

### Requirement: Erros com código e status por natureza
Falha de operação SHALL responder `ProblemDetails` com a extensão `code` (formato da
D1 da change `catalogo-base-sincronizada`) e `detail` igual ao detalhe do código
quando houver, com o status:

| status | códigos |
|---|---|
| `404` | `provider-not-configured`, `knowledge-base-not-found` |
| `422` | `access-denied`, `not-a-folder`, `folder-trashed` |
| `502` | `api-not-configured`, `provider-auth-failed`, `provider-error`, `sync-api-error` |
| `503` | `rate-limited`, `provider-unavailable`, `sync-not-configured`, `sync-api-unavailable` |

`403` SHALL ser usado apenas para recusa de autorização do subject. Um `401` ou `403`
recebido do `apps/api` NÃO SHALL ser repassado com o mesmo status.

#### Scenario: Pasta sem acesso na navegação
- **WHEN** o operador navega dentro de uma pasta que a conta não lê
- **THEN** a resposta é `422` com `code` igual a `access-denied` e `detail` igual ao
  e-mail da conta, e **não** é `200` com lista vazia

#### Scenario: Drive API desligada
- **WHEN** o Google responde `403 accessNotConfigured`
- **THEN** a resposta é `502` com `code` igual a `api-not-configured`, e não `403`

#### Scenario: Provedor não configurado
- **WHEN** `service:api` pede a descrição de pasta com `providerKey` igual a `onedrive`
- **THEN** a resposta é `404` com `code` igual a `provider-not-configured`

#### Scenario: Sincronização sem o apps/api configurado
- **WHEN** o operador pede a sincronização num processo sem `Api:BaseUrl`
- **THEN** a resposta é `503` com `code` igual a `sync-not-configured`

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

