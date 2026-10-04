## MODIFIED Requirements

### Requirement: Mensagem de saída persistida ao entregar resposta ao canal
O sistema SHALL, ao invocar o sender de entrega registrado para o
`ChannelType` do canal de origem (ver capability
`inbox-channel-adapter-plugin`, Requirement "Entrega da resposta do agente
ao canal de origem"), persistir um registro de `Message` associado à
`Session` envolvida, com direção de saída, o conteúdo entregue, tipo de
conteúdo `Text` e o instante da tentativa de entrega — toda mensagem de
saída nesta fatia é textual. O conteúdo entregue é a resposta do agente ou,
num desfecho sem resposta por falha, o aviso de falha (ver capability
`inbox-message-orchestration`, Requirement "Desfecho sem resposta avisa a
conversa"); os dois são persistidos da mesma forma.

#### Scenario: Resposta do agente entregue ao canal é persistida como mensagem de saída
- **WHEN** o sistema invoca o sender de entrega registrado para o
  `ChannelType` de uma sessão, com o texto da resposta do agente
- **THEN** um registro de `Message` de saída é persistido para essa
  `Session`, com esse conteúdo, tipo de conteúdo `Text` e o instante da
  tentativa

#### Scenario: Aviso de falha entregue ao canal é persistido como mensagem de saída
- **WHEN** o sistema invoca o sender de entrega registrado para o
  `ChannelType` de uma sessão, com o texto do aviso de falha
- **THEN** um registro de `Message` de saída é persistido para essa
  `Session`, com o texto do aviso, tipo de conteúdo `Text`, o instante da
  tentativa e o status de entrega correspondente ao resultado

### Requirement: Estado de dispatch é espelhado nas mensagens de entrada agrupadas
O sistema SHALL manter, em cada `Message` de entrada que integra um ciclo de
disparo de debounce, um status de dispatch que reflete a transição de
estado desse ciclo — pendente enquanto bufferizando, em processamento
quando reivindicado para disparo, falhou quando o ciclo terminar sem que
nenhuma resposta venha a ser recebida (esgotamento das tentativas de envio
configuradas, rejeição síncrona do disparo, rejeição de protocolo, task em
estado terminal que não é concluído — recebida por push notification ou pela
reconciliação —, ou encerramento por idade de ciclo sem identificador de
task), e concluído quando a task correspondente terminar concluída e for
processada, com ou sem mensagem de resposta — mesmo depois que o registro do
ciclo de disparo (ver capability `inbox-message-orchestration`, Requirement
"Conteúdo bufferizado é removido ao final do ciclo de disparo") tiver sido
removido.

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

#### Scenario: Mensagens de um grupo cuja task terminou em falha mostram status de falha
- **WHEN** a task de um ciclo de disparo de debounce termina num estado
  terminal que não é concluído, e esse desfecho é processado por push
  notification ou pela reconciliação
- **THEN** as `Message` de entrada desse grupo passam a refletir status de
  dispatch de falha, e esse status permanece consultável mesmo após o
  registro do ciclo de disparo ser removido

#### Scenario: Mensagens de um grupo encerrado por idade sem identificador de task mostram status de falha
- **WHEN** um ciclo de disparo sem identificador de task é encerrado por idade
- **THEN** as `Message` de entrada desse grupo passam a refletir status de
  dispatch de falha, e esse status permanece consultável mesmo após o
  registro do ciclo de disparo ser removido

#### Scenario: Mensagens de um grupo cujo disparo foi concluído mostram status concluído
- **WHEN** a task correspondente a um ciclo de disparo de debounce termina
  concluída, com ou sem mensagem de resposta, e esse desfecho é processado
  por push notification ou pela reconciliação
- **THEN** as `Message` de entrada desse grupo passam a refletir status de
  dispatch concluído, e esse status permanece consultável mesmo após o
  registro do ciclo de disparo ser removido
