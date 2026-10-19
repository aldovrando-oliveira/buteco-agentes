# workers-concurrent-task-consumption Specification

## Purpose

Diz quantas tasks uma instância de `apps/workers` executa ao mesmo tempo, e por
que esse número é um só: o mesmo `N` define quantas mensagens o broker entrega à
instância e quantas ela executa, para nenhuma entrega ficar presa no processo
fora do alcance de outras instâncias. Cobre o valor padrão (declarado não
medido), a faixa aceita no boot e o `N = 1` como rollback exato do comportamento
anterior. Calibrar `N` e decidir réplicas por disponibilidade ficam fora (#48).

## Requirements

### Requirement: Uma instância executa até N tasks ao mesmo tempo
O consumidor da fila `agent-tasks` em `apps/workers` SHALL executar até `N` tasks
ao mesmo tempo no mesmo processo, em que `N` é o valor de
`TaskConsumer:MaxConcurrentExecutions`. O mesmo valor SHALL definir o número de
mensagens não confirmadas que o broker entrega à instância (prefetch) **e** o número
de execuções simultâneas do canal de tasks (concorrência de despacho). Os dois
SHALL NOT divergir: nenhuma entrega SHALL ficar retida no processo sem estar em
execução, fora do alcance de outras instâncias. A opção SHALL valer só para o canal
de tasks, e SHALL NOT alterar o consumidor da fila de indexação nem o publicador.

#### Scenario: Instância com N = 2 executa duas tasks e deixa a terceira na fila
- **WHEN** uma instância com `N = 2` está ativa e três tasks que demoram a terminar
  são publicadas em `agent-tasks`
- **THEN** exatamente duas tasks estão em `working` ao mesmo tempo, e a terceira
  continua pronta na fila (`messages_ready == 1`), disponível para outra instância

#### Scenario: Consumidor de indexação não é afetado
- **WHEN** `TaskConsumer:MaxConcurrentExecutions` está configurado com qualquer
  valor válido
- **THEN** o consumidor da fila de indexação continua processando uma mensagem por
  vez

### Requirement: Valor padrão de N declarado e não medido
Na ausência de configuração, `TaskConsumer:MaxConcurrentExecutions` SHALL valer
`4`. O valor SHALL ser documentado como **não medido**, com o gatilho de
recalibração (o `C` de pico de conversas delegando ao mesmo tempo) e o lugar em
que a calibração acontece (#48).

#### Scenario: Instância sem configuração usa o padrão
- **WHEN** `apps/workers` inicia sem `TaskConsumer:MaxConcurrentExecutions`
  configurado
- **THEN** a instância executa até quatro tasks ao mesmo tempo, e a linha de log
  da checagem de boot registra `4`

### Requirement: N fora da faixa válida reprova o boot
`apps/workers` SHALL recusar a inicialização quando
`TaskConsumer:MaxConcurrentExecutions` estiver fora de `[1, 32]`, com mensagem que
nomeia o valor encontrado, a faixa aceita e o motivo do teto. O valor SHALL NOT ser
corrigido automaticamente. Quando o valor estiver na faixa, a checagem SHALL
registrar uma linha de log com o valor em vigor.

#### Scenario: N = 0 reprova o boot
- **WHEN** `apps/workers` inicia com `TaskConsumer:MaxConcurrentExecutions = 0`
- **THEN** o processo falha a inicialização com um erro explícito que nomeia `0` e
  a faixa `[1, 32]`, sem consumir nenhuma mensagem

#### Scenario: N acima do teto reprova o boot
- **WHEN** `apps/workers` inicia com `TaskConsumer:MaxConcurrentExecutions = 33`
- **THEN** o processo falha a inicialização com um erro explícito que nomeia `33`,
  a faixa `[1, 32]` e o pool de conexões do Postgres como motivo do teto

#### Scenario: N válido registra a linha de sucesso
- **WHEN** `apps/workers` inicia com um valor de `N` dentro da faixa
- **THEN** o log de inicialização tem uma linha identificando a checagem e o valor
  de `N` em vigor

### Requirement: N = 1 reproduz o comportamento anterior
Com `TaskConsumer:MaxConcurrentExecutions = 1`, o consumidor de tasks SHALL se
comportar exatamente como antes desta capacidade: uma mensagem não confirmada e uma
execução por vez por instância, consumo serializado dentro da instância, e
delegação numa instância única expirando pelo timeout. Esse valor SHALL ser o
rollback declarado da capacidade.

#### Scenario: Instância com N = 1 serializa o consumo
- **WHEN** uma instância com `N = 1` recebe duas tasks publicadas em sequência
- **THEN** a segunda só começa depois que a primeira foi confirmada, na ordem de
  publicação
