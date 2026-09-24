## ADDED Requirements

### Requirement: Rota única de agregação no escopo de um agente

O sistema SHALL expor, em `apps/api`, uma rota HTTP de leitura
`GET /insights/agents/{id}` que recebe uma janela de período e responde `200` com
um **único objeto agregado** contendo as métricas do catálogo recortadas para
aquele agente.

A rota SHALL herdar, **sem reabrir**, o contrato de janela, balde diário,
preservação de nulo e mapa de regimes já fixado pela capability do escopo do
sistema. Os dois escopos SHALL usar a **mesma** interpretação de limites e o
**mesmo** fuso: um contrato repartido entre duas rotas é o mesmo defeito que
justificou uma rota única em cada escopo.

A rota NÃO SHALL ser a rota do escopo do sistema com um filtro adicional. Ela é
**outro conjunto de consultas**: há métrica do escopo do sistema que não existe
neste escopo, há métrica que muda de significado, e há métrica cuja fonte troca
de tabela.

Toda agregação SHALL ser calculada **no banco de dados**, e a rota NÃO SHALL
devolver linha bruta das tabelas de métrica.

A rota SHALL exigir autenticação, como toda rota de `apps/api` que não esteja
explicitamente classificada como anônima — e, por isso, NÃO SHALL ser
acrescentada à allowlist de rotas anônimas, cuja validação de startup reprovaria
o boot ao encontrar ali um padrão que não está mapeado como anônimo.

#### Scenario: A rota responde o agregado do agente no período

- **WHEN** a rota é chamada com o identificador de um agente existente e uma
  janela válida
- **THEN** a resposta é `200` com um objeto único cobrindo as métricas do escopo
  daquele agente, e nenhuma linha bruta das tabelas de métrica aparece no corpo

#### Scenario: O recorte por agente exclui o que é de outro agente

- **WHEN** existem execuções de dois agentes distintos dentro da janela
- **THEN** o agregado devolvido para um deles não inclui nenhuma ocorrência do
  outro

#### Scenario: A rota exige token

- **WHEN** a rota é chamada sem token
- **THEN** a resposta é `401`, sem que nenhuma classificação de rota precise ter
  sido declarada para isso

### Requirement: Agente inexistente é recusado, e agente sem dado não é

O sistema SHALL responder `404` quando o identificador recebido não corresponder
a nenhum agente cadastrado, e NÃO SHALL responder `200` com um agregado zerado.

Um agregado zerado para um identificador que não existe afirmaria uma medição que
não aconteceu sobre um sujeito que não existe — é a mesma família do `0` emitido
onde deveria haver ausência, e a distinção entre *"não sei"*, *"medi e não achei
nada"* e *"sei que não existe"* é o que esta família de capabilities existe para
preservar.

O sistema SHALL responder `200` quando o agente **existir e não tiver nenhum dado
no período**, com as contagens medidas em `0` e os valores não coletados
ausentes. Um agente existente sem uso é um período **medido e vazio**, que é
outra coisa.

O sistema SHALL responder `200` quando o agente existir e estiver **inativo**. A
inatividade é estado de cadastro, não ausência de sujeito, e o que ele executou
enquanto ativo continua tendo sido medido.

#### Scenario: Identificador que não corresponde a agente algum

- **WHEN** a rota é chamada com um identificador bem formado que não existe na
  tabela de agentes
- **THEN** a resposta é `404`, e nenhum agregado é devolvido

#### Scenario: Agente existente sem nenhuma ocorrência no período

- **WHEN** a rota é chamada para um agente existente cuja janela está inteiramente
  dentro do regime de medição e não contém ocorrência alguma
- **THEN** a resposta é `200`, as contagens medidas chegam `0` e os valores não
  coletados chegam ausentes ou nulos

#### Scenario: Agente inativo continua sendo consultável

- **WHEN** a rota é chamada para um agente que existe e está inativo
- **THEN** a resposta é `200` com o agregado do que ele executou, e não `404`

### Requirement: Os dois lados da delegação leem fontes diferentes e podem divergir

O sistema SHALL reportar, para o agente consultado, **dois** conjuntos de
delegação com fontes distintas:

- **o lado de quem delega** — as delegações que o agente **tentou**, lidas da
  tabela de resultados de delegação pelo identificador de agente de **origem**,
  agrupadas por agente de destino, e discriminadas por resultado;
- **o lado de quem é delegado** — as execuções que de fato **rodaram** no agente
  por delegação, lidas da tabela de execuções pelo identificador de agente do
  **próprio agente** com origem de delegação, agrupadas por agente de origem.

