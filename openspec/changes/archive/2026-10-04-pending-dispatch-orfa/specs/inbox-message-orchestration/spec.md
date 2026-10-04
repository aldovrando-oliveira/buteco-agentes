## ADDED Requirements

### Requirement: Parada de apps/inbox não interrompe o envio de um disparo já reivindicado
O sistema SHALL executar o trecho de um ciclo de disparo que vai do registro da
reivindicação até o registro do identificador da task — a resolução do canal, o
envio do `SendMessage` e o tratamento da resposta — com um prazo próprio,
independente do sinal de parada de `apps/inbox`. A parada SHALL aguardar esse
trecho terminar. O prazo SHALL ser maior que o timeout do envio do disparo e
menor que o tempo que o ambiente de execução concede à parada antes de encerrar o
processo à força.

Nenhum ciclo de disparo SHALL ser reivindicado depois do pedido de parada de
`apps/inbox`, nem para disparo nem para reconciliação, ainda que o serviço que o
reivindicaria só seja parado depois de outros componentes do processo.

A entrega ao canal de origem que a reconciliação já estiver fazendo quando a
parada é pedida SHALL ser aguardada até o mesmo prazo, contado do pedido de
parada, e cancelada depois dele. Nesse caso o ciclo permanece reivindicado e em
processamento, e é reconciliado de novo no boot seguinte.

#### Scenario: Parada com envio em voo grava o identificador da task
- **WHEN** `apps/inbox` recebe o sinal de parada enquanto o `SendMessage` de um
  ciclo de disparo reivindicado está em andamento, e `apps/api` responde dentro
  do prazo
- **THEN** a parada aguarda a resposta, o identificador da task é registrado no
  ciclo de disparo, e o ciclo permanece em processamento à espera da push
  notification

#### Scenario: Nenhum disparo é reivindicado depois do pedido de parada
- **WHEN** a parada de `apps/inbox` é pedida e, enquanto outros componentes ainda
  terminam o que tinham em voo, a janela de debounce de um ciclo pendente vence
- **THEN** esse ciclo não é reivindicado nem enviado, e permanece pendente para o
  boot seguinte

#### Scenario: Entrega em voo na reconciliação é aguardada até o prazo
- **WHEN** a parada de `apps/inbox` é pedida enquanto a reconciliação entrega ao
  canal de origem o desfecho de um ciclo, e a entrega termina dentro do prazo
- **THEN** a parada aguarda a entrega, e o ciclo é removido normalmente

#### Scenario: Entrega em voo na reconciliação além do prazo é cancelada
- **WHEN** a parada de `apps/inbox` é pedida enquanto a reconciliação entrega ao
  canal de origem, e a entrega não termina dentro do prazo
- **THEN** a entrega é cancelada, a parada termina, e o ciclo permanece
  reivindicado e em processamento para o boot seguinte

#### Scenario: Parada com apps/api sem resposta espera no máximo o prazo
- **WHEN** `apps/inbox` recebe o sinal de parada enquanto o `SendMessage` de um
  ciclo de disparo reivindicado está em andamento, e `apps/api` não responde
- **THEN** a parada termina em no máximo o prazo do trecho


### Requirement: Disparo em processamento cuja task já terminou é reconciliado pelo estado da task
O sistema SHALL varrer periodicamente, em `apps/inbox`, os ciclos de disparo em
processamento que já têm o identificador da task registrado, e SHALL consultar o
estado dessa task em `apps/api` pelo protocolo A2A. Quando a task estiver em
estado terminal há mais tempo que uma carência, o sistema SHALL resolver o ciclo
de disparo com exatamente o mesmo processamento aplicado a uma push notification
válida para essa task, sem nenhuma semântica própria da reconciliação.

A carência SHALL ser maior que o tempo máximo em que uma push notification para a
mesma task ainda pode estar sendo processada, que é a soma do timeout do webhook
em `apps/workers` com o timeout do envio ao canal de origem.

A idade do ciclo de disparo SHALL NOT decidir a reconciliação de um ciclo com
identificador de task: a decisão é o estado terminal da task.

