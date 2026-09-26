# agent-insights-ui Specification

## Purpose
TBD - created by archiving change insights-aba-do-agente. Update Purpose after archive.
## Requirements
### Requirement: Aba Insights no detalhe do agente servida por uma consulta só

O sistema SHALL prover, em `apps/frontend`, uma aba **Insights** dentro da página
de detalhe do agente, alcançável pela lista de abas e identificada na URL como as
demais.

A aba SHALL obter os números de **uma única** requisição à rota agregada do
escopo do agente, com o identificador que a rota da página já carrega. O sistema
SHALL NOT repartir os números da aba entre rotas diferentes, nem recalcular no
cliente qualquer agregado que a rota já devolva.

A requisição SHALL ser feita **apenas quando a aba está ativa**, e SHALL NOT ser
disparada pela simples abertura da página de detalhe em outra aba.

A aba SHALL obter do **catálogo de agentes** — que a rota agregada não devolve —
o **nome** de cada agente citado nos dois lados da delegação e o **registro** de
quais vínculos de delegação existem hoje. Essa segunda fonte SHALL ser
independente da primeira: nenhuma das duas SHALL segurar o conteúdo já pronto da
outra, e a falha de uma SHALL NOT apagar os números da outra.

#### Scenario: A aba é alcançável e identificada na URL
- **WHEN** o operador aciona a aba Insights no detalhe de um agente
- **THEN** a URL passa a identificar essa aba, e recarregar a página nesse
  endereço reabre a mesma aba

#### Scenario: Uma requisição serve a aba inteira
- **WHEN** a aba Insights é aberta
- **THEN** exatamente uma requisição é feita à rota agregada do escopo do agente,
  e todos os números apresentados vêm dela

#### Scenario: Aba inativa não consulta métricas
- **WHEN** a página de detalhe do agente é aberta em qualquer outra aba
- **THEN** **nenhuma** requisição à rota agregada do escopo do agente é feita

#### Scenario: Catálogo indisponível não apaga os números
- **WHEN** a rota agregada responde e o catálogo de agentes falha
- **THEN** todos os números da aba continuam apresentados, e apenas a
  identificação dos agentes das duas seções de delegação fica sem nome, com nova
  tentativa própria

### Requirement: Os dois lados da delegação são conjuntos distintos, e a divergência é afirmada

O sistema SHALL apresentar os dois conjuntos de delegação — **o que o agente
tentou delegar** e **o que de fato rodou nele por delegação** — como conjuntos
**separados**, cada um com a sua fonte, e SHALL NOT apresentá-los como duas
vistas da mesma contagem.

O sistema SHALL NOT derivar um lado do outro, SHALL NOT exibir um total que some
os dois, e SHALL NOT sinalizar como erro, alerta ou inconsistência o fato de os
dois lados mostrarem números diferentes para a mesma relação entre os mesmos dois
agentes. Essa diferença é resultado correto: um lado conta **tentativa** e o
outro conta **execução**, e as duas janelas são situadas por relógios diferentes
— a de quem delega pela execução de origem, a de quem é delegado pela execução de
destino.

O sistema SHALL tornar disponível, junto do card que carrega os dois lados, o
texto **inteiro** do código de parcialidade que declara essa assimetria, e SHALL
NOT deixá-lo implícito nem descartá-lo.

O texto PODE ficar atrás de um recurso de leitura, desde que o recurso seja
**visível** no card e o texto seja alcançável sem sair da superfície. Um recurso
que só se revela por interação SHALL expor o texto também ao leitor de tela.

O sistema SHALL discriminar, no lado de quem delega, **o resultado** de cada
delegação tentada, e SHALL NOT colapsar os resultados num número único por agente
de destino: é o resultado que explica a divergência, e um total sem ele deixa a
diferença entre os dois lados sem causa visível na tela.

Um valor de resultado que a tela não conheça SHALL ser apresentado **como está**,
de forma visível, e SHALL NOT ser omitido nem mapeado para o rótulo de outro
resultado.

#### Scenario: Os dois lados discordam e a tela afirma os dois
- **WHEN** a resposta traz, para a mesma relação entre os mesmos dois agentes, um
  número no lado de quem delega e um número menor no lado de quem é delegado
- **THEN** os dois números aparecem, cada um na sua seção, e **nenhum** deles é
  corrigido, somado, reconciliado ou marcado como inconsistência

#### Scenario: O resultado da delegação é discriminado
- **WHEN** o agente tentou delegar para o mesmo destino com mais de um resultado
  na janela
