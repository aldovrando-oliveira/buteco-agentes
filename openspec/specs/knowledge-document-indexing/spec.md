# knowledge-document-indexing Specification

## Purpose

Cobre o **pipeline que transforma documento em fragmentos consultáveis**: a
fragmentação do conteúdo, a geração do embedding de cada fragmento, o
enfileiramento e o consumo assíncronos, a máquina de estados de indexação
(`Pending` → `Indexing` → `Indexed` | `Failed`), e a integridade entre o modelo
de embedding declarado na configuração e o que está gravado no índice.

O que esta capability garante, e que é o motivo de ela existir separada do
catálogo: **nenhum caminho deixa o documento sem fragmento com aparência de
normal.** Isso se desdobra em quatro propriedades que a spec afirma —
fragmentação que nunca perde conteúdo (três invariantes absolutos), substituição
integral em transação única, falha que preserva o conteúdo que já respondia, e
recusa de gravar sucesso com zero fragmentos.

Cobre também o que o operador vê quando algo dá errado: motivo de falha em texto
legível (nunca exceção crua), contagem de tentativas e instante da última, o
resumo de indexação agregado por base, e a **proveniência gravada no índice** —
com que provedor, modelo e dimensão os fragmentos foram produzidos, servida por
rota global de `apps/api`. A proveniência e a checagem de integridade do boot
leem as **mesmas três colunas**, e é por isso que as duas vivem nesta capability:
a rota é o segundo consumidor da propriedade, não uma propriedade nova.

**Não cobre** o cadastro de documentos (é `knowledge-document-catalog`), nem a
consulta ao índice pelo agente — a tool de busca, a resolução por agente e o
formato do resultado são de outra capability, de outra etapa. A separação é a
mesma que existe entre `mcp-server-catalog` e `mcp-tool-execution`: catálogo é
cadastro, isto é execução, e quem consome o índice é outra coisa.

Os parâmetros de fragmentação (alvo de merge-up e teto de tamanho)
**deliberadamente não estão aqui**: a rodada de medição que precedeu esta
capability provou que o fragmentador não perde conteúdo, e não provou que
aqueles números sejam o ótimo. São constantes de produto em `apps/workers`, com
gatilho de remedição registrado.

## Requirements

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

### Requirement: Reindexação sob pedido do operador
O sistema SHALL oferecer, via `apps/api`, uma operação que reenfileira a
indexação de um documento **sem que o conteúdo dele tenha mudado**:
`POST /knowledge-bases/{knowledgeBaseId}/documents/{id}/reindex`.

A operação SHALL localizar o documento por `knowledgeBaseId` **e** `id` — nunca
só por `id` —, e SHALL responder HTTP 404 quando a base não existe, quando o
documento não existe, ou quando o documento pertence a outra base.

Em caso de sucesso a operação SHALL, numa única gravação:

- levar `indexingStatus` para `Pending`;
- limpar `failureReason`;
- zerar `indexingAttempts` e `lastAttemptAt`;
- **preservar `indexedAt` e `fragmentCount`**;
- **não alterar `contentHash` nem `contentRevision`**.

Depois de gravar, a operação SHALL publicar **exatamente uma** mensagem na fila
de indexação, para o documento, com contagem de execução inicial — a mesma
mensagem e o mesmo caminho de publicação que a criação e a atualização de
documento usam. A publicação SHALL ocorrer depois da gravação, nunca antes.

A operação SHALL responder HTTP 200 com o documento já no estado novo.

Esta operação é a **única exceção** à regra "Conteúdo idêntico não é
reindexado". A exceção é o propósito da operação, não um conflito com aquela
regra: a regra governa o que uma *atualização* faz, e existe para não gastar
chamada ao provedor de embedding em edição de metadado; a reindexação é pedido
explícito do operador e existe precisamente para o caso em que o conteúdo está
correto e a indexação falhou.

