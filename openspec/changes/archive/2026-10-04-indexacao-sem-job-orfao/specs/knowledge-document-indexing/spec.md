## MODIFIED Requirements

### Requirement: Indexação assíncrona por fila própria
Documento criado ou atualizado com conteúdo diferente SHALL ser publicado numa
fila de indexação **própria**, distinta da fila de execução de tarefas de
agente. O consumidor dessa fila SHALL ser o único escritor de fragmentos.

Toda escrita do `apps/api` que pede indexação SHALL gravar um **pedido de
indexação** (documento e revisão) **na mesma transação** que persiste o
documento: os dois são gravados juntos ou nenhum dos dois. A mensagem SHALL ser
publicada a partir desse pedido, depois da transação, nunca antes — para que não
exista mensagem apontando para documento que não foi gravado. O pedido SHALL ser
removido só depois de o broker confirmar a mensagem, e SHALL continuar sendo
despachado, sem ação do operador, enquanto a publicação falhar.

Nenhuma escrita do `apps/api` SHALL publicar na fila de indexação por outro
caminho que não o despacho do pedido.

#### Scenario: Documento criado é enfileirado e indexado
- **WHEN** um documento é criado numa base
- **THEN** ele transita de `Pending` para `Indexed`, `indexedAt` é preenchido, e
  a contagem de fragmentos passa a ser maior que zero

#### Scenario: Indexação não bloqueia execução de agente
- **WHEN** um documento grande está sendo indexado
- **THEN** tarefas de agente continuam sendo consumidas normalmente, porque as
  duas filas são distintas

#### Scenario: Pedido gravado junto com o documento sobrevive à fila indisponível
- **WHEN** um documento é gravado com pedido de indexação enquanto a publicação
  na fila falha
- **THEN** o pedido existe no banco ao lado do documento, nenhuma mensagem foi
  publicada, e, quando a publicação volta a funcionar, exatamente uma mensagem
  com o id e a revisão do documento é publicada sem ação do operador, e o pedido
  é removido

#### Scenario: Pedido não é removido sem confirmação do broker
- **WHEN** o broker recusa ou não confirma a mensagem de um pedido
- **THEN** o pedido continua no banco e é despachado de novo no ciclo seguinte

#### Scenario: Nenhuma escrita publica fora do despacho
- **WHEN** se inspecionam os tipos do `apps/api` que dependem do publisher de
  indexação
- **THEN** o único é o componente de despacho do pedido, e nenhum handler de
  escrita depende do publisher

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

Na mesma gravação, a operação SHALL registrar **exatamente um** pedido de
indexação para o documento, com a revisão corrente, e esse pedido SHALL resultar
em **exatamente uma** mensagem na fila de indexação, com contagem de execução
inicial — o mesmo pedido, a mesma mensagem e o mesmo caminho de despacho que a
criação e a atualização de documento usam. A mensagem SHALL sair depois da
gravação, nunca antes, e SHALL sair mesmo que a fila esteja indisponível no
momento da operação, assim que ela voltar.

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


#### Scenario: Reindexar com a fila indisponível responde 200 e indexa depois
- **WHEN** um documento recebe `POST .../reindex` enquanto a publicação na fila
  de indexação falha
- **THEN** a API responde HTTP 200 com `indexingStatus: "Pending"`, e, quando a
  publicação volta a funcionar, exatamente uma mensagem de indexação com a
  revisão corrente é publicada para o documento, sem nova ação do operador

## ADDED Requirements

### Requirement: Despacho de pedido de indexação com várias instâncias
O despacho dos pedidos de indexação SHALL ser seguro com várias instâncias do
`apps/api`: cada pedido SHALL ser tomado por um único despacho de cada vez, e um
pedido NÃO SHALL ser publicado por dois despachos concorrentes.

O despacho SHALL ocorrer no fim da escrita que gravou o pedido, limitado a 5
segundos, e por varredura periódica de 30 segundos. Falha do despacho na escrita
NÃO SHALL alterar a resposta da escrita.

