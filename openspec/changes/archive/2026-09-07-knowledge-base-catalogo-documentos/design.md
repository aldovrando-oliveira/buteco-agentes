## Context

Etapa 1 de cinco na linha de trabalho de bases de conhecimento. Duas rodadas de
exploração precederam esta change e fecharam com evidência real (medição em
Postgres 18.4, spike compilado e executado, decompilação de SDK, leitura de
código) as decisões que definem o schema desta etapa. Este documento registra
essas decisões com o motivo, e resolve as que ficaram explicitamente em aberto
para cá.

**O que já está fechado e não é reaberto aqui:**

- Busca lexical pura reprovou por medição (recall@5 de 47-52% em corpus real de
  419 fragmentos, com a query já reescrita como um modelo a emitiria; e
  `ts_rank_cd` não é comparável entre consultas — o score do topo da pergunta
  *sem resposta no corpus* ficou acima do de uma recuperação correta, então
  nenhum limiar separa acerto de ruído). **Embedding vence; indexação é
  assíncrona; `Pending` existe.**
- `pgvector` 0.8.6, busca exata com btree em `KnowledgeBaseId` (55-85 ms sobre
  7.500 fragmentos), sem índice ANN — etapa 2.
- Fragmentos, `ContentHash`, contagem de fragmentos, fila e consumidor de
  indexação — etapa 2.

**Restrições do repositório em jogo:** isolamento estrito entre apps (sem
`ProjectReference` cruzado); `apps/api` e `apps/workers` compartilham o mesmo
Postgres com dois `AppDbContext` sincronizados por disciplina; CQRS via
`Mediator` em `apps/api`; toda rota exige token de operador por padrão.

**Nenhuma versão nova a fixar.** Esta change não adiciona pacote algum: usa
`.NET 10`, EF Core `10.0.10` e `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3`
já pinados em `Directory.Packages.props`. A decisão sobre a coluna vetorial e
sobre `Pgvector.EntityFrameworkCore` pertence à etapa 2 e já tem spike rodado,
mas não entra aqui.

## Goals / Non-Goals

**Goals:**

- Catálogo de bases e de documentos em `apps/api`, com extração de markdown.
- Fixar o **contrato** do ciclo de vida de indexação (`IndexingStatus`,
  `IndexedAt`, `FailureReason`, `ContentRevision`) e do fluxo de atualização,
  para que a etapa 2 implemente um desenho já decidido em vez de decidi-lo sob
  pressão de implementação.
- Ponto de extensão de extrator por `SourceType` com garantia de boot.
- Espelho EF Core em `apps/workers` e par de migrações.

**Non-Goals:**

- Fragmentos, chunking, coluna vetorial, provedor de embedding, fila e
  consumidor de indexação, checagem de startup do modelo de embedding — etapa 2.
- `ContentHash` e coluna de contagem de fragmentos — etapa 2 (convenção 2:
  coluna sem cenário nesta etapa).
- Vínculo agente ↔ base (etapa 3), resolvedor de tool (etapa 4), UI (5a/5b/5c).
- Multipart e tipos binários — gatilho registrado em D3, não omissão.
- Testes de frontend: **não há mudança de frontend nesta change**, logo não há
  teste de frontend. A regra de "testes unitários em frontend e backend" é
  atendida por `apps/api` e `apps/workers`; a ausência do terceiro é escopo, e
  a UI traz os seus nas etapas 5a/5b/5c.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
  KnowledgeBases/
    Commands/
      CreateKnowledgeBase/          CreateKnowledgeBaseCommand.cs
                                    CreateKnowledgeBaseCommandHandler.cs
      UpdateKnowledgeBase/          UpdateKnowledgeBaseCommand.cs
                                    UpdateKnowledgeBaseCommandHandler.cs
      ActivateKnowledgeBase/        ActivateKnowledgeBaseCommand.cs
                                    ActivateKnowledgeBaseCommandHandler.cs
      DeactivateKnowledgeBase/      DeactivateKnowledgeBaseCommand.cs
                                    DeactivateKnowledgeBaseCommandHandler.cs
    Queries/
      ListKnowledgeBases/           ListKnowledgeBasesQuery.cs
                                    ListKnowledgeBasesQueryHandler.cs
      GetKnowledgeBaseById/         GetKnowledgeBaseByIdQuery.cs
                                    GetKnowledgeBaseByIdQueryHandler.cs
    Entities/                       KnowledgeBase.cs
    Endpoints/                      KnowledgeBaseEndpoints.cs
    Requests/                       CreateKnowledgeBaseRequest.cs
                                    UpdateKnowledgeBaseRequest.cs
    Responses/                      KnowledgeBaseResponse.cs

  KnowledgeDocuments/
    Commands/
      CreateKnowledgeDocument/      CreateKnowledgeDocumentCommand.cs
                                    CreateKnowledgeDocumentCommandHandler.cs
                                    CreateKnowledgeDocumentResult.cs
      UpdateKnowledgeDocument/      UpdateKnowledgeDocumentCommand.cs
                                    UpdateKnowledgeDocumentCommandHandler.cs
                                    UpdateKnowledgeDocumentResult.cs
      DeleteKnowledgeDocument/      DeleteKnowledgeDocumentCommand.cs
                                    DeleteKnowledgeDocumentCommandHandler.cs
    Queries/
      ListKnowledgeDocuments/       ListKnowledgeDocumentsQuery.cs
                                    ListKnowledgeDocumentsQueryHandler.cs
      GetKnowledgeDocumentById/     GetKnowledgeDocumentByIdQuery.cs
                                    GetKnowledgeDocumentByIdQueryHandler.cs
    Entities/                       KnowledgeDocument.cs
                                    KnowledgeIndexingStatus.cs
    Extraction/                     IKnowledgeSourceExtractor.cs
                                    MarkdownSourceExtractor.cs
                                    KnowledgeSourceTypes.cs
                                    ExtractionResult.cs
                                    KnowledgeContentProcessor.cs
                                    KnowledgeExtractorRegistrationExtensions.cs
    Endpoints/                      KnowledgeDocumentEndpoints.cs
    Requests/                       CreateKnowledgeDocumentRequest.cs
                                    UpdateKnowledgeDocumentRequest.cs
    Responses/                      KnowledgeDocumentResponse.cs
                                    KnowledgeDocumentSummaryResponse.cs
    Options/                        KnowledgeDocumentLimits.cs

  Infrastructure/Migrations/        <timestamp>_AddKnowledgeBaseCatalog.cs

