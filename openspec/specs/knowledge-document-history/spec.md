# knowledge-document-history Specification

## Purpose

Cobre o **histórico de mudanças nos documentos de uma base de conhecimento** em
`apps/api`: quais escritas de documento geram evento e quais não geram, o que
cada evento carrega, como ele sobrevive ao documento e morre com a base, e a
rota de leitura paginada por base.

Esta capability não decide o que cada escrita faz com o documento — isso
continua sendo de `knowledge-document-catalog`, que não muda. Aqui se afirma só
o rastro que a escrita deixa: um evento por inclusão, por exclusão e por
atualização que muda texto ou título, gravado na mesma transação que o
documento. A ordem da lista de eventos segue `api-response-ordering` como ela já
está escrita (mais recente primeiro, desempate por `id`), com cenário próprio
aqui para a fronteira de página.

## Requirements

### Requirement: Inclusão de documento registra evento
Toda inclusão de documento bem-sucedida numa base SHALL registrar exatamente um
evento do tipo `Created` nessa base, com o identificador do documento, o título
como gravado, o autor da escrita e o instante. O evento SHALL ser gravado na
mesma transação que o documento: nenhum dos dois existe sem o outro.

`contentChanged` e `titleChanged` SHALL ser nulos num evento `Created`.

#### Scenario: Cadastro gera um evento Created
- **WHEN** o operador cadastra um documento numa base
- **THEN** a base passa a ter exatamente um evento `Created`, com o `id` do
  documento, o `title` enviado como `documentTitle`, `author` igual a
  `operator`, e `contentChanged` e `titleChanged` nulos

#### Scenario: Cadastro recusado por conteúdo inválido não gera evento
- **WHEN** o cadastro é recusado com `400` por conteúdo inválido para o tipo de
  origem
- **THEN** a base não tem nenhum evento novo

#### Scenario: Cadastro recusado por conteúdo acima do teto não gera evento
- **WHEN** o cadastro é recusado com `400` porque o texto extraído passa do teto
  de tamanho
- **THEN** a base não tem nenhum evento novo

#### Scenario: Cadastro em base inexistente não gera evento
- **WHEN** o cadastro é feito para uma base que não existe e responde `404`
- **THEN** não existe nenhum evento com esse `KnowledgeBaseId`

### Requirement: Atualização só registra evento quando texto ou título mudam
Uma atualização de documento bem-sucedida SHALL registrar exatamente um evento
`Updated` se e somente se o texto extraído ou o título mudou em relação ao
gravado, em comparação ordinal. O evento SHALL dizer qual dos dois mudou em
`contentChanged` e `titleChanged`, ambos não nulos.

O critério de "conteúdo alterado" SHALL ser o mesmo que incrementa
`ContentRevision`, e MUST NOT ser o `ContentHash`. Numa linha legada com
`ContentHash` nulo, reenviar conteúdo idêntico volta o documento para a fila de
indexação, e isso continua valendo, mas MUST NOT gerar evento.

Trocar só o `sourceType`, sem mudar texto nem título, MUST NOT gerar evento.

#### Scenario: Só o texto muda
- **WHEN** o operador atualiza um documento com texto diferente e o mesmo título
- **THEN** a base ganha um evento `Updated` com `contentChanged = true` e
  `titleChanged = false`

#### Scenario: Só o título muda
- **WHEN** o operador atualiza um documento com o mesmo texto e título diferente
- **THEN** a base ganha um evento `Updated` com `contentChanged = false`,
  `titleChanged = true` e `documentTitle` igual ao título novo

#### Scenario: Texto e título mudam juntos
- **WHEN** o operador atualiza um documento com texto e título diferentes
- **THEN** a base ganha **um** evento `Updated`, com `contentChanged = true` e
  `titleChanged = true`, e não dois eventos

#### Scenario: Conteúdo e título idênticos não geram evento
- **WHEN** o operador reenvia exatamente o texto e o título gravados
- **THEN** a atualização responde `200` e a base não ganha nenhum evento

#### Scenario: Linha legada com hash nulo e conteúdo idêntico não gera evento
- **WHEN** um documento tem `ContentHash` nulo, como as linhas anteriores ao
  hash, e o operador reenvia exatamente o texto e o título gravados
