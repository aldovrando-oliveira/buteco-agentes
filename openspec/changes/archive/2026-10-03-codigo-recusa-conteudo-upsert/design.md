## Context

A #105 (reconciliação do `apps/connectors`) vai mandar cada arquivo da pasta para o
upsert de `/sync` e, quando o `apps/api` recusar, registrar o arquivo como ignorado
com um **código** (D1 da `catalogo-base-sincronizada`, #102). Hoje a recusa não
tem código. O que foi lido na `main` (`2cb1f5c`) antes de propor:

**O caminho da recusa.**

- `KnowledgeContentProcessor.Process`
  (`apps/api/src/Buteco.Api/KnowledgeDocuments/Extraction/KnowledgeContentProcessor.cs`)
  devolve `KnowledgeContentResult.Invalid(chave, frase)`, um
  `Dictionary<string, string[]>` de uma entrada.
- `UpsertSyncedDocumentCommandHandler.cs:55-59` repassa o dicionário em
  `UpsertSyncedDocumentResult.Invalid`.
- `KnowledgeSyncEndpoints.cs:116-119` responde
  `TypedResults.ValidationProblem(result.ValidationErrors)`.
- As rotas do operador fazem o mesmo: `CreateKnowledgeDocumentCommandHandler.cs:41-44`
  e `UpdateKnowledgeDocumentCommandHandler.cs:45-48` chamam o mesmo processador, e
  `KnowledgeDocumentEndpoints.cs:82-85` e `:150-153` respondem
  `ValidationProblem` com o dicionário.

**As recusas de conteúdo que o upsert de `/sync` pode devolver** — todas nascem no
processador ou no extrator que ele chama, e todas saem hoje como `400` com frase:

| # | recusa | onde nasce | chave em `errors` |
|---|---|---|---|
| 1 | texto extraído acima de 1 MiB | `KnowledgeContentProcessor.cs:43-48` | `content` |
| 2 | `sourceType` sem extrator registrado | `KnowledgeContentProcessor.cs:29-34` | `sourceType` |
| 3 | conteúdo com U+0000 | `MarkdownSourceExtractor.cs:25-29`, repassado em `KnowledgeContentProcessor.cs:36-40` | `content` |
| 4 | conteúdo vazio depois da normalização | `MarkdownSourceExtractor.cs:42-45`, repassado em `KnowledgeContentProcessor.cs:36-40` | `content` |
| 4' | conteúdo `""` ou só de espaços | validação de **forma**, `KnowledgeDocumentEndpoints.cs:299-302` (`ValidateShape`), chamada pelo upsert em `KnowledgeSyncEndpoints.cs:78` | `content` |

A 4 e a 4' são **a mesma regra em dois lugares**, com a mesma frase ("O conteúdo do
documento é obrigatório e não pode ser vazio."). A 4' pega `""` e espaços; a 4 pega
o que só fica vazio depois de normalizar, por exemplo um conteúdo que é só o BOM
(`'﻿'` não é espaço para `char.IsWhiteSpace`, então passa a 4' e cai na 4).

O resto do handler do upsert não recusa conteúdo: devolve `404`, `409`, `503` ou
`200`. A entidade não tem `HasMaxLength` em título nem em conteúdo
(`AppDbContext.cs`, bloco de `KnowledgeDocument`).

**As recusas de forma do upsert** (`KnowledgeSyncEndpoints.cs:78-94`): `title`,
`sourceType` e `content` ausentes ou em branco (`ValidateShape`), `externalRef` e
`externalVersion` ausentes ou em branco. Todas no mesmo `ValidationProblem`, antes
do comando.

**O padrão de erro com código do `apps/api`** (#104):
`TypedResults.Problem(..., extensions: { ["code"] = ... })`, em
`KnowledgeBaseEndpoints.cs:87-109` (`folder-in-use` e o repasse do
`apps/connectors`). O formato do código é o de `SyncCode`
(`KnowledgeSync/SyncCode.cs`).

**O que o frontend lê hoje.** O `request<T>` da feature de bases
(`apps/frontend/src/features/knowledge-bases/api/knowledgeBasesApi.ts:50-57`) monta
o `ApiError` com `message = problem.title`, e o modal de documento mostra
`error.message` (`KnowledgeDocumentModal.tsx:34-36`, usado em `:176` e `:208`). O
`title` de um `ValidationProblem` sem título explícito é o padrão do ASP.NET,
"One or more validation errors occurred.". **O modal não lê `errors.content`**: o
operador que sobe um arquivo de 2 MiB vê a frase genérica em inglês, não a frase
do teto. Isso corrige uma premissa do enunciado da change, que dizia que o
frontend "lê a mensagem" (D4).

**A API do framework, conferida na DLL de referência** (convenção 6):
`Microsoft.AspNetCore.App.Ref/10.0.9` declara
`TypedResults.ValidationProblem(IDictionary<string,string[]> errors, string detail,
string instance, string title, string type, IDictionary<string,object> extensions)`.
A extensão cabe no `ValidationProblem` sem trocar de tipo de resposta.

## Goals / Non-Goals

**Goals:**

- Toda recusa de conteúdo do upsert de `/sync` responde com um código estável no
  formato da D1 da #102, lido do texto da resposta HTTP.
- A recusa de forma continua distinguível da de conteúdo, sem que o cliente leia
  frase.
- Nenhuma asserção dos testes atuais das rotas do operador muda.

**Non-Goals:**

- Ler o código no `apps/connectors` ou gravar o arquivo ignorado: é a #105.
- Mudar o que o painel mostra: é a #131, aberta pela D4.
- Dar código às recusas de forma, ou a outros `400` do `apps/api`.
- Tornar o teto configurável.

## Decisions

### D1. A change cobre as quatro recusas de conteúdo, não só o tamanho

| recusa | código | `detail` | extensões além de `code` |
|---|---|---|---|
| acima do teto | `too-large` | a frase de hoje, com o tamanho e o teto | `contentBytes`, `maxContentBytes` (números) |
| `sourceType` sem extrator | `unsupported-source-type` | — | — |
| U+0000 | `null-character` | — | — |
| vazio | `empty-content` | — | — |

A #105 recebe as quatro. Um Google Doc vazio exporta markdown vazio e cai na 4/4';
um `.md` com NUL cai na 3; a 2 não deveria acontecer, porque o conector manda
sempre `markdown`, mas é o que o `apps/api` responde se acontecer. Com código só
no `too-large`, a #105 teria de ler a frase das outras três, que é o que a #120 e a
D1 da #102 proíbem.

**`unsupported-source-type`, e não `unsupported-type`:** `unsupported-type` já é
código do `apps/connectors` na D1 da #102, para o tipo de **arquivo** que o
conector não sabe converter (decisão dele, antes de chamar o upsert). O do
`apps/api` é sobre o `sourceType` do contrato — o extrator. Os dois motivos
chegariam à mesma lista de ignorados, e o mesmo código para os dois esconderia de
quem lê qual app recusou.

**Os códigos ficam numa classe só do `apps/api`**, `KnowledgeContentRefusalCodes`,
ao lado do processador. Não há lista fechada do lado de quem lê (D1 da #102: o
consumidor aceita qualquer código no formato), então um extrator futuro com motivo
novo acrescenta uma constante aqui sem mudar a #105. Um teste afirma que toda
constante passa em `SyncCode.IsValid`.

- *Descartado, só o `too-large`:* é o mínimo da #120, e deixaria três recusas sem
  código no mesmo caminho. A #105 nasceria lendo frase em três casos.
- *Descartado, um código genérico `invalid-content` para as três outras:* a #105
  gravaria o mesmo motivo para um arquivo vazio e um com NUL, e o operador (#107)
  receberia a mesma explicação para os dois, que pedem ações diferentes.

**Escopo cresceu em relação à #120** (uma recusa → quatro, e as rotas do operador
pela D4): registrado por comentário na issue.

### D2. Forma × conteúdo: o `code` só existe na recusa de conteúdo

O cliente distingue pela **presença** da extensão `code`. A recusa de forma
continua `ValidationProblemDetails` sem `code`, como hoje.

O que torna isso uma distinção útil, e não só uma convenção, é a natureza das duas:
recusa de forma é **defeito de quem chama** (o conector mandou payload sem
`externalRef`), e não é propriedade de nenhum arquivo; recusa de conteúdo é
**propriedade do arquivo** e é o que vai para a lista de ignorados. A #105 trata a
primeira como erro do ciclo e a segunda como arquivo ignorado.

**Consequência que a decisão exige — o conteúdo vazio sai da forma.** Hoje
`ValidateShape` recusa `content` `""` e só de espaços como forma (4' da tabela do
Context). **O caso que decide:** um Google Doc vazio numa pasta sincronizada
exporta `""`. O conector manda esse `""` no upsert, e com a regra de hoje a
resposta é recusa de **forma**, sem código. A #105 não conseguiria registrá-lo
como arquivo ignorado sem ler a frase, e pela regra desta D2 o trataria como
defeito do próprio payload, quando é propriedade do arquivo. Então:

- `ValidateShape` passa a recusar só `content` **ausente** (`null`) — esse sim é
  defeito do payload;
- `""` e só de espaços seguem para o processador, e o extrator já os recusa com a
  **mesma frase**, agora com `empty-content`.

`title` e `sourceType` em branco continuam forma: não são propriedade do arquivo,
o conector os compõe.

**Efeito colateral nas rotas do operador, aceito:** conteúdo em branco passava a
ser recusado **antes** de procurar a base; agora é recusado depois. Para base
inexistente com conteúdo em branco, a resposta passa de `400` a `404`; para base
`Synced`, de `400` a `409`. É a ordem que as outras recusas de conteúdo (teto, NUL)
já seguem hoje. Os testes atuais de conteúdo vazio usam base existente e afirmam
só `400` (`KnowledgeDocumentCatalogTests.cs:116-129`), e continuam passando.

- *Descartado, um código também na recusa de forma (`invalid-payload`):* o cliente
  teria de conhecer a lista dos códigos de conteúdo para saber o que é arquivo
  ignorado, e um código de conteúdo novo exigiria mudar a #105. Com a presença como
  sinal, a lista fica aberta, que é a D1 da #102.
- *Descartado, manter o vazio como forma e mapear a chave `content` para
  `empty-content` no endpoint do `/sync`:* o mesmo `ValidationProblem` pode trazer
  `title` e `content` juntos, e a resposta teria código e forma misturados; e a
  regra do vazio continuaria em dois lugares.

### D3. Formato: `ValidationProblemDetails` com a extensão `code`, nada removido

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "O conteúdo do documento tem 1048577 bytes e excede o limite de 1048576 bytes.",
  "errors": { "content": ["O conteúdo do documento tem 1048577 bytes e excede o limite de 1048576 bytes."] },
  "code": "too-large",
  "contentBytes": 1048577,
  "maxContentBytes": 1048576
}
```

- **`code`** é o sinal. Está no nível de cima, como no `folder-in-use` da #104.
- **`errors`** fica como está, com a chave e a frase de hoje. É o que mantém o
  contrato das rotas do operador inalterado, e é o que um leitor humano da
  resposta crua procura.
- **`detail`** só no `too-large`, porque é a única recusa com dado além do código.
  O tamanho e o teto também vão como **números** (`contentBytes`,
  `maxContentBytes`): quem precisar deles não lê frase.
- **`title`** fica o padrão do framework. Trocá-lo mudaria o que o painel mostra
  hoje (D4), e isso é da #131.

Uma recusa por resposta: o processador para na primeira, então há um `code` só.

- *Descartado, `ProblemDetails` sem `errors` (a forma exata da #104):* removeria
  `errors.content` e `errors.sourceType` do contrato das rotas do operador, que é
  mudança de contrato, não acréscimo.
- *Descartado, o código dentro de `errors` (por exemplo
  `errors.content: ["too-large"]`):* troca a frase de hoje por código no campo que
  é lido como mensagem, e mistura os dois papéis no mesmo valor.
- *Descartado, só a frase no `detail`, sem os números:* quem quisesse o tamanho
  teria de extraí-lo da frase.

### D4. As rotas do operador levam o mesmo código

`POST /knowledge-bases/{id}/documents` e `PUT /knowledge-bases/{id}/documents/{docId}`
respondem a mesma recusa, com o mesmo `code`. A recusa nasce no processador
compartilhado; uma resposta só, montada num lugar só
(`KnowledgeDocumentEndpoints.ContentRefused`), usada pelas três rotas.

**Efeito no frontend atual: nenhum.** O painel lê só o `title`, que não muda; a
extensão nova é ignorada. **Mas a leitura revelou um achado que não é desta
change:** o modal de documento mostra o `title` genérico em inglês para toda
recusa de conteúdo, e nunca o motivo. Com o `code` servido, o painel pode
escolher a mensagem pelo código, como a #106 faz no cadastro de base. É mudança em
`apps/frontend`, então **não** entra aqui (convenção 1): foi aberta como **#131**,
`blocked-by` #120, em `Backlog`.

- *Descartado, código só no `/sync`:* o endpoint do `/sync` teria de saber qual
  recusa aconteceu para pôr o código, e o operador continuaria sem código no
  mesmo motivo. Duas montagens da mesma recusa divergem com o tempo.

### D5. O código anda tipado do extrator até o endpoint

- `ExtractionResult.Failure` passa a receber o código junto com a frase. O
  contrato de extrator (`IKnowledgeSourceExtractor`) é ponto de extensão: um
  extrator futuro de PDF declara os próprios motivos.
- `KnowledgeContentResult` troca o `Dictionary<string, string[]>` por uma
  `KnowledgeContentRefusal` (chave, frase, código, e os números do teto quando é o
  teto).
- Os três tipos de resultado (`CreateKnowledgeDocumentResult`,
  `UpdateKnowledgeDocumentResult`, `UpsertSyncedDocumentResult`) carregam a
  `KnowledgeContentRefusal` em vez do dicionário, e os endpoints chamam
  `ContentRefused` em vez de `ValidationProblem` direto.

O teto continua sendo `KnowledgeDocumentLimits.MaxContentBytes`, e
`maxContentBytes` é lido dele. Nenhuma cópia nova, nos testes inclusive: o teste
compara com a constante.

- *Descartado, o endpoint deduzir o código da frase ou da chave:* `content` serve a
  três recusas diferentes; e deduzir de frase é o defeito que a change corrige.

### Árvore de arquivos

```
apps/api/src/Buteco.Api/
├── KnowledgeDocuments/
│   ├── Extraction/
│   │   ├── ExtractionResult.cs                 (código na falha)
│   │   ├── MarkdownSourceExtractor.cs          (null-character, empty-content)
│   │   ├── KnowledgeContentProcessor.cs        (KnowledgeContentRefusal; too-large, unsupported-source-type)
│   │   └── KnowledgeContentRefusalCodes.cs     (novo: as quatro constantes)
│   ├── Commands/CreateKnowledgeDocument/CreateKnowledgeDocumentResult.cs
│   ├── Commands/CreateKnowledgeDocument/CreateKnowledgeDocumentCommandHandler.cs
│   ├── Commands/UpdateKnowledgeDocument/UpdateKnowledgeDocumentResult.cs
│   ├── Commands/UpdateKnowledgeDocument/UpdateKnowledgeDocumentCommandHandler.cs
│   └── Endpoints/KnowledgeDocumentEndpoints.cs (ValidateShape; ContentRefused)
└── KnowledgeSync/
    ├── Commands/UpsertSyncedDocument/UpsertSyncedDocumentResult.cs
    ├── Commands/UpsertSyncedDocument/UpsertSyncedDocumentCommandHandler.cs
    └── Endpoints/KnowledgeSyncEndpoints.cs

apps/api/tests/Buteco.Api.Tests/Knowledge/
├── KnowledgeDocumentCatalogTests.ContentRefusal.cs  (novo arquivo da classe partial existente:
│                                                     forma do fio nas três rotas)
├── KnowledgeContentRefusalCodesTests.cs              (novo, sem contêiner: formato dos códigos)
├── MarkdownSourceExtractorTests.cs                   (código da falha; sem contêiner)
└── KnowledgeDocumentCatalogTests.Synced.cs           (o teste do teto ganha o código)
```

Nada em `libs/`. Nenhuma migração. **Nenhuma classe nova de teste com contêiner.**
Cada classe com `IClassFixture<ApiFactoryFixture>` sobe o próprio Postgres
(`KnowledgeDocumentCatalogTests.cs:16`), então os testes de integração novos entram
num arquivo novo da classe `partial` `KnowledgeDocumentCatalogTests`, que é o molde
do `KnowledgeDocumentCatalogTests.Synced.cs`. As duas classes novas de fato
(`KnowledgeContentRefusalCodesTests` e as asserções em
`MarkdownSourceExtractorTests`) são unitárias, sem fixture. Se a implementação
precisar de uma classe nova com contêiner, para e pede autorização.

## Risks / Trade-offs

- **[Teste que desserializa a resposta no mesmo tipo é cego ao nome no fio]**
  (convenção 12) → os testes leem o **texto** da resposta com `JsonDocument` e
  procuram `code` no nível de cima, nunca por um tipo C# do próprio `apps/api`.
  Cenário: "Recusa de tamanho no upsert tem o código no texto".
- **[O guarda do `too-large` passar verde com a resposta de hoje]** (convenção 15)
  → tarefa própria: reintroduzir a resposta só com frase e ver o teste reprovar
  antes de manter a correção.
- **[Recusa de forma ganhar um código de conteúdo por acidente, e a #105 registrar
  defeito do conector como arquivo ignorado]** → asserção **negativa**: o upsert
  sem `externalRef` responde `400` sem a propriedade `code`. Cenário: "Recusa de
  forma não tem código".
- **[Mudar o contrato das rotas do operador sem perceber]** → os testes atuais
  dessas rotas rodam sem mudança de asserção, e o teste novo afirma que `errors` e
  `title` continuam presentes junto com o `code`. Cenário: "A recusa do operador
  continua com `errors` e ganha o código".
- **[Uma recusa de conteúdo nova sem código]** — um extrator futuro, ou uma
  checagem nova no processador → o tipo da falha exige o código
  (`ExtractionResult.Failure` e `KnowledgeContentRefusal` não têm construtor sem
  ele), então o compilador barra. Não há teste para isso: é garantia de tipo, e o
  teste que todo código passa em `SyncCode.IsValid` cobre o formato.
- **[Conteúdo em branco muda de `400` para `404`/`409` com base inexistente ou
  `Synced`]** (D2) → aceito, e coberto: cenário "Conteúdo em branco é recusa de
  conteúdo", e os testes atuais de conteúdo vazio continuam com `400`.
- **[O `upsert` dentro do teto mudar de comportamento]** → cenário e teste: `200`
  com o `outcome` de hoje.

## Migration Plan

Sem migração de dados. O deploy do `apps/api` antes da #105 é seguro: nenhum
consumidor lê o `code` ainda, e nada que já estava na resposta sai. Reverter é
reverter o código; o contrato volta a ser o de hoje sem efeito em quem não lê a
extensão.

## Open Questions

Nenhuma.
