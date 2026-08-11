# inbox-channel-adapter-plugin Specification

## Purpose

TBD - defined by change inbox-adapter-contrato-catalogo. Update Purpose after archive.

## Requirements

### Requirement: Tipo de canal validado contra adapters registrados
O sistema SHALL validar `channelType`, no cadastro e na atualização de um
canal, contra o conjunto de tipos de adapter efetivamente registrados no
processo de `apps/inbox`, em vez de uma lista fixa de valores conhecidos
em tempo de compilação. Um `channelType` sem adapter correspondente
registrado SHALL ser rejeitado com erro de validação (HTTP 400), sem
criar ou alterar nenhum registro.

#### Scenario: Cadastro com channelType de adapter registrado é aceito
- **WHEN** um cliente envia `POST /channels` com um `channelType` que
  corresponde a um adapter efetivamente registrado no processo
- **THEN** a API aceita o `channelType` e prossegue com a validação dos
  demais campos

#### Scenario: Cadastro com channelType sem adapter registrado é rejeitado
- **WHEN** um cliente envia `POST /channels` com um `channelType` que não
  corresponde a nenhum adapter registrado no processo
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

### Requirement: Configuração de canal validada pelo adapter do tipo correspondente
O sistema SHALL, no cadastro e na atualização de um canal com credencial
informada, invocar o validador de configuração registrado para o
`channelType` correspondente sobre a credencial em texto plano, antes de
criptografá-la. Uma credencial rejeitada pelo validador do adapter SHALL
impedir o cadastro/atualização, retornando erro de validação (HTTP 400)
com os detalhes reportados pelo validador.

#### Scenario: Credencial aceita pelo validador do adapter é cadastrada
- **WHEN** um cliente envia `POST /channels` com uma credencial que o
  validador registrado para o `channelType` informado considera válida
- **THEN** a API criptografa a credencial e cria o registro normalmente

#### Scenario: Credencial rejeitada pelo validador do adapter é rejeitada
- **WHEN** um cliente envia `POST /channels` ou `PUT /channels/{id}` com
  uma credencial que o validador registrado para o `channelType`
  correspondente considera inválida
- **THEN** a API responde com erro de validação (HTTP 400), com os
  detalhes reportados pelo validador, e não cria nem altera nenhum
  registro

### Requirement: Recepção de webhook despachada pelo handler do ChannelType
O sistema SHALL expor uma rota HTTP genérica única para recepção de
webhooks de canal (`POST /webhooks/{channelId}`) que resolve o `Channel`
pelo identificador informado na rota, lê o `ChannelType` já persistido
nesse `Channel`, e despacha a requisição para o handler de webhook de
entrada registrado para esse `ChannelType`. Um `channelId` que não
corresponde a nenhum `Channel` cadastrado SHALL ser rejeitado com HTTP 404,
sem invocar nenhum handler. Um `ChannelType` sem handler de webhook
registrado SHALL ser rejeitado com HTTP 400.

#### Scenario: Webhook para canal existente com handler registrado é despachado
- **WHEN** uma requisição chega em `POST /webhooks/{channelId}` para um
  `channelId` que corresponde a um `Channel` cadastrado, cujo `ChannelType`
  tem um handler de webhook de entrada registrado
- **THEN** a requisição é despachada para o handler registrado desse
  `ChannelType`, com o `channelId` resolvido

#### Scenario: Webhook para channelId inexistente é rejeitado
- **WHEN** uma requisição chega em `POST /webhooks/{channelId}` para um
  `channelId` que não corresponde a nenhum `Channel` cadastrado
- **THEN** a API responde com HTTP 404, sem invocar nenhum handler

#### Scenario: Webhook para ChannelType sem handler registrado é rejeitado
- **WHEN** uma requisição chega em `POST /webhooks/{channelId}` para um
  `Channel` existente cujo `ChannelType` não tem nenhum handler de webhook
  de entrada registrado
- **THEN** a API responde com HTTP 400, sem invocar nenhum handler

### Requirement: Entrega da resposta do agente ao canal de origem
O sistema SHALL, ao receber uma push notification válida cuja task
concluída possui uma mensagem de resposta associada, resolver o `Channel`
de origem da `Session` envolvida, decifrar a credencial persistida, e
invocar o sender de entrega registrado para o `ChannelType` desse canal
com o texto da resposta e o identificador externo do contato de destino.

#### Scenario: Push notification com resposta invoca o sender do ChannelType correto
- **WHEN** uma push notification com token correto é recebida para uma
  task cujo estado terminal inclui uma mensagem de resposta, e existe um
  `IOutboundMessageSender` registrado para o `ChannelType` do `Channel`
  de origem da sessão
- **THEN** o sender registrado para esse `ChannelType` é invocado com o
  texto da resposta, o identificador externo do contato de destino e a
  credencial já decifrada do canal

#### Scenario: Push notification sem mensagem de resposta não invoca nenhum sender
- **WHEN** uma push notification com token correto é recebida para uma
  task cujo estado terminal não possui nenhuma mensagem de resposta
  associada
- **THEN** nenhum `IOutboundMessageSender` é invocado, e o registro do
  buffer de debounce é removido normalmente

### Requirement: URL de webhook computada por canal
O sistema SHALL expor, em toda resposta que representa um canal
(`ChannelResponse`), uma `webhookUrl` computada a partir do identificador
do canal e da URL pública configurada do processo, nunca persistida.

#### Scenario: Resposta de canal inclui webhookUrl computada
- **WHEN** um cliente consulta, cria, atualiza, ativa, desativa ou lista
  um canal
- **THEN** a resposta inclui `webhookUrl`, computada a partir do
  identificador do canal e da URL pública configurada do processo
</content>
