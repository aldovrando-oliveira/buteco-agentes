## ADDED Requirements

### Requirement: SendMessage para agente com modelo não habilitado rejeita a task sem publicar job
O sistema SHALL, ao receber `SendMessage` na rota A2A de um agente
cadastrado (`/agents/{id}/a2a`) ativo, com `provider` configurado no
ambiente de `apps/api` e cujo par `provider`+`model` **não** esteja na lista
curada de modelos habilitados (capability `llm-model-curation`),
transicionar a task para o estado `rejected` do protocolo A2A, sem publicar
nenhum job de execução no RabbitMQ.

A conferência SHALL ler a lista curada do banco a cada `SendMessage`, na
mesma regra e pelo mesmo código da validação de agente (capability
`llm-provider-catalog`), e nunca de cache nem do `A2AServer` já resolvido. SHALL vir depois da conferência de provedor configurado: com o
provedor sem chave, a recusa é a do provedor. A conferência SHALL NOT
consultar a listagem de modelos de nenhum fornecedor.

A recusa SHALL valer só para tarefas novas: um job publicado antes de o
modelo ser retirado SHALL NOT ser recusado nem cancelado por esta regra. A
continuação de uma conversa (`SendMessage` com `taskId`/`contextId` de
task anterior) é tarefa nova e SHALL passar por ela.

#### Scenario: SendMessage para agente cujo modelo foi retirado é rejeitado
- **WHEN** um agente ativo, com provider configurado, tem um `model` que o
  Admin retirou da lista curada, e um cliente envia `SendMessage` válido
  para a rota A2A dele
- **THEN** a API responde com a task no estado `TASK_STATE_REJECTED` e
  nenhuma mensagem referenciando essa task é publicada no RabbitMQ

#### Scenario: Task rejeitada por modelo não habilitado continua consultável via GetTask
- **WHEN** um `SendMessage` para um agente com modelo não habilitado resulta
  em task rejeitada
- **THEN** uma consulta subsequente via `GetTask` para o mesmo `taskId`
  responde com HTTP 200 e estado `TASK_STATE_REJECTED`

#### Scenario: Modelo habilitado de novo volta ao fluxo normal sem reiniciar
- **WHEN** o modelo de um agente foi retirado, o `A2AServer` desse agente já
  tinha sido resolvido, o Admin habilita o modelo de novo, e um cliente
  envia `SendMessage` para a rota A2A do agente
- **THEN** a API cria a task em `submitted` e publica o job de execução no
  RabbitMQ normalmente

#### Scenario: Agente editado para um modelo habilitado volta ao fluxo normal
- **WHEN** um agente com modelo retirado é atualizado via `PUT /agents/{id}`
  para um `model` habilitado, e um cliente envia `SendMessage` para a rota
  A2A dele
- **THEN** a API cria a task em `submitted` e publica o job de execução

#### Scenario: A validação de agente e o SendMessage concordam sobre o par
- **WHEN** um agente ativo tem, em cada caso, um par habilitado com chave, um par
  habilitado sem chave, um par não habilitado com chave, e um modelo habilitado só
  para outro provedor
- **THEN** em cada caso, `PUT /agents/{id}` com o mesmo `provider`/`model` é aceito
  se e somente se o `SendMessage` é aceito, e a recusa do `SendMessage` traz
  `ProviderNotConfigured` quando o erro de validação é no campo `provider` e
  `ModelNotEnabled` quando é no campo `model`

#### Scenario: A recusa não consulta o fornecedor
- **WHEN** a listagem de modelos dos três fornecedores está indisponível e
  um cliente envia `SendMessage` para um agente com modelo habilitado
- **THEN** a task é aceita e o job é publicado, sem nenhuma chamada a
  fornecedor

#### Scenario: Agente que executava antes do deploy continua aceitando tarefas
- **WHEN** a migration da lista curada é aplicada sobre um banco com um
  agente ativo cujo par `provider`+`model` está fora dos nove do catálogo
  estático anterior, e um cliente envia `SendMessage` para ele
- **THEN** a task é aceita e o job é publicado
