## MODIFIED Requirements

### Requirement: Workers processam a task até um estado terminal
O sistema SHALL, em `apps/workers`, consumir o job publicado, transicionar a
task para `working`, montar o agente configurado (nome e instruções
cadastrados), resolver o `IChatClient` correspondente ao `provider`
cadastrado no agente, incluir na chamada ao LLM o histórico da conversa de
tasks anteriores `completed` no mesmo `contextId` (respeitando um limite de
tamanho e excluindo turnos que não terminaram em `completed`), resumir de
forma incremental a porção do histórico que ultrapassa um limiar global de
interações em vez de descartá-la, e escrever o resultado de volta no mesmo
store durável compartilhado com a API.

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

#### Scenario: Segunda mensagem no mesmo contextId chega ao LLM com o histórico da primeira
- **WHEN** uma task anterior no mesmo `contextId` terminou `completed` e o
  worker consome um novo job referenciando esse mesmo `contextId`
- **THEN** a chamada ao LLM para a nova task inclui a mensagem do usuário e a
  resposta do agente da task anterior, além da nova mensagem do usuário

#### Scenario: Histórico reconstruído respeita um limite de tamanho
- **WHEN** o número de mensagens acumuladas de tasks `completed` anteriores
  no mesmo `contextId` excede o limite configurado globalmente
- **THEN** o worker inclui na chamada ao LLM só as mensagens mais recentes
  dentro do limite, sem repassar o histórico inteiro sem limite

#### Scenario: Histórico que ultrapassa o limiar de interações é resumido em vez de descartado
- **WHEN** o número de interações (turnos de usuário) acumuladas no mesmo
  `contextId` ultrapassa o limiar global configurado para resumo
- **THEN** a chamada ao LLM para o turno corrente inclui um resumo
  condensado da porção mais antiga da conversa em vez do histórico cru
  correspondente a ela, junto com os turnos mais recentes preservados sem
  resumir

#### Scenario: Resumo é recalculado de forma incremental, não do zero a cada gatilho
- **WHEN** o limiar de interações é cruzado mais de uma vez ao longo de uma
  mesma conversa no mesmo `contextId`
- **THEN** cada novo resumo é produzido a partir do resumo anterior somado
  aos turnos novos desde então, sem reenviar ao LLM os turnos já cobertos
  por um resumo anterior

#### Scenario: Falha na chamada de resumo não impede o turno do usuário de ser processado
- **WHEN** o limiar de interações é cruzado e a chamada ao LLM para gerar o
  resumo falha (ex.: provider indisponível)
- **THEN** o worker segue processando o turno corrente do usuário usando o
  histórico não resumido daquele momento, sem transicionar a task para
  `failed` por causa exclusivamente dessa falha, e tenta resumir novamente
  no próximo gatilho

#### Scenario: Tasks failed ou rejected no mesmo contextId não entram no histórico
- **WHEN** uma task anterior no mesmo `contextId` terminou `failed` ou
  `rejected` (sem ter sido seguida por nenhuma task `completed` depois dela)
- **THEN** o worker processa a próxima task desse `contextId` sem incluir
  conteúdo dessa task falha/rejeitada no histórico enviado ao LLM

#### Scenario: Tasks do mesmo contextId processadas por instâncias diferentes do worker compartilham o histórico
- **WHEN** uma task `completed` de um `contextId` foi processada por uma
  instância do worker, e uma task seguinte do mesmo `contextId` é consumida
  por uma instância diferente do worker
- **THEN** a instância que processa a task seguinte recupera o histórico
  produzido pela instância anterior a partir do store durável compartilhado,
  sem depender de nenhum estado mantido em memória por uma instância
  específica

#### Scenario: Agente editado entre duas mensagens do mesmo contextId usa as novas Instructions imediatamente
- **WHEN** um agente tem suas `Instructions` atualizadas (`PUT
  /agents/{id}`) entre duas mensagens de uma mesma conversa em andamento no
  mesmo `contextId`
- **THEN** a chamada ao LLM para a mensagem seguinte usa as `Instructions`
  atuais do agente, sem ficar presa a um valor antigo guardado na sessão
  recuperada do `contextId`

#### Scenario: Worker consome o job antes da task existir no store e tenta novamente
- **WHEN** o worker consome um job referenciando uma task que ainda não está
  visível no store durável (corrida entre a API publicar no RabbitMQ e o
  registro da task `submitted` ainda não ter sido commitado)
- **THEN** o worker tenta ler a task novamente por um curto período antes de
  desistir, processando a task normalmente assim que ela existir no store,
  em vez de descartar a mensagem na primeira tentativa
