## MODIFIED Requirements

### Requirement: Cadastro de base de conhecimento
O sistema SHALL permitir, via `apps/api`, cadastrar uma base de conhecimento
informando nome e descrição. A base SHALL nascer ativa (`isActive: true`) e
receber um identificador único gerado pelo sistema.

A descrição NÃO é texto decorativo: a partir da etapa de execução ela é o texto
que o modelo lê para decidir se a base é relevante para a pergunta. Por isso ela
SHALL ser obrigatória e não vazia.

O cadastro SHALL aceitar `contentMode` opcional. Omitido ou `Manual`, a base SHALL
nascer `Manual`. `Synced` SHALL ser recusado com `400` em `contentMode`, porque a
base sincronizada é criada com a pasta validada pelo app que acessa o provedor, e
essa rota ainda não existe. Valor desconhecido SHALL ser recusado com `400`.

#### Scenario: Criar base com nome e descrição
- **WHEN** um cliente envia `POST /knowledge-bases` com nome e descrição não
  vazios
- **THEN** a API responde HTTP 201 com a base criada, incluindo id, nome,
  descrição e `isActive: true`

#### Scenario: Criar base sem nome é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases` sem nome, ou com nome
  vazio ou só de espaços
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

#### Scenario: Base sincronizada não é criada por esta rota
- **WHEN** um cliente envia `POST /knowledge-bases` com `contentMode: "Synced"`
- **THEN** a API responde HTTP 400 em `contentMode` e não cria nenhum registro

## ADDED Requirements

### Requirement: Tipo de conteúdo e origem da base
Toda base SHALL ter `contentMode`, `Manual` ou `Synced`. Base `Synced` SHALL ter
provedor, id da pasta, nome e URL da pasta, todos não nulos. Base `Manual` NÃO
SHALL ter nenhum desses campos nem estado de sincronização. O banco SHALL garantir
essas combinações.

O provedor SHALL ser string aberta, sem lista fechada no `apps/api`.

#### Scenario: Base sincronizada sem pasta é recusada pelo banco
- **WHEN** uma base `Synced` sem id da pasta é inserida direto no banco
- **THEN** o banco recusa a inserção

#### Scenario: Base manual com pasta é recusada pelo banco
- **WHEN** uma base `Manual` com provedor e id da pasta é inserida direto no banco
- **THEN** o banco recusa a inserção

### Requirement: Tipo, provedor e pasta são imutáveis
`contentMode`, provedor e id da pasta NÃO SHALL mudar depois do cadastro, por
nenhuma rota. Nome e URL da pasta NÃO SHALL mudar pela rota do operador; só a
gravação de um ciclo de sincronização bem-sucedido os atualiza.

#### Scenario: PUT com tipo, provedor e pasta diferentes não altera os valores gravados
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` para uma base `Synced` com
  nome e descrição válidos e também `contentMode: "Manual"`, outro `provider`, outro
  `folderId`, outro `folderName` e outra `folderUrl`
- **THEN** a API responde HTTP 200 com o nome e a descrição novos, e o tipo, o
  provedor, a pasta, o nome e a URL da pasta lidos do banco continuam os anteriores

#### Scenario: PUT com tipo diferente em base manual não a torna sincronizada
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` para uma base `Manual` com
  `contentMode: "Synced"` e um `folderId`
- **THEN** a base continua `Manual`, sem provedor nem pasta

#### Scenario: Edição de nome e descrição de base sincronizada é permitida
- **WHEN** um cliente edita nome e descrição de uma base `Synced`
- **THEN** a API responde HTTP 200 com os valores novos

#### Scenario: Ativar e desativar base sincronizada é permitido
- **WHEN** um cliente desativa e depois ativa uma base `Synced`
- **THEN** as duas operações respondem HTTP 200 e a origem e o estado da
  sincronização continuam os mesmos

### Requirement: Uma pasta pertence a uma base só
O banco SHALL garantir que não existem duas bases `Synced` com o mesmo provedor e
o mesmo id de pasta, **inclusive quando uma delas está inativa**. O id da pasta
SHALL ser comparado como veio, sem normalizar caixa.

