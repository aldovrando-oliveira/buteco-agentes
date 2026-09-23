## Why

Esta é a **etapa 2** da linha `metricas-de-operacao` — a coleta de embedding —, e
ela fecha o catálogo de tokens. A etapa 1 (`metricas-execucao-coleta`) está
aplicada e **medindo desde 22/09/2026 às 01:21, `America/Sao_Paulo`**, e cobre
tokens de **conversa** (M11–M17). O card **"Tokens de embedding" (M19) da página
Insights aprovada não tem fonte nenhuma hoje**, e **M30 (falhas de indexação)
tampouco**: `knowledge_documents` guarda `IndexingStatus`, `FailureReason`,
`IndexingAttempts` e `LastAttemptAt`, mas todos os quatro são **estado corrente,
sobrescrito a cada tentativa** — uma tentativa que falhou e depois deu certo não
deixa rastro, e um contador de falhas por período não tem de onde sair.

A métrica não é retroativa. Cada dia sem coleta é um dia que a tela nunca vai
mostrar — e aqui isso já custou: o `502 upstream_error` que motivou a
`indexacao-lote-de-fragmentos` precisou ser reproduzido à mão, porque **o status
não ficou gravado em lugar nenhum**.

## Reverificação (obrigatória — duas changes entraram depois da exploração de 20/09)

Os quatro itens foram conferidos contra `HEAD` (`b95e3a6`). **Dois confirmaram a
decisão registrada, um a contrariou e um produziu achado** (convenção 9 — o
registro contrariado é corrigido com a causa, no `design.md`):

| # | o que se conferiu | resultado |
|---|---|---|
| 1 | indexação loteada (`Chunk(BatchSize)`, padrão 250) | **confirma**: uma indexação produz N chamadas, e o grão da linha é o lote (D3) |
| 2 | `HttpStatusOf` serve o SDK do gateway de embedding | **confirma, sem trabalho novo**: `ExecutionMetricsScope.HttpStatusOf` já tem o braço `ClientResultException { Status: > 0 }`, e é essa a exceção do caminho `openai`. Reusável como está (D6) |
| 3 | tornar `TaskId` de `provider_calls` anulável | **contraria D7 da etapa 1**: a decisão de M11 × M19 serem visões separadas obriga **toda** consulta da tabela a filtrar por `Purpose`, e a tabela passa a ser duas. Tabela própria, com o motivo escrito (D1) |
| 4 | `KnowledgeIndexingFailure` serve para M30 | **achado, com defeito medido junto**: ela é classificador de **texto de tela**, e no único provedor implementado os braços de `429`/`401`/`403` são **inalcançáveis** — verificado por execução: `ClientResultException` deriva de `Exception`, **não** de `HttpRequestException`. Todo erro HTTP do gateway cai hoje no balde genérico. M30 grava **fase**, não texto (D5) |

## What Changes

**Coleta durável de embedding, sem rota e sem tela.** Escrita em `apps/workers`;
a migração sai **só** de `apps/api`, e `apps/workers` espelha (precedente
`KnowledgeFragment` e `AddExecutionMetrics`).

- **Tabela `knowledge_indexing_attempts`** (`apps/api` migra, `apps/workers`
  escreve) — uma linha por **tentativa** de indexação que chegou a contar
  tentativa: documento, base, revisão de conteúdo, número da tentativa e o
  máximo, início e fim, desfecho (`Indexed` | `RetryScheduled` | `Failed` |
  `Discarded`), a **fase** da falha e a contagem de fragmentos quando indexou. É
  a fonte de **M30**, e é o pai das linhas de indexação.
- **Tabela `embedding_calls`** — uma linha por **chamada ao gateway de
  embedding**: finalidade (`Indexing` | `Search`), provedor, modelo, **dimensão**,
  número de entradas da chamada, duração, tokens reportados (**anuláveis**, nulo
  ≠ zero), se falhou e o status HTTP quando tipado. É a fonte de **M19**.
- **A finalidade separa indexação de busca**, e **as duas entram nesta etapa**.
  São dois consumidores do mesmo gateway com perguntas diferentes — indexação é
  custo de cadastro, busca é custo por conversa —, e a **busca é o termo
  dominante** (um vetor por **mensagem** de agente, contra um por documento
  indexado). Um M19 que cobrisse só indexação mostraria a metade menor sob o
  rótulo do todo. A busca roda **dentro** de execução de task, então carrega
  `TaskId` e é alcançada pelo `AsyncLocal` que a etapa 1 já abriu — custo de
  captura próximo de zero (D4).
