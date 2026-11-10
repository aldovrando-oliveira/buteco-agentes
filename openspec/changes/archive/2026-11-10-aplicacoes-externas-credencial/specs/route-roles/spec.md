## MODIFIED Requirements

### Requirement: Matriz de papéis por rota nos três apps
O `apps/api`, o `apps/inbox` e o `apps/connectors` SHALL autorizar o token do painel (cliente `buteco-painel`, subject `operator`) por uma tabela fechada de papéis por rota (`RoleRoutes`), casada por método e `RoutePattern.RawText`, com os papéis `admin`, `operator` e `reader` (Admin, Operador e Leitor). O token do painel SHALL passar só nas rotas em cuja entrada esteja o `role` dele, e SHALL receber `403` nas outras, sem executar nenhuma lógica de negócio da rota. O subject `operator` SHALL NOT dar acesso implícito a nenhuma rota.

A tabela SHALL ser esta (A = `admin`, O = `operator`, L = `reader`):

| app | rotas | papéis |
|---|---|---|
| `apps/api` | leituras de agentes, provedores, MCP (inclusive `GET /mcp-servers/{id}/tools`), bases, documentos, eventos de documento, diagnóstico do índice, resumo de indexação e `/insights/*` | A O L |
| `apps/api` | escritas de agentes (inclusive vínculos de MCP, delegação e bases), MCP (inclusive `POST /mcp-servers/test` e `POST /mcp-servers/{id}/test`), bases e documentos (inclusive reindexar) | A O |
| `apps/api` | `/users/*` (inclusive convite, novo link, cancelamento, redefinição de senha, redefinição de 2 etapas e `PUT /users/{id}/role`) e `GET /identity` | A |
| `apps/api` | `/account/*` | A O L |
| `apps/api` | `/applications/*` (listar, ver, criar, editar, desativar, reativar, gerar e revogar segredo) | A |
| `apps/inbox` | `GET /channels/`, `GET /channels/{id}`, `GET /sessions/summary`, `GET /messages/summary` | A O L |
| `apps/inbox` | escritas de `/channels/*`, `GET /channels/{channelId}/sessions`, `GET /sessions/{sessionId}/messages`, `/contacts/*` | A O |
| `apps/connectors` | `GET /connectors/providers`, `GET /connectors/providers/{providerKey}/folders`, `POST /connectors/knowledge-bases/{knowledgeBaseId}/sync` | A O |

Com token do painel válido e papel válido, a requisição a uma rota inexistente SHALL continuar respondendo `404`.

#### Scenario: Leitor lê e não escreve agentes
- **WHEN** um token do painel com `role` `reader` chama `GET /agents/` e `POST /agents/` com corpo válido
- **THEN** a primeira responde `200`, a segunda `403`, e nenhum agente é criado

#### Scenario: Operador fora de Usuários e Identidade
- **WHEN** um token do painel com `role` `operator`, de um usuário ativo, chama `GET /users` e `GET /identity`
- **THEN** as duas respostas são `403`

#### Scenario: Leitor fora do histórico de conversas
- **WHEN** um token do painel com `role` `reader` chama `GET /sessions/{sessionId}/messages` e `GET /channels/{channelId}/sessions` no `apps/inbox`
- **THEN** as duas respostas são `403`

#### Scenario: Leitor vê os resumos do Insights
- **WHEN** um token do painel com `role` `reader` chama `GET /sessions/summary` e `GET /messages/summary` no `apps/inbox`, e `GET /insights/system` no `apps/api`
- **THEN** nenhuma das respostas é `401` nem `403`

#### Scenario: Todo papel em Minha conta
- **WHEN** um usuário ativo de cada papel chama `GET /account` com o próprio token
- **THEN** as três respostas são `200`

#### Scenario: Rota inexistente com papel válido
- **WHEN** um token do painel com `role` `reader` chama `GET /rota-que-nao-existe` no `apps/api`
- **THEN** a resposta é `404`

#### Scenario: Só o Admin em Aplicações externas
- **WHEN** um token do painel de cada papel chama `GET /applications/`
- **THEN** o `admin` não recebe `401` nem `403`, e o `operator` e o `reader` recebem `403`

### Requirement: Toda rota autenticada declara papel ou subject de serviço, conferido no boot
No boot, depois de todos os `Map*`, cada app SHALL conferir, sobre os endpoints do host construído:

- que todo `RouteEndpoint` que exige autenticação está na `RoleRoutes` ou na lista de rotas de algum subject de serviço;
- que toda entrada da `RoleRoutes` e das listas de subjects de serviço corresponde a um endpoint mapeado, com o método aceito;
- que nenhuma entrada da `RoleRoutes` tem lista de papéis vazia;
- que nenhum subject sem papel (`ServiceRoutes`) tem lista de rotas vazia, com uma única exceção nomeada, `application`, que fica vazia até a #187 abrir a rota A2A com o filtro por agente e retirar a exceção;
- que o subject `application` existe na `ServiceRoutes`.

