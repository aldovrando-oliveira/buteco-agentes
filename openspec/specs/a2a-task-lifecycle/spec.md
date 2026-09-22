# a2a-task-lifecycle Specification

## Purpose

Define o ciclo de vida de uma task do protocolo A2A de ponta a ponta, através
dos **dois** processos que o executam — nenhum deles o cobre sozinho.

`apps/api` recebe o `SendMessage` no endpoint A2A do agente, decide se a task
chega a nascer (agente inativo, sem `provider`/`model` configurados, ou com
provedor indisponível são rejeitados **antes** de publicar qualquer job),
persiste a task em `submitted` no store durável e publica um job no RabbitMQ.
`apps/api` **nunca chama o LLM**.

`apps/workers` consome o job e leva a task até um estado terminal: monta o
agente, concatena os blocos de contexto temporal e de canal às instruções sem
tocar o que está persistido, resolve o `IChatClient` do provedor — reutilizando
uma instância por `(provider, model)` —, reconstrói o histórico da conversa do
mesmo `contextId` respeitando limite e resumo incremental, e escreve o
resultado de volta no **mesmo** store durável.

O que esta capability garante, e que nenhuma das duas metades garante isolada:
a task nunca fica presa em `working` quando a execução falha; o estado terminal
é observável por polling; e o que um processo grava em `Metadata` é lido de
volta byte-identicamente pelo outro.

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
controle ao cliente sem aguardar a execução do agente pelo LLM. Qualquer
valor presente em `Message.Metadata` da mensagem recebida SHALL ser
persistido no store durável sem alteração de formato — mesmas opções de
serialização usadas pelo restante do pipeline A2A
(`A2AJsonUtilities.DefaultOptions`) — de forma que um worker que releia
essa task encontre o mesmo valor byte-identicamente.

#### Scenario: SendMessage cria task submitted e retorna imediatamente
- **WHEN** um cliente envia `SendMessage` válido para a rota A2A de um
  agente cadastrado
- **THEN** a API persiste uma task com estado `submitted` no store durável
  e responde ao cliente sem bloquear aguardando a resposta do LLM

#### Scenario: Task submitted é publicada no RabbitMQ para os workers processarem
- **WHEN** uma task é criada com estado `submitted`
- **THEN** uma mensagem referenciando essa task é publicada em uma fila
  durável do RabbitMQ, disponível para consumo pelos workers