- **THEN** cada resultado aparece nomeado com a sua contagem, e a tela **não**
  apresenta apenas a soma deles

#### Scenario: Resultado desconhecido aparece cru
- **WHEN** a resposta traz um valor de resultado de delegação que a tela não
  conhece
- **THEN** o próprio valor é apresentado de forma visível, e **não** é descartado
  nem exibido com o rótulo de outro resultado

#### Scenario: A assimetria é declarada onde os dois lados aparecem
- **WHEN** a aba apresenta os dois lados da delegação
- **THEN** o card apresenta um recurso visível que dá o texto do código de
  parcialidade que declara a assimetria, e esse recurso **não** fica numa lista
  separada dos números que ele limita

#### Scenario: O texto atrás do recurso continua inteiro e legível por leitor de tela
- **WHEN** o texto da assimetria é apresentado por um recurso que se revela por
  interação
- **THEN** o texto chega **completo**, e é exposto também sem depender da
  interação, para o leitor de tela

#### Scenario: Números que coincidem não viram afirmação de espelho
- **WHEN** os dois lados da mesma relação trazem, na janela consultada, o mesmo
  número
- **THEN** a tela continua apresentando os dois conjuntos separados, e **não**
  apresenta a coincidência como confirmação de que os lados se espelham

### Requirement: Os quatro cenários de delegação têm a mesma estrutura

O sistema SHALL apresentar **sempre as duas seções** de delegação — quem o agente
aciona e quem aciona o agente — independentemente de o agente ter vínculo em um
lado, no outro, nos dois ou em nenhum.

O lado sem vínculo SHALL ser apresentado com um estado próprio, visível, e SHALL
NOT sumir da tela: a estrutura da aba é a mesma para qualquer agente, e uma seção
ausente faz o operador procurar defeito na navegação em vez de ler o fato.

O agente que **não delega nem é delegado** SHALL receber o mesmo card, com as
**duas** seções nesse estado.

#### Scenario: Agente que só delega
- **WHEN** o agente consultado tem vínculo de delegação de saída e nenhum agente
  o aciona
- **THEN** a seção de quem ele aciona aparece preenchida e a outra aparece no
  estado de ausência de vínculo, sem sumir

#### Scenario: Agente que só é delegado
- **WHEN** o agente consultado é acionado por outros e não tem vínculo de
  delegação de saída
- **THEN** a seção de quem o aciona aparece preenchida e a outra aparece no
  estado de ausência de vínculo, sem sumir

#### Scenario: Agente que delega e é delegado
- **WHEN** o agente consultado está nos dois lados
- **THEN** as duas seções aparecem preenchidas, e nenhuma delas é apresentada
  como derivada da outra

#### Scenario: Agente que não delega nem é delegado
- **WHEN** o agente consultado não tem vínculo em nenhum dos dois lados
- **THEN** o card continua presente com as **duas** seções no estado de ausência
  de vínculo, e **nenhuma** contagem zero é apresentada no lugar delas

### Requirement: Vínculo cadastrado e vínculo usado são fatos diferentes nos dois lados

O sistema SHALL distinguir, nas duas seções de delegação, **"não há vínculo
cadastrado"** de **"há vínculo cadastrado e ele não foi usado no período"**.

Vínculo que existe no cadastro e não tem ocorrência no período SHALL aparecer
como **linha com contagem `0`** — é contagem feita sobre um vínculo que existe.
Ausência de vínculo no cadastro SHALL aparecer como o estado de ausência de
vínculo, sem número.

O sistema SHALL NOT emitir `0` para relação que não está cadastrada, e SHALL NOT
apresentar como ausência de cadastro uma relação cadastrada e ociosa.

Relação que **tem ocorrência medida** e não está mais no cadastro SHALL continuar
sendo apresentada, com a identificação que o catálogo der, e SHALL NOT ser
omitida: a medição aconteceu.

O texto do estado de ausência de vínculo no lado de quem aciona o agente SHALL
NOT afirmar que o agente recebe pedidos externos quando não houve nenhuma task
de origem externa no período.

#### Scenario: Vínculo cadastrado e ocioso aparece com zero
- **WHEN** o agente tem um vínculo de delegação cadastrado e nenhuma delegação
  para aquele destino na janela
- **THEN** o destino aparece como linha com contagem `0`, e **não** como ausência
  de vínculo

#### Scenario: Ausência de cadastro não vira zero
- **WHEN** o agente não tem nenhum vínculo de delegação cadastrado
- **THEN** a seção apresenta o estado de ausência de vínculo, e **nenhum** `0`
  aparece nela

