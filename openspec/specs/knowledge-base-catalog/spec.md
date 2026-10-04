# knowledge-base-catalog Specification

## Purpose

Cobre o **cadastro da base de conhecimento em `apps/api`**: criar, listar,
consultar por id, editar, ativar, desativar e excluir. A base é o *recipiente* — nome,
descrição e estado de ativação —, nunca o conteúdo que mora dentro dela.

A capability existe por uma pergunta de operação, não por CRUD: **o agente deve
consultar esta base quando o cliente perguntar isso?** Duas decisões saem daí e
são o que esta capability afirma de mais próprio:

- **A descrição é obrigatória**, ao contrário da de `McpServer`. Ela não é texto
  decorativo: a partir da etapa de execução é o texto que o modelo lê para
  decidir se a base é relevante para a pergunta. Base sem descrição seria uma
  ferramenta que o modelo não sabe quando chamar.
- **Desativar não é excluir.** Base é entidade de catálogo com vínculos de agente
  apontando para ela; desativar impede o *uso pelo agente* e preserva tudo — os
  documentos ficam intactos. **Desde a #108 a base tem exclusão real**, a exceção à
  regra "catálogo só se desativa" de `mcp-server-catalog`: ninguém lê o passado
  pelo id da base, e uma base sincronizada retém a pasta, que sem exclusão ficaria
  presa para sempre. A exclusão exige a base **inativa** e leva junto documentos,
  fragmentos, eventos de histórico e vínculos; as métricas ficam.

O que a distingue das vizinhas, que é onde a confusão nasce:

- `knowledge-document-catalog` é dona do **conteúdo** dentro da base — os
  documentos, o teto de tamanho, o ciclo de vida de indexação de cada um.
- `knowledge-document-indexing` é dona do que **transforma** esse conteúdo em
  fragmentos consultáveis, e do estado agregado de indexação por base. Esta
  capability afirma explicitamente que a resposta de base **não** carrega essa
  contagem: seis sítios a constroem, quatro deles operações de escrita sem
  relação nenhuma com indexação, e encarecer o catálogo inteiro serviria uma
  única tela.
- `agent-knowledge-binding` é dona de **qual agente consulta qual base**. Esta
  capability nunca sabe quem a consome.
- `knowledge-base-catalog-ui` é a contraparte de tela desta, em
  `apps/frontend`.

## Requirements

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

### Requirement: Listagem de bases de conhecimento
O sistema SHALL permitir, via `apps/api`, listar todas as bases cadastradas,
incluindo as inativas.

#### Scenario: Lista retorna todas as bases cadastradas
- **WHEN** um cliente envia `GET /knowledge-bases` e existem bases cadastradas
- **THEN** a API responde HTTP 200 com a lista, incluindo id, nome, descrição e
  `isActive` de cada uma

#### Scenario: Lista inclui bases inativas
- **WHEN** um cliente envia `GET /knowledge-bases` e existe pelo menos uma base
  desativada
- **THEN** a API inclui essa base na resposta normalmente, com
  `isActive: false`

#### Scenario: Lista sem nenhuma base cadastrada
- **WHEN** um cliente envia `GET /knowledge-bases` e não existe nenhuma base
- **THEN** a API responde HTTP 200 com uma lista vazia, nunca HTTP 404

### Requirement: Consulta de base de conhecimento por id
O sistema SHALL permitir, via `apps/api`, consultar uma base específica pelo seu
identificador, incluindo bases inativas.

#### Scenario: Consulta de base existente retorna dados completos
- **WHEN** um cliente envia `GET /knowledge-bases/{id}` para um id existente
- **THEN** a API responde HTTP 200 com id, nome, descrição e `isActive`

#### Scenario: Consulta de base inexistente retorna 404
- **WHEN** um cliente envia `GET /knowledge-bases/{id}` para um id que não
  existe
- **THEN** a API responde HTTP 404