#### Scenario: Valor de messageInstant em Message.Metadata persiste no formato correto
- **WHEN** um `SendMessage` cujo `Message.Metadata` contém a chave
  `messageInstant` (ver capability `inbox-message-orchestration`,
  Requirement "Disparo do debounce envia SendMessage real contra
  apps/api") é recebido e a task correspondente é persistida
- **THEN** o valor lido de volta do registro persistido, em JSON bruto, é
  uma string ISO 8601 idêntica à enviada — sem casing, escaping ou
  estrutura divergente do restante do pipeline A2A

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
conversa, **serializar entre instâncias concorrentes do worker o
processamento de tasks do mesmo par `(agentId, contextId)`** por meio de um
lock adquirido no Postgres compartilhado — de forma que duas instâncias nunca
executem o agente para a mesma conversa ao mesmo tempo, e de forma que a
**falha em adquirir esse lock, inclusive por esgotamento do tempo de espera,
leve a task a um estado terminal em vez de deixá-la presa em `working`** —,
resolver o `IChatClient` correspondente ao `provider` cadastrado
no agente **reutilizando uma única instância por combinação de
`(provider, model)` ao longo de toda a vida do processo**, sem
construir um client novo por mensagem e sem descartá-lo entre
execuções, registrando em log a duração de cada requisição ao
provedor de LLM, incluir na chamada ao LLM o histórico da conversa de tasks
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
- **THEN** o worker usa um `IChatClient` específico do Anthropic para essa
  execução, sem depender de um client único fixo para todos os agentes

#### Scenario: Duas mensagens para o mesmo provider e model reutilizam a mesma instância de IChatClient
- **WHEN** o worker resolve o `IChatClient` duas vezes para o mesmo par
  `(provider, model)`
- **THEN** as duas resoluções devolvem **a mesma instância**, sem construir
  um client novo na segunda — de forma que o pool de conexões HTTP criado
  pelo SDK do provedor seja um só por par, e não um por mensagem

#### Scenario: Providers ou models diferentes recebem instâncias diferentes
- **WHEN** o worker resolve o `IChatClient` para dois pares
  `(provider, model)` que diferem em pelo menos um dos dois valores
- **THEN** cada par recebe sua própria instância, e nenhuma das duas é
  devolvida no lugar da outra

#### Scenario: Provider sem credencial configurada volta a falhar em toda resolução
- **WHEN** o worker resolve duas vezes seguidas o `IChatClient` de um
  `provider` cuja credencial não está configurada no ambiente
- **THEN** as duas resoluções falham com o mesmo erro explícito, sem que a
  primeira falha fique memorizada como resultado da chave nem passe a ser
  devolvida no lugar de uma tentativa nova

#### Scenario: Instância reutilizada continua utilizável depois de uma execução completa
- **WHEN** uma task é processada até um estado terminal e, em seguida, uma
  segunda task do mesmo agente é processada
- **THEN** a segunda task também chega a um estado terminal, sem falhar por
  uso de um client já descartado pela execução anterior

#### Scenario: Duração da requisição ao provedor de LLM é registrada em log
- **WHEN** o worker executa uma task e a chamada ao provedor de LLM retorna
- **THEN** o worker registra em log a duração dessa requisição, identificando
  `provider` e `model`, medindo a requisição ao provedor e não o turno
  completo do agente (que inclui execução de tools)

#### Scenario: Duração é registrada também quando a requisição ao provedor falha
- **WHEN** a chamada ao provedor de LLM lança
- **THEN** o worker registra em log a duração decorrida até a falha antes de
  a exceção propagar, para que uma chamada que estoura por espera não fique
  sem medida registrada

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

#### Scenario: Segunda mensagem no mesmo contextId recebe um bloco de contexto temporal atualizado
- **WHEN** o worker processa uma segunda task no mesmo `contextId` de uma
  conversa já em andamento, em um momento posterior à primeira task
- **THEN** a chamada ao LLM para a segunda task inclui o instante de
  processamento da segunda execução, não o instante capturado durante a
  primeira execução, independentemente do histórico de conversa recuperado
  da sessão persistida

#### Scenario: Bloco de contexto temporal nunca é persistido nem entra no histórico de conversa
- **WHEN** uma task é concluída após o worker montar o bloco de contexto
  temporal para a chamada ao LLM
- **THEN** o conteúdo desse bloco não aparece no histórico de conversa
  recuperado por uma task seguinte do mesmo `contextId`, e a coluna
  `Instructions` do agente persistida em `apps/api`/`apps/workers`
  permanece inalterada

#### Scenario: Metadata da task persistida pelo worker é lida de volta byte-identicamente pelo PostgresTaskStore da API
- **WHEN** o worker grava, via seu `PostgresTaskStore`, uma task cujo
  `Metadata` contém um valor codificado internamente (ex.:
  `conversationSession`, produzido por `ConversationSessionCodec.Encode`)
- **THEN** o `PostgresTaskStore` de `apps/api`, lendo a mesma linha da
  tabela `a2a_tasks`, retorna esse valor de `Metadata` com o texto JSON
  bruto (`GetRawText()`) idêntico ao que o worker gravou — sem depender de
  nenhuma normalização adicional para os dois lados concordarem

#### Scenario: Falha ao adquirir a serialização da conversa leva a task a failed
- **WHEN** o worker consome um job, transiciona a task para `working` e não
  consegue adquirir o lock de serialização do par `(agentId, contextId)`
  dentro do tempo de espera disponível — porque outra execução da mesma
  conversa o mantém em posse
- **THEN** a task é transicionada para `failed` no store durável, sem ficar
  presa em `working`, e a push notification configurada para ela é disparada
  como em qualquer outro caminho de falha

#### Scenario: Duas tasks concorrentes do mesmo contextId em instâncias diferentes terminam as duas
- **WHEN** duas instâncias do worker consomem, ao mesmo tempo, duas tasks do
  mesmo par `(agentId, contextId)`, e a execução da primeira se estende além
  do tempo que a segunda pode esperar pelo lock
- **THEN** as duas tasks alcançam um estado terminal — a primeira pelo seu
  próprio resultado, a segunda por `failed` —, e nenhuma permanece em
  `working` indefinidamente

#### Scenario: Falha ao adquirir a serialização não deixa recurso pendurado
- **WHEN** a aquisição do lock de serialização falha
- **THEN** o escopo de serviço e a conexão Postgres abertos para essa
  aquisição são descartados antes de a falha ser propagada, de forma que
  ocorrências repetidas não esgotem o pool de conexões do processo

#### Scenario: Task concluída com sucesso não é sobrescrita por falha ao liberar a serialização
- **WHEN** a execução termina com sucesso, a task já foi gravada como
  `completed`, e a liberação do lock de serialização falha em seguida
- **THEN** a task permanece `completed` no store durável, com o resultado do
  agente preservado — a falha na liberação não a regrava como `failed`

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
