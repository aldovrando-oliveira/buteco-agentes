# knowledge-document-catalog Specification

## Purpose

Cobre o **cadastro de documentos dentro de uma base de conhecimento** em
`apps/api`: criação, listagem, consulta por id, atualização e exclusão, mais o
teto de tamanho do conteúdo e o contrato do ciclo de vida de indexação que esses
documentos carregam.

Três coisas que esta capability afirma e que são decisão, não detalhe:

- **Exclusão é real, não `IsActive`.** Documento é *conteúdo*, não entidade de
  catálogo referenciada por vínculos — e o caso de uso concreto (o operador subiu
  o arquivo errado, ou um com dado que não devia estar ali) é exatamente aquele
  em que "continua no banco, invisível" é a resposta errada. É o único `DELETE`
  real do repositório.
- **A listagem não devolve o conteúdo**, e o tamanho exposto é medido pelo banco,
  em bytes UTF-8 — a mesma unidade que a validação do teto usa, sobre a mesma
  string.
- **O ciclo de vida de indexação tem quatro estados, e a distinção entre "nunca
  indexado" e "há conteúdo indexado respondendo agora" é carregada por
  `indexedAt`**, não por um valor de enum. É o que permite a regra de exibição da
  contagem de fragmentos: exibida quando `indexedAt` não é nulo, **omitida**
  quando é nulo, nunca zerada.

**Não cobre** o pipeline que produz os fragmentos — fragmentação, embedding,
fila, transições de estado e integridade do índice são de
`knowledge-document-indexing`. Aqui mora o cadastro; lá, a execução.

## Requirements

### Requirement: Cadastro de documento numa base
O sistema SHALL permitir, via `apps/api`, cadastrar um documento dentro de uma
base de conhecimento informando título, `sourceType` e conteúdo como texto.

O corpo SHALL ser JSON. NÃO SHALL haver rota multipart nesta etapa: os formatos
aceitos são texto, e o cliente envia o conteúdo já lido como string. O servidor
SHALL receber título explícito e NÃO SHALL derivá-lo do nome de arquivo — a
derivação é sugestão editável do cliente. O mesmo endpoint SHALL servir tanto o
caminho "arquivo subido" quanto o caminho "texto digitado": nada no contrato os
distingue.

O conteúdo SHALL passar pelo extrator do `sourceType` informado antes de ser
persistido em `extractedText`.

#### Scenario: Criar documento com conteúdo markdown
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` com título
  não vazio, `sourceType: "markdown"` e conteúdo não vazio, para uma base
  existente
- **THEN** a API responde HTTP 201 com o documento criado, incluindo id, título,
  `sourceType`, `indexingStatus: "Pending"`, `indexedAt: null`,
  `failureReason: null` e `contentRevision: 1`

#### Scenario: Criar documento em base inexistente retorna 404
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` para um
  `kbId` que não existe
- **THEN** a API responde HTTP 404 e nenhum documento é criado

#### Scenario: Criar documento em base inativa é permitido
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` para uma
  base desativada
- **THEN** a API cria o documento normalmente — desativar a base impede seu uso
  pelo agente, não a manutenção do seu conteúdo

#### Scenario: Criar documento sem título é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` sem título,
  ou com título vazio ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

#### Scenario: Criar documento com conteúdo vazio é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases/{kbId}/documents` com
  conteúdo vazio ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

#### Scenario: Título duplicado na mesma base é permitido
- **WHEN** um cliente cria dois documentos com o mesmo título na mesma base
- **THEN** a API cria os dois normalmente, cada um com identificador próprio

### Requirement: Teto de tamanho de conteúdo por documento
O sistema SHALL rejeitar conteúdo cujo tamanho em UTF-8 exceda 1 MiB
(1.048.576 bytes), respondendo HTTP 400 com `ValidationProblemDetails`. O limite
SHALL ser validado no handler, e NÃO SHALL depender do limite de corpo de
requisição do servidor web — que responderia HTTP 413 sem corpo interpretável
pela interface.