Pelo mesmo motivo, `contentHash` SHALL permanecer intacto: anulá-lo faria a
próxima atualização apenas de metadado reindexar sem motivo, quebrando aquela
regra pela porta dos fundos.

Esta operação também **refina** o significado de `indexingAttempts` fixado como
"tentativas sobre a revisão corrente". A contagem é de tentativas dentro de uma
**rodada de indexação**, e uma rodada abre quando chega conteúdo novo **ou
quando o operador pede uma reindexação**. O zeramento aqui não é a contagem
mentindo sobre a revisão: é uma rodada nova sobre a mesma revisão.

#### Scenario: Reindexar documento que falhou volta para Pending e enfileira
- **WHEN** um documento em `Failed`, com motivo de falha gravado e tentativas
  acumuladas, recebe `POST .../reindex`
- **THEN** a API responde HTTP 200, `indexingStatus` é `Pending`,
  `failureReason` é nulo, `indexingAttempts` é zero, `lastAttemptAt` é nulo, e
  exatamente uma mensagem de indexação é publicada para esse documento

#### Scenario: Reindexar preserva o conteúdo que já respondia
- **WHEN** um documento que tem `indexedAt` preenchido e `fragmentCount` maior
  que zero — de uma indexação anterior bem-sucedida — recebe `POST .../reindex`
- **THEN** `indexedAt` e `fragmentCount` permanecem exatamente como estavam,
  ainda que `indexingStatus` volte para `Pending`

#### Scenario: Reindexar não altera a revisão nem o hash do conteúdo
- **WHEN** um documento recebe `POST .../reindex`
- **THEN** `contentRevision` permanece o mesmo valor de antes, e a mensagem
  publicada carrega essa mesma revisão

#### Scenario: Reindexar não faz a atualização seguinte reindexar de novo
- **WHEN** um documento é reindexado e, em seguida, atualizado com o **mesmo**
  conteúdo e um título diferente
- **THEN** essa atualização NÃO publica mensagem de indexação nenhuma, porque o
  `contentHash` continua correspondendo ao conteúdo gravado

#### Scenario: Reindexar documento nunca indexado
- **WHEN** um documento que nunca saiu de `Pending`, com `indexedAt` nulo,
  recebe `POST .../reindex`
- **THEN** a API responde HTTP 200, `indexingStatus` continua `Pending`,
  `indexedAt` continua nulo, e uma mensagem de indexação é publicada

#### Scenario: Reindexar documento de base inativa
- **WHEN** um documento de uma base **desativada** recebe `POST .../reindex`
- **THEN** a API responde HTTP 200 e publica a mensagem normalmente — desativar
  a base impede o uso pelo agente, não a manutenção do conteúdo

#### Scenario: Reindexar documento de outra base é recusado
- **WHEN** um cliente envia `POST .../reindex` com o id de um documento que
  existe, mas pertence a outra base que não a do caminho
- **THEN** a API responde HTTP 404 e nenhuma mensagem é publicada

#### Scenario: Reindexar documento inexistente
- **WHEN** um cliente envia `POST .../reindex` para um id de documento que não
  existe
- **THEN** a API responde HTTP 404 e nenhuma mensagem é publicada

### Requirement: Resumo de indexação por base
O sistema SHALL oferecer, via `apps/api`, um recurso que devolve o estado de
indexação **agregado por base de conhecimento**, para o conjunto inteiro de
bases numa única requisição: `GET /knowledge-bases/indexing-summary`.

Cada item SHALL trazer o identificador da base e três contagens:
`documentCount` (documentos da base), `indexedCount` (documentos em `Indexed`) e
`failedCount` (documentos em `Failed`).

A resposta SHALL conter **um item para cada base do catálogo**, inclusive:

- a base que **não tem documento nenhum**, com as três contagens em zero;
- a base **inativa**, com as contagens reais.

O zero de `documentCount` é uma contagem **medida** — a agregação percorreu os
documentos daquela base e não encontrou nenhum — e por isso, ao contrário de
`fragmentCount` em documento nunca indexado, ele PODE ser exibido. Omitir a base
da resposta obrigaria o consumidor a interpretar ausência, que é o que produz
afirmação sem base.

