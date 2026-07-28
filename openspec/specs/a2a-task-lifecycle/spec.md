# a2a-task-lifecycle Specification

## Purpose

TBD - defined by change backend-agente-a2a-mvp. Update Purpose after archive.

## Requirements

### Requirement: Endpoint A2A por agente cadastrado
O sistema SHALL expor, para cada agente cadastrado em `apps/api`, uma rota
A2A própria (`/agents/{id}/a2a`), hospedada via
`Microsoft.Agents.AI.Hosting.A2A.AspNetCore`, capaz de receber requisições
`SendMessage` do protocolo A2A.

#### Scenario: Agente cadastrado responde na rota A2A própria
- **WHEN** um agente foi cadastrado com sucesso e um cliente envia uma
  requisição `SendMessage` do protocolo A2A para `/agents/{id}/a2a`
- **THEN** a API aceita a requisição e inicia o processamento da task,
  sem erro de rota não encontrada

#### Scenario: SendMessage para agente inexistente retorna erro apropriado
- **WHEN** um cliente envia `SendMessage` para `/agents/{id}/a2a` com um
  `id` que não corresponde a nenhum agente cadastrado
- **THEN** a API responde com um erro do protocolo A2A indicando que o
  agente/rota não existe, sem criar nenhuma task

### Requirement: Task A2A nasce em submitted e a execução é delegada de forma assíncrona
O sistema SHALL, ao receber `SendMessage`, criar a task no store durável com
estado `submitted` e publicar um job de execução no RabbitMQ, retornando o
controle ao cliente sem aguardar a execução do agente pelo LLM.

#### Scenario: SendMessage cria task submitted e retorna imediatamente
- **WHEN** um cliente envia `SendMessage` válido para a rota A2A de um
  agente cadastrado
- **THEN** a API persiste uma task com estado `submitted` no store durável
  e responde ao cliente sem bloquear aguardando a resposta do LLM

#### Scenario: Task submitted é publicada no RabbitMQ para os workers processarem
- **WHEN** uma task é criada com estado `submitted`
- **THEN** uma mensagem referenciando essa task é publicada em uma fila
  durável do RabbitMQ, disponível para consumo pelos workers

### Requirement: Workers processam a task até um estado terminal
O sistema SHALL, em `apps/workers`, consumir o job publicado, transicionar a
task para `working`, montar o agente configurado (nome e instruções
cadastrados) e chamar o LLM, escrevendo o resultado de volta no mesmo store
durável compartilhado com a API.

#### Scenario: Worker processa a task com sucesso até completed
- **WHEN** o worker consome um job referenciando uma task `submitted` e a
  chamada ao LLM é bem-sucedida
- **THEN** a task é transicionada para `working` e, em seguida, para
  `completed`, com o resultado do agente registrado como artefato/mensagem
  da task no store durável

#### Scenario: Falha na execução do agente leva a task a failed
- **WHEN** o worker consome um job e a chamada ao LLM falha
- **THEN** a task é transicionada para `failed` no store durável, sem
  deixar a task presa indefinidamente em `working`

### Requirement: Consulta do estado final da task via polling
O sistema SHALL permitir, via `apps/api`, consultar o estado atual de uma
task A2A (incluindo o estado final produzido pelo worker) através do método
`GetTask` do protocolo A2A, lendo diretamente do store durável compartilhado.

#### Scenario: GetTask reflete o resultado após o worker completar a task
- **WHEN** um worker completa o processamento de uma task e, em seguida, um
  cliente consulta essa task via `GetTask`
- **THEN** a API responde com estado `completed` e o resultado produzido
  pelo agente, mesmo que a consulta tenha sido feita por uma instância da
  API diferente da que recebeu o `SendMessage` original

#### Scenario: GetTask antes da conclusão reflete o estado intermediário
- **WHEN** um cliente consulta `GetTask` para uma task que o worker ainda
  não terminou de processar
- **THEN** a API responde com o estado atual da task (`submitted` ou
  `working`) tal como persistido no store durável

### Requirement: SendMessage para agente inativo rejeita a task sem publicar job
O sistema SHALL, ao receber `SendMessage` na rota A2A de um agente
cadastrado (`/agents/{id}/a2a`) cujo estado seja inativo (`isActive =
false`), transicionar a task para o estado `rejected` do protocolo A2A,
sem publicar nenhum job de execução no RabbitMQ e sem inventar um código
de erro HTTP próprio para esse caso.

#### Scenario: SendMessage para agente inativo é rejeitado
- **WHEN** um cliente envia `SendMessage` válido para a rota A2A de um
  agente cadastrado que está com `isActive = false`
- **THEN** a API responde com a task no estado `TASK_STATE_REJECTED` e
  nenhuma mensagem referenciando essa task é publicada no RabbitMQ

#### Scenario: Task rejeitada continua consultável via GetTask
- **WHEN** um `SendMessage` para um agente inativo resulta em task
  rejeitada
- **THEN** uma consulta subsequente via `GetTask` para o mesmo `taskId`
  responde com HTTP 200 e estado `TASK_STATE_REJECTED`, em vez de "task
  não encontrada"

#### Scenario: SendMessage para agente reativado volta ao fluxo normal
- **WHEN** um agente foi desativado e depois reativado (`isActive =
  true`), e um cliente envia `SendMessage` para a rota A2A desse agente
- **THEN** a API cria a task em `submitted` e publica o job de execução no
  RabbitMQ normalmente, mesmo que o `A2AServer` desse agente já tivesse
  sido resolvido antes da desativação