Antes de entregar qualquer conteúdo ao canal de origem, o sistema SHALL reivindicar
o ciclo de disparo de forma que apenas uma reivindicação concorrente vença,
entre instâncias de `apps/inbox`, e que uma push notification para o mesmo ciclo
recebida depois da reivindicação seja rejeitada. Uma reivindicação SHALL valer
por um prazo de posse, durante o qual nenhuma instância reivindica o mesmo ciclo
de novo — inclusive uma que leia o ciclo depois de a reivindicação ter sido
gravada. Vencido o prazo com o ciclo ainda em processamento, o ciclo SHALL voltar
a ser reivindicável.

#### Scenario: Task concluída com resposta, sem push notification recebida, tem a resposta entregue
- **WHEN** um ciclo de disparo está em processamento com o identificador de uma
  task que está em estado concluído, com mensagem de resposta, há mais tempo que
  a carência, e nenhuma push notification válida foi recebida
- **THEN** a resposta é entregue ao canal de origem, as mensagens de entrada do
  grupo refletem status de dispatch concluído, e o registro do ciclo de disparo é
  removido

#### Scenario: Task terminal em falha, sem push notification recebida, gera o aviso de falha
- **WHEN** um ciclo de disparo está em processamento com o identificador de uma
  task em estado terminal que não é concluído, há mais tempo que a carência
- **THEN** o aviso de falha é entregue ao canal de origem, as mensagens de entrada
  do grupo refletem status de dispatch de falha, e o registro do ciclo de disparo
  é removido

#### Scenario: Task terminal sem carimbo do estado terminal espera a carência a partir da primeira observação
- **WHEN** a consulta de um ciclo de disparo em processamento devolve uma task em
  estado terminal sem o instante da transição para esse estado
- **THEN** o ciclo não é reconciliado na varredura em que a task é vista terminal
  pela primeira vez, e é reconciliado na primeira varredura em que a carência,
  contada dessa primeira observação, já tiver passado

#### Scenario: Task terminal dentro da carência não é reconciliada
- **WHEN** um ciclo de disparo está em processamento com o identificador de uma
  task que entrou em estado terminal há menos tempo que a carência
- **THEN** o ciclo de disparo não é alterado nesse ciclo de varredura

#### Scenario: Task ainda não terminal não é reconciliada, por mais velha que seja
- **WHEN** um ciclo de disparo está em processamento com o identificador de uma
  task que está em estado não-terminal, qualquer que seja a idade do ciclo
- **THEN** o ciclo de disparo não é alterado, nada é entregue ao canal de origem,
  e o ciclo volta a ser consultado na varredura seguinte

#### Scenario: Task não encontrada ou consulta que falha não altera o ciclo
- **WHEN** a consulta do estado da task de um ciclo de disparo em processamento
  responde que a task não existe, ou falha
- **THEN** o ciclo de disparo não é alterado, o evento é registrado por log
  estruturado, e os demais ciclos da mesma varredura continuam sendo processados

#### Scenario: Duas instâncias reconciliando o mesmo ciclo entregam uma única vez
- **WHEN** duas instâncias de `apps/inbox` reconciliam ao mesmo tempo o mesmo ciclo
  de disparo cuja task está terminal além da carência
- **THEN** o conteúdo é entregue ao canal de origem uma única vez

#### Scenario: Instância que lê o ciclo depois da reivindicação não o reivindica de novo
- **WHEN** uma instância reivindicou um ciclo de disparo e ainda está entregando o
  desfecho ao canal de origem, e outra instância consulta o mesmo ciclo dentro do
  prazo de posse
- **THEN** a segunda instância não reivindica o ciclo nem entrega nada

#### Scenario: Ciclo reivindicado e não resolvido volta a ser reivindicável depois do prazo de posse
- **WHEN** um ciclo de disparo foi reivindicado pela reconciliação, continua em
  processamento, e o prazo de posse venceu
- **THEN** o ciclo é reivindicado e resolvido de novo

#### Scenario: Push notification recebida depois da reivindicação é rejeitada
- **WHEN** a reconciliação reivindicou um ciclo de disparo, e uma push
  notification para a mesma task chega em seguida com o token gerado no disparo
- **THEN** a push notification é rejeitada como não autorizada, e nada é entregue
  em dobro

