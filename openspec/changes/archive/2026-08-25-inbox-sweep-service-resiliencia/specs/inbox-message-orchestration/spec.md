## ADDED Requirements

### Requirement: Falha de infraestrutura durante a consulta de candidatos do ciclo de varredura não derruba o processo
O sistema SHALL capturar e registrar (log estruturado) qualquer exceção
de infraestrutura não relacionada a A2A/transporte (ex.: falha de acesso
ao Postgres) que ocorra durante a consulta que identifica os candidatos
elegíveis de um ciclo de varredura do debounce, sem propagá-la para fora
do `BackgroundService` responsável pela varredura. O processo
`apps/inbox` SHALL permanecer em execução após essa falha, e o ciclo de
varredura seguinte (no intervalo configurado) SHALL consultar e
processar normalmente, sem necessidade de reinício do processo.

Distinto do requisito já existente "Falha de transporte do SendMessage é
reintentada antes de ser considerada perda definitiva" — aquele cobre
falha ao chamar `apps/api` durante o disparo de um buffer específico;
este cobre falha na própria consulta de candidatos, antes mesmo de
chegar a um disparo.

#### Scenario: Falha transitória de banco durante a consulta de candidatos não derruba o processo
- **WHEN** uma exceção não relacionada a A2A/transporte (ex.: falha de
  conexão com o Postgres) ocorre durante a consulta de
  `pending_dispatches` elegíveis em um ciclo de varredura
- **THEN** a exceção é registrada via log estruturado e o processo
  `apps/inbox` continua em execução

#### Scenario: Ciclo de varredura seguinte consulta e processa normalmente após uma falha anterior na consulta
- **WHEN** a consulta de candidatos de um ciclo de varredura anterior
  falhou por infraestrutura e foi tratada conforme o Scenario acima
- **AND** o ciclo seguinte, no intervalo configurado, não encontra
  nenhuma condição de falha
- **THEN** os `pending_dispatches` elegíveis nesse ciclo seguinte são
  consultados e processados normalmente, como se a falha anterior não
  tivesse ocorrido

### Requirement: Falha ao processar um candidato específico não bloqueia os demais candidatos do mesmo ciclo
O sistema SHALL capturar e registrar (log estruturado) qualquer exceção
de infraestrutura não relacionada a A2A/transporte que ocorra ao
processar o disparo de um candidato específico dentro de um ciclo de
varredura, sem interromper o processamento dos demais candidatos
elegíveis nesse mesmo ciclo.

#### Scenario: Falha ao processar um candidato não impede o disparo de outro candidato no mesmo ciclo
- **WHEN** dois candidatos elegíveis (A e B) existem no mesmo ciclo de
  varredura
- **AND** o processamento do candidato A lança uma exceção não
  relacionada a A2A/transporte
- **THEN** a exceção de A é registrada via log estruturado, e o
  candidato B é disparado normalmente no mesmo ciclo, sem esperar por um
  novo ciclo de varredura

### Requirement: Falha capturada durante o ciclo de varredura nunca é silenciosa
O sistema SHALL registrar toda exceção capturada durante o ciclo de
varredura — tanto na consulta de candidatos quanto no processamento
individual de um candidato — via log estruturado de nível de erro,
incluindo a exceção original e contexto suficiente para diagnóstico
(no caso de falha por candidato, incluindo o identificador do candidato
afetado). Nunca deve haver supressão silenciosa de uma falha real.

#### Scenario: Falha na consulta de candidatos gera entrada de log de nível erro
- **WHEN** uma exceção de infraestrutura é capturada durante a consulta
  de candidatos de um ciclo de varredura
- **THEN** uma entrada de log de nível erro é emitida, contendo a
  exceção original

#### Scenario: Falha ao processar um candidato gera entrada de log de nível erro
- **WHEN** uma exceção de infraestrutura é capturada ao processar o
  disparo de um candidato específico
- **THEN** uma entrada de log de nível erro é emitida, contendo a
  exceção original e o identificador do candidato afetado
