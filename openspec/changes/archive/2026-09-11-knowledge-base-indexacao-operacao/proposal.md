## Why

A etapa 2a entregou o pipeline de indexação e **deixou de fora, de propósito, a
superfície de operação em `apps/api`**. O corte não foi arbitrário: a 2b não
toca `apps/workers`, a ordem entre as duas é forçada (a reindexação publica na
fila que a 2a criou), e a 2b é exatamente o que a etapa 5a-2 bloqueia.

Duas coisas faltam, e as duas têm consumidor nomeado:

- **Reindexar um documento.** Hoje não há caminho nenhum. Um documento que
  terminou em `Failed` por causa transitória — o 429 do provedor, que é o caso
  concreto de D3 da 2a — fica `Failed` para sempre, porque a única coisa que
  reenfileira é alterar o conteúdo, e o conteúdo está certo. O botão
  "Reindexar documento" da faixa de falha do protótipo depende desta rota.
- **Resumo de indexação por base.** A 5a-1 recusou as colunas `Documentos` e
  `Indexação` e o filtro `Com falha` (D1 e D9) por um motivo medido: a contagem
  exigiria **uma requisição por base**, e o handoff declara **100+ bases**. A
  recusa veio com o encaminhamento explícito — "o que falta vai como handoff
  para a etapa 2 (contagem em `KnowledgeBaseResponse`, ou rota de resumo)". Esta
  change decide entre as duas e entrega.

## What Changes

- **`POST /knowledge-bases/{knowledgeBaseId}/documents/{id}/reindex`.** Força o
  documento para `Pending` e publica na fila `knowledge-indexing` **pelo mesmo
  publisher e a mesma mensagem** que `CreateKnowledgeDocument` e
  `UpdateKnowledgeDocument` já usam (`IKnowledgeIndexingJobPublisher`,
  `KnowledgeIndexingJobMessage`) — nenhuma mensagem nova, nenhuma fila nova.
- **`GET /knowledge-bases/indexing-summary`.** Uma requisição para o conjunto
  inteiro, com `documentCount`, `indexedCount` e `failedCount` por base, e
  **uma linha para toda base do catálogo**, inclusive a que não tem documento
  nenhum e a que está inativa.
- **`KnowledgeBaseResponse` NÃO muda**, e isso é a decisão central (design.md,
  D1): o catálogo continua barato, e nenhum dos **seis** sítios que constroem
  esse record é tocado.
- **Duas exceções da reindexação, escritas como exceções** (design.md, D4):
  ela reindexa **apesar** de `ContentHash` inalterado — é o ponto —, e ela
  **zera** `IndexingAttempts`/`LastAttemptAt`, que a 2a definiu como "tentativas
  sobre a revisão corrente" e a revisão não muda aqui. As duas são bypass
  deliberado de regra vigente, não contradição, e o motivo do segundo **não é o
  que parecia** — ver Impact.
- **`ContentHash` e `ContentRevision` não são tocados** pela reindexação, e
  `IndexedAt`/`FragmentCount` são **preservados**: o conteúdo anterior continua
  respondendo até os fragmentos novos serem gravados.

Fora de escopo, explicitamente: qualquer mudança em `apps/workers`,
`apps/inbox` ou `apps/frontend`; as telas (**5a-2**); a coluna
`Consultada por` (**5b**, e a 5a-1 já registrou que ela não está bloqueada);
o diagnóstico do índice (**5c**); a tool de busca (**etapa 4**).

## Capabilities

### New Capabilities

Nenhuma. Os dois requisitos cabem em capabilities existentes, e inventar uma
terceira para duas operações contrariaria a convenção 2 — a régua que separou
`mcp-tool-execution` de `mcp-server-catalog` pede um domínio próprio, não um par
de rotas.

### Modified Capabilities

