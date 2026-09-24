# system-insights-aggregation Specification

## Purpose

Cobre a **rota de leitura `GET /insights/system`** de `apps/api`: a janela que ela
recebe, o balde diário em que o dado cai, o que a resposta diz quando o sistema
não sabe, e desde quando cada métrica está sendo medida. É a superfície que torna
consultável o que as coletas já gravam no escopo do sistema — sem recorte por
agente, provedor, modelo ou base.

Quatro coisas que esta capability afirma e que são decisão, não detalhe:

- **Uma rota, e não 27.** Janela, fuso, tratamento de nulo e "medindo desde" são
  **um contrato só**; reparti-los entre uma rota por métrica ou por grupo criaria
  N chances de discordarem sobre o mesmo balde.

- **A agregação acontece sempre no banco**, com custo independente do número de
  linhas existentes, e a rota nunca devolve linha bruta das tabelas de métrica.
  Empurrar a agregação para o cliente levaria milhares de linhas ao navegador e
  colapsaria a distinção entre nulo e zero que esta capability existe para
  preservar.

- **Nulo é preservado, e `0` é reservado a contagem medida.** Valor não coletado
  chega ausente ou nulo e não é normalizado para zero em ponto algum do caminho;
  `0` significa que a agregação percorreu o período e não encontrou nada. A régua
  é a **proveniência** do valor, nunca o tipo do campo — e é ela que sustenta as
  outras duas afirmações da mesma família: o balde diário é o **dia local**, nunca
  o dia UTC, e "medindo desde" é um **mapa de regimes** vindo de configuração,
  nunca do menor carimbo existente nos dados, para que um período medido e vazio
  não seja confundido com um período anterior à medição.

- **Métrica que a fonte não sustenta declara a sua parcialidade.** A rota devolve
  o que sabe e torna a lacuna legível, ou omite a métrica — nunca apresenta um
  número parcial com o rótulo do todo.

**Não cobre** o escopo por agente, que é de outra change e **não** é esta rota
filtrada — é outro conjunto de consultas —, nenhuma tela, nem a coleta das
métricas, que aqui é apenas lida.

## Requirements

### Requirement: Rota única de agregação no escopo do sistema

O sistema SHALL expor, em `apps/api`, uma rota HTTP de leitura
`GET /insights/system` que recebe uma janela de período e responde `200` com um
**único objeto agregado** contendo as métricas do catálogo no escopo do sistema
— sem recorte por agente, provedor, modelo ou base.

A rota SHALL ser **uma**, e não uma por métrica ou por grupo: janela, fuso,
tratamento de nulo e "medindo desde" são um contrato só, e reparti-los entre N
rotas cria N chances de discordarem sobre o mesmo balde.

Toda agregação SHALL ser calculada **no banco de dados**, com custo independente
do número de linhas existentes. A rota NÃO SHALL devolver linha bruta das tabelas
de métrica: empurrar a agregação para o cliente levaria milhares de linhas ao
navegador e colapsaria a distinção entre nulo e zero que esta capability existe
para preservar.

A rota SHALL exigir autenticação, como toda rota de `apps/api` que não esteja
explicitamente classificada como anônima — e, por isso, NÃO SHALL ser
acrescentada à allowlist de rotas anônimas, cuja validação de startup reprovaria
o boot ao encontrar ali um padrão que não está mapeado como anônimo.

#### Scenario: A rota responde o agregado do período

- **WHEN** a rota é chamada com uma janela válida
- **THEN** a resposta é `200` com um objeto único cobrindo as métricas do escopo
  do sistema, e nenhuma linha bruta das tabelas de métrica aparece no corpo

#### Scenario: A rota exige token

- **WHEN** a rota é chamada sem token
- **THEN** a resposta é `401`, sem que nenhuma classificação de rota precise ter
  sido declarada para isso

### Requirement: A janela é explícita, obrigatória e sem intervalo implícito

A rota SHALL receber os dois limites da janela por query string, em parâmetros
`from` e `to`, ambos obrigatórios. O sistema NÃO SHALL assumir um intervalo
implícito, um limite padrão, nem um período relativo ao instante da requisição.

Os dois parâmetros SHALL ser declarados na assinatura do endpoint como texto, e
não como tipo de data: com o tipo de data na assinatura, um valor malformado
falha no **binding**, antes do método rodar, e o framework devolve um `400` com
corpo próprio — forma diferente do erro de validação usado no resto da casa.

O sistema SHALL aceitar instantes em ISO 8601 e SHALL acumular os problemas dos
dois limites em uma **única** resposta de validação, usando o mesmo formato de
corpo para limite ausente e para limite malformado. Os dois limites SHALL ser
**inclusivos**.

