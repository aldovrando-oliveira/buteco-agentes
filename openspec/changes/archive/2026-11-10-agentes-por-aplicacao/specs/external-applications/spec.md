## MODIFIED Requirements

### Requirement: Listar aplicações
`GET /applications/` SHALL devolver todas as aplicações, ativas e desativadas, com `{ id, nome, descricao, clientId, ativa, criadaEm, desativadaEm, ultimoUso, agentes }`, em que `agentes` é a lista `[{ id, nome }]` dos agentes liberados para a aplicação (`external-application-agents`), pelo nome, vazia quando não há nenhum, ordenadas por ativas antes e depois pelo nome. `?busca=` SHALL filtrar pelo nome ou pelo `clientId` contendo o texto, sem diferenciar maiúscula. `?estado=ativas` e `?estado=desativadas` SHALL filtrar pelo estado; outro valor SHALL responder `400` com o campo `estado`. Sem aplicação, a resposta SHALL ser `200` com a lista vazia.

#### Scenario: Busca por client ID
- **WHEN** existem "Portal do Parceiro" (`btc_app_7kq2m9x4`) e "Chatbot do site", e o Admin chama `GET /applications/?busca=7KQ2`
- **THEN** a lista tem só "Portal do Parceiro"

#### Scenario: Desativadas continuam listadas, depois das ativas
- **WHEN** existem "Zeta" ativa e "Alfa" desativada, e o Admin chama `GET /applications/`
- **THEN** a lista é "Zeta" e depois "Alfa", com `ativa` `false` na segunda

#### Scenario: Estado desconhecido
- **WHEN** o Admin chama `GET /applications/?estado=todas`
- **THEN** a resposta é `400` com o campo `estado`

#### Scenario: Os agentes liberados na lista
- **WHEN** a aplicação "Portal do Parceiro" tem os agentes "Triagem de Pedidos" e "Atendimento Varejo" liberados, e "Chatbot do site" nenhum, e o Admin chama `GET /applications/`
- **THEN** "Portal do Parceiro" traz `agentes` com "Atendimento Varejo" e depois "Triagem de Pedidos", cada um com o `id`, e "Chatbot do site" traz `agentes` vazia

### Requirement: Desativar e reativar
`POST /applications/{id}/deactivate` SHALL, **numa transação**, gravar a desativação e revogar todo segredo vivo da aplicação, e SHALL responder `200` com o detalhe. Desativar uma aplicação já desativada SHALL responder `200` sem mudar nada. `POST /applications/{id}/activate` SHALL voltar a aplicação a ativa **sem segredo vivo** e SHALL responder `200` com o detalhe. Nenhuma das duas SHALL pedir senha. Não SHALL existir rota de exclusão de aplicação. Desativar e reativar SHALL manter os agentes liberados da aplicação (`external-application-agents`). Desativada, a aplicação SHALL receber `404` na rota A2A já na requisição seguinte, mesmo com um access token emitido antes e ainda no prazo (`a2a-task-lifecycle`); nas outras rotas o token dela já recebe `403` (`route-roles`).

#### Scenario: Aplicação desativada não obtém token
- **WHEN** o Admin desativa uma aplicação e ela pede token com o segredo que tinha
- **THEN** a resposta do token é `invalid_client`, e todo segredo dela tem `estado` `revogado`

#### Scenario: Reativar não devolve o segredo antigo
- **WHEN** o Admin desativa e reativa uma aplicação, e ela pede token com o segredo de antes
- **THEN** a resposta do token é `invalid_client`, e o detalhe mostra a aplicação ativa sem segredo `ativo`

#### Scenario: Excluir não existe
- **WHEN** o Admin chama `DELETE /applications/{id}`
- **THEN** a resposta não é `2xx`, e a aplicação continua existindo

#### Scenario: Desativar corta a A2A na hora
- **WHEN** o Admin desativa uma aplicação com o agente A liberado, e ela chama `POST /agents/{id}/a2a` do agente A com um token emitido antes da desativação
- **THEN** a resposta é `404`, e o agente A continua na lista de agentes liberados da aplicação
