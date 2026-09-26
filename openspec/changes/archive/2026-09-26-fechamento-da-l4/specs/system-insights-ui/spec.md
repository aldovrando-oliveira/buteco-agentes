## RENAMED Requirements

- FROM: `### Requirement: Consumo por agente cruza com o catálogo e declara o que não tem fonte`
- TO: `### Requirement: Consumo por agente cruza com o catálogo e leva ao diagnóstico`

O nome antigo prometia a declaração que este delta remove. Mantê-lo faria o
título afirmar um comportamento que os cenários abaixo proíbem — convenção 13 no
próprio cabeçalho.

## MODIFIED Requirements

### Requirement: Consumo por agente cruza com o catálogo e leva ao diagnóstico

O sistema SHALL apresentar o consumo por agente como tabela, cruzando os
identificadores que a rota agregada devolve com os nomes do catálogo de agentes.

Um agente presente na agregação e ausente do catálogo SHALL ter a linha
preservada, com o identificador apresentado em lugar do nome — o consumo dele é
real, e o que falta é o rótulo, não o número.

O nome do agente SHALL levar à **superfície de diagnóstico daquele agente** — a
aba de Insights do detalhe —, e não a uma superfície a partir da qual o operador
ainda precise escolher. A tabela responde **qual** agente olhar; a superfície de
destino responde **o que aconteceu com ele**, e a passagem entre as duas é de um
acionamento só.

As colunas que o protótipo desenha e a rota agregada do sistema **não serve**
SHALL NOT ser apresentadas com valor inventado, derivado de outro nível de
agregação, nem com zero.

**A declaração dessas colunas junto da tabela foi removida deste requisito.** Elas
deixaram de ser lacuna e passaram a ser **ausência decidida** — têm fonte, são
apresentadas na superfície de destino, e ficam fora desta por decisão registrada.
O requisito *"Métrica aprovada no protótipo e sem fonte não é inventada"* governa
a apresentação dos dois casos, e para coluna ele proíbe o elemento que este
requisito antes exigia.

#### Scenario: Cada agente aparece com nome e consumo
- **WHEN** a agregação e o catálogo respondem
- **THEN** cada agente com consumo no período aparece uma vez, com o seu nome e
  os seus números

#### Scenario: Agente fora do catálogo mantém a linha
- **WHEN** a agregação traz um agente que não está no catálogo
- **THEN** a linha é apresentada com o identificador em lugar do nome, e os
  números dele permanecem

#### Scenario: O nome leva direto ao diagnóstico do agente
- **WHEN** o operador aciona o nome de um agente na tabela
- **THEN** a aplicação navega para a superfície de diagnóstico daquele agente,
  **já aberta nela**, sem exigir um segundo acionamento para alcançá-la

#### Scenario: Colunas sem fonte não são preenchidas
- **WHEN** o operador visualiza a tabela de consumo por agente
- **THEN** **nenhuma** das colunas que esta rota não serve aparece preenchida com
  zero ou com valor de outro nível de agregação

#### Scenario: NEGATIVO — a ausência decidida não vira elemento na tabela
- **WHEN** o operador visualiza a tabela de consumo por agente
- **THEN** **nenhum** elemento do card nomeia as colunas ausentes nem explica a
  ausência delas

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

#### Scenario: A lacuna não é confundida com dado desconhecido
- **WHEN** a lacuna declarada e o travessão aparecem na mesma tela
- **THEN** os dois têm apresentação distinta, e o texto da lacuna não diz que a
  consulta falhou

#### Scenario: Ausência decidida não é declarada como lacuna
- **WHEN** uma métrica deixa de ser apresentada numa superfície por decisão
  registrada, estando servida e apresentada em outra
- **THEN** a tela não ganha texto anunciando a ausência, e a explicação fica no
  registro histórico e na issue fechada, com o gatilho de reabertura