apps/workers/src/Buteco.Workers/
  Knowledge/Entities/               KnowledgeBase.cs
                                    KnowledgeDocument.cs
                                    KnowledgeIndexingStatus.cs
  Infrastructure/Migrations/        <timestamp>_AddKnowledgeBaseCatalog.cs

apps/api/tests/Buteco.Api.Tests/
  Knowledge/                        KnowledgeBaseCatalogTests.cs
                                    KnowledgeDocumentCatalogTests.cs
                                    KnowledgeDocumentUpdateTests.cs
                                    KnowledgeDocumentDeleteTests.cs
                                    KnowledgeEmptyCatalogTests.cs
                                    KnowledgeRouteAuthenticationTests.cs
                                    MarkdownSourceExtractorTests.cs
                                    KnowledgeExtractorStartupValidationTests.cs
                                    KnowledgeProductionRegistrationTests.cs
                                    KnowledgeWireFormatTests.cs
                                    KnowledgeTestClient.cs

apps/workers/tests/Buteco.Workers.Tests/
  Knowledge/                        KnowledgeSchemaMirrorTests.cs
```

> *Ajustado durante a implementação (convenção 9), em cinco pontos:*
>
> - **`KnowledgeContentProcessor` não estava previsto.** Resolve o extrator,
>   extrai e aplica o teto, num lugar só. Tem exatamente dois consumidores reais
>   nesta change (criação e atualização), que é a régua da convenção 2 — e
>   duplicar a *ordem das operações* de D5 nos dois handlers seria justamente o
>   jeito de os dois divergirem depois.
> - **`KnowledgeExtractorMisregistrationFixture` não existe.** Ver D12: a
>   checagem roda sobre `IServiceCollection` antes do `Build()`, e uma
>   `WebApplicationFactory` só consegue injetar serviços **depois** que
>   `Program.cs` registrou os seus e já rodou o validador — nenhuma das duas
>   divergências seria observável por aquele caminho. Os testes exercitam a
>   extensão diretamente, no mesmo ponto do ciclo em que a aplicação a chama.
> - **`KnowledgeBaseSummaryResponse` não foi necessário** — a listagem de bases
>   devolve os mesmos campos do `GET` por id (a base não tem campo caro como o
>   `ExtractedText` do documento), então um segundo tipo seria duplicação sem
>   cenário.
> - **`KnowledgeProductionRegistrationTests` não estava previsto** — fecha a
>   lacuna que a forma `IServiceCollection` da checagem abre. Ver D12.
> - **Três arquivos de teste a mais**: `KnowledgeEmptyCatalogTests` (classe
>   própria porque o cenário de catálogo vazio exige um banco sem nenhuma base,
>   e cada classe recebe seu próprio container), `KnowledgeRouteAuthenticationTests`
>   e `KnowledgeTestClient` (só arranjo compartilhado, nenhuma asserção).

Nada em `libs/`. A convenção 2 pede 2-3 consumidores reais antes de extrair, e
aqui há um só produtor (`apps/api`) e um espelho passivo (`apps/workers`) — o
mesmo padrão já usado para `McpServer`, cuja entidade é duplicada nos dois apps
sem lib compartilhada.

## Decisions

### D1 — Duas tabelas relacionais próprias, não jsonb

`KnowledgeBase` e `KnowledgeDocument` são tabelas com identidade própria, não
uma coleção jsonb dentro da base. A régua da casa (convenção 2) é: jsonb quando
não há necessidade de FK/consulta relacional, tabela quando os dois lados são
entidades com identidade própria. Documento tem identidade (é endereçável,
editável e excluível individualmente), terá filhos na etapa 2
(`KnowledgeFragment`), e sua listagem precisa de projeção sem trazer o conteúdo.

*Alternativa considerada:* documentos como jsonb em `KnowledgeBase`, no molde de
`AgentMcpServer.AllowedTools`. Recusada: `AllowedTools` é jsonb porque tools não
são catálogo persistido em lugar nenhum (são descobertas ao vivo); documento é o
oposto — é justamente o catálogo.

### D2 — Um `POST` JSON serve os dois caminhos de entrada

`POST /knowledge-bases/{kbId}/documents` recebe `{ title, sourceType, content }`.
O modal do protótipo tem duas abas ("Subir arquivos" e "Escrever manualmente"),
mas ambas produzem o mesmo corpo: o cliente lê o arquivo com `FileReader` e
manda o texto. O servidor não sabe — e não deve saber — de qual aba veio.

Título derivado do nome do arquivo é **sugestão editável no cliente**; o
servidor sempre recebe título explícito. Isso mantém um único contrato de
entrada por documento para os dois caminhos.

*Verificado no código:* não existe `IFormFile`, multipart, `FormOptions` nem
`RequestSizeLimit` em `apps/api` ou `apps/inbox`, e o `request<T>` de cada
feature do frontend fixa `'Content-Type': 'application/json'` no cabeçalho —
multipart obrigaria a sobrescrevê-lo (e removê-lo, para o browser gerar o
boundary) em cada feature.

### D3 — Zero multipart, com gatilho registrado

Os três formatos aceitos (`.md`, `.markdown`, `.txt`) são texto. **Multipart
nasce com o primeiro tipo binário (PDF), junto com o extrator que precisa dos
bytes** — não antes. Isso é decisão com gatilho, não omissão: quando houver um
`SourceType` cujo extrator opere sobre bytes e não sobre string, o contrato de
entrada ganha um segundo caminho, e é essa change que paga o custo de
`IFormFile` + limite de multipart + validação de tipo binário.

O custo evitado é dimensionado: manter a etapa 1 perto de `b5df504` (catálogo +
vínculo de delegação, 30 arquivos / 1593 linhas) em vez de `da31b27` (catálogo +
vínculo MCP com handshake, 76 arquivos / 3847).

### D4 — Envio de vários arquivos é N chamadas independentes, não rota de lote

O único precedente de lote da casa são `PUT /agents/{id}/mcp-servers` e
`PUT /agents/{id}/delegations`, e ambos são **replace-all tudo-ou-nada**:
`ReplaceAgentMcpServersCommandHandler` retorna no primeiro `InvalidIds`/
`HandshakeFailed`/`InvalidToolsFound` sem salvar nada. Uma rota de lote com
resposta **por item** não tem precedente algum aqui e exigiria um shape de
resposta heterogêneo que o `ApiError`/`ValidationProblemDetails` do frontend não
sabe ler.

N chamadas ao `POST` unitário reusam o endpoint que a aba manual já precisa, e a
falha parcial aparece onde a listagem já a mostra: **uma linha por documento,
com estado e ações próprias**. Documento que falhou não arrasta os que deram
certo.

*Consequência a corrigir no protótipo (convenção 17):* o rodapé do modal
("2 documentos serão criados e entram como pendentes") promete atomicidade que
nenhum dos desenhos sustenta. Corrige-se o texto do protótipo, não o backend.
Isso é achado a repassar para a etapa 5a, não trabalho desta change.

### D5 — Teto de 1 MiB por documento, validado no handler

`content` limitado a **1.048.576 bytes em UTF-8**, rejeitado com HTTP 400 e
`ValidationProblemDetails`.

O número não vem do framework: o default do Kestrel é `30000000L` (28,6 MiB) —
decompilado de `KestrelServerLimits` em `Microsoft.AspNetCore.Server.Kestrel.Core`
10.0.9 — e o repositório **não configura nada**, então é ele que vale hoje. A
sobrecarga de codificação JSON foi medida nos arquivos reais que o protótipo
usou: `01-ARQUITETURA_E_CONVENCOES.md` (26,3 KB → 29,4 KB, +11,9%) e
`02-HISTORICO_E_STATUS.md` (79,2 KB → 88,0 KB, +11,1%). Ou seja, ~355 arquivos
de 80 KB caberiam num único corpo — o limite do Kestrel não é o que importa.

1 MiB é teto de **produto**, não de transporte: é ~12× o maior documento real
observado, e mantém o custo de embedding de um único documento previsível na
etapa 2. Medir em bytes UTF-8 (não em caracteres) porque é o que o cliente
consegue calcular antes de enviar (`Blob.size`/`TextEncoder`) e o que mapeia
para armazenamento.

**Ordem de operações: extrair primeiro, validar o teto depois, sobre o texto
extraído.** A extração normaliza (remove BOM, `CRLF` → `LF`, ver D11), e isso
encolhe o conteúdo — 3 bytes do BOM, mais um byte por linha. Validar a entrada
crua e expor o tamanho do texto extraído faria os dois números medirem strings
diferentes, e um documento com muitas linhas poderia ser rejeitado a 1 MiB bruto
enquanto o que seria persistido cabia. Validar depois da extração faz o teto e
o `contentLengthBytes` exposto medirem **exatamente a mesma string**. Extração é
só manipulação de string, e o corpo já está limitado pelo Kestrel, então
extrair antes de checar o tamanho não abre superfície nova.

**A unidade é parte do contrato, não detalhe de implementação** (mesma família
da convenção 12, que já mordeu com formato de fio de enum). O campo exposto na
listagem é `contentLengthBytes`, na **mesma unidade** desta validação — ver D14.
Expor caracteres aqui e validar bytes faria a tela mostrar um número contra o
qual o servidor não valida: em português acentuado a divergência é material
(1.048.576 bytes ≈ 950 mil caracteres), e o operador não conseguiria saber
quanto falta para o teto olhando a tela.

*Por que validar no handler e não só confiar no Kestrel:* estourar o limite do
Kestrel devolve **413 sem corpo**, e o `request<T>` do frontend cai no
`catch(() => undefined)` e mostra "Erro 413" cru. 400 com
`ValidationProblemDetails` é mensagem que a UI sabe renderizar.

Sem teto por base (número de documentos) e sem teto por requisição: convenção 2
— não há cenário real de alguém precisar de outro valor, e N chamadas
independentes (D4) já eliminam a requisição gigante.

### D6 — `DELETE` real de documento; `IsActive` na base. Primeiro `MapDelete` do repositório

**Verificado:** `grep -rn "MapDelete"` em `apps/api/src` e `apps/inbox/src`
retorna **zero ocorrências**. `McpServerEndpoints` expõe
`POST /{id}/activate` e `POST /{id}/deactivate`, e o filtro vive no momento da
resolução (`where binding.AgentId == agentId && server.IsActive` em
`McpToolSetResolver`).

Esta change diverge disso para documento, conscientemente:

- O padrão `IsActive` da casa foi formado para **entidades de catálogo com
  vínculos apontando para elas**. Um `McpServer` desativado continua
  referenciado por `AgentMcpServer`, e o histórico que o usou precisa continuar
  fazendo sentido.
- **Documento é conteúdo**, não catálogo referenciado: nada aponta para ele
  além dos seus próprios fragmentos.
- O caso concreto — operador subiu o arquivo errado, ou um com dado que não
  devia estar ali — é exatamente aquele em que "continua no banco, invisível" é
  a resposta errada.
- Custo medido: ~60 MB de vetores por 7.500 fragmentos (150.000 × 1536 dims =
  1200 MB, medido em `pgvector/pgvector:pg18`). Conteúdo morto retido
  indefinidamente é custo real, não hipotético.

`KnowledgeBase` **continua seguindo a casa**: `IsActive`, sem delete — ela é
entidade de catálogo, e a partir da etapa 3 terá vínculos de agente apontando
para ela, exatamente o caso que formou o padrão.

*Observado na implementação:* `DELETE /knowledge-bases/{id}` responde **405
Method Not Allowed**, não 404 — a rota existe para `GET` e `PUT`, e o que falta
é o verbo. É a resposta mais informativa das duas, e é ela que o teste afirma;
mudar isso para 404 esconderia a distinção entre "recurso inexistente" e
"operação não oferecida".

FK de `KnowledgeFragment` → `KnowledgeDocument` com
`OnDelete(DeleteBehavior.Cascade)`, declarada na etapa 2 — ali a cascata é o
comportamento desejado, porque a exclusão do documento **é** real e intencional.
Nesta etapa a cascata não tem o que apagar; o cenário "documento excluído não é
mais listado nem consultável" é testável hoje, e o cenário "seus fragmentos
somem" entra na etapa 2.

**FK de `KnowledgeDocument` → `KnowledgeBase` usa `DeleteBehavior.Restrict`, não
`Cascade`.** O default do EF Core para FK obrigatória é `Cascade`, e aceitá-lo
por omissão deixaria a base pré-armada: no dia em que alguém adicionasse exclusão
de base, todos os seus documentos sumiriam em silêncio, sem que essa
consequência tivesse sido decidida por ninguém. `Restrict` torna esse dia uma
decisão explícita — quem for adicionar delete de base terá de dizer o que fazer
com os documentos, em vez de descobrir depois. Hoje as duas opções são inertes,
porque `KnowledgeBase` não tem rota de exclusão; a diferença é qual delas falha
de forma segura se a premissa mudar.

*Alternativa considerada:* manter `Cascade` com a justificativa de que apagar a
base deveria mesmo apagar os documentos. Recusada não por estar errada, mas por
decidir hoje, sem cenário, o comportamento de uma rota que não existe — e por
fazê-lo de forma silenciosa, que é a parte cara.

*Alternativa considerada:* seguir `IsActive` também para documento, por
consistência. Recusada porque exigiria o filtro de `IsActive` na consulta de
**fragmentos** (não só na listagem de documentos) — um lugar fácil de esquecer,
cujo esquecimento faz um documento "excluído" continuar respondendo pelo
agente. Delete real elimina a classe inteira de defeito em vez de a testar.

### D7 — `ContentRevision` explícita, não `xmin`

Coluna `ContentRevision` (`int`, começa em 1), incrementada **apenas quando
`ExtractedText` muda**. O consumidor da etapa 2 lê a revisão no início, faz o
trabalho lento (embedding), e grava condicionado a ela ainda ser a corrente; se
mudou, descarta o resultado inteiro, porque o trabalho novo já está na fila. O
mesmo mecanismo cobre o delete de graça.

Escolhida contra `xmin`, que é o idioma da casa (`PendingDispatch`), por três
motivos:

1. **O consumidor muta a própria linha do documento.** Ele escreve
   `Pending → Indexing → Indexed/Failed`. Com `xmin`, cada uma dessas escritas
   bumpa o token e invalida o próprio trabalho em curso, exigindo recarregar e
   rebaixar o token a cada transição — cerimônia que existe só por causa da
   escolha. `ContentRevision`, que o consumidor **nunca escreve**, permanece
   estável ao longo das suas próprias transições de status.
2. **Precisão.** `xmin` muda com qualquer `UPDATE`, inclusive um `PUT` que só
   troca o título. Isso descartaria uma indexação em andamento e gastaria um
   ciclo de embedding à toa. `ContentRevision` só se move quando o conteúdo se
   move, que é a pergunta que o consumidor faz.
3. **Testabilidade (convenções 10 e 15).** Um teste pode afirmar
   `ContentRevision == 2` e reintroduzir a divergência de propósito. `xmin` é
   opaco e só observável por exceção.

Custo adicional de `xmin` verificado no código: `apps/inbox` precisou de
propriedade shadow `entity.Property<uint>("Version").IsRowVersion()` porque
`UseXminAsConcurrencyToken()` não existe na versão do provider referenciada
(`AppDbContext.cs:133-137`) — mesma versão pinada aqui. Não é bloqueio, mas
confirma que `xmin` não é o caminho mais curto.

*Trade-off aceito:* divergir do idioma da casa. O motivo é que os dois casos são
assimétricos — em `PendingDispatch`, os concorrentes são escritores do mesmo
tipo disputando a mesma linha; aqui, um lado (operador) muda conteúdo e o outro
(worker) muda status. Um token de linha conflaria duas preocupações distintas.

### D8 — `Pending` tem dois significados, e a saída **não** é um valor de enum novo

O problema é real: um documento em reindexação está `Pending` **e tem
fragmentos válidos ainda respondendo**; um documento novo está `Pending` e não
tem nada. Mesmo rótulo, significados opostos, e a regra da convenção 13 ("não
mostrar `0 fragmentos`, porque 0 afirma que a indexação rodou") está certa para
o primeiro e errada para o segundo.

**Decisão: o enum fica com quatro valores (`Pending`, `Indexing`, `Indexed`,
`Failed`), e a distinção é carregada por `IndexedAt`.**

Motivo — e isto contraria a formulação que trouxe a questão para cá:

- A distinção **já é derivável do dado que a change tem de qualquer forma**:
  `IndexedAt == null` significa "nunca indexado com sucesso";
  `IndexedAt != null` significa "há conteúdo indexado respondendo agora".
  Criar um valor de enum para isso é estado redundante que pode divergir do
  `IndexedAt` — fonte de defeito, não de clareza (convenção 2).
- Um valor `Reindexing` **não bastaria**. O ciclo tem dois estados
  não-terminais (`Pending` *e* `Indexing`), e a reindexação passa pelos dois com
  fragmentos antigos vivos. Seriam necessários **dois** valores novos
  (`ReindexPending` e `Reindexing`), levando o enum de quatro para seis e
  dobrando os caminhos de UI — para expressar o que uma coluna já existente
  responde.
- Uma regra única cobre as oito combinações: **a contagem de fragmentos é
  exibida sempre que `IndexedAt != null`, qualquer que seja o estado; e é
  omitida quando `IndexedAt == null`.** Isso vale inclusive para `Failed`:
  falha na primeira indexação (`IndexedAt == null`) não mostra contagem; falha
  numa reindexação (`IndexedAt != null`) continua mostrando a contagem antiga,
  que é exatamente o que ainda está respondendo.

A coluna de contagem só nasce na etapa 2, mas a regra e o significado de
`IndexedAt` são contrato **desta** change, e é isso que a etapa 5a vai
consumir. Precedente da casa para estado derivável e não promovido a valor
próprio: `Session` aberta/encerrada, derivada de `ClosedAt`.

### D9 — Contrato de atualização, fixado aqui e implementado na etapa 2

`PUT /knowledge-bases/{kbId}/documents/{id}` recebe o mesmo shape do `POST`. O
servidor **não distingue** "editado na textarea" de "arquivo novo subido", e não
deve — nada no contrato os diferencia (D2).

Na mesma transação: grava conteúdo novo, incrementa `ContentRevision` se
`ExtractedText` mudou, coloca `IndexingStatus = Pending`, limpa `FailureReason`,
**preserva `IndexedAt`**, e (etapa 2) publica na fila.

Três garantias que são contrato, não implementação:

1. **Substituição integral, nunca diff de fragmento.** Um parágrafo editado
   desloca as fronteiras de todos os fragmentos seguintes; casar antigo com novo
   custa mais que reprocessar, e reprocessar é fração de centavo.
2. **Fragmentos antigos sobrevivem até a reindexação terminar.** Entre o `PUT` e
   a gravação dos novos, a busca continua devolvendo os antigos: o agente
   responde com conteúdo levemente desatualizado por alguns minutos, em vez de
   responder "não tenho essa informação" sobre um assunto que a base cobre. A
   gravação final é transação única — apaga todos os fragmentos do documento e
   insere os novos, ou não faz nada.
3. **Falha de indexação preserva os fragmentos antigos.**
   `IndexingStatus = Failed` + `FailureReason`, `IndexedAt` intacto; o documento
   continua respondendo com o conteúdo anterior e a tela mostra que a
   atualização não pegou. O que não pode acontecer é a falha deixar o documento
   sem fragmento nenhum com aparência de normal.

**Estas três garantias NÃO viram requisito na spec desta change.** Elas não têm
gatilho verificável aqui — não existe consumidor de indexação, e nenhum teste
desta change pode reprová-las. Um requisito assim passa verde sem provar nada,
que é exatamente o padrão que a convenção 10 nomeia como o mais caro desta base
("risco listado e não coberto parece cuidado sem ser"). Elas ficam registradas
**aqui**, em D9, como decididas nesta change, e entram como *ADDED requirements*
da etapa 2 com cenário real e verificável (transação única; fragmentos antigos
sobrevivendo até o sucesso; falha preservando os antigos). A etapa 2 as **herda
decididas** — se divergir, corrige este documento com a causa real (convenção 9),
não redecide em silêncio.

*Evolução planejada, registrada para não parecer regressão depois:* nesta etapa
**toda** atualização volta a `Pending`, inclusive um `PUT` que só troca o
título. Com `ContentHash` (etapa 2), conteúdo idêntico deixa de voltar a
`Pending`, não enfileira e não gasta embedding. A `ContentRevision` já se
comporta corretamente hoje (não incrementa se `ExtractedText` não mudou); é só o
`IndexingStatus` que é conservador nesta etapa.

### D10 — O documento fica `Pending` indefinidamente, e isso está na spec

Não há fila nem consumidor nesta etapa. Todo documento criado ou atualizado
permanece `Pending` para sempre até a etapa 2 existir. **Isso é requisito
declarado, não defeito** — está escrito na spec para que a etapa 2 encontre um
estado esperado em vez de um bug aparente, e para que a etapa 5a saiba que
`Pending` é o único estado observável até lá.

**Pelo mesmo motivo, e para que a revisão da etapa 2 não os leia como código
morto:** os valores `Indexing`, `Indexed` e `Failed` do enum, e as colunas
`IndexedAt` e `FailureReason`, **nascem sem nenhum escritor nesta etapa**. Nada
neste código os produz. Eles existem aqui porque são schema e contrato —
`IndexedAt` é o que carrega a distinção de D8 e a etapa 5a vai consumir, e
`FailureReason` é exigido pela garantia 3 de D9. Quem os escreve é o consumidor
da etapa 2. Isso vale igualmente para `ContentRevision`, cujo motivo já está em
D7.

*Consequência a declarar em vez de deixar parecer cobertura completa:*
`KnowledgeWireFormatTests` só consegue afirmar `"Pending"` como string, porque os
outros três valores não têm como aparecer numa resposta real ainda. O formato de
fio de `Indexing`/`Indexed`/`Failed` fica verificável só na etapa 2, e é lá que
o teste equivalente tem de ser estendido. O risco que sobra é pequeno — o
conversor é declarado no tipo, não por valor — mas a cobertura desta change é
parcial e está dito.

### D11 — Extração de markdown **preserva** a marcação; é normalização, não conversão

`ExtractedText` guarda o markdown com a sintaxe intacta. "Extração" aqui não
significa converter para texto puro: a estratégia de chunking já decidida na
exploração é **cabeçalho + merge-up até ~900 caracteres** (medida contra divisão
pura por cabeçalho, que produziu 5.008 fragmentos de média 167 caracteres contra
419 de média 1092). Ela **depende dos cabeçalhos `#` sobreviverem**. Um extrator
que "limpasse" a marcação destruiria a estrutura de que a etapa 2 depende.

