## Context

A #98 pede o histórico de mudanças nos documentos de uma base: incluído,
atualizado e excluído, com autor e instante, legível por uma rota paginada. A
issue já fixou quatro coisas, que este documento não reabre:

- a tabela é ligada à base por cascade e **não** tem FK para o documento, e o nome
  do documento é snapshot;
- o evento é gravado pelo `apps/api` na mesma transação da escrita, porque só ele
  sabe o que a escrita fez;
- o autor é uma string aberta;
- a retenção é sem limite nesta entrega.

O estado atual que pesa nas decisões:

- **As três escritas** ficam em
  `apps/api/src/Buteco.Api/KnowledgeDocuments/Commands/`: `Create`, `Update` e
  `Delete`, e cada uma faz **um** `SaveChangesAsync`. `Reindex` também escreve na
  linha, mas só estado de indexação.
- **`KnowledgeDocument.Update` hoje devolve um `bool`**: "há indexação a
  enfileirar". O retorno é decidido por `ContentHash`, e não pela comparação de
  texto, por causa da linha legada com hash nulo (comentário longo no próprio
  método). `ContentRevision` é incrementada só quando o texto muda (comparação
  ordinal).
- **O FK documento → base é `Restrict`** (D6 da change de catálogo), e a base
  **não tem rota de exclusão**: é catálogo, só se desativa.
- **O token do operador sai com o subject `operator`**
  (`AuthEndpoints.cs`, `tokenService.Issue("operator", ...)`), e o único outro
  subject, `service:inbox`, só é autorizado em duas rotas de agente
  (`ServiceScopeAuthorizationHandler`).
- **Não existe paginação de REST no repositório.** O único `Take` em lista é o
  `ListTasksAsync` do A2A, que não pagina de fato (`NextPageToken` sempre vazio) e
  é contrato do SDK, não da casa. A escolha fica aqui (D7).
- **`apps/workers` escreve em `knowledge_documents`**, mas só estado de indexação,
  nunca título nem texto. E não migra o schema. A tabela nova é só do `apps/api`,
  sem espelho, no mesmo molde de `task_rejections`.

## Goals / Non-Goals

**Goals:**

- Toda escrita de documento que muda o que o documento **é** (existir, texto,
  título) deixa exatamente um evento, atômico com ela.
- Nenhuma escrita que não muda isso deixa evento: conteúdo e título idênticos
  (inclusive na linha legada), escrita recusada, reindexação, transição de estado
  feita pelo `apps/workers`.
- O evento de exclusão sobrevive ao documento, e os eventos morrem com a base.
- Uma rota lê o histórico de **uma** base, do mais recente para o mais antigo, sem
  duplicar nem pular evento entre páginas quando eventos novos chegam no meio.

**Non-Goals:**

- UI. A aba Histórico é a #101.
- Autor "sincronização" e as rotas de serviço. São da #102; esta change só deixa o
  autor aberto o bastante para ela não precisar mexer no histórico.
- Retenção, expurgo ou arquivamento de eventos (D9).
- Eventos de base (criar, renomear, desativar). A #98 é sobre documentos.
- Rota de exclusão de base (D3).

## Decisions

### D1. Tabela própria, FK só para a base, `DocumentId` como coluna solta

`knowledge_document_events`, com:

| coluna | tipo | nota |
|---|---|---|
| `Id` | `uuid` | `Guid.NewGuid()`, como toda entidade do `apps/api` |
| `KnowledgeBaseId` | `uuid` | FK para `knowledge_bases`, **`ON DELETE CASCADE`** |
| `DocumentId` | `uuid` | **sem FK**: identifica o documento enquanto ele existe e depois que some |
| `DocumentTitle` | `text` | snapshot do título **depois** da escrita; na exclusão, o título que o documento tinha |
| `Type` | `text` | `Created`, `Updated` ou `Deleted`, gravado como string |
| `ContentChanged` | `boolean` nulo | só em `Updated` (D5) |
| `TitleChanged` | `boolean` nulo | só em `Updated` (D5) |
| `Author` | `text` | subject do token (D6) |
| `OccurredAt` | `timestamptz` | instante da escrita no `apps/api` |

