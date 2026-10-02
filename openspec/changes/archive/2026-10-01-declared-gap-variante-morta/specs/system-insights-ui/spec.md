## MODIFIED Requirements

### Requirement: Métrica aprovada no protótipo e sem fonte não é inventada, e a lacuna é declarada onde há elemento para declará-la

O sistema SHALL NOT preencher com zero, com valor derivado de outro nível de
agregação, nem com qualquer valor inventado, métrica que o protótipo aprovado
desenha e cuja fonte não existe na rota.

Onde o protótipo desenha um **subtítulo** para essa métrica, o sistema SHALL
substituí-lo por uma lacuna declarada, no mesmo peso do subtítulo: um texto que
nomeia o que falta, sem número.

Onde o protótipo desenha **coluna ou conjunto de colunas**, o sistema SHALL
removê-las e SHALL NOT acrescentar elemento próprio para anunciá-las. A lacuna
existe na **issue** que a registra, não na tela.

**A lacuna declarada SHALL ter uma forma só, e ela é a do subtítulo.** O sistema
SHALL NOT dispor de forma de lacuna declarada que renderize moldura, quadro ou
qualquer elemento de contenção próprio — não como opção escolhida, não como opção
disponível, e não como padrão.

**A razão de a proibição ser sobre a FORMA e não sobre o resultado:** proibir o
quadro na tela deixa aberto o caminho para produzi-lo, e o caminho mais barato é o
que se percorre sem escolher — uma forma que exista e seja o padrão renderiza o
quadro para quem não pediu nada. Enquanto a forma existir, a conformidade depende
de cada sítio novo lembrar de recusá-la, e uma forma sem sítio que a use **não é
detectável por cobertura de teste**: o guarda que a exercita a mantém viva e esconde
que ela não tem consumidor.

**A razão da assimetria:** declarar na tela vale onde há um elemento do próprio
protótipo para carregar a declaração — ali a lacuna ocupa um lugar que já
existia. Criar um quadro novo, que o artboard não tem, acrescenta à tela um
elemento cuja única função é falar do que ela não mostra, e o peso dele compete
com os números que ela mostra. **O que sobrevive ao archive é a issue**
(convenção 23), e é lá que a lacuna precisa estar.

**Métrica ausente da superfície tem duas causas, e elas SHALL ser distinguidas
no registro:**

- **Lacuna** — a métrica **não tem fonte em lugar nenhum**. A explicação vive na
  **issue aberta** que a registra, e a issue é o que obriga alguém a voltar.
- **Ausência decidida** — a métrica **tem fonte**, é servida, e é apresentada em
  **outra** superfície; fica fora desta por decisão registrada. A explicação vive
  no registro histórico e na **issue fechada**, com o gatilho de reabertura
  escrito.

**As duas SHALL sumir da tela do mesmo jeito**, e o sistema SHALL NOT acrescentar
elemento para anunciar nenhuma das duas onde o que sai é coluna. **O que muda é
onde a explicação mora, nunca o que a tela mostra.**

E o sistema SHALL NOT declarar como lacuna aquilo que passou a ser ausência
decidida: um texto que pede desculpa por uma escolha afirma ao operador que a
tela está incompleta quando ela está como se quis.

#### Scenario: Métrica sem fonte não recebe valor
- **WHEN** o operador visualiza uma superfície cujo protótipo previa uma métrica
  que a rota não serve
- **THEN** **nenhum** número é apresentado no lugar dela, nem zero, nem valor de
  outro nível de agregação

#### Scenario: Subtítulo sem fonte vira lacuna declarada
- **WHEN** o protótipo desenha um subtítulo cuja fonte a rota não serve
- **THEN** o subtítulo apresenta o que falta, no mesmo peso das demais linhas de
  subtítulo da tela

#### Scenario: Coluna sem fonte sai sem deixar quadro no lugar
- **WHEN** o protótipo desenha uma coluna cuja fonte a rota não serve
- **THEN** a coluna não aparece, e **nenhum** elemento novo é acrescentado ao
  card para anunciá-la

#### Scenario: A lacuna declarada vem sem moldura mesmo quando nada é escolhido
- **WHEN** uma lacuna declarada é apresentada sem que a superfície escolha forma
  alguma para ela
- **THEN** ela vem no peso do subtítulo, **sem moldura e sem quadro**

#### Scenario: Não há forma de lacuna que renderize quadro
- **WHEN** qualquer superfície apresenta uma lacuna declarada
- **THEN** **nenhuma** escolha disponível a essa superfície produz moldura, quadro
  ou elemento de contenção próprio em torno da lacuna

#### Scenario: A lacuna não é confundida com dado desconhecido
- **WHEN** a lacuna declarada e o travessão aparecem na mesma tela
- **THEN** os dois têm apresentação distinta, e o texto da lacuna não diz que a
  consulta falhou

#### Scenario: Ausência decidida não é declarada como lacuna
- **WHEN** uma métrica deixa de ser apresentada numa superfície por decisão
  registrada, estando servida e apresentada em outra
- **THEN** a tela não ganha texto anunciando a ausência, e a explicação fica no
  registro histórico e na issue fechada, com o gatilho de reabertura
