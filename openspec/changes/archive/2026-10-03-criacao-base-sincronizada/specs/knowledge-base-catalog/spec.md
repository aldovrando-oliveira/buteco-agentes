## MODIFIED Requirements

### Requirement: Cadastro de base de conhecimento
O sistema SHALL permitir, via `apps/api`, cadastrar uma base de conhecimento
informando nome e descrição. A base SHALL nascer ativa (`isActive: true`) e
receber um identificador único gerado pelo sistema.

A descrição NÃO é texto decorativo: a partir da etapa de execução ela é o texto
que o modelo lê para decidir se a base é relevante para a pergunta. Por isso ela
SHALL ser obrigatória e não vazia.

O cadastro SHALL aceitar `contentMode` opcional. Omitido ou `Manual`, a base SHALL
nascer `Manual`, e `provider` e `folderId` SHALL ser proibidos: `null` SHALL ser
tratado como ausente, e qualquer valor não nulo de um dos dois, inclusive string
vazia ou só de espaços, SHALL ser recusado com `400`. `Synced` SHALL
exigir `provider`, no formato de código `^[a-z0-9]+(-[a-z0-9]+)*\z` com até 64 caracteres, e
`folderId`, não vazio nem só de espaços, com até 256 caracteres; a falta ou a forma
inválida de qualquer um SHALL ser recusada com `400` antes de qualquer chamada ao
`apps/connectors`. Valor desconhecido de `contentMode` SHALL ser recusado com `400`.

A base `Synced` SHALL ser criada só depois de a pasta ser validada pelo
`apps/connectors` (capability `knowledge-sync-folder-validation`). O nome e a URL
da pasta gravados SHALL ser os devolvidos pela validação; `folderName` e
`folderUrl` enviados no corpo SHALL ser ignorados. O id da pasta SHALL ser gravado
como foi enviado, sem normalizar caixa nem aparar espaços. A base `Synced` SHALL
nascer sem estado de sincronização: os três instantes nulos, `lastError: null` e
`ignoredFiles: null`.

#### Scenario: Criar base com nome e descrição
- **WHEN** um cliente envia `POST /knowledge-bases` com nome e descrição não
  vazios
- **THEN** a API responde HTTP 201 com a base criada, incluindo id, nome,
  descrição e `isActive: true`

#### Scenario: Criar base sem nome é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases` sem nome, ou com
  nome vazio ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

#### Scenario: Criar base sem descrição é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases` sem descrição, ou com
  descrição vazia ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

#### Scenario: Nome duplicado é permitido
- **WHEN** um cliente envia `POST /knowledge-bases` com um nome já usado por
  outra base
- **THEN** a API cria a segunda base normalmente, com identificador próprio

#### Scenario: Tipo omitido cria base manual
- **WHEN** um cliente envia `POST /knowledge-bases` sem `contentMode`
- **THEN** a base criada tem `contentMode: "Manual"`, `syncSource: null` e
  `syncState: null`

#### Scenario: Base manual com provedor ou pasta é recusada
- **WHEN** um cliente envia `POST /knowledge-bases` sem `contentMode`, ou com
  `Manual`, trazendo `provider` ou `folderId` com qualquer valor não nulo,
  inclusive `""` ou só de espaços
- **THEN** a API responde HTTP 400 no campo enviado, não cria nenhum registro e
  não chama o `apps/connectors`

#### Scenario: Base manual com provedor e pasta nulos é aceita
- **WHEN** um cliente envia `POST /knowledge-bases` com `contentMode: "Manual"`,
  `"provider": null` e `"folderId": null`
- **THEN** a API responde HTTP 201 com uma base `Manual`, `syncSource: null`, e não
  chama o `apps/connectors`

#### Scenario: Base sincronizada sem provedor ou sem pasta é recusada antes da chamada externa
- **WHEN** um cliente envia `POST /knowledge-bases` com `contentMode: "Synced"` sem
  `provider`, sem `folderId`, com `provider` fora do formato de código (por exemplo
  `"../x"` ou `"Google Drive"`) ou com `folderId` só de espaços
- **THEN** a API responde HTTP 400 no campo correspondente, não cria nenhum
  registro e o `apps/connectors` não recebe nenhuma requisição

