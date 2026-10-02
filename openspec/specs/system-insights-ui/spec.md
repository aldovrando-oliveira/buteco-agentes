# system-insights-ui Specification

## Purpose

A página de Insights do escopo do sistema em `apps/frontend`: o que a operação
mediu, apresentado de modo que a tela **nunca afirme mais do que o sistema sabe**.

**A capability existe por causa de uma distinção, e é ela que organiza tudo o que
está abaixo:** um número ausente pode significar quatro coisas diferentes, e
colapsá-las é o modo de falha que esta tela existe para impedir.

| origem | apresentação | significado |
|---|---|---|
| número | o número | valor medido |
| ausente ou nulo | célula vazia | há linha, e não há o que dizer |
| consulta sem resposta | travessão, com a razão | dado desconhecido |
| zero | zero, dito por extenso onde cabe | contagem feita que deu zero |

As três etapas anteriores da linha `metricas-de-operacao` preservaram o nulo
ponta a ponta — da coleta ao agregado —, e **tudo isso pode ser desfeito por uma
linha na camada de apresentação**: um `?? 0` transforma "o provedor não
reportou" em "o provedor reportou zero", e o painel passa a afirmar o que
ninguém mediu. Por isso os guardas desta capability são **negativos**: eles
afirmam a AUSÊNCIA do zero onde a origem é nula, não a presença do vazio.

**A mesma disciplina se estende ao tempo.** Um dia sem ponto na série é um dia
não medido, e não um dia sem uso — a tela lê a série que o servidor serve em vez
de reconstruir a distinção a partir do regime, porque duas regras sobre "o que
foi medido" divergem em silêncio. E o "medindo desde" sai **por regime**: com
mais de um, um texto único mentiria sobre pelo menos um deles.

**E onde o protótipo aprovado pede um número que a rota não serve, a tela não
inventa.** Onde há elemento para carregar a declaração, ela declara; onde não
há, a métrica sai e o registro vive na issue — que é o que sobrevive ao archive.

Consome `GET /insights/system` em **uma única** requisição, mais o catálogo de
agentes para os nomes, em consulta independente.

## Requirements

### Requirement: Página Insights do sistema servida por uma consulta só

O sistema SHALL prover, em `apps/frontend`, uma página de Insights do escopo do
sistema, alcançável pela navegação da barra lateral e por rota própria.

A página SHALL obter os números de **uma única** requisição à rota agregada do
escopo do sistema. O sistema SHALL NOT repartir os números da página entre
rotas diferentes, nem recalcular no cliente qualquer agregado que a rota já
devolva — janela, fuso, tratamento de nulo e regime de medição são um contrato
só, e dois cards da mesma tela derivados de contratos diferentes divergiriam
sem que a tela tivesse como dizer qual está certo.

A página SHALL obter o **nome** de cada agente do catálogo de agentes, que a
rota agregada não devolve. Essa consulta SHALL ser independente da primeira:
cada uma SHALL apresentar o seu próprio estado de carregamento, o seu próprio
erro e a sua própria nova tentativa, e uma SHALL NOT segurar o conteúdo já
pronto da outra.

#### Scenario: A página é alcançável pela navegação
- **WHEN** o operador aciona o item Insights da barra lateral
- **THEN** a aplicação navega para a página de Insights do sistema, e o item
  passa a ser apresentado como ativo

#### Scenario: Uma requisição serve a página inteira
- **WHEN** a página de Insights é carregada
- **THEN** exatamente uma requisição é feita à rota agregada do escopo do
  sistema, e todos os números apresentados vêm dela

#### Scenario: O catálogo lento não segura os números
- **WHEN** a rota agregada responde e o catálogo de agentes ainda não respondeu
- **THEN** todos os números da página já aparecem, e apenas a coluna de nome da
  tabela por agente permanece em carregamento

#### Scenario: Falha do catálogo não derruba a página
- **WHEN** a rota agregada responde e o catálogo de agentes falha
- **THEN** a página apresenta os números normalmente e oferece nova tentativa
  apenas para o catálogo

### Requirement: Gramática dos quatro estados de valor