`Pending` e `Indexing` NÃO ganham contagem própria: o consumidor que precisa
distingui-los é o detalhe da base, que já recebe o estado **por documento** na
listagem de documentos. O total não terminal permanece exato por subtração.

O custo da resposta SHALL ser independente do número de bases — nunca uma
consulta por base.

A resposta SHALL ser uma lista com ordenação determinística, pelo mesmo critério
do catálogo de bases, com desempate estável.

#### Scenario: Resumo agrega os estados de cada base
- **WHEN** um cliente envia `GET /knowledge-bases/indexing-summary` e existe uma
  base com três documentos indexados, um em falha e um pendente
- **THEN** a API responde HTTP 200 e o item dessa base traz `documentCount: 5`,
  `indexedCount: 3` e `failedCount: 1`

#### Scenario: Base sem documento nenhum aparece com zeros
- **WHEN** existe uma base sem nenhum documento cadastrado
- **THEN** o resumo **inclui** um item para ela, com `documentCount`,
  `indexedCount` e `failedCount` em zero — a base nunca é omitida

#### Scenario: Base inativa aparece com as contagens reais
- **WHEN** existe uma base **desativada** com documentos
- **THEN** o resumo inclui um item para ela com as contagens reais dos seus
  documentos, sem filtrar por estado de ativação

#### Scenario: Resumo sem nenhuma base cadastrada
- **WHEN** um cliente envia `GET /knowledge-bases/indexing-summary` e não existe
  nenhuma base
- **THEN** a API responde HTTP 200 com uma lista vazia, nunca HTTP 404

#### Scenario: O custo não cresce com o número de bases
- **WHEN** o resumo é pedido com várias bases cadastradas, uma delas sem
  documento nenhum
- **THEN** o número de consultas que a requisição emite ao banco é o mesmo
  qualquer que seja o número de bases — nunca uma consulta por base

#### Scenario: Resumo tem ordenação determinística com desempate
- **WHEN** duas bases compartilham o mesmo instante de criação
- **THEN** a ordem entre elas na resposta é estável, decidida por desempate
  explícito da consulta e não pelo plano do banco

#### Scenario: Documento excluído sai do resumo
- **WHEN** um documento de uma base é excluído
- **THEN** o `documentCount` dessa base no resumo diminui de acordo

### Requirement: Proveniência gravada do índice é servida por rota
O sistema SHALL oferecer, via `apps/api`, um recurso que devolve a
**proveniência gravada no índice de conhecimento**:
`GET /knowledge-index/diagnostics`.

A resposta SHALL ser a lista das **combinações distintas** de provedor, modelo e
dimensão de embedding presentes nos fragmentos, cada item com quatro campos:
`provider`, `model`, `dimensions` e `fragmentCount` — o número de fragmentos
gravados com aquela combinação.

A rota SHALL ser **global**: NÃO SHALL aceitar identificador de base de
conhecimento, e SHALL agregar o índice inteiro. Provedor, modelo e dimensão são
propriedade do **sistema**, não da base — a dimensão é fixada pelo tipo da coluna
(`vector(4096)`), que recusa qualquer vetor de outra dimensão, e a checagem de
integridade do boot de `apps/workers` exige combinação única no índice **inteiro**.
Uma rota por base afirmaria que bases diferentes podem ter proveniências
diferentes, o que o schema torna impossível.

A resposta SHALL refletir o que está **gravado**, nunca o que a configuração
declara. NÃO SHALL existir, na resposta, nenhum valor vindo de configuração de
embedding: `apps/api` não tem essa configuração, e introduzi-la criaria a segunda
fonte do mesmo valor.

**Mais de um item SHALL ser uma resposta válida desta rota**, não um erro. É o
estado em que `apps/workers` se recusa a subir — índice com vetores de modelos
incomparáveis — enquanto `apps/api` continua de pé; é exatamente nesse estado que
o operador abre a tela, e a contagem por combinação é o que torna a reindexação
decidível.

