## Context

O `apps/api` é dono das bases de conhecimento e dos documentos. Toda escrita de
documento passa pelos handlers de `KnowledgeDocuments/`, que normalizam o texto
(`KnowledgeContentProcessor`), decidem a indexação pelo `ContentHash`, contam a
revisão por `ContentRevision` e gravam o evento do histórico (#98) no mesmo
`SaveChangesAsync`. A base hoje tem só `Name`, `Description` e `IsActive`.

A linha do Google Drive acrescenta um app novo, `apps/connectors` (#103), que vai
ler pastas e escrever documentos **pelo `apps/api`**, nunca no banco. Esta change
prepara o `apps/api` para isso e nada mais. A etapa 0 (#100) mediu o Drive e mudou
duas coisas que o corpo da #102 dizia (ver "Divergências resolvidas", abaixo).

O que o código mostrou e muda decisões, lido antes de propor:

- **Hoje qualquer subject que não seja `service:inbox` tem o acesso do
  operador.** `ServiceScopeAuthorizationHandler` (`Auth/ServiceScopeAuthorizationHandler.cs:29-34`)
  faz `if (subject != ServiceSubject) Succeed`. Um token com
  `sub: "service:connectors"`, assinado com a chave compartilhada, passaria em
  todas as rotas. A D6 inverte isso.
- **O token de serviço não é emitido pelo `apps/api`.** O `apps/inbox` assina o
  próprio token a cada requisição com a chave compartilhada
  (`apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs:24`, TTL
  fixo de 5 min). O `01` (`01-ARQUITETURA_E_CONVENCOES.md:405`) e o
  `docs/architecture.md` (linha 585) dizem "`apps/api` emite (login do operador e
  token de serviço)", o que não é o que o código faz. **Corrigido nesta change**,
  por decisão do mantenedor em 03/10/2026, nas tarefas 8.1 e 8.2, que já editam as
  duas seções; sem issue própria.
- **O `apps/inbox` tem o defeito da D6 numa forma mais larga, fora do escopo
  desta change.** A `FallbackPolicy` dele é só `RequireAuthenticatedUser()`
  (`apps/inbox/src/Buteco.Inbox/Program.cs:112-113`), sem requisito de subject:
  qualquer token validamente assinado, com qualquer `sub`, tem acesso às rotas do
  operador, inclusive o `service:inbox` que o próprio app assina. Lido no código,
  não executado. Registrado na **#116**; esta change não toca o `apps/inbox`.
- **Nenhuma rota do `apps/api` devolve `409` hoje.** `grep Conflict` em
  `apps/api/src` não acha nada. O `409` desta change é o primeiro, e usa
  `TypedResults.Problem(statusCode: 409)`, a forma que
  `AgentMcpBindingEndpoints.cs:68` já usa para o `502`.
- **O idioma de corrida da casa** é índice único + `catch (DbUpdateException)`
  com `PostgresException { SqlState: UniqueViolation }` + detach + uma releitura,
  em `apps/inbox` (`ContactSessionResolver.cs:24,130`). O `apps/api` não tem
  nenhum caso ainda.
- **O `apps/workers` mapeia `knowledge_bases` e `knowledge_documents`** no próprio
  `AppDbContext` (`apps/workers/.../Infrastructure/AppDbContext.cs:139,150`), sem
  as colunas novas, e só atualiza colunas de indexação de documento. Coluna nova
  anulável ou com default não o afeta.

### Divergências resolvidas

| ponto | o que divergia | como fica |
|---|---|---|
| `ExternalVersion` | o `02` (seção da etapa 0, "O que a #102 herda") diz "hash de conteúdo, não campo do Drive"; o comentário da etapa 0 na #102, posterior, diz "marcador do provedor, string opaca: `modifiedTime` para Doc, `md5Checksum` para `.md`", e descarta o hash calculado pelo conector | **vale o comentário** (D3). O `02` fica desatualizado nesse ponto e é corrigido na tarefa de documentação |
| autor das escritas do subject | o corpo da #102 diz autor "sincronização" | **`service:connectors`**, o subject verbatim, pela D6 da #98; "sincronização" é rótulo do frontend (#101/#107) |
| snapshots de nome e URL da pasta | o corpo da #102 diz "todos imutáveis"; a #105 diz "nome e URL da pasta são atualizados a cada ciclo", e a #107 rotula o nome em falha como "nome na última sincronização concluída" | provedor e id da pasta são imutáveis sempre. Nome e URL são imutáveis **para o operador** e só a gravação de um ciclo **bem-sucedido** os atualiza (D8). Sem isso, o rótulo da #107 não teria significado |
| reconciliação | a #105 compara "por `ExternalRef` e `ExternalVersion`", e a #102 descarta guardar o mapeamento no app de sincronização | o conector precisa ler as referências e versões que o `apps/api` tem. Esta change acrescenta a rota de leitura (D9), que não estava na lista da issue |

## Goals / Non-Goals

**Goals:**

- Base com tipo, origem e estado da sincronização, imutáveis onde precisam ser, e
  com o estado na resposta da base.
- `ExternalRef` e `ExternalVersion` no documento, com o vínculo ao tipo da base
  garantido pelo banco.
- Escrita do operador recusada em base `Synced`, sem afetar o que continua
  liberado.
- Subject `service:connectors` com escopo próprio, rotas de serviço idempotentes e
  seguras sob corrida, e escritas no histórico com autor próprio.
- Subject desconhecido recusado por padrão.

**Non-Goals:**

- Criar base `Synced` por rota (#104), conector e ciclo (#103, #105), telas
  (#107), exclusão de base (#108).
- Traduzir código de erro em texto.
- Qualquer mudança em `apps/workers`, `apps/inbox` ou `apps/frontend`.

## Decisions

### D1. Motivos como código estável, com detalhe opcional, e nenhum texto no `apps/api`

O último erro e o motivo de cada arquivo ignorado são gravados como **código**
(`access-denied`, `api-not-configured`, `unsupported-type`, `too-large`,
`download-blocked`, `export-failed`, ...) mais um **detalhe** opcional (por
exemplo o e-mail da conta no `access-denied`). O texto exibido é do frontend
(#107), no raciocínio da D6 da #98: rótulo é apresentação, e trocar o texto não
pode virar migração de dados.

O `apps/api` aceita o código como **string aberta**: não há enum nem lista
fechada, porque o conjunto pertence ao `apps/connectors` e cresce com cada
conector (mesmo raciocínio de `SourceType` e `ChannelType`). Mas aceita só a
**forma** de código: `^[a-z0-9]+(-[a-z0-9]+)*$`, até 64 caracteres. Uma frase
("Sem acesso à pasta") é recusada com `400`.

**Achado da implementação, sem mudar a decisão:** a regex vai com `\z` no fim, não
`$`. Em .NET o `$` casa antes de um `\n` final, e `"access-denied\n"` passaria
(`SyncCode`, caso de teste próprio).

**Onde mora:**

- último erro: duas colunas em `knowledge_bases`, `LastSyncErrorCode` e
  `LastSyncErrorDetail`;
- arquivos ignorados: uma coluna `jsonb`, `SyncIgnoredFiles`, com a lista
  `[{ "externalRef", "name", "code", "detail" }]`. `externalRef` e `name` são
  obrigatórios (o nome é o que a tela mostra; a referência é o que liga o item ao
  arquivo), `detail` é anulável.

O `jsonb` segue o molde de `Agent.Skills`: `HasConversion` com `JsonSerializer` e
`ValueComparer` explícito. É a forma que o `01` classifica como imune ao aliasing
do change tracker (serializa o valor inteiro a cada `SaveChanges`), ao contrário
de `OwnsMany().ToJson()`.

- *Descartado, guardar a frase pronta:* o texto viraria dado, e o frontend
  perderia a decisão sobre a mensagem (a #107 já pede textos diferentes para
  `api-not-configured` e `access-denied`, que voltam com o mesmo `403` do Drive).
- *Descartado, enum fechado no `apps/api`:* cada motivo novo de um conector
  exigiria mudar o `apps/api`, o mesmo argumento que a issue usa para o provedor.
- *Descartado, aceitar qualquer string:* deixaria passar a frase, e o frontend
  receberia um código que ele não reconhece e que, renderizado com rótulo neutro
  (convenção 13), esconderia a informação.
- *Descartado, tabela própria para os ignorados:* a lista é sempre substituída
  inteira a cada ciclo bem-sucedido e sempre lida com a base; nenhuma consulta
  filtra por item. Tabela custaria FK, cascata e uma junção na listagem de bases
  para nada.

### D2. "Falhando desde" é derivado da transição, e o banco garante a forma

O resultado de um ciclo é gravado por uma rota só (D8), com dois desfechos:

| desfecho | `LastSyncCompletedAt` | `LastSyncFinishedAt` | `LastSyncError*` | `SyncFailingSince` | `SyncIgnoredFiles` | nome e URL da pasta |
|---|---|---|---|---|---|---|
| sucesso | agora | agora | nulo | nulo | a lista enviada | os enviados |
| falha | **preservado** | agora | o enviado | **o anterior, ou agora se era nulo** | **preservado** | **preservados** |

- "Falhando desde" nasce na primeira falha depois de um sucesso (ou de nunca ter
  sincronizado), é mantido nas falhas seguintes e é limpo no próximo sucesso.
- A falha **não toca** a última concluída. É o teste negativo pedido: registrar
  uma falha e afirmar que `lastCompletedAt` continua igual ao valor anterior.
- A falha também não toca a lista de ignorados: ela descreve a última listagem
  feita, e uma falha de pasta não listou nada.
- **`LastSyncFinishedAt` é decisão que não estava na issue.** Sem ela, uma
  segunda falha com o mesmo código não muda nenhum campo da resposta, e o polling
  condicional da #107 (convenção 20) não teria como saber que o ciclo pedido por
  "Sincronizar agora" terminou.

O instante é o relógio do `apps/api` (`TimeProvider`) no momento da gravação, e
não um campo enviado pelo conector.

**O banco garante a forma** com `CHECK`s na mesma tabela, que cabem numa `CHECK`
porque não cruzam tabela:

- `("SyncFailingSince" IS NULL) = ("LastSyncErrorCode" IS NULL)`: está falhando
  se e somente se há último erro;
- `"LastSyncErrorDetail" IS NULL OR "LastSyncErrorCode" IS NOT NULL`;
- base `Manual` tem todas as colunas de sincronização nulas (D4).

- *Descartado, o conector enviar `failingSince` e `completedAt`:* dois relógios, e
  uma gravação atrasada fora de ordem poderia mover o estado para trás. E a regra
  de "falhando desde" passaria a ser do conector, com o `apps/api` só copiando.
- *Descartado, uma flag "sincronizando agora" no `apps/api`:* é estado de um
  processo do `apps/connectors`; se o processo cair no meio, a flag fica presa.
- *Descartado, um `PUT` do estado inteiro:* o conector teria de ler o estado
  antes para preservar a última concluída, e a regra mais importante desta seção
  dependeria de o cliente acertar.

### D3. `ExternalVersion` é opaco, e quem decide se o conteúdo mudou continua sendo o `apps/api`

`ExternalVersion` é o marcador de mudança do provedor: `modifiedTime` para Google
Doc, `md5Checksum` para `.md` (comentário da etapa 0 na #102). O `apps/api` grava
como veio e **nunca o compara, nunca o interpreta e nunca decide nada por ele**.
Serve ao conector, que o lê de volta pela rota da D9 para não reexportar o que não
mudou.

Quem decide se o documento mudou é a regra que já existe: `ContentHash` para
indexar, `ContentRevision` e título para o evento (D4 da #98). No upsert:

| texto | título | efeito |
|---|---|---|
| igual | igual | **sem efeito**: só `ExternalVersion` é gravado. Sem evento, sem indexação e **sem tocar `UpdatedAt`** |
| igual | diferente | evento `Updated` com `titleChanged`, sem indexação (regra do hash) |
| diferente | qualquer | evento `Updated` com `contentChanged`, `ContentRevision` + 1, indexação |

"Texto igual" é o texto **já normalizado** pelo `KnowledgeContentProcessor` (BOM e
`CRLF`), que é a razão pela qual o hash calculado pelo conector foi descartado na
etapa 0: seria uma segunda regra de "mudou", divergente desta.

**Não tocar `UpdatedAt` no caso sem efeito é decisão.** Hoje
`KnowledgeDocument.Update` atualiza `UpdatedAt` sempre, inclusive no `PUT` do
operador com payload idêntico. No upsert, renomear ou recompartilhar um Google Doc
muda o `modifiedTime` com o markdown idêntico (P6 da etapa 0): atualizar
`UpdatedAt` ali faria a tela afirmar "atualizado às 14:02" sobre algo que não
mudou (convenção 13). O caminho do operador não muda.

A entidade ganha um método próprio para isso, `ApplyExternalRevision(title,
sourceType, extractedText, externalVersion)`, que compara antes e só delega a
`Update` quando há mudança. `SourceType` diferente com texto e título iguais é
gravado sem evento, como no `PUT` do operador.

- *Descartado, guardar o hash do conteúdo calculado pelo conector:* descartado na
  etapa 0, pelo motivo acima.
- *Descartado, não gravar o marcador no caso sem efeito:* o conector veria o
  marcador antigo no ciclo seguinte e reexportaria o mesmo Doc a cada 5 minutos,
  para sempre, gastando cota de download (P5 da etapa 0).
- *Descartado, reaproveitar `Update` e aceitar o `UpdatedAt`:* pelo motivo acima.

### D4. O vínculo entre `ExternalRef` e o tipo da base vive no banco, por uma FK composta

"`ExternalRef` preenchido se e somente se a base é `Synced`" cruza duas tabelas e
não cabe numa `CHECK`. A regra fica em **dois lugares, com papéis diferentes**:

1. **No banco, como garantia.** O documento ganha `KnowledgeBaseContentMode`, uma
   cópia do tipo da base. A FK de documento para base passa a ser **composta**:
   `(KnowledgeBaseId, KnowledgeBaseContentMode)` → `knowledge_bases ("Id",
   "ContentMode")`, que exige uma chave alternativa única `("Id", "ContentMode")`
   na base. Com a FK garantindo que a cópia é igual ao tipo da base, a regra vira
   uma `CHECK` **na tabela do documento**:
   `("KnowledgeBaseContentMode" = 'Synced') = ("ExternalRef" IS NOT NULL)`, e
   `("ExternalRef" IS NULL) = ("ExternalVersion" IS NULL)`.
2. **Na aplicação, como resposta.** Os handlers recusam antes de chegar ao banco:
   `409` para o operador em base `Synced`, `409` para o subject de serviço em base
   `Manual`, `400` para upsert sem `ExternalRef`. O banco nunca é o caminho normal
   de recusa; é o que impede um handler futuro de esquecer a regra.

**A cópia não envelhece porque o tipo é imutável**, e a mesma estrutura reforça a
imutabilidade: um `UPDATE` do `ContentMode` de uma base com documentos viola a FK
(ação padrão `NO ACTION`), e no EF a propriedade passa a fazer parte de uma chave
alternativa, que o change tracker se recusa a modificar.

A FK composta **substitui** a FK simples atual, com o mesmo `Restrict`. Ela implica
a existência da base, então a simples ficaria redundante.

Os dois sentidos são provados por inserção direta no banco: documento com
`ExternalRef` em base `Manual` e documento sem `ExternalRef` em base `Synced` são
recusados pela `CHECK`, e um documento com a cópia divergente do tipo da base é
recusado pela FK.

**Verificado antes do código (tarefa 1.1), e dois detalhes de forma que não mudam a
decisão:** o EF cria sozinho um índice `("KnowledgeBaseId", "KnowledgeBaseContentMode")`
para sustentar a FK composta, ao lado do índice simples de `KnowledgeBaseId` que já
existia; e o enum com `HasDefaultValue(Manual)` precisa de `HasSentinel` fora do
domínio, senão o EF omite `Manual` no `INSERT` por ser o default do CLR.

**E a FK composta segurou um defeito de verdade no guarda (g2) da tarefa 6.13:** sem
a recusa do handler de cadastro, o documento de base manual numa base `Synced` foi
recusado pelo banco (`500`, nada gravado). A recusa do handler continua sendo a
resposta (`409`); o banco é o que impede o estado inválido quando ela some.

- *Descartado, só na aplicação:* qualquer handler novo que esquecesse a regra
  gravaria o estado inválido em silêncio, e não haveria como provar a regra no
  banco. É o raciocínio da D5 da #98 (formato garantido pelo banco).
- *Descartado, trigger:* seria o primeiro do repositório, procedural, invisível no
  modelo do EF e no snapshot, e só verificável lendo SQL.
- *Descartado, deixar `ExternalRef` sem vínculo e confiar no subject:* o operador
  não envia `ExternalRef`, mas o subject de serviço poderia gravar um em base
  `Manual` por defeito do handler.

### D5. Unicidade da pasta: índice único parcial, provado por inserção direta

Índice único em `knowledge_bases ("SyncProvider", "SyncFolderId") WHERE
"ContentMode" = 'Synced'`. Sem filtro por `IsActive`: base inativa continua
sendo sincronizada (#105) e ocupa a pasta. O id é comparado **como veio**: a
collation padrão do banco é determinística, então `'AbC'` e `'abc'` são valores
distintos, e o teste afirma que as duas bases coexistem (a asserção negativa da
normalização).

Nesta change nenhuma rota cria base `Synced`, então o índice é provado por
inserção direta: duas inserções da mesma pasta (uma ativa e uma inativa) e a
corrida de duas conexões inserindo a mesma pasta ao mesmo tempo, com exatamente uma
vencedora e a outra recebendo `23505`.

**Registrado para a #104**, que é quem vai criar base `Synced` por rota:

- a violação do índice vira `409`, nunca `500`, com o **nome da base que já usa a
  pasta**, pelo idioma da casa (captura de `UniqueViolation`, detach, releitura da
  base vencedora pelo provedor e pela pasta);
- **enquanto a #108 não existir, a mensagem NÃO pode mandar excluir a base**: não
  existe rota para isso. O que ela pode dizer é qual base usa a pasta e que ela
  continua ocupada mesmo inativa.

- *Descartado, unicidade só entre bases ativas:* a #108 já registra por quê — duas
  bases acompanhariam a mesma pasta, com conteúdo e embedding duplicados.
- *Descartado, normalizar a caixa do id:* ids do Drive diferenciam maiúsculas, e
  normalizar fundiria pastas diferentes.

### D6. `service:connectors` segue o molde do `service:inbox`, e subject desconhecido passa a ser recusado

**Emissão e validação, como hoje:** token HMAC sem biblioteca, mesma chave
`Auth:TokenSigningKey`, mesmo esquema `OperatorTokenAuthenticationHandler`, que
põe o subject em `ClaimTypes.NameIdentifier`. Nada muda na emissão nem na
validação.

**Como o `apps/connectors` (#103) vai obter o token:** do mesmo jeito que o
`apps/inbox`. Ele recebe a mesma `Auth__TokenSigningKey` no compose e assina um
token novo a cada requisição de saída, com `sub: "service:connectors"` e TTL fixo
curto, num `DelegatingHandler` próprio, duplicado no app e sem `libs/`, como o
`TokenService` já é duplicado em `apps/inbox`. Esta change não cria nada fora do
`apps/api`; o registro fica aqui e no `docs/architecture.md` para a #103.

**O que muda, no `ServiceScopeAuthorizationHandler`:**

- a regra deixa de ser "quem não é `service:inbox` passa" e vira uma tabela
  explícita: `operator` passa em tudo; cada subject de serviço conhecido passa só
  nas rotas da sua lista; **qualquer outro subject recebe `403`**;
- `service:connectors` tem a lista das cinco rotas de `/sync/knowledge-bases`
  (D8), e nenhuma outra. Ele recebe `403` em `GET /knowledge-bases`, inclusive;
- `service:inbox` continua com as duas rotas que tem, e recebe `403` nas rotas de
  `/sync`;
- a constante `ServiceSubject` vira `InboxSubject`, ao lado de
  `ConnectorsSubject` e `OperatorSubject`; `AuthEndpoints` passa a emitir o token
  do operador pela constante, e não pelo literal.

**Checagem no boot (convenção 8):** cada par (método, padrão) das listas de
serviço precisa existir entre os endpoints mapeados; uma entrada sem rota derruba
o boot. A lista casa por `RoutePattern.RawText`, e um padrão renomeado deixaria o
subject recebendo `403` em produção sem nenhum teste de boot reprovar. Mesma forma
de `ValidateRouteAuthenticationClassification` (sobre o host construído), com um
teste da extensão contra uma lista com entrada inexistente e um teste da
composição real.

**A checagem pagou o custo na primeira execução, e o padrão da lista mudou por
isso.** A lista dizia `GET /sync/knowledge-bases`; o `RawText` que
`MapGroup("/sync/knowledge-bases")` gera para `MapGet("/")` é
`/sync/knowledge-bases/`, com barra final (medido à parte: `MapGet("")` dá o mesmo).
O boot de toda fixture caiu nomeando a entrada. Sem a checagem, o conector receberia
`403` nessa rota em produção, e nenhum teste de rota do operador teria notado. A
entrada da lista ficou `("GET", "/sync/knowledge-bases/")`; a URL que o cliente chama
não muda. O teste da composição real ficou em `ServiceScopeAuthorizationTests`, que
já tem o host construído: a primeira redação, sem host, mapeava as rotas a partir da
própria lista e não provava nada.

**Evidência de que a recusa de subject desconhecido não quebra nenhum chamador
existente.** Varredura feita em 03/10/2026, em `9cd2a93`, sobre o repositório
inteiro (`apps/` com os testes de todos os apps, `tests/`, `scripts/`, `deploy/`),
com `git grep` por `Issue(`, `ServiceSubject`, `"operator"`, `service:`,
`TokenSigningKey`, `CreateToken`, `IssueToken`, `TokenPayload`, `hmac`, `"sub"` e
`Bearer`. Todo lugar que **assina** token:

| arquivo e linha | subject |
|---|---|
| `apps/api/src/Buteco.Api/Auth/Endpoints/AuthEndpoints.cs:45` (login) | `operator` |
| `apps/inbox/src/Buteco.Inbox/Auth/ServiceTokenDelegatingHandler.cs:24` (constante na linha 12) | `service:inbox` |
| `apps/api/tests/Buteco.Api.Tests/Support/TestAuthentication.cs:53` | `operator` |
| `apps/api/tests/Buteco.Api.Tests/ServiceScopeAuthorizationTests.cs:74` | `service:inbox` (`ServiceScopeAuthorizationHandler.ServiceSubject`) |
| `apps/api/tests/Buteco.Api.Tests/Knowledge/KnowledgeRouteAuthenticationTests.cs:149` | `service:inbox` (idem) |
| `apps/inbox/tests/Buteco.Inbox.Tests/Support/TestAuthentication.cs:26` | `operator` |

Quem só **anexa** token emitido por outro lugar, sem assinar:
`tests/InboxOrchestratorRoundTrip.Tests/RoundTripTests.cs:204,217,284,317,355`
(pelo login, `RoundTripFixture.LoginAsOperatorAsync`, logo `operator`), os
`request<T>` do frontend (token do login guardado em `sessionStorage`) e o
`apps/inbox` nas chamadas de saída (o `service:inbox` acima). `scripts/` só tem
`check-docs.py`, `deploy/` só tem o `migrate` (Dockerfile e `entrypoint.sh`), e
nenhum dos dois monta token. Não há smoke nem teste de carga no repositório.
`apps/workers` não faz chamada HTTP autenticada a outro app. As duas
implementações de `TokenService.Issue` (`apps/api/.../Auth/TokenService.cs:14` e
`apps/inbox/.../Auth/TokenService.cs:13`) recebem o subject do chamador e não têm
subject próprio.

**Nenhum subject fora de `operator` e `service:inbox` está em uso.** A tabela da
D6 (`operator`, `service:inbox`, `service:connectors`) cobre todos os chamadores
atuais, e o único subject novo é o desta change.

- *Descartado, acrescentar um segundo `if` para `service:connectors` e manter o
  resto:* manteria o acesso total para qualquer subject novo assinado com a chave,
  que é exatamente o defeito achado.
- *Descartado, chave de assinatura por serviço:* fecharia o fato de que qualquer
  dono da chave pode assinar qualquer subject, inclusive `operator`. Mas muda o
  molde da autenticação nos dois apps que validam, e não é o problema desta change.
  Fica como risco registrado, com issue própria: **#117** (`aguardando gatilho`).

### D7. Escrita do operador em base `Synced` responde `409`, decidida no handler

Criar, editar e excluir documento pela rota do operador numa base `Synced`
responde `409` com `ProblemDetails` (título em português dizendo que os
documentos vêm da pasta de origem). A ordem é: base inexistente → `404`; base
`Synced` → `409`; só então o conteúdo é processado. Na exclusão o `409` vem antes de
procurar o documento: o operador não pode excluir nada ali, exista ou não.

Na edição, o documento é procurado primeiro (é o que o handler já fazia), e o `409`
sai da cópia do tipo no próprio documento, que a FK composta mantém igual ao da base
(D4). Documento inexistente numa base `Synced` responde `404` na edição.

A validação de forma do payload continua no endpoint e acontece antes do handler,
então um payload malformado numa base `Synced` responde `400`. Os testes usam
payload válido para afirmar o `409`.

Continuam liberados em base `Synced`: reindexar documento, editar nome e descrição
da base, ativar e desativar.

O `409` também vale no sentido oposto: as rotas de escrita de documento de
`/sync` em base `Manual` respondem `409`.

- *Descartado, `403`:* o operador tem permissão; o que impede a escrita é o estado
  do recurso, que é o significado do `409`.
- *Descartado, `405`:* a casa usa `405` para verbo que a rota não oferece (`01`,
  "Exclusão: catálogo × conteúdo"). Aqui a rota oferece o verbo; quem decide é o
  tipo da base.

### D8. Rotas de serviço sob `/sync/knowledge-bases`

| método e rota | o que faz | respostas |
|---|---|---|
| `GET /sync/knowledge-bases` | bases `Synced`, inclusive inativas: id, provedor, pasta, nome e URL da pasta, `isActive` | `200` |
| `GET /sync/knowledge-bases/{id}/documents` | `externalRef`, `externalVersion` e `documentId` de cada documento da base | `200`, `404`, `409` em base `Manual` |
| `PUT /sync/knowledge-bases/{id}/documents` | upsert por `externalRef` (no corpo) | `200` com `{ documentId, outcome }`, `400`, `404`, `409` |
| `DELETE /sync/knowledge-bases/{id}/documents?externalRef=...` | exclusão por referência | `204`, `400`, `404`, `409` |
| `POST /sync/knowledge-bases/{id}/sync-results` | grava o resultado de um ciclo (D2) | `200` com a base, `400`, `404`, `409` |

- **`externalRef` no corpo e na query, não no caminho:** é string opaca de um
  conjunto aberto de provedores, e o id do próximo pode ter `/`. No caminho, `%2F`
  tem tratamento inconsistente entre roteador e proxy.
- **`outcome` do upsert** é `Created`, `Updated` ou `Unchanged`, como string
  (`JsonStringEnumConverter`). Um `200` só, em vez de `201` para inclusão, para o
  conector tratar uma forma.
- **Exclusão de referência inexistente responde `204`**, sem evento. O estado
  pedido ("não há documento com essa referência") já vale, e a retentativa do
  conector depois de uma falha de rede não vira erro. O `404` fica reservado para a
  base inexistente, que a #105 trata como "encerrar o ciclo daquela base".
- **Upsert e exclusão ignoram `IsActive`:** base inativa continua sendo mantida.
- **Ordenação:** bases por `CreatedAt` e desempate por `Id`; referências por
  `ExternalRef` e desempate por `Id` (`api-response-ordering`).
- **O resultado do ciclo é `POST` de um evento, não `PUT` do estado:** a rota
  aplica a regra da D2, não substitui o estado. O desfecho vem em `outcome`
  (`Succeeded` ou `Failed`); campos do outro desfecho no mesmo corpo são recusados
  com `400`, em vez de ignorados, porque é contrato entre apps e ignorar esconderia
  um defeito do conector. Lista de ignorados com `externalRef` repetido também é
  recusada.

- *Descartado, rotas de serviço dentro de `/knowledge-bases`:* misturaria as duas
  superfícies nas mesmas rotas, com regras opostas por subject no mesmo handler (o
  operador recebe `409` onde o serviço escreve), e o escopo teria de casar método e
  rota mais o subject para cada verbo.

### D9. A rota de leitura das referências entra nesta change

`GET /sync/knowledge-bases/{id}/documents` não estava na lista da issue. Sem ela o
conector não tem como reconciliar: a #105 compara por `ExternalRef` e
`ExternalVersion` e exclui o que sumiu da pasta, e a única outra fonte seria um
mapeamento no banco do próprio conector, que a #102 descarta nas alternativas
(duplicata quando o app cai entre criar o documento e gravar o mapeamento).

Ela devolve só referência, versão e id: o conector não precisa do texto.

- *Descartado, deixar para a #105:* a #105 é do `apps/connectors` e teria de reabrir
  o escopo de serviço do `apps/api` que esta change fecha.

### D10. Upsert idempotente sob corrida: índice único por base e uma releitura

Índice único em `knowledge_documents ("KnowledgeBaseId", "ExternalRef") WHERE
"ExternalRef" IS NOT NULL`.

O handler procura o documento por base e referência. Se não acha, cria documento e
evento `Created` e salva. **Se o `SaveChangesAsync` falhar por `UniqueViolation`**,
outro upsert da mesma referência venceu: o handler desanexa as entidades `Added`
(documento e evento), relê o documento vencedor e aplica o payload como
atualização, pelas regras da D3. Uma releitura basta, pelo mesmo motivo registrado
em `apps/inbox`: o Postgres só libera a violação para quem perde depois que o
vencedor fez commit.

A consequência que o teste afirma: dois upserts simultâneos com o **mesmo** payload
terminam com **um** documento, **um** evento `Created`, **uma** publicação de
indexação e nenhum `500`; o perdedor responde `200` com `outcome: "Unchanged"`.

**Como o teste sabe que viu a corrida:** o `catch` aceita só a violação do índice de
`ExternalRef` (pelo `ConstraintName`) e registra um evento de log próprio (id 1021),
que o teste conta. Medido: 29 de 30 iterações passaram pelo `catch`. Sem essa
contagem, um laço em que as duas requisições nunca se intercalassem passaria igual.

A publicação na fila continua depois do `SaveChangesAsync` que deu certo, nunca
antes. O perdedor não publica nada se o seu payload não mudou o documento.

**Corrida de duas atualizações do mesmo documento já existente: `ContentRevision` é
token de concorrência.** *Corrigido na revisão da implementação.* A primeira redação
desta decisão dizia "sem token de concorrência, a última escrita vence", e o risco
correspondente dizia que o estado convergia, com trabalho repetido e sem dado errado.
**Estava errado, e a causa é o uso que o indexador faz da revisão:**

- o consumidor de `apps/workers` lê o **texto do banco** no início, sem rastreamento
  (`apps/workers/src/Buteco.Workers/Knowledge/Indexing/KnowledgeIndexingService.cs:71-73`),
  compara a revisão antes de começar (linha 78), e na gravação final faz um
  `UPDATE ... WHERE "ContentRevision" = <revisão da mensagem>` (linha 321) **na mesma
  transação** que apaga e insere os fragmentos (linhas 318-345);
- com duas escritas lendo a revisão N, as duas gravavam N+1 e publicavam N+1 para
  textos diferentes. O job A lia o texto A; o upsert B gravava o texto B com N+1; o
  job B indexava B; o job A terminava depois, conferia a revisão (N+1 batia) e gravava
  os fragmentos de A. **O documento ficava com o texto B e o índice com os fragmentos
  de A, marcado como indexado**;
- reproduzido antes da correção: as duas publicações saíram `[2, 2]`, nos dois
  caminhos. **O caminho do operador (dois `PUT` simultâneos) tinha o mesmo defeito, e
  ele é anterior a esta change.**

A correção:

1. **`ContentRevision` com `IsConcurrencyToken()`** no `AppDbContext`. O SQL emitido,
   capturado do caminho real da requisição:
   `UPDATE knowledge_documents SET "ContentHash" = @p9, "ContentRevision" = @p10, "ExtractedText" = @p11, "UpdatedAt" = @p12 WHERE "Id" = @p13 AND "ContentRevision" = @p14`,
   e o `DELETE` também: `DELETE FROM knowledge_documents WHERE "Id" = @p9 AND "ContentRevision" = @p10`.
   Sem migração: o token não muda o esquema, e
   `dotnet ef migrations has-pending-model-changes` respondeu "No changes have been
   made to the model since the last migration".
2. **Uma releitura.** Ao receber `DbUpdateConcurrencyException`, o handler limpa o
   change tracker, relê o documento e reaplica a escrita UMA vez. O perdedor fica com
   N+2 e publica a própria indexação. Vale para o upsert do `service:connectors` e
   para a atualização do operador, e também para a reindexação e para as duas
   exclusões, que passaram a receber a exceção porque o `UPDATE` e o `DELETE` delas
   também levam a revisão no `WHERE`: a reindexação precisa publicar a revisão
   corrente, e não a lida, ou o consumidor a descartaria; a exclusão relê para gravar
   o evento com o título atual.
3. **A segunda falha seguida responde `503 Service Unavailable` com
   `Retry-After: 1`**, e nada é gravado. *Descartado, `409`:* nas mesmas rotas o
   `409` já diz "o tipo da base não permite esta escrita" (D7), que repetir não
   resolve, e o conector precisaria ler o corpo para distinguir "pare" de "repita".
   O `503` é o status que políticas de retentativa padrão (como o handler de
   resiliência do `HttpClient`) repetem sozinhas, e o `Retry-After` diz quando.
   *Descartado, `500`:* afirma defeito do servidor onde houve contenção esperada.
   *Descartado, laço de releituras até dar certo:* sob contenção contínua, a
   requisição não termina; uma releitura cobre a corrida de duas escritas, que é o
   caso real.
4. **O `apps/workers` não muda.** Ele não grava `ContentRevision` (só filtra por ela
   nos três `ExecuteUpdate`, linhas 291, 321 e 369), então as transições de
   indexação nunca invalidam a escrita da API — é o mesmo motivo pelo qual a revisão
   é coluna explícita e não `xmin`.

**O teste de cada cenário de `503`:**

| cenário (spec) | teste |
|---|---|
| "Segunda falha seguida nas rotas do operador" (`knowledge-document-catalog`) | `KnowledgeDocumentCatalogTests.SecondConsecutiveConcurrencyFailure_Returns503WithRetryAfter_OnEveryWritePath` (caminhos `PUT`, reindexação e `DELETE` do operador) |
| "Segunda falha seguida no upsert e na exclusão por referência" (`knowledge-sync-service-api`) | o mesmo teste, caminhos upsert e `DELETE` por referência |
| upsert com o documento excluído entre a primeira tentativa e a releitura (mesmo requisito) | `KnowledgeDocumentCatalogTests.Upsert_WhenTheDocumentIsDeletedBeforeTheReread_Returns503AndARetryCreatesIt` |

O teste dos cinco caminhos afirma, por caminho, `503`, `Retry-After`, contagem de
eventos inalterada e nenhuma publicação; os guardas que publicam ou gravam o evento
antes de confirmar a gravação o reprovam (tarefa 11.3).

**Como os testes sabem que viram a corrida:** cada handler registra um evento de log
próprio na releitura (1022 no upsert, 1023 na atualização do operador), e os testes
contam. Medido: **20 de 20** iterações pela releitura em cada um dos dois caminhos. A
segunda falha seguida é forçada de forma determinística por um interceptor que lança
`DbUpdateConcurrencyException` em todo `SaveChanges` que modifica ou exclui um
documento, num host derivado da fixture.

- *Descartado, `INSERT ... ON CONFLICT` em SQL:* o evento do histórico teria de
  saber se a linha foi inserida ou atualizada, e a regra de `ContentRevision`,
  `ContentHash` e evento sairia da entidade para o SQL.
- *Descartado, `SELECT ... FOR UPDATE` ou bloqueio consultivo:* não há linha para
  bloquear na inclusão, e o bloqueio consultivo seria um mecanismo novo para um
  caso que o índice já resolve.
- *Descartado, `xmin` como token de concorrência no documento:* o consumidor de
  indexação de `apps/workers` muta a mesma linha ao transicionar de estado, e um
  token de linha faria o upsert falhar por causa da indexação (é o motivo de
  `ContentRevision` ser coluna explícita, `01`). O token escolhido é a própria
  `ContentRevision`, que o consumidor nunca escreve.

### D11. Cadastro e edição de base pela rota do operador

- **`POST /knowledge-bases`** ganha `contentMode`, opcional. Omitido ou `Manual` →
  base `Manual`. `Synced` → `400` em `contentMode`, dizendo que a base sincronizada
  é criada com a pasta validada (#104). Valor desconhecido → `400`.
- **`PUT /knowledge-bases/{id}`** não ganha campo nenhum. Um corpo com
  `contentMode`, `provider` ou `folderId` diferentes responde `200` com os valores
  gravados intactos, porque o `System.Text.Json` ignora propriedades desconhecidas
  no `UpdateKnowledgeBaseRequest`. É o aceite "um `PUT` com pasta diferente não
  altera a pasta gravada", e o teste afirma pelo banco, não só pela resposta.
- **A base `Synced` nasce por um construtor próprio**, `KnowledgeBase.CreateSynced`,
  usado nesta change só pelos testes e, a partir da #104, pelo handler de cadastro.
  É declaração com consumidor de produção agendado e issue aberta, e não órfã
  (convenção 25).

- *Descartado, ignorar `contentMode: "Synced"` no `POST` e criar base `Manual`:* o
  cliente pediria uma coisa e receberia outra com `201`.
- *Descartado, recusar com `400` o `PUT` que trouxer os campos imutáveis:* o
  aceite da issue fala em "não altera", e o padrão da casa é ignorar propriedade
  desconhecida em todo request.

### D12. Onde os testes moram, e a contagem de contêineres

Nenhuma classe nova que suba contêiner. Os casos novos entram em classes que já
existem, em arquivos novos declarados `partial` da mesma classe, porque
`IClassFixture` é por classe e uma classe nova com `ApiFactoryFixture` subiria mais
um contêiner:

| arquivo novo | classe (existente) | o quê |
|---|---|---|
| `Knowledge/KnowledgeBaseCatalogTests.Synced.cs` | `KnowledgeBaseCatalogTests` | cadastro com `contentMode`, imutabilidade no `PUT`, resposta da base e formato de fio, índice da pasta (inclusive corrida), resultado de ciclo |
| `Knowledge/KnowledgeDocumentCatalogTests.Synced.cs` | `KnowledgeDocumentCatalogTests` | `409` do operador, o que continua liberado, invariantes no banco, upsert, exclusão, histórico, corridas |
| `ServiceScopeAuthorizationTests.cs` (já existe) | `ServiceScopeAuthorizationTests` | escopo dos três subjects e subject desconhecido |
| `KnowledgeBaseContentModeMigrationTests.cs` | **classe nova**, na `MigrationPostgresCollection` | migração deixa as bases como `Manual` sem alterar mais nada |

A classe de migração é nova, mas entra na collection que já tem contêiner próprio
(D12 da #98): **zero fonte de contêiner nova**. Pelo critério literal do `02`, ela
soma uma classe.

**Autorizada pelo mantenedor em 03/10/2026**, na revisão dos artefatos:
`KnowledgeBaseContentModeMigrationTests` na `MigrationPostgresCollection`, sem
contêiner novo. A alternativa que estava na mesa (pôr os casos na
`KnowledgeDocumentEventsMigrationTests`, da mesma collection) fica descartada: o
nome da classe deixaria de dizer o que ela testa.

Os testes de checagem de boot do escopo (D6) seguem o molde de
`RouteAuthenticationStartupFailureTests`, sem contêiner.

**Contagem medida na abertura** (`9cd2a93`), pelo critério do `02` (classes com
fixture de contêiner mais classes que constroem o próprio, excluída `Support/`),
com `git grep`:

| commit | `IClassFixture` de contêiner | pela collection | constroem o próprio | classes (literal) | fontes de contêiner |
|---|---|---|---|---|---|
| `1cae600` (baseline da #98) | 37 | 0 | 5 | 42 | 42 |
| `9cd2a93` (abertura desta) | 37 | 2 | 4 | **43** | **42** |
| previsto ao fim desta change | 37 | 3 | 4 | 44 | 42 |

**O `02` registrou 41 → 42 classes e 41 fontes para a #98; esta medição dá 42 →
43 e 42.** A diferença é constante (+1 nos dois commits), então é de instrumento e
não de mudança no código: o `git grep` conta 37 `IClassFixture` de contêiner em
`1cae600`, e o `02` registrou 36 com fixture. Não investigado além disso; o delta
de cada change continua igual nas duas réguas. A tarefa final remede com o mesmo
comando.

### D13. Formato de fio da base

`KnowledgeBaseResponse` ganha três campos, todos em camelCase pela política web
padrão:

```json
{
  "contentMode": "Synced",
  "syncSource": {
    "provider": "google-drive",
    "folderId": "1AbC...",
    "folderName": "Atendimento",
    "folderUrl": "https://drive.google.com/drive/folders/1AbC..."
  },
  "syncState": {
    "lastCompletedAt": "2026-10-03T12:00:00+00:00",
    "lastFinishedAt": "2026-10-03T12:05:00+00:00",
    "failingSince": "2026-10-03T12:05:00+00:00",
    "lastError": { "code": "access-denied", "detail": "leitor@projeto.iam.gserviceaccount.com" },
    "ignoredFiles": [
      { "externalRef": "1xYz...", "name": "planilha.xlsx", "code": "unsupported-type", "detail": null }
    ]
  }
}
```

- `contentMode` é string (`JsonStringEnumConverter` no enum), nunca ordinal.
- **Base `Manual`: `syncSource` e `syncState` são `null`.** É o quarto estado da
  convenção 13, "sei que não existe": base manual não tem origem.
- **Base `Synced` que nunca terminou um ciclo:** `syncState` existe, com os
  instantes nulos e **`ignoredFiles: null`**, não `[]`. Lista vazia afirmaria que
  uma listagem aconteceu e não ignorou nada; nulo diz que nenhuma listagem
  aconteceu. Depois do primeiro sucesso, `[]` é medição.
- `provider` é string aberta, sem enum (alternativa da issue: provedor como enum
  exigiria mudar o `apps/api` a cada conector).
- O formato é afirmado sobre o **texto** da resposta HTTP real, nunca por
  desserialização de ida e volta (convenção 11, e o molde de
  `KnowledgeBaseIndexingSummaryTests.SummaryResponse_UsesCamelCaseFieldNamesOnTheWire`).

A resposta de documento do operador não muda: o tipo é da base, e a tela já tem a
base.

## Árvore de pastas proposta

Só `apps/api`. Nada em `libs/`.

```
apps/api/
├── src/Buteco.Api/
│   ├── Auth/
│   │   ├── Endpoints/AuthEndpoints.cs                      (alterado: OperatorSubject)
│   │   ├── ServiceScopeAuthorizationHandler.cs             (alterado: tabela de subjects, default-deny)
│   │   └── ServiceScopeRouteValidation.cs                  (novo: checagem no boot, D6)
│   ├── KnowledgeBases/
│   │   ├── Commands/CreateKnowledgeBase/
│   │   │   └── CreateKnowledgeBaseCommand.cs               (NÃO mudou: só Manual chega ao handler)
│   │   ├── Endpoints/KnowledgeBaseEndpoints.cs             (alterado: contentMode no POST)
│   │   ├── Entities/
│   │   │   ├── KnowledgeBase.cs                            (alterado: origem, estado, CreateSynced, RecordSync*)
│   │   │   ├── KnowledgeBaseContentMode.cs                 (novo)
│   │   │   └── KnowledgeBaseSyncIgnoredFile.cs             (novo)
│   │   ├── Requests/CreateKnowledgeBaseRequest.cs          (alterado)
│   │   └── Responses/
│   │       ├── KnowledgeBaseResponse.cs                    (alterado)
│   │       ├── KnowledgeBaseSyncSourceResponse.cs          (novo)
│   │       └── KnowledgeBaseSyncStateResponse.cs           (novo)
│   ├── KnowledgeDocuments/
│   │   ├── Commands/
│   │   │   ├── CreateKnowledgeDocument/...Handler.cs       (alterado: 409, ContentMode na cópia)
│   │   │   ├── UpdateKnowledgeDocument/...Handler.cs       (alterado: 409)
│   │   │   ├── UpdateKnowledgeDocument/...Result.cs        (alterado: Conflict)
│   │   │   ├── CreateKnowledgeDocument/...Result.cs        (alterado: Conflict)
│   │   │   ├── ReindexKnowledgeDocument/...Command.cs      (alterado: resultado com a segunda falha de concorrência, D10)
│   │   │   ├── DeleteKnowledgeDocument/...Handler.cs       (alterado: resultado com 409)
│   │   │   └── DeleteKnowledgeDocument/...Result.cs        (novo: enum de três desfechos)
│   │   ├── Endpoints/KnowledgeDocumentEndpoints.cs         (alterado: 409)
│   │   └── Entities/KnowledgeDocument.cs                   (alterado: ExternalRef, ExternalVersion, ApplyExternalRevision)
│   ├── KnowledgeSync/                                      (novo)
│   │   ├── Commands/
│   │   │   ├── UpsertSyncedDocument/{Command,Handler,Result}.cs
│   │   │   ├── DeleteSyncedDocument/{Command,Handler,Result}.cs
│   │   │   └── RecordSyncResult/{Command,Handler,Result}.cs
│   │   ├── Queries/
│   │   │   ├── ListSyncedKnowledgeBases/{Query,Handler}.cs
│   │   │   └── ListSyncedDocumentRefs/{Query,Handler}.cs
│   │   ├── Endpoints/KnowledgeSyncEndpoints.cs
│   │   ├── Requests/{UpsertSyncedDocumentRequest,RecordSyncResultRequest}.cs
│   │   ├── Responses/{SyncedKnowledgeBaseResponse,SyncedDocumentRefResponse,UpsertSyncedDocumentResponse}.cs
│   │   ├── SyncCode.cs                                     (forma do código, D1)
│   │   └── SyncedKnowledgeBaseLookup.cs                    (novo: 404/409/segue, comum às rotas de /sync)
│   ├── Infrastructure/
│   │   ├── AppDbContext.cs                                 (alterado)
│   │   └── Migrations/<timestamp>_AddKnowledgeBaseSync.cs  (novo)
│   └── Program.cs                                          (alterado: MapKnowledgeSyncEndpoints, checagem)
└── tests/Buteco.Api.Tests/
    ├── Knowledge/KnowledgeBaseCatalogTests.cs              (alterado: partial)
    ├── Knowledge/KnowledgeBaseCatalogTests.Synced.cs       (novo, mesma classe)
    ├── Knowledge/KnowledgeDocumentCatalogTests.cs          (alterado: partial)
    ├── Knowledge/KnowledgeDocumentCatalogTests.Synced.cs   (novo, mesma classe)
    ├── Knowledge/KnowledgeDocumentCatalogTests.Concurrency.cs (novo, mesma classe: SQL do token e segunda falha seguida, D10)
    ├── Knowledge/KnowledgeSyncTestSeed.cs                  (novo: semeia base Synced; contador de log da corrida)
    ├── Knowledge/KnowledgeSyncEntityTests.cs               (novo, sem contêiner: regras da entidade, tarefa 2.5)
    ├── Knowledge/SyncCodeTests.cs                          (novo, sem contêiner)
    ├── KnowledgeBaseContentModeMigrationTests.cs           (novo, na MigrationPostgresCollection)
    ├── ServiceScopeAuthorizationTests.cs                   (alterado)
    ├── ServiceScopeRouteValidationTests.cs                 (novo, sem contêiner; a composição real ficou em ServiceScopeAuthorizationTests)
    └── Knowledge/KnowledgeRouteAuthenticationTests.cs      (alterado: InboxSubject)
```

## Risks / Trade-offs

- **[Duas escritas simultâneas do mesmo documento gravavam a mesma revisão]** →
  *Corrigido na revisão da implementação; a redação anterior deste risco dizia que o
  estado convergia, e estava errada (D10).* Com a mesma revisão para textos
  diferentes, o indexador podia gravar os fragmentos de um texto sobre o outro e
  marcar o documento como indexado. Mitigação: `ContentRevision` como token de
  concorrência, uma releitura, e `503` com `Retry-After` na segunda falha seguida.
  Verificável: as corridas afirmam revisões N+1 e N+2 nas duas publicações e N+2 no
  final, e o guarda que retira o token as reprova.
- **[Qualquer dono da chave assina qualquer subject]** → Já era verdade com o
  `apps/inbox`, e o `apps/connectors` é mais um processo com a chave. A D6 não
  piora o modelo e fecha o acesso total de subject desconhecido, mas protege contra
  defeito de código, não contra o comprometimento de um app que guarda a chave: esse
  app assina `operator`. Chave por serviço fica fora (D6), na **#117**, com o
  gatilho "primeira implantação do `apps/connectors` fora do ambiente de
  desenvolvimento".
- **[A lista de ignorados cresce sem limite e vai em toda listagem de bases]** →
  Uma pasta com milhares de arquivos não suportados poria milhares de itens em
  cada `GET /knowledge-bases`. Gatilho para rever: uma base com mais de 1.000
  ignorados numa implantação, ou p95 de `GET /knowledge-bases` acima de 300 ms.
  Hoje não há base sincronizada. Truncar a lista foi descartado: esconderia
  arquivos sem dizer (convenção 13).
- **[A FK composta troca a FK atual numa migração]** → A troca valida as linhas
  existentes; todas recebem `Manual` na cópia, igual ao default da base, e a FK nova
  passa. Verificável: o teste de migração cria base e documentos no estado
  anterior e migra.
- **[O EF pode não aceitar a chave alternativa com conversor de enum, ou gerar a
  FK composta de forma diferente da descrita]** → Convenção 6: verificado na
  tarefa 1.1, pelo SQL gerado (`dotnet ef migrations script`) e por um teste que
  tenta modificar `ContentMode` pelo change tracker, antes de construir o resto
  sobre a D4. Se não sustentar, a D4 é corrigida com a causa.
- **[A checagem de boot do escopo derruba o boot por uma entrada errada]** → É o
  efeito pretendido (convenção 8). Verificável: teste da composição real sobe.
- **[O `02` diz "hash de conteúdo" para o `ExternalVersion`]** → Corrigido na
  tarefa de documentação, apontando o comentário da etapa 0 na #102.

## Migration Plan

Uma migração, `AddKnowledgeBaseSync`, no `apps/api`:

1. `knowledge_bases`: `ContentMode text NOT NULL DEFAULT 'Manual'`; `SyncProvider`,
   `SyncFolderId`, `SyncFolderName`, `SyncFolderUrl`, `LastSyncErrorCode`,
   `LastSyncErrorDetail` `text NULL`; `LastSyncCompletedAt`, `LastSyncFinishedAt`,
   `SyncFailingSince` `timestamptz NULL`; `SyncIgnoredFiles jsonb NULL`.
2. `CHECK`s da base: valores de `ContentMode`; `Synced` com provedor, pasta, nome e
   URL não nulos e `Manual` com todas as colunas de sincronização nulas; as duas
   da D2.
3. Chave alternativa única `("Id", "ContentMode")` e índice único parcial da pasta
   (D5).
4. `knowledge_documents`: `KnowledgeBaseContentMode text NOT NULL DEFAULT 'Manual'`,
   `ExternalRef` e `ExternalVersion` `text NULL`, as duas `CHECK`s da D4 e o índice
   único parcial da D10.
5. Troca da FK simples pela composta, com `Restrict`.

**Sem backfill além do default.** Bases existentes ficam `Manual` com tudo mais
como estava, e documentos existentes ficam com `ExternalRef` nulo e cópia `Manual`.
Nenhum evento no histórico.

**Rollback:** o `Down` remove a FK composta, recria a simples, remove índices,
`CHECK`s e colunas. Só é seguro enquanto não houver base `Synced`, que nesta change
só existe nos testes.

**Ordem de deploy:** o `migrator` do compose roda antes de `api` e `workers`. O
`apps/workers` não lê as colunas novas.

## Open Questions

Nenhuma. Os dois pontos que dependiam do dono foram decididos na revisão de
03/10/2026: a classe de migração nova na collection foi autorizada (D12), e a
correção de quem emite o token de serviço entra nesta change (Context, tarefas 8.1 e
8.2). A varredura de subjects da D6 não achou nenhum subject em uso fora de
`operator` e `service:inbox`, então não abriu pergunta.
