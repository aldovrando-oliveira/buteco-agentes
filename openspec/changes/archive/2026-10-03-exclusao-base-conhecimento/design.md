## Context

`KnowledgeBase` segue hoje a regra "catálogo referenciado → `IsActive`; conteúdo sem
referência → exclusão real" (`01`, "Exclusão: catálogo × conteúdo"). A base não tem
`MapDelete` (`KnowledgeBaseEndpoints.cs:41-45`), e um teste afirma o `405`
(`KnowledgeBaseCatalogTests.cs:228-243`).

A linha de bases sincronizadas tornou isso um bloqueio: a unicidade de
`(SyncProvider, SyncFolderId)` vale para base inativa (#102, D5), a pasta é imutável,
e a recuperação de uma base que perdeu a pasta é excluir a base antiga e criar outra.
Sem exclusão, a pasta fica presa para sempre.

O schema, conferido no `AppDbContextModelSnapshot.cs` de `origin/main` (`0499161`):

| relação | FK | `OnDelete` | linha do snapshot |
|---|---|---|---|
| `knowledge_documents` → `knowledge_bases` | composta `(KnowledgeBaseId, KnowledgeBaseContentMode)` → `(Id, ContentMode)` | **`Restrict`** | 827-832 |
| `knowledge_fragments` → `knowledge_documents` | `KnowledgeDocumentId` | `Cascade` | 846-850 |
| `knowledge_fragments` → `knowledge_bases` | **nenhuma** (`KnowledgeBaseId` é desnormalizado) | — | 664-672 |
| `knowledge_document_events` → `knowledge_bases` | `KnowledgeBaseId` | `Cascade` | 837-841 |
| `knowledge_document_events` → documento | **nenhuma** (D1 da #98) | — | — |
| `agent_knowledge_bases` → `knowledge_bases` | `KnowledgeBaseId` | `Cascade` | 772-776 |
| `knowledge_indexing_attempts` → documento ou base | **nenhuma** (métrica sobrevive ao catálogo) | — | 225-262 |

A #105 (ciclo de sincronização, `apps/connectors`) está em desenvolvimento em
paralelo e trata `404` na base como fim do ciclo daquela base. A #106 e a #107
(telas da base sincronizada) estão na fila e mexem no mesmo detalhe de base.

## Goals / Non-Goals

**Goals:**

- `DELETE /knowledge-bases/{id}` que apaga a base e tudo que é dela, de forma
  atômica, só para base inativa.
- A pasta de uma base sincronizada excluída fica livre para outra base.
- Nenhuma escrita de `/sync` que encontre a base excluída no meio responde `500`:
  todas respondem `404`.

**Non-Goals:**

- Tela de exclusão (#136).
- A mensagem do `409` de pasta em uso (D8): continua neutra; muda com o botão, na
  #136.
- As corridas do operador contra a exclusão (inclusão de documento e substituição
  de vínculos): risco aceito (D6, Risks).
- Exclusão de `Agent`, `McpServer` ou `Channel`: continuam só se desativando.
- Mudança no `apps/connectors`: a reação a `404` é da #105.
- Mudança de schema: nenhuma migração.
- Lixeira, exclusão reversível ou retenção dos eventos de uma base excluída.

## Decisions

### D1. A exceção à regra de catálogo é da base, e o critério ganha uma segunda pergunta

O critério da casa responde "quem aponta para a entidade". A base tem vínculos de
agente apontando para ela, então pelo critério literal ela é catálogo. O motivo pelo
qual catálogo não se exclui é **preservar a leitura do passado**: um `McpServer`
apagado deixaria execuções antigas sem sentido. Para a base isso não vale:

- nenhuma execução, métrica ou insight lê a base pelo id depois do fato
  (`knowledge_indexing_attempts` e `embedding_calls` não têm FK e não fazem join com
  `knowledge_bases`; as consultas de insight juntam com `task_executions`, nunca com
  a base — `GetSystemInsightsQueryHandler.cs:305-350`);
- o histórico de documentos é a auditoria **da base**, e já foi decidido que morre
  com ela (D3 da #98);
- manter a base existindo tem um custo funcional, não só de disco: a pasta presa.

A regra passa a ter duas perguntas: **alguém lê o passado por esta entidade? Ela
retém um recurso exclusivo?** Base de conhecimento responde "não" e "sim", e por
isso tem exclusão real. `Agent` (execuções e métricas o leem pelo id), `McpServer`
(execuções) e `Channel` (sessões e mensagens) continuam só se desativando.

O `01` e o `docs/architecture.md` ganham um parágrafo com a exceção, sem reescrever
o critério existente.

- *Descartado, mudar a regra para "toda entidade de catálogo pode ser excluída":*
  `Agent` e `Channel` têm passado lido pelo id; abrir a exceção geral exige decidir
  esses destinos, o que nenhuma issue pede.
- *Descartado, unicidade de pasta só entre bases ativas:* já descartado na #102, e
  a #108 repete o motivo — base inativa continua sincronizando, e duas bases
  acompanhariam a mesma pasta.

### D2. Pré-condição: só base inativa; vínculos vão junto

**Base ativa é recusada com `409` e `code: "knowledge-base-active"`, sem apagar
nada.** Com vínculos ela pode ser excluída, e os vínculos vão pela cascata que já
existe.

O peso está no operador: excluir uma base ativa e vinculada tira conhecimento de um
agente em produção no meio do expediente, sem passo intermediário. Exigir a base
inativa faz a perda de conhecimento acontecer **na desativação**, que é reversível e
já tem efeito conhecido: a resolução de tools filtra por `IsActive`
(`KnowledgeToolSetResolver.cs:84-87`), então o agente para de ver a base ali. A
exclusão, depois, não muda o que nenhum agente vê. Excluir vira dois passos, e o
primeiro pode ser desfeito.

Os vínculos não precisam ser removidos antes: com a base inativa eles já são inertes
para a execução, e exigir a remoção obrigaria o operador a editar cada agente sem
ganho de segurança. A confirmação na tela (#136) nomeia os agentes vinculados, no
padrão da exclusão de documento.

O `IsActive` é conferido **sob o bloqueio da linha da base** (D3): uma ativação
concorrente espera a exclusão, ou a exclusão vê a base ativa e recusa.

- *Descartado, qualquer base:* o efeito acima, sem aviso.
- *Descartado, exigir base sem vínculos:* atrito sem proteção adicional, porque a
  base inativa já não chega a nenhum agente.
- *Descartado, exigir base inativa E sem vínculos:* soma o atrito do anterior.

### D3. Documentos vão junto, apagados na aplicação; a FK continua `Restrict`

**A exclusão apaga os documentos da base**, numa transação só, nesta ordem:

1. `SELECT ... FROM knowledge_bases WHERE "Id" = @id FOR UPDATE` — trava a linha.
   Sem linha → `404`. Base ativa → `409` (D2).
2. `DELETE FROM knowledge_documents WHERE "KnowledgeBaseId" = @id` (por
   `ExecuteDeleteAsync`) — os fragmentos vão pela cascata documento → fragmento.
3. `DELETE FROM knowledge_bases WHERE "Id" = @id` — os eventos e os vínculos vão
   pelas cascatas base → evento e base → vínculo.
4. Commit → `204`.

**Por que travar antes:** uma inclusão de documento faz `FOR KEY SHARE` na linha da
base (verificação da FK), e `FOR UPDATE` conflita com ele. Com a linha travada,
nenhum documento novo entra depois do passo 2; e o passo 2 é um comando novo, que em
`READ COMMITTED` enxerga o que foi commitado enquanto a exclusão esperava o bloqueio.
Sem o passo 1, um upsert que commitasse entre os passos 2 e 3 faria o `Restrict`
recusar o passo 3, e a exclusão do operador responderia `500`.

**Sem evento `Deleted` por documento:** os eventos da base são apagados no mesmo
commit (D4), então o evento nasceria morto.

**A FK continua `Restrict` de propósito.** Ela foi escolhida para obrigar quem
criasse exclusão de base a decidir o destino dos documentos (`01`, "Exclusão"); a
decisão está tomada aqui, num lugar só, e o `Restrict` continua sendo a rede para
qualquer outro caminho — um `DELETE` por SQL ou uma rota futura — que esqueça os
documentos. A FK composta também carrega a regra de `ExternalRef` (#102, D4), e
mexer nela é exatamente a "simplificação" que o `01` avisa para não fazer.

- *Descartado, mudar a FK para `Cascade`:* exige migração numa FK composta sensível,
  e tira a rede de segurança de todo caminho futuro.
- *Descartado, exigir base vazia:* em base `Synced` o operador não pode excluir
  documento (`409`, #102 D7), então a base sincronizada só ficaria vazia se a pasta
  ficasse vazia — o caso da issue (pasta perdida, com documentos) seria impossível.
- *Descartado, registrar evento `Deleted` por documento antes de apagar a base:*
  apagado no mesmo commit.

### D4. Histórico: a cascata da D3 da #98 deixa de ser inerte

A cascata base → evento passa a ser alcançada pela rota. O requisito "Eventos
pertencem à base e morrem com ela" (`knowledge-document-history`) ganha o cenário
pela rota, com uma base que **ainda tem documentos e eventos** — o caso que a #98 não
podia exercitar, porque o `Restrict` impedia. Os eventos de outra base continuam.

### D5. A pasta fica livre: provado pelo cadastro real

O índice único parcial da pasta só olha linhas existentes, então apagar a linha
libera a pasta. O teste exclui uma base `Synced` (inativa) e cria outra, pela rota
real do operador, com o mesmo provedor e a mesma pasta, com o `apps/connectors`
falso respondendo a descrição: `201`. E o mesmo cadastro, **antes** da exclusão,
responde `409` `folder-in-use` — sem o primeiro lado, o `201` não prova nada.

### D6. Escritas de `/sync` que encontram a base excluída no meio: `404`, nunca `500`

Toda rota que **lê** a existência da base e depois **grava** tem uma janela em que a
base pode sumir. Antes desta change a janela era inalcançável; agora não é. Esta
decisão cobre **as rotas de `/sync`**, e só elas: a #105 sincroniza bases inativas,
e só base inativa pode ser excluída (D2), então a escrita do ciclo contra a exclusão
é o caso realista. O comportamento de hoje, lido no código:

| rota | o que acontece hoje se a base some entre a leitura e a gravação |
|---|---|
| `PUT /sync/.../documents`, inclusão | `INSERT` viola a FK → `DbUpdateException` não tratada → **`500`** (`UpsertSyncedDocumentCommandHandler.cs:77-81`, só captura o índice de `ExternalRef`) |
| `PUT /sync/.../documents`, atualização | o `INSERT` do evento vem antes do `UPDATE` (3.1) e viola a FK evento → base → `DbUpdateException` não tratada → **`500`** |
| `PUT /sync/.../documents`, sem mudança de documento | só o `UPDATE` do marcador, que afeta 0 linhas → concorrência → releitura acha nada → **`503`** (`:145-151`) |
| `DELETE /sync/.../documents?externalRef=` | o `INSERT` do evento vem antes do `DELETE` (3.1) e viola a FK → **`500`** |
| `POST /sync/.../sync-results` | `UPDATE` da base afeta 0 linhas → `DbUpdateConcurrencyException` não tratada → **`500`** (`RecordSyncResultCommandHandler.cs:46`) |
| `GET /sync/.../documents` | só leitura: responde o que leu |

> **Corrigido na implementação (convenção 9).** A versão aprovada desta tabela
> previa `503` na atualização e `204` na exclusão por referência, supondo que o
> comando sobre o documento fosse o primeiro. A medição da 3.1 mostrou o `INSERT` do
> evento antes, e o teste de janela com os handlers revertidos confirmou: as duas
> respondiam **`500`**, pela FK do evento. O caminho que dava `503` é o upsert sem
> mudança de documento, que não grava evento. A regra abaixo não muda: ela já cobria
> a violação de FK.

**A regra:** quando a gravação de uma rota de `/sync` falha com violação de FK para
a base, ou com concorrência seguida de releitura vazia, o handler **relê a
existência da base**; se ela não existe, responde `404`, que a #105 lê como fim do
ciclo daquela base.

Só a violação das FKs **para a base** é capturada, pelo nome da constraint, no idioma
de `IsExternalRefUniqueViolation`; qualquer outra sobe. Quem a dispara é o upsert de
inclusão, que grava documento e evento — as duas FKs para a base.

O `GET` de referências e a listagem de bases respondem o que leram. Uma leitura que
começa antes da exclusão devolve o estado anterior; a próxima escrita do ciclo
recebe `404`.

**Fora da D6, as rotas do operador.** Lidas no mesmo código, para registrar o que
fica como está:

| rota | se a base some entre a leitura e a gravação |
|---|---|
| `POST /knowledge-bases/{id}/documents` | `INSERT` viola a FK → **`500`** (`CreateKnowledgeDocumentCommandHandler.cs:55`, sem `catch`) |
| `PUT /agents/{id}/knowledge-bases` | `INSERT` do vínculo viola a FK, ou o `DELETE` do vínculo anterior acha 0 linhas porque a cascata já o apagou → **`500`** (`ReplaceAgentKnowledgeBasesCommandHandler.cs:53-60`) |
| `PUT`/`DELETE`/reindexar documento do operador | releitura acha nada → `404` (`UpdateKnowledgeDocumentCommandHandler.cs:75`, idem exclusão e reindexação) — **já correto** |

As duas primeiras exigem o mesmo operador excluindo a base e, ao mesmo tempo, em
outra aba, incluindo documento nela ou vinculando-a a um agente. Ficam como **risco
aceito** (ver Risks).

**Impasse (`40P01`).** Uma escrita de documento que travasse a linha do documento
antes de inserir o evento (que faz `FOR KEY SHARE` na base) formaria um ciclo com a
exclusão, que trava a base antes de apagar os documentos. A ordem dos comandos do EF
não é contrato, então ela foi **medida** (tarefa 3.1, `EmittedSqlCapture` na
requisição real, 03/10/2026, EF Core 10.0.10):

| escrita | comandos de gravação (um `DbCommand` em lote, nesta ordem) |
|---|---|
| upsert, inclusão | `INSERT INTO knowledge_document_events` → `INSERT INTO knowledge_documents` |
| upsert, atualização | `INSERT INTO knowledge_document_events` → `UPDATE knowledge_documents ... WHERE "Id" = ... AND "ContentRevision" = ...` |
| exclusão por referência | `INSERT INTO knowledge_document_events` → `DELETE FROM knowledge_documents ... WHERE "Id" = ... AND "ContentRevision" = ...` |

**O evento vem primeiro nos três**, então a escrita trava a base (`FOR KEY SHARE`)
antes de tocar a linha do documento, na mesma ordem da exclusão (base → documentos).
Duas transações que pegam os bloqueios na mesma ordem não se esperam em ciclo:
**o impasse não é alcançável** com a ordem medida. Ou a escrita pega o `KEY SHARE`
primeiro e a exclusão espera o commit dela (e depois apaga o que ela gravou), ou a
exclusão pega o `FOR UPDATE` primeiro e a escrita espera, encontra a base apagada e
cai na releitura (`404`). O upsert sem mudança de documento só faz `UPDATE` do
marcador, sem evento e sem bloqueio na base; ele trava a linha do documento, a
exclusão espera, e não há ciclo. A gravação do resultado de ciclo só faz `UPDATE`
na base, e espera o `FOR UPDATE`.

Consequências:

- **A tarefa 5.10 (provocar o impasse) não se aplica**: não há interleaving que o
  produza com a ordem medida.
- **A ordem passa a ser afirmada por teste** (par determinístico, convenção 15): o
  SQL emitido pelo upsert de atualização e pela exclusão por referência tem o
  `INSERT INTO knowledge_document_events` antes do comando do documento. Uma versão
  do EF que inverta a ordem reprova esse teste, e é ele que reabre a questão.
- **O tratamento de `40P01` fica como estava decidido**, por ser o mesmo caminho de
  código da concorrência e a garantia de "nunca `500`" do requisito: a exclusão
  repete a transação uma vez, e os handlers de `/sync` tratam `40P01` como a
  concorrência (limpa, relê a base, `404` se ela sumiu). Ele é exercitado por
  teste com a exceção injetada, não por um impasse real.

**Como o teste vê a janela** (molde de `KnowledgeDocumentCatalogTests.Concurrency.cs`,
`DeleteThenConflictOnceInterceptor`): um `SaveChangesInterceptor` registrado num
`WithWebHostBuilder` (mesmo Postgres, sem contêiner novo) chama a **rota real** de
exclusão da base, pela fixture, no primeiro `SaveChanges` da escrita observada, e só
depois deixa a gravação seguir. A escrita passou pela leitura com a base existindo, e
grava com ela excluída: exatamente a janela. O teste afirma `404`, nenhum `500`, e
que o interceptor disparou (escrita que não viu a janela não prova nada).

- *Descartado, deixar a janela como `500` documentado:* a issue e a #105 dependem
  do `404` para encerrar o ciclo daquela base sem nova tentativa.
- *Descartado, bloqueio da base (`FOR SHARE`) em toda escrita de documento:* põe
  transação explícita em todas as escritas para um caso raro, que a releitura
  resolve.
- *Descartado, responder `503` (retentar) na escrita que perdeu para a exclusão:* a
  #105 reagendaria uma base que não volta.
- *Descartado, tratar também as duas corridas do operador:* exigiria estender o
  reconhecimento de FK por nome à inclusão de documento e mudar o handler de
  vínculos (releitura dos ids depois de violação de FK ou de concorrência na remoção
  do vínculo anterior), com um teste de janela para cada — custo de código e de
  teste para um `500` que exige duas ações simultâneas do mesmo operador, sem perda
  de dado.

### D7. Indexação em curso: o `apps/workers` já descarta o documento sumido

Conferido em `apps/workers/src/Buteco.Workers/Knowledge/Indexing/KnowledgeIndexingService.cs`:

- **antes de começar** (linhas 71-83): o documento é lido por id; nulo → `Discarded`,
  sem contar tentativa e sem chamar o provedor de embedding;
- **na gravação** (linhas 318-337): o `UPDATE` condicionado à revisão roda dentro da
  transação que apaga e insere os fragmentos; 0 linhas → rollback e `Discarded`.
  Nenhum fragmento órfão: o rollback desfaz tudo;
- **na falha** (linhas 366-382): mesmo condicionamento, 0 linhas → `Discarded`;
- **o consumidor** (`KnowledgeIndexingConsumer.cs:106-109`) confirma a mensagem e não
  republica `Discarded`.

Se a indexação commita **antes** da exclusão, a exclusão espera o bloqueio da linha
do documento e depois apaga documento e fragmentos novos. É o mesmo caminho da
exclusão de documento, então **nenhuma mudança no `apps/workers`**. A linha de
`knowledge_indexing_attempts` da tentativa descartada sobrevive, como sobrevive à
exclusão de documento (métrica não tem FK).

A busca (tool de conhecimento) não precisa de nada: a base precisa estar inativa
para ser excluída (D2), e a resolução de tools já a exclui. Uma busca em voo contra
base apagada — só possível numa execução que resolveu as tools com a base ainda
ativa — consulta fragmentos por `KnowledgeBaseId` sem tocar `knowledge_bases`
(`KnowledgeToolSetResolver.cs:167-177`), acha zero, e devolve o resultado de "base
sem conteúdo indexado" (`:179-197`), sem exceção. Conferido na tarefa 6.2.

### D8. A mensagem do `409` de pasta em uso continua neutra nesta change

A mensagem proíbe falar em excluir porque não havia rota (#104, D5: "Quando a #108
existir, ela muda a mensagem e o teste"). A rota passa a existir nesta change, mas o
**botão no painel só chega com a #136** (D9). Se a mensagem mandar excluir agora, ela
orienta o operador a uma ação que a tela não oferece — o mesmo defeito que a #104
evitou, só que com a ausência do outro lado. Por isso o `detail`, o requisito
"Pasta em uso recusada no cadastro com a base que a usa" e o teste que proíbe
`exclu`, `remov` e `apag` **ficam como estão**.

A mudança da mensagem passa a ser escopo da **#136**, junto com o botão: o `detail`
passa a orientar desativar e excluir a base que usa a pasta, o requisito da spec
`knowledge-base-catalog` muda, e o teste da ausência é trocado por um que afirma a
orientação. Isso toca o `apps/api` além do `apps/frontend`, e está registrado na
issue: https://github.com/aldovrando-oliveira/buteco-agentes/issues/136#issuecomment-5974453278

O que muda aqui é só o comentário de código de `FolderInUse`
(`KnowledgeBaseEndpoints.cs:81-86`), que afirma que "não existe rota de exclusão de
base até a #108": a rota passa a existir, e o comentário passa a dizer que a
mensagem continua neutra até o botão da #136.

- *Descartado, orientar a exclusão já na #108:* a mensagem mandaria o operador a uma
  ação que o painel não oferece até a #136.

### D9. Frontend: change separada, #136

A tela fica fora desta change. Três motivos:

1. **O protótipo não desenha exclusão de base.** Desenhar a tela é decisão de
   produto que a convenção 17 manda registrar, e merece revisão própria, não um
   apêndice de uma change de API.
2. **A #106 e a #107 mexem no mesmo detalhe de base**, e a #107 é a que mostra o
   alerta que manda excluir. A tela de exclusão encaixa melhor depois delas.
3. **A conferência visual** (convenção 14) exige subir o painel, e a #105 está
   usando a máquina em paralelo.

A #136 foi aberta com o padrão de confirmação da exclusão de documento
(`KnowledgeBaseDetailPage.tsx:342-360`, agentes por `agentsConsultingBase`), a
pré-condição de base inativa e o registro da ausência no protótipo; está marcada
como bloqueada pela #108.

- *Descartado, incluir a tela aqui:* os três motivos acima, e a change dobraria.

### D10. Tamanho: uma change só

O trabalho é `apps/api` e documentação: um handler novo com transação, três
handlers de `/sync` com o tratamento da base sumida (D6), um comentário de código
(D8), e os testes. Sem migração, sem `apps/workers`, sem frontend. Cabe numa revisão. A estimativa por
componente fica para depois da verificação (convenção 18).

- *Descartado, separar a D6 numa change própria:* a exclusão sem a D6 abriria
  `500` nas rotas de `/sync` no exato momento em que a #105 começa a usá-las.

### D11. Contrato da rota

| situação | resposta |
|---|---|
| base inativa existente | `204`, sem corpo |
| base ativa | `409` `ProblemDetails`, `code: "knowledge-base-active"`, `detail` dizendo para desativar antes |
| id inexistente | `404` |
| impasse repetido (`40P01` duas vezes) | `503` com `Retry-After`, nada apagado |

`DELETE /knowledge-bases/{id}` deixa de responder `405`. O teste
`DeleteKnowledgeBase_IsNotAllowedAndBaseSurvives` sai, e o comentário de
`KnowledgeBaseEndpoints.cs:41-45` é reescrito com o motivo da exceção.

A exclusão grava um evento de log informativo com o id, o tipo e a contagem de
documentos apagados (id de evento `1025`, seguindo `1021`, `1022` e `1024`): é a
única pegada de uma ação irreversível depois que o histórico vai junto.

O subject é o do operador; o escopo `service:connectors` **não** ganha a rota (o
escopo de serviço é por método e rota, e a exclusão não é escrita da
sincronização). O teste de escopo afirma o `403` para o subject de serviço.

### D12. Onde os testes moram, e a guarda contra o defeito real

Nenhuma classe nova e nenhum contêiner novo: arquivos `partial` de classes que já
existem (D12 da #102).

| arquivo novo | classe (existente) | o quê |
|---|---|---|
| `Knowledge/KnowledgeBaseCatalogTests.Deletion.cs` | `KnowledgeBaseCatalogTests` | `204`, `404`, `409` de base ativa, o que some e o que fica, outra base intacta, pasta liberada, SQL e transação emitidos, histórico, impasse injetado na exclusão (`204` depois de um, `503` depois de dois) |
| `Knowledge/KnowledgeDocumentCatalogTests.BaseDeletion.cs` | `KnowledgeDocumentCatalogTests` | a janela da D6 em cada rota de `/sync`, as rotas depois da exclusão, a ordem dos comandos medida na 3.1, impasse injetado no upsert |
| `ServiceScopeAuthorizationTests.cs` (existente) | `ServiceScopeAuthorizationTests` | `403` do `service:connectors` na rota nova |

**Guarda (convenção 15):** o teste de recusa de base ativa é escrito, a conferência
de `IsActive` é retirada do handler, o teste precisa reprovar (`204` no lugar de
`409`, e a base sumida), e só então a conferência volta. Registrado no `tasks.md`
com o resultado.

**Par determinístico do bloqueio (convenção 15, quinta forma):** um teste de
corrida entre exclusão e inclusão pode passar por sorte do escalonamento. O teste
captura o SQL emitido na requisição real (`EmittedSqlCapture`) e afirma que o
`FOR UPDATE` na base vem antes do `DELETE` dos documentos, e que os dois estão na
mesma transação. Retirar o bloqueio reprova esse teste em 100% das execuções.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
  KnowledgeBases/
    Commands/
      DeleteKnowledgeBase/
        DeleteKnowledgeBaseCommand.cs          (novo)
        DeleteKnowledgeBaseCommandHandler.cs   (novo — D2, D3, D11)
        DeleteKnowledgeBaseResult.cs           (novo)
    Endpoints/KnowledgeBaseEndpoints.cs        (MapDelete; só o comentário de FolderInUse — D8)
  KnowledgeSync/
    KnowledgeBaseWriteFailures.cs              (novo — nomes das FKs para a base, lidos do banco, e a releitura — D6)
    Commands/UpsertSyncedDocument/UpsertSyncedDocumentCommandHandler.cs   (D6)
    Commands/DeleteSyncedDocument/DeleteSyncedDocumentCommandHandler.cs   (D6)
    Commands/RecordSyncResult/RecordSyncResultCommandHandler.cs           (D6)
  Auth/ (escopo de serviço — só o teste muda, a rota não entra no escopo)

apps/api/tests/Buteco.Api.Tests/
  Knowledge/KnowledgeBaseCatalogTests.cs              (sai o teste do 405)
  Knowledge/KnowledgeBaseCatalogTests.Deletion.cs     (novo, partial)
  Knowledge/KnowledgeDocumentCatalogTests.BaseDeletion.cs (novo, partial)
  ServiceScopeAuthorizationTests.cs                   (403 na rota nova)

01-ARQUITETURA_E_CONVENCOES.md   (seção acrescentada — D1)
docs/architecture.md             (seção acrescentada — D1)
02-HISTORICO_E_STATUS.md         (entrada da change)
CHANGELOG.md                     (Added / Changed)
```

Nada em `libs/`. Nada em `apps/workers`, `apps/connectors`, `apps/inbox` ou
`apps/frontend`.

## Risks / Trade-offs

- **[Exclusão irreversível de uma base com muito conteúdo]** → a pré-condição de
  base inativa (D2) põe um passo reversível antes; o log `1025` registra o que foi
  apagado; a confirmação na tela é da #136. Verificável: teste do `409` com a guarda
  da convenção 15.
- **[Corrida exclusão × inclusão deixa a exclusão em `500` pelo `Restrict`]** →
  `FOR UPDATE` antes de apagar documentos (D3). Verificável: o par determinístico do
  SQL emitido (D12).
- **[Escrita da sincronização em `500` ao perder para a exclusão]** → releitura da
  base nos handlers (D6). Verificável: um teste de janela por rota, com o
  interceptor afirmando que disparou.
- **[Impasse entre upsert e exclusão]** → repetição única nos dois lados (D6), e a
  ordem real dos comandos medida na implementação. Verificável: a medição registrada
  aqui e, se o impasse for alcançável, um teste que o provoque.
- **[Exclusão de base grande demora e segura o bloqueio]** → o `DELETE` em massa leva
  junto o TOAST dos vetores; durante ele, escritas naquela base esperam. Só aquela
  base; as outras não são tocadas. Aceito: a base está inativa e a operação é rara.
  A implementação mede o tempo de exclusão de uma base com volume de referência e
  registra no `02`.
- **[Corridas do operador contra a exclusão: `500` na inclusão de documento e na
  substituição de vínculos]** → **risco aceito** (D6). Exige o mesmo operador
  excluindo a base e, ao mesmo tempo, em outra aba, incluindo documento nela ou
  vinculando-a a um agente. O efeito é um `500` improvável e **sem perda de dado**:
  a gravação inteira falha no banco, nada é gravado pela metade, e repetir a ação
  responde como base inexistente (`404` na inclusão, `400` no vínculo). Sem teste
  nesta change, por decisão; o comportamento está lido no código e citado na
  tabela da D6. Se acontecer em uso real, vira issue.
- **[Mensagem do `409` de pasta em uso não cita a exclusão, embora a rota exista]**
  → aceito até a #136 (D8): a saída só é útil ao operador quando o painel a
  oferece, e a mensagem muda junto com o botão.
- **[A #105 edita os mesmos documentos]** → edições acrescentadas como seções
  próprias, nunca reescrita de trecho; registrado no `tasks.md`.
- **[Exclusão de outra base por id errado]** → todos os `DELETE` filtram pelo id da
  base travada; o teste afirma que outra base, com documentos, fragmentos, eventos e
  vínculos, continua intacta.

## Migration Plan

Sem migração de banco: o schema já tem todas as cascatas, e a FK de documento
continua `Restrict`. O deploy é só do `apps/api`. Rollback: reverter o código; bases
já excluídas não voltam, o que é a natureza da operação.

A ordem com a #105 não importa para a correção: sem esta change, a #105 nunca vê
base excluída; com ela, recebe `404`, que já trata.

## Open Questions

Nenhuma de negócio. A ordem dos comandos do EF na atualização por upsert (D6) é
medição de implementação, não pergunta aberta.
