## MODIFIED Requirements

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
