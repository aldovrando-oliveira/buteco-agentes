## ADDED Requirements

### Requirement: Fragmentação nunca perde conteúdo
A fragmentação de um documento SHALL satisfazer três invariantes absolutos, sem
faixa de tolerância. Estes invariantes são propriedades verificáveis do
resultado, e são o que a spec afirma — os parâmetros de tamanho que os produzem
NÃO SHALL ser fixados aqui.

**I1** — Todo documento com conteúdo não-vazio SHALL produzir pelo menos um
fragmento. Nenhuma forma de documento SHALL resultar em zero fragmentos:
markdown sem cabeçalho nenhum, com apenas título de nível 1 seguido de
parágrafos, ou com cabeçalhos que pulem o nível 2, SHALL todos ser fragmentados.

**I2** — Nenhum fragmento SHALL exceder o teto de tamanho, medido sobre o
**texto emitido**, incluindo o prefixo de caminho de cabeçalhos que o fragmento
carrega. Bloco que não possa ser dividido por parágrafo — tabela markdown, bloco
de código — SHALL ser dividido por linha, por sentença, e em último caso por
corte de caractere, para que o teto seja garantia e não intenção.

**I3** — Todo parágrafo do documento de origem SHALL aparecer em algum
fragmento. Perder linha em branco e marcação de estrutura é aceitável; perder
parágrafo não. O texto anterior ao primeiro cabeçalho de seção (preâmbulo)
SHALL ser capturado como qualquer outro conteúdo.

#### Scenario: Documento de texto puro sem cabeçalho nenhum
- **WHEN** um documento cujo conteúdo é texto corrido, sem nenhuma linha de
  cabeçalho markdown, é indexado
- **THEN** ele produz pelo menos um fragmento, e todo parágrafo do texto
  original aparece em algum fragmento

#### Scenario: Documento com título e parágrafos, sem cabeçalho de seção
- **WHEN** um documento com uma linha `#` seguida apenas de parágrafos é
  indexado
- **THEN** ele produz pelo menos um fragmento, e nenhum parágrafo é descartado

#### Scenario: Documento cujos cabeçalhos pulam o nível 2
- **WHEN** um documento com `#` seguido de seções `###`, sem nenhum `##`, é
  indexado
- **THEN** cada seção `###` é reconhecida como fronteira e nenhum parágrafo é
  descartado

#### Scenario: Preâmbulo entre o título e a primeira seção é indexado
- **WHEN** um documento tem parágrafos entre a linha `#` e o primeiro cabeçalho
  de seção, e uma consulta é respondida apenas por esse trecho
- **THEN** o trecho está contido em algum fragmento

#### Scenario: Tabela markdown longa respeita o teto
- **WHEN** um documento contém uma tabela markdown cuja seção excede o teto de
  tamanho
- **THEN** nenhum fragmento produzido excede o teto, medido sobre o texto
  emitido com o prefixo de cabeçalhos incluído

#### Scenario: Bloco de código longo respeita o teto
- **WHEN** um documento contém um bloco de código cuja seção excede o teto de
  tamanho
- **THEN** nenhum fragmento produzido excede o teto


### Requirement: Cada pedaço de tabela carrega a linha de cabeçalho
Todo fragmento que contenha linhas de uma tabela markdown SHALL conter também a
linha de cabeçalho e a linha separadora dessa tabela, mesmo quando a tabela for
dividida em mais de um fragmento.

Isto é mitigação medida, não estética: um bloco de linhas de tabela sem prosa e
sem os nomes das colunas produz vetor próximo do centroide do vocabulário do
domínio e passa a vencer a busca em consultas com que não tem relação nenhuma.

#### Scenario: Continuação de tabela repete o cabeçalho
- **WHEN** uma tabela markdown com mais linhas do que cabem num fragmento é
  fragmentada
- **THEN** todo fragmento que contenha linhas dessa tabela contém também a
  linha de cabeçalho e a linha separadora dela

### Requirement: Sucesso com zero fragmentos é recusado
O consumidor NÃO SHALL gravar resultado de indexação com zero fragmentos para um
documento com conteúdo. Nesse caso o documento SHALL terminar em `Failed`, com
motivo legível, e NÃO SHALL terminar em `Indexed` com contagem zerada.