O extrator de `markdown` faz, e só faz:

- remove BOM UTF-8 inicial;
- normaliza fim de linha `CRLF`/`CR` → `LF`;
- **rejeita conteúdo com caractere NUL (U+0000)**;
- rejeita conteúdo vazio ou só de espaços.

*O NUL não é hipótese:* verificado contra o Postgres real desta stack —
`select length(chr(0))` responde `ERROR: null character not permitted`. Uma
coluna `text` não aceita U+0000, então um `.txt` com byte nulo estouraria no
`INSERT` com erro cru de banco em vez de validação. O extrator o transforma em
400 com mensagem, que é onde o operador consegue entender o que houve.

Um `.txt` **sem** marcação nenhuma não é caso de erro: texto puro é markdown
válido. É por isso que `.txt` entra com `sourceType: "markdown"` sem precisar de
extrator próprio (D12).

### D12 — `SourceType` é ponto de extensão tipo-plugin, com checagem bidirecional no startup

String aberta validada em runtime contra os extratores efetivamente registrados
via DI keyed (`AddKeyedSingleton<IKnowledgeSourceExtractor>(sourceType)`), no
mesmo idioma de `Channel.ChannelType`. `KnowledgeSourceTypes` declara a lista esperada;
`ValidateKnowledgeExtractorRegistrations` é extensão de `IServiceCollection` e
roda sobre `builder.Services`, logo após os registros e **antes** de
`builder.Build()`.