Quando `to` for anterior a `from`, o sistema SHALL responder erro de validação —
e SHALL fazê-lo somente depois de os dois limites terem sido interpretados, para
que um limite malformado não mascare a inversão nem o contrário.

A ausência de **teto de intervalo** é decisão consciente, herdada do precedente
das rotas de resumo de `apps/inbox` — mas o gatilho de recalibração NÃO SHALL ser
copiado daquele precedente: lá a rota devolve um escalar, aqui devolve o agregado
do catálogo inteiro.

#### Scenario: Limite ausente e limite malformado têm o mesmo formato de erro

- **WHEN** a rota é chamada com `from` ausente e `to` malformado
- **THEN** a resposta é uma única resposta de validação, com uma entrada por
  limite, no mesmo formato de corpo

#### Scenario: Período invertido é recusado depois de os dois limites parsearem

- **WHEN** `from` e `to` são instantes válidos e `to` é anterior a `from`
- **THEN** a resposta é erro de validação reportado sob a chave do limite final

#### Scenario: Os dois limites são inclusivos

- **WHEN** existe dado cujo instante coincide exatamente com `from` ou com `to`
- **THEN** esse dado está incluído no agregado

### Requirement: O limite recebido é normalizado antes de tocar o banco

O sistema SHALL interpretar um limite **sem deslocamento de fuso** como UTC, e
SHALL normalizar o valor interpretado para deslocamento zero antes de usá-lo em
qualquer consulta.

As duas normalizações têm papéis diferentes e nenhuma delas é estilo. A primeira
decide **qual instante** um valor sem deslocamento significa — sem ela, o valor
recebe o deslocamento local do processo. A segunda evita que um valor com
deslocamento diferente de zero chegue ao driver do banco, que o **recusa** para
coluna de instante — transformando entrada válida do usuário em erro interno.

Este requisito SHALL ter guarda próprio, e o guarda SHALL exercitar um limite com
deslocamento **diferente de zero**. O defeito que ele previne é invisível em
servidor cujo fuso seja UTC, que é o que `apps/api` é hoje e deixa de ser com esta
change: ele passa de inalcançável a alcançável exatamente aqui.

#### Scenario: Limite com deslocamento diferente de zero é aceito

- **WHEN** a rota é chamada com `from` em ISO 8601 carregando um deslocamento
  diferente de zero
- **THEN** a resposta é `200`, e não um erro interno

#### Scenario: Limite sem deslocamento é interpretado como UTC

- **WHEN** a rota é chamada com um limite sem deslocamento de fuso
- **THEN** o instante considerado é o mesmo que o valor equivalente escrito
  explicitamente em UTC, independentemente do fuso do processo

### Requirement: O balde diário é o dia local, nunca o dia UTC

Toda métrica agrupada por dia, por dia da semana ou por calendário SHALL usar o
**dia no fuso configurado do sistema**, e NÃO SHALL usar o dia em UTC.

A conversão SHALL acontecer na consulta, sobre as linhas já restritas pela janela
— o filtro de período é uma comparação de **instantes**, e o agrupamento por dia
local se aplica ao resultado dela. O sistema NÃO SHALL criar índice de expressão
sobre a conversão de fuso: isso congelaria o nome do fuso no schema, que é o
oposto de mantê-lo em configuração.

Este requisito SHALL ter guarda próprio, e o guarda SHALL **reprovar contra um
balde em UTC**. Ele existe porque a operação é noturna o bastante para que mais de
um quarto das tasks medidas caia em outro dia — **e em outro dia da semana** —
quando o balde sai em UTC.

#### Scenario: Instante noturno cai no dia local correto

- **WHEN** existe dado cujo instante, no fuso configurado, é de um dia, e em UTC
  é do dia seguinte
- **THEN** ele é contado no dia local, e o guarda reprova se o agrupamento for
  feito em UTC

#### Scenario: O dia da semana segue o dia local

- **WHEN** um instante cai em dias da semana diferentes conforme o fuso usado
- **THEN** o mapa por dia da semana o atribui ao dia da semana local

### Requirement: Nulo é preservado e o zero é reservado a contagem medida

A rota NÃO SHALL afirmar mais do que o sistema sabe. Valor não coletado SHALL
chegar ao cliente como **ausente ou nulo**, e NÃO SHALL ser normalizado para
zero em nenhum ponto do caminho — nem na consulta, nem no mapeamento, nem na
serialização.

O valor `0` SHALL ser reservado a **contagem medida**: a agregação percorreu o
período e não encontrou nada. A régua é a **proveniência** do valor, nunca o tipo
do campo.

O guarda deste requisito SHALL ser **negativo** — afirmar a ausência do zero onde
a fonte é nula —, porque é a asserção negativa que impede a regressão
bem-intencionada de "deixar a tela sem buraco".

#### Scenario: Token não reportado pelo provedor chega nulo

