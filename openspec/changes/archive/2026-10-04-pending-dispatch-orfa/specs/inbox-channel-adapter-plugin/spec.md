## MODIFIED Requirements

### Requirement: Entrega da resposta do agente ao canal de origem
O sistema SHALL, ao processar o desfecho de uma task concluída que possui uma
mensagem de resposta associada — recebido por push notification válida ou pela
reconciliação de um ciclo de disparo em processamento (ver capability
`inbox-message-orchestration`) —, resolver o `Channel` de origem da `Session`
envolvida, decifrar a credencial persistida, e invocar o sender de entrega
registrado para o `ChannelType` desse canal com o texto da resposta e o
identificador externo do contato de destino. O aviso de falha de um desfecho
sem resposta (ver capability `inbox-message-orchestration`, Requirement
"Desfecho sem resposta avisa a conversa") SHALL ser entregue pelo mesmo
caminho.

#### Scenario: Push notification com resposta invoca o sender do ChannelType correto
- **WHEN** uma push notification com token correto é recebida para uma
  task cujo estado terminal inclui uma mensagem de resposta, e existe um
  `IOutboundMessageSender` registrado para o `ChannelType` do `Channel`
  de origem da sessão
- **THEN** o sender registrado para esse `ChannelType` é invocado com o
  texto da resposta, o identificador externo do contato de destino e a
  credencial já decifrada do canal

#### Scenario: Reconciliação de task concluída com resposta invoca o sender do ChannelType correto
- **WHEN** a reconciliação resolve um ciclo de disparo cuja task está
  concluída com mensagem de resposta, e existe um `IOutboundMessageSender`
  registrado para o `ChannelType` do `Channel` de origem da sessão
- **THEN** o sender registrado para esse `ChannelType` é invocado com o
  texto da resposta, o identificador externo do contato de destino e a
  credencial já decifrada do canal

#### Scenario: Push notification de task concluída sem mensagem de resposta não invoca nenhum sender
- **WHEN** uma push notification com token correto é recebida para uma
  task concluída que não possui nenhuma mensagem de resposta associada
- **THEN** nenhum `IOutboundMessageSender` é invocado, e o registro do
  buffer de debounce é removido normalmente

#### Scenario: Push notification de task em falha invoca o sender com o aviso
- **WHEN** uma push notification com token correto é recebida para uma
  task em estado terminal que não é concluído
- **THEN** o sender registrado para o `ChannelType` do `Channel` de origem é
  invocado com o texto do aviso de falha, o identificador externo do contato
  de destino e a credencial já decifrada do canal