Depois de um despacho que falha — na escrita, por erro ou pelo limite de 5
segundos, ou numa publicação da varredura —, a instância SHALL pular o despacho
na escrita por 30 segundos, e as escritas nesse intervalo SHALL responder sem
esperar o limite. O pedido SHALL continuar sendo gravado na mesma transação do
documento em todos os casos, e SHALL ser publicado pela varredura. Um despacho
bem-sucedido, na escrita ou na varredura, SHALL encerrar o intervalo. Falha de um ciclo da varredura,
inclusive na consulta ao banco, NÃO SHALL interromper os ciclos seguintes.

A entrega SHALL ser pelo menos uma vez: um pedido publicado e não removido
(processo interrompido entre a confirmação e o commit) SHALL ser publicado de
novo, e a duplicata da mesma revisão NÃO SHALL produzir dado errado no índice.

#### Scenario: Dois despachos concorrentes publicam cada pedido uma vez
- **WHEN** dois despachos rodam ao mesmo tempo sobre os mesmos pedidos
- **THEN** cada pedido é publicado exatamente uma vez, e a consulta que toma os
  pedidos é emitida com `FOR UPDATE SKIP LOCKED`

#### Scenario: Depois de um despacho que falhou, as escritas seguintes não esperam o limite
- **WHEN** com a publicação sem responder, uma sequência de escritas grava
  pedidos de indexação, e o despacho da primeira falha pelo limite de 5 segundos
- **THEN** só a primeira escrita espera o limite; as seguintes respondem sem
  esperá-lo, cada uma com o documento e o pedido gravados; e, quando a
  publicação volta, a varredura publica exatamente uma mensagem por pedido

#### Scenario: Despacho bem-sucedido encerra o intervalo
- **WHEN** a varredura publica com sucesso enquanto o intervalo aberto por uma
  falha ainda não venceu
- **THEN** a escrita seguinte volta a despachar o próprio pedido antes de
  responder

#### Scenario: Ciclo da varredura que falha não para a varredura
- **WHEN** a consulta de um ciclo da varredura falha
- **THEN** o erro é logado e um ciclo seguinte publica os pedidos pendentes

#### Scenario: Escrita não espera o broker inacessível além do limite
- **WHEN** uma escrita grava um pedido e a publicação não responde
- **THEN** a resposta da escrita sai depois de no máximo 5 segundos de despacho,
  com o documento e o pedido gravados

#### Scenario: Pedidos acima do lote são todos despachados
- **WHEN** há mais pedidos pendentes que o tamanho do lote e a publicação volta a
  funcionar
- **THEN** todos são publicados, na ordem em que foram gravados

#### Scenario: Mensagem repetida da mesma revisão não duplica fragmentos
- **WHEN** o consumidor recebe duas mensagens com o mesmo documento e a mesma
  revisão
- **THEN** o documento termina em `Indexed` com o mesmo conjunto de fragmentos de
  uma entrega só, sem fragmento repetido

#### Scenario: Pedidos de revisões sucessivas indexam só a última
- **WHEN** com a publicação falhando, um documento é atualizado duas vezes com
  conteúdos diferentes, e a publicação volta
- **THEN** são publicadas as mensagens das duas revisões, em ordem, e só a
  revisão corrente é gravada no índice; a anterior é descartada pelo consumidor

### Requirement: Documentos sem indexação enfileirada são recuperados na migração
A migração que introduz o pedido de indexação SHALL gravar um pedido para cada
documento em `Pending` no momento em que roda, com a revisão corrente do
documento, e NÃO SHALL gravar pedido para documento em `Indexing`, `Indexed` ou
`Failed`.

#### Scenario: Documento órfão em Pending ganha pedido
- **WHEN** a migração roda sobre um banco com documentos `Pending`
- **THEN** cada um deles tem exatamente um pedido, com a sua revisão corrente

#### Scenario: Documento fora de Pending não ganha pedido
- **WHEN** a migração roda sobre um banco com documentos em `Indexing`,
  `Indexed` e `Failed`
- **THEN** nenhum deles tem pedido
