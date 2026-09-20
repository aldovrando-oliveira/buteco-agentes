## MODIFIED Requirements

### Requirement: Espera pela task delegada com timeout e degradação graciosa
A tool de delegação SHALL aguardar a task do Target chegar a um estado
terminal (`Completed`, `Failed`, `Rejected` ou `Canceled`) antes de
retornar um resultado ao LLM do Source, respeitando um timeout
configurado. Se o timeout expirar, ou se o Target chegar a um estado
terminal de falha, a tool SHALL retornar um resultado de falha — a task
do Source SHALL permanecer seu fluxo normal, nunca falhando por causa
disso.

Em **qualquer** dos dois caminhos de falha, a tool SHALL registrar um
registro de diagnóstico que identifique a delegação inteira — o
identificador da task do Source, o do Target, e os identificadores dos
agentes Source e Target — e que carregue o **último estado observado** da
task do Target junto com o instante dessa observação. O registro SHALL
nomear esse campo pelo que ele é: o último estado que a tool chegou a ler,
nunca "o estado no instante da desistência", porque entre a última leitura
bem-sucedida e a desistência cabe um intervalo de poll. Quando a tool
desistir sem nunca ter conseguido ler a task do Target, o registro SHALL
dizer isso explicitamente, e não SHALL apresentá-lo como um estado.

O registro SHALL expor o estado como o valor do estado da task, não como
texto livre reinterpretado, de forma que um consumidor possa separar
`Submitted` (task publicada e nunca consumida) de `Working` (task
consumida e ainda em execução) sem interpretar a redação da mensagem.

#### Scenario: Delegação bem-sucedida devolve o resultado ao Source
- **WHEN** a task delegada ao Target chega a `Completed` dentro do
  timeout
- **THEN** a tool de delegação retorna o conteúdo produzido pelo Target
  como resultado da chamada, e o LLM do Source continua o turno com
  esse resultado disponível

#### Scenario: Timeout expira sem a task do Target concluir
- **WHEN** a task delegada ao Target não chega a nenhum estado terminal
  antes do timeout configurado expirar
- **THEN** a tool de delegação retorna um resultado de falha para o LLM
  do Source, e a task do Source não é marcada como `failed` por causa
  disso

#### Scenario: Task do Target falha durante o processamento
- **WHEN** a task delegada ao Target chega a `Failed`
- **THEN** a tool de delegação retorna um resultado de falha para o LLM
  do Source, tratado pelo LLM como qualquer outra falha de tool, sem
  tratamento especial

#### Scenario: Expiração com o Target nunca consumido é distinguível de Target em execução
- **WHEN** o timeout expira e o último estado lido da task do Target era
  `Submitted` — publicada e nunca consumida por instância nenhuma
- **THEN** o registro de diagnóstico da desistência carrega `Submitted`
  como último estado observado, distinguível sem ambiguidade do registro
  produzido quando o último estado lido era `Working`

#### Scenario: Expiração com o Target em execução é distinguível de Target nunca consumido
- **WHEN** o timeout expira e o último estado lido da task do Target era
  `Working` — consumida por alguma instância e ainda em execução
- **THEN** o registro de diagnóstico da desistência carrega `Working`
  como último estado observado, e os dois registros diferem por esse
  valor, não pela redação da mensagem

#### Scenario: A desistência identifica a delegação inteira, não só a task do Target
- **WHEN** a tool de delegação devolve um resultado de falha ao LLM do
  Source, por timeout ou por estado terminal de falha do Target
- **THEN** o registro de diagnóstico carrega o identificador da task do
  Source, o da task do Target e os identificadores dos agentes Source e
  Target, de forma que a falha possa ser ligada ao registro que a
  execução do Target produziu por conta própria

#### Scenario: Desistência sem nenhuma leitura bem-sucedida do Target
- **WHEN** a tool de delegação desiste sem ter conseguido ler a task do
  Target nenhuma vez
- **THEN** o registro de diagnóstico declara a ausência de observação, em
  vez de apresentar um estado que nunca foi lido