#### Scenario: Ocorrência medida sem vínculo atual continua visível
- **WHEN** a resposta traz ocorrência de delegação para um destino que não está
  mais no cadastro do agente
- **THEN** a linha aparece com a contagem medida, e **não** é omitida

#### Scenario: O texto de ausência não afirma origem externa que não houve
- **WHEN** nenhum agente aciona o agente consultado e ele também não executou
  nenhuma task de origem externa na janela
- **THEN** o estado de ausência de vínculo desse lado **não** afirma que o agente
  recebe pedidos externos

### Requirement: Agente inexistente, agente sem dado e agente inativo são três respostas distintas

O sistema SHALL apresentar de forma **distinta** as três respostas que a rota
agregada dá, e SHALL NOT colapsar nenhuma delas nas outras nem no estado de
consulta sem resposta:

- **agente inexistente** — a rota recusa o identificador. A aba SHALL apresentar
  um estado próprio que nomeia a recusa, SHALL NOT apresentar número algum, e
  SHALL NOT oferecer nova tentativa, porque a resposta não é transitória;
- **agente existente e sem dado no período** — a aba SHALL apresentar as
  contagens medidas em `0` e os valores não coletados no estado de ausência,
  porque o período foi **medido e vazio**;
- **agente inativo** — a aba SHALL apresentar normalmente o que ele executou.
  Inatividade é estado de cadastro, não ausência de sujeito, e a aba SHALL NOT
  sumir, esvaziar-se nem sinalizar erro por causa dela.

#### Scenario: Recusa do identificador não vira período vazio
- **WHEN** a rota agregada recusa o identificador do agente
- **THEN** a aba apresenta o estado de recusa, **nenhum** `0` aparece na tela, e
  o texto do período medido e vazio **não** é apresentado

#### Scenario: Agente sem dado no período mostra zero medido
- **WHEN** a rota responde para um agente existente com todas as contagens em `0`
- **THEN** os `0` aparecem como contagem feita, e a aba **não** apresenta o
  estado de recusa nem o de consulta sem resposta

#### Scenario: Agente inativo continua com aba cheia
- **WHEN** o agente consultado está inativo e executou tasks na janela
- **THEN** a aba apresenta os números normalmente, e **não** apresenta nenhum
  estado de indisponibilidade

#### Scenario: Recusa é distinguível de consulta sem resposta
- **WHEN** a rota recusa o identificador
- **THEN** o texto apresentado difere do texto de consulta sem resposta, e
  **nenhuma** ação de nova tentativa é oferecida ao lado dele

### Requirement: A gramática dos quatro estados de valor vale na aba

O sistema SHALL distinguir, em toda superfície da aba que apresente valor
numérico, os mesmos quatro estados já fixados para a página do escopo do sistema,
e SHALL NOT colapsar um no outro:

- **valor medido** — o número;
- **célula vazia** — há linha e não há o que dizer, porque a fonte não reporta
  aquele número;
- **dado desconhecido** — travessão, **sempre acompanhado da razão**, quando a
  consulta não respondeu ou ainda corre;
- **zero contado** — `0`, reservado à contagem feita e a mais nada.

O sistema SHALL NOT apresentar `0` onde a origem é nula ou ausente, e SHALL NOT
apresentar valor algum enquanto a consulta não tiver respondido — uma requisição
que não respondeu não é evidência de ausência.

As asserções que protegem esta regra SHALL ser **negativas**: afirmar que **não**
há `0` onde a fonte é nula, em vez de afirmar apenas que algo vazio foi
renderizado.

#### Scenario: Fonte nula não vira zero
- **WHEN** a resposta traz um valor nulo para um número apresentado na aba
- **THEN** a célula correspondente fica vazia, e **nenhum** `0` aparece nela

#### Scenario: Zero medido não vira célula vazia
- **WHEN** a resposta traz uma contagem medida igual a `0`
- **THEN** o `0` é apresentado como contagem feita, e a célula **não** fica vazia

#### Scenario: Consulta em curso não afirma ausência
- **WHEN** a consulta da aba ainda não respondeu
- **THEN** todos os números aparecem como dado desconhecido com a razão ao lado,
  e **nenhum** `0` é apresentado

#### Scenario: Consulta sem resposta não afirma ausência
- **WHEN** a consulta da aba falha
- **THEN** todos os números aparecem como dado desconhecido com a razão ao lado,
  a aba oferece nova tentativa, e **nenhum** `0` é apresentado