> *Corrigido durante a implementação (convenção 9).* O texto original citava
> dois moldes contraditórios — `ValidateChannelAdapterRegistrations` (que opera
> sobre `IServiceCollection`, antes do `Build()`) e
> `ValidateTimeZoneConfiguration` (que opera sobre `IHost`, depois dele) — e
> descrevia o segundo. Vale o primeiro: a checagem inspeciona **descritores de
> DI keyed**, que é o que `IServiceCollection` expõe; sobre `IHost` seria
> preciso resolver os serviços para descobrir as chaves. `apps/inbox` chama
> `builder.Services.ValidateChannelAdapterRegistrations()` em
> `Program.cs:78` pelo mesmo motivo.
>
> *Correção de segunda ordem, na revisão final:* a versão anterior desta nota
> afirmava que "o requisito da spec ('valida no startup, antes de começar a
> servir') é atendido dos dois jeitos" — **e citava a spec de memória, errado**.
> A spec dizia "depois de construir o host", ou seja, fixava o mecanismo, e
> fixava o que não foi implementado. Isso importa mais que a divergência
> original: o `design.md` é arquivado, a spec vira spec vivo, e quem for
> acrescentar o extrator de PDF leria ali a instrução errada — é o caso literal
> da convenção 6 (requisito errado sobrevive ao archive). A spec foi corrigida
> para não amarrar mecanismo nenhum: exige que a aplicação não sirva requisição
> com registro divergente, e remete à convenção 8 do
> `01-ARQUITETURA_E_CONVENCOES.md` para as duas formas do padrão. A tarefa 2.5
> de `tasks.md`, que repetia o mesmo erro, também foi corrigida.