Esta guarda é **defesa em profundidade contra defeito de fragmentação**, e não
contra conteúdo vazio: conteúdo vazio ou só de espaços é rejeitado com HTTP 400
na criação e na atualização, e nenhum caminho de API produz documento assim. O
gatilho alcançável é uma regressão no chunker — que é exatamente o caso em que a
contagem zerada com aparência de sucesso, o pior caso da convenção 13, apareceria
sem nada reprovar.

#### Scenario: Fragmentação que devolve conjunto vazio não vira sucesso
- **WHEN** a fragmentação de um documento com conteúdo devolve zero fragmentos
- **THEN** nenhum fragmento é gravado, `indexingStatus` é `Failed` com motivo
  legível, e `indexedAt` não é preenchido por esta execução

### Requirement: Indexação assíncrona por fila própria
Documento criado ou atualizado com conteúdo diferente SHALL ser publicado numa
fila de indexação **própria**, distinta da fila de execução de tarefas de
agente. O consumidor dessa fila SHALL ser o único escritor de fragmentos.

A publicação SHALL ocorrer na mesma transação que persiste o documento, ou
depois dela — nunca antes, para que não exista mensagem apontando para
documento que não foi gravado.

#### Scenario: Documento criado é enfileirado e indexado
- **WHEN** um documento é criado numa base
- **THEN** ele transita de `Pending` para `Indexed`, `indexedAt` é preenchido, e
  a contagem de fragmentos passa a ser maior que zero

#### Scenario: Indexação não bloqueia execução de agente
- **WHEN** um documento grande está sendo indexado
- **THEN** tarefas de agente continuam sendo consumidas normalmente, porque as
  duas filas são distintas

### Requirement: Substituição integral dos fragmentos em transação única
A gravação do resultado da indexação SHALL apagar todos os fragmentos do
documento e inserir os novos numa **única transação** — tudo ou nada. NÃO SHALL
existir diferença incremental de fragmento.

Entre a atualização do documento e a gravação dos fragmentos novos, os
fragmentos anteriores SHALL continuar existindo e sendo consultáveis.

#### Scenario: Reindexação substitui o conjunto inteiro
- **WHEN** um documento já indexado tem o conteúdo alterado e é reindexado
- **THEN** nenhum fragmento do conteúdo anterior permanece, e todos os
  fragmentos presentes correspondem ao conteúdo novo

#### Scenario: Fragmentos antigos sobrevivem até o sucesso
- **WHEN** um documento já indexado é atualizado e a indexação nova ainda não
  terminou
- **THEN** os fragmentos do conteúdo anterior continuam gravados e
  consultáveis

#### Scenario: Falha na gravação não deixa conjunto parcial
- **WHEN** a gravação dos fragmentos novos falha no meio
- **THEN** o documento permanece com o conjunto anterior completo, nunca com
  parte do antigo e parte do novo

### Requirement: Falha preserva o conteúdo que já respondia
Falha de indexação SHALL registrar `IndexingStatus = Failed` e `failureReason`,
e SHALL **preservar** `indexedAt` e os fragmentos anteriores. O documento
continua respondendo com o conteúdo anterior.

NÃO SHALL ocorrer de uma falha deixar o documento sem fragmento nenhum com
aparência de estado normal.

#### Scenario: Falha na reindexação preserva o conteúdo anterior
- **WHEN** um documento já indexado é atualizado e a indexação nova falha
- **THEN** `indexingStatus` é `Failed`, `failureReason` está preenchido,
  `indexedAt` mantém o valor anterior, e os fragmentos anteriores continuam
  gravados

#### Scenario: Falha na primeira indexação não inventa data
- **WHEN** um documento nunca indexado falha ao indexar
- **THEN** `indexingStatus` é `Failed`, `failureReason` está preenchido e
  `indexedAt` continua nulo

### Requirement: Motivo de falha é legível por operador
`failureReason` SHALL conter texto destinado a um operador humano, descrevendo o
que falhou e o que fazer, e NÃO SHALL conter representação crua de exceção —
nem nome de tipo, nem pilha de chamadas, nem mensagem de biblioteca repassada
sem tradução.

