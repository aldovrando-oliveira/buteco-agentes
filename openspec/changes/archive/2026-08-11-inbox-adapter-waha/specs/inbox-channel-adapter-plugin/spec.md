## ADDED Requirements

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