O sistema NÃO SHALL derivar um lado do outro, e NÃO SHALL apresentá-los como duas
vistas da mesma contagem. Os dois lados da **mesma relação entre os mesmos dois
agentes** SHALL poder mostrar **números diferentes**, e isso é resultado correto,
não defeito: um conta tentativa, o outro conta execução.

As causas da divergência SHALL ser tratadas como parte do contrato, e são duas,
independentes:

- **resultado de delegação que não produz execução** — uma delegação que nunca
  chegou a criar a task do destino não tem execução para contar do outro lado, e
  uma que expirou pode ter criado uma task que nunca rodou;
- **as duas janelas são medidas em relógios diferentes** — o lado de quem delega
  é situado no tempo pela execução **de origem**, e o lado de quem é delegado pela
  execução **de destino**. Uma delegação disparada perto do limite da janela pode
  ter os seus dois lados em dias, ou em janelas, diferentes.

O sistema SHALL tornar a assimetria legível para o cliente em vez de deixá-la
implícita, declarando-a como parcialidade nomeada junto do conjunto de delegação.

Este requisito SHALL ter guarda próprio, e o guarda SHALL **afirmar a
divergência** — exercitar um cenário em que os dois lados da mesma relação
discordam e asserir que eles discordam. Um guarda que afirmasse igualdade
reprovaria o comportamento correto, e a asserção da divergência é o que impede
alguém "consertar" os dois lados para baterem.

#### Scenario: Delegação que não gerou execução divergindo dos dois lados

- **WHEN** um agente registrou uma delegação para outro cujo resultado não
  produziu execução no destino, e a janela cobre a execução de origem
- **THEN** o lado de quem delega conta essa delegação, o lado de quem é delegado
  do agente de destino não a conta, e a resposta não apresenta os dois números
  como se devessem coincidir

#### Scenario: Os dois lados coexistem para o mesmo agente

- **WHEN** o agente consultado tanto delegou para outros quanto executou por
  delegação de outros na janela
- **THEN** os dois conjuntos chegam preenchidos e separados, cada um com a sua
  fonte, sem que um seja derivado do outro

#### Scenario: Agente que só delega

- **WHEN** o agente consultado delegou na janela e não executou nenhuma task por
  delegação
- **THEN** o lado de quem delega chega preenchido e o lado de quem é delegado
  chega **vazio**, distinguível de ausência de medição

#### Scenario: Agente que só é delegado

- **WHEN** o agente consultado executou tasks por delegação na janela e não
  disparou nenhuma delegação
- **THEN** o lado de quem é delegado chega preenchido e o lado de quem delega
  chega **vazio**, distinguível de ausência de medição

### Requirement: Métrica que o escopo por agente não sustenta é omitida, nunca estimada

O sistema SHALL omitir do agregado por agente toda métrica do catálogo cuja fonte
**não tenha vínculo com agente**, e NÃO SHALL atribuí-la ao agente por caminho
indireto.

As métricas nesta condição, e a causa de cada uma:

- **falhas de indexação de conhecimento** — a tabela de tentativas de indexação
  tem documento e base, e **nenhuma coluna de agente**. Indexação é trabalho da
  base, não de um agente;
- **tokens de embedding de indexação** — pela mesma causa, já que a chamada de
  embedding de indexação pendura na tentativa de indexação e não em execução
  alguma.

O sistema NÃO SHALL atribuir esse trabalho aos agentes vinculados à base pelo
vínculo de conhecimento: a indexação aconteceu **uma vez** e o vínculo é de
muitos para muitos, então distribuí-la contaria a mesma medição em cada agente
vinculado e afirmaria como trabalho do agente um trabalho que não foi dele.

A métrica de **tokens de embedding de busca** SHALL existir no escopo do agente,
porque a chamada de embedding de busca pendura na execução, que tem agente — e
SHALL declarar que cobre **apenas** a busca, nunca a indexação, para que o número
não seja lido com o rótulo do todo.

A métrica **"por agente"** do escopo do sistema NÃO SHALL existir no escopo de um
agente: o recorte já é o agente, e reproduzi-la seria um agrupamento de um
elemento só com o nome de uma comparação.

#### Scenario: Falhas de indexação não aparecem no escopo do agente

- **WHEN** existe tentativa de indexação falha na janela, numa base vinculada ao
  agente consultado
- **THEN** ela não aparece no agregado daquele agente, e o agregado não apresenta
  nenhuma contagem de falha de indexação atribuída a ele

#### Scenario: Tokens de embedding cobrem só a busca, e dizem isso

- **WHEN** existem, na janela, chamadas de embedding de indexação numa base
  vinculada ao agente e chamadas de embedding de busca feitas dentro de execuções
  dele