O sistema SHALL distinguir, em toda superfície que apresente valor numérico,
quatro estados que SHALL NOT ser colapsados um no outro:

| origem | apresentação | significado |
|---|---|---|
| número | o número | valor medido |
| ausente ou nulo | **célula vazia** | há linha, e não há o que dizer |
| consulta sem resposta | **travessão** | dado desconhecido |
| zero | **zero** | contagem feita que deu zero |

O sistema SHALL NOT apresentar `0` onde a origem é nula ou ausente, sob nenhuma
circunstância, nem como resultado de soma, média, percentual ou formatação.

O travessão SHALL ser acompanhado da razão pela qual o dado é desconhecido, para
que não seja lido como zero.

Onde o zero é a única informação de um card ou de uma seção, o sistema SHALL
apresentá-lo **por extenso**, em vez do algarismo, para que a diferença em
relação ao travessão seja legível em palavras e não só em símbolo.

#### Scenario: Valor medido aparece como número
- **WHEN** a rota devolve um número para uma métrica
- **THEN** a tela apresenta esse número

#### Scenario: Valor nulo não vira zero
- **WHEN** a rota devolve nulo para uma métrica
- **THEN** a célula correspondente fica vazia, e **nenhum** `0` é apresentado
  naquela posição

#### Scenario: Consulta sem resposta apresenta travessão com razão
- **WHEN** a consulta da página falha
- **THEN** a tela apresenta travessão no lugar dos números, acompanhado da razão
  da falha e de uma ação de nova tentativa, e **nenhum** `0` é apresentado

#### Scenario: Zero medido é dito por extenso
- **WHEN** a rota devolve contagem zero para o período e a consulta respondeu
- **THEN** a tela afirma em palavras que não houve registro no período, em vez de
  apresentar o algarismo isolado

#### Scenario: Soma de parcelas nulas não produz zero
- **WHEN** todas as parcelas de um total são nulas
- **THEN** o total fica vazio, e **nenhum** `0` é apresentado

### Requirement: Período não medido é distinguível de período medido sem uso

O sistema SHALL distinguir período não medido de período medido sem uso, e SHALL
fazê-lo lendo a série diária que a rota devolve — **não** reconstruindo a
distinção a partir do instante de início do regime.

A rota emite um ponto para **todo** dia medido, inclusive o dia medido em que
nada aconteceu, e omite os dias anteriores ao início do regime. A ausência de um
dia na série SHALL, portanto, significar **uma** coisa: não medido.

Para cada dia da janela pedida, o sistema SHALL apresentar:

- o valor medido, quando o dia tem ponto na série com contagem maior que zero;
- **zero medido**, quando o dia tem ponto na série com contagem zero;
- **não medido**, com tratamento visual próprio e distinto do zero, quando o dia
  **não** tem ponto na série.

O sistema SHALL NOT apresentar contagem para nenhum dia ausente da série —
fazê-lo inventaria um período de inatividade que nunca existiu.

O sistema SHALL NOT derivar a condição de "medido" do mapa de regimes. O regime
é configuração do servidor, e o cliente só o conhece porque a resposta o declara;
rederivar no cliente uma condição que o servidor já resolveu cria duas regras que
divergem em silêncio.

O dia em que a medição começou SHALL ser apresentado como medido, e o instante de
início SHALL ser declarado em texto na página, de modo que a medição parcial desse
primeiro dia seja legível sem inventar um estado visual para ela.

Toda aritmética de dia SHALL usar o fuso horário que a resposta declara, e SHALL
NOT usar o fuso do navegador.

#### Scenario: Dia ausente da série aparece como não medido
- **WHEN** a janela pedida contém dias que não aparecem na série diária
- **THEN** esses dias recebem o tratamento de não medido, e **nenhum** deles
  apresenta contagem

#### Scenario: Dia presente com contagem zero aparece como zero medido
- **WHEN** um dia aparece na série diária com contagem zero
- **THEN** esse dia é apresentado como zero medido, com tratamento distinto do
  dia não medido

#### Scenario: O mapa de regimes não decide célula
- **WHEN** a série diária e o mapa de regimes discordam sobre quais dias foram
  medidos
- **THEN** a apresentação segue a série, e o mapa de regimes é usado apenas para
  o texto de início da medição

