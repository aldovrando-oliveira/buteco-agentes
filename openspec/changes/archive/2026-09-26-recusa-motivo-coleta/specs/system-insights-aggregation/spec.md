## ADDED Requirements

### Requirement: A recusa de entrada é contada à parte e os motivos dela são servidos

A rota SHALL devolver, no grupo de erros, a contagem de tasks recusadas **antes
de qualquer execução** como número **próprio**, distinto da contagem de recusas
que têm linha de execução, e SHALL declarar em texto estável que as duas contam
populações diferentes.

A rota SHALL NOT somar as duas contagens num número só, e SHALL NOT substituir o
significado da contagem que já existe: a que é derivada das tabelas de métrica
continua contando o que elas contêm.

A rota SHALL devolver os motivos das recusas de entrada como **lista de valor e
contagem**, no mesmo formato das fases de falha — um valor do vocabulário
fechado por linha, com a contagem dela, sem subcardinalidade por provedor,
modelo ou agente.

A soma das contagens da lista de motivos SHALL ser igual à contagem de recusas de
entrada da mesma janela, porque a coluna de motivo é obrigatória e não existe
recusa gravada sem motivo.

Um valor de motivo que a rota não conheça SHALL chegar ao cliente **como está**, e
SHALL NOT ser omitido nem reescrito: omiti-lo faria a soma dos motivos deixar de
fechar com a contagem, sem sintoma.

#### Scenario: As duas contagens de recusa chegam separadas

- **WHEN** a janela contém recusas de entrada e recusas com linha de execução
- **THEN** a resposta traz as duas contagens como campos distintos, e nenhum campo
  apresenta a soma delas

#### Scenario: Os motivos chegam como valor e contagem

- **WHEN** a janela contém recusas de entrada de causas diferentes
- **THEN** a resposta traz uma linha por motivo, com o valor do vocabulário e a
  contagem dele, e a soma das contagens é igual à contagem de recusas de entrada

#### Scenario: Motivo desconhecido pela rota não é descartado

- **WHEN** a fonte contém um valor de motivo que a rota não conhece
- **THEN** ele aparece na lista com o valor recebido e a sua contagem

#### Scenario: Sem recusa de entrada na janela, a contagem é zero medido

- **WHEN** a janela está inteiramente dentro do regime da coleta de recusa e não
  contém nenhuma recusa de entrada
- **THEN** a contagem de recusas de entrada é `0` e a lista de motivos é vazia, e
  nenhum dos dois é apresentado como ausência de fonte

### Requirement: Regime declarado por um grupo de métricas sem instante configurado reprova o boot

`apps/api` SHALL validar, no startup, que **todo** regime de medição declarado
pelas rotas de agregação tem instante correspondente no mapa de configuração, e
SHALL falhar a inicialização quando algum não tiver.

A validação SHALL acontecer no boot, e não na primeira consulta. Um regime sem
instante configurado **desliga o recorte daquele grupo de métricas**: a janela
pedida passa a valer inteira, o período anterior à coleta vira contagem `0` em vez
de ausência, e a resposta afirma medição que não houve — com número plausível, em
silêncio. É o mesmo modo de falha que a distinção entre período não medido e
período sem uso existe para impedir, entrando pela porta da configuração.

#### Scenario: Regime sem instante configurado reprova o boot

- **WHEN** `apps/api` inicializa com um regime declarado por um grupo de métricas
  ausente do mapa de instantes de configuração
- **THEN** a inicialização falha com mensagem que nomeia o regime ausente, e a
  aplicação não passa a responder requisição

#### Scenario: Todos os regimes configurados sobem normalmente

- **WHEN** todos os regimes declarados têm instante no mapa de configuração
- **THEN** a inicialização conclui e as rotas respondem

## MODIFIED Requirements

### Requirement: "Medindo desde" é declarado por regime de medição

A rota SHALL devolver os instantes de início de medição como um **mapa de
regimes**, e cada grupo de métricas SHALL declarar a qual regime pertence. A rota
NÃO SHALL devolver um único "medindo desde" para todas as métricas.

