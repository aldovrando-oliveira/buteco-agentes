## Why

A etapa 1 (`knowledge-base-catalogo-documentos`) entregou o catálogo e declarou,
em spec, que todo documento nasce `Pending` e **permanece `Pending`
indefinidamente**, porque não existe consumidor. Não é defeito: é requisito
escrito para que esta etapa encontre um estado esperado em vez de um bug
aparente. Esta change é o consumidor.

Sem ela nada é indexado, e a etapa 4 (resolvedor de tool) não tem o que
consultar: os valores `Indexing`, `Indexed` e `Failed` do enum, e as colunas
`IndexedAt` e `FailureReason`, nascem sem nenhum escritor na etapa 1, de
propósito. Quem os escreve é esta change.

Esta é a **etapa 2a de 5** da linha de bases de conhecimento. A superfície de
operação em `apps/api` — rota de reindexação e resumo de indexação por base —
sai para a **2b**, pelo corte que a exploração fixou e que se sustenta em três
coisas verificáveis: a 2b não toca `apps/workers`; a 2b é exatamente o que a
etapa 5a-2 bloqueia; e a ordem é forçada, porque a reindexação publica na fila
que esta change cria.

Duas rodadas de medição precederam esta proposta e estão registradas em
`02-HISTORICO_E_STATUS.md`: `0b` (modelo, dimensão, schema, mecanismo do "não
encontrei") e `0c` (chunker corrigido, invariantes, overlap, `k`, limiar). O que
elas fecharam entra aqui **decidido**; o que elas explicitamente **não**
certificaram — um número de recall — não vira requisito de spec.

## What Changes

- **`apps/api` ganha a entidade `KnowledgeFragment`** e a migração que cria a
  tabela e a extensão `vector`. É o app que **não escreve** na tabela: escreve
  `apps/workers`. A migração nasce aqui porque o `migrator` do compose só
  empacota bundles de `apps/api` e `apps/inbox`, e `apps/workers` nunca aplica
  migração a banco real (regra operacional já registrada, com o `42P07`
  reproduzido).
- **Coluna de verdade `vector(4096)`, sem índice ANN.** A derivada indexável
  `halfvec(3072)` por `subvector` em coluna gerada **não** nasce agora — nasce
  quando o índice fizer falta, sem chamar o gateway e sem reembedar. O gatilho é
  medido: busca exata acima de 200 ms no p95.
- **`apps/workers` ganha o pipeline de indexação**: chunker, resolvedor de
  gerador de embedding, fila própria com publisher e consumidor, e a máquina de
  estados `Pending → Indexing → Indexed | Failed`.
- **Fila própria, não `agent-tasks`.** `TaskJobConsumer` roda com
  `prefetchCount: 1`, e `AgentDelegationConcurrencyTests` existe justamente para
  provar que isso torna a execução serializada dentro de uma instância.
  Indexação de minutos na mesma fila não é lentidão — é a mesma classe de
  bloqueio.
- **`KnowledgeDocument` ganha quatro colunas**: `ContentHash` (conteúdo idêntico
  numa atualização não volta a `Pending` e não gasta embedding), `FragmentCount`
  (exibida quando `indexedAt` não é nulo, **omitida** quando é nulo, nunca
  zerada), `IndexingAttempts` e `LastAttemptAt` (o alerta da etapa 5a-2 mostra
  "429 nas três tentativas, a última às 03:14" — os dois são requisito, não
  formatação de tela).
- **`FailureReason` passa a carregar texto legível por operador**, não exceção
  crua. A tela da 5a-2 mostra o motivo **completo, sem truncar** — é a única
  cópia de falha que ela tem.
- **`ContentRevision` ganha o consumidor que nunca teve.** O consumidor lê a
  revisão no início, faz o trabalho lento, e grava condicionado a ela ainda ser
  a corrente; se mudou, descarta o resultado inteiro.
- **Checagem de integridade do índice no boot de `apps/workers`** — quarto caso
  da convenção 8, bidirecional, e o primeiro desta base que consulta o banco.
- **Testcontainers: 11 dos 15 sítios trocam** `postgres:18` por
  `pgvector/pgvector:pg18`. Os outros 4 ficam.
- **Duas dependências novas**: `Pgvector` e `Pgvector.EntityFrameworkCore`.

Fora de escopo, explicitamente: rota de reindexação e resumo de indexação em
`KnowledgeBaseResponse` (**2b**); resolvedor de tool e a tool de busca
(**etapa 4**); qualquer UI (**5a-2**); índice ANN; busca híbrida; reescrita de
query; limiar de distância.

## Capabilities

### New Capabilities

- `knowledge-document-indexing`: o pipeline que transforma documento em
  fragmentos consultáveis — fragmentação com os três invariantes medidos,
  geração de embedding, enfileiramento e consumo assíncrono, máquina de estados
  de indexação, substituição integral em transação única, descarte por revisão
  de conteúdo concorrente, política de tentativas com motivo legível, e
  integridade entre o modelo declarado e o que está gravado no índice.
  Capability própria, e não requisito acrescentado ao catálogo, pelo mesmo
  motivo que `mcp-tool-execution` é separada de `mcp-server-catalog`: o
  catálogo é cadastro, isto é execução, e quem consome o índice é outra
  capability de outra etapa.

### Modified Capabilities

- `knowledge-document-catalog`: **duas mudanças de comportamento em spec, não de
  implementação.** (a) O requisito "documento permanece `Pending`
  indefinidamente" **deixa de valer** — era o contrato da ausência de consumidor,
  e o consumidor passa a existir; o cenário que o afirma é substituído pelo que
  afirma a transição. (b) O formato de fio dos valores de estado, que hoje só
  consegue afirmar `"Pending"` porque os outros três não tinham como aparecer
  numa resposta real, passa a cobrir os quatro — extensão pedida nominalmente
  pela etapa 1. As colunas novas de resposta (`fragmentCount`,
  `indexingAttempts`, `lastAttemptAt`) entram junto, com a regra de omissão de
  `fragmentCount` que a etapa 1 já fixou.

## Impact

- **`apps/api`**: uma entidade (`KnowledgeFragment`), quatro colunas novas em
  `KnowledgeDocument`, dois campos nos dois responses de documento, e uma
  migração que cria a tabela **e a extensão `vector`**. Nenhum handler novo:
  esta change não abre rota. **Blast radius verificado**:
  `KnowledgeDocumentResponse` e `KnowledgeDocumentSummaryResponse` são records
  posicionais, então campo novo **quebra a compilação** em cada site de
  construção — é o mecanismo que garante que nenhum caminho fique defasado, o
  mesmo que a etapa 3 usou com `AgentResponse`.
- **`apps/workers`**: o pipeline inteiro, mais o espelho de EF Core da entidade
  nova, mais a migração equivalente de design-time. É a primeira change em que
  `apps/workers` **escreve** numa tabela do domínio de conhecimento — até aqui
  ele só espelhava schema.
- **`apps/inbox`**: nenhuma mudança de código. Banco próprio, sem tabela em
  comum. Mas o compose **não separa os apps**: há um servidor Postgres
  hospedando dois bancos (`buteco_agents` e `buteco_inbox`), então trocar a
  imagem do serviço troca o servidor que também hospeda `buteco_inbox`. É
  inócuo — a extensão é por banco —, e está dito para não sugerir um isolamento
  que o compose não tem.
- **`apps/frontend`**: nenhuma mudança. Os campos novos são aditivos e as
  interfaces TS não validam schema em runtime. As telas são a 5a-2.
- **Rotas**: nenhuma rota nova, nenhuma alteração na allowlist de rotas
  anônimas.
- **Dependências**: `Pgvector` 0.3.2 e `Pgvector.EntityFrameworkCore` 0.3.0,
  ambas em `Directory.Packages.props`. A segunda declara `net8.0` e
  `Npgsql.EntityFrameworkCore.PostgreSQL >= 9.0.1`, contra os `net10.0` e
  10.0.3 deste repositório — **verificado end-to-end contra `pgvector:pg18`
  real** antes desta proposta, e registrado como risco com gatilho.
- **Deploy**: `CREATE EXTENSION vector` exige superusuário (a extensão não é
  `trusted`). Funciona hoje porque o `migrator` conecta como
  `${POSTGRES_USER}`, que é o superusuário de bootstrap do compose. É
  restrição a declarar, não propriedade a assumir.
- **Configuração**: seção `Embedding` nova em `apps/workers`, reusando a
  credencial já registrada. **Não existe hoje nenhuma configuração de embedding
  no repositório** — esta change a cria, não troca um default.
- **Sequenciamento**: depende da etapa 1 (entregue) e das rodadas `0b`/`0c`
  (fechadas). A **2b** depende desta. A etapa 4 depende das duas.
