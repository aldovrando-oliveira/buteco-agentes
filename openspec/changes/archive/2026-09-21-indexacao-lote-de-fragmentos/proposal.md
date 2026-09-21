## Why

O documento `02 HISTORICO E STATUS` **falhou a indexação três vezes seguidas** no
piloto (20/09/2026), com `502 upstream_error` do gateway de embedding, e o
`01` da mesma base indexou normalmente. `KnowledgeIndexingService.IndexAsync`
(`apps/workers/src/Buteco.Workers/Knowledge/Indexing/KnowledgeIndexingService.cs:73`)
chama `GenerateAsync(IEnumerable values)` com **todos os fragmentos do documento
numa única chamada**, e nada no caminho impõe teto.

As cinco medidas, com o regime colado (piloto de **20/09/2026,
`America/Sao_Paulo`**; os carimbos do banco estão em **UTC** e marcam 21/09 —
`2026-09-21 01:40:24+00` é 20/09 às 22:40 local), gateway de embedding do
`.env.prod`, modelo de 4.096 dimensões:

| documento | bytes | fragmentos | resultado | duração |
|---|---|---|---|---|
| `02` inteiro | 473.492 | ~442 | **falha, 3 de 3 tentativas** | — |
| `02` parte 1 | 268.181 | 267 | indexado | 8,9 s |
| `02` parte 2 | 205.310 | 175 | indexado | 14,1 s |
| `01` | 89.518 | 78 | indexado | **0,83 s** |
| `01` (reindexado) | 89.518 | 78 | indexado | **3,62 s** |

Cinco leituras que sustentam qualquer número escolhido:

1. **442 falha isolado.** Duas das três tentativas foram com o `02` sozinho, sem
   o `01` concorrendo — **não é disputa entre instâncias no gateway**.
2. **O `02` inteiro nunca indexou com sucesso** (`ContentRevision = 1`).
3. **`FragmentCount = 0`** no documento falhado: a falha acontece na chamada de
   embedding, **antes de qualquer persistência**.
4. **O mesmo `01`, trabalho idêntico, variou 4,4×** (0,83 s contra 3,62 s). O
   upstream é instável, e a variação não vem do nosso lado.
5. **As durações de 267 e 175 não são limpas** — as duas partes começaram no
   mesmo instante (`01:40:24,17` e `01:40:24,58`), em instâncias diferentes, e
   medem contenção junto com custo. A única duração isolada é a do `01`.

**O que NÃO se sabe, e fica escrito:** o **formato do teto do gateway** — por
número de entradas, por bytes do corpo, ou por tempo de resposta do upstream.
Nenhuma das três foi estabelecida. O que se sabe é o par: **442 falha, 267
passa.** As duas causas de pé não se excluem e apontam para a mesma correção: um
lote grande demora mais no upstream, e um upstream que varia 4× derruba o que
está mais perto do limite. O `01` passa sempre porque tem folga; o `02` falha
sempre porque não tem.

**Por que agora:** o `02` inteiro **não indexa hoje**, e isso foi medido — três
tentativas, `ContentRevision = 1`, nenhum fragmento gravado. O defeito está
aberto, e está aberto independentemente de qualquer outro plano.

> **Correção de premissa (convenção 9).** Esta seção dizia, originalmente, que o
> motivo era a **limpeza do banco**: a próxima etapa da fila seria limpar e
> deployar, a limpeza **custa reindexação de todo o conhecimento**, e sem o lote
> a reindexação reproduziria a falha em todo documento grande de uma vez, sem
> conteúdo anterior para preservar. **O plano mudou:** o dono decidiu **deployar
> sem limpar o banco**, e a reindexação em massa não vai acontecer. A premissa
> fica escrita em vez de apagada, porque ela é o que explica por que a change foi
> sequenciada onde foi — e porque o motivo que sobra é mais forte, não mais
> fraco: não depende de plano nenhum.

## What Changes

Tudo em **`apps/workers`**, exceto documentação de configuração e do stack.

- **Lotear a chamada ao gerador de embedding.** `IndexAsync` passa a chamar
  `GenerateAsync` uma vez por lote de fragmentos, **sequencialmente**,
  concatenando os vetores na ordem dos fragmentos. A conferência de contagem de
  vetores passa a ser **por lote**, além da total.
- **`Embedding:BatchSize`, nova opção de `apps/workers`, com padrão 250 —
  declaradamente provisório.** 250 porque 267 passou e 442 falhou, com margem;
  **não há teto medido**. A configuração existe para ser ajustada quando o
  piloto mostrar e **para permitir medir**: variar o parâmetro é como o teto vai
  ser descoberto.
- **Lote inválido reprova o boot** (`≤ 0`), no molde da convenção 8 — e não é
  corrigido em silêncio. É um parâmetro que **vai ser variado à mão em
  produção**, e o modo de errar é digitar `0`.
