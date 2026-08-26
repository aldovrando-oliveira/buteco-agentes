# inbox-message-orchestration Specification

## Purpose

`apps/inbox` hoje só sabe cadastrar canais e resolver `Contact`/`Session` —
nunca conversou de verdade com `apps/api`. Esta capability prova o pedaço
que todo adapter de canal (WhatsApp/Telegram) vai precisar: agrupar
mensagens que chegam em rajada (debounce) e completar um round-trip real do
protocolo A2A — `SendMessage` contra `apps/api` e a resposta de volta via
push notification. Ela cobre a ingestão de mensagens normalizadas, o buffer
de debounce persistido por sessão, o disparo idempotente do `SendMessage`
via cliente A2A, e a recepção validada da resposta assíncrona, garantindo
que nenhum conteúdo bufferizado seja perdido ou retido além do necessário.
## Requirements
### Requirement: Ingestão de mensagem normalizada resolve Contact e Session
O sistema SHALL expor, via `apps/inbox`, um serviço interno que recebe uma
mensagem normalizada `(ChannelId, ExternalId, texto, receivedAt, metadado do
contato, identificador externo da mensagem)`, resolve `Contact`/`Session`
usando o resolvedor já existente — repassando o metadado do contato para
captura na criação (ver capability `inbox-contact-session`, Requirement
"Captura de metadado do contato na criação") — persiste um registro durável
da mensagem de entrada (ver capability `inbox-message-history`, Requirement
"Mensagem de entrada persistida durante a ingestão") e adiciona a mensagem
ao buffer de debounce da sessão resolvida — sem exigir nenhum endpoint HTTP
para essa ingestão. Quando o identificador externo da mensagem já
corresponder a uma mensagem de entrada persistida para a mesma `Session`, o
serviço SHALL tratar a ingestão como deduplicada (ver capability
`inbox-message-history`, Requirement "Deduplicação de mensagem de entrada
por identificador externo"), sem adicionar conteúdo novo ao buffer de
debounce.

#### Scenario: Mensagem recebida é associada à Session resolvida
- **WHEN** o serviço interno é chamado com um `(ChannelId, ExternalId)`, um
  texto de mensagem, um metadado de contato e um identificador externo de
  mensagem inédito para essa `Session`
- **THEN** a `Session` correspondente é resolvida (criada ou reaproveitada,
  conforme as regras já existentes de resolução de sessão), o metadado
  informado é repassado à resolução de `Contact`, um registro durável da
  mensagem de entrada é persistido, e a mensagem é adicionada ao buffer de
  debounce dessa `Session`

#### Scenario: Mensagem com identificador externo já processado não é adicionada ao buffer novamente
- **WHEN** o serviço interno é chamado com um identificador externo de
  mensagem que já corresponde a uma mensagem de entrada persistida para a
  mesma `Session`
- **THEN** nenhum conteúdo novo é adicionado ao buffer de debounce dessa
  `Session` por essa chamada

### Requirement: Debounce agrupa mensagens dentro da janela configurada
O sistema SHALL agrupar, num único disparo de `SendMessage`, todas as
mensagens recebidas para a mesma `Session` dentro da janela de debounce
configurada globalmente. Cada nova mensagem recebida antes do disparo
SHALL reiniciar a contagem da janela a partir do momento de recebimento
dessa mensagem.

#### Scenario: Duas mensagens dentro da janela geram um único SendMessage
- **WHEN** duas mensagens chegam para a mesma `Session`, a segunda antes da
  janela de debounce expirar desde a primeira
- **THEN** apenas um `SendMessage` é disparado, contendo o conteúdo das
  duas mensagens, disparado após a janela expirar desde a última mensagem
  recebida

#### Scenario: Mensagens fora da janela geram disparos separados
- **WHEN** uma mensagem chega para uma `Session`, o disparo referente a ela
  ocorre, e só depois uma nova mensagem chega para a mesma `Session`
- **THEN** dois `SendMessage` distintos são disparados, um para cada
  mensagem

### Requirement: Buffer de debounce persistido sobrevive a restart de apps/inbox
O sistema SHALL persistir o buffer de debounce (mensagens pendentes e o
timestamp da última mensagem recebida por sessão) em PostgreSQL, sem uso
de armazenamento em memória, de forma que mensagens já recebidas não sejam
perdidas se o processo de `apps/inbox` reiniciar antes do disparo.

#### Scenario: Mensagem bufferizada sobrevive a um restart antes do disparo
- **WHEN** uma mensagem é recebida e bufferizada, e o processo de
  `apps/inbox` é reiniciado antes da janela de debounce expirar
- **THEN** após o restart, a mensagem continua no buffer e o disparo ocorre
  normalmente quando a janela expirar

### Requirement: Disparo do debounce é idempotente entre múltiplas instâncias
O sistema SHALL garantir que, quando múltiplas instâncias de `apps/inbox`
estiverem rodando simultaneamente, um buffer de debounce cuja janela
expirou seja disparado exatamente uma vez, nunca duas.

#### Scenario: Duas instâncias competindo pelo mesmo buffer disparam uma única vez
- **WHEN** duas instâncias de `apps/inbox` avaliam, ao mesmo tempo, o mesmo
  buffer de debounce cuja janela já expirou
- **THEN** apenas uma das instâncias efetivamente dispara o `SendMessage`
  para esse buffer; a outra reconhece que já foi reivindicado e não dispara

### Requirement: Disparo do debounce envia SendMessage real contra apps/api
O sistema SHALL, ao disparar um buffer de debounce, montar e enviar um
`SendMessage` real contra `apps/api`, usando o `AgentId` do `Channel` e o
`ContextId` da `Session` resolvida, incluindo uma configuração de push
notification que aponta para um endpoint receptor do próprio `apps/inbox`
com um token gerado para essa chamada específica.

#### Scenario: SendMessage disparado usa o AgentId do Channel e o ContextId da Session
- **WHEN** um buffer de debounce é disparado
- **THEN** o `SendMessage` enviado usa o `AgentId` do `Channel` de origem da
  sessão e o `ContextId` da `Session` resolvida, e inclui
  `pushNotificationConfig` com uma `url` do próprio `apps/inbox` e um token
  específico dessa chamada

### Requirement: Resposta síncrona de rejeição encerra o disparo sem aguardar push notification
O sistema SHALL, ao receber uma resposta síncrona de `SendMessage` cujo
estado da task já é terminal de rejeição, encerrar imediatamente o
acompanhamento desse disparo, sem aguardar uma push notification que não
será enviada para esse caso.

#### Scenario: SendMessage retorna task rejeitada
- **WHEN** a resposta síncrona de um `SendMessage` disparado retorna uma
  task com estado de rejeição (ex. agente inativo ou sem provider
  configurado)
- **THEN** o sistema registra o encerramento desse disparo imediatamente,
  sem manter nenhuma espera por push notification para essa tentativa

### Requirement: Falha de transporte do SendMessage é reintentada antes de ser considerada perda definitiva
O sistema SHALL capturar falhas de transporte ao enviar um `SendMessage`
(timeout, `apps/api` inalcançável) sem interromper o funcionamento do
orquestrador, e SHALL reapresentar o buffer de debounce correspondente
para uma nova tentativa de disparo, em vez de descartá-lo na primeira
falha. Somente após um número configurado de tentativas sem sucesso o
sistema SHALL considerar a mensagem perdida, registrando essa perda
definitiva por meio de log estruturado com severidade mais alta que a
usada para uma falha individual.

#### Scenario: apps/api inalcançável durante o disparo é reintentado
- **WHEN** o disparo de um buffer de debounce falha porque `apps/api` está
  inalcançável ou não responde dentro do timeout configurado, e o número
  de tentativas anteriores ainda está abaixo do limite configurado
- **THEN** a falha é registrada via log estruturado, o buffer permanece
  disponível para uma nova tentativa de disparo, o orquestrador continua
  funcionando normalmente para os demais buffers, e nenhuma exceção não
  tratada é propagada

#### Scenario: Falhas repetidas esgotam as tentativas e a mensagem é considerada perdida
- **WHEN** o disparo de um mesmo buffer de debounce falha repetidamente
  por falha de transporte até atingir o número máximo de tentativas
  configurado
- **THEN** o sistema registra a perda definitiva dessa mensagem via log
  estruturado de severidade mais alta, e o buffer não é mais reapresentado
  para disparo

### Requirement: Endpoint receptor de push notification valida o token da chamada
O sistema SHALL expor, em `apps/inbox`, um endpoint HTTP que recebe a
`AgentTask` completa enviada como push notification, e SHALL aceitar essa
requisição somente quando o header contendo o token da notificação
corresponder ao token gerado para a chamada `SendMessage` correspondente.

#### Scenario: Push notification com token correto é aceita
- **WHEN** o endpoint receptor recebe uma requisição para uma task
  disparada por este orquestrador, com o header de token correspondendo ao
  token gerado para essa chamada
- **THEN** a requisição é aceita e processada como conclusão do round-trip

#### Scenario: Push notification sem o token correto é rejeitada
- **WHEN** o endpoint receptor recebe uma requisição sem o header de token,
  ou com um valor que não corresponde ao token gerado para a chamada
  correspondente
- **THEN** a requisição é rejeitada, sem processar o payload recebido

### Requirement: Conteúdo bufferizado é removido ao final do ciclo de disparo
O sistema SHALL remover o registro do buffer de debounce (incluindo o
conteúdo das mensagens e o token de push notification) assim que esse
ciclo de disparo alcançar um estado terminal — seja por push notification
recebida e validada, seja por rejeição síncrona, seja por falha de
transporte — sem reter esse conteúdo além do necessário para o disparo.
Quando o estado terminal for alcançado por uma push notification válida
cuja task possui uma mensagem de resposta associada, o sistema SHALL,
antes de remover o registro, entregar essa resposta ao canal de origem
(ver capability `inbox-channel-adapter-plugin`, Requirement "Entrega da
resposta do agente ao canal de origem"). Uma vez que uma push
notification tenha sido aceita por ter o token correto, o sistema SHALL
completar a entrega ao canal de origem (quando aplicável) e a
persistência local do resultado — atualização de status de dispatch e
remoção do registro do buffer — independentemente de o chamador que
enviou a push notification já ter encerrado a conexão ou desistido de
esperar a resposta dentro do seu próprio timeout.

#### Scenario: Buffer é removido após push notification válida
- **WHEN** uma push notification com token correto é recebida para um
  disparo em andamento
- **THEN** a resposta do agente, quando presente, é entregue ao canal de
  origem antes de o registro correspondente do buffer de debounce ser
  removido

#### Scenario: Buffer é removido após rejeição síncrona ou falha de transporte
- **WHEN** um disparo termina por rejeição síncrona ou por falha de
  transporte
- **THEN** o registro correspondente do buffer de debounce é removido

#### Scenario: Processamento completa mesmo com o chamador desconectado
- **WHEN** uma push notification com token correto é aceita, e o
  chamador que a enviou encerra a conexão (por exemplo, por atingir seu
  próprio timeout) antes de receber a resposta HTTP deste endpoint, mas
  depois de o token já ter sido validado
- **THEN** a entrega ao canal de origem (quando aplicável), a
  atualização do status de dispatch das mensagens do grupo para
  concluído e a remoção do registro do buffer de debounce completam
  normalmente, sem serem interrompidas pela desconexão do chamador

### Requirement: Falha de infraestrutura durante a consulta de candidatos do ciclo de varredura não derruba o processo
O sistema SHALL capturar e registrar (log estruturado) qualquer exceção
de infraestrutura não relacionada a A2A/transporte (ex.: falha de acesso
ao Postgres) que ocorra durante a consulta que identifica os candidatos
elegíveis de um ciclo de varredura do debounce, sem propagá-la para fora
do `BackgroundService` responsável pela varredura. O processo
`apps/inbox` SHALL permanecer em execução após essa falha, e o ciclo de
varredura seguinte (no intervalo configurado) SHALL consultar e
processar normalmente, sem necessidade de reinício do processo.

Distinto do requisito já existente "Falha de transporte do SendMessage é
reintentada antes de ser considerada perda definitiva" — aquele cobre
falha ao chamar `apps/api` durante o disparo de um buffer específico;
este cobre falha na própria consulta de candidatos, antes mesmo de
chegar a um disparo.

#### Scenario: Falha transitória de banco durante a consulta de candidatos não derruba o processo
- **WHEN** uma exceção não relacionada a A2A/transporte (ex.: falha de
  conexão com o Postgres) ocorre durante a consulta de
  `pending_dispatches` elegíveis em um ciclo de varredura
- **THEN** a exceção é registrada via log estruturado e o processo
  `apps/inbox` continua em execução

#### Scenario: Ciclo de varredura seguinte consulta e processa normalmente após uma falha anterior na consulta
- **WHEN** a consulta de candidatos de um ciclo de varredura anterior
  falhou por infraestrutura e foi tratada conforme o Scenario acima
- **AND** o ciclo seguinte, no intervalo configurado, não encontra
  nenhuma condição de falha
- **THEN** os `pending_dispatches` elegíveis nesse ciclo seguinte são
  consultados e processados normalmente, como se a falha anterior não
  tivesse ocorrido

### Requirement: Falha ao processar um candidato específico não bloqueia os demais candidatos do mesmo ciclo
O sistema SHALL capturar e registrar (log estruturado) qualquer exceção
de infraestrutura não relacionada a A2A/transporte que ocorra ao
processar o disparo de um candidato específico dentro de um ciclo de
varredura, sem interromper o processamento dos demais candidatos
elegíveis nesse mesmo ciclo.

#### Scenario: Falha ao processar um candidato não impede o disparo de outro candidato no mesmo ciclo
- **WHEN** dois candidatos elegíveis (A e B) existem no mesmo ciclo de
  varredura
- **AND** o processamento do candidato A lança uma exceção não
  relacionada a A2A/transporte
- **THEN** a exceção de A é registrada via log estruturado, e o
  candidato B é disparado normalmente no mesmo ciclo, sem esperar por um
  novo ciclo de varredura

### Requirement: Falha capturada durante o ciclo de varredura nunca é silenciosa
O sistema SHALL registrar toda exceção capturada durante o ciclo de
varredura — tanto na consulta de candidatos quanto no processamento
individual de um candidato — via log estruturado de nível de erro,
incluindo a exceção original e contexto suficiente para diagnóstico
(no caso de falha por candidato, incluindo o identificador do candidato
afetado). Nunca deve haver supressão silenciosa de uma falha real.

#### Scenario: Falha na consulta de candidatos gera entrada de log de nível erro
- **WHEN** uma exceção de infraestrutura é capturada durante a consulta
  de candidatos de um ciclo de varredura
- **THEN** uma entrada de log de nível erro é emitida, contendo a
  exceção original

#### Scenario: Falha ao processar um candidato gera entrada de log de nível erro
- **WHEN** uma exceção de infraestrutura é capturada ao processar o
  disparo de um candidato específico
- **THEN** uma entrada de log de nível erro é emitida, contendo a
  exceção original e o identificador do candidato afetado

