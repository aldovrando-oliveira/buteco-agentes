## MODIFIED Requirements

### Requirement: Ciclo de vida de estado de indexação
`IndexingStatus` SHALL ter exatamente quatro valores: `Pending`, `Indexing`,
`Indexed` e `Failed`. NÃO SHALL existir valor separado para reindexação: a
distinção entre "nunca indexado" e "há conteúdo indexado respondendo" SHALL ser
carregada por `indexedAt` (nulo no primeiro caso, preenchido no segundo), em
qualquer um dos quatro estados.

Consumidores da interface SHALL exibir informação derivada da indexação (como
contagem de fragmentos) **sempre que `indexedAt` não for nulo, qualquer que seja
o estado**, e SHALL omiti-la quando `indexedAt` for nulo — nunca exibindo um
valor zerado, que afirmaria que a indexação rodou e não encontrou nada.

Documento criado ou atualizado com conteúdo novo SHALL transitar de `Pending`
para `Indexing` e daí para `Indexed` ou `Failed`, sem intervenção. Os quatro
valores passam a ter escritor.

Esta redação **substitui** a que a etapa 1 escreveu — "não existe fila nem
consumidor; documento SHALL permanecer em `Pending` indefinidamente" — junto com
o cenário que a afirmava. Aquilo era o contrato da ausência de consumidor,
declarado para que esta etapa encontrasse um estado esperado em vez de um bug
aparente; o consumidor passa a existir e a permanência deixa de ser verdadeira.
Nenhum valor de enum é removido ou renomeado, e nenhum dado precisa migrar:
documentos parados em `Pending` são enfileirados pelo caminho normal.

#### Scenario: Documento recém-criado nasce Pending sem data de indexação
- **WHEN** um documento é criado
- **THEN** `indexingStatus` é `Pending`, `indexedAt` é nulo e `failureReason` é
  nulo

#### Scenario: Documento criado transita para Indexed
- **WHEN** um documento é criado e a indexação dele conclui com sucesso
- **THEN** `indexingStatus` é `Indexed` e `indexedAt` está preenchido

#### Scenario: Documento reindexado mantém a distinção carregada por indexedAt
- **WHEN** um documento já indexado é atualizado e volta para `Pending`
- **THEN** `indexedAt` mantém o valor anterior, indicando que há conteúdo
  indexado respondendo enquanto a indexação nova não termina

### Requirement: Formato de fio dos valores de estado e de tipo de origem
`indexingStatus` e `sourceType` SHALL atravessar a API como **string**, nunca
como inteiro ordinal. `IndexingStatus` SHALL declarar
`JsonStringEnumConverter<T>` no próprio tipo.

A verificação SHALL cobrir os **quatro** valores de `IndexingStatus`, e não
apenas `Pending`: os outros três passam a ter escritor nesta etapa e passam a
poder aparecer numa resposta real.

#### Scenario: Estado de indexação sai como string no JSON
- **WHEN** um cliente consulta um documento e o JSON bruto da resposta é
  inspecionado como texto
- **THEN** o valor de `indexingStatus` é a string `"Pending"`, não um número

#### Scenario: Os quatro estados saem como string no JSON
- **WHEN** o JSON bruto de respostas de documentos em `Pending`, `Indexing`,
  `Indexed` e `Failed` é inspecionado como texto
- **THEN** cada valor de `indexingStatus` é a string correspondente
  (`"Pending"`, `"Indexing"`, `"Indexed"`, `"Failed"`), nunca um número

#### Scenario: Tipo de origem sai como string no JSON
- **WHEN** um cliente consulta um documento e o JSON bruto da resposta é
  inspecionado como texto
- **THEN** o valor de `sourceType` é a string `"markdown"`, não um número

### Requirement: Atualização de documento
`PUT /knowledge-bases/{kbId}/documents/{id}` SHALL receber o mesmo shape do
`POST` e NÃO SHALL distinguir edição manual de arquivo novo — nada no contrato
os diferencia.

Numa única operação, a atualização SHALL: gravar o conteúdo novo; incrementar
`contentRevision` se e somente se `extractedText` mudou; limpar `failureReason`;
e **preservar** `indexedAt`.

`indexingStatus` SHALL voltar para `Pending` **apenas quando o conteúdo extraído
tiver mudado**, aferido por `contentHash`. Atualização cujo conteúdo seja
idêntico ao gravado SHALL preservar o estado de indexação corrente e NÃO SHALL
enfileirar indexação nova.

#### Scenario: Atualizar conteúdo incrementa a revisão e volta para Pending
- **WHEN** um documento tem o `content` alterado
- **THEN** `contentRevision` é incrementado, `indexingStatus` volta para
  `Pending` e `failureReason` fica nulo

#### Scenario: Atualizar só o título preserva o estado de indexação
- **WHEN** um documento indexado é atualizado com o mesmo `content` e título
  diferente
- **THEN** `contentRevision` não muda, `indexingStatus` permanece `Indexed` e
  `indexedAt` é preservado

#### Scenario: Atualização preserva indexedAt de indexação anterior
- **WHEN** um documento com `indexedAt` preenchido tem o conteúdo alterado
- **THEN** a resposta mantém o `indexedAt` anterior e `indexingStatus` volta
  para `Pending` — o valor de `indexedAt` continua indicando que há conteúdo
  indexado respondendo

## ADDED Requirements

### Requirement: Documento expõe contagem de fragmentos e histórico de tentativas
As respostas de documento — listagem e consulta por id — SHALL incluir
`fragmentCount` (inteiro), `indexingAttempts` (inteiro) e `lastAttemptAt`
(instante, anulável).

`fragmentCount` SHALL refletir o número de fragmentos gravados para o documento.
A regra de exibição é a do ciclo de vida: consumidores o exibem quando
`indexedAt` não for nulo e o omitem quando for nulo, nunca exibindo zero.

#### Scenario: Listagem inclui os campos de indexação
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents`
- **THEN** cada item inclui `fragmentCount`, `indexingAttempts` e
  `lastAttemptAt`, além dos campos já existentes

#### Scenario: Consulta por id inclui os campos de indexação
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents/{id}`
- **THEN** a resposta inclui `fragmentCount`, `indexingAttempts` e
  `lastAttemptAt`

#### Scenario: Documento nunca indexado devolve tentativas zeradas e sem data
- **WHEN** um documento recém-criado é consultado antes de qualquer tentativa
- **THEN** `indexingAttempts` é zero e `lastAttemptAt` é nulo