### Requirement: Métrica que muda de significado neste escopo recebe rótulo próprio

O sistema SHALL rotular, nas superfícies da aba, o significado que cada métrica
tem **no escopo de um agente**, e SHALL NOT reaproveitar o rótulo da mesma
métrica no escopo do sistema.

Em particular:

- a distribuição por **provedor e por modelo** descreve o histórico de
  configuração de **um** agente. Mais de um valor ali SHALL ser apresentado como
  **mudança de configuração daquele agente dentro da janela**, e SHALL NOT ser
  apresentado como comparação entre agentes;
- **profundidade de delegação**, se apresentada, SHALL ser rotulada como a maior
  profundidade **em que aquele agente executou** — uma posição na cadeia — e
  SHALL NOT ser rotulada como profundidade alcançada pelo sistema nem como
  tamanho de cadeia.

#### Scenario: Dois modelos no período são mudança de configuração
- **WHEN** a resposta traz dois modelos na distribuição do agente
- **THEN** a tela os apresenta como histórico de configuração daquele agente, e o
  texto **não** os apresenta como comparação entre agentes

#### Scenario: Um modelo só não afirma que nunca mudou
- **WHEN** a resposta traz um único modelo na distribuição do agente
- **THEN** a tela apresenta a linha e **não** afirma que a configuração do agente
  é imutável nem que ela vale fora da janela

#### Scenario: Profundidade não herda o rótulo do sistema
- **WHEN** a profundidade de delegação do agente é apresentada em alguma
  superfície da aba
- **THEN** o rótulo diz que é a profundidade em que **aquele agente** executou, e
  **não** a profundidade alcançada pelo sistema

### Requirement: O que este escopo não sustenta não aparece, e o que o protótipo não desenha não ganha elemento novo

O sistema SHALL NOT apresentar, na aba do agente, métrica cuja fonte não tem
vínculo com agente — as falhas de indexação de conhecimento e os tokens de
embedding de indexação —, e SHALL NOT apresentá-las como lista vazia, contagem
`0` ou lacuna declarada: uma lista vazia é o texto de *"medi e não achei nada"*,
e afirmaria medição onde não há fonte.

O sistema SHALL NOT apresentar, na aba do agente, agrupamento **por agente**: o
recorte já é o agente, e um agrupamento de um elemento só com nome de comparação
é a métrica do escopo do sistema ressuscitada por descuido.

Onde o protótipo aprovado desenha um elemento cuja fonte a rota não serve, o
sistema SHALL seguir a regra já fixada para esta família de telas: **subtítulo**
sem fonte vira lacuna declarada no mesmo peso do subtítulo; **coluna** sem fonte
sai sem deixar quadro no lugar. O sistema SHALL NOT acrescentar elemento novo,
que o artboard não tem, só para anunciar o que falta — o que sobrevive ao archive
é a issue.

Métrica que a rota **serve** e o protótipo **não desenha** SHALL NOT ganhar
elemento novo na aba por iniciativa da implementação; acrescentá-la é divergência
com o protótipo e SHALL ser registrada como tal antes de existir na tela.

#### Scenario: Falha de indexação não aparece nem como lista vazia
- **WHEN** a aba é apresentada para um agente vinculado a uma base que teve
  tentativa de indexação falha na janela
- **THEN** **nenhum** elemento de falha de indexação aparece na aba, nem
  preenchido, nem vazio, nem como lacuna declarada

#### Scenario: Nenhum agrupamento por agente na aba do agente
- **WHEN** a aba é apresentada
- **THEN** **nenhuma** superfície agrupa números por agente

#### Scenario: Coluna sem fonte sai sem deixar quadro
- **WHEN** o protótipo desenha uma coluna cuja fonte a rota não serve
- **THEN** a coluna não aparece, e **nenhum** elemento novo é acrescentado ao
  card para anunciá-la

#### Scenario: Métrica servida sem elemento no protótipo não ganha um
- **WHEN** a rota serve uma métrica para a qual nenhum artboard desta superfície
  desenha elemento
- **THEN** a aba **não** a apresenta, e **nenhum** grupo, quadro ou rodapé é
  criado para ela

### Requirement: Códigos de parcialidade renderizados junto do número que limitam, com posição própria desta superfície

O sistema SHALL tornar disponível o texto de cada código de parcialidade **junto
do número que ele limita nesta aba**, e SHALL NOT apresentá-los numa lista
separada dos valores a que se aplicam.