- **WHEN** existe chamada ao provedor cujo número de tokens não foi reportado
- **THEN** o valor correspondente chega ausente ou nulo, e o guarda afirma que
  ele não é `0`

#### Scenario: Período medido e vazio chega zero

- **WHEN** a janela está inteiramente dentro de um regime de medição e não há
  nenhuma ocorrência nela
- **THEN** a contagem correspondente chega `0`

### Requirement: "Medindo desde" é declarado por regime de medição

A rota SHALL devolver os instantes de início de medição como um **mapa de
regimes**, e cada grupo de métricas SHALL declarar a qual regime pertence. A rota
NÃO SHALL devolver um único "medindo desde" para todas as métricas.

São **dois** regimes hoje — a coleta de execução e a coleta de embedding começaram
em datas diferentes —, e um texto único mentiria sobre um dos dois. O formato é um
mapa, e não dois campos, porque a etapa seguinte da linha acrescentará um terceiro
regime e o contrato precisa absorvê-lo sem mudar de forma.

O instante de início de cada regime SHALL vir de **configuração**, registrada no
dia do deploy da coleta correspondente, e NÃO SHALL ser derivado do menor carimbo
existente nos dados. O menor carimbo é *"quando a primeira linha chegou"*: se o
sistema ficou ocioso depois do deploy, derivá-lo marcaria como **não medido** um
período que foi medido e estava vazio — exatamente a distinção que esta capability
existe para preservar. O menor carimbo permanece válido como **conferência**.

#### Scenario: Os dois regimes chegam separados

- **WHEN** a rota é chamada
- **THEN** a resposta carrega o instante de início de cada regime de medição, e
  cada grupo de métricas declara o seu

#### Scenario: Ociosidade depois do deploy não encurta o regime

- **WHEN** o primeiro dado de um regime é posterior ao início declarado dele
- **THEN** o início devolvido é o declarado, não o do primeiro dado

### Requirement: Período anterior ao regime é distinguível de período sem uso

A série diária SHALL omitir — nunca emitir `0` para — os dias anteriores ao
início do regime a que a métrica pertence. Dentro do regime, um dia sem ocorrência
SHALL emitir `0`.

É esta distinção que sustenta o estado de "período pedido maior que a medição" da
tela aprovada: sem ela, um período que antecede a coleta apareceria como um
período de inatividade real.

A série diária SHALL cobrir **todos** os dias entre o início efetivo da medição
na janela e o fim efetivo dela, sem buracos. A ausência de um dia na série SHALL
significar **uma** coisa: aquele dia não foi medido. Um consumidor SHALL conseguir
decidir o estado de um dia lendo a série, e SHALL NOT precisar cruzá-la com o
mapa de regimes para isso — o regime é configuração do servidor, e reconstruir no
cliente uma condição que o servidor já resolveu cria duas regras que divergem em
silêncio.

O fim efetivo da janela SHALL ser o mais cedo entre o limite pedido e o instante
da consulta. A série SHALL NOT conter dias posteriores ao instante da consulta:
emitir `0` para um dia que ainda não aconteceu afirma medição sobre o futuro, que
é o mesmo defeito que a omissão dos dias anteriores ao regime evita no passado.

**O mesmo tratamento SHALL valer para a agregação por dia da semana**, cuja
ausência hoje não distingue nada:

- um dia da semana que **ocorre** entre os dias medidos e teve ocorrência SHALL
  chegar com a sua contagem;
- um dia da semana que **ocorre** entre os dias medidos e não teve ocorrência
  SHALL chegar com `0`;
- um dia da semana que **não ocorre** entre os dias medidos SHALL ser **omitido**
  — nunca houve medição dele para dar zero.

O sistema SHALL NOT eleger dia da semana de pico quando nenhuma ocorrência foi
medida no período. Com a agregação por dia da semana passando a emitir `0`, uma
faixa medida sem nenhuma ocorrência produz contagens todas iguais a zero, e
eleger a primeira delas afirmaria um pico que não existe.

O contador de tokens de um dia SHALL permanecer anulável e SHALL chegar **nulo**
no dia medido e sem ocorrência. "Foram zero tasks" e "não há token a relatar" são
afirmações diferentes, e o `0` da contagem de tasks SHALL NOT ser propagado para
o contador de tokens.

#### Scenario: Janela que começa antes do regime

- **WHEN** a janela pedida começa antes do início do regime de uma métrica
- **THEN** os dias anteriores ao início não aparecem na série dessa métrica, e o
  cliente consegue distinguir esses dias dos dias medidos e vazios

#### Scenario: Dia medido e sem ocorrência aparece na série com zero

- **WHEN** a janela pedida está inteiramente dentro do regime e contém um dia sem
  nenhuma ocorrência, entre dias que tiveram