#### Scenario: O primeiro dia medido aparece como medido
- **WHEN** a medição começou durante um dia da janela e esse dia aparece na série
- **THEN** o dia é apresentado como medido, e o instante de início aparece em
  texto na página

#### Scenario: O fuso da resposta decide o dia
- **WHEN** a página é aberta por um navegador em fuso diferente do que a resposta
  declara
- **THEN** os dias apresentados são os do fuso da resposta, não os do navegador

### Requirement: "Medindo desde" é declarado por regime, não por tela

O sistema SHALL apresentar o início da medição **por regime**, junto do grupo de
métricas que pertence a ele.

A resposta traz um **mapa** de regimes de medição, e cada grupo de métricas
declara a qual regime pertence.

O sistema SHALL NOT apresentar um único "medindo desde" para a página inteira —
com mais de um regime, um texto único mentiria sobre pelo menos um deles.

O sistema SHALL absorver um regime novo na resposta sem mudança de estrutura, e
SHALL NOT depender da quantidade nem dos nomes dos regimes conhecidos hoje.

#### Scenario: Cada regime aparece junto do grupo que ele mede
- **WHEN** a resposta traz mais de um regime de medição
- **THEN** cada grupo de métricas apresenta o início do regime a que pertence

#### Scenario: Regime novo é absorvido sem mudança de estrutura
- **WHEN** a resposta traz um regime que a tela não conhecia
- **THEN** o grupo que o declara apresenta o início dele normalmente

#### Scenario: Nenhum "medindo desde" único para a página
- **WHEN** o operador visualiza o cabeçalho da página
- **THEN** o cabeçalho apresenta a janela pedida, e o início da medição aparece
  por regime e não como afirmação única sobre a página

### Requirement: Janela do período escolhida pelo operador

O sistema SHALL oferecer ao operador a escolha do período e SHALL calcular a
partir dela os dois limites que a rota exige — não existe período implícito.

A janela SHALL ser **rolante em instantes** — N × 24 h terminando no instante da
consulta — e SHALL NOT ser de calendário, que obrigaria a decidir de qual fuso é
a meia-noite e mudaria de tamanho com o horário de verão.

A janela SHALL ser calculada **no instante da consulta**, e SHALL NOT fazer parte
da chave de cache: uma janela calculada a cada render geraria chave nova a cada
render, e a página ficaria permanentemente em carregamento.

#### Scenario: Trocar de período refaz a consulta
- **WHEN** o operador escolhe outro período
- **THEN** a página consulta a rota com a janela correspondente e apresenta os
  números do novo período

#### Scenario: Nova tentativa consulta a janela atualizada
- **WHEN** o operador aciona nova tentativa depois de uma falha
- **THEN** a janela é recalculada no instante dessa tentativa

#### Scenario: A página não fica presa em carregamento
- **WHEN** a página é renderizada repetidamente sem troca de período
- **THEN** apenas uma consulta é feita para aquele período

### Requirement: Dia da semana só afirma o que a faixa medida cobre

O sistema SHALL apresentar a atividade por dia da semana e SHALL marcar o dia de
pico que a resposta declara.

A cobertura de um dia da semana SHALL ser derivada da **série diária**, que é a
única fonte de quais dias foram medidos. A série por dia da semana omite o dia
sem ocorrência, e a série diária é o que diz se aquele dia chegou a ser medido.

Um dia da semana que **ocorre** entre os dias da série e não teve atividade SHALL
ser apresentado como zero medido. Um dia da semana que **não ocorre** entre os
dias da série SHALL ser apresentado como não medido, e o sistema SHALL NOT
apresentá-lo como zero — nunca houve medição dele para dar zero.

Quando a resposta não declara dia de pico, o sistema SHALL NOT eleger um.

#### Scenario: Dia da semana sem atividade entre os dias medidos
- **WHEN** um dia da semana ocorre entre os dias da série diária e não aparece na
  série por dia da semana
- **THEN** ele é apresentado como zero medido

#### Scenario: Dia da semana que não ocorre entre os dias medidos
- **WHEN** a série diária é curta demais para conter um dia da semana
- **THEN** esse dia é apresentado como não medido, e **nenhum** zero é
  apresentado para ele