#### Scenario: Conteúdo dentro do teto é aceito
- **WHEN** um cliente envia um documento cujo conteúdo tem 1.048.576 bytes em
  UTF-8 ou menos
- **THEN** a API cria o documento normalmente

#### Scenario: Conteúdo acima do teto é rejeitado com 400
- **WHEN** um cliente envia um documento cujo conteúdo excede 1.048.576 bytes em
  UTF-8
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` identificando
  o campo de conteúdo, e não cria nenhum registro

#### Scenario: O teto é medido depois da extração, não sobre a entrada crua
- **WHEN** um cliente envia um documento com BOM UTF-8 e terminadores `CRLF`
  cujo tamanho **bruto** excede 1.048.576 bytes, mas cujo texto já extraído
  (BOM removido, `CRLF` normalizado para `LF`) não excede
- **THEN** a API cria o documento normalmente, e o `contentLengthBytes`
  devolvido é o do texto extraído — o mesmo valor que a validação usou

### Requirement: Listagem de documentos de uma base
O sistema SHALL permitir listar os documentos de uma base. A resposta da
listagem NÃO SHALL incluir o conteúdo do documento; SHALL incluir o tamanho do
conteúdo em `contentLengthBytes`, sem materializar o texto no servidor.

`contentLengthBytes` SHALL estar na **mesma unidade do teto validado no
cadastro** — bytes em UTF-8 — e SHALL medir **a mesma string** que a validação
mede (o texto já extraído), para que a interface possa exibir quanto falta para
o limite. Expor caracteres onde a validação usa bytes divergiria de forma
material em texto acentuado e SHALL ser tratado como defeito de contrato, do
mesmo tipo que expor enum como ordinal.

O valor SHALL ser derivado do conteúdo pelo próprio banco, nunca escrito pela
aplicação — não há caminho de escrita de conteúdo que possa deixá-lo
desatualizado.

#### Scenario: Lista retorna os documentos da base
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents` para uma
  base com documentos
- **THEN** a API responde HTTP 200 com a lista, cada item incluindo id, título,
  `sourceType`, `indexingStatus`, `indexedAt`, `failureReason` e
  `contentLengthBytes`

#### Scenario: Tamanho exposto é o mesmo que a validação usa
- **WHEN** um documento é criado com conteúdo acentuado, cujo tamanho em bytes
  UTF-8 difere do número de caracteres
- **THEN** o `contentLengthBytes` devolvido na listagem é igual ao número de
  bytes UTF-8 do conteúdo, não ao número de caracteres

#### Scenario: Listagem não inclui o conteúdo do documento
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents`
- **THEN** o JSON da resposta não contém a chave `extractedText` em nenhum item

#### Scenario: Lista de base sem nenhum documento
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents` para uma
  base existente sem documentos
- **THEN** a API responde HTTP 200 com uma lista vazia, nunca HTTP 404

#### Scenario: Lista de base inexistente retorna 404
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents` para um
  `kbId` que não existe
- **THEN** a API responde HTTP 404

### Requirement: Consulta de documento por id
O sistema SHALL permitir consultar um documento específico. A resposta SHALL
incluir o conteúdo (`extractedText`) — é o que preenche a edição manual — e
SHALL incluir **todos os campos da listagem**, inclusive `contentLengthBytes`,
para que a tela de edição não precise de uma segunda requisição nem recalcule o
tamanho por conta própria.

#### Scenario: Consulta de documento existente inclui o conteúdo
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents/{id}` para um
  documento existente daquela base
- **THEN** a API responde HTTP 200 com todos os campos do documento, incluindo
  `extractedText` e `contentLengthBytes`

