## ADDED Requirements

### Requirement: Checagem de inicialização bem-sucedida registra uma linha
Toda checagem de inicialização de `apps/workers` SHALL registrar uma linha de log
quando passar, e não apenas quando falhar, identificando o que foi
conferido e o valor conferido. Sucesso SHALL NOT ser indistinguível de a
checagem não ter rodado. Isso SHALL valer para a checagem do tamanho de lote de
embedding e para a de consistência do índice de embedding, cuja única
manifestação em log era a consulta que o EF Core imprimia — consulta que o nível
de log de produção silencia.

#### Scenario: Worker sobe com configuração de embedding válida
- **WHEN** o worker inicia com tamanho de lote e índice de embedding consistentes
  com a configuração
- **THEN** o log de inicialização tem uma linha por checagem, identificando o
  valor conferido, e o processo segue para o processamento

#### Scenario: Worker sobe em produção, com o log de comando do EF silenciado
- **WHEN** o worker inicia no ambiente de produção, onde o log de comando de
  banco do EF Core está em `Warning`
- **THEN** as linhas das checagens continuam aparecendo, sem depender da consulta
  que o EF Core imprimiria

#### Scenario: Checagem que falha continua falhando
- **WHEN** o worker inicia com configuração de embedding inconsistente
- **THEN** a inicialização falha com o erro explícito de hoje, sem ser mascarada
  pela linha de sucesso
