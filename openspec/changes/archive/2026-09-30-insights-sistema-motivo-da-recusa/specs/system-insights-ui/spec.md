## RENAMED Requirements

- FROM: `### Requirement: Motivos de falha, com o motivo da recusa declarado como lacuna`
- TO: `### Requirement: Motivos de falha e motivos de recusa, separados por população`

O nome antigo promete a lacuna que esta change fecha. O motivo da recusa passou a
ter fonte com a #51, e um título que a declara como lacuna afirmaria uma
limitação que não há — convenção 13 no próprio cabeçalho, e é o que a
`fechamento-da-l4` acabou de corrigir no requisito vizinho.

- FROM: `### Requirement: Falha e recusa são apresentadas separadas, e a recusa não vira percentual`
- TO: `### Requirement: Falha e recusa de entrada são apresentadas separadas, e nenhuma das duas vira percentual indevido`

O nome antigo diz *"a recusa"* quando existem duas, de regimes diferentes, e é
justamente a confusão entre elas que o requisito passa a decidir. Sem o `de
entrada` no título, o requisito continuaria podendo ser lido sobre o número
errado — que é o defeito que ele corrige.

## MODIFIED Requirements

### Requirement: Motivos de falha e motivos de recusa, separados por população

O sistema SHALL apresentar os motivos das falhas a partir das fases de falha e
das falhas de indexação que a resposta declara, traduzidos para rótulos de
operador.

O sistema SHALL apresentar os motivos das recusas de entrada a partir do
vocabulário de motivo que a resposta declara, também traduzidos para rótulos de
operador.

**Os dois conjuntos SHALL ser apresentados separados, e SHALL NOT ser
apresentados como uma lista única nem como um total único.** São populações de
medição diferentes — a falha tem linha de execução, a recusa de entrada não tem —
e uma lista em que as duas se alternam sem marca convida à soma que o card de
contagens proíbe em texto.

Um valor **desconhecido** pela tela — fase de falha, resultado de indexação ou
motivo de recusa — SHALL ser apresentado de forma neutra e visível, com o próprio
valor recebido, e SHALL NOT ser omitido nem reaproveitar o rótulo de outro valor.

**A soma das contagens de motivo de recusa SHALL fechar com a contagem de recusa
de entrada da mesma janela.** A coluna de motivo é obrigatória na fonte; omitir um
valor desconhecido faria a soma deixar de fechar sem nenhum sintoma na tela.

O sistema SHALL NOT nomear, para as recusas, causa que a resposta não tenha
declarado. Este `SHALL NOT` **permanece** do requisito anterior, e é o que a
change preserva: o que mudou é que há fonte, não que a régua caiu.

#### Scenario: Cada fase de falha aparece com rótulo de operador
- **WHEN** a resposta traz contagens por fase de falha
- **THEN** cada fase aparece com o seu rótulo em português e a sua contagem

#### Scenario: Cada motivo de recusa aparece com rótulo de operador
- **WHEN** a resposta traz contagens por motivo de recusa de entrada
- **THEN** cada motivo aparece com o seu rótulo em português e a sua contagem

#### Scenario: Fase desconhecida aparece de forma neutra
- **WHEN** a resposta traz uma fase de falha que a tela não conhece
- **THEN** a linha aparece com o valor recebido apresentado de forma neutra, e
  **não** com o rótulo de outra fase

#### Scenario: Motivo de recusa desconhecido APARECE, com o valor recebido
- **WHEN** a resposta traz um motivo de recusa que a tela não conhece
- **THEN** a linha aparece, com o valor recebido apresentado de forma neutra, e a
  soma das contagens apresentadas continua fechando com a contagem de recusa de
  entrada

#### Scenario: NEGATIVO — o motivo desconhecido não é omitido
- **WHEN** a resposta traz um motivo de recusa que a tela não conhece junto de
  motivos conhecidos
- **THEN** **nenhum** dos motivos recebidos falta na apresentação

#### Scenario: NEGATIVO — as duas populações não são somadas nem misturadas
- **WHEN** a resposta traz fases de falha e motivos de recusa no mesmo período
- **THEN** os motivos de recusa aparecem separados dos motivos de falha, e
  **nenhum** total que some as duas populações é apresentado