O texto PODE ser apresentado diretamente ou atrás de um recurso de leitura
**visível** no mesmo lugar. O que SHALL NOT acontecer é o código chegar e não
haver, junto do número, nem o texto nem um recurso visível que o dê.

A **posição** de um código SHALL ser determinada por superfície, e SHALL NOT ser
herdada da página do escopo do sistema: o mesmo código limita elementos
diferentes nas duas telas, e um código pode ter número nesta aba e não ter
naquela página, ou o contrário.

Um código cujo número **não é apresentado nesta aba** SHALL NOT ser renderizado:
um texto de limitação sem o número que ele limita não tem o que qualificar. Essa
classificação SHALL ser explícita, e SHALL NOT ser silêncio por omissão.

Um código que a tela não conhece SHALL ser apresentado com o próprio código
visível, como aviso, e SHALL NOT ser descartado em silêncio.

#### Scenario: O código aparece junto do número que limita nesta aba
- **WHEN** a resposta traz um código de parcialidade conhecido cujo número a aba
  apresenta
- **THEN** o texto dele aparece junto desse número

#### Scenario: A posição não é a da página do sistema
- **WHEN** um mesmo código de parcialidade é apresentado nas duas superfícies
- **THEN** a posição dele em cada uma é a do número que ele limita **naquela**
  superfície

#### Scenario: Código sem número nesta aba não é renderizado
- **WHEN** a resposta traz um código que qualifica uma métrica que esta aba não
  apresenta
- **THEN** nenhum texto de limitação aparece solto na aba

#### Scenario: Código desconhecido aparece como aviso
- **WHEN** a resposta traz um código de parcialidade que a tela não conhece
- **THEN** o próprio código é apresentado de forma visível, e **não** é
  descartado

### Requirement: Janela, regime e cobertura da faixa medida na aba

O sistema SHALL apresentar, no topo da aba, a **janela efetivamente consultada** e
o instante de início do regime que governa os números da aba, e SHALL tomar esses
dois valores da **resposta**, não de cálculo local, sempre que a resposta tiver
chegado.

O operador SHALL poder escolher o período entre as mesmas opções já oferecidas na
página do escopo do sistema, e a janela SHALL ser recalculada no instante da
consulta, nunca no instante do render.

Quando a janela pedida começa **antes** do início da medição, o sistema SHALL
declarar quantos dos dias pedidos têm medida e quantos não existem, e SHALL
afirmar que os dias sem medida **não são dias sem uso**.

A cobertura por dia da semana SHALL ser decidida pela **série diária**: dia da
semana que ocorre entre os dias medidos e não tem ocorrência SHALL aparecer com
`0`; dia da semana que não ocorre entre os dias medidos SHALL aparecer sem
número, e SHALL NOT receber `0`.

A distinção entre o dia medido e vazio e o dia não medido SHALL ser legível na
**apresentação do próprio valor**, e SHALL NOT depender de texto explicativo ao
pé do agrupamento.

#### Scenario: A janela vem da resposta
- **WHEN** a resposta da aba chega
- **THEN** o período apresentado no topo é o que a resposta ecoa, e o fuso usado
  para formatá-lo é o que ela declara

#### Scenario: Janela maior que a medição é declarada
- **WHEN** a janela pedida começa antes do início do regime que governa a aba
- **THEN** a aba declara quantos dias têm medida e quantos não existem, e afirma
  que os que não existem **não** são dias sem uso

#### Scenario: Dia da semana fora da faixa medida não recebe zero
- **WHEN** um dia da semana não ocorre entre os dias medidos da janela
- **THEN** ele aparece sem número, e **nenhum** `0` é apresentado para ele

#### Scenario: A distinção não depende de prosa
- **WHEN** a janela cobre um dia da semana sem ocorrência e deixa outro fora da
  faixa medida
- **THEN** os dois são distinguíveis pela apresentação do valor — `0` num, nada
  no outro —, sem que nenhum texto ao pé do agrupamento precise explicá-la

### Requirement: Falha e recusa são populações distintas, e a recusa de entrada não ganha elemento

O sistema SHALL apresentar, na aba, as falhas do agente e as recusas dele como
populações **distintas**, e SHALL NOT somá-las num número só.

A rota serve **três** populações, e a aba apresenta **duas**:

- **falhas** — execuções que chegaram a estado de falha. É o numerador do
  percentual de falha, e SHALL ser apresentada;
