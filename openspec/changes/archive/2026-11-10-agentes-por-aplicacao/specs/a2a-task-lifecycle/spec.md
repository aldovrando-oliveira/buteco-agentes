## ADDED Requirements

### Requirement: A rota A2A da aplicação externa passa pelo vínculo antes do JSON-RPC
Para o token do subject `application` (`route-roles`), `POST /agents/{id}/a2a` SHALL conferir, antes de o SDK do A2A processar a requisição, nesta ordem:

1. a aplicação está ativa **e** tem vínculo com o agente `{id}` (`external-application-agents`); senão SHALL responder `404` com o corpo vazio;
2. o método está mapeado a uma operação liberada no vínculo: `SendMessage` → `enviar-mensagem`, `GetTask` → `consultar-task`, e `SendMessage` com `params.configuration.pushNotificationConfig` → também `notificacao-push`; senão SHALL responder `403` com o corpo vazio.

Qualquer outro método — `CancelTask`, `ListTasks`, `SendStreamingMessage`, `SubscribeToTask`, `CreateTaskPushNotificationConfig`, `GetTaskPushNotificationConfig`, `ListTaskPushNotificationConfig`, `DeleteTaskPushNotificationConfig`, `GetExtendedAgentCard` ou nome desconhecido — SHALL receber `403` vazio, com qualquer vínculo. Nos dois casos de recusa, nada SHALL ser lido de `agents`, nenhuma task SHALL ser criada ou alterada e nenhum job SHALL ser publicado. Corpo sem `method` legível SHALL seguir para o SDK, que responde o erro JSON-RPC de sempre.

A decisão SHALL ser tomada sobre o corpo desserializado com os mesmos tipos e as mesmas opções do SDK, de modo que o método e a configuração que a checagem autoriza sejam os que o SDK executa. O token de outros subjects (`service:inbox`) SHALL NOT passar por esta checagem.

#### Scenario: O 404 não revela se o agente existe
- **WHEN** uma aplicação chama `SendMessage` para um agente inexistente, para um agente existente sem vínculo e, já desativada e com o token ainda no prazo, para um agente com vínculo
- **THEN** as três respostas são `404` e iguais byte a byte (status, cabeçalhos de conteúdo e corpo vazio), e nenhuma task é criada

#### Scenario: Aplicação desativada perde a A2A na hora
- **WHEN** o Admin desativa uma aplicação e ela, com o token emitido antes, ainda no prazo de 5 min, chama `GetTask` de uma task dela num agente liberado
- **THEN** a resposta é `404`

#### Scenario: Operação não liberada
- **WHEN** uma aplicação com só `consultar-task` no agente A chama `SendMessage` no agente A
- **THEN** a resposta é `403` com o corpo vazio, nenhuma task é criada e nenhum job é publicado

#### Scenario: Cancelar está fora das operações
- **WHEN** uma aplicação com as três operações no agente A chama `CancelTask` de uma task dela em `submitted`
- **THEN** a resposta é `403`, e a task continua em `submitted`

#### Scenario: Listar tasks está fora das operações
- **WHEN** uma aplicação com as três operações no agente A chama `ListTasks`
- **THEN** a resposta é `403`, sem nenhuma task no corpo

#### Scenario: O método que a checagem autoriza é o que o SDK executa
- **WHEN** uma aplicação com só `consultar-task` no agente A manda um corpo com `"method": "GetTask"` e, depois, outra chave `"method": "ListTasks"`
- **THEN** a resposta é `403`, e o `ListTasks` não é executado

#### Scenario: Método duplicado no outro sentido
- **WHEN** a mesma aplicação manda `"method": "ListTasks"` e, depois, `"method": "GetTask"` com o id de uma task dela no agente A
- **THEN** a resposta é a do `GetTask` dessa task

#### Scenario: Nome de método com caixa trocada
- **WHEN** uma aplicação com as três operações chama o método `gettask`
- **THEN** a resposta é `403`

#### Scenario: Agente inativo com vínculo rejeita a task
- **WHEN** uma aplicação com `enviar-mensagem` no agente A, que foi desativado, chama `SendMessage` no agente A
- **THEN** a resposta não é `404` nem `403`: a task termina `TASK_STATE_REJECTED` e nenhum job é publicado, como para o inbox

#### Scenario: O inbox não passa pela checagem
- **WHEN** o token do `buteco-servico-inbox` chama `SendMessage` para um agente ativo que nenhuma aplicação tem liberado
- **THEN** a task é criada em `submitted` e o job é publicado, como antes desta checagem

### Requirement: Cada task guarda a aplicação que a criou
A task criada pelo `SendMessage` de uma aplicação SHALL gravar o id dela como dono, uma vez, na criação. A task criada pelo `service:inbox`, a criada pela delegação entre agentes (`agent-delegation-execution`) e as que já existiam antes desta mudança SHALL ficar sem dono. As transições de estado, no `apps/api` e no `apps/workers`, SHALL NOT alterar o dono.