- **THEN** o total de tokens de embedding do agente inclui apenas as de busca, e a
  resposta declara essa parcialidade

### Requirement: Métrica que muda de significado no escopo do agente é declarada, não reaproveitada

O sistema SHALL tratar como métrica **diferente** — não como a mesma consulta com
um filtro — toda métrica do catálogo cujo significado dependa do escopo, e SHALL
declarar o significado que ela tem neste escopo.

As métricas nesta condição, e o que mudam:

- **profundidade de delegação observada** — no escopo do sistema é a maior
  profundidade que o sistema alcançou; no escopo do agente é a maior profundidade
  **em que o agente executou**, que é uma posição na cadeia e não um tamanho de
  cadeia;
- **distribuição por provedor e por modelo** — no escopo do agente elas descrevem
  o histórico de configuração de um agente só, e mais de um valor ali significa
  que o agente **mudou** de provedor ou de modelo dentro da janela, nunca que
  agentes diferentes usam valores diferentes;
- **tasks sem estado terminal** — as duas populações continuam existindo e
  continuam distinguíveis, mas ambas SHALL ser recortadas pelo agente: a execução
  aberta pelo agente da execução, e a task nunca consumida pelo agente da task no
  store durável.

#### Scenario: Profundidade é a do agente, não a do sistema

- **WHEN** existe na janela uma cadeia de delegação mais profunda que a maior
  profundidade em que o agente consultado executou
- **THEN** a profundidade devolvida para o agente é a dele, menor que a do
  sistema no mesmo período

#### Scenario: Mais de um modelo no período é mudança de configuração

- **WHEN** o agente executou na janela com dois modelos diferentes
- **THEN** os dois aparecem na distribuição do agente, e a resposta os apresenta
  como histórico daquele agente, não como comparação entre agentes

### Requirement: As parcialidades herdadas valem também no escopo do agente

O sistema SHALL declarar, no agregado por agente, as mesmas parcialidades já
declaradas no escopo do sistema sempre que elas alcancem este escopo, e NÃO SHALL
apresentar como completa uma contagem que o recorte por agente não torna completa.

Permanecem valendo, com a causa inalterada:

- **recusas feitas antes de qualquer execução** não produzem linha de execução, e
  a contagem de recusas derivada das tabelas de métrica **subconta** também no
  escopo do agente. A parte que falta existe no store durável de tasks, que tem
  agente — e é por ter agente que a lacuna é **quantificável** aqui, sem que isso
  a corrija: continua sem provedor, sem modelo e sem motivo;
- **o motivo de uma recusa feita antes da execução não tem fonte nenhuma**;
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

#### Scenario: Carimbo ausente produz indefinido, não zero, também por agente

- **WHEN** existe execução do agente consultado cujo carimbo de submissão é nulo
- **THEN** a duração e o tempo de fila dessa execução ficam fora do cálculo como
  ausentes, e não entram como `0`

### Requirement: O balde, a janela e os regimes são os mesmos dos dois escopos

O sistema SHALL agrupar por dia, por dia da semana e por calendário usando o **dia
no fuso configurado**, e NÃO SHALL usar o dia em UTC, exatamente como no escopo do
sistema.

O sistema SHALL interpretar limite sem deslocamento de fuso como UTC e SHALL
normalizar para deslocamento zero **todo** instante que chegue ao driver do banco
— inclusive os que vêm de **configuração**, e não apenas os que vêm da query
string. Um valor de configuração entra por um caminho que a interpretação dos
limites não cobre, e o driver recusa deslocamento diferente de zero para coluna de
instante.

O sistema SHALL devolver os instantes de início de medição como **mapa de
regimes**, e a série diária SHALL omitir — nunca emitir `0` para — os dias
anteriores ao início do regime a que cada métrica pertence.

#### Scenario: Instante noturno cai no dia local correto também por agente

- **WHEN** existe execução do agente cujo instante, no fuso configurado, é de um
  dia, e em UTC é do dia seguinte
- **THEN** ela é contada no dia local, e o guarda reprova se o agrupamento for
  feito em UTC

#### Scenario: Instante de regime com deslocamento não vira erro interno

- **WHEN** o início de regime configurado carrega deslocamento de fuso diferente de
  zero e é ele que delimita a consulta, por ser posterior ao início pedido
- **THEN** a resposta é `200`, e não um erro interno

#### Scenario: Janela que começa antes do regime, no escopo do agente

- **WHEN** a janela pedida para um agente começa antes do início do regime de uma
  métrica
- **THEN** os dias anteriores ao início não aparecem na série dessa métrica, e o
  cliente consegue distinguir esses dias dos dias medidos e vazios