**Consequência da forma escolhida, e como ela foi fechada.** Esta é a única das
quatro checagens de integridade do repositório que roda sobre
`IServiceCollection`; as outras três (contrato de canal, classificação de rotas,
fuso horário) rodam sobre o host construído e derrubam o boot de verdade. A
diferença tem custo: testar a extensão diretamente verifica **a extensão**, não
o caminho de boot. Se alguém movesse ou removesse a chamada do `Program.cs`, os
quatro testes de unidade continuariam verdes e a aplicação subiria com registro
divergente.

Isso **não** ficou como limite aceito: `KnowledgeProductionRegistrationTests`
captura a `IServiceCollection` **real** pelo `ConfigureServices` da
`WebApplicationFactory` — que roda depois de todos os registros de
`Program.cs`, portanto sobre a composição de produção — e afirma ali que os
extratores registrados correspondem exatamente a `KnowledgeSourceTypes.All`.

Verificado no idioma da convenção 15, com o defeito que só ele pega: removendo
a chamada de `Program.cs` **e** acrescentando um `AddKeyedSingleton` para
`"pdf"`, os quatro testes de unidade da extensão ficam **4/4 verdes** e os dois
testes de composição real **reprovam**. Restaurado, 6/6 verdes. Custo: 819 ms,
sem container.

