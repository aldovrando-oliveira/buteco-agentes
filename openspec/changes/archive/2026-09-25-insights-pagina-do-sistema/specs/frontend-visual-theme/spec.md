## ADDED Requirements

### Requirement: Escala de intensidade declarada nos dois esquemas de cor

O sistema SHALL declarar no tema a escala de intensidade usada por mapas de
calor, com um passo para o zero medido e os passos de intensidade crescente, e
SHALL declará-la **separadamente para cada esquema de cor**.

A rampa SHALL crescer em sentidos opostos entre os esquemas: no esquema escuro a
intensidade cresce **clareando**; no claro, cresce **escurecendo**. É o mesmo
papel visual trocando de ponta da escala, e por isso ele SHALL sair de variável
declarada por esquema e SHALL NOT sair de tom fixo da paleta — um tom fixo é
claro nos dois esquemas ou escuro nos dois, e quebraria em um deles.

O componente que desenha o mapa de calor SHALL consumir essas variáveis, e SHALL
NOT referenciar nenhum tom da paleta diretamente.

A escala SHALL ser composta de tons que já existem na paleta do tema; nenhuma cor
nova entra por ela.

#### Scenario: A escala existe nos dois esquemas
- **WHEN** a aplicação é renderizada em qualquer um dos dois esquemas de cor
- **THEN** todos os passos da escala de intensidade estão definidos naquele
  esquema

#### Scenario: A rampa inverte entre os esquemas
- **WHEN** os passos de intensidade são comparados entre os dois esquemas
- **THEN** o passo de maior intensidade do esquema escuro é mais claro que o de
  menor intensidade, e no esquema claro a relação é a inversa

#### Scenario: O componente lê a variável, não o tom
- **WHEN** o mapa de calor é renderizado
- **THEN** as células referenciam as variáveis da escala declaradas no tema, e
  **nenhum** tom da paleta aparece cravado no componente

### Requirement: Degrau de superfície entre card e superfície sutil

O sistema SHALL declarar no tema uma superfície sutil, distinta da superfície de
cards, para faixas de cabeçalho, quadros internos de métrica e texturas de
ausência de dado; e SHALL declará-la **separadamente para cada esquema de cor**.

O degrau entre essa superfície e a do card SHALL ser perceptível em área
chapada, nos dois esquemas — não basta que as duas cores sejam distintas.

**É um terceiro nível, e não o mesmo da página contra o card.** O primeiro
separa o fundo do conteúdo elevado; este separa, dentro do conteúdo elevado, o
que é destaque do que é base. Um degrau que existe no token e não se vê na tela
cumpre a primeira exigência e falha a segunda.

#### Scenario: A superfície sutil existe nos dois esquemas
- **WHEN** a aplicação é renderizada em qualquer um dos dois esquemas de cor
- **THEN** a superfície sutil está declarada naquele esquema, e é distinta da
  superfície do card

#### Scenario: O degrau é perceptível em área chapada
- **WHEN** um quadro de métrica ou uma faixa de cabeçalho é renderizado sobre um
  card, em qualquer um dos dois esquemas
- **THEN** a diferença entre as duas superfícies é perceptível sem depender de
  borda, e **não** fica abaixo do limiar em que uma área chapada se confunde com
  o fundo

#### Scenario: A distinção entre as cores não basta como garantia
- **WHEN** o contrato da superfície sutil é verificado
- **THEN** a verificação mede a **razão de contraste** entre ela e a superfície
  do card, e **não** apenas que as duas apontam para tons diferentes da paleta
