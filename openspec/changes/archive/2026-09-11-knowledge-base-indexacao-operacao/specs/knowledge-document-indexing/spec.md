## ADDED Requirements

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