- **THEN** o documento volta para `Pending` e uma indexação é enfileirada, como
  hoje
- **AND** a base não ganha nenhum evento, em particular nenhum com
  `contentChanged = true`

#### Scenario: Atualização recusada por conteúdo inválido não gera evento
- **WHEN** a atualização é recusada com `400` por conteúdo inválido
- **THEN** a base não ganha nenhum evento

#### Scenario: Atualização recusada por conteúdo acima do teto não gera evento
- **WHEN** a atualização é recusada com `400` porque o texto extraído passa do
  teto
- **THEN** a base não ganha nenhum evento

#### Scenario: Atualização de documento inexistente ou de outra base não gera evento
- **WHEN** a atualização é feita para um documento que não existe, ou que
  existe em outra base, e responde `404`
- **THEN** nenhuma das duas bases ganha evento

#### Scenario: O banco recusa Updated sem mudança
- **WHEN** uma linha `Updated` com `ContentChanged = false` e
  `TitleChanged = false` é inserida diretamente em `knowledge_document_events`
- **THEN** o banco recusa a inserção pela restrição de verificação

### Requirement: Exclusão registra evento que sobrevive ao documento
A exclusão bem-sucedida de um documento SHALL registrar exatamente um evento
`Deleted` na base, com o identificador do documento e o título que ele tinha, na
mesma transação da exclusão. O evento MUST NOT ter FK para o documento e SHALL
continuar existindo depois que o documento some.

`contentChanged` e `titleChanged` SHALL ser nulos num evento `Deleted`.

#### Scenario: Evento de exclusão continua existindo depois que o documento some
- **WHEN** o operador exclui um documento e a exclusão responde `204`
- **THEN** `GET` do documento responde `404`
- **AND** a rota de eventos da base devolve o `Deleted` com o `documentId` e o
  título do documento excluído
- **AND** os eventos anteriores desse documento (`Created`, `Updated`) também
  continuam existindo

#### Scenario: Exclusão de documento inexistente ou de outra base não gera evento
- **WHEN** a exclusão é feita para um documento que não existe, ou que existe em
  outra base, e responde `404`
- **THEN** nenhuma das duas bases ganha evento

### Requirement: Eventos pertencem à base e morrem com ela
Os eventos de uma base SHALL ser removidos pelo banco quando a base é removida,
por FK com `ON DELETE CASCADE`.

#### Scenario: Excluir a base remove os eventos dela e só os dela
- **WHEN** uma base teve documentos incluídos e depois excluídos, de modo que
  não tem documento mas tem eventos, e a linha dela é apagada de
  `knowledge_bases`
- **THEN** `knowledge_document_events` não tem mais nenhuma linha dessa base
- **AND** os eventos de outra base continuam intactos

### Requirement: Reindexação e escritas de indexação não registram evento
Reindexar um documento MUST NOT registrar evento, porque não muda texto nem
título. As transições de estado de indexação gravadas por `apps/workers` também
MUST NOT registrar evento.

#### Scenario: Reindexar não gera evento
- **WHEN** o operador reindexa um documento e a rota responde `200`
- **THEN** a base não ganha nenhum evento

### Requirement: Autor é o subject de quem escreveu
O `author` de cada evento SHALL ser o subject do token autenticado que fez a
escrita, gravado como veio, sem tradução para rótulo. Nesta capability o único
escritor é o operador, cujo subject é `operator`.

#### Scenario: Escrita do operador grava author operator
- **WHEN** o operador, autenticado pelo login, cadastra, atualiza e exclui um
  documento
- **THEN** os três eventos têm `author` igual a `operator`

### Requirement: Rota paginada de eventos de uma base
`GET /knowledge-bases/{knowledgeBaseId}/document-events` SHALL devolver os
eventos **só** da base pedida, do mais recente para o mais antigo (`occurredAt`
decrescente, desempate por `id` decrescente), em páginas de 50, no formato
`{ "items": [...], "nextCursor": string | null }`.

A próxima página SHALL ser pedida com `?cursor=<nextCursor>` e SHALL começar
estritamente depois do último item da página anterior. `nextCursor` SHALL ser
nulo na última página.

Base inexistente SHALL responder `404`. Base existente sem evento SHALL
responder `200` com `items` vazio e `nextCursor` nulo. Base inativa SHALL ser
lida normalmente. Cursor malformado SHALL responder `400` com erro de validação
na chave `cursor`.