Índice em `(KnowledgeBaseId, OccurredAt, Id)`, que serve o filtro por base e a
ordem da rota (o Postgres percorre o B-tree de trás para frente para a ordem
descendente).

**`DocumentId` não estava na issue.** Ele entra porque sem ele dois documentos de
mesmo título são indistinguíveis no histórico, e a #101 não teria como agrupar os
eventos de um documento nem dizer se ele ainda existe.

- *Alternativa descartada, FK para o documento com `ON DELETE SET NULL`:* a
  exclusão apagaria justamente a identidade que o evento de exclusão precisa
  carregar. E a issue já tinha descartado a FK.
- *Alternativa descartada, só o título como identificação:* título não é único
  dentro da base, e o snapshot muda a cada evento de renomeação.

### D2. O evento entra no `ChangeTracker` antes do único `SaveChangesAsync` do handler

Cada handler adiciona o evento ao `AppDbContext` **antes** do `SaveChangesAsync`
que já existe, e não chama um segundo. O EF Core grava as duas linhas na mesma
transação implícita: ou as duas existem, ou nenhuma.

Isso dá a regra da escrita recusada de graça. Os retornos antecipados de hoje
(base inexistente, documento inexistente ou em outra base, conteúdo inválido,
acima do teto) acontecem **antes** de o evento ser criado. Todos os handlers já
validam antes de tocar a entidade, e a ordem continua a mesma.

A publicação na fila de indexação continua **depois** do `SaveChangesAsync`, sem
mudança.

- *Alternativa descartada, interceptor de `SaveChanges` lendo o
  `ChangeTracker`:* teria que inferir "criou, atualizou ou não mudou nada"
  comparando valores originais e correntes. É exatamente o adivinhar que a issue
  recusa, e a regra da linha legada (D4) mora em `KnowledgeDocument.Update`, não
  nos valores das propriedades.
- *Alternativa descartada, trigger no Postgres:* dispararia também nas escritas do
  `apps/workers`, que atualiza a linha do documento a cada transição de indexação.
  Também não tem como saber o autor, e espalha regra de domínio para o banco.

### D3. A cascata para a base nasce inerte, e é verificada no banco

O FK de evento → base é `Cascade`, como a issue decidiu. Dois fatos limitam o que
isso faz hoje:

1. **A base não tem rota de exclusão.** A cascata só é alcançável por SQL direto.
2. **O FK documento → base é `Restrict`.** Uma base com documento não pode ser
   apagada nem por SQL. A cascata só remove eventos de uma base que **já não tem
   documento nenhum**: todos foram excluídos, e sobraram os eventos.

O contraste com as tabelas de métrica (`knowledge_indexing_attempts`,
`embedding_calls`), que **não** têm FK para o catálogo e sobrevivem a ele, é
proposital. Métrica é total do sistema num período, e não pode mudar porque o
catálogo mudou depois. O histórico é a auditoria **da base**: sem a base, não há
onde ele seja lido.

O cenário "excluir a base remove os eventos dela" é verificado apagando a linha
de `knowledge_bases` por SQL num teste, depois de excluir os documentos pela rota.
É o mesmo recurso que o teste da linha legada já usa
(`KnowledgeDocumentIndexingContractTests`).

**A rota de exclusão de base é a #108.** Quando ela existir, a cascata desta
change deixa de ser inerte: excluir a base apaga o histórico dela pela rota, e não
só por SQL. Decidir o destino dos documentos (hoje `Restrict`) é escopo da #108,
não desta change.

- *Alternativa descartada, `Restrict` também nos eventos:* a base cujos documentos
  foram todos excluídos ficaria impossível de apagar por causa do próprio
  histórico. Quem criar a rota de exclusão de base teria que apagar eventos à mão
  antes, e isso contraria a decisão da issue.

### D4. "Atualizado" é decidido por `ContentRevision` e pelo título, nunca pelo `ContentHash`

`KnowledgeDocument.Update` passa a devolver um record com três campos:

```
KnowledgeDocumentUpdateOutcome(bool NeedsIndexing, bool ContentChanged, bool TitleChanged)
```

