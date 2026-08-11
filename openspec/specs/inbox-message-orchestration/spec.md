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
mensagem normalizada `(ChannelId, ExternalId, texto, receivedAt, metadado do contato)`,
resolve `Contact`/`Session` usando o resolvedor já existente — repassando o
metadado do contato para captura na criação (ver capability
`inbox-contact-session`, Requirement "Captura de metadado do contato na
criação") — e adiciona a mensagem ao buffer de debounce da sessão
resolvida — sem exigir nenhum endpoint HTTP para essa ingestão.

#### Scenario: Mensagem recebida é associada à Session resolvida
- **WHEN** o serviço interno é chamado com um `(ChannelId, ExternalId)`, um
  texto de mensagem e um metadado de contato
- **THEN** a `Session` correspondente é resolvida (criada ou reaproveitada,
  conforme as regras já existentes de resolução de sessão), o metadado
  informado é repassado à resolução de `Contact`, e a mensagem é adicionada
  ao buffer de debounce dessa `Session`

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
resposta do agente ao canal de origem").

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
