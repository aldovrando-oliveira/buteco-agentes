## ADDED Requirements

### Requirement: A recusa de entrada do agente é contada e os motivos dela são servidos no mesmo formato dos dois escopos

O sistema SHALL devolver, no agregado por agente, a contagem de tasks daquele
agente recusadas **antes de qualquer execução** como número próprio, e os motivos
delas como **lista de valor e contagem**, com o **mesmo formato e o mesmo
vocabulário** do escopo do sistema.

O recorte por agente SHALL sair da coluna de agente da própria fonte de recusa, e
SHALL NOT ser derivado de junção com o catálogo nem com o store de tasks: a linha
de recusa carrega o agente a que a task foi endereçada.

Os dois escopos SHALL passar a contar a recusa de entrada **na mesma change**, e
SHALL NOT divergir em qual dos dois a conta — um escopo que contasse e outro que
não contasse fariam os dois medirem coisas diferentes com o mesmo rótulo, que é a
razão registrada de a lacuna não ter sido fechada só aqui quando foi descoberta.

#### Scenario: A contagem e os motivos chegam recortados pelo agente

- **WHEN** a rota é chamada para um agente que tem recusas de entrada na janela, e
  outro agente também tem
- **THEN** a contagem e os motivos trazem apenas as recusas do agente consultado

#### Scenario: Agente sem recusa de entrada recebe zero medido

- **WHEN** a janela está dentro do regime da coleta de recusa e o agente
  consultado não tem nenhuma recusa de entrada nela
- **THEN** a contagem é `0` e a lista de motivos é vazia, e nenhum dos dois é
  apresentado como ausência de fonte

#### Scenario: Os dois escopos concordam sobre a mesma janela

- **WHEN** existe exatamente um agente com recusas de entrada na janela
- **THEN** a contagem do escopo do agente é igual à do escopo do sistema para a
  mesma janela

## MODIFIED Requirements

### Requirement: As parcialidades herdadas valem também no escopo do agente

O sistema SHALL declarar, no agregado por agente, as mesmas parcialidades já
declaradas no escopo do sistema sempre que elas alcancem este escopo, e NÃO SHALL
apresentar como completa uma contagem que o recorte por agente não torna completa.

Uma parcialidade que deixe de existir SHALL sair da resposta dos **dois** escopos
ao mesmo tempo, e SHALL NOT permanecer declarada em um deles.

Permanecem valendo, com a causa inalterada:

- **recusas feitas antes de qualquer execução** não produzem linha de execução, e
  a contagem de recusas derivada das tabelas de métrica conta **outra população**
  também no escopo do agente. A contagem das recusas de entrada e os motivos delas
  passam a ter fonte própria, com coluna de agente; o que continua sem fonte é
  **provedor e modelo** delas, porque duas das causas de recusa são a ausência de
  provedor ou modelo válidos — então o agrupamento de falha por provedor e modelo
  continua parcial por construção;
- **duração total e tempo de fila** ficam indefinidas quando o carimbo de
  submissão é nulo, e essas execuções ficam fora do cálculo como ausentes, nunca
  como zero;
- **o resíduo de tempo não atribuído ao provedor** inclui mais do que ferramentas,
  e o rótulo declara o que inclui.

#### Scenario: Recusa sem linha de execução não é omitida em silêncio no escopo do agente

- **WHEN** existe, na janela, task do agente consultado recusada antes de qualquer
  execução
- **THEN** a resposta não apresenta a contagem de recusas do agente como completa
  a partir das tabelas de métrica

#### Scenario: A parcialidade do motivo sai dos dois escopos

- **WHEN** a rota do agente responde e o motivo da recusa tem fonte
- **THEN** o código de parcialidade que declarava a ausência dessa fonte não
  aparece na resposta do escopo do agente, como não aparece na do sistema

#### Scenario: Carimbo ausente produz indefinido, não zero, também por agente

- **WHEN** existe execução do agente consultado cujo carimbo de submissão é nulo
- **THEN** a duração e o tempo de fila dessa execução ficam fora do cálculo como
  ausentes, e não entram como `0`