São **três** regimes — a coleta de execução, a coleta de embedding e a coleta do
motivo da recusa começaram em datas diferentes —, e um texto único mentiria sobre
dois deles. O formato é um mapa, e não um campo por regime, porque foi feito para
absorver o regime seguinte sem mudar de forma — e absorveu: o terceiro entrou sem
alterar o contrato do mapa.

Um grupo de métricas cujos números venham de **fontes de regimes diferentes**
SHALL declarar os regimes de todos eles, e SHALL NOT eleger um para representar o
grupo: o grupo de erros lê execução, embedding e recusa, e um único nome ali
atribuiria a três coletas a data de uma.

O instante de início de cada regime SHALL vir de **configuração**, registrada no
dia do deploy da coleta correspondente, e NÃO SHALL ser derivado do menor carimbo
existente nos dados. O menor carimbo é *"quando a primeira linha chegou"*: se o
sistema ficou ocioso depois do deploy, derivá-lo marcaria como **não medido** um
período que foi medido e estava vazio — exatamente a distinção que esta capability
existe para preservar. O menor carimbo permanece válido como **conferência**.

#### Scenario: Os três regimes chegam separados

- **WHEN** a rota é chamada
- **THEN** a resposta carrega o instante de início de cada regime de medição, e
  cada grupo de métricas declara o seu

#### Scenario: Grupo que lê três fontes declara os três regimes

- **WHEN** o grupo de erros é lido
- **THEN** ele declara o regime de execução, o de embedding e o da coleta de
  recusa, e nenhum dos três é omitido em favor dos outros

#### Scenario: Ociosidade depois do deploy não encurta o regime

- **WHEN** o primeiro dado de um regime é posterior ao início declarado dele
- **THEN** o início devolvido é o declarado, não o do primeiro dado

### Requirement: Métrica de fonte parcial declara a sua parcialidade

A rota SHALL declarar a parcialidade de toda métrica do catálogo que não puder
ser derivada integralmente das fontes existentes, e NÃO SHALL apresentá-la como
se fosse completa. Ela SHALL devolver o que sabe e tornar a lacuna legível para o
cliente, ou omitir a métrica — nunca apresentar um número parcial com o rótulo do
todo.

Uma parcialidade que **deixe de existir** SHALL ter o seu código retirado da
resposta, e SHALL NOT continuar declarada por compatibilidade: código de
parcialidade que sobrevive à lacuna que ele descrevia afirma uma limitação que já
não há, e ensina o cliente a ignorar os outros.

As parcialidades conhecidas nesta etapa, e que este requisito cobre:

- **Recusas feitas antes de qualquer execução** não produzem linha de execução. A
  contagem de recusas derivada das tabelas de métrica conta **outra população** —
  as recusas que uma execução registrou —, e continua sem provedor e sem modelo
  para as de entrada, porque duas das causas delas são justamente a ausência de
  provedor ou modelo válidos. A contagem de recusas de entrada e os motivos dela
  passam a ter fonte própria; o agrupamento de falha por provedor e modelo
  continua **parcial por construção**.
- **Duração total e tempo de fila** dependem de um carimbo que é nulo em
  reentrega, e ficam **indefinidos** nesses casos — nulo a preservar, nunca zero.
- **Tempo em ferramentas** é um resíduo que inclui também espera de lock,
  chamadas a servidores externos e busca vetorial. O rótulo SHALL declarar o que
  o número inclui, ou a métrica SHALL mudar de nome.

#### Scenario: Recusa sem linha de execução não é silenciosamente omitida

- **WHEN** existe task recusada antes de qualquer execução
- **THEN** a resposta não a apresenta como se a contagem de recusas fosse
  completa a partir das tabelas de métrica

#### Scenario: Parcialidade que deixou de existir sai da resposta

- **WHEN** a rota responde e o motivo da recusa tem fonte
- **THEN** o código de parcialidade que declarava a ausência dessa fonte **não**
  aparece entre os códigos do grupo de erros

#### Scenario: Carimbo ausente produz indefinido, não zero

- **WHEN** existe execução cujo carimbo de submissão é nulo
- **THEN** a duração e o tempo de fila dessa execução ficam fora do cálculo como
  ausentes, e não entram como `0`