#### Scenario: Dono gravado na criação
- **WHEN** uma aplicação cria uma task pelo `SendMessage`
- **THEN** a linha da task em `a2a_tasks` tem `application_id` igual ao id da aplicação

#### Scenario: O worker não apaga o dono
- **WHEN** a task de uma aplicação é executada pelo `apps/workers` até `completed`
- **THEN** a linha continua com o mesmo `application_id`

#### Scenario: A task do inbox fica sem dono
- **WHEN** o `service:inbox` cria uma task
- **THEN** a linha tem `application_id` nulo

### Requirement: A aplicação só alcança as próprias tasks no agente da rota
Para o subject `application`, uma task SHALL ser alcançável pela rota `/agents/{id}/a2a` só se o dono dela for a aplicação **e** o agente dela for `{id}`. Em `GetTask` e em `SendMessage` com `message.taskId`, qualquer outra task — de outra aplicação, sem dono ou de outro agente — SHALL receber o mesmo erro JSON-RPC de task inexistente (`-32001`, "Task '{id}' not found."), sem ler nem alterar a task e sem publicar job. O `service:inbox` SHALL continuar alcançando qualquer task, como antes.

#### Scenario: Task de outra aplicação é não encontrada
- **WHEN** a aplicação B, com `consultar-task` no agente A, chama `GetTask` de uma task da aplicação A1 no agente A
- **THEN** a resposta é igual, byte a byte, à de um `GetTask` com um id que não existe (com o mesmo `id` do JSON-RPC)

#### Scenario: Task sem dono é não encontrada
- **WHEN** uma aplicação chama `GetTask` de uma task criada pelo inbox no mesmo agente
- **THEN** a resposta é o erro de task inexistente

#### Scenario: Task própria em outro agente é não encontrada
- **WHEN** uma aplicação com `consultar-task` nos agentes A e B chama, pela rota do agente A, `GetTask` de uma task dela no agente B
- **THEN** a resposta é o erro de task inexistente

#### Scenario: Continuar a task de outro dono
- **WHEN** uma aplicação manda `SendMessage` com `message.taskId` de uma task de outra aplicação
- **THEN** a resposta é o erro de task inexistente, a task não muda e nenhum job é publicado

#### Scenario: O inbox lê a task de uma aplicação
- **WHEN** o `service:inbox` chama `GetTask` de uma task criada por uma aplicação
- **THEN** a resposta traz a task

### Requirement: A aplicação só reusa o contexto das próprias tasks no agente da rota
Um `contextId` informado no `SendMessage` de uma aplicação SHALL ser aceito só se existir uma task dela, no agente da rota, com esse contexto. Qualquer outro — que não existe, de outra aplicação, de task sem dono, ou dela em outro agente — SHALL receber o mesmo erro JSON-RPC `-32602` com a mensagem "Contexto não encontrado.", sem criar task e sem publicar job, de modo que a resposta não revele se o contexto existe. O `SendMessage` sem `contextId` SHALL abrir uma conversa nova, com o contexto gerado pelo SDK. O `service:inbox` SHALL continuar informando qualquer `contextId`, como antes.

#### Scenario: Contexto inexistente e contexto alheio dão a mesma resposta
- **WHEN** uma aplicação manda `SendMessage` com um `contextId` que nunca foi usado e, depois, com o `contextId` de uma conversa do inbox no mesmo agente
- **THEN** as duas respostas são iguais byte a byte (com o mesmo `id` do JSON-RPC): `-32602` "Contexto não encontrado.", e nenhuma task é criada

#### Scenario: A conversa continua no contexto próprio
- **WHEN** uma aplicação recebe o `contextId` de uma task dela no agente A e manda o próximo `SendMessage` no agente A com ele
- **THEN** a task nova é criada nesse contexto, e o worker monta o histórico com a task anterior

#### Scenario: Conversa nova sem contexto
- **WHEN** uma aplicação manda `SendMessage` sem `contextId`
- **THEN** a task é criada com um `contextId` novo, devolvido na resposta

### Requirement: A delegação não herda a restrição da aplicação
A task de uma aplicação SHALL poder delegar para qualquer agente que o agente dela tenha como alvo de delegação, liberado ou não para a aplicação. A task filha SHALL nascer sem dono, e a aplicação SHALL NOT alcançá-la pelo id.

#### Scenario: Filha num agente não liberado
- **WHEN** a task de uma aplicação no agente A delega para o agente B, que não está liberado para ela
- **THEN** a delegação acontece, a filha tem `application_id` nulo, e um `GetTask` da aplicação pelo id da filha (pela rota do A ou do B) recebe `404` ou o erro de task inexistente
