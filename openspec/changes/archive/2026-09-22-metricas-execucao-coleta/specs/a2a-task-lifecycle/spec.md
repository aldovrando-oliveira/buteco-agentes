## ADDED Requirements

### Requirement: Task lida em estado terminal não é executada de novo
`apps/workers` SHALL não executar uma task que lê do store em estado terminal —
`completed`, `failed`, `rejected` ou `canceled`, pelo mesmo predicado de estado
terminal do protocolo A2A — e SHALL confirmar a mensagem do job, registrando um
aviso. Uma task lida em `working` SHALL continuar sendo executada. A task nova
criada para a mensagem seguinte da mesma conversa SHALL ser executada
normalmente.

#### Scenario: Mensagem reentregue de task já concluída
- **WHEN** o job de uma task que já está em `completed` é entregue de novo ao worker
- **THEN** o LLM não é chamado, o estado da task continua `completed` e a mensagem é confirmada, sem voltar à fila

#### Scenario: Mensagem reentregue de task em qualquer estado terminal
- **WHEN** o job de uma task em `failed`, `rejected` ou `canceled` é entregue de novo ao worker
- **THEN** o LLM não é chamado e o estado da task não muda

#### Scenario: Mensagem reentregue de task em working continua executando
- **WHEN** o job de uma task que o worker lê em `working` é entregue de novo
- **THEN** a task é executada e chega a um estado terminal

#### Scenario: A conversa continua em task nova depois de uma task concluída
- **WHEN** uma task de um contexto termina em `completed` e a mensagem seguinte da mesma conversa chega
- **THEN** ela chega como task nova em `submitted`, é executada e chega a `completed`

#### Scenario: Mensagem para uma task já terminal é recusada pelo protocolo
- **WHEN** um `SendMessage` referencia o `taskId` de uma task já em estado terminal
- **THEN** a resposta é um erro do protocolo e nenhum job é publicado