#### Scenario: Base sincronizada criada com os snapshots da validação
- **WHEN** um cliente envia `POST /knowledge-bases` com `contentMode: "Synced"`,
  `provider` e `folderId` de uma pasta válida, e também `folderName` e `folderUrl`
  com valores diferentes dos que o `apps/connectors` devolve
- **THEN** a API responde HTTP 201 com `contentMode: "Synced"` e `syncSource` com o
  provedor e o id enviados e o nome e a URL devolvidos pelo `apps/connectors`; os
  mesmos valores estão gravados no banco, e o nome e a URL enviados no corpo não
  aparecem nem na resposta nem no banco

#### Scenario: Base sincronizada nasce sem estado de sincronização
- **WHEN** uma base `Synced` acaba de ser criada pela rota
- **THEN** `syncState` traz `lastCompletedAt`, `lastFinishedAt` e `failingSince`
  nulos, `lastError: null` e `ignoredFiles: null`

#### Scenario: Id da pasta gravado como veio
- **WHEN** um cliente cria uma base `Synced` com `folderId` igual a `AbC` e o
  `apps/connectors` devolve a pasta com o mesmo id
- **THEN** o id gravado é `AbC`, sem mudança de caixa

## ADDED Requirements

### Requirement: Pasta em uso recusada no cadastro com a base que a usa
O cadastro SHALL recusar com `409` a base `Synced` cujo provedor e id de pasta já
são usados por outra base, ativa ou inativa, respondendo com `ProblemDetails`, a extensão `code`
igual a `folder-in-use`, as extensões `knowledgeBaseId` e `knowledgeBaseName` com
a base que usa a pasta, e um `detail` que nomeia essa base e diz que a pasta
continua ocupada mesmo com a base inativa. A resposta NÃO SHALL mencionar excluir,
remover ou apagar a base, porque não há rota de exclusão de base. Nenhuma base
SHALL ser criada.

A pasta em uso SHALL ser detectada antes da chamada ao `apps/connectors`, e por
isso SHALL responder `409` mesmo com o `apps/connectors` fora do ar ou não
configurado. A corrida entre dois cadastros simultâneos da mesma pasta, que passa
pela detecção antes da gravação, SHALL ser resolvida pelo índice único da pasta:
a violação desse índice SHALL virar o mesmo `409`, com a base vencedora, e NUNCA
`500`.

#### Scenario: Pasta usada por base ativa
- **WHEN** um cliente cria uma base `Synced` com o provedor e a pasta de uma base
  ativa já existente
- **THEN** a API responde HTTP 409 com `code: "folder-in-use"`, o id e o nome da
  base existente, e não cria nenhum registro

#### Scenario: Pasta usada por base inativa
- **WHEN** a base que usa a pasta está inativa e um cliente cria outra base
  `Synced` com a mesma pasta
- **THEN** a API responde HTTP 409 com o nome da base inativa e um `detail` que diz
  que a pasta continua ocupada mesmo com a base inativa

#### Scenario: A mensagem não manda excluir a base
- **WHEN** a API responde 409 por pasta em uso
- **THEN** o texto inteiro da resposta não contém `exclu`, `remov` nem `apag`, em
  qualquer caixa

#### Scenario: Pasta em uso com o apps/connectors fora do ar
- **WHEN** a pasta já é usada por outra base e o `apps/connectors` não responde,
  ou `Connectors:BaseUrl` não está configurado
- **THEN** a API responde HTTP 409 com `code: "folder-in-use"`, e o
  `apps/connectors` não recebe nenhuma requisição

#### Scenario: Corrida de dois cadastros da mesma pasta
- **WHEN** dois cadastros `Synced` da mesma pasta passam juntos pela detecção de
  pasta em uso, com a validação no `apps/connectors` retida até os dois chegarem
- **THEN** exatamente um responde HTTP 201 e o outro HTTP 409 com o nome da base
  criada pelo primeiro, nenhum responde 500, há uma base só com aquela pasta no
  banco, e o evento de log de corrida rejeitada foi registrado uma vez

#### Scenario: Pastas que diferem só na caixa não conflitam
- **WHEN** existe uma base `Synced` com a pasta `AbC` e um cliente cria outra, do
  mesmo provedor, com a pasta `abc`
- **THEN** a segunda base é criada com HTTP 201
