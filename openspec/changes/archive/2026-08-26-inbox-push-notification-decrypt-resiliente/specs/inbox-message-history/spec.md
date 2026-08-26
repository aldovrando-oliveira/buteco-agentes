## MODIFIED Requirements

### Requirement: Status de entrega da mensagem de saída reflete sucesso ou falha do envio ao provedor
O sistema SHALL persistir, na `Message` de saída correspondente, o
resultado da tentativa de entrega ao canal de origem: sucesso quando a
resolução do canal, a decifragem de sua credencial e a chamada ao sender
de entrega retornarem sem lançar exceção, ou falha — com o motivo da
exceção — quando qualquer uma dessas três etapas lançar. Uma falha de
entrega SHALL continuar sendo registrada por log estruturado e SHALL
continuar não interrompendo o ciclo de disparo em andamento (ver
capability `inbox-message-orchestration`, Requirement "Conteúdo
bufferizado é removido ao final do ciclo de disparo").

Essa garantia cobre a execução dessas três etapas em si: nenhuma delas,
ao falhar, propaga a exceção para fora do fluxo de entrega. Não cobre
indisponibilidade de infraestrutura que também impeça a persistência
final do ciclo de disparo em si (ver capability
`inbox-message-orchestration`, mesmo Requirement citado acima) —
permanece sujeita à mesma garantia (ou ausência dela) que já se aplica
hoje a qualquer push notification aceita, com ou sem mensagem de
resposta associada.

#### Scenario: Envio bem-sucedido é persistido como enviado
- **WHEN** a resolução do canal de origem, a decifragem de sua credencial
  e o sender de entrega registrado para o `ChannelType` retornam sem
  lançar exceção
- **THEN** a `Message` de saída correspondente é persistida com status de
  entrega de sucesso

#### Scenario: Envio malsucedido é persistido como falha, com motivo, sem derrubar o ciclo de disparo
- **WHEN** o sender de entrega registrado para o `ChannelType` lança uma
  exceção
- **THEN** a `Message` de saída correspondente é persistida com status de
  entrega de falha e o motivo da exceção, o evento é registrado por log
  estruturado, e o ciclo de disparo em andamento (ver capability
  `inbox-message-orchestration`) continua até sua remoção normal, sem
  propagar a exceção

#### Scenario: Falha ao decifrar a credencial do canal é persistida como falha de entrega, sem derrubar o ciclo de disparo
- **WHEN** a credencial persistida do canal de origem não pode ser
  decifrada (ex.: chave de criptografia rotacionada, dado corrompido)
- **THEN** a `Message` de saída correspondente é persistida com status de
  entrega de falha e o motivo da exceção, o sender de entrega registrado
  para o `ChannelType` **não** é invocado, o evento é registrado por log
  estruturado, e o ciclo de disparo em andamento (ver capability
  `inbox-message-orchestration`) continua até sua remoção normal, sem
  propagar a exceção

#### Scenario: Falha ao resolver o canal de origem é persistida como falha de entrega, sem derrubar o ciclo de disparo
- **WHEN** a resolução do `Channel` de origem da `Session` envolvida (a
  consulta que localiza `Channel`/`Contact` a partir da `Session`) lança
  uma exceção
- **THEN** a `Message` de saída correspondente é persistida com status de
  entrega de falha e o motivo da exceção, nem a decifragem da credencial
  nem o sender de entrega registrado para o `ChannelType` são invocados,
  o evento é registrado por log estruturado, e o ciclo de disparo em
  andamento (ver capability `inbox-message-orchestration`) continua até
  sua remoção normal, sem propagar a exceção