O que sobra de resíduo é benigno: a chamada removida **sem** divergência de
registro não falha nada — mas também não causa dano, porque o registro está
correto; e a divergência, quando aparecer, é pega no mesmo instante. **Gatilho
para reavaliar:** o registro do segundo `SourceType` (etapa de PDF), quando a
divergência deixa de ser hipotética.

**Bidirecional** (convenção 8): tipo declarado sem extrator registrado falha o
boot, **e** extrator registrado para um tipo não declarado também falha. O
segundo sentido é o que impede um extrator entrar em produção alcançável pela
API sem ter sido declarado como suportado.

Extensão de arquivo e `SourceType` são coisas distintas: o **cliente** sugere
`markdown` para `.md`/`.markdown`/`.txt`; o servidor recebe `sourceType`
explícito e o valida contra o registro. Único tipo nesta etapa: `markdown`.

`SourceType` no fio é string sempre — não é enum fechado, exatamente como
`ChannelType`.

### D13 — Título duplicado na mesma base é permitido

`McpServer.Name` e `Agent.Name` não têm unicidade, e a ausência já mordeu
(`DelegationToolNameSlugifier` existe porque `Agent.Name` não é único). Impor
unicidade em documento seria a primeira regra desse tipo na base. O caso real
que a motivaria (subir a mesma pasta duas vezes) será resolvido melhor por
`ContentHash` na etapa 2 do que por rejeição de título.

### D14 — A listagem não devolve `ExtractedText`

`GET /knowledge-bases/{kbId}/documents` projeta
`KnowledgeDocumentSummaryResponse` (id, título, `sourceType`, `indexingStatus`,
`indexedAt`, `failureReason`, `contentLengthBytes`, `createdAt`, `updatedAt`) —
**sem o conteúdo**. Uma base com 50 documentos de 80 KB devolveria 4 MB de
texto que nenhuma tela usa.

`GET /knowledge-bases/{kbId}/documents/{id}` **devolve** `extractedText`: é o
que preenche a textarea do caminho "Escrever manualmente" na edição.

**`contentLengthBytes` é coluna gerada pelo Postgres**, declarada no modelo com
`HasComputedColumnSql("octet_length(\"ExtractedText\")", stored: true)`. A
aplicação **nunca a escreve** — o banco a calcula a partir da própria coluna de
texto, no `INSERT` e a cada `UPDATE`.