- **A fase da falha de indexação vira vocabulário fechado** em
  `KnowledgeIndexingService`, determinada por onde o código estava, no mesmo
  molde do `FailurePhase` de `task_executions`. O texto de operador
  (`KnowledgeIndexingFailure.Describe`) **fica como está** e continua indo para
  a tela de documentos — os dois não se substituem (D5).
- **Degradação graciosa:** falha ao gravar métrica é registrada em log de aviso
  e **nunca** altera o desfecho da indexação, o estado do documento nem os
  fragmentos gravados. A escrita acontece **depois** do estado terminal do
  documento (D7).
- **Registro, sem correção:** o defeito de alcance de
  `KnowledgeIndexingFailure.Describe` (item 4) é **registrado e não corrigido
  aqui** — corrigi-lo muda texto de tela, que é mudança de comportamento
  observável e não cabe numa change de coleta. O achado é **absorvido no item que
  o `02` já tem** sobre o mesmo texto, aberto pela `indexacao-lote-de-fragmentos`
  — mesmo arquivo, mesmo braço, duas causas —, com **gatilho cumprido** e a
  posição recalibrada, porque a correção deixa de ser só de `apps/frontend`
  (D5).

## Capabilities

### New Capabilities

- `knowledge-embedding-metrics`: coleta durável de chamada ao gateway de
  embedding (indexação e busca) e de tentativa de indexação, gravada por
  `apps/workers` em tabelas migradas por `apps/api`, com nulo preservado, com o
  status HTTP quando o SDK o expõe tipado, e sem efeito sobre o desfecho da
  indexação nem sobre o resultado da busca.

### Modified Capabilities

Nenhuma. A coleta é aditiva e graciosa por construção: nenhum requisito de
`knowledge-document-indexing` e de `knowledge-tool-execution` muda de valor —
nem o desfecho da indexação, nem a contagem de tentativas, nem o texto de falha
na tela, nem o que a tool de busca devolve ao modelo. **Se a implementação
descobrir que algum deles muda, isso é achado a reportar, não spec a ajustar em
silêncio.**

## Impact

- **`apps/workers`** (escrita): `Knowledge/Indexing/KnowledgeIndexingService.cs`
  (fase + abertura do escopo + as duas escritas),
  `Knowledge/Execution/KnowledgeToolSetResolver.cs` (linha da busca),
  `Knowledge/Embedding/` (medição da chamada ao gerador),
  `Infrastructure/AppDbContext.cs`, pasta nova `EmbeddingMetrics/`, e a migração
  **espelho** — que nunca roda contra banco real, só contra Testcontainers.
- **`apps/api`** (só migração): duas entidades de mapeamento, `AppDbContext`, a
  migração, o snapshot e um teste de migração. **Nenhuma rota.**
- **`apps/frontend`, `apps/inbox`, `libs/`:** nada.
- **Banco:** duas tabelas novas. `provider_calls` **não muda** — `TaskId`
  continua obrigatório e a chave estrangeira continua valendo para toda linha.
  `knowledge_documents`, `knowledge_fragments` e `a2a_tasks` não mudam.
- **Operação:** nenhuma variável de ambiente nova. `EMBEDDING_BATCH_SIZE` e a
  política de tentativas não são tocadas. A data em que a coleta de embedding
  começa em produção é registrada no `02` no deploy, com o fuso — é um **segundo
  regime** da linha, e não o mesmo da etapa 1.

### Non-Goals explícitos

- **Nenhuma rota de agregação, nenhuma tela** — etapas 3 a 5.
- **M35 (acessos a base de conhecimento) continua adiada.** A linha de busca dá
  provedor, modelo, duração e tokens — **não** dá o que M35 pede (qual base, com
  que resultado, com que relevância). Ganhar parte da fonte antes da hora não
  fecha a métrica, e esta change não afirma que fecha.
- Não mexer no `EMBEDDING_BATCH_SIZE` nem na política de tentativas.
- Não coletar uso de MCP (M33); não tocar o bloco do inbox (M36–M39).
- Não remover o detector de tasks não-terminais — a condição de remoção é M32,
  na etapa 3.
- Não mexer em número de instâncias de `apps/workers`, na varredura de
  `PendingDispatch`, nem no bloco do inbox.
- Não corrigir o `TZ` de `apps/api` — é pré-requisito da etapa 3.
- **Não corrigir `KnowledgeIndexingFailure.Describe`** — absorvido no item que o
  `02` já tem, sequenciado e fora daqui (ver *Registro, sem correção*).
