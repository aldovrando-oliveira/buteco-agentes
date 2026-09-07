## Why

Um agente hoje só sabe o que cabe nas suas `Instructions`. Todo conhecimento
duradouro do negócio — política de troca, tabela de preços, procedimento de
atendimento — precisa ser colado no campo de instruções e reescrito por
inteiro a cada ajuste, sem versão, sem separação por assunto e sem
possibilidade de reuso entre agentes. É o mesmo problema que o catálogo de
servidores MCP resolveu para ferramentas, ainda não resolvido para conteúdo.

Esta é a **etapa 1** da linha de trabalho de bases de conhecimento
(catálogo → vínculo → execução → UI, convenção 1): o catálogo de bases e de
documentos, com extração de markdown. Nada é indexado ainda, e essa ausência é
deliberada — indexação, fragmentos e provedor de embedding são a etapa 2.

Duas rodadas de exploração já fecharam com medição real o que decide o schema
desta etapa: busca lexical pura (`tsvector`/`ts_rank_cd`) foi medida em corpus
real e reprovou — recall@5 de 47-52% mesmo com a query já otimizada pelo
modelo, e o score não é comparável entre consultas, então nenhum limiar separa
acerto de ruído. **Embedding vence, indexação é assíncrona, e portanto
`IndexingStatus` com `Pending` existe desde esta etapa** — inclusive na
ausência de qualquer consumidor.

## What Changes

- **`apps/api` ganha o catálogo de bases de conhecimento** (`KnowledgeBase`:
  `Name`, `Description`, `IsActive`). `Description` não é enfeite de UI: na
  etapa 4 é o texto que o modelo lê para decidir se a base é relevante para a
  pergunta, e a spec diz isso desde já para que a validação e a UI o tratem
  como conteúdo funcional.
- **`apps/api` ganha o catálogo de documentos por base** (`KnowledgeDocument`:
  `KnowledgeBaseId`, `Title`, `SourceType`, `ExtractedText`,
  `ContentLengthBytes`, `IndexingStatus`, `IndexedAt?`, `FailureReason?`,
  `ContentRevision`), com criação, listagem, consulta, atualização e exclusão.
  `ContentLengthBytes` é coluna **gerada pelo Postgres**
  (`GENERATED ALWAYS AS (octet_length("ExtractedText")) STORED`), na mesma
  unidade do teto validado — a aplicação nunca a escreve.
- **Um único `POST` JSON serve os dois caminhos de entrada** (arquivo subido e
  texto digitado). Zero multipart: markdown/`.txt`/`.markdown` são texto e o
  cliente os lê com `FileReader`. Envio de vários arquivos é N chamadas
  independentes ao endpoint unitário, não rota de lote.
- **`SourceType` é ponto de extensão tipo-plugin**: string aberta validada em
  runtime contra extratores registrados via DI keyed, com checagem de
  integridade **bidirecional** no startup — mesmo idioma de
  `Channel.ChannelType`/`ValidateChannelAdapterRegistrations` (convenção 8).
  Único extrator nesta etapa: `markdown`.
- **`IndexingStatus` e `SourceType` atravessam a API como string** desde o
  primeiro dia (`JsonStringEnumConverter<T>` por enum, convenção 12).
- **`DELETE /knowledge-bases/{kbId}/documents/{id}` é o primeiro `MapDelete` do
  repositório.** Decisão de arquitetura registrada com motivo no `design.md`
  (D6), não herdada por inércia: o padrão `IsActive` desta base foi formado
  para entidades de catálogo com vínculos apontando para elas; documento é
  conteúdo, e "continua no banco, invisível" é a resposta errada para o caso
  real (arquivo errado, ou com dado que não devia estar ali). `KnowledgeBase`
  continua seguindo a casa: `IsActive`, sem delete.
- **`ContentRevision` nasce nesta etapa** — coluna cujo consumidor só existe na
  etapa 2, incluída aqui porque é schema e porque o desenho de atualização não
  pode ser fixado sem comportá-la. Motivo e a escolha contra `xmin` estão no
  `design.md` (D7).
- **O fluxo de atualização é implementado aqui** (mesmo shape do `POST`,
  incremento condicional de `ContentRevision`, volta para `Pending`,
  preservação de `IndexedAt`). As **três garantias de reindexação** —
  substituição integral, fragmentos antigos sobrevivendo até o sucesso, e falha
  preservando os antigos — ficam **decididas** em `design.md` (D9), mas
  deliberadamente **fora da spec desta change**: não têm gatilho verificável
  aqui, e requisito que nenhum teste pode reprovar é o padrão de falha que a
  convenção 10 nomeia. Elas entram como *ADDED requirements* da etapa 2, que as
  herda decididas em vez de as redecidir.
- **Nesta etapa o documento nasce `Pending` e permanece `Pending`
  indefinidamente**, porque não existe fila nem consumidor. Isso está dito na
  spec como estado esperado, para não ser descoberto como defeito na etapa 2.

Sem UI, sem vínculo com agente, sem tool, sem fragmento, sem embedding.

## Capabilities

### New Capabilities

- `knowledge-base-catalog`: cadastro, listagem, consulta, edição e
  ativação/desativação de bases de conhecimento em `apps/api`; `Description`
  como texto funcional destinado ao modelo.
- `knowledge-document-catalog`: cadastro, listagem, consulta, atualização e
  exclusão de documentos dentro de uma base; extração de conteúdo; contrato do
  ciclo de vida de `IndexingStatus` e de `ContentRevision`; tetos de tamanho.
- `knowledge-source-extractor-plugin`: contrato de extrator por `SourceType`,
  resolvido via DI keyed, com checagem de integridade bidirecional no startup.
  Capability própria pelo mesmo motivo que `inbox-channel-adapter-plugin` é
  separada de `inbox-channel-catalog` — o ponto de extensão tem contrato e
  garantia de boot próprios, independentes do catálogo que o consome.

### Modified Capabilities

Nenhuma. Esta change não altera requisito de nenhuma capability existente:
não toca `Agent`, `McpServer`, o protocolo A2A, nem `apps/inbox`.

## Impact

- **`apps/api`**: duas entidades novas (`KnowledgeBase`, `KnowledgeDocument`),
  uma migração, dois grupos de endpoints (`/knowledge-bases` e
  `/knowledge-bases/{kbId}/documents`), comandos/queries CQRS no molde de
  `McpServers`/`AgentDelegations`, contrato `IKnowledgeSourceExtractor` com
  registro keyed e checagem de startup, e o primeiro `MapDelete` do
  repositório.
- **`apps/workers`**: apenas o espelho de EF Core das duas entidades novas e a
  migração equivalente — os dois `AppDbContext` são mantidos sincronizados por
  disciplina, e o par de migrações é parte desta change mesmo sem nenhum
  código de `apps/workers` ler essas tabelas ainda. Nenhuma mudança em
  `AgentExecutionService`, nos resolvedores de tools ou no consumidor de fila.
- **`apps/frontend`**: nenhuma mudança (etapas 5a/5b/5c).
- **`apps/inbox`**: nenhuma mudança; banco próprio, sem tabela em comum.
- **Rotas**: todas sob autenticação de operador por padrão — nenhuma entrada
  nova na allowlist de rotas anônimas.
- **Dependências**: nenhum pacote novo. A coluna vetorial e a decisão sobre
  `Pgvector.EntityFrameworkCore` pertencem à etapa 2.
- **Sequenciamento**: esta change vem depois de `0a` (dedupe global de nome de
  tool), que é carve de defeito pré-existente de MCP/delegação e não depende de
  nada de conhecimento.