#### Scenario: Push notification que perde a corrida para a reconciliação não responde erro
- **WHEN** uma push notification com token correto é aceita, e a reconciliação
  reivindica o mesmo ciclo de disparo antes de o processamento da push
  notification gravar o resultado
- **THEN** a push notification é respondida com sucesso, o evento é registrado
  por log estruturado de severidade de aviso, sem severidade de erro, e o
  resultado persistido é o da reconciliação

#### Scenario: Push notification que chegou antes do registro da task é recuperada
- **WHEN** a push notification de uma task chega antes de o identificador dessa
  task estar registrado no ciclo de disparo, é rejeitada, e o identificador é
  registrado em seguida
- **THEN** a reconciliação resolve o ciclo de disparo pelo estado da task depois da
  carência

### Requirement: Disparo em processamento sem identificador de task além do limite é encerrado como perda
O sistema SHALL encerrar como perda definitiva todo ciclo de disparo em
processamento que não tenha o identificador da task registrado e cuja última
mensagem tenha sido recebida há mais tempo que um limite. O encerramento SHALL ter
a mesma consequência do esgotamento das tentativas de envio — registro por log
estruturado de severidade mais alta, aviso de falha à conversa, mensagens de
entrada com status de dispatch de falha, e remoção do registro do ciclo — e SHALL
NOT reapresentar o ciclo para um novo disparo.

Este requisito cobre o ciclo de disparo que ficou sem identificador de task por
encerramento abrupto do processo ou por falha inesperada entre a reivindicação e
o registro da task; a parada normal é coberta pelo Requirement "Parada de
apps/inbox não interrompe o envio de um disparo já reivindicado".

O limite SHALL ser maior que o intervalo máximo entre a última mensagem de um
ciclo e o registro do identificador da task num disparo que esteja em andamento: a
janela de debounce, mais o intervalo de varredura, mais o timeout do envio do
disparo, com folga para uma varredura com muitos candidatos.

#### Scenario: Disparo interrompido antes do registro da task é encerrado como perda
- **WHEN** um ciclo de disparo está em processamento, sem identificador de task, e
  sua última mensagem foi recebida há mais tempo que o limite
- **THEN** o evento é registrado por log estruturado de severidade mais alta, o
  aviso de falha é entregue ao canal de origem, as mensagens de entrada do grupo
  refletem status de dispatch de falha, o registro do ciclo é removido, e nenhum
  novo envio é feito para esse ciclo

#### Scenario: Disparo sem identificador de task dentro do limite não é alterado
- **WHEN** um ciclo de disparo está em processamento, sem identificador de task, e
  sua última mensagem foi recebida há menos tempo que o limite
- **THEN** o ciclo de disparo não é alterado

### Requirement: Desfecho sem resposta avisa a conversa
O sistema SHALL entregar ao canal de origem um aviso de falha, com o texto fixo
«Não consegui responder agora. Pode tentar de novo em instantes?», sempre que um ciclo de disparo terminar sem que nenhuma resposta do agente venha a
ser entregue por falha: task em estado terminal que não é concluído (recebida por
push notification ou pela reconciliação), rejeição síncrona do disparo — qualquer
que seja o motivo da rejeição, inclusive agente inativo —, rejeição de protocolo, esgotamento das tentativas de envio, e encerramento por idade de
ciclo sem identificador de task. A entrega do aviso SHALL usar o mesmo caminho de
entrega da resposta do agente, com as mesmas garantias de degradação: a falha ao
entregar o aviso é persistida e registrada, e não interrompe o encerramento do
ciclo.

Uma task concluída sem mensagem de resposta SHALL NOT gerar aviso.

#### Scenario: Push notification de task em falha entrega o aviso
- **WHEN** uma push notification com token correto é recebida para uma task em
  estado de falha
- **THEN** o aviso de falha é entregue ao canal de origem antes de o registro do
  ciclo de disparo ser removido

#### Scenario: Rejeição síncrona do disparo entrega o aviso
- **WHEN** o `SendMessage` de um ciclo de disparo retorna uma task já rejeitada
- **THEN** o aviso de falha é entregue ao canal de origem, e o registro do ciclo de
  disparo é removido

