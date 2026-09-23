## ADDED Requirements

### Requirement: Requisição de resumo do histórico é aceita por todos os provedores suportados
A requisição que `apps/workers` faz ao provedor para resumir o histórico SHALL
ser montada de forma aceita por **todos** os provedores suportados, e em
particular SHALL NOT terminar em mensagem de assistente — há provedor suportado
que recusa com erro `400` uma requisição encerrada em turno de modelo. Quando a
porção do histórico selecionada para resumo terminar em mensagem de assistente,
o worker SHALL acrescentar uma mensagem final de usuário à requisição, sem
alterar as mensagens do histórico nem a instrução de sistema que acompanha o
resumo.

#### Scenario: Porção resumida termina em mensagem de assistente
- **WHEN** o limiar de interações é cruzado e a porção do histórico selecionada
  para resumo termina em mensagem de assistente
- **THEN** a requisição enviada ao provedor termina em mensagem de usuário, e as
  mensagens do histórico chegam ao provedor sem alteração de papel nem de
  conteúdo

#### Scenario: Conversa longa com provedor que recusa turno final de modelo
- **WHEN** uma conversa no mesmo `contextId` passa do limiar de interações várias
  vezes seguidas usando um provedor que recusa requisição encerrada em turno de
  modelo
- **THEN** o resumo é produzido e incorporado ao histórico, e o número de
  mensagens enviadas ao provedor no turno seguinte para de crescer a cada turno,
  em vez de crescer indefinidamente

#### Scenario: Provedor que aceita turno final de modelo continua funcionando
- **WHEN** a mesma conversa longa acontece num provedor que aceita requisição
  encerrada em turno de modelo
- **THEN** o resumo continua sendo produzido e incorporado ao histórico, sem
  regressão em relação ao comportamento anterior

### Requirement: Falha na chamada de resumo é registrada em log
Quando a chamada ao provedor para gerar o resumo falhar, `apps/workers` SHALL
registrar em log, em nível aviso, que o resumo falhou e qual foi o erro. A falha
SHALL NOT ficar silenciosa, mesmo continuando a não transicionar a task para
`failed`.

#### Scenario: Chamada de resumo recusada pelo provedor
- **WHEN** o limiar de interações é cruzado e a chamada de resumo falha
- **THEN** existe no log uma linha de nível aviso identificando a falha do resumo
  e o erro do provedor, e o turno corrente do usuário ainda assim chega a
  `completed`

### Requirement: Linha de log da requisição ao provedor distingue sucesso de falha
`apps/workers` SHALL registrar a requisição ao provedor de LLM em **linhas
distintas** para sucesso e para falha, de forma que uma requisição que falhou
não seja indistinguível de uma bem-sucedida pelo texto da linha. As duas linhas
SHALL identificar a task da execução e a finalidade da requisição (turno do
agente × resumo do histórico), e a linha de falha SHALL registrar o tipo da
exceção e o status HTTP quando houver. Fora de execução de task, a ausência de
identificador de task SHALL NOT impedir o registro da linha.

#### Scenario: Requisição bem-sucedida dentro de uma execução
- **WHEN** o worker executa uma task e a chamada ao provedor retorna
- **THEN** a linha registrada identifica sucesso, a task, a finalidade, o
  provedor, o modelo e a duração

#### Scenario: Requisição que falha dentro de uma execução
- **WHEN** a chamada ao provedor lança dentro de uma execução
- **THEN** a linha registrada identifica falha, com o tipo da exceção e o status
  HTTP quando houver, e é textualmente distinta da linha de sucesso

#### Scenario: Requisição de resumo é distinguível da requisição do turno
- **WHEN** uma task cruza o limiar de resumo do histórico
- **THEN** as linhas da requisição de resumo e as do turno do agente se
  distinguem pela finalidade registrada, sem depender de comparar durações

#### Scenario: Requisição fora de qualquer execução
- **WHEN** o client é chamado sem execução de task em andamento
- **THEN** a linha continua sendo registrada, sem identificador de task e sem
  falhar por causa disso