#### Scenario: Consulta de documento inexistente retorna 404
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents/{id}` para um
  id que não existe
- **THEN** a API responde HTTP 404

#### Scenario: Documento de outra base não é acessível pelo caminho errado
- **WHEN** um cliente envia `GET /knowledge-bases/{kbId}/documents/{id}` com um
  `id` que existe mas pertence a outra base
- **THEN** a API responde HTTP 404

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

### Requirement: Exclusão de documento
O sistema SHALL permitir excluir um documento definitivamente, via
`DELETE /knowledge-bases/{kbId}/documents/{id}`. A exclusão SHALL remover o
registro do banco, não marcá-lo como inativo.

Esta é a única entidade do repositório com exclusão real. A justificativa está
registrada no `design.md` (D6): documento é conteúdo, não entidade de catálogo
com vínculos apontando para ela, e o caso de uso concreto — arquivo errado, ou
com dado que não devia estar ali — é exatamente aquele em que manter o registro
invisível no banco é a resposta errada.

#### Scenario: Excluir documento existente
- **WHEN** um cliente envia `DELETE /knowledge-bases/{kbId}/documents/{id}` para
  um documento existente
- **THEN** a API responde HTTP 204 e o documento deixa de aparecer na listagem e
  na consulta por id

#### Scenario: Excluir documento inexistente retorna 404
- **WHEN** um cliente envia `DELETE /knowledge-bases/{kbId}/documents/{id}` para
  um id que não existe
- **THEN** a API responde HTTP 404 e nenhum registro é afetado

#### Scenario: Excluir documento de outra base pelo caminho errado retorna 404
- **WHEN** um cliente envia `DELETE /knowledge-bases/{kbId}/documents/{id}` com
  um `id` que existe mas pertence a outra base
- **THEN** a API responde HTTP 404 e o documento da outra base **continua
  existindo** — o handler resolve o documento por `KnowledgeBaseId` **e** `Id`,
  nunca só por `Id`. Como a exclusão é irreversível, este é o cenário mais caro
  de errar de toda a change

#### Scenario: Excluir documento de base inativa é permitido
- **WHEN** um cliente envia `DELETE /knowledge-bases/{kbId}/documents/{id}` para
  um documento de uma base desativada
- **THEN** a API exclui o documento normalmente, pelo mesmo motivo que permite
  criar e atualizar em base inativa

#### Scenario: Excluir documento não afeta os demais da base
- **WHEN** um cliente exclui um documento de uma base que tem outros documentos
- **THEN** os demais documentos permanecem inalterados e continuam listados

#### Scenario: Excluir documento não afeta a base
- **WHEN** um cliente exclui o último documento de uma base
- **THEN** a base continua existindo e sua listagem de documentos responde HTTP
  200 com lista vazia

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

### Requirement: Marca de revisão de conteúdo
Cada documento SHALL ter `contentRevision` (inteiro, iniciando em 1),
incrementado **apenas** quando `extractedText` muda. O consumidor de indexação
SHALL, na etapa de indexação, ler a revisão no início do trabalho e gravar o
resultado condicionado a ela ainda ser a corrente, descartando o resultado
inteiro se tiver mudado.

`contentRevision` SHALL ser coluna explícita, não `xmin`: o consumidor muta o
próprio registro ao transicionar de estado, e um token de linha invalidaria o
próprio trabalho em curso.

#### Scenario: Revisão começa em 1
- **WHEN** um documento é criado
- **THEN** `contentRevision` é 1

#### Scenario: Revisão é exposta na API
- **WHEN** um cliente consulta um documento
- **THEN** a resposta inclui `contentRevision` com o valor corrente

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

### Requirement: Autenticação das rotas de documento
Todas as rotas de documento SHALL exigir token de operador válido. Nenhuma delas
SHALL ser adicionada à allowlist de rotas anônimas.

#### Scenario: Requisição sem token é rejeitada
- **WHEN** um cliente envia qualquer requisição para
  `/knowledge-bases/{kbId}/documents` sem cabeçalho de autorização
- **THEN** a API responde HTTP 401 e nenhuma operação é executada