#### Scenario: Pasta já usada por base ativa
- **WHEN** uma segunda base `Synced` com o mesmo provedor e o mesmo id de pasta é
  inserida direto no banco
- **THEN** o banco recusa a inserção por violação de unicidade

#### Scenario: Pasta já usada por base inativa
- **WHEN** a base que usa a pasta está inativa e uma segunda base com a mesma pasta
  é inserida direto no banco
- **THEN** o banco recusa a inserção por violação de unicidade

#### Scenario: Ids que diferem só na caixa são pastas diferentes
- **WHEN** duas bases `Synced` do mesmo provedor com ids `AbC` e `abc` são
  inseridas
- **THEN** as duas são gravadas

#### Scenario: Corrida no índice da pasta
- **WHEN** duas conexões inserem ao mesmo tempo uma base `Synced` com o mesmo
  provedor e o mesmo id de pasta
- **THEN** exatamente uma inserção é gravada e a outra recebe violação de
  unicidade

### Requirement: Estado da sincronização na resposta da base
A resposta de base SHALL trazer, na listagem, na consulta por id e nas respostas
de criação, edição, ativação e desativação, `contentMode` como string, `syncSource` e
`syncState`. Em base `Manual`, `syncSource` e `syncState` SHALL ser `null`. Em
base `Synced`, `syncSource` SHALL trazer `provider`, `folderId`, `folderName` e
`folderUrl`, e `syncState` SHALL trazer `lastCompletedAt`, `lastFinishedAt`,
`failingSince`, `lastError` (`code` e `detail`, ou `null`) e `ignoredFiles`
(lista de `externalRef`, `name`, `code` e `detail`). Os códigos SHALL sair como
foram gravados, sem tradução.

`ignoredFiles` SHALL ser `null` enquanto nenhum ciclo bem-sucedido foi gravado, e
lista (possivelmente vazia) depois do primeiro.

#### Scenario: Formato de fio verificado sobre o texto da resposta
- **WHEN** um cliente consulta, por `GET /knowledge-bases/{id}`, uma base `Synced`
  com um erro de sincronização e um arquivo ignorado gravados
- **THEN** o texto JSON da resposta HTTP traz as chaves `contentMode`, `syncSource`,
  `syncState`, `lastCompletedAt`, `lastFinishedAt`, `failingSince`, `lastError`,
  `ignoredFiles`, `externalRef`, `folderId` em camelCase, `contentMode` como a
  string `"Synced"`, e os códigos como as strings gravadas, sem nenhuma chave em
  PascalCase

#### Scenario: Base sincronizada nunca sincronizada
- **WHEN** um cliente consulta uma base `Synced` que nunca recebeu resultado de
  ciclo
- **THEN** `syncState` traz os três instantes nulos, `lastError: null` e
  `ignoredFiles: null`

#### Scenario: Base manual na listagem
- **WHEN** um cliente envia `GET /knowledge-bases` e existe uma base `Manual`
- **THEN** o item dessa base traz `contentMode: "Manual"`, `syncSource: null` e
  `syncState: null`

#### Scenario: Base sincronizada inativa com falha aparece na listagem
- **WHEN** existe uma base `Synced` inativa com sincronização falhando
- **THEN** `GET /knowledge-bases` a traz com `isActive: false` e `failingSince`
  preenchido

### Requirement: Bases existentes viram manuais na migração
A migração que introduz o tipo de conteúdo SHALL deixar toda base existente como
`Manual`, sem origem e sem estado de sincronização, e NÃO SHALL alterar nenhum
outro campo das bases nem dos documentos existentes.

#### Scenario: Base e documentos anteriores à migração
- **WHEN** a migração roda sobre um banco com uma base ativa, uma inativa e
  documentos
- **THEN** as duas bases ficam `Manual`, com nome, descrição, `isActive`,
  `createdAt` e `updatedAt` iguais aos anteriores, os documentos ficam com
  `ExternalRef` nulo e com o resto igual, e nenhum evento de histórico é criado