#### Scenario: Falha do provedor vira motivo legível
- **WHEN** o provedor de embedding recusa a requisição por limite de taxa e as
  tentativas se esgotam
- **THEN** `failureReason` descreve o limite de taxa em texto de operador, e não
  contém nome de tipo de exceção nem pilha de chamadas

### Requirement: Tentativas de indexação são contadas e datadas
Cada documento SHALL ter `indexingAttempts` (inteiro, iniciando em zero) e
`lastAttemptAt` (instante, nulo enquanto nunca houve tentativa). Ambos SHALL ser
persistidos, não derivados de log.

`indexingAttempts` SHALL ser zerado quando o conteúdo do documento mudar — a
contagem é de tentativas sobre a revisão corrente, não sobre a vida do
documento.

A indexação SHALL ser tentada novamente, com espaçamento crescente, até um
limite; esgotado o limite, o documento termina em `Failed`.

#### Scenario: Tentativas são contadas até o limite
- **WHEN** a indexação de um documento falha repetidamente por causa
  transitória até esgotar o limite de tentativas
- **THEN** `indexingAttempts` reflete o número de tentativas feitas,
  `lastAttemptAt` é o instante da última, e `indexingStatus` é `Failed`

#### Scenario: Sucesso após falha transitória
- **WHEN** a primeira tentativa falha por causa transitória e a seguinte tem
  sucesso
- **THEN** `indexingStatus` é `Indexed`, `failureReason` é nulo, e
  `indexingAttempts` reflete as tentativas feitas

#### Scenario: Conteúdo novo zera a contagem de tentativas
- **WHEN** um documento que acumulou tentativas falhas tem o conteúdo alterado
- **THEN** `indexingAttempts` volta a zero

### Requirement: Conteúdo idêntico não é reindexado
Cada documento SHALL ter `contentHash`, derivado do conteúdo extraído.
Atualização cujo conteúdo extraído seja idêntico ao já gravado NÃO SHALL voltar
o documento para `Pending`, NÃO SHALL enfileirar indexação e NÃO SHALL consumir
chamada ao provedor de embedding.

Atualização que altere apenas metadado do documento, sem alterar o conteúdo,
está coberta por esta regra.

`contentHash` nulo SHALL significar "nunca indexado sob esta regra", e documento
com `contentHash` nulo SHALL ser enfileirado mesmo quando o conteúdo enviado for
idêntico ao gravado. É o **oposto** da regra acima, e de propósito: documentos
criados antes desta capability existir têm conteúdo gravado e nenhum fragmento,
e tratá-los como "já indexado" os deixaria parados para sempre.

#### Scenario: Atualização que só troca o título não reindexa
- **WHEN** um documento indexado é atualizado com o mesmo conteúdo e título
  diferente
- **THEN** `indexingStatus` permanece `Indexed`, `indexedAt` não muda, e nenhuma
  indexação nova é enfileirada

#### Scenario: Atualização com conteúdo diferente reindexa
- **WHEN** um documento indexado é atualizado com conteúdo diferente
- **THEN** `indexingStatus` volta para `Pending` e a indexação é enfileirada

#### Scenario: Documento sem hash é enfileirado ainda com conteúdo idêntico
- **WHEN** um documento cujo `contentHash` é nulo — criado antes desta
  capability existir — é atualizado com conteúdo **idêntico** ao gravado
- **THEN** `indexingStatus` vai para `Pending` e a indexação é enfileirada,
  ainda que o conteúdo não tenha mudado

### Requirement: Trabalho obsoleto é descartado, não gravado
O consumidor SHALL ler `contentRevision` do documento ao iniciar o trabalho e
SHALL gravar o resultado **condicionado** a essa revisão ainda ser a corrente.
Se a revisão tiver mudado, o resultado inteiro SHALL ser descartado sem gravar
fragmento nenhum e sem alterar o estado do documento — o trabalho novo já está
enfileirado.

Se o documento tiver deixado de existir durante o trabalho, o resultado SHALL
ser descartado da mesma forma, sem erro visível ao operador.