- **THEN** esse dia aparece na série com contagem `0`, e a série não tem buraco
  entre os dias que tiveram ocorrência

#### Scenario: O contador de tokens do dia vazio não vira zero

- **WHEN** um dia medido e sem ocorrência aparece na série
- **THEN** a contagem de tasks dele é `0` e o contador de tokens dele é nulo

#### Scenario: Dia posterior ao instante da consulta não entra na série

- **WHEN** a janela pedida termina depois do instante da consulta
- **THEN** a série termina no dia da consulta, e nenhum dia posterior a ele
  aparece com `0`

#### Scenario: Dia da semana medido e sem ocorrência chega com zero

- **WHEN** um dia da semana ocorre entre os dias medidos e não teve nenhuma
  ocorrência
- **THEN** ele aparece na agregação por dia da semana com contagem `0`

#### Scenario: Dia da semana que não ocorre entre os dias medidos é omitido

- **WHEN** a faixa medida é curta demais para conter algum dia da semana
- **THEN** esse dia da semana não aparece na agregação, e nenhum `0` é emitido
  para ele

#### Scenario: Período medido sem nenhuma ocorrência não tem dia de pico

- **WHEN** a janela pedida está inteiramente dentro do regime e não contém
  nenhuma ocorrência
- **THEN** o dia da semana de pico chega nulo, e nenhum dia da semana é eleito a
  partir de contagens todas iguais a zero

### Requirement: Tasks sem estado terminal cobrem as duas populações

A métrica de tasks sem estado terminal SHALL cobrir as **duas** populações que
existem, e o sistema SHALL reportá-las de forma distinguível:

- **execução aberta** — existe linha de execução sem instante de término;
- **task nunca consumida** — não existe linha de execução alguma, porque a linha
  de execução nasce no consumo, e a task só existe no store durável de tasks.

Esta é a única leitura que a rota faz do store durável de tasks, e é o que a torna
necessária: nenhuma tabela de métrica enxerga a segunda população.

O conjunto de estados considerados não-terminais SHALL ser **o mesmo** que a
varredura periódica de `apps/workers` usa. O protocolo admite outros estados
não-terminais que essa varredura não cobre e que nunca foram observados nesta
base; adotar um conjunto diferente faria as duas fontes medirem números
diferentes justamente enquanto elas precisam ser comparadas em paralelo.

A métrica SHALL ser explicitamente uma leitura **do instante da consulta**, e NÃO
SHALL ser apresentada como histórico: o store durável de tasks sobrescreve o
carimbo a cada transição e não guarda série.

#### Scenario: As duas populações aparecem separadas

- **WHEN** existe uma execução aberta e uma task publicada que nunca foi
  consumida
- **THEN** ambas são reportadas, e a resposta permite distinguir uma da outra

#### Scenario: Task em estado terminal nunca é reportada

- **WHEN** existe task em estado terminal, por mais antiga que seja
- **THEN** ela não aparece na métrica

### Requirement: Métrica de fonte parcial declara a sua parcialidade

A rota SHALL declarar a parcialidade de toda métrica do catálogo que não puder
ser derivada integralmente das fontes existentes, e NÃO SHALL apresentá-la como
se fosse completa. Ela SHALL devolver o que sabe e tornar a lacuna legível para o
cliente, ou omitir a métrica — nunca apresentar um número parcial com o rótulo do
todo.

As parcialidades conhecidas nesta etapa, e que este requisito cobre:

- **Recusas feitas por `apps/api`** não produzem linha de execução. A contagem de
  recusas derivada das tabelas de métrica **subconta**, e a parte que falta existe
  apenas no store durável de tasks — onde há agente, mas **não** há provedor,
  modelo nem motivo.
- **O motivo de uma recusa feita por `apps/api` não tem fonte nenhuma**: causas
  distintas colapsam em um único estado, sem coluna de motivo em lugar algum.
- **Duração total e tempo de fila** dependem de um carimbo que é nulo em
  reentrega, e ficam **indefinidos** nesses casos — nulo a preservar, nunca zero.
- **Tempo em ferramentas** é um resíduo que inclui também espera de lock,
  chamadas a servidores externos e busca vetorial. O rótulo SHALL declarar o que
  o número inclui, ou a métrica SHALL mudar de nome.

#### Scenario: Recusa sem linha de execução não é silenciosamente omitida

- **WHEN** existe task recusada antes de qualquer execução
- **THEN** a resposta não a apresenta como se a contagem de recusas fosse
  completa a partir das tabelas de métrica

#### Scenario: Carimbo ausente produz indefinido, não zero

- **WHEN** existe execução cujo carimbo de submissão é nulo
- **THEN** a duração e o tempo de fila dessa execução ficam fora do cálculo como
  ausentes, e não entram como `0`