- `ContentChanged` é **exatamente** a condição que já incrementa
  `ContentRevision`: o texto extraído novo é diferente do gravado, em comparação
  ordinal.
- `TitleChanged` é a mesma comparação ordinal sobre o título.
- `NeedsIndexing` é o `bool` de hoje, sem mudança: decidido por `ContentHash`.

O handler grava `Updated` se e somente se `ContentChanged || TitleChanged`.

**Por que os dois critérios divergem, e por que cada um está certo no seu lugar.**
Na linha legada (`ContentHash` nulo), reenviar o conteúdo idêntico dá
`NeedsIndexing = true` (o hash nulo significa "nunca indexado sob esta regra", e
a indexação precisa rodar) e `ContentChanged = false` (o texto é o mesmo). A
primeira resposta é sobre o **índice** e a segunda sobre o **documento**. Gravar
"conteúdo alterado" ali afirmaria uma mudança que o operador não fez, e o
histórico mentiria justamente na base mais antiga. O teste dessa divergência é
obrigatório e tem cenário próprio na spec.

**Trocar só o `SourceType` não gera evento.** Não muda texto nem título, e hoje só
existe `markdown`.

- *Alternativa descartada, `ContentHash` como critério:* erra na linha legada,
  como acima.
- *Alternativa descartada, o handler guardar título e revisão antes e comparar
  depois:* funciona, mas re-deriva no handler uma regra que a entidade já tem. Duas
  cópias da regra "o que é mudança de conteúdo" é como a linha legada vira defeito
  no dia em que uma delas mudar.

### D5. O detalhe são dois booleanos anuláveis, com o formato garantido pelo banco

`ContentChanged` e `TitleChanged` são `null` em `Created` e `Deleted`. Em
`Updated` os dois são não nulos e pelo menos um é `true`. Uma `CHECK` no banco
garante o formato:

```
("Type" = 'Updated' AND "ContentChanged" IS NOT NULL AND "TitleChanged" IS NOT NULL
   AND ("ContentChanged" OR "TitleChanged"))
OR ("Type" <> 'Updated' AND "ContentChanged" IS NULL AND "TitleChanged" IS NULL)
```

São nulos, e não `false`, em `Created` e `Deleted` porque `false` ali afirmaria
"o conteúdo não mudou" sobre uma inclusão, que é a convenção "a UI nunca afirma
mais do que o sistema sabe" valendo para o dado que a UI vai ler.

A `CHECK` torna impossível, e não só improvável, o evento `Updated` sem mudança
que a D4 existe para evitar. Ela ganha teste próprio, porque um guarda só vale
depois de ter falhado.

- *Alternativa descartada, um campo de texto livre para o detalhe:* a #101 teria
  que interpretar texto para decidir o rótulo.
- *Alternativa descartada, dois eventos quando título e conteúdo mudam juntos:* uma
  escrita viraria duas linhas de histórico, e a contagem de "o que aconteceu"
  passaria a depender de como o operador agrupou as edições.

### D6. O autor é o subject do token, verbatim, lido no endpoint

O endpoint recebe o `ClaimsPrincipal` e passa
`user.FindFirst(ClaimTypes.NameIdentifier)?.Value` no command, num campo
`Author`. O handler grava o valor como veio. Nesta change o único valor possível
é `operator`.

**A forma é a que o código já usa, e não uma segunda.** Medido no código, não
presumido:

- **Não há JwtBearer neste app.** A autenticação é um esquema próprio,
  `OperatorTokenAuthenticationHandler` (registrado em `Program.cs:93-95`), e
  nenhum mapeamento de `sub` para `NameIdentifier` acontece por convenção de
  biblioteca.
- **O próprio handler monta o claim**: valida o token pelo `ITokenService` e cria
  `new Claim(ClaimTypes.NameIdentifier, result.Subject!)`
  (`Auth/OperatorTokenAuthenticationHandler.cs:38`). O subject é o que
  `AuthEndpoints.cs:45` emitiu: `tokenService.Issue("operator", ...)`.
- **A leitura que já existe** é
  `context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value`
  (`Auth/ServiceScopeAuthorizationHandler.cs:29`). É essa que o endpoint usa. A
  forma desta D6 na primeira redação (`FindFirstValue(ClaimTypes.NameIdentifier)`)
  lia **o mesmo claim** e daria o mesmo valor. Ela foi trocada para que a base
  tenha uma forma só de ler o subject, e não por divergência de resultado.

