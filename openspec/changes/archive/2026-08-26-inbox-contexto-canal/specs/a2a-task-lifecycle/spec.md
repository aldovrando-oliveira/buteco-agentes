## MODIFIED Requirements

### Requirement: Workers processam a task até um estado terminal
O sistema SHALL, em `apps/workers`, consumir o job publicado, transicionar a
task para `working`, montar o agente configurado (nome e instruções
cadastrados), concatenar às instruções um bloco de contexto temporal
(instante de processamento atual, dia da semana calculado em código, o
instante da mensagem original quando disponível em
`Message.Metadata["messageInstant"]` da última mensagem do usuário no
histórico da task, a defasagem entre os dois instantes quando ambos
existirem e a diferença exceder o limiar configurado, e regra de
precedência para expressões de tempo relativas) e, em seguida, um bloco
de contexto de canal (o tipo do canal de origem quando disponível em
`Message.Metadata["channelType"]` e o identificador do contato atribuído
pelo provedor do canal quando disponível em
`Message.Metadata["contactExternalId"]`, ambos da mesma última mensagem
do usuário, nomeando o identificador pelo que ele é — atribuído pelo
provedor — nunca como telefone) — sem tocar `Agent.Instructions`
persistida e sem qualquer um dos dois blocos entrar no histórico de
conversa, resolver o `IChatClient` correspondente ao `provider` cadastrado
no agente, incluir na chamada ao LLM o histórico da conversa de tasks
anteriores `completed` no mesmo `contextId` (respeitando um limite de
tamanho e excluindo turnos que não terminaram em `completed`), resumir de
forma incremental a porção do histórico que ultrapassa um limiar global de
interações em vez de descartá-la, e escrever o resultado de volta no mesmo
store durável compartilhado com a API, de forma que qualquer valor
colocado em `Metadata` seja lido de volta byte-identicamente pelo
`PostgresTaskStore` de `apps/api`.

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

#### Scenario: Chamada ao LLM inclui o instante de processamento e o dia da semana correspondente
- **WHEN** o worker monta a chamada ao LLM para qualquer task
- **THEN** as instruções enviadas ao LLM incluem o instante de
  processamento atual (relógio do worker no momento da execução, em ISO
  8601 com offset) e o dia da semana por extenso correspondente a esse
  instante, calculado pelo worker e não deixado para o LLM inferir

#### Scenario: Sem instante da mensagem disponível, expressões relativas se resolvem contra o instante de processamento
- **WHEN** o worker monta a chamada ao LLM para uma task cuja última
  mensagem do usuário no histórico não tem `Message.Metadata`, não tem a
  chave `messageInstant`, ou tem um valor que não é uma string ISO 8601
  parseável
- **THEN** as instruções enviadas ao LLM incluem uma regra explícita de que
  expressões de tempo relativas no pedido do usuário devem se resolver
  contra o instante de processamento informado, sem apresentar um segundo
  instante inexistente

#### Scenario: Valor de messageInstant ilegível é tratado como ausência, com log de aviso
- **WHEN** a última mensagem do usuário no histórico da task tem
  `Message.Metadata["messageInstant"]` presente, mas com um valor que não
  é uma string ISO 8601 parseável
- **THEN** o worker trata a task como se o instante da mensagem estivesse
  ausente (regra de precedência resolve contra o instante de
  processamento) e registra um log de nível aviso identificando a task
  afetada, sem transicionar a task para `failed` por causa disso

#### Scenario: Com instante da mensagem disponível, expressões relativas se resolvem contra ele
- **WHEN** o worker monta a chamada ao LLM para uma task cuja última
  mensagem do usuário no histórico tem
  `Message.Metadata["messageInstant"]` presente com um valor ISO 8601
  parseável
- **THEN** as instruções enviadas ao LLM incluem esse instante como o
  instante em que a mensagem do usuário foi originalmente escrita, e uma
  regra explícita de que expressões de tempo relativas no pedido do
  usuário devem se resolver contra ele, não contra o instante de
  processamento

