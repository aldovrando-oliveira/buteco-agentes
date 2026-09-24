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
