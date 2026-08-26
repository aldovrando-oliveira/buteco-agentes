# inbox-message-history Specification

## Purpose

TBD - defined by change inbox-mensagens-persistidas. Update Purpose after archive.

## Requirements

### Requirement: Mensagem de entrada persistida durante a ingestão
O sistema SHALL, ao ingerir uma mensagem normalizada de entrada (ver
capability `inbox-message-orchestration`, Requirement "Ingestão de mensagem
normalizada resolve Contact e Session"), persistir um registro de `Message`
associado à `Session` resolvida, com direção de entrada, o conteúdo
recebido, o tipo de conteúdo, o instante de recebimento e o identificador
externo da mensagem informado pelo adapter de origem. Quando o adapter de
origem indicar que a mensagem é de mídia (imagem, áudio ou documento) sem
texto acompanhando, o sistema SHALL persistir o tipo de conteúdo
correspondente e gravar um marcador textual no conteúdo, sem persistir o
conteúdo binário da mídia.

#### Scenario: Mensagem de entrada é persistida com os dados do adapter
- **WHEN** uma mensagem normalizada de entrada é ingerida, com conteúdo,
  instante e identificador externo informados
- **THEN** um registro de `Message` de entrada é persistido para a `Session`
  resolvida, com esse conteúdo, instante e identificador externo

#### Scenario: Mensagem de mídia sem texto grava marcador e tipo de conteúdo correspondente
- **WHEN** uma mensagem normalizada de entrada é ingerida, e o adapter de
  origem indica que essa mensagem é de mídia (imagem, áudio ou documento),
  sem texto acompanhando
- **THEN** o registro de `Message` de entrada persistido tem o tipo de
  conteúdo correspondente ao tipo de mídia informado, e o conteúdo gravado é
  um marcador textual, sem nenhum conteúdo binário persistido

#### Scenario: Mensagem só de texto grava tipo de conteúdo Text
- **WHEN** uma mensagem normalizada de entrada é ingerida sem nenhuma
  indicação de mídia por parte do adapter de origem
- **THEN** o registro de `Message` de entrada persistido tem tipo de
  conteúdo `Text`, com o texto recebido como conteúdo

### Requirement: Deduplicação de mensagem de entrada por identificador externo
O sistema SHALL rejeitar a persistência de uma segunda `Message` de entrada
com o mesmo identificador externo já persistido para a mesma `Session`,
sem propagar erro para o chamador e sem adicionar a mensagem duplicada ao
buffer de debounce da sessão.

#### Scenario: Webhook reentregue com o mesmo identificador externo não duplica mensagem
- **WHEN** uma mensagem normalizada de entrada é ingerida com um
  identificador externo que já corresponde a uma `Message` de entrada
  persistida para a mesma `Session`
- **THEN** nenhuma nova `Message` é persistida, o conteúdo já persistido
  permanece inalterado, e nenhuma mensagem é adicionada ao buffer de
  debounce por essa ingestão

### Requirement: Mensagem de saída persistida ao entregar resposta ao canal
O sistema SHALL, ao invocar o sender de entrega registrado para o
`ChannelType` do canal de origem (ver capability
`inbox-channel-adapter-plugin`, Requirement "Entrega da resposta do agente
ao canal de origem"), persistir um registro de `Message` associado à
`Session` envolvida, com direção de saída, o conteúdo da resposta, tipo de
conteúdo `Text` e o instante da tentativa de entrega — toda mensagem de
saída nesta fatia é textual.

#### Scenario: Resposta do agente entregue ao canal é persistida como mensagem de saída
- **WHEN** o sistema invoca o sender de entrega registrado para o
  `ChannelType` de uma sessão, com o texto da resposta do agente
- **THEN** um registro de `Message` de saída é persistido para essa
  `Session`, com esse conteúdo, tipo de conteúdo `Text` e o instante da
  tentativa

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

### Requirement: Estado de dispatch é espelhado nas mensagens de entrada agrupadas
O sistema SHALL manter, em cada `Message` de entrada que integra um ciclo de
disparo de debounce, um status de dispatch que reflete a transição de
estado desse ciclo — pendente enquanto bufferizando, em processamento
quando reivindicado para disparo, falhou quando o ciclo terminar sem que
nenhuma resposta venha a ser recebida (esgotamento das tentativas de envio
configuradas, rejeição síncrona do disparo, ou rejeição de protocolo), e
concluído quando a resposta correspondente for recebida e processada —
mesmo depois que o registro do ciclo de disparo (ver capability
`inbox-message-orchestration`, Requirement "Conteúdo bufferizado é removido
ao final do ciclo de disparo") tiver sido removido.

#### Scenario: Mensagens de um grupo em debounce mostram status pendente
- **WHEN** uma mensagem de entrada é adicionada ao buffer de debounce de uma
  `Session`, antes do disparo
- **THEN** a `Message` de entrada correspondente reflete status de dispatch
  pendente

#### Scenario: Mensagens de um grupo reivindicado para disparo mostram status em processamento
- **WHEN** o ciclo de disparo de debounce de uma `Session` é reivindicado
  para disparo
- **THEN** as `Message` de entrada desse grupo passam a refletir status de
  dispatch em processamento

#### Scenario: Mensagens de um grupo cujo disparo esgotou tentativas mostram status de falha
- **WHEN** o ciclo de disparo de debounce de uma `Session` esgota o número
  máximo de tentativas configurado sem sucesso
- **THEN** as `Message` de entrada desse grupo passam a refletir status de
  dispatch de falha, e esse status permanece consultável mesmo após o
  registro do ciclo de disparo ser removido

#### Scenario: Mensagens de um grupo cujo disparo termina por rejeição síncrona ou de protocolo mostram status de falha
- **WHEN** o ciclo de disparo de debounce de uma `Session` termina por
  rejeição síncrona do disparo ou por rejeição de protocolo, sem que
  nenhuma push notification venha a ser esperada
- **THEN** as `Message` de entrada desse grupo passam a refletir status de
  dispatch de falha, e esse status permanece consultável mesmo após o
  registro do ciclo de disparo ser removido

#### Scenario: Mensagens de um grupo cujo disparo foi concluído mostram status concluído
- **WHEN** a resposta correspondente a um ciclo de disparo de debounce é
  recebida e processada com sucesso
- **THEN** as `Message` de entrada desse grupo passam a refletir status de
  dispatch concluído, e esse status permanece consultável mesmo após o
  registro do ciclo de disparo ser removido

### Requirement: Consulta cronológica de mensagens de uma sessão
O sistema SHALL permitir, via `apps/inbox`, listar todas as mensagens (de
entrada e de saída) de uma `Session` específica, em ordem cronológica pelo
instante de cada mensagem. Os campos `Direction`, `ContentType`,
`DeliveryStatus` e `DispatchStatus` SHALL ser serializados como string
com o nome do valor do enum (ex. `"Failed"`), nunca como o inteiro
ordinal subjacente.

#### Scenario: Lista de mensagens de uma sessão com histórico
- **WHEN** um cliente consulta as mensagens de uma `Session` existente que já
  tem mensagens de entrada e/ou saída persistidas
- **THEN** a resposta traz todas as mensagens dessa `Session`, ordenadas
  cronologicamente pelo instante de cada uma, incluindo direção, conteúdo,
  tipo de conteúdo e, quando aplicável, status de entrega ou status de
  dispatch

#### Scenario: Sessão sem nenhuma mensagem retorna lista vazia
- **WHEN** um cliente consulta as mensagens de uma `Session` existente que
  ainda não tem nenhuma mensagem persistida
- **THEN** a resposta traz uma lista vazia, sem erro

#### Scenario: Consulta de mensagens de sessão inexistente retorna 404
- **WHEN** um cliente consulta as mensagens de um id de `Session` que não
  existe
- **THEN** a resposta é HTTP 404

#### Scenario: Enums de mensagem de entrada serializados como string
- **WHEN** um cliente consulta as mensagens de uma `Session` que inclui
  uma mensagem de entrada com `Direction: Inbound`, `ContentType: Text` e
  `DispatchStatus: Failed`
- **THEN** os campos `Direction`, `ContentType` e `DispatchStatus` na
  resposta JSON são as strings `"Inbound"`, `"Text"` e `"Failed"`,
  respectivamente — nenhum dos três é um valor numérico

#### Scenario: Enums de mensagem de saída serializados como string
- **WHEN** um cliente consulta as mensagens de uma `Session` que inclui
  uma mensagem de saída com `Direction: Outbound` e
  `DeliveryStatus: Sent`
- **THEN** os campos `Direction` e `DeliveryStatus` na resposta JSON são
  as strings `"Outbound"` e `"Sent"`, respectivamente — nenhum dos dois
  é um valor numérico