#### Scenario: Consulta de base inativa retorna normalmente
- **WHEN** um cliente envia `GET /knowledge-bases/{id}` para uma base desativada
- **THEN** a API responde HTTP 200 com `isActive: false`, sem tratar isso como
  não encontrado

### Requirement: Edição de base de conhecimento
O sistema SHALL permitir, via `apps/api`, alterar nome e descrição de uma base
existente, com as mesmas validações do cadastro.

#### Scenario: Editar nome e descrição de base existente
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` com nome e descrição não
  vazios, para um id existente
- **THEN** a API responde HTTP 200 com a base atualizada refletindo os novos
  valores

#### Scenario: Editar base inexistente retorna 404
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` para um id que não existe
- **THEN** a API responde HTTP 404 e nenhum registro é alterado

#### Scenario: Editar base com descrição vazia é rejeitado
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` com descrição vazia ou
  só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e a base
  permanece com os valores anteriores

### Requirement: Ativação e desativação de base de conhecimento
O sistema SHALL permitir ativar e desativar uma base, sem excluí-la. Desativar NÃO
SHALL apagar nada: a base continua existindo, com seus documentos intactos. A
exclusão é uma operação separada, que exige a base inativa (requisito "Exclusão de
base de conhecimento inativa").

#### Scenario: Desativar base ativa
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/deactivate` para uma
  base ativa
- **THEN** a API responde HTTP 200 com `isActive: false` e a base continua
  existindo, com seus documentos intactos

#### Scenario: Ativar base inativa
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/activate` para uma base
  desativada
- **THEN** a API responde HTTP 200 com `isActive: true`

#### Scenario: Desativar base já inativa é idempotente
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/deactivate` para uma
  base que já está inativa
- **THEN** a API responde HTTP 200 com `isActive: false`, sem erro