*Por que não projeção SQL na consulta:* a projeção natural
(`.Select(d => d.ExtractedText.Length)`) daria **caracteres**, não bytes.
Decompilando `NpgsqlStringMemberTranslator` do provider pinado
(`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3), `string.Length` traduz para
`length(text)`; e a string `octet_length` **não aparece nenhuma vez no assembly
inteiro** — não há mapeamento LINQ para ela.

*Por que não coluna comum escrita em C#:* seria dado derivado mantido por
disciplina, e a garantia de não divergir dependeria de `ExtractedText` jamais
ser escrito por um caminho que esquecesse de recalcular. Coluna gerada elimina a
classe inteira **pelo banco**, não por convenção — não há caminho, presente ou
futuro, que atualize o texto sem atualizar o tamanho.

**Spike rodado** (`net10.0`, Npgsql EF 10.0.3, `postgres:18` real):

```
DDL gerado pelo EF, sem SQL cru na migração:
  ContentLengthBytes = table.Column<int>(type: "integer", nullable: false,
      computedColumnSql: "octet_length(\"ExtractedText\")", stored: true)

INSERT  chars=64   bytesUTF8=73   colunaLida=73    ok=True
UPDATE  chars=119  bytesUTF8=131  colunaLida=131   ok=True
projeção sem trazer o texto: id=1 bytes=131
```

Três achados que a leitura sozinha não daria: (1) **não foi preciso SQL cru** —
`HasComputedColumnSql(..., stored: true)` gera o DDL nativamente, então a
migração continua sendo migração normal; (2) o EF **lê o valor de volta** depois
do `INSERT` e depois do `UPDATE`, sem `Reload()` explícito; (3) a propriedade
pode ter **setter privado** e ainda ser hidratada.

Não é "coluna sem cenário" (convenção 2): seu consumidor é a listagem **desta**
change.

*Handoff para a etapa 5a:* o contador do modal do protótipo diz "N caracteres de
texto". Ou passa a dizer bytes, ou mostra os dois deixando claro qual é o que
conta para o limite — mas o número comparado ao teto é o de bytes. Registrado na
tarefa de fechamento junto com os demais itens de handoff.

*Cuidado registrado para a etapa 2:* quando a contagem de fragmentos entrar na
listagem, ela deve ser um `GroupBy` com `Count()` **traduzido para SQL**, não o
padrão de `ListAgentsQueryHandler`, que materializa todos os vínculos com
`ToListAsync()` e agrupa em memória. Aquele shape é seguro em escala de catálogo
e catastrófico em escala de fragmentos. Reusar o espírito (uma consulta em lote,
sem N+1), não a letra.

### D15 — Espelho em `apps/workers`: entidades e migração, sem código

`apps/workers` ganha as duas entidades e a migração equivalente, e nada mais —
nenhum resolver, nenhum consumidor, nenhuma leitura. Os dois `AppDbContext` são
sincronizados por disciplina (não por schema compartilhado), e deixar o espelho
para a etapa 2 significaria que a migração de `apps/api` já teria rodado contra
um modelo que `apps/workers` desconhece. O teste `KnowledgeSchemaMirrorTests`
afirma que o modelo espelhado gera o mesmo schema.

#### Como as duas migrações coexistem contra o mesmo banco

Esta change cria duas migrações que criam **as mesmas duas tabelas**, e os dois
apps apontam para o mesmo Postgres. O mecanismo que resolve isso **já existe** e
foi verificado, não suposto — `AddKnowledgeBaseCatalog` só precisa segui-lo:

- **Não há `MigrationsHistoryTable` configurada em lugar nenhum**
  (`grep -rn "MigrationsHistoryTable\|HistoryRepository" apps` → vazio). Os dois
  contextos usariam o mesmo `__EFMigrationsHistory` default.
- **`apps/workers` nunca aplica migração contra banco real.** Está declarado em
  três lugares: o XML doc do seu `AppDbContext` ("este contexto nunca chama
  `Database.MigrateAsync` — as migrations aqui existem só para geração de
  schema/ferramental do EF Core (detectar divergência), não para serem
  executadas em runtime"), o cabeçalho de `deploy/migrate/Dockerfile`
  ("`apps/workers` NUNCA gera nem roda bundle"), e a D1 de
  `containerizacao-stack-servidor`.
- **Confirmado no código, não só no comentário:** o único uso de `Database.` em
  `apps/workers/src` fora desse comentário é o lock consultivo de
  `ConversationContextLock`. Não há `MigrateAsync`, `GetPendingMigrations`,
  `EnsureCreated` nem checagem de schema no startup — a única validação de boot
  de `apps/workers` é `ValidateTimeZoneConfiguration`.
- **"Detectar divergência" é design-time, não runtime**: as migrações de
  `apps/workers` existem para que `dotnet ef migrations add` ali produza um diff
  se o modelo espelhado sair de sincronia, e para que
  `WorkerInfrastructureFixture` consiga criar o schema num Testcontainer limpo
  (é ele quem chama `MigrateAsync`, contra container descartável).

**Portanto a migração de `apps/workers` nunca entra no histórico do banco
compartilhado, e a preocupação de "ficar permanentemente pendente" não se
materializa: nada jamais pergunta.** Nenhum caminho de runtime consulta
migrações pendentes em `apps/workers`.

O que **é** real, e foi reproduzido: rodar `dotnet ef database update` a partir
de `apps/workers` contra um banco que já recebeu as migrações de `apps/api`
falha. Executado contra um Postgres descartável (`postgres:18`, banco limpo):

```
1) apps/api      -> dotnet ef database update  ... Done.
2) apps/workers  -> dotnet ef database update  (MESMO banco)
   42P07: relation "agents" already exists
