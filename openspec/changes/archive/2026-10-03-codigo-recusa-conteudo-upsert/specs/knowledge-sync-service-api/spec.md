## MODIFIED Requirements

### Requirement: Upsert de documento por referência externa
`PUT /sync/knowledge-bases/{id}/documents` SHALL receber `externalRef`,
`externalVersion`, `title`, `sourceType` e `content`, e SHALL criar o documento
quando a base não tem documento com aquela referência, ou atualizá-lo quando tem.
`externalRef`, `externalVersion`, `title` e `sourceType` SHALL ser obrigatórios e
não vazios; `content` SHALL ser obrigatório. O conteúdo SHALL passar pela mesma
extração, normalização e teto de tamanho do cadastro do operador. A resposta SHALL
ser `200` com `documentId` e `outcome` (`Created`, `Updated` ou `Unchanged`, como
string).

SHALL responder `404` para base inexistente, `409` para base `Manual` e `400` para
payload inválido. Base inativa SHALL aceitar o upsert normalmente.

O `400` SHALL ser de uma de duas naturezas, distinguíveis pelo texto da resposta:

- **recusa de conteúdo** — propriedade do arquivo enviado: `too-large`,
  `unsupported-source-type`, `null-character` ou `empty-content`, na extensão
  `code` do `ValidationProblemDetails`, como no cadastro do operador;
- **recusa de forma** — campo obrigatório ausente ou em branco no payload:
  `ValidationProblemDetails` **sem** a propriedade `code`.

O cliente NÃO SHALL precisar ler a mensagem para distinguir as duas, nem para
saber qual recusa de conteúdo aconteceu.

`externalVersion` SHALL ser gravado como veio e NÃO SHALL participar de nenhuma
decisão: se o documento mudou é decidido pelo texto extraído e pelo título, pelas
mesmas regras da atualização do operador.

#### Scenario: Primeira gravação cria o documento
- **WHEN** o upsert chega para uma referência que a base não tem
- **THEN** a API responde `200` com `outcome: "Created"`, o documento é criado com
  `externalRef` e `externalVersion` enviados, um evento `Created` é registrado e a
  indexação é enfileirada

#### Scenario: Marcador diferente com texto e título iguais não tem efeito
- **WHEN** o upsert chega com `externalVersion` diferente do gravado e o mesmo
  título e o mesmo texto extraído
- **THEN** a API responde `outcome: "Unchanged"`, grava o `externalVersion` novo,
  não registra evento, não enfileira indexação, e `contentRevision`,
  `indexingStatus` e `updatedAt` do documento continuam como estavam

#### Scenario: Texto diferente atualiza e reindexa
- **WHEN** o upsert chega com texto extraído diferente do gravado
- **THEN** a API responde `outcome: "Updated"`, `contentRevision` é incrementado,
  um evento `Updated` com `contentChanged: true` é registrado e a indexação é
  enfileirada

#### Scenario: Só o título mudou
- **WHEN** o upsert chega com o mesmo texto e título diferente
- **THEN** a API responde `outcome: "Updated"`, registra evento `Updated` com
  `titleChanged: true` e `contentChanged: false`, e não enfileira indexação

#### Scenario: Upsert em base manual é recusado
- **WHEN** o upsert chega para uma base `Manual`
- **THEN** a API responde `409` e nenhum documento é criado

#### Scenario: Upsert sem referência externa é recusado
- **WHEN** o upsert chega sem `externalRef`, ou com `externalRef` vazio
- **THEN** a API responde `400` em `externalRef` e nenhum documento é criado

#### Scenario: Recusa de forma não tem código
- **WHEN** o upsert chega sem `externalRef`
- **THEN** o texto da resposta tem status `400`, `errors.externalRef`, e não tem a
  propriedade `code` — nem `too-large`, `unsupported-source-type`,
  `null-character` ou `empty-content`, nem outra

#### Scenario: Conteúdo acima do teto é recusado
- **WHEN** o upsert chega com texto extraído acima de 1 MiB
- **THEN** a API responde `400` e o documento existente, se houver, continua como
  estava, inclusive o `externalVersion`

#### Scenario: Recusa de tamanho no upsert tem o código no texto
- **WHEN** o upsert chega com texto extraído acima de 1 MiB, para uma referência
  que a base já tem
- **THEN** o texto da resposta HTTP tem status `400`, a propriedade `code` com o
  valor `too-large`, `contentBytes` igual ao tamanho do texto extraído e
  `maxContentBytes` igual ao teto do `apps/api`; e o documento continua com o
  texto, a revisão e o `externalVersion` de antes

#### Scenario: Outras recusas de conteúdo no upsert têm o seu código
- **WHEN** o upsert chega com `sourceType` sem extrator, com conteúdo contendo
  U+0000, ou com `content` vazio
- **THEN** o texto da resposta tem status `400` e `code`, respectivamente,
  `unsupported-source-type`, `null-character` e `empty-content`, e nenhum
  documento é criado

#### Scenario: Upsert dentro do teto continua como antes
- **WHEN** o upsert chega com texto extraído de exatamente 1 MiB, para uma
  referência que a base não tem
- **THEN** a API responde `200` com `outcome: "Created"`

#### Scenario: Referência comparada como veio
- **WHEN** a base tem um documento com `externalRef` `AbC` e chega um upsert com
  `externalRef` `abc`
- **THEN** a API cria um segundo documento, com `outcome: "Created"`