#### Scenario: Sem pico declarado, nenhum pico é marcado
- **WHEN** a resposta não declara dia de pico
- **THEN** nenhum dia aparece marcado como pico

### Requirement: Mapa de calor do período com quatro estados de célula

O sistema SHALL apresentar um mapa de calor dos dias da janela, em que cada
célula é um dia local, e SHALL distinguir quatro estados de célula: valor medido
na escala de intensidade, zero medido com passo próprio, dia não medido com
tratamento visual distinto dos dois anteriores, e posição fora da janela sem
célula.

A intensidade SHALL ser calculada a partir da série efetivamente medida, e SHALL
NOT usar limiares fixos, que só valem para um conjunto de dados.

O dia não medido SHALL NOT usar nenhum passo da escala de intensidade.

#### Scenario: Célula de dia medido usa a escala
- **WHEN** um dia da janela tem contagem medida maior que zero
- **THEN** a célula correspondente recebe um passo da escala de intensidade,
  proporcional à contagem dentro da série medida

#### Scenario: Célula de dia não medido não usa a escala
- **WHEN** um dia da janela é anterior ao início do regime
- **THEN** a célula correspondente recebe o tratamento de não medido, distinto do
  passo de zero medido e de todos os passos de intensidade

#### Scenario: Série com um único valor não quebra a escala
- **WHEN** todos os dias medidos têm a mesma contagem
- **THEN** todas as células medidas recebem um passo válido da escala

### Requirement: Consumo por provedor e por modelo preserva a célula vazia

O sistema SHALL apresentar o consumo por provedor e o consumo por modelo como
tabelas, e SHALL preservar a célula vazia onde a resposta traz nulo.

Uma célula vazia nessas tabelas SHALL significar que aquele provedor não atende
aquele tipo de chamada neste sistema, ou não reporta aquele número — e o sistema
SHALL declarar isso em texto junto da tabela, para que a célula não seja lida
como consumo zero.

O total de uma linha SHALL somar apenas as parcelas conhecidas, e SHALL ficar
vazio quando todas forem nulas.

#### Scenario: Provedor sem aquele tipo de chamada tem célula vazia
- **WHEN** a resposta traz nulo para uma das colunas de um provedor
- **THEN** a célula correspondente fica vazia, e **nenhum** `0` é apresentado nela

#### Scenario: O significado da célula vazia é declarado
- **WHEN** o operador visualiza a tabela de consumo por provedor
- **THEN** um texto junto da tabela declara que célula vazia não significa
  consumo zero

#### Scenario: Total soma só o que é conhecido
- **WHEN** uma das parcelas do total de uma linha é nula e a outra não
- **THEN** o total apresenta a soma das parcelas conhecidas

### Requirement: Consumo por agente cruza com o catálogo e leva ao diagnóstico

O sistema SHALL apresentar o consumo por agente como tabela, cruzando os
identificadores que a rota agregada devolve com os nomes do catálogo de agentes.

**A população das linhas SHALL ser a união dos agentes que a resposta mede por
consumo com os agentes que ela mede por falha.** Agente que falhou e **não** tem
consumo medido SHALL ter linha, com a célula de consumo apresentada como **valor
não medido** — nunca `0`, e nunca com valor derivado de outro nível de agregação.

**A razão de a população ser a união, e não só o consumo:** a contagem de falhas de
um agente é apresentada **na linha dele**, então agente sem linha é agente com as
falhas **ausentes da tela**. Ausência de linha é o sintoma mais difícil de notar —
ninguém repara numa linha que não existe —, e a população que ela esconde
preferencialmente é a da falha anterior à primeira chamada de provedor, que é
justamente a falha de configuração.

**A contagem de falhas apresentada para um agente SHALL ser a SOMA de todas as
linhas de falha que a resposta serve para aquele agente.** A resposta agrupa a
falha por agente **e** por atributos de execução, então um agente pode chegar em
mais de uma linha; apresentar uma delas SHALL NOT ocorrer, e apresentar a de menor
contagem — que é onde a ordenação da resposta a deixa — SHALL NOT ocorrer.