#### Scenario: Defasagem entre o instante da mensagem e o instante de processamento acima do limiar aparece na chamada ao LLM
- **WHEN** o instante da mensagem está disponível e a diferença entre ele e
  o instante de processamento excede o limiar configurado
- **THEN** as instruções enviadas ao LLM incluem uma linha informando essa
  defasagem em termos legíveis (dias/horas/minutos), para que o agente
  possa considerar que a resposta está atrasada

#### Scenario: Defasagem abaixo do limiar não aparece na chamada ao LLM
- **WHEN** o instante da mensagem está disponível e a diferença entre ele e
  o instante de processamento não excede o limiar configurado
- **THEN** as instruções enviadas ao LLM não incluem a linha de defasagem,
  apenas o instante de processamento, o instante da mensagem e a regra de
  precedência

#### Scenario: Chamada ao LLM inclui o bloco de contexto de canal quando tipo de canal e identificador do contato estão disponíveis
- **WHEN** o worker monta a chamada ao LLM para uma task cuja última
  mensagem do usuário no histórico tem `Message.Metadata["channelType"]`
  e `Message.Metadata["contactExternalId"]` presentes com valores string
  não vazios
- **THEN** as instruções enviadas ao LLM incluem um bloco de contexto de
  canal com o tipo de canal e o identificador do contato, nomeando este
  último como identificador atribuído pelo provedor — nunca como
  telefone

#### Scenario: Sem channelType nem contactExternalId disponíveis, nenhum bloco de contexto de canal é incluído
- **WHEN** o worker monta a chamada ao LLM para uma task cuja última
  mensagem do usuário no histórico não tem `Message.Metadata`, ou tem
  `Message.Metadata` sem as chaves `channelType` e `contactExternalId`,
  ou com valor vazio (`""`) ou de tipo JSON diferente de string (número,
  objeto, array, booleano, `null`) para as duas
- **THEN** as instruções enviadas ao LLM não incluem nenhum bloco de
  contexto de canal, sem transicionar a task para `failed` por causa
  disso

#### Scenario: Só um dos dois campos de contexto de canal disponível resulta em bloco parcial
- **WHEN** o worker monta a chamada ao LLM para uma task cuja última
  mensagem do usuário no histórico tem só uma das duas chaves
  (`channelType` ou `contactExternalId`) presente com valor string não
  vazio, e a outra ausente, vazia, ou com valor de tipo JSON diferente de
  string
- **THEN** as instruções enviadas ao LLM incluem o bloco de contexto de
  canal só com a linha correspondente ao campo presente, sem mencionar o
  campo ausente como desconhecido ou com placeholder

#### Scenario: Valor de channelType ou contactExternalId com tipo JSON diferente de string é tratado como ausência do respectivo campo, com log de aviso
- **WHEN** a última mensagem do usuário no histórico da task tem
  `Message.Metadata["channelType"]` e/ou
  `Message.Metadata["contactExternalId"]` presente, mas com um valor cujo
  tipo JSON não é string (número, objeto, array, booleano, ou `null`
  explícito)
- **THEN** o worker trata o(s) campo(s) afetado(s) como ausente(s) para
  efeito do bloco de contexto de canal (mesmo resultado visual da
  ausência silenciosa) e registra um log de nível aviso identificando a
  task e o nome do campo afetado, sem transicionar a task para `failed`
  por causa disso

#### Scenario: Bloco de contexto de canal não entra no histórico de conversa nem em Agent.Instructions persistida
- **WHEN** o worker monta e usa o bloco de contexto de canal para uma
  chamada ao LLM
- **THEN** o bloco não é persistido em `Agent.Instructions` no banco, e
  não aparece no histórico de conversa reconstruído para tasks
  subsequentes do mesmo `contextId`