```

O histórico após as duas tentativas contém **apenas os 8 IDs de `apps/api`** — a
tentativa de `apps/workers` falhou na sua primeira migração e reverteu inteira,
sem deixar linha. Ou seja: o erro é ruidoso e não corrompe estado.

Isso é propriedade **pré-existente** do repositório (vale hoje para `agents` e
`mcp_servers`), não algo que esta change introduz — mas não estava escrito em
lugar nenhum, e por isso está aqui. A regra operacional: **nunca rodar
`dotnet ef database update` a partir de `apps/workers`.** Migração de banco real
sai só de `apps/api` (e de `apps/inbox`, no banco dele).

*Risco residual e seu guarda:* alguém "consertar" o estado pendente adicionando
`MigrateAsync` ao boot de `apps/workers`. A tarefa 5.5 verifica explicitamente
que nenhum caminho de runtime aplica migração ali, e a 5.4 já diz que
`apps/workers` não ganha mais nada além do espelho.

## Risks / Trade-offs

Cada risco com contraparte verificável (convenção 10) — cenário na spec e
teste, ou justificativa explícita de por que não é testável.

| # | Risco | Contraparte verificável |
|---|---|---|
| R1 | **Documento preso em `Pending` parece defeito** e alguém "conserta" na etapa 2 mudando o contrato | Cenário na spec: documento criado responde `indexingStatus: "Pending"` e permanece assim; teste afirma o estado após criação **e** após atualização. É requisito, não acidente (D10) |
| R2 | **Conteúdo com NUL estoura como erro cru de Postgres** em vez de validação | Cenário: `content` com U+0000 → HTTP 400 com `ValidationProblemDetails`, nenhum registro criado. Par "sem item": conteúdo normal com acentos e emoji é aceito. Verificado contra o Postgres real (D11) |
| R3 | **Extrator registrado sem tipo declarado** (ou o inverso) fica alcançável/inalcançável em silêncio | Checagem bidirecional no startup (D12) + fixture `KnowledgeExtractorMisregistrationFixture` que reprova o boot nos **dois** sentidos. Convenção 15: escrever o teste, reintroduzir a divergência de propósito, ver reprovar, e só então manter |
| R4 | **`ContentRevision` incrementa quando não devia**, invalidando trabalho de indexação da etapa 2 à toa | Teste: `PUT` que só troca título mantém `ContentRevision`; `PUT` que muda conteúdo incrementa; `PUT` com conteúdo idêntico byte a byte mantém. Os três, nesta change (D7) |
| R5 | **Enum sai como inteiro ordinal no fio** — já aconteceu duas vezes nesta base | `KnowledgeWireFormatTests` inspeciona o **texto do JSON** da resposta real, não desserializa para o mesmo tipo (convenção 11: round-trip pelo mesmo tipo é cego a isso). Vale para `indexingStatus` e `sourceType` |
| R6 | **Delete real apaga conteúdo sem volta** — é o primeiro do repositório e não há lixeira | **Aceitação consciente, sem contraparte técnica nesta change.** É o comportamento pedido e D6 o sustenta; não há o que testar além do 404, e o 404 não cobre o risco enunciado. Não delego a contraparte para a etapa 5a: confirmação antes de excluir é item de **handoff registrado** (tarefa 9.3), não promessa numa célula de tabela que será arquivada |
| R12 | **Validação do teto e tamanho exposto medindo strings diferentes** — a extração encolhe o conteúdo (BOM, `CRLF`) | Ordem fixada em D5: extrair primeiro, validar sobre o texto extraído, que é o mesmo que a coluna gerada mede. Teste: documento com BOM e `CRLF` cujo tamanho **bruto** passa de 1 MiB mas cujo texto extraído não — é aceito, e o `contentLengthBytes` devolvido bate com o extraído, não com o bruto |
| R11 | **Alguém "conserta" a migração pendente de `apps/workers`** adicionando `MigrateAsync` ao boot dele, e passa a haver dois aplicadores contra o mesmo banco | Tarefa 5.5: verificar explicitamente que nenhum caminho de runtime de `apps/workers` aplica migração. O mecanismo e a regra operacional ("nunca rodar `database update` a partir de `apps/workers`") estão em D15, com o erro reproduzido (`42P07`) |
| R7 | **Listagem devolvendo `ExtractedText`** infla a resposta em ordens de grandeza | Teste afirma que o JSON da listagem **não contém** a chave `extractedText` (asserção negativa) e que o `GET` por id **contém** (D14) |
| R8 | **Teto de 1 MiB rejeita documento legítimo** de um cliente com manual grande | Mitigado por margem: 1 MiB é ~12× o maior documento real observado (79,2 KB). Se aparecer cenário real, é ajuste de uma constante com medição por trás — não desenho a refazer (D5) |
| R9 | **Divergência entre os dois `AppDbContext`** — sincronizados por disciplina, não por schema | `KnowledgeSchemaMirrorTests` em `apps/workers` (D15). É o mesmo risco que `McpServer`/`Agent` já correm hoje, agora com teste |
| R10 | **Injeção de prompt via documento** — o conteúdo vai para a janela de contexto do agente na etapa 4 | **Não testável nesta etapa** e registrado como tal: não há caminho de leitura pelo agente ainda. A mitigação hoje é *procedimental* (o operador é a única origem), não técnica. **Gatilho explícito:** no dia em que qualquer caminho permitir usuário final alimentar uma base, a conta muda, e o item já aberto de `AgentMcpServer.AllowedTools` não distinguir leitura de escrita volta a valer com força. A distinção que `inbox-contexto-canal` formulou (dado atribuído pelo provedor × texto livre do usuário final) classifica documento como a **segunda** categoria |

## Migration Plan

Duas migrações EF Core equivalentes, uma em cada app, criando as mesmas duas
tabelas. **Só a de `apps/api` é aplicada a banco real**: o `migrator` do
`docker-compose.prod.yml` builda bundles apenas de `apps/api` e `apps/inbox`, e
`apps/workers` nunca gera nem roda bundle. A migração de `apps/workers` é
ferramental de design-time e só roda em Testcontainer descartável. Ver D15 para
o mecanismo completo e a evidência.

Aditivo puro: nenhuma tabela ou coluna existente é tocada, nenhum dado migrado.
Rollback é o `Down` das duas migrações (drop das duas tabelas); como nada
consome esses dados ainda, não há perda funcional em reverter.

## Open Questions

1. **Quanto embedding melhora sobre os 47-52% do lexical?** Não fechado por
   falta de credencial: a chave em `.env` é o placeholder `changeme`, e o
   ambiente não alcança PyPI para um modelo local. **Não bloqueia esta change** —
   a recomendação de embedding está fechada pelo *negativo* (lexical reprovou e
   nenhum limiar o salva), e o número só refina o chunking da etapa 2, não o
   schema desta. Scripts e corpus prontos para rodar em minutos com uma chave.
2. **Se `Pending` só se resolve na etapa 2, qual o limite de tempo aceitável
   para um documento ficar sem responder após a atualização?** É pergunta de
   produto, não técnica: define se a etapa 2 precisa de prioridade de fila ou se
   FIFO simples basta. Não afeta o schema desta change.

### Achado incidental, fora do escopo desta change

O `.env` local usa `ChatClient__BaseUrl` / `ChatClient__ApiKey` /
`ChatClient__Model`, mas `ChatClientOptions.SectionName` é `"OpenAI"` e o
`Program.cs` de `apps/workers` faz `GetSection("OpenAI")`. O `.env.example`
(linhas 57-58) e o `docker-compose.prod.yml` (linhas 80-81) usam os nomes
corretos. As três entradas `ChatClient__*` do `.env` local são nomes obsoletos
de antes de `backend-multi-provedor-llm` e **não bindam em nada**. Não pertence
a esta change; registrado para virar correção própria.