A rota SHALL exigir autenticação, como toda rota de conhecimento, e MUST NOT
entrar no escopo de nenhum subject de serviço: só o operador a lê.

#### Scenario: A rota só devolve eventos da base pedida
- **WHEN** duas bases têm eventos e a rota é consultada para uma delas
- **THEN** nenhum item devolvido tem `documentId` de documento da outra base, e
  todos os eventos da base pedida estão presentes

#### Scenario: Cursor obtido em outra base não devolve eventos dela
- **WHEN** um `nextCursor` obtido na base A é usado na rota da base B
- **THEN** a resposta só contém eventos da base B

#### Scenario: Mais recente primeiro
- **WHEN** um documento é cadastrado, atualizado e excluído, nessa ordem
- **THEN** a rota devolve `Deleted`, `Updated` e `Created`, nessa ordem

#### Scenario: Desempate por id decrescente atravessa a fronteira de página
- **WHEN** a base tem mais de 50 eventos e alguns têm o **mesmo** `occurredAt`,
  inseridos em ordem oposta à ordem decrescente dos seus `id`, e esse grupo
  empatado cai na fronteira entre a primeira e a segunda página
- **THEN** a concatenação das páginas devolve cada evento exatamente uma vez, e
  os empatados saem em ordem **decrescente** de `id`

#### Scenario: Evento novo entre páginas não duplica nem desloca
- **WHEN** o cliente lê a primeira página, um evento novo é gravado na base, e o
  cliente pede a página seguinte com o `nextCursor`
- **THEN** a página seguinte não repete nenhum item da primeira e não contém o
  evento novo

#### Scenario: Última página tem cursor nulo
- **WHEN** a base tem exatamente 50 eventos
- **THEN** a primeira página traz os 50 com `nextCursor` nulo, sem obrigar o
  cliente a pedir uma página vazia

#### Scenario: Com 51 eventos há uma segunda página de um item
- **WHEN** a base tem exatamente 51 eventos
- **THEN** a primeira página traz 50 com `nextCursor` não nulo, e a segunda traz
  o evento mais antigo com `nextCursor` nulo

#### Scenario: Base sem evento responde lista vazia
- **WHEN** a base existe e nunca teve documento
- **THEN** a rota responde `200` com `items` vazio e `nextCursor` nulo

#### Scenario: Base inexistente responde 404
- **WHEN** a rota é consultada com um `knowledgeBaseId` que não existe
- **THEN** a resposta é `404`

#### Scenario: Cursor malformado responde 400
- **WHEN** a rota é consultada com `cursor` que não é um cursor emitido por ela
- **THEN** a resposta é `400` com erro de validação na chave `cursor`

#### Scenario: Rota sem token responde 401
- **WHEN** a rota é consultada sem token
- **THEN** a resposta é `401`

#### Scenario: Rota com token de serviço responde 403
- **WHEN** a rota é consultada com um token de subject `service:inbox`
- **THEN** a resposta é `403`

### Requirement: Formato de fio dos eventos
Cada item SHALL ter, em camelCase no fio, `id`, `documentId`, `documentTitle`,
`type`, `contentChanged`, `titleChanged`, `author` e `occurredAt`. `type` SHALL
sair como string (`"Created"`, `"Updated"`, `"Deleted"`), nunca como ordinal.

#### Scenario: O texto da resposta tem type como string e campos em camelCase
- **WHEN** a rota devolve um evento de cada tipo
- **THEN** o **texto** JSON da resposta HTTP contém `"type":"Created"`,
  `"type":"Updated"` e `"type":"Deleted"`, e as chaves `documentTitle`,
  `contentChanged`, `titleChanged`, `occurredAt` e `nextCursor`, verificado sobre
  o texto, nunca por desserialização para o mesmo tipo

### Requirement: Sem eventos retroativos
A migração que cria o histórico MUST NOT criar eventos para documentos que já
existiam. O histórico SHALL começar vazio e só registrar escritas feitas depois
da implantação.

#### Scenario: Documentos anteriores à migração não ganham evento
- **WHEN** um banco migrado até a migração anterior tem documentos, e a migração
  do histórico é aplicada
- **THEN** `knowledge_document_events` tem zero linhas
