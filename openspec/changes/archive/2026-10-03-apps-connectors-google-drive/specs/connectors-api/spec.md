## Purpose

As rotas HTTP do `apps/connectors` sob `/connectors`, o formato de fio delas, o
status de cada falha e quem pode chamar cada uma: uma tabela explícita de subjects em
que o operador não passa em tudo e qualquer subject fora dela recebe `403`, conferida
no boot nos dois sentidos.

## ADDED Requirements

### Requirement: Rotas sob o prefixo /connectors
`apps/connectors` SHALL servir as rotas abaixo, todas sob `/connectors`, com JSON em
`camelCase` e enum como string:

| método e rota | resposta de sucesso |
|---|---|
| `GET /connectors/providers` | `200`, lista de `{ "key", "accountEmail" }` ordenada por `key` |
| `GET /connectors/providers/{providerKey}/folders?parentId=` | `200`, lista de `{ "id", "name", "kind", "webUrl" }`, `kind` igual a `SharedDrive` ou `Folder`, ordenada por `name` (comparação ordinal) e desempate por `id` |
| `GET /connectors/providers/{providerKey}/folder?id=` | `200`, `{ "id", "name", "webUrl" }` |

`parentId` é opcional; `id` é obrigatório.

#### Scenario: Provedores configurados com e-mail
- **WHEN** o operador faz `GET /connectors/providers` com o Google configurado
- **THEN** a resposta é `200` com `[{"key":"google-drive","accountEmail":"<client_email>"}]`,
  e o texto do JSON contém as chaves `key` e `accountEmail`

#### Scenario: Descrição sem id
- **WHEN** `service:api` faz `GET /connectors/providers/google-drive/folder` sem `id`
- **THEN** a resposta é `400`

### Requirement: Erros com código e status por natureza
Falha de operação SHALL responder `ProblemDetails` com a extensão `code` (formato da
D1 da change `catalogo-base-sincronizada`) e `detail` igual ao detalhe do código
quando houver, com o status:

| status | códigos |
|---|---|
| `404` | `provider-not-configured` |
| `422` | `access-denied`, `not-a-folder`, `folder-trashed` |
| `502` | `api-not-configured`, `provider-auth-failed`, `provider-error` |
| `503` | `rate-limited`, `provider-unavailable` |

`403` SHALL ser usado apenas para recusa de autorização do subject.

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

### Requirement: Autorização por tabela explícita de subjects
Toda rota não anônima SHALL exigir que o subject do token esteja numa tabela
explícita e que a rota esteja na lista desse subject:

| subject | rotas |
|---|---|
| `operator` | `GET /connectors/providers`, `GET /connectors/providers/{providerKey}/folders` |
| `service:api` | `GET /connectors/providers/{providerKey}/folder` |

Subject fora da tabela, inclusive `service:inbox` e `service:connectors`, SHALL
receber `403` em toda rota, mesmo com token validamente assinado. Subject da tabela
SHALL receber `403` em rota fora da sua lista. `operator` SHALL NOT ter acesso
implícito a todas as rotas.

#### Scenario: Subject desconhecido
- **WHEN** um token validamente assinado com `sub` igual a `service:inbox`,
  `service:connectors` ou `qualquer` chama cada uma das três rotas
- **THEN** todas as respostas são `403`

#### Scenario: Operador em rota exclusiva do apps/api
- **WHEN** um token de `operator` chama `GET /connectors/providers/google-drive/folder?id=x`
- **THEN** a resposta é `403`

#### Scenario: apps/api em rota do operador
- **WHEN** um token de `service:api` chama `GET /connectors/providers` e
  `GET /connectors/providers/google-drive/folders`
- **THEN** as duas respostas são `403`

#### Scenario: Subjects nas próprias rotas
- **WHEN** `operator` chama as duas rotas dele e `service:api` chama a dele
- **THEN** nenhuma das respostas é `401` nem `403`

### Requirement: Tabela de subjects conferida no boot nos dois sentidos
No boot, depois de todos os `Map*`, o sistema SHALL conferir que toda entrada
(método, padrão de rota) da tabela corresponde a um endpoint mapeado, comparando
pelo `RoutePattern.RawText`, e que todo endpoint mapeado que exige autenticação
aparece na lista de pelo menos um subject. Qualquer divergência SHALL derrubar o
processo com mensagem que nomeia a entrada ou o endpoint.

#### Scenario: Entrada da tabela sem rota
- **WHEN** a tabela lista `GET /connectors/providers/{providerKey}/pastas`, que não é
  mapeada
- **THEN** a inicialização falha nomeando o subject, o método e o padrão

#### Scenario: Rota autenticada sem dono
- **WHEN** um endpoint autenticado é mapeado sem constar em nenhuma lista
- **THEN** a inicialização falha nomeando o padrão do endpoint

#### Scenario: Composição real
- **WHEN** o host de produção é construído
- **THEN** a checagem passa sem divergência