- `knowledge-document-indexing`: ganha **duas operações**. (a) *Reindexação sob
  pedido do operador* — a única entrada que reenfileira sem o conteúdo ter
  mudado, com as duas exceções ditas como exceções e com a regra de que
  `IndexedAt` e `FragmentCount` sobrevivem. (b) *Resumo de indexação por base* —
  a agregação dos estados `Indexed` e `Failed` por base. A agregação mora aqui, e
  não no catálogo, porque quem é dono do significado de `Indexed` e `Failed` é
  quem tem de ser dono da contagem deles: se a máquina de estados ganhar um
  valor, é o contrato do resumo que muda junto.
- `knowledge-base-catalog`: ganha **um requisito negativo** — a resposta de base
  não carrega contagem agregada, e o resumo vive em recurso próprio. Requisito e
  não só decisão de design porque é exatamente a coisa que alguém "corrige" sem
  ver a causa, pelo mesmo motivo que a 5a-1 registrou D9 em separado de D1.

## Impact

- **`apps/api`**: dois grupos de endpoint ganham uma rota cada, dois handlers
  novos (um comando, uma consulta), um response novo, e um método novo na
  entidade `KnowledgeDocument` (`RequestReindex`). Nenhuma migração — nenhuma
  coluna nova, nenhuma tabela nova. **Blast radius verificado no código, e ele é
  o argumento de D1**: `KnowledgeBaseResponse.FromEntity` tem **6 sítios de
  chamada** — `ListKnowledgeBases`, `GetKnowledgeBaseById` e os **quatro**
  handlers de comando (`Create`, `Update`, `Activate`, `Deactivate`). O record é
  posicional, então acrescentar campo **quebra a compilação nos seis**; os quatro
  de comando teriam de fazer uma consulta agregada que não tem nada a ver com o
  que eles fazem, ou preencher zero — que em `Update`/`Activate`/`Deactivate`
  seria falso, e a convenção 13 o proíbe. A rota própria custa **zero** desses
  seis.
- **`apps/workers`**: nenhuma mudança de código, e é verificado, não assumido. A
  mensagem publicada é a que o consumidor já lê, com `Attempt = 1`, que é o
  default do record.
- **`apps/inbox`** e **`apps/frontend`**: nenhuma mudança.
- **Rotas**: duas rotas novas, ambas autenticadas — nenhuma entra na allowlist
  de rotas anônimas. `GET /knowledge-bases/indexing-summary` é segmento
  literal e não colide com `GET /knowledge-bases/{id:guid}`: a restrição `:guid`
  já exclui a cadeia, e o roteamento do ASP.NET Core classifica segmento literal
  acima de segmento de parâmetro de qualquer forma.
- **Dependências**: nenhuma nova.
- **Uma premissa do enunciado foi desmentida pelo código, e a correção muda o
  motivo, não a decisão** (convenção 6). O enunciado supunha que reindexar sem
  zerar `IndexingAttempts` deixaria o documento "com o limite esgotado, tornando
  o botão inútil". Lido em `apps/workers`, o limite **não** mora nessa coluna:
  `KnowledgeIndexingService.cs:116` compara `message.Attempt <
  KnowledgeIndexingQueues.MaxAttempts`, e `:137` faz
  `SetProperty(d => d.IndexingAttempts, message.Attempt)` — **atribuição, não
  incremento**, ou seja, o consumidor sobrescreve a coluna no começo de cada
  execução. Uma reindexação com `Attempt = 1` ganharia três execuções novas
  mesmo sem zerar nada. O reset continua entrando, por uma razão diferente e
  verificável — a janela entre o `POST` responder e o consumidor pegar a
  mensagem, em que a tela afirmaria uma rodada encerrada que acabou (design.md,
  D4).
- **Sequenciamento**: depende da 2a (arquivada em
  `openspec/changes/archive/2026-09-11-knowledge-base-indexacao/`). A **5a-2**
  depende desta. A convenção 1 foi conferida no sentido inverso — se a 2b
  precisasse de algo que a 2a não serve, seria achado a sequenciar — e **não
  achou nada**: o publisher, a mensagem, a máquina de estados e os campos por
  documento que o resumo agrega já estão todos entregues.