- **recusas com linha de execução** — contadas pelas tabelas de métrica. SHALL
  ser apresentadas, SHALL NOT entrar no percentual de falha nem no numerador nem
  no denominador, e SHALL ter o código de parcialidade que declara essa exclusão
  disponível junto delas;
- **recusas de entrada**, com o seu regime de medição próprio e os seus motivos
  — o protótipo aprovado **não tem elemento para elas**, e por isso o sistema
  SHALL NOT criar um. Elas SHALL ficar registradas como métrica servida e não
  apresentada, com gatilho, pela regra que governa esta superfície.

O sistema SHALL NOT somar as duas recusas num número só em nenhuma circunstância:
elas vêm de **regimes de medição diferentes**, e um número único juntaria duas
janelas sob um rótulo só.

A **contagem de falhas** SHALL ser apresentada no cabeçalho do elemento que
explica as falhas, e SHALL NOT ser embutida no texto do título — o título SHALL
seguir os dois estados que o protótipo desenha: um para o período **com** falha e
outro para o período **sem** falha.

Quando não houve nenhuma falha no período, o sistema SHALL apresentar o estado de
ausência de falha com o total de tasks que chegaram a concluído, e SHALL NOT
apresentar uma tabela vazia.

#### Scenario: As duas populações apresentadas aparecem separadas
- **WHEN** o agente tem, na janela, falhas e recusas com linha de execução
- **THEN** os dois números aparecem distintos, e **nenhum** número apresentado é
  a soma deles

#### Scenario: Recusa não entra no percentual de falha
- **WHEN** o agente tem recusas na janela
- **THEN** o percentual de falha apresentado não as inclui, nem no numerador nem
  no denominador, e o texto que declara essa parcialidade está disponível junto
  do número de recusas

#### Scenario: A recusa de entrada não aparece, nem os motivos dela
- **WHEN** a resposta traz contagem de recusa de entrada e motivos para o agente
- **THEN** **nenhum** elemento da aba os apresenta, e **nenhum** número da aba os
  soma a outra população

#### Scenario: O título da explicação das falhas tem dois estados
- **WHEN** o período tem falha, e depois quando não tem
- **THEN** o título muda entre os dois estados que o protótipo desenha, e a
  contagem aparece no cabeçalho, fora do texto do título

#### Scenario: Período sem falha tem estado próprio
- **WHEN** o agente executou tasks na janela e nenhuma falhou
- **THEN** a aba apresenta o estado de ausência de falha nomeando quantas tasks
  chegaram a concluído, e **não** apresenta uma tabela vazia

### Requirement: Proporção desenhada não é composta a partir de conhecimento parcial

O sistema SHALL NOT desenhar área, barra ou fatia proporcional a partir de um
conjunto de parcelas em que alguma seja desconhecida ou ausente.

Quando o sistema apresentar o **total** de um conjunto de parcelas, o rótulo do
total SHALL dizer o que o número é. Um total que some médias medidas sobre
populações diferentes SHALL NOT ser rotulado como média nem como mediana da
grandeza que as parcelas compõem — a soma não produz nenhuma das duas.

O total SHALL ficar **sem valor** quando alguma parcela for desconhecida: um
total que ignora a parcela ausente afirma que ela não pesou nada.

Quando alguma parcela da composição não é conhecida, o sistema SHALL apresentar
as parcelas conhecidas com os seus próprios estados e SHALL **suprimir a
representação proporcional**, em vez de tratar a parcela desconhecida como zero —
uma área desenhada afirma uma proporção, e proporção com parcela nula afirma um
número que ninguém mediu.

#### Scenario: Parcela desconhecida suprime a barra
- **WHEN** uma das parcelas da composição de tempo é nula
- **THEN** a representação proporcional não é desenhada, as parcelas conhecidas
  continuam apresentadas com os seus valores, e **nenhuma** parcela é desenhada
  com largura zero no lugar da desconhecida

#### Scenario: Todas as parcelas conhecidas desenham a composição
- **WHEN** todas as parcelas da composição de tempo são conhecidas
- **THEN** a representação proporcional é desenhada a partir delas, e o total
  delas é apresentado

#### Scenario: O rótulo do total não promete a média da grandeza
- **WHEN** o total de parcelas medidas sobre populações diferentes é apresentado
- **THEN** o rótulo o identifica como a soma das parcelas, e **não** como média
  nem mediana da grandeza que elas compõem

#### Scenario: Parcela desconhecida deixa o total sem valor
- **WHEN** uma das parcelas da composição é desconhecida
- **THEN** o total é apresentado como ausente, e **não** como a soma das
  parcelas conhecidas