- **A política de tentativas não muda**: a retentativa continua sendo do
  **documento inteiro**, três execuções espaçadas por filas de espera. O motivo
  está no `design.md` (D2) e ele já estava decidido em D3 da
  `knowledge-base-indexacao`.
- **A persistência não muda**: substituição integral dos fragmentos em transação
  única, condicionada a `ContentRevision`. Só a chamada ao gateway é loteada.
- **Guarda vermelho** (convenção 15): um documento com mais fragmentos que o
  tamanho do lote produz **mais de uma** chamada ao gerador. Contra `HEAD`, uma
  só. Com o par que passa nos dois lados (documento menor que o lote → exatamente
  uma chamada) e o par "sem item" da convenção 5 (zero fragmentos → nenhuma
  chamada, com a precondição afirmada).
- **Configuração de stack e documentação**: `EMBEDDING_BATCH_SIZE` no
  `docker-compose.prod.yml` e no `.env.prod.example` (com default, **não**
  obrigatória), e as duas tabelas de `docs/configuration.md`.
- **Registros no `02-HISTORICO_E_STATUS.md`**, com gatilho e posição: o limite de
  documentos indexados simultaneamente, o texto de falha da tela de documentos, e
  as cinco medidas do gateway com o regime colado.

### Posição na fila

Esta change entra **na posição 5, antes do deploy**, empurrando
`replicas-de-worker` para 6 e `metricas-execucao-coleta` para 7.

O motivo é o do parágrafo "por que agora": **o `02` não indexa hoje**, com ou sem
limpeza de banco. Deployar as outras quatro correções deixando este defeito de
fora entregaria uma produção em que um documento grande da base continua sem
poder ser indexado — e o custo de incluí-lo no mesmo deploy é uma variável de
ambiente opcional.

O motivo **original** era outro — a limpeza do banco obrigaria a reindexar tudo —
e está registrado acima como premissa corrigida. A posição não mudou com a
correção, e vale dizer por quê: o defeito medido é anterior ao plano de limpeza e
sobrevive ao cancelamento dele.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `knowledge-document-indexing`: requisito novo — a geração de embedding é
  **loteada**, com tamanho configurável, lotes **sequenciais**, ordem dos vetores
  preservada, e lote inválido reprovando o boot. O **valor** do lote
  deliberadamente **não** entra na spec, pelo mesmo motivo que os parâmetros de
  fragmentação não entraram: não há medição que o otimize, e afirmar em spec um
  número não medido é a convenção 10 (requisito que passa verde sem provar nada).

## Impact

**`apps/workers`** (único app tocado — nenhuma referência nova entre apps):

- `Knowledge/Indexing/KnowledgeIndexingService.cs` — o laço de lotes.
- `Options/EmbeddingOptions.cs` — `BatchSize`, aditivo.
- `Knowledge/Indexing/EmbeddingBatchSizeValidation.cs` — **novo**, checagem de
  boot.
- `Program.cs` — uma chamada a mais.
- Testes: `Knowledge/Support/KnowledgeIndexingHarness.cs` (o duplo já conta
  chamadas; passa a guardar o tamanho de **cada** uma),
  `Knowledge/KnowledgeIndexingTests.cs`, e um arquivo novo de teste unitário para
  a checagem de boot — **sem `WorkerInfrastructureFixture`**, para não criar a
  15ª classe da `WorkerHostCollection` e não disparar a recalibração da convenção
  22 (hoje **14 classes**, referência de duração **6m37s com zero containers de
  dev**).

**Blast radius de assinatura: nenhum.** `EmbeddingOptions` ganha propriedade
(todos os sítios usam inicializador de objeto) e `FakeEmbeddingGenerator` ganha
membro. Medido por **compilação**, não por `grep` — a régua da convenção 18
mudou de ferramenta na oitava medição.

**Fora de `apps/workers`**: `docker-compose.prod.yml`, `.env.prod.example`,
`docs/configuration.md`, `02-HISTORICO_E_STATUS.md`, `CHANGELOG.md`.

**Não toca** `apps/api`, `apps/inbox`, `apps/frontend` nem `libs/`. Sem migração
de banco, sem mudança de contrato de fila, sem mudança de formato de fio.

### Non-Goals explícitos

- **Não limitar quantos documentos são indexados simultaneamente.** Fica
  registrado no `02` com gatilho e posição; é change maior, que mexe no ciclo de
  vida do documento e introduz componente periódico.
- **Não mexer em `prefetchCount` da fila de indexação nem em número de
  instâncias.** Em k8s o paralelismo é consequência do autoscaler, não
  configuração de app.
- **Não mudar o texto de falha da tela** — registrado no `02`, change de
  `apps/frontend` depois desta.
- **Não instrumentar embedding** (duração, falha, tokens). É a
  `metricas-execucao-coleta`.
- **Não tocar `apps/api`, `apps/inbox`, `apps/frontend` ou `libs/`.**