#### Scenario: Documento atualizado durante a indexação descarta o resultado
- **WHEN** o conteúdo de um documento é alterado enquanto uma indexação da
  revisão anterior está em andamento
- **THEN** nenhum fragmento da revisão anterior é gravado, e o documento termina
  refletindo apenas a revisão nova

#### Scenario: Documento excluído durante a indexação não ressuscita fragmento
- **WHEN** um documento é excluído enquanto uma indexação dele está em andamento
- **THEN** nenhum fragmento é gravado para o documento excluído

### Requirement: Exclusão de documento leva os fragmentos junto
Excluir um documento SHALL excluir todos os fragmentos dele. NÃO SHALL restar
fragmento órfão, e a exclusão do documento NÃO SHALL ser impedida pela
existência de fragmentos.

#### Scenario: Excluir documento indexado remove os fragmentos
- **WHEN** um documento com fragmentos gravados é excluído
- **THEN** a exclusão tem sucesso e nenhum fragmento daquele documento
  permanece

### Requirement: Contagem de fragmentos acompanha o índice
Cada documento SHALL expor `fragmentCount`. O valor SHALL ser exibido pelos
consumidores sempre que `indexedAt` não for nulo, qualquer que seja o estado, e
SHALL ser omitido quando `indexedAt` for nulo — nunca exibido zerado, o que
afirmaria que a indexação rodou e não encontrou nada.

#### Scenario: Documento indexado expõe a contagem
- **WHEN** um documento é indexado com sucesso
- **THEN** `fragmentCount` é igual ao número de fragmentos gravados para ele

#### Scenario: Documento nunca indexado não afirma contagem
- **WHEN** um documento recém-criado é consultado
- **THEN** `indexedAt` é nulo, e o consumidor omite a contagem em vez de exibir
  zero

### Requirement: Integridade entre o modelo declarado e o índice gravado
`apps/workers` SHALL verificar, no boot, que o provedor, o modelo e a dimensão
de embedding declarados na configuração coincidem com os gravados nos
fragmentos existentes.

A verificação SHALL falhar o boot quando o índice contiver qualquer combinação
diferente da declarada, **inclusive mais de uma combinação distinta**, que é
corrupção por troca anterior não detectada. Índice vazio SHALL subir. Uma única
combinação igual à declarada SHALL subir.

Cada fragmento SHALL gravar o provedor, o modelo e a dimensão que o produziram
— sem isso a verificação não tem contra o que comparar.

NÃO SHALL existir modo de tolerância nem bypass por configuração.

#### Scenario: Índice vazio sobe
- **WHEN** `apps/workers` inicia com a tabela de fragmentos vazia
- **THEN** o processo sobe normalmente

#### Scenario: Índice coerente com o declarado sobe
- **WHEN** `apps/workers` inicia com fragmentos gravados pelo mesmo provedor,
  modelo e dimensão que a configuração declara
- **THEN** o processo sobe normalmente

#### Scenario: Modelo divergente reprova o boot
- **WHEN** `apps/workers` inicia com fragmentos gravados por um modelo diferente
  do declarado
- **THEN** o processo não sobe, e a mensagem nomeia o modelo declarado e o
  encontrado

#### Scenario: Dimensão divergente reprova o boot
- **WHEN** `apps/workers` inicia com fragmentos cuja dimensão difere da
  declarada
- **THEN** o processo não sobe

#### Scenario: Mais de um modelo no índice reprova o boot
- **WHEN** a tabela de fragmentos contém fragmentos de dois modelos distintos
- **THEN** o processo não sobe, ainda que um deles seja o declarado

### Requirement: Vetor gravado é o vetor cheio do modelo
O embedding SHALL ser persistido na dimensão nativa que o provedor devolve, sem
truncagem no cliente. A dimensão SHALL ser verificada contra a declarada no
momento da gravação — o provedor pode aceitar um pedido de dimensão e ignorá-lo.

#### Scenario: Dimensão devolvida diferente da declarada falha a indexação
- **WHEN** o provedor devolve embedding com dimensão diferente da declarada na
  configuração
- **THEN** a indexação daquele documento falha com motivo legível, e nenhum
  fragmento é gravado com dimensão divergente
