## MODIFIED Requirements

### Requirement: Workers processam a task até um estado terminal
O sistema SHALL, em `apps/workers`, consumir o job publicado, transicionar a
task para `working`, montar o agente configurado (nome e instruções
cadastrados), resolver o `IChatClient` correspondente ao `provider`
cadastrado no agente e chamar o LLM através dele, escrevendo o resultado de
volta no mesmo store durável compartilhado com a API.

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

#### Scenario: Worker resolve o IChatClient do provider configurado no agente
- **WHEN** o worker consome um job referenciando um agente com `provider`
  Anthropic
- **THEN** o worker constrói e usa um `IChatClient` específico do
  Anthropic para essa execução, sem depender de um client único fixo para
  todos os agentes

#### Scenario: Provider configurado no agente mas ausente no ambiente do worker leva a task a failed
- **WHEN** o worker consome um job referenciando um agente cujo `provider`
  não está configurado no ambiente de `apps/workers` (ainda que estivesse
  configurado no ambiente de `apps/api` no momento do `SendMessage`)
- **THEN** a task é transicionada para `failed` no store durável, sem
  deixar o processo do worker encerrar de forma não tratada

## ADDED Requirements

### Requirement: SendMessage para agente sem provider/model configurados rejeita a task sem publicar job
O sistema SHALL, ao receber `SendMessage` na rota A2A de um agente
cadastrado (`/agents/{id}/a2a`) cujo `provider` ou `model` estejam nulos
(estado "precisa de reconfiguração"), transicionar a task para o estado
`rejected` do protocolo A2A, sem publicar nenhum job de execução no
RabbitMQ e sem inventar um código de erro HTTP próprio para esse caso.

#### Scenario: SendMessage para agente sem provider/model é rejeitado
- **WHEN** um cliente envia `SendMessage` válido para a rota A2A de um
  agente cadastrado cujo `provider` ou `model` estão nulos
- **THEN** a API responde com a task no estado `TASK_STATE_REJECTED` e
  nenhuma mensagem referenciando essa task é publicada no RabbitMQ

#### Scenario: Task rejeitada por falta de configuração continua consultável via GetTask
- **WHEN** um `SendMessage` para um agente sem `provider`/`model`
  resulta em task rejeitada
- **THEN** uma consulta subsequente via `GetTask` para o mesmo `taskId`
  responde com HTTP 200 e estado `TASK_STATE_REJECTED`, em vez de "task
  não encontrada"

#### Scenario: SendMessage para agente reconfigurado volta ao fluxo normal
- **WHEN** um agente estava sem `provider`/`model` e foi atualizado via
  `PUT /agents/{id}` com valores válidos e disponíveis, e um cliente envia
  `SendMessage` para a rota A2A desse agente
- **THEN** a API cria a task em `submitted` e publica o job de execução no
  RabbitMQ normalmente

### Requirement: SendMessage para agente com provider indisponível rejeita a task sem publicar job
O sistema SHALL, ao receber `SendMessage` na rota A2A de um agente
cadastrado (`/agents/{id}/a2a`) cujo `provider` não esteja mais configurado
no ambiente de `apps/api` (variáveis de ambiente exigidas ausentes,
independentemente de terem estado presentes no momento do cadastro),
transicionar a task para o estado `rejected` do protocolo A2A, sem publicar
nenhum job de execução no RabbitMQ.

#### Scenario: SendMessage para agente com provider que deixou de estar configurado é rejeitado
- **WHEN** um agente foi cadastrado com `provider` Gemini configurado no
  ambiente, a variável de ambiente da API key do Gemini é removida do
  ambiente de `apps/api`, e um cliente envia `SendMessage` para a rota A2A
  desse agente
- **THEN** a API responde com a task no estado `TASK_STATE_REJECTED` e
  nenhuma mensagem referenciando essa task é publicada no RabbitMQ

#### Scenario: SendMessage para agente com provider reconfigurado no ambiente volta ao fluxo normal
- **WHEN** a variável de ambiente da API key de um provider anteriormente
  ausente é configurada novamente no ambiente de `apps/api`, e um cliente
  envia `SendMessage` para a rota A2A de um agente que usa esse `provider`
- **THEN** a API cria a task em `submitted` e publica o job de execução no
  RabbitMQ normalmente, mesmo que o `A2AServer` desse agente já tivesse
  sido resolvido antes do provider voltar a ficar disponível
