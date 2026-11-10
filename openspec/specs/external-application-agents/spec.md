# external-application-agents Specification

## Purpose
Diz quais agentes cada aplicação externa alcança pelo A2A e com quais operações: o vínculo aplicação ×
agente no `apps/api`, com as operações liberadas por agente (enviar mensagem, consultar task,
notificação push; sem cancelamento, #264), as rotas do Admin em `/applications/{id}/agents` e, no
agente, a contagem das aplicações com acesso (todos os papéis) e a lista delas (só o Admin). O que a
rota A2A faz com o vínculo está em `a2a-task-lifecycle` e `a2a-push-notifications`. Criada pela change
`agentes-por-aplicacao` (#187; ver `openspec/changes/archive/2026-11-10-agentes-por-aplicacao/`).
## Requirements
### Requirement: O vínculo aplicação × agente, com as operações liberadas
O `apps/api` SHALL guardar, por par (aplicação externa, agente), no máximo um vínculo, com a lista não vazia das operações liberadas para aquela aplicação naquele agente. As operações SHALL ser exatamente três, com estes valores em `operacoes`:

| valor | o que libera na rota A2A (`a2a-task-lifecycle`) |
|---|---|
| `enviar-mensagem` | `SendMessage` |
| `consultar-task` | `GetTask` |
| `notificacao-push` | `configuration.pushNotificationConfig` dentro do `SendMessage` |

Não SHALL existir operação de cancelamento. Uma aplicação SHALL nascer sem nenhum vínculo. O vínculo SHALL sobreviver a desativar e reativar a aplicação e a desativar e reativar o agente. Agente inativo e aplicação desativada SHALL poder ser liberados e ter as operações alteradas.

#### Scenario: Aplicação nasce sem agentes
- **WHEN** um Admin cria uma aplicação e chama `GET /applications/{id}/agents`
- **THEN** a resposta é `200` com a lista vazia

#### Scenario: Desativar e reativar não apaga o vínculo
- **WHEN** uma aplicação com o agente A liberado em `enviar-mensagem` é desativada e reativada
- **THEN** `GET /applications/{id}/agents` continua com o agente A e `enviar-mensagem`

### Requirement: Listar os agentes liberados de uma aplicação
`GET /applications/{id}/agents` SHALL ser do Admin com o papel gravado (`route-roles`) e SHALL responder `200` com `[{ agenteId, nome, descricao, ativo, operacoes, liberadoEm }]`, ordenado pelo nome do agente, com `operacoes` na ordem da tabela do vínculo. Aplicação inexistente SHALL responder `404` com `title` "Aplicação não encontrada.". Aplicação desativada SHALL responder a lista normalmente.

#### Scenario: Agente inativo aparece com ativo false
- **WHEN** a aplicação tem liberado um agente que foi desativado, e o Admin lista os agentes dela
- **THEN** o item do agente tem `ativo` `false` e as mesmas `operacoes`

#### Scenario: Aplicação inexistente
- **WHEN** o Admin chama `GET /applications/{id}/agents` com um `id` sem aplicação
- **THEN** a resposta é `404` com `title` "Aplicação não encontrada."

### Requirement: Liberar um agente para uma aplicação
`POST /applications/{id}/agents` SHALL receber `{ agenteId, operacoes }`, sem senha, e SHALL conferir, nesta ordem, parando na primeira falha: Admin gravado (`403`); aplicação existente (`404` "Aplicação não encontrada."); `operacoes` com ao menos um item (`400` `ValidationProblem`, campo `operacoes`, "Marque ao menos uma operação."), e cada item entre os três valores (`400`, campo `operacoes`, "Operação desconhecida: {valor}."); agente existente (`400`, campo `agenteId`, "Agente não encontrado."); o par ainda sem vínculo (`409` com `title` "Este agente já está liberado para a aplicação."). Valores repetidos em `operacoes` SHALL ser gravados uma vez. Passando, SHALL gravar o vínculo e responder `201` com o item no formato da listagem.

#### Scenario: Liberar com duas operações
- **WHEN** o Admin libera o agente A para a aplicação com `["consultar-task", "enviar-mensagem"]`
- **THEN** a resposta é `201` com `operacoes` `["enviar-mensagem", "consultar-task"]`, e o agente aparece na listagem

#### Scenario: Sem operação
- **WHEN** o Admin libera o agente A com `operacoes` vazia
- **THEN** a resposta é `400` com o campo `operacoes` "Marque ao menos uma operação.", e nenhum vínculo é gravado

#### Scenario: Cancelar não é operação
- **WHEN** o Admin libera o agente A com `["enviar-mensagem", "cancelar-task"]`
- **THEN** a resposta é `400` com o campo `operacoes` "Operação desconhecida: cancelar-task.", e nenhum vínculo é gravado

#### Scenario: Agente já liberado
- **WHEN** o agente A já está liberado para a aplicação e o Admin o libera de novo
- **THEN** a resposta é `409` com `title` "Este agente já está liberado para a aplicação.", e as operações gravadas não mudam

#### Scenario: Agente inexistente
- **WHEN** o Admin libera um `agenteId` que não corresponde a nenhum agente
- **THEN** a resposta é `400` com o campo `agenteId` "Agente não encontrado."

#### Scenario: Agente inativo pode ser liberado
- **WHEN** o Admin libera um agente desativado para a aplicação
- **THEN** a resposta é `201` com `ativo` `false`

### Requirement: Mudar as operações e retirar o agente
`PUT /applications/{id}/agents/{agentId}` SHALL receber `{ operacoes }`, com as regras de `operacoes` da liberação, e SHALL substituir a lista inteira, respondendo `200` com o item. `DELETE /applications/{id}/agents/{agentId}` SHALL apagar o vínculo e responder `204`. As duas, sem senha, SHALL responder `404` com `title` "Aplicação não encontrada." para aplicação inexistente e `404` com `title` "Agente não liberado para esta aplicação." quando o par não tem vínculo. Retirar o agente ou uma operação SHALL valer na próxima chamada A2A da aplicação; a entrega de push de task já criada não é desfeita (`a2a-push-notifications`).

#### Scenario: Trocar as operações
- **WHEN** o agente A está liberado com `enviar-mensagem` e `consultar-task`, e o Admin grava `["consultar-task"]`
- **THEN** a resposta é `200` com `operacoes` `["consultar-task"]`, e o próximo `SendMessage` da aplicação no agente A recebe `403`

#### Scenario: Retirado entre abrir e salvar
- **WHEN** um Admin retira o agente A da aplicação e outro, depois, grava operações para o mesmo par
- **THEN** a segunda resposta é `404` com `title` "Agente não liberado para esta aplicação."

#### Scenario: Retirar
- **WHEN** o Admin retira o agente A da aplicação
- **THEN** a resposta é `204`, a listagem não traz o agente A, e a próxima chamada A2A da aplicação no agente A recebe `404`

### Requirement: Aplicações com acesso a um agente
O `apps/api` SHALL servir, por agente, duas rotas, cada uma com a sua linha na matriz de papéis (`route-roles`):

- `GET /agents/{id}/applications/count`, para Admin, Operador e Leitor, que SHALL responder `200` com `{ total, ativas }`: `total` conta os vínculos do agente, de aplicações ativas e desativadas, e `ativas`, só os de aplicações ativas;
- `GET /agents/{id}/applications`, só do Admin com o papel gravado, que SHALL responder `200` com `[{ id, nome, ativa, operacoes, ultimoUso }]`, ativas antes e depois pelo nome, com `ultimoUso` o da aplicação (`external-applications`).

Agente inexistente SHALL responder `404` nas duas. Nenhuma das duas SHALL trazer o client ID nem dado de segredo. A contagem SHALL NOT trazer nome nem identificador de aplicação.

#### Scenario: Leitor vê só a contagem
- **WHEN** o agente A tem três vínculos, um de aplicação desativada, e um Leitor chama as duas rotas
- **THEN** a contagem responde `200` com `{ total: 3, ativas: 2 }`, e a lista responde `403`

#### Scenario: Admin vê a lista
- **WHEN** o Admin chama `GET /agents/{id}/applications` do mesmo agente
- **THEN** a resposta tem as três aplicações, as duas ativas antes, com `ativa`, `operacoes` e `ultimoUso`

#### Scenario: Agente sem aplicação
- **WHEN** o agente não tem vínculo e um Operador chama a contagem
- **THEN** a resposta é `200` com `{ total: 0, ativas: 0 }`