#### Scenario: NEGATIVO — nenhuma causa não medida é nomeada para as recusas
- **WHEN** a resposta traz contagem de recusa de entrada e **nenhum** motivo
- **THEN** **nenhuma** causa é nomeada para essas recusas

#### Scenario: Sem motivo nenhum, o card diz o zero medido
- **WHEN** não há falhas nem recusas no período e a consulta respondeu
- **THEN** o card afirma em palavras que não houve motivo a listar

### Requirement: Falha e recusa de entrada são apresentadas separadas, e nenhuma das duas vira percentual indevido

O sistema SHALL apresentar a contagem de execuções que falharam e a contagem de
tasks **recusadas na entrada** como números **separados**, e SHALL declarar em
texto a diferença entre os dois — recusa nunca chega a processar; falha é execução
que começou e quebrou. O sistema SHALL NOT somá-los num número só.

A contagem de recusa apresentada SHALL ser a da **recusa de entrada** — a que a
resposta conta em campo próprio, com regime de medição próprio. O sistema SHALL
NOT apresentar sob esse rótulo a contagem de recusa **com** linha de execução, que
é outra população e hoje cobre só a de profundidade de delegação.

A contagem de recusa de entrada SHALL ser apresentada com o **início do regime
dela declarado junto do número**, porque ele difere do regime que governa a
página. O nome do regime SHALL ser apresentado em rótulo de operador quando a tela
o conhecer, e com o próprio nome recebido quando não o conhecer — nunca com o
rótulo de outro regime.

O texto que declara a diferença entre falha e recusa SHALL NOT enumerar um
subconjunto das causas de recusa que a tela apresenta ao lado.

O percentual SHALL ser apresentado apenas para a contagem cujo numerador e
denominador contam a **mesma população**. Para a recusa, que não produz linha de
execução e por isso subconta o denominador, o sistema SHALL apresentar a contagem
e, no lugar do percentual, o código de parcialidade que a resposta declara.

#### Scenario: Os dois números aparecem separados
- **WHEN** o operador visualiza o card de falhas
- **THEN** a contagem de falhas e a contagem de recusas de entrada aparecem como
  números distintos, com a diferença entre eles declarada em texto

#### Scenario: A contagem de recusa é a da recusa de entrada
- **WHEN** a resposta traz as duas contagens de recusa com valores diferentes
- **THEN** o número apresentado é o da recusa de entrada

#### Scenario: NEGATIVO — a contagem de recusa com linha de execução não é apresentada
- **WHEN** a resposta traz contagem de recusa **com** linha de execução maior que
  zero e contagem de recusa de entrada com outro valor
- **THEN** o valor da recusa com linha de execução **não** aparece na página

#### Scenario: O regime da recusa é declarado junto do número
- **WHEN** a resposta declara para a recusa um regime diferente do que governa a
  página
- **THEN** o início daquele regime aparece junto da contagem de recusa, com
  rótulo de operador

#### Scenario: Regime desconhecido é declarado com o próprio nome
- **WHEN** a resposta declara para a recusa um regime cujo nome a tela não conhece
- **THEN** o texto apresenta o próprio nome recebido, e **não** o rótulo de outro
  regime

#### Scenario: Falha tem percentual, recusa não
- **WHEN** a resposta traz as duas contagens e o total de tasks executadas
- **THEN** a falha é apresentada com o seu percentual, e a recusa é apresentada
  sem percentual, acompanhada do texto do código de parcialidade

#### Scenario: Sem tasks executadas, nenhum percentual é apresentado
- **WHEN** o total de tasks executadas no período é zero
- **THEN** nenhum percentual é apresentado, e **nenhuma** divisão por zero
  aparece como número

#### Scenario: NEGATIVO — o texto não enumera subconjunto das causas apresentadas
- **WHEN** a resposta traz motivos de recusa que a tela apresenta ao lado
- **THEN** o texto que distingue falha de recusa **não** enumera parte dessas
  causas como se fossem todas
