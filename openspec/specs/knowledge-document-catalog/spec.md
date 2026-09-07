# knowledge-document-catalog Specification

## Purpose

TBD - defined by change knowledge-base-catalogo-documentos. Update Purpose after archive.

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
O sistema SHALL permitir atualizar um documento com o mesmo shape do cadastro
(título, `sourceType`, conteúdo). O servidor NÃO SHALL distinguir "conteúdo
editado manualmente" de "arquivo novo enviado" — nada no contrato os diferencia.

Na mesma transação, a atualização SHALL: gravar o conteúdo novo já extraído;
incrementar `contentRevision` se e somente se `extractedText` mudou; colocar
`indexingStatus` em `Pending`; limpar `failureReason`; e **preservar**
`indexedAt`.

#### Scenario: Atualizar conteúdo incrementa a revisão e volta para Pending
- **WHEN** um cliente envia `PUT /knowledge-bases/{kbId}/documents/{id}` com
  conteúdo diferente do atual
- **THEN** a API responde HTTP 200 com `contentRevision` incrementado em 1,
  `indexingStatus: "Pending"` e `failureReason: null`

#### Scenario: Atualizar apenas o título não incrementa a revisão
- **WHEN** um cliente envia `PUT /knowledge-bases/{kbId}/documents/{id}` com
  título novo e conteúdo idêntico ao atual
- **THEN** a API responde HTTP 200 com o título novo e `contentRevision`
  inalterado

#### Scenario: Atualizar com conteúdo idêntico não incrementa a revisão
- **WHEN** um cliente envia `PUT /knowledge-bases/{kbId}/documents/{id}` com
  conteúdo byte a byte idêntico ao atual
- **THEN** `contentRevision` permanece inalterado

#### Scenario: Atualização preserva indexedAt de indexação anterior
- **WHEN** um documento com `indexedAt` preenchido é atualizado
- **THEN** a resposta mantém o `indexedAt` anterior e `indexingStatus` volta
  para `Pending` — o valor de `indexedAt` continua indicando que há conteúdo
  indexado respondendo

#### Scenario: Atualizar documento inexistente retorna 404
- **WHEN** um cliente envia `PUT /knowledge-bases/{kbId}/documents/{id}` para um
  id que não existe
- **THEN** a API responde HTTP 404 e nenhum registro é alterado

#### Scenario: Atualizar documento de outra base pelo caminho errado retorna 404
- **WHEN** um cliente envia `PUT /knowledge-bases/{kbId}/documents/{id}` com um
  `id` que existe mas pertence a outra base
- **THEN** a API responde HTTP 404 e o documento da outra base permanece
  inalterado — o handler resolve o documento por `KnowledgeBaseId` **e** `Id`,
  nunca só por `Id`

#### Scenario: Atualizar documento de base inativa é permitido
- **WHEN** um cliente envia `PUT /knowledge-bases/{kbId}/documents/{id}` para um
  documento de uma base desativada
- **THEN** a API atualiza o documento normalmente — desativar a base impede seu
  uso pelo agente, não a manutenção do seu conteúdo

#### Scenario: Atualizar com conteúdo acima do teto é rejeitado
- **WHEN** um cliente envia `PUT` com conteúdo acima de 1.048.576 bytes em UTF-8
- **THEN** a API responde HTTP 400 e o documento permanece com o conteúdo e a
  revisão anteriores

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
contagem de fragmentos, a partir da etapa de indexação) **sempre que
`indexedAt` não for nulo, qualquer que seja o estado**, e SHALL omiti-la quando
`indexedAt` for nulo — nunca exibindo um valor zerado, que afirmaria que a
indexação rodou e não encontrou nada.

Nesta etapa NÃO existe fila nem consumidor de indexação. Documento criado ou
atualizado SHALL permanecer em `Pending` indefinidamente. Isso é comportamento
esperado e declarado, não defeito.

#### Scenario: Documento recém-criado nasce Pending sem data de indexação
- **WHEN** um documento é criado
- **THEN** `indexingStatus` é `Pending`, `indexedAt` é nulo e `failureReason` é
  nulo

#### Scenario: Documento permanece Pending sem consumidor
- **WHEN** um documento é criado e consultado novamente depois
- **THEN** `indexingStatus` continua `Pending` — nenhuma transição automática
  ocorre nesta etapa

### Requirement: Formato de fio dos valores de estado e de tipo de origem
`indexingStatus` e `sourceType` SHALL atravessar a API como **string**, nunca
como inteiro ordinal. `IndexingStatus` SHALL declarar
`JsonStringEnumConverter<T>` no próprio tipo.

#### Scenario: Estado de indexação sai como string no JSON
- **WHEN** um cliente consulta um documento e o JSON bruto da resposta é
  inspecionado como texto
- **THEN** o valor de `indexingStatus` é a string `"Pending"`, não um número

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

### Requirement: Autenticação das rotas de documento
Todas as rotas de documento SHALL exigir token de operador válido. Nenhuma delas
SHALL ser adicionada à allowlist de rotas anônimas.

#### Scenario: Requisição sem token é rejeitada
- **WHEN** um cliente envia qualquer requisição para
  `/knowledge-bases/{kbId}/documents` sem cabeçalho de autorização
- **THEN** a API responde HTTP 401 e nenhuma operação é executada
