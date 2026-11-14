## MODIFIED Requirements

### Requirement: Matriz de papéis por rota nos três apps
O `apps/api`, o `apps/inbox` e o `apps/connectors` SHALL autorizar o token do painel (cliente `buteco-painel`, subject `operator`) por uma tabela fechada de papéis por rota (`RoleRoutes`), casada por método e `RoutePattern.RawText`, com os papéis `admin`, `operator` e `reader` (Admin, Operador e Leitor). O token do painel SHALL passar só nas rotas em cuja entrada esteja o `role` dele, e SHALL receber `403` nas outras, sem executar nenhuma lógica de negócio da rota. O subject `operator` SHALL NOT dar acesso implícito a nenhuma rota.

A tabela SHALL ser esta (A = `admin`, O = `operator`, L = `reader`):

| app | rotas | papéis |
|---|---|---|
| `apps/api` | leituras de agentes, provedores, MCP (inclusive `GET /mcp-servers/{id}/tools`), bases, documentos, eventos de documento, diagnóstico do índice, resumo de indexação e `/insights/*` | A O L |
| `apps/api` | escritas de agentes (inclusive vínculos de MCP, delegação e bases), MCP (inclusive `POST /mcp-servers/test` e `POST /mcp-servers/{id}/test`), bases e documentos (inclusive reindexar) | A O |
| `apps/api` | `/users/*` (inclusive convite, novo link, cancelamento, redefinição de senha, redefinição de 2 etapas, `PUT /users/{id}/role` e `DELETE /users/{id}/federated/{chave}`), `GET /identity` e `GET /identity/federated-providers` | A |
| `apps/api` | `/account/*` (inclusive `POST /account/federated/{chave}/connect` e `DELETE /account/federated/{chave}`) | A O L |
| `apps/api` | `/applications/*` (listar, ver, criar, editar, desativar, reativar, gerar e revogar segredo; listar, liberar, mudar operações e retirar agentes da aplicação) e `GET /agents/{id}/applications` | A |
| `apps/api` | `GET /agents/{id}/applications/count` | A O L |
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

#### Scenario: Aplicações com acesso a um agente, por papel
- **WHEN** um token do painel de cada papel chama `GET /agents/{id}/applications/count` e `GET /agents/{id}/applications` de um agente existente
- **THEN** os três papéis não recebem `401` nem `403` na contagem; na lista, o `admin` não recebe `401` nem `403`, e o `operator` e o `reader` recebem `403`

#### Scenario: Só o Admin libera agentes para uma aplicação
- **WHEN** um token do painel com `role` `operator` chama `POST /applications/{id}/agents` com corpo válido
- **THEN** a resposta é `403`, e nenhum vínculo é gravado

#### Scenario: Só o Admin lê os provedores e desconecta outro usuário
- **WHEN** um token do painel de cada papel chama `GET /identity/federated-providers` e `DELETE /users/{id}/federated/{chave}` de outro usuário vinculado
- **THEN** o `admin` não recebe `401` nem `403`, e o `operator` e o `reader` recebem `403`, sem o vínculo mudar

#### Scenario: Todo papel desconecta a própria conta
- **WHEN** um usuário ativo de cada papel, com senha e vínculo, chama `DELETE /account/federated/{chave}` com o próprio token
- **THEN** nenhuma das respostas é `401` nem `403`