Qualquer divergência SHALL derrubar o boot com uma mensagem que nomeia o método e o padrão. Rota anônima classificada SHALL ficar fora dos dois sentidos.

#### Scenario: Rota autenticada sem declaração
- **WHEN** um endpoint autenticado é mapeado sem constar na `RoleRoutes` nem em nenhuma lista de subject de serviço
- **THEN** a inicialização falha nomeando o método e o padrão do endpoint

#### Scenario: Entrada sem rota
- **WHEN** a `RoleRoutes` lista um padrão que não é mapeado
- **THEN** a inicialização falha nomeando o método e o padrão da entrada

#### Scenario: Lista de papéis vazia
- **WHEN** uma entrada da `RoleRoutes` não tem nenhum papel
- **THEN** a inicialização falha nomeando a entrada

#### Scenario: Composição real dos três apps
- **WHEN** o host de produção de cada app é construído
- **THEN** a checagem passa, e a `RoleRoutes` conferida não é vazia

#### Scenario: Lista vazia de subject de serviço
- **WHEN** a checagem roda com uma `ServiceRoutes` em que `service:connectors` tem lista vazia
- **THEN** a inicialização falha nomeando `service:connectors`

#### Scenario: A lista vazia de application passa, por exceção
- **WHEN** o host de produção do `apps/api` é construído, com a lista de `application` vazia
- **THEN** a checagem passa

#### Scenario: Sem a entrada application
- **WHEN** a checagem roda com uma `ServiceRoutes` sem a chave `application`
- **THEN** a inicialização falha nomeando `application`

### Requirement: Prazos da mudança de papel
O rebaixamento SHALL valer:

- **na requisição seguinte** nas rotas do `apps/api` que identificam a pessoa pela `CurrentUser` (`/users/*`, convite, novo link, cancelamento, redefinição de senha e de 2 etapas, `PUT /users/{id}/role`, `GET /identity` e `/applications/*`), que exigem o papel `admin` **gravado**, além do papel do token;
- **em até 15 min 30 s** (vida do access token mais a tolerância de 30 s) nas outras rotas do `apps/api` e no `apps/inbox` e no `apps/connectors`, que leem só o papel do token.

A promoção SHALL chegar na renovação seguinte, quando o token novo traz o papel gravado. Mudar o papel SHALL NOT trocar o carimbo de segurança nem revogar sessões.

#### Scenario: Admin rebaixado perde Usuários na requisição seguinte
- **WHEN** um Admin é rebaixado a Operador e, com o access token emitido antes, ainda no prazo, chama `GET /users`
- **THEN** a resposta é `403`

#### Scenario: Até a renovação, o resto segue o token
- **WHEN** um Operador é rebaixado a Leitor e, com o access token emitido antes, ainda no prazo, chama `POST /agents/` com corpo válido
- **THEN** a resposta não é `401` nem `403`; e, depois de renovar, a mesma chamada responde `403`

#### Scenario: A promoção chega na renovação
- **WHEN** um Leitor é promovido a Operador e, sem renovar, chama `POST /agents/`, e depois renova e chama de novo
- **THEN** a primeira resposta é `403`, e a segunda não é `401` nem `403`

## ADDED Requirements

### Requirement: O token de aplicação externa é o subject application, sem papel e sem rota até a #187
No `apps/api`, o token de uma aplicação externa SHALL ter o subject `application` quando o `client_id` dele está fora da tabela fechada de clientes, não começa com `buteco-`, e o token traz o claim `buteco_app`. Um `client_id` da tabela fechada SHALL continuar com o subject da tabela, mesmo que o token traga `buteco_app`. O `role` SHALL NOT ser lido para `application`. O subject `application` SHALL ser autorizado só pela lista dele na `ServiceRoutes`, que nesta entrega é **vazia**: o token de aplicação SHALL receber `403` em toda rota autenticada do `apps/api`, inclusive `POST /agents/{id}/a2a`, sem executar nenhuma lógica de negócio. A #187 acrescenta a rota A2A junto com o filtro por agente.

#### Scenario: 403 em toda rota autenticada
- **WHEN** um token de aplicação válido chama cada rota autenticada mapeada no host do `apps/api`
- **THEN** todas respondem `403`, e nenhuma responde `401`

#### Scenario: A2A recusada sem publicar job
- **WHEN** um token de aplicação válido chama `POST /agents/{id}/a2a` com um `SendMessage` válido para um agente ativo
- **THEN** a resposta é `403`, e nenhum job é publicado

#### Scenario: buteco_app forjado num cliente da tabela fechada
- **WHEN** um token do `buteco-servico-inbox` traz o claim `buteco_app` e chama `GET /agents/{id}` de um agente existente
- **THEN** o subject é `service:inbox` e a resposta é `200`

#### Scenario: Cliente fora do prefixo sem buteco_app
- **WHEN** um token do emissor com `aud` `buteco-api` e `client_id` `teste-sem-cadastro`, sem `buteco_app`, chama `GET /agents/`
- **THEN** a resposta é `403`