#### Scenario: Rejeição de protocolo entrega o aviso
- **WHEN** o `SendMessage` de um ciclo de disparo é rejeitado no nível de protocolo
  A2A
- **THEN** o aviso de falha é entregue ao canal de origem, e o registro do ciclo de
  disparo é removido

#### Scenario: Esgotamento das tentativas entrega o aviso
- **WHEN** um ciclo de disparo esgota o número máximo de tentativas de envio
- **THEN** o aviso de falha é entregue ao canal de origem, e o registro do ciclo de
  disparo é removido

#### Scenario: Falha de transporte ainda reintentável não entrega aviso
- **WHEN** um envio de disparo falha por transporte e ainda resta tentativa
- **THEN** nenhum aviso é entregue, e o ciclo volta a pendente como hoje

#### Scenario: Task concluída sem resposta não entrega aviso
- **WHEN** uma push notification com token correto é recebida para uma task
  concluída sem mensagem de resposta
- **THEN** nenhum conteúdo é entregue ao canal de origem

#### Scenario: Falha ao entregar o aviso não impede o encerramento do ciclo
- **WHEN** a entrega do aviso de falha ao canal de origem lança uma exceção
- **THEN** a falha é persistida na mensagem de saída do aviso e registrada por log
  estruturado, e o registro do ciclo de disparo é removido normalmente

## MODIFIED Requirements

### Requirement: Conteúdo bufferizado é removido ao final do ciclo de disparo
O sistema SHALL remover o registro do buffer de debounce (incluindo o
conteúdo das mensagens e o token de push notification) assim que esse
ciclo de disparo alcançar um estado terminal — seja por push notification
recebida e validada, seja por rejeição síncrona, seja por falha de
transporte, seja pela reconciliação de um ciclo cuja task já terminou, seja
pelo encerramento por idade de um ciclo sem identificador de task — sem
reter esse conteúdo além do necessário para o disparo.
Quando o estado terminal for alcançado por uma push notification válida, ou
pela reconciliação, e a task possuir uma mensagem de resposta associada, o
sistema SHALL, antes de remover o registro, entregar essa resposta ao canal
de origem (ver capability `inbox-channel-adapter-plugin`, Requirement
"Entrega da resposta do agente ao canal de origem"). Quando o estado
terminal for um desfecho sem resposta por falha, o sistema SHALL, antes de
remover o registro, entregar o aviso de falha (ver Requirement "Desfecho sem
resposta avisa a conversa"). Uma vez que uma push notification tenha sido
aceita por ter o token correto, o sistema SHALL
completar a entrega ao canal de origem (quando aplicável) e a
persistência local do resultado — atualização de status de dispatch e
remoção do registro do buffer — independentemente de o chamador que
enviou a push notification já ter encerrado a conexão ou desistido de
esperar a resposta dentro do seu próprio timeout.

#### Scenario: Buffer é removido após push notification válida
- **WHEN** uma push notification com token correto é recebida para um
  disparo em andamento
- **THEN** a resposta do agente, quando presente, ou o aviso de falha, quando
  a task terminou em falha, é entregue ao canal de origem antes de o registro
  correspondente do buffer de debounce ser removido

#### Scenario: Buffer é removido após rejeição síncrona ou falha de transporte
- **WHEN** um disparo termina por rejeição síncrona ou por falha de
  transporte
- **THEN** o registro correspondente do buffer de debounce é removido

#### Scenario: Buffer é removido após a reconciliação ou o encerramento por idade
- **WHEN** um disparo é resolvido pela reconciliação de uma task terminal, ou
  encerrado por idade sem identificador de task
- **THEN** o registro correspondente do buffer de debounce é removido

#### Scenario: Processamento completa mesmo com o chamador desconectado
- **WHEN** uma push notification com token correto é aceita, e o
  chamador que a enviou encerra a conexão (por exemplo, por atingir seu
  próprio timeout) antes de receber a resposta HTTP deste endpoint, mas
  depois de o token já ter sido validado
- **THEN** a entrega ao canal de origem (quando aplicável), a
  atualização do status de dispatch das mensagens do grupo para
  o status correspondente ao desfecho e a remoção do registro do buffer de
  debounce completam normalmente, sem serem interrompidas pela desconexão do
  chamador