A #102 acrescenta o subject `service:connectors`, e as escritas dele vão gravar
`service:connectors` sem nenhuma mudança no histórico. Os rótulos "operador" e
"sincronização" são apresentação, e moram no frontend (#101), que renderiza valor
desconhecido com rótulo neutro.

Ler o subject é seguro: as rotas de documento caem na `FallbackPolicy`
autenticada, e `service:inbox` recebe `403` nelas antes de chegar ao handler.
Então o claim sempre existe quando o handler roda. Se faltar mesmo assim, o
endpoint falha com exceção em vez de gravar autor vazio: afirmar autoria que não
existe é pior que a escrita falhar.

- *Alternativa descartada, valor fixo `"operador"` no handler:* a #102 teria que
  passar o autor pelo command de qualquer jeito, e o valor fixo mentiria no dia em
  que uma rota de serviço reaproveitasse o handler.
- *Alternativa descartada, gravar o rótulo em português:* mistura apresentação no
  dado. Trocar o rótulo viraria migração de dados.

### D7. Paginação por cursor (keyset), página fixa de 50

Ordem: `OccurredAt` decrescente, desempate por `Id` decrescente. O desempate é
exigido por `api-response-ordering`, porque `OccurredAt` não é único.

A resposta é `{ "items": [...], "nextCursor": "<opaco>" | null }`. O cliente
pede a próxima página com `?cursor=<nextCursor>`. A consulta busca **51** linhas
e devolve 50: a 51ª só existe para dizer se há página seguinte, e `nextCursor` é
`null` exatamente quando ela não veio. Assim o cliente nunca pede uma página
vazia só para descobrir que acabou. O
cursor carrega `(OccurredAt, Id)` do último item, codificado em base64url, e é
opaco para o cliente.

Cursor malformado responde `400` com `ValidationProblem` na chave `cursor`.

**Por que cursor e não `page`/`offset`.** O histórico cresce **no topo**, e é o
caso normal, não a exceção: o operador abre a aba e uma escrita acontece antes de
"Carregar mais". Com offset, cada evento novo empurra a página seguinte uma
posição para baixo, e o último item da página anterior aparece de novo. Com
cursor, a página seguinte começa estritamente depois do último item visto, e
evento novo nunca aparece duplicado nem desloca nada.

**Por que 50 fixo, sem `limit`.** "Carregar mais" não tem cenário real para outro
tamanho, e opção sem cenário não existe (convenção "sem abstração prematura").

- *Alternativa descartada, `page`/`pageSize`:* duplica itens quando eventos chegam,
  como acima.
- *Alternativa descartada, `Id` `bigint` sequencial como chave única de ordem e de
  cursor:* simplifica o cursor, mas tira a tabela do padrão `uuid` da casa e faz a
  ordem do histórico ser a da sequência, não a do instante que a tela agrupa por
  dia.
- *Alternativa descartada, devolver `totalCount`:* custa um `count(*)` a cada
  página, e "Carregar mais" não precisa dele.

### D8. Rota `GET /knowledge-bases/{knowledgeBaseId}/document-events`

- Base inexistente: `404`. Base existente sem evento: `200` com `items` vazio e
  `nextCursor` nulo. É o idioma de `GET .../documents`.
- Base inativa: liberada. Desativar impede o uso pelo agente, não a leitura da
  manutenção, como em toda rota de documento.
- Subject de serviço recebe `403`, e um teste afirma isso. Hoje o `403` vem de
  graça, porque a rota não está na lista de `ServiceScopeAuthorizationHandler`.
  Mas a #102 vai mexer nesse escopo para acrescentar `service:connectors`, e é
  ali que uma liberação ampla demais (por prefixo de rota, por exemplo)
  escaparia sem teste.
- Filtra **sempre** por `KnowledgeBaseId`. O cursor não carrega a base, então um
  cursor de outra base não abre a porta para ela: ele só desloca a posição dentro
  da base pedida.
- `type` sai como string (`"Created"`, `"Updated"`, `"Deleted"`), com
  `JsonStringEnumConverter` no enum, como `KnowledgeIndexingStatus`. Os campos
  saem em camelCase: `id`, `documentId`, `documentTitle`, `type`,
  `contentChanged`, `titleChanged`, `author` e `occurredAt`.
- Mapeada num grupo próprio em `KnowledgeDocumentEndpoints`, ao lado das rotas de
  documento.

- *Alternativa descartada, `/knowledge-bases/{id}/documents/events`:* coloca uma
  coleção que não é de documentos dentro da coleção de documentos, e o evento de
  um documento excluído não é sub-recurso de documento nenhum.
- *Alternativa descartada, `/knowledge-bases/{id}/history`:* o nome promete o
  histórico **da base**, e eventos de base estão fora desta change.

### D9. Retenção sem limite, com gatilho observável

Nenhum expurgo nesta entrega. O gatilho para rever é **`knowledge_document_events`
passar de 1 milhão de linhas numa implantação**, medido com
`select count(*) from knowledge_document_events;`. Na escala de hoje (dezenas de
documentos por base, editados à mão), isso está a anos de distância. A
sincronização (#105) é o que pode encurtar esse prazo, e é por isso que o número
fica registrado agora.

**O gatilho mora na #109** (rótulo `aguardando gatilho`), e não só aqui: este
`design.md` vai ser arquivado, e a issue é o que sobrevive ao archive.

- *Alternativa descartada, TTL desde já (por exemplo 90 dias):* política de
  produto sem cenário. Apagar histórico de auditoria é irreversível, e decidir
  isso sem volume medido é o caso clássico de opção sem cenário real.

### D10. A migração não cria eventos retroativos

A migração só cria tabela, FK, índice e `CHECK`. Não faz `INSERT`. Documentos que
já existem não ganham evento `Created`.

- *Alternativa descartada, backfill de `Created` a partir de `CreatedAt`:* afirmaria
  autor e instante de uma escrita que o sistema não registrou. O instante até
  existe, mas o autor seria inventado, e um histórico com eventos fabricados no
  começo deixa de ser confiável no resto. A nota "o histórico só existe a partir
  da implantação" é da #101.

O teste migra um banco até a migração **imediatamente anterior** a
`AddKnowledgeDocumentEvents`, resolvida pela lista ordenada de migrações do
`AppDbContext` e não fixada pelo nome, cria base e documentos por SQL, aplica a
migração nova e afirma zero eventos. Ele falha de forma explícita se
`AddKnowledgeDocumentEvents` não estiver na lista ou for a primeira. Fixar o nome
da anterior quebraria o teste, ou o faria testar o par errado, no dia em que uma
migração entrar entre as duas. Onde ele roda está na D12.

### D11. Onde os testes moram

Os cenários de comportamento entram nas classes de teste de documento que **já
existem** e já pagam contêiner:

| cenário | classe |
|---|---|
| cadastro gera `Created`; cadastro recusado não gera | `KnowledgeDocumentCatalogTests` |
| atualização (conteúdo, título, ambos, idêntico, recusada) | `KnowledgeDocumentUpdateTests` |
| linha legada com hash nulo | `KnowledgeDocumentIndexingContractTests` (já tem o recurso de anular o hash) |
| exclusão; evento sobrevive; cascata da base | `KnowledgeDocumentDeleteTests` |
| reindexar não gera evento | `KnowledgeDocumentReindexTests` |
| rota: ordem, desempate, páginas, isolamento por base, `404`, cursor inválido, formato de fio | `KnowledgeDocumentCatalogTests` |
| rota sem token responde `401`; token `service:inbox` responde `403` | `KnowledgeRouteAuthenticationTests` |
| `CHECK` recusa `Updated` sem mudança | `KnowledgeDocumentUpdateTests` |
| `KnowledgeDocument.Update` devolve o outcome certo (unitário, sem banco) | classe nova sem contêiner |
| migração sem retroativos, e a forma do schema | `KnowledgeDocumentEventsMigrationTests`, na collection da D12 |

O formato de fio é afirmado sobre o **texto** da resposta HTTP real, nunca por
round-trip no mesmo tipo.

### D12. O teste de migração divide um contêiner com `RejectionMetricsMigrationTests`, numa collection

**Decisão do dono, com regra de escolha:** preferir uma collection compartilhada
com `RejectionMetricsMigrationTests`, se ela só precisar trocar a fixture pela
collection sem mudar o que afirma. Senão, usar uma 42ª classe de contêiner.

**Como `RejectionMetricsMigrationTests` obtém o contêiner hoje (lido no
código):**

- **não usa fixture nem collection.** Constrói o `PostgreSqlContainer` como
  **campo da própria classe de teste** (`pgvector/pgvector:pg18`, banco
  `buteco_agents_rejection_metrics_test`);
- implementa `IAsyncLifetime` na classe de teste: `InitializeAsync` sobe o
  contêiner e aplica **todas** as migrações, e `DisposeAsync` derruba o contêiner;
- o isolamento é o **contêiner inteiro**. Nenhum teste dela escreve; todos leem
  `information_schema` e `pg_index`.

**O que a troca exige dela, e por que cabe na regra.** O campo do contêiner sai,
a classe ganha `[Collection]` e recebe a fixture no construtor, e
`InitializeAsync` passa a criar um banco de nome único (`CREATE DATABASE`) no
contêiner da fixture e migrá-lo. O `ScalarAsync` passa a ler a connection string
desse banco. **Nenhum `[Fact]` ou `[Theory]` muda**: as consultas, os valores
esperados e as asserções ficam idênticos, e cada teste continua vendo um banco
recém-migrado e só seu. É a troca da fonte do contêiner, nada além.

**A forma:**

- `Support/MigrationPostgresFixture.cs`: um `PostgreSqlContainer` por execução
  da collection e um `CreateDatabaseAsync()` que cria um banco
  `migration_<guid>` e devolve a connection string dele;
- `Support/MigrationPostgresCollection.cs`: `[CollectionDefinition]` com
  `ICollectionFixture<MigrationPostgresFixture>`;
- as duas classes de migração com `[Collection]`, cada teste criando o próprio
  banco.

**Por que banco por teste, e não por classe.** `KnowledgeDocumentEventsMigrationTests`
precisa de um banco parado na migração **anterior**, e
`RejectionMetricsMigrationTests` de um banco na **última**. Banco por teste
isola os dois estados sem ordem entre testes, e reproduz o que cada teste da
`RejectionMetricsMigrationTests` já tem hoje.

**A contagem, medida agora** em `1cae600`, pelo critério do `02` ("classes com
fixture de contêiner mais classes que constroem o seu próprio, excluída
`Support/`, contadas em todas as subpastas"): **41**, sendo **36** com
`IClassFixture` de fixture de contêiner e **5** que constroem o próprio
(`AgentDescriptionAndSkillsMigrationTests`,
`AgentMcpServerAllowedToolsMigrationTests`, `EmbeddingMetricsMigrationTests`,
`ExecutionMetricsMigrationTests`, `RejectionMetricsMigrationTests`). Bate com os
41 do `02`.

**O que acontece com o número depois desta change, e a ressalva sobre o
critério.** Fontes de contêiner continuam **41**: a fixture da collection
substitui o contêiner próprio de `RejectionMetricsMigrationTests`, e
`KnowledgeDocumentEventsMigrationTests` não acrescenta nenhum. **Lido ao pé da
letra, o critério daria 42**, porque conta **classes** com fixture, e as duas
classes da collection têm fixture. O critério foi escrito quando "classe" e
"contêiner" eram a mesma coisa. Com uma collection, uma fixture serve duas
classes, e o critério precisa contar a collection uma vez. Isso fica registrado
no `02` na implementação (tarefa 1.2).

**Achado que a leitura do código levantou: a #110.** `IAsyncLifetime`
implementado na **classe de teste**, e não numa fixture, roda a cada **teste**,
porque o xUnit cria uma instância da classe por teste. As 5 classes que
constroem o próprio contêiner sobem, portanto, **um contêiner por teste**, e não
um por classe como o `02` registra. `RejectionMetricsMigrationTests` tem 7
casos. O achado virou a **#110** (convenção 23), que move as outras 4 classes. A
tarefa 8.1 continua medindo o número em execução real, como evidência para ela.

- *Alternativa descartada, a 42ª classe de contêiner:* a regra do dono prefere a
  collection quando ela cabe, e ela cabe.
- *Alternativa descartada, o plano B da primeira redação (segundo banco no
  contêiner de uma classe de comportamento):* acopla um teste de migração a uma
  fixture `WebApplicationFactory` que não tem relação com ele, e que migra o
  próprio banco no boot.
- *Alternativa descartada, mover as 5 classes de migração para a collection:*
  ganho real se a ressalva acima se confirmar, mas é mudança em 4 classes que
  esta change não precisa tocar. Fica para a #110.

## Árvore de pastas proposta

Só o que esta change cria (`+`) ou altera (`~`), em `apps/api`:

```
apps/api/
├── src/Buteco.Api/
│   ├── Infrastructure/
│   │   ├── AppDbContext.cs                                   ~ DbSet + mapeamento + CHECK
│   │   └── Migrations/
│   │       ├── <timestamp>_AddKnowledgeDocumentEvents.cs     +
│   │       ├── <timestamp>_AddKnowledgeDocumentEvents.Designer.cs +
│   │       └── AppDbContextModelSnapshot.cs                  ~
│   └── KnowledgeDocuments/
│       ├── Entities/
│       │   ├── KnowledgeDocument.cs                          ~ Update devolve o outcome
│       │   ├── KnowledgeDocumentUpdateOutcome.cs             +
│       │   ├── KnowledgeDocumentEvent.cs                     +
│       │   └── KnowledgeDocumentEventType.cs                 +
│       ├── Commands/
│       │   ├── CreateKnowledgeDocument/
│       │   │   ├── CreateKnowledgeDocumentCommand.cs         ~ + Author
│       │   │   └── CreateKnowledgeDocumentCommandHandler.cs  ~ grava Created
│       │   ├── UpdateKnowledgeDocument/
│       │   │   ├── UpdateKnowledgeDocumentCommand.cs         ~ + Author
│       │   │   └── UpdateKnowledgeDocumentCommandHandler.cs  ~ grava Updated quando houve mudança
│       │   └── DeleteKnowledgeDocument/
│       │       ├── DeleteKnowledgeDocumentCommand.cs         ~ + Author
│       │       └── DeleteKnowledgeDocumentCommandHandler.cs  ~ grava Deleted
│       ├── Queries/
│       │   └── ListKnowledgeDocumentEvents/
│       │       ├── ListKnowledgeDocumentEventsQuery.cs       +
│       │       ├── ListKnowledgeDocumentEventsQueryHandler.cs +
│       │       └── KnowledgeDocumentEventCursor.cs           + codificação do cursor
│       ├── Responses/
│       │   ├── KnowledgeDocumentEventResponse.cs             +
│       │   └── KnowledgeDocumentEventPageResponse.cs         +
│       └── Endpoints/
│           └── KnowledgeDocumentEndpoints.cs                 ~ autor nos commands + rota nova
└── tests/Buteco.Api.Tests/
    ├── RejectionMetricsMigrationTests.cs                     ~ só a fonte do contêiner (D12)
    ├── KnowledgeDocumentEventsMigrationTests.cs              + collection da D12
    ├── Support/
    │   ├── MigrationPostgresFixture.cs                       + contêiner + banco por teste
    │   └── MigrationPostgresCollection.cs                    + [CollectionDefinition]
    └── Knowledge/
        ├── KnowledgeDocumentCatalogTests.cs                  ~
        ├── KnowledgeDocumentUpdateTests.cs                   ~
        ├── KnowledgeDocumentDeleteTests.cs                   ~
        ├── KnowledgeDocumentReindexTests.cs                  ~
        ├── KnowledgeDocumentIndexingContractTests.cs         ~
        ├── KnowledgeRouteAuthenticationTests.cs              ~
        ├── KnowledgeTestClient.cs                            ~ helper de leitura dos eventos
        └── KnowledgeDocumentUpdateOutcomeTests.cs            + unitário, sem contêiner
```

As classes de migração ficam na raiz de `Buteco.Api.Tests/`, ao lado das outras
cinco, e não em `Knowledge/`.

Fora de `apps/api`: `docs/architecture.md` e `CHANGELOG.md`. Nada em `libs/`,
`apps/workers`, `apps/inbox` ou `apps/frontend`. As regras de teste unitário de
`apps/workers` e do frontend não se aplicam, porque esta change não toca nenhum
dos dois.

## Risks / Trade-offs

- **[A comparação de tupla do cursor pode não traduzir para SQL como se
  espera]** `(OccurredAt, Id) < (cursorAt, cursorId)` sobre `uuid` e `timestamptz`
  pelo Npgsql: se cair em avaliação no cliente, ou comparar `uuid` com outra
  ordem que a do `ORDER BY`, páginas pulam ou repetem itens. → Mitigação: a forma
  da tradução é verificada na implementação (documentação ou decompilação do
  provider, nunca de memória), e o cenário de desempate na spec cria eventos de
  **mesmo** `OccurredAt` em ordem de inserção oposta à de `Id`, atravessando a
  fronteira de página. É ele que pega a divergência.
- **[Precisão de `OccurredAt`]** O .NET tem resolução de 100 ns e o `timestamptz`
  de 1 µs. Um cursor montado com o valor em memória, e não com o lido do banco,
  compararia um instante que não existe na tabela. → Mitigação: o cursor é
  sempre montado a partir das linhas **lidas** pela consulta, e o cenário de
  paginação atravessa mais de uma página real (mais de 50 eventos) contra o
  Postgres do contêiner.
- **[Duas atualizações concorrentes do mesmo documento]** O documento não tem
  token de concorrência no caminho de atualização (comportamento de hoje, fora
  desta change). Duas escritas simultâneas leem o mesmo estado, e cada uma grava
  o evento da **sua** escrita contra o que leu. → Aceito sem teste: o histórico
  registra as escritas como aconteceram, e a última vence no documento como já
  vence hoje. Corrigir isso exigiria token de concorrência em
  `KnowledgeDocument`, que é mudança de `knowledge-document-catalog`.
- **[Um caminho de recusa novo criado depois desta change, mas depois do `Add` do
  evento]** → Mitigação: os cenários negativos cobrem cada recusa que existe hoje,
  e a D2 fixa a ordem "valida, toca a entidade, adiciona o evento, salva". Recusa
  futura que entrar depois do `Add` aparece como evento sem escrita no cenário
  dela, desde que o cenário afirme também a ausência de evento.
- **[A cascata só é exercida por SQL]** Sem rota de exclusão de base, o cenário
  apaga a linha diretamente. → Aceito: é o que existe para ser verificado. O
  teste afirma a cascata do **banco**, que é onde ela mora.
- **[A leitura do autor estar errada]** Se o endpoint ler o subject de um claim
  que o esquema não emite, a exceção da D6 faz **toda** escrita de documento
  responder `500`. → Mitigação: a leitura é a mesma de
  `ServiceScopeAuthorizationHandler.cs:29`, sobre o claim que
  `OperatorTokenAuthenticationHandler.cs:38` emite. E o cenário "`author` igual a
  `operator`", que roda com o token real emitido pelo login, é o que pega o erro:
  com a leitura errada ele reprova com `500` em vez de passar.
- **[Mudança de assinatura de `KnowledgeDocument.Update`]** Quebra quem chama.
  → Os chamadores são o handler e os testes, todos dentro do `apps/api`. A
  compilação acusa cada um.

## Migration Plan

1. A migração cria `knowledge_document_events`, o FK em cascata para
   `knowledge_bases`, o índice e a `CHECK`. Não toca nenhuma tabela existente, e
   não insere dados.
2. O deploy segue o fluxo de hoje: o bundle de `deploy/migrate` roda antes do
   `apps/api` novo subir. O `apps/workers` não é afetado.
3. **Rollback:** o `Down` da migração derruba a tabela. Os eventos gravados
   entre o deploy e o rollback se perdem, e isso é aceitável porque não há
   consumidor deles antes da #101. O `apps/api` anterior não conhece a tabela e
   sobe normalmente sobre o schema migrado, então o rollback de código não exige
   rollback de schema.
