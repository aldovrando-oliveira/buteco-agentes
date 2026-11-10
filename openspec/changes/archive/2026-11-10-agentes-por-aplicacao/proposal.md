## Why

Desde a #186 (`7095350`, 10/11/2026), uma aplicação externa tem credencial própria e não alcança
nada: o subject `application` tem a lista de rotas **vazia** na `ServiceRoutes`, e o token dela
recebe `403` em toda rota autenticada do `apps/api`, inclusive `POST /agents/{id}/a2a`. A #187 abre a
rota A2A para a aplicação **junto com** o limite: cada aplicação alcança só os agentes liberados para
ela, e em cada agente só as operações liberadas; cada task guarda a aplicação que a criou, e uma
aplicação não enxerga task de outra.

## What Changes

- **Vínculo aplicação × agente**, com as operações liberadas por agente. Três operações nesta change:
  **enviar mensagem** (`SendMessage`), **consultar task** (`GetTask`) e **notificação push**
  (`configuration.pushNotificationConfig` dentro do `SendMessage`). A aplicação nasce sem agentes.
  "Cancelar task" fica fora (decisão do dono, Q1): o `CancelTask` grava `canceled` e o `apps/workers` o
  sobrescreve quando a task já está em `working`; o cancelamento real é a #264. Para a aplicação, o
  `CancelTask` cai na regra do método fora das operações (`403`). O comportamento do cancelamento não
  muda.
- **A rota A2A abre para o subject `application`**: `("POST", "/agents/{id}/a2a")` entra na lista dele,
  e a exceção "lista vazia de `application`" da checagem de boot sai no mesmo commit.
- **O filtro antes do JSON-RPC**, só para o subject `application`: agente sem vínculo, inexistente, ou
  aplicação desativada → `404` vazio, idêntico nos três casos; método fora das operações liberadas, ou
  `pushNotificationConfig` sem a operação push → `403` vazio (D2, Q2). O filtro lê o corpo com os
  tipos e as opções do próprio SDK: o que ele autoriza é o que o SDK executa.
- **Dono gravado em cada task** (`a2a_tasks.application_id`, nulo para o que é do Buteco), e filtro
  por dono **e por agente** em consultar e em continuar uma task (`message.taskId`): task de outra
  aplicação, do Buteco ou de outro agente → "não encontrada", igual à inexistente (D3). Um `contextId`
  só é aceito se for de uma task da própria aplicação no mesmo agente; inexistente e alheio recebem o
  mesmo `-32602` "Contexto não encontrado." (Q5).
- **Delegação não herda a restrição**: a task filha nasce sem dono, e a aplicação não a consulta (D4).
- **Rotas do Admin** para listar, liberar, mudar operações e retirar agentes de uma aplicação, e duas
  rotas no agente: a **contagem** de aplicações com acesso (todos os papéis) e a **lista** (só Admin)
  (D5).
- **Docs**: `docs/a2a-integration.md` (o que a aplicação alcança, as respostas `404`/`403`, as
  operações, a task e o contexto "não encontrados"; o cancelamento como ele é hoje, l. 59-62 e a linha
  de `TASK_STATE_CANCELED`, l. 453; "não há push", l. 555), `docs/deployment.md` (a volta atrás).

## Capabilities

### New Capabilities

- `external-application-agents`: o vínculo aplicação × agente, as operações, as rotas do Admin, a
  contagem e a lista por agente.

### Modified Capabilities

- `route-roles`: a rota A2A na lista de `application`; a exceção da lista vazia sai; as rotas novas na
  matriz.
- `route-authentication`: o subject `application` deixa de ter a lista "vazia até a #187".
- `a2a-task-lifecycle`: o filtro da rota A2A para a aplicação, o dono da task e o filtro por dono e
  por agente (task e contexto).
- `a2a-push-notifications`: a notificação push condicionada à operação, para a aplicação.
- `external-applications`: a lista traz os agentes liberados de cada aplicação (Q11); a desativação corta a chamada A2A na hora (D2.4), e o vínculo sobrevive a
  desativar e reativar.

## Impact

- **Código:** só o `apps/api`. O `apps/workers` não muda: a task filha nasce sem dono porque o
  `AppDbContext` dele não conhece a coluna (D4). O `apps/inbox` não muda: o filtro não se aplica ao
  `service:inbox`.
- **Banco:** migration com a tabela `external_application_agents` e a coluna anulável
  `a2a_tasks.application_id` (com índice). Nenhuma linha existente muda.
- **Contrato:** para o token de aplicação, `POST /agents/{id}/a2a` deixa de responder `403` em tudo e
  passa a `404`/`403`/JSON-RPC conforme o vínculo; rotas novas de vínculo. Nada muda para o
  `service:inbox` nem para o painel.
- **Suítes:** `api`, `rt-inbox`, `rt-crossapp`, `rt-connectors` (`~/.cache/buteco-agents/bin/suite-187.sh`).
- **Telas:** da #188, contra o protótipo na versão 63 (`1794271196-4a59`). Os estados sem quadro (agente
  inativo na lista, aplicação desativada na aba, o `409`, o `404` de vínculo retirado, o agente sem
  aplicação) são desenhados lá; o contrato de cada um está nas specs desta change. A aba Atividade é da
  #199.
- **Decisões:** Q1–Q14 do design, decididas pelo dono em 09/11/2026 (Q1 e Q5 com o texto dele).