Um agente presente na agregação e ausente do catálogo SHALL ter a linha
preservada, com o identificador apresentado em lugar do nome — o consumo dele é
real, e o que falta é o rótulo, não o número.

O nome do agente SHALL levar à **superfície de diagnóstico daquele agente** — a
aba de Insights do detalhe —, e não a uma superfície a partir da qual o operador
ainda precise escolher. A tabela responde **qual** agente olhar; a superfície de
destino responde **o que aconteceu com ele**, e a passagem entre as duas é de um
acionamento só.

**O destaque da contagem de falhas SHALL ser calculado sobre a mesma população que
a tabela apresenta.** Destacar a maior contagem de um subconjunto das linhas
apresentadas SHALL NOT ocorrer.

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

#### Scenario: Agente que falhou sem consumo medido tem linha
- **WHEN** a resposta mede falhas para um agente que **não** aparece na agregação
  de consumo
- **THEN** o agente aparece na tabela, com a contagem de falhas dele apresentada
- **AND** a célula de consumo dele é apresentada como valor **não medido**, e não
  como `0`

#### Scenario: A contagem de falhas de um agente é a soma das linhas dele
- **WHEN** a resposta serve **mais de uma** linha de falha para o mesmo agente,
  com contagens diferentes
- **THEN** a tabela apresenta a **soma** das contagens daquele agente
- **AND** NÃO apresenta a contagem de nenhuma linha isolada

#### Scenario: O destaque considera a população inteira
- **WHEN** a linha de maior contagem de falhas é a de um agente que entrou na
  tabela por falha, sem consumo medido
- **THEN** é essa linha que recebe o destaque

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

### Requirement: Tasks sem estado terminal apresentadas como duas populações do instante

O sistema SHALL apresentar o aviso de tasks sem estado terminal com as **duas
populações separadas** que a resposta declara — execução aberta e task nunca
consumida —, porque elas têm causas diferentes e somá-las apagaria a causa.

O aviso SHALL declarar que a leitura é **do instante da consulta**, conforme o
código de parcialidade que a resposta traz, e SHALL NOT afirmar idade nem
duração do estado, que a resposta não mede.

O aviso SHALL NOT oferecer atalho enquanto não existir uma listagem que apresente
exatamente essas tasks.

O aviso SHALL aparecer apenas quando pelo menos uma das duas contagens for maior
que zero.

#### Scenario: As duas populações aparecem separadas
- **WHEN** a resposta traz as duas contagens maiores que zero
- **THEN** o aviso apresenta cada uma com a sua contagem e a sua causa

#### Scenario: O aviso declara que é leitura do instante
- **WHEN** o aviso é apresentado
- **THEN** ele declara que a contagem é do instante da consulta, e **não** afirma
  há quanto tempo as tasks estão nesse estado

#### Scenario: Zeros medidos não geram aviso
- **WHEN** as duas contagens são zero
- **THEN** nenhum aviso de tasks sem estado terminal é apresentado

### Requirement: Códigos de parcialidade renderizados junto do número que limitam

A resposta declara códigos estáveis de parcialidade. O sistema SHALL apresentar
o texto de cada código **junto do número que ele limita**, e SHALL NOT
apresentá-los numa lista separada do valor a que se aplicam.

Um código que a tela não conhece SHALL ser apresentado com o próprio código
visível, como aviso, e SHALL NOT ser descartado em silêncio.

Um código cujo número não é apresentado nesta página SHALL NOT ser renderizado —
um texto de limitação sem o número que ele limita não tem o que qualificar.

#### Scenario: O código aparece junto do número que limita
- **WHEN** a resposta traz um código de parcialidade conhecido
- **THEN** o texto dele aparece junto do número correspondente na tela

#### Scenario: Código desconhecido aparece como aviso
- **WHEN** a resposta traz um código de parcialidade que a tela não conhece
- **THEN** o próprio código é apresentado de forma visível, e **não** é
  descartado

#### Scenario: Código sem número correspondente não é renderizado
- **WHEN** a resposta traz um código que qualifica uma métrica que esta página
  não apresenta
- **THEN** nenhum texto de limitação aparece solto na página

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