Índice vazio SHALL responder HTTP 200 com **lista vazia**, nunca HTTP 404 e nunca
um item com campos nulos ou zerados.

A lista SHALL ter ordenação determinística por `provider`, `model` e
`dimensions`, produzida pela **consulta** e não por comparação em memória. Os três
campos juntos são o identificador da combinação e são únicos por construção — são
a própria chave de agrupamento —, então o desempate por identificador que toda
lista ordenada de `apps/api` exige está satisfeito pelo critério primário, sem
quarto critério.

O custo da resposta SHALL ser de **uma única consulta**, independente do número
de bases e do número de documentos — nunca uma consulta por base.

A rota SHALL exigir operador autenticado, e SHALL recusar o token de serviço de
`apps/inbox`.

#### Scenario: Índice vazio devolve lista vazia, sem afirmar proveniência
- **WHEN** um cliente autenticado envia `GET /knowledge-index/diagnostics` e não
  existe nenhum fragmento gravado
- **THEN** a API responde HTTP 200 com uma lista vazia, nunca HTTP 404, e a
  resposta não contém nenhum nome de provedor, nome de modelo nem dimensão

#### Scenario: Uma combinação devolve um item com a contagem exata
- **WHEN** existem fragmentos gravados, todos com o mesmo provedor, modelo e
  dimensão
- **THEN** a resposta traz **um** item, com aquele provedor, aquele modelo,
  aquela dimensão, e `fragmentCount` igual ao número de fragmentos gravados

#### Scenario: Duas combinações devolvem dois itens, cada um com a sua contagem
- **WHEN** o índice contém fragmentos de **dois** modelos distintos — o estado que
  reprova o boot de `apps/workers`
- **THEN** a resposta é HTTP 200 com **dois** itens, cada um com o seu
  `fragmentCount`, e a rota não trata isso como erro

#### Scenario: Bases diferentes com a mesma combinação devolvem um item só
- **WHEN** duas bases de conhecimento distintas têm fragmentos gravados com o
  mesmo provedor, modelo e dimensão
- **THEN** a resposta traz **um** item, com `fragmentCount` igual à soma dos
  fragmentos das duas bases — a agregação é do índice, não da base

#### Scenario: A rota não aceita identificador de base
- **WHEN** um cliente tenta alcançar a proveniência por uma rota sob o recurso de
  bases, como `/knowledge-bases/{id}/index-diagnostics`
- **THEN** não existe tal rota, e a proveniência é servida apenas pelo recurso
  global

#### Scenario: A ordem dos itens é determinística
- **WHEN** o índice contém mais de uma combinação e a rota é chamada duas vezes
- **THEN** os itens saem na mesma ordem nas duas respostas, crescente por
  `provider`, depois `model`, depois `dimensions`

#### Scenario: O custo não cresce com o número de bases nem de documentos
- **WHEN** a proveniência é pedida com fragmentos espalhados por várias bases e
  vários documentos
- **THEN** `apps/api` emite **uma** consulta ao banco, nunca uma por base nem uma
  por documento

#### Scenario: Requisição sem token é recusada
- **WHEN** um cliente envia `GET /knowledge-index/diagnostics` sem token de
  operador
- **THEN** a API responde HTTP 401, pela política padrão que cobre toda rota não
  marcada como anônima

#### Scenario: Token de serviço é recusado
- **WHEN** a requisição chega com o token de serviço de `apps/inbox`, válido
  estruturalmente
- **THEN** a API responde HTTP 403, porque esta rota não está entre as que o
  serviço consome

#### Scenario: Os nomes dos campos no fio são os declarados
- **WHEN** a resposta de um índice com uma combinação é lida como **texto**
- **THEN** o JSON contém as chaves `provider`, `model`, `dimensions` e
  `fragmentCount`, com `dimensions` e `fragmentCount` como números
