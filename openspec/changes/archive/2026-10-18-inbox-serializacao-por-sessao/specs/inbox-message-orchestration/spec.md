## ADDED Requirements

### Requirement: Disparo do debounce é serializado por sessão
O sistema SHALL NOT disparar o buffer de debounce de uma `Session` enquanto
existir, para a mesma `Session`, um ciclo de disparo anterior ainda em
processamento (`Dispatching`), e SHALL aplicar essa condição tanto na seleção
dos candidatos do ciclo de varredura quanto na releitura que precede a
reivindicação do buffer. As mensagens recebidas para essa `Session` enquanto o
ciclo anterior estiver em processamento SHALL continuar sendo acumuladas no
buffer em aberto, e SHALL ser enviadas num único `SendMessage`, na ordem de
chegada, no primeiro ciclo de varredura em que o ciclo anterior já tiver sido
encerrado (por push notification, reconciliação ou desfecho de falha) e a
janela de debounce estiver expirada. A serialização SHALL valer com qualquer
número de instâncias de `apps/inbox`, sem impedir que um buffer de outra
`Session` seja disparado no mesmo ciclo.

A espera SHALL ter teto: um ciclo em processamento cuja última mensagem é mais
velha que o teto configurado (padrão de produção: 10 minutos) SHALL deixar de
impedir o disparo do buffer seguinte da mesma `Session`, sem que esse ciclo
antigo seja alterado. Um teto configurado com valor menor ou igual a zero, ou
menor ou igual à janela de debounce configurada, SHALL impedir a inicialização
de `apps/inbox`, com mensagem que nomeia a chave e o valor recebido (e, no
segundo caso, a janela vigente), em vez de ser corrigido automaticamente.

#### Scenario: Mensagens recebidas com o ciclo anterior em processamento saem num único disparo depois dele
- **WHEN** o buffer de uma `Session` é disparado e o ciclo fica em
  processamento, e depois duas mensagens chegam para a mesma `Session`, fora da
  janela de debounce
- **THEN** nenhum novo `SendMessage` é disparado para essa `Session` enquanto o
  ciclo anterior estiver em processamento, e, depois de ele ser encerrado por
  push notification, exatamente um novo `SendMessage` é disparado, contendo as
  duas mensagens na ordem em que chegaram

#### Scenario: Ciclo em processamento de outra Session não impede o disparo
- **WHEN** uma `Session` tem um ciclo de disparo em processamento e outra
  `Session` tem um buffer cuja janela de debounce já expirou
- **THEN** o buffer da segunda `Session` é disparado normalmente no ciclo de
  varredura

#### Scenario: Ciclo em processamento além do teto deixa de impedir o disparo
- **WHEN** uma `Session` tem um ciclo de disparo em processamento cuja última
  mensagem é mais velha que o teto configurado, e um buffer cuja janela de
  debounce já expirou
- **THEN** o buffer é disparado, e o ciclo antigo continua em processamento,
  inalterado, à espera da reconciliação

#### Scenario: Ciclo em processamento dentro do teto continua impedindo o disparo
- **WHEN** uma `Session` tem um ciclo de disparo em processamento cuja última
  mensagem é mais nova que o teto configurado, e um buffer cuja janela de
  debounce já expirou
- **THEN** nenhum `SendMessage` é disparado para esse buffer

#### Scenario: Teto inválido impede a inicialização
- **WHEN** `apps/inbox` inicia com o teto de serialização configurado com valor
  menor ou igual a zero
- **THEN** a inicialização falha, com mensagem que nomeia a chave e o valor
  recebido

#### Scenario: Teto que não excede a janela de debounce impede a inicialização
- **WHEN** `apps/inbox` inicia com o teto de serialização configurado com valor
  positivo, mas menor ou igual à janela de debounce configurada
- **THEN** a inicialização falha, com mensagem que nomeia a chave, o valor
  recebido e a janela vigente

#### Scenario: Teto maior que a janela de debounce permite a inicialização
- **WHEN** `apps/inbox` inicia com o teto de serialização configurado com valor
  maior que a janela de debounce configurada
- **THEN** a inicialização prossegue, e o teto conferido é registrado em log

#### Scenario: Segunda mensagem de conversa com execução longa não falha no round-trip real
- **WHEN** duas instâncias de `apps/workers` consomem a fila, a primeira
  mensagem de uma `Session` gera uma execução mais longa que o tempo de espera
  do lock de contexto do worker, e uma segunda mensagem chega para a mesma
  `Session` enquanto a primeira execução está em andamento
- **THEN** as duas tasks terminam `Completed`, as duas respostas são entregues
  ao canal de origem, e nenhum aviso de falha é entregue

## MODIFIED Requirements

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
  ocorre e o ciclo dele é encerrado, e só depois uma nova mensagem chega para
  a mesma `Session`
- **THEN** dois `SendMessage` distintos são disparados, um para cada
  mensagem, e o segundo só depois do encerramento do ciclo do primeiro
