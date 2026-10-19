## MODIFIED Requirements

### Requirement: Parada do worker com execução em voo
`apps/workers` SHALL, ao parar com uma ou mais execuções em andamento, deixar de
receber jobs do broker, e então cancelar **todas** as execuções **antes** de fechar
o canal e a conexão com o broker. O cancelamento SHALL alcançar as execuções ao
mesmo tempo, de modo que o tempo de parada seja o da execução mais lenta a
observar o cancelamento, e não a soma delas. A parada SHALL retornar sem esperar os
prazos de fechamento do canal e da conexão. As execuções interrompidas SHALL
terminar enquanto o host ainda está ativo. Cada task interrompida SHALL permanecer
em `working`, sem que `failed` seja gravado por causa da parada, e cada job SHALL
ser devolvido explicitamente à fila para reentrega, sem ser reentregue ao worker
que está parando. A parada SHALL NOT ser registrada em log como erro de execução.

#### Scenario: Parada com execução esperando o provedor não espera os prazos de fechamento
- **WHEN** o worker é parado enquanto uma execução espera a resposta do provedor
  de LLM, observando o cancelamento
- **THEN** a parada retorna sem lançar exceção e sem esperar os prazos de
  fechamento do canal e da conexão, e a execução recebe o cancelamento antes de o
  canal ser fechado

#### Scenario: Parada com execução esperando uma tool não espera os prazos de fechamento
- **WHEN** o worker é parado enquanto uma execução espera uma tool que observa o
  cancelamento, como a espera de uma delegação
- **THEN** a parada retorna sem lançar exceção e sem esperar os prazos de
  fechamento do canal e da conexão, e a execução não continua como se a tool
  tivesse falhado

#### Scenario: Parada com várias execuções em voo cabe no prazo do compose
- **WHEN** um worker com `N = 2` é parado com duas execuções em voo, ambas
  esperando o provedor de LLM e observando o cancelamento
- **THEN** a parada retorna em menos de 10 s, as duas tasks continuam em
  `working`, os dois jobs estão de novo na fila `agent-tasks`, e nenhum lock de
  contexto de conversa continua em posse

#### Scenario: Worker que está parando não recebe o job de volta
- **WHEN** a execução em voo recebe o cancelamento da parada
- **THEN** o worker já não está registrado como consumidor da fila `agent-tasks`,
  e o job devolvido não é entregue de novo a ele

#### Scenario: Task interrompida pela parada fica em working e o job volta à fila
- **WHEN** uma execução é interrompida pela parada do worker
- **THEN** a task continua em `working` no store durável, nenhum `failed` é
  gravado, o job está de novo na fila `agent-tasks`, e o lock de contexto da
  conversa foi liberado

#### Scenario: Job devolvido pela parada é executado na reentrega
- **WHEN** o job de uma task interrompida pela parada é entregue de novo a um
  worker
- **THEN** a task é executada a partir de `working` e chega a um estado terminal