#### Scenario: Ativar ou desativar base inexistente retorna 404
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/activate` ou
  `POST /knowledge-bases/{id}/deactivate` para um id que não existe
- **THEN** a API responde HTTP 404

### Requirement: A resposta de base não carrega contagem agregada
A resposta de base de conhecimento NÃO SHALL carregar contagem de documentos
nem contagem de estado de indexação — nem em `GET /knowledge-bases`, nem em
`GET /knowledge-bases/{id}`, nem nas respostas de criação, atualização,
ativação e desativação. O estado de indexação agregado vive em recurso próprio,
`GET /knowledge-bases/indexing-summary`.

Isto é requisito, e não detalhe de implementação, porque é exatamente a coisa
que alguém "corrige" acrescentando um campo sem ver a causa. As razões, todas
verificáveis no código:

- A resposta é construída em **seis** lugares, e **quatro** deles são operações
  de escrita — criar, atualizar, ativar e desativar uma base. Nenhuma delas tem
  relação com indexação, e todas passariam a arcar com uma agregação sobre
  documentos para montar a própria resposta.
- A alternativa a essa agregação seria devolver zero nessas quatro, o que seria
  **falso** em atualização, ativação e desativação — a interface afirmando uma
  contagem que ninguém fez.
- `GET /knowledge-bases` tem consumidores que nunca olham contagem alguma,
  inclusive as telas de vínculo entre agente e base. Encarecer o catálogo para
  todos eles serve uma única tela.

O catálogo SHALL, portanto, permanecer uma consulta simples sobre as bases, sem
junção com documentos.

#### Scenario: Listagem de bases não devolve contagem de documentos
- **WHEN** um cliente envia `GET /knowledge-bases` e as bases têm documentos em
  estados variados
- **THEN** a resposta traz apenas os campos da própria base — id, nome,
  descrição, estado de ativação e datas — e nenhum campo de contagem

#### Scenario: Criar uma base não afirma contagem nenhuma
- **WHEN** um cliente cria uma base de conhecimento
- **THEN** a resposta da criação não traz campo de contagem de documentos nem de
  estado de indexação

### Requirement: Autenticação das rotas de base de conhecimento
Todas as rotas de base de conhecimento SHALL exigir token de operador válido.
Nenhuma delas SHALL ser adicionada à allowlist de rotas anônimas.

#### Scenario: Requisição sem token é rejeitada
- **WHEN** um cliente envia qualquer requisição para `/knowledge-bases` sem
  cabeçalho de autorização
- **THEN** a API responde HTTP 401 e nenhuma operação é executada

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

### Requirement: Exclusão de base de conhecimento inativa
`DELETE /knowledge-bases/{id}` SHALL excluir a base, de forma atômica, junto com
tudo que é dela: os documentos, os fragmentos desses documentos, os eventos de
histórico da base e os vínculos de agentes com a base. A exclusão SHALL valer para
base `Manual` e `Synced`, e SHALL responder `204` sem corpo.

A exclusão SHALL exigir a base **inativa**. Base ativa SHALL ser recusada com `409`
em `ProblemDetails`, com a extensão `code` igual a `knowledge-base-active` e um
`detail` que diz para desativar a base antes; nesse caso NADA SHALL ser apagado. A
conferência de `IsActive` SHALL acontecer sob o bloqueio da linha da base, na mesma
transação que apaga.

Id inexistente SHALL responder `404`. A exclusão NÃO SHALL tocar documentos,
fragmentos, eventos ou vínculos de outra base. As métricas de indexação e de
embedding NÃO SHALL ser apagadas.

A rota é do operador: o subject `service:connectors` SHALL receber `403`.

#### Scenario: Excluir base inativa com conteúdo
- **WHEN** um cliente envia `DELETE /knowledge-bases/{id}` para uma base inativa
  com documentos indexados, eventos de histórico e um agente vinculado
- **THEN** a API responde HTTP 204
- **AND** `GET /knowledge-bases/{id}` responde 404
- **AND** não resta linha da base em `knowledge_documents`, `knowledge_fragments`,
  `knowledge_document_events` nem `agent_knowledge_bases`
- **AND** o agente continua existindo, sem a base entre as vinculadas

#### Scenario: Base ativa é recusada sem apagar nada
- **WHEN** um cliente envia `DELETE /knowledge-bases/{id}` para uma base ativa com
  documentos, fragmentos, eventos e um agente vinculado
- **THEN** a API responde HTTP 409 com `code: "knowledge-base-active"`
- **AND** a base, os documentos, os fragmentos, os eventos e o vínculo continuam
  existindo, nas mesmas contagens de antes

#### Scenario: Base inexistente
- **WHEN** um cliente envia `DELETE /knowledge-bases/{id}` para um id que não existe
- **THEN** a API responde HTTP 404

#### Scenario: Outra base não é tocada
- **WHEN** duas bases inativas têm documentos, fragmentos, eventos e vínculos, e
  uma delas é excluída
- **THEN** a outra mantém todos os documentos, fragmentos, eventos e vínculos, nas
  mesmas contagens de antes

#### Scenario: Métricas de indexação sobrevivem à base
- **WHEN** uma base com tentativas de indexação registradas é excluída
- **THEN** as linhas de `knowledge_indexing_attempts` daquela base continuam
  existindo

#### Scenario: A pasta de uma base sincronizada excluída fica livre
- **WHEN** uma base `Synced` inativa usa uma pasta, o cadastro de outra base com a
  mesma pasta responde 409 `folder-in-use`, e a base é excluída
- **THEN** o mesmo cadastro, repetido, responde HTTP 201 e a base nova usa a pasta

#### Scenario: O bloqueio vem antes de apagar os documentos
- **WHEN** a exclusão de uma base inativa é executada
- **THEN** o SQL emitido na requisição trava a linha da base com `FOR UPDATE` antes
  do `DELETE` dos documentos, na mesma transação

#### Scenario: Subject de serviço não exclui base
- **WHEN** o subject `service:connectors` envia `DELETE /knowledge-bases/{id}`
- **THEN** a API responde HTTP 403 e a base continua existindo
