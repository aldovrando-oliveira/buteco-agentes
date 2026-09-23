## Context

Etapa 2 da linha `metricas-de-operacao`. A etapa 1 está aplicada e medindo desde
**22/09/2026 às 01:21, `America/Sao_Paulo`**, e entregou `task_executions`,
`provider_calls` e `delegation_outcomes` — tokens de **conversa**. Faltam **M19**
(tokens de embedding) e **M30** (falhas de indexação), e nenhuma das duas tem
fonte hoje.

O consumo de embedding tem **dois** produtores, e os dois falam com o mesmo
gateway:

| produtor | onde | frequência | tem `TaskId`? |
|---|---|---|---|
| **indexação** | `KnowledgeIndexingService.IndexAsync`, consumidor e fila próprios | um lote de até `BatchSize` fragmentos por chamada, N chamadas por documento | **não** — indexação não roda dentro de execução de task |
| **busca** | `KnowledgeToolSetResolver.SearchAsync`, dentro do turno do agente | **um vetor por mensagem** que chama a tool | **sim** |

O estado durável de indexação que existe hoje é **corrente, não histórico**:
`knowledge_documents.IndexingStatus`, `FailureReason`, `IndexingAttempts` e
`LastAttemptAt` são sobrescritos a cada tentativa. Uma tentativa que falhou e
depois deu certo não deixa rastro nenhum, e M30 ("falhas de indexação" num
período) não tem de onde sair.

### Reverificação — o que mudou desde a exploração de 20/09, conferido contra `b95e3a6`

**1. A indexação é loteada — confirma.** `GenerateInBatchesAsync` percorre
`fragments.Chunk(options.BatchSize)` sequencialmente (padrão 250,
`EMBEDDING_BATCH_SIZE`). Uma indexação de 442 fragmentos produz **duas**
chamadas. O grão da linha é o lote (D3).

**2. `HttpStatusOf` serve, e não há trabalho novo — confirma.** O caminho
`openai` de `EmbeddingGeneratorResolver` constrói `OpenAIClient` sobre
`System.ClientModel`, e `ExecutionMetricsScope.HttpStatusOf` **já** tem o braço
`ClientResultException { Status: > 0 }`, acrescentado pela etapa 1. Verificado
por execução (convenção 6), refletindo sobre os assemblies contra os quais o
repositório compila:

```
### ClientResultException chain:
  System.ClientModel.ClientResultException
  System.Exception
  System.Object
### Status prop: System.Int32
```

`ClientResultException` **deriva de `Exception`, não de
`HttpRequestException`** — então a ordem dos braços do `switch` não é problema
aqui (o caso do Gemini, que motivou a correção da `compactacao-historico`, era
ocultação de propriedade em tipo **derivado**; este não é). **O método é
reusado como está, sem cópia e sem mudança** (D6).

**3. `TaskId` anulável em `provider_calls` — contraria D7 da etapa 1.** A etapa 1
registrou que a etapa 2 tornaria `TaskId` anulável e reusaria a tabela. Está
errado, e a causa é uma decisão que a própria etapa 1 registrou do outro lado:
**M11 e M19 são visões separadas**. Ver D1 — o registro é corrigido lá, com a
causa (convenção 9).

**4. `KnowledgeIndexingFailure` para M30 — achado, com defeito medido junto.**
Ela é classificadora de **texto de tela** — a `delegacao-diagnostico` já a
recusou para log por isso. Para M30 o consumidor **é** a tela, então valia
conferir se serve. **Não serve, e o motivo é mais forte do que o esperado:**
o `switch` casa `HttpRequestException` com `StatusCode` de `429`, `401` e `403`,
e o único provedor de embedding implementado **nunca lança esse tipo quando o
gateway responde com status**. Como o item 2 verificou, a exceção do caminho
`openai` é `ClientResultException`, que não deriva de `HttpRequestException`.
Consequência: **os três braços com status são inalcançáveis no caminho real**, e
todo erro HTTP do gateway — inclusive o `502 upstream_error` que motivou o
loteamento — cai no balde genérico *"A indexação falhou por um erro interno"*.

Isso é achado desta reverificação, **não** é corrigido aqui, e a condição de
correção está em D5.

## Goals / Non-Goals

**Goals:**

- Uma linha durável por **chamada ao gateway de embedding**, nas duas
  finalidades, com dimensão, duração, tokens anuláveis, falha e status HTTP
  quando tipado. É M19.
- Uma linha durável por **tentativa de indexação**, com o número da tentativa, o
  desfecho e a **fase** da falha. É M30.
- Nulo preservado ponta a ponta: nulo é "o provedor não reportou", zero é "o
  provedor reportou zero" (convenção 13).
- Nenhum efeito sobre o que a indexação e a busca faziam antes.

**Non-Goals:**

- Nenhuma rota, nenhuma tela, nenhuma agregação (etapas 3 a 5).
- **Não fechar M35.** A linha de busca não carrega o que M35 pede.
- Não corrigir `KnowledgeIndexingFailure.Describe` (D5), não mexer no
  `EMBEDDING_BATCH_SIZE` nem na política de tentativas, não coletar M33, não
  remover o detector de tasks não-terminais, não corrigir o `TZ` de `apps/api`.

## Decisions

### D1 — Tabela própria para embedding, e não `TaskId` anulável em `provider_calls`

**Decidido: duas tabelas novas, `embedding_calls` e
`knowledge_indexing_attempts`. `provider_calls` não muda.**

**Isto corrige D7 da etapa 1** (convenção 9). D7 concluiu *"indexação não roda
dentro de task, então `TaskId` passa a anulável e ganha uma referência de
documento; `DROP NOT NULL` não reescreve a tabela"*. A conclusão pesou o custo
**de migração** — que de fato é baixo — e não pesou o custo **de consulta**, que
é o que decide. A causa é uma decisão que a própria etapa 1 registrou do outro
lado do documento:

> **M11 e M19 são visões separadas**, por decisão do dono: o modelo de embedding
> é configuração de processo, não escolha de agente. Só se somam **no nível do
> provedor** (M14).

Se as linhas de embedding entrarem em `provider_calls`, então **toda** consulta
de M11 a M17 — tokens de conversa, entrada × saída, por agente, por modelo,
ranking de modelos, tokens por task — passa a precisar de `WHERE Purpose NOT IN
('Indexing','Search')`. Nenhuma precisa disso hoje. Uma tabela em que **toda**
consulta existente tem de filtrar por um discriminador já é duas tabelas, e a
versão em que alguém esquece o filtro não estoura: devolve um número **maior** e
plausível, com o modelo de embedding em primeiro lugar no ranking — exatamente o
que a decisão do dono existe para impedir.

Os outros custos, que sozinhos não decidiriam mas se somam a esse:

- `TaskId` é a coluna pela qual `provider_calls` é indexada e ligada ao pai. Ela
  passaria a ser nula em provavelmente **a maioria** das linhas (ver D2: a busca
  é um vetor por mensagem), e a chave estrangeira deixaria de valer justamente
  para elas.
- Quatro colunas que só embedding tem (dimensão, número de entradas do lote,
  tentativa de indexação, base de conhecimento) ficariam nulas em toda linha de
  conversa, e `OutputTokens`/`CachedInputTokens` nulas em toda linha de
  embedding.

**Custo aceito, e ele é um só:** M14 (total por provedor, o **único** nível em
que os dois se somam) vira `union` de duas somas em vez de uma soma. É uma
consulta, num card, e ela fica **explícita** sobre estar somando duas coisas
diferentes — que é o que o catálogo diz que ela está fazendo.

**Alternativa recusada:** duas tabelas separadas para indexação e busca
(`indexing_embedding_calls` e `search_embedding_calls`). Elas compartilhariam
nove das onze colunas, M19 viraria `union` de duas e M14 `union` de três. A
distinção entre elas é uma **coluna** de vocabulário fechado, não uma tabela
(D2).

**Convenção 2 conferida:** a tabela nova não é abstração prematura — ela tem
escritor no mesmo commit, consumidor nomeado (M19, M30) e o motivo da recusa da
alternativa está escrito acima, que é o que a convenção exige.

### D2 — A busca entra nesta etapa, e a finalidade é o que a separa da indexação

**Decidido: `Purpose` ∈ {`Indexing`, `Search`}, e as duas gravam.**

Ficar só com indexação era a opção barata, e ela custa mais do que parece. São
três argumentos, em ordem de peso:

1. **A busca é provavelmente o termo dominante, não a metade menor.** Indexação
   gera vetor por **documento cadastrado**; busca gera um vetor por **mensagem**
   de agente que chama a tool. É o mesmo argumento que já está escrito no
   `default:` de `EmbeddingGeneratorResolver`, sobre outro assunto: *"desde a
   etapa 4 da linha de conhecimento, a consulta gera um embedding por MENSAGEM
   de agente, não um por documento indexado"*. Um card rotulado **"Tokens de
   embedding"** que mostrasse só indexação não estaria mostrando metade — estaria
   mostrando a parte pequena com o rótulo do todo.
2. **Consertar isso na tela é pior.** Sem a busca, ou o protótipo aprovado muda
   de rótulo na etapa 4, ou a tela afirma mais do que sabe (convenção 13).
   Nenhum dos dois é melhor do que gravar a linha agora.
3. **O custo de captura é próximo de zero, e a etapa 1 já escreveu isso.** D7 da
   etapa 1: *"A busca de conhecimento **dentro** de uma task (um vetor por
   mensagem) já cabe sem mudança: roda no fluxo da execução, e o `AsyncLocal` a
   alcança."* Continua valendo — é o único ponto em que embedding e turno se
   encontram, e a máquina já está montada.

**O que isto NÃO faz: não fecha M35** (acessos a base de conhecimento), que
continua adiada. A linha de busca grava provedor, modelo, dimensão, duração,
tokens e falha da **chamada ao gateway**. M35 pergunta outra coisa — qual base
foi consultada, com que resultado, com que relevância, e quantas vezes o agente
consultou sem achar nada. `KnowledgeBaseId` na linha dá o primeiro pedaço e
nenhum dos outros. Quem for fazer M35 começa daí e não daqui.

### D3 — Grão: uma linha por **chamada ao gateway**, isto é, por lote

**Decidido: uma linha por chamada, não uma por documento com a soma.**

- É o grão em que o **`502` é atribuível**. Uma linha por documento diria "a
  indexação falhou" e perderia *qual* lote, com quantas entradas, em que
  duração — que é a medida que faltou em 20/09 e custou uma exploração.
- É o mesmo grão de `provider_calls` ("uma linha por requisição HTTP"), então as
  duas tabelas respondem "duração da chamada" e "chamadas por unidade de
  trabalho" com a mesma fórmula.
- **Uma indexação que falha no segundo lote gravou o primeiro, e isso é
  correto**: os tokens do primeiro lote foram consumidos e cobrados, mesmo com o
  documento terminando em `Failed` e nenhum fragmento gravado. Uma soma por
  documento teria de escolher entre mentir (somar um prefixo como se fosse o
  documento) ou perder (não gravar nada). O grão por lote não escolhe.

**Consequência a declarar na etapa 4:** tokens de embedding de indexação **não**
são "tokens dos documentos indexados" — incluem o consumo de tentativas que
falharam. É a definição certa para uma métrica de custo, e a tela diz qual é.

**O grão da linha não é o momento da escrita.** Uma linha por chamada não
implica gravá-la dentro do laço de lotes: o acumulador da tentativa guarda as
linhas e a escrita acontece uma vez, depois do estado terminal do documento. A
alternativa — gravar dentro do laço — está pesada e recusada em **D7**, onde a
escrita mora.

### D4 — Onde a captura acontece: **nenhum `AsyncLocal` novo**

A pergunta era "o escopo equivalente ao da etapa 1 abre onde?". A resposta é que
**para indexação ele não abre**, e isso é decisão, não omissão.

A etapa 1 precisou de `AsyncLocal` por um motivo específico e escrito em D2 dela:
quem mede a requisição (`LlmCallDurationChatClient`) é **compartilhado por
`(provider, model)` durante a vida do processo** e não sabe de qual task é a
chamada; e o caminho entre `ExecuteAsync` e `GetResponseAsync` atravessa o
`FunctionInvokingChatClient`, tools e a estratégia de compactação.

Na indexação **nada disso vale**: o laço de lotes (`GenerateInBatchesAsync`) é um
método privado da mesma classe que é a unidade de trabalho (`IndexAsync`). O
acumulador é uma **lista local**, passada como parâmetro. Montar um `AsyncLocal`
para alcançar um método privado da própria classe seria maquinaria sem motivo
(convenção 2), e maquinaria estática tem custo real: o `Begin` que sobrescreve,
o `lock`, o descarte que restaura.

Para a **busca**, o `AsyncLocal` que serve já existe e não é novo: é o
`ExecutionMetricsScope` da etapa 1, aberto em `AgentExecutionService.ExecuteAsync`
(`:187`). Ele ganha uma lista de `EmbeddingCall` e um
`RecordEmbeddingCall`, no molde exato de `RecordProviderCall` — inclusive o
no-op fora de escopo. **Esta change não cria `AsyncLocal` nenhum.**

**Onde a medida é tomada, nos dois casos:** um
`MeasuredEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>`,
fino, que cronometra `GenerateAsync`, lê `GeneratedEmbeddings<T>.Usage`, captura
a exceção com `ExecutionMetricsScope.HttpStatusOf` e **relança**, entregando a
linha a um `Action<EmbeddingCall>` recebido no construtor. Ele é aplicado **nos
dois sítios de chamada** (o laço de lotes e `SearchAsync`), envolvendo o que
`embeddingResolver.Resolve()` devolveu.

**Aplicado no sítio de chamada, e NÃO dentro de
`EmbeddingGeneratorResolver.Resolve()`**, por um motivo de verificação: os duplos
dos testes substituem o **resolvedor** (`StubEmbeddingResolver`,
`FakeEmbeddingGeneratorResolver`). Embrulhar dentro do resolvedor faria todo
cenário de teste passar por fora da medição, e os guardas ficariam verdes sem
nada medido.

**`Dispose` é no-op**, pelo mesmo motivo que `CompactionCallChatClient` não
deriva de `DelegatingChatClient`: o inner é o cliente que o resolvedor construiu
sobre o transporte estático de `HttpClientPipelineTransport.Shared`, e descarte
em cascata é o vazamento que `fix-vazamento-httpclient-chat` corrigiu.

**`GenerateVectorAsync` não precisa de braço próprio** (contraste com D9 da etapa
1, que instrumentou os dois caminhos do `IChatClient`): ele é **método de
extensão** sobre a interface e desemboca em `GenerateAsync` com uma entrada só.
Um braço só cobre os dois sítios. Verificado por leitura da assinatura, e o
guarda da busca é o que prova.

### D5 — M30 grava **fase**, não o texto de operador — e o texto fica como está

**Decidido:** `knowledge_indexing_attempts.FailurePhase`, vocabulário fechado
gravado como **texto** (convenção 12), determinado por **onde o código estava**:

`Chunking` | `ProviderResolution` | `EmbeddingGateway` | `VectorCountMismatch` |
`DimensionMismatch` | `Persistence`

É o mesmo molde do `FailurePhase` de `task_executions` (D12 da etapa 1), e pelo
mesmo motivo de lá: *"o texto de uma exceção é de quem a lança, e muda sem
aviso; a fase é determinada por onde o código estava, que é estável por
construção"*.

**Aqui o motivo é mais forte do que foi na etapa 1, e é medido.** Como o item 4
da reverificação estabeleceu, `KnowledgeIndexingFailure.Describe` distingue
`429`, `401` e `403` por `HttpRequestException.StatusCode`, e o único provedor de
embedding implementado **não lança esse tipo quando o gateway responde com
status** — lança `ClientResultException`, que deriva de `Exception`. Os três
braços com status são **inalcançáveis no caminho real**, e o `502` que motivou o
loteamento cai no balde genérico. Se M30 agrupasse por esse texto, a consulta
concluiria que o gateway nunca devolve 429 e nunca recusa credencial.

**O texto continua indo para a tela, e os dois não se substituem.** `Describe`
responde *"o que o operador faz agora"*; a fase responde *"em que passo parou"*;
e o **status HTTP** fica na linha filha de `embedding_calls`, onde
`HttpStatusOf` o coloca corretamente. As três coisas são diferentes e as três
ficam.

**Registro, sem correção (convenção 9).** O defeito de alcance de `Describe`
é **registrado e não corrigido nesta change**: corrigi-lo muda texto que o
operador lê na tela de documentos, que é mudança de comportamento observável e
não cabe numa change cujo objeto é coleta.

**Decidido: absorver no item que já existe, e não abrir item próprio.** O `02`
já tem, em *Itens em aberto* → **Abertos por `indexacao-lote-de-fragmentos`
(2026-09-20)**, o item *"O texto de falha da tela de documentos manda repetir sem
dizer o que já foi repetido"*. Ele aponta para
`KnowledgeIndexingFailure.cs:52-53` — **o mesmo arquivo, o mesmo braço `_` e o
mesmo texto de tela** que este achado. São duas causas do mesmo defeito:

| | o que diz | causa |
|---|---|---|
| item existente (20/09) | *"Reindexe o documento"* não diz que **três** tentativas já aconteceram | o texto do braço genérico é impreciso |
| este achado (22/09) | aquele texto é o **único** que o caminho real produz | os braços de `429`/`401`/`403` são inalcançáveis |

**Por que absorver, e não abrir o segundo item:** porque as duas correções não
são independentes, e separá-las produz exatamente a meia-correção que o registro
existe para impedir. Reescrever só o texto genérico dá uma mensagem melhor
redigida que continua errada para `429`, `401` e `403` — e para o `502`, em que
*"Reindexe o documento"* aconselha o que **não** resolve. Acrescentar só o braço
de `ClientResultException` faz os quatro casos caírem em textos certos e deixa o
genérico ainda mandando repetir sem dizer o que já foi repetido. **Quem corrigir
tem de fazer as duas coisas**, e um item só é o que garante isso. É o precedente
da `lock-de-contexto-falha-terminal`, que acrescentou a segunda fonte ao item da
`PendingDispatch` órfã em vez de abrir outro.

**Consequência que a absorção obriga a recalibrar** (convenção 22): o item
existente registra *"**Posição:** change de `apps/frontend`"*. Com a segunda
causa, **não é mais só de `apps/frontend`** — o braço que falta é `apps/workers`,
em `KnowledgeIndexingFailure.Describe`. A posição passa a ser uma change que
toca os dois, e quem absorver corrige essa linha com o motivo.

O que a entrada absorvida acrescenta, e fica escrito no `02`:

- os braços de `429`, `401` e `403` são **inalcançáveis no caminho real** — o
  único provedor implementado lança `ClientResultException`, que deriva de
  `Exception` e **não** de `HttpRequestException`. **Verificado por execução**
  (convenção 6), nesta change;
- **todo** erro HTTP do gateway cai no balde genérico, inclusive o `502` que
  motivou a `indexacao-lote-de-fragmentos`;
- consequência para quem opera: a tela diz *"erro interno … Reindexe o
  documento"* num caso em que reindexar não resolve, e é o único texto que
  aquele caminho produz;
- **Gatilho: cumprido** — é esta verificação. A correção deixa de depender de
  evidência nova;
- quem corrigir acrescenta o braço de `ClientResultException` **antes** dos de
  `HttpRequestException`, reescreve o texto genérico, e recalibra os dois
  registros.

E fica, no código, um comentário ao lado do `switch` nomeando o tipo que falta,
por quê, e o item do `02` que tem o assunto.

### D6 — `HttpStatusOf` é **reusado**, não copiado

`ExecutionMetricsScope.HttpStatusOf` é `public static` e já cobre os três SDKs,
incluindo `ClientResultException` (item 2 da reverificação). `MeasuredEmbeddingGenerator`
o chama diretamente.

**Alternativa recusada:** copiar o `switch` para um helper de embedding, para não
acoplar `EmbeddingMetrics` a `ExecutionMetrics`. Recusada porque cópia é
exatamente o que a convenção 15 chama de defeito com detecção difícil: o
próximo SDK acrescentado a um dos dois `switch` deixaria o outro devolvendo nulo,
e nada reprovaria. O acoplamento real é uma chamada estática dentro do mesmo
app.

**O que fica pendente de execução real:** que o gateway, ao responder `502`,
produza `ClientResultException` com `Status = 502` até a borda onde medimos — a
verificação acima estabeleceu a **hierarquia do tipo**, não o **caminho da
exceção** através de `Microsoft.Extensions.AI.OpenAI`. É tarefa de verificação
por execução real (convenção 6), no `tasks.md`, e é a que fecha o ciclo aberto em
20/09.

### D7 — Uma escrita só, depois do estado terminal do documento

**Decidido: uma escrita, no fim de `IndexAsync`, depois de o desfecho estar
gravado** — e não o par "abrir cedo / fechar depois" da etapa 1 (D3 de lá).

A etapa 1 abriu cedo porque a linha aberta **é** o dado de M32 ("tasks sem estado
terminal"), e ele não existiria de outro jeito. Aqui esse dado **já existe e é
de outra tabela**: `MarkAttemptStartedAsync` conta a tentativa e move o documento
para `Indexing` **antes** do trabalho, e um processo que morre no meio deixa o
documento em `Indexing` — visível na tela de documentos hoje, sem coleta
nenhuma. Abrir cedo aqui seria um INSERT a mais por tentativa para repetir um
sinal que já está gravado.

**Custo aceito, escrito:** um crash entre a primeira chamada ao gateway e o fim
perde as linhas dos lotes já gerados, **e também a linha de tentativa**. É o
mesmo custo que a etapa 1 aceitou para as filhas dela, e pelo mesmo motivo: o
contrário é uma ida ao banco por lote.

**Alternativa recusada: gravar cada linha de chamada dentro do laço de lotes.**
Ela protege contra exatamente um caso — o processo morrer no meio da indexação —
e o preço é estrutural, não de desempenho:

- A chave estrangeira `Restrict` de `embedding_calls` exige o **pai gravado
  antes** da primeira filha. Gravar por chamada obriga a INSERTar a linha de
  tentativa **antes** do primeiro lote, que é o padrão "abrir cedo" da etapa 1 —
  o mesmo que esta decisão acabou de recusar, e pelo motivo que está acima: o
  sinal de "indexação sem desfecho" **já existe** em `knowledge_documents`.
- As idas ao banco por indexação passam de **uma** para **N + 2**.

**O que NÃO é argumento para ela, e a confusão é fácil:** os dois cenários da
spec que parecem exigi-la — *"a exceção continua propagando com o mesmo efeito
que teria sem a coleta"* e *"falha no segundo lote preserva a linha do
primeiro"* — **são satisfeitos pela escrita em bloco**. O acumulador é o
contexto da tentativa, que sobrevive à exceção do segundo lote: o `catch` de
`IndexAsync` recebe o controle com a linha do primeiro lote (`Failed = false`) e
a do segundo (`Failed = true`, com o `HttpStatus`) já na lista, e as duas são
gravadas depois do estado terminal. A escrita por chamada não compra nenhum dos
dois cenários — compra só o crash, que está contabilizado acima.

**Ordem de grandeza, para a pergunta não voltar.** A chamada ao gateway custa
**segundos** e a escrita custa **milissegundos**: medido no piloto de
20/09/2026, 8,9 s para 267 fragmentos e 14,1 s para 175. Uma indexação como a do
documento `02` (460 fragmentos, lote 250) são **duas** chamadas ao gateway, e a
escrita — uma, no fim — não é o termo dominante em nenhuma das duas variantes.
**Isto é o que torna a escolha barata nos dois sentidos**: a escrita por chamada
não seria cara, e por isso ela não foi recusada por custo, e sim por reintroduzir
a abertura antecipada que D7 dispensa.

**A escrita acontece depois do estado terminal** — `CommitAsync` / `FailAsync` /
o descarte já retornaram — e é **atômica entre pai e filhas**: a linha de
tentativa e as linhas de chamada entram num `SaveChangesAsync` só, o que faz a
chave estrangeira `Restrict` valer por construção.

**Nada aqui lança** (convenção 4): falha de gravação vira `LogWarning` com
`KnowledgeDocumentId` no estado estruturado, e o `KnowledgeIndexingOutcome`
devolvido ao consumidor é o mesmo que seria sem coleta. Métrica que muda o
resultado do que ela mede não é métrica.

**Forma no código:** o corpo atual de `IndexAsync` vira `IndexCoreAsync`, e
`IndexAsync` passa a ser: cria o contexto da tentativa → `await
IndexCoreAsync(contexto, …)` → grava métrica (nunca lança) → devolve o desfecho.
O escopo de DI da escrita é **próprio**, como em `ExecutionMetricsWriter`, para
não pegar carona no `DbContext` que acabou de transacionar.

### D8 — Descarte antes de começar não grava linha; descarte tardio grava

`IndexAsync` tem **dois** descartes, e eles são diferentes:

| descarte | onde | tentativa contada? | linha? |
|---|---|---|---|
| **antes de começar** — documento sumiu, ou revisão já não é a pedida | no topo, antes de `MarkAttemptStartedAsync` | **não** (já é o comportamento de hoje) | **não** |
| **tardio** — `CommitAsync`/`FailAsync` afetaram zero linhas | depois do trabalho | **sim** | **sim**, `Outcome = Discarded` |

A regra que une os dois: **a linha de tentativa existe exatamente quando a
tentativa foi contada.** Isso mantém `count(*)` em
`knowledge_indexing_attempts` reconciliável com `IndexingAttempts` do documento,
que é a conferência que a etapa 3 vai querer fazer.

O descarte tardio gravar linha não é detalhe: ele **consumiu tokens do gateway**
(a indexação rodou inteira e foi descartada na gravação), e M19 tem de vê-los.

### D9 — Nenhuma chave estrangeira para o catálogo, e nenhuma coluna derivável duplicada

**Sem FK** de `knowledge_indexing_attempts` para `knowledge_documents` ou
`knowledge_bases`, nem de `embedding_calls` para `knowledge_bases`. Dois motivos,
e o segundo é o que decide:

1. É o mesmo de D13 da etapa 1 (nenhuma métrica referencia `agents`).
2. **A exclusão é real e cascateia.** A spec de `knowledge-document-indexing` diz
   *"Exclusão de documento leva os fragmentos junto"*. Com FK `Restrict`, apagar
   uma base passaria a **falhar**; com `Cascade`, o total de tokens de um período
   **mudaria retroativamente** quando alguém apagasse uma base. Nenhum dos dois é
   aceitável: a métrica registra o que aconteceu, e o que aconteceu não muda
   porque o catálogo mudou depois.

`Provider` e `Model` são **snapshot** na linha, pelo mesmo motivo (D13 da etapa
1): trocar `Embedding:Model` não reescreve consumo já medido.

**As duas FKs que existem** são para tabelas de métrica, e valem por construção:
`embedding_calls.KnowledgeIndexingAttemptId` → `knowledge_indexing_attempts`
(gravadas no mesmo `SaveChangesAsync`, D7) e `embedding_calls.TaskId` →
`task_executions` (gravadas no fechamento da execução, quando o pai já existe).
As duas são **anuláveis**, e a nulidade é determinada por `Purpose` — exatamente
uma delas é preenchida em cada linha.

**Colunas deriváveis não são duplicadas:** `KnowledgeDocumentId` fica só na
tentativa (a linha de chamada chega nele por junção), e o agente da busca fica só
em `task_executions.AgentId`. `KnowledgeBaseId` **é** duplicado na linha de
chamada, e de propósito: é a única coluna que as **duas** finalidades têm, e M19
por base é consulta de primeira ordem.

### D10 — `OutputTokens` não existe em `embedding_calls`

Embedding não tem saída. A etapa 1 escreveu, em D7, que a coluna anulável de
`provider_calls` comportaria isso — mas aquilo pressupunha reusar a tabela, e D1
recusou. Numa tabela própria, uma coluna que é **sempre** nula é pior do que
nenhuma: convida a somá-la, e a soma é sempre nula.

O que se grava é `InputTokens` (`bigint`, anulável). Verificado por execução que
a fonte existe e preserva nulo:

```
### GeneratedEmbeddings<T> props:  UsageDetails Usage  …
### UsageDetails props:  Nullable<Int64> InputTokenCount  Nullable<Int64> TotalTokenCount  …
```

**Nulo ≠ zero** (convenção 13): `InputTokens` é nulo quando `Usage` vem nulo ou
`InputTokenCount` vem nulo, e zero **só** quando o provedor reportar zero. O
guarda negativo afirma a ausência do zero, não só a presença do nulo.

**Pendente de execução real:** se o gateway do `.env.prod` reporta uso em
resposta de embedding. Se não reportar, M19 fica com a coluna nula e a tela da
etapa 4 **diz isso** em vez de mostrar zero — que é a convenção 13 na direção
forte, e é a razão de a coluna ser anulável desde o primeiro dia.

### D11 — Onde os guardas moram, e por que a coleção continua com 14 classes

Convenção 15: vermelho contra `HEAD` **pela propriedade**, no componente que a
correção toca. Contra `HEAD` puro os guardas nem compilam (as entidades não
existem), e vermelho de compilação não prova nada. **Por isso a ordem:** o schema
entra primeiro (grupo 2 do `tasks.md`), e os guardas são vistos reprovando contra
`HEAD` + schema, com as tabelas **vazias** — é a propriedade que falta.

Cada guarda mora na classe que **já tem o arranjo**:
`KnowledgeIndexingTests`, `KnowledgeIndexingZeroFragmentGuardTests`,
`KnowledgeIndexingConcurrencyTests` e `KnowledgeToolExecutionEndToEndTests` — as
quatro já estão na `WorkerHostCollection`. **As classes da coleção continuam
14**, medidas por `^\[Collection(` (régua textual, que exclui comentário), e a
15ª obrigaria a recalibrar o limiar de carga da suíte (convenção 22). Os dois
arquivos de teste novos ficam **fora** da coleção: o espelho de schema usa
`IClassFixture<WorkerInfrastructureFixture>`, no precedente de
`KnowledgeSchemaMirrorTests`, e o leitor é infraestrutura, não classe de teste.

**Escala dos cenários:** lote pequeno contra documento pequeno, no precedente já
escrito em `KnowledgeIndexingHarness.MultiFragmentMarkdown` — a propriedade é
escala-livre ("mais fragmentos que o lote"), e semear 442 fragmentos gravaria
~7 MB de vetor por cenário e empurraria a suíte na direção da referência de
duração, que é onde a convenção 22 manda não mexer sem recalibrar.

## Mapa: métrica do catálogo → a coluna que a produz

Contra a lista de 27 entradas registrada no `02` ("catálogo de métricas —
referência viva").

| métrica | fonte depois desta change |
|---|---|
| **M19** — tokens de embedding | `sum(embedding_calls.InputTokens)`, recortável por `Purpose`, `KnowledgeBaseId`, `Model` e período (`StartedAt` do pai / da execução) |
| **M14** — por provedor, com total conversa + embedding **só neste nível** | `union` de `sum(provider_calls)` e `sum(embedding_calls)` por `Provider` (D1) |
| **M30** — falhas de indexação | `knowledge_indexing_attempts` com `Outcome in ('Failed','RetryScheduled')`, por `FailurePhase`, `KnowledgeDocumentId` e `Attempt` |
| **M23** — duração da chamada ao provedor | ganha o recorte de embedding por `embedding_calls.DurationMs` |

**O que continua sem fonte, e é de outra etapa:** M35 (D2), M33, M32 (etapa 3),
M36–M39.

## Árvore de pastas

```
apps/api/src/Buteco.Api/
  EmbeddingMetrics/
    Entities/
      EmbeddingCall.cs                        (novo — mapeamento, sem escritor)
      KnowledgeIndexingAttempt.cs             (novo — idem)
  Infrastructure/
    AppDbContext.cs                           (modificado — 2 DbSet + 2 blocos)
    Migrations/
      AAAAMMDDHHMMSS_AddEmbeddingMetrics.cs           (novo, gerado)
      AAAAMMDDHHMMSS_AddEmbeddingMetrics.Designer.cs  (novo, gerado)
      AppDbContextModelSnapshot.cs            (modificado, gerado)

apps/workers/src/Buteco.Workers/
  EmbeddingMetrics/
    Entities/
      EmbeddingCall.cs                        (novo — espelho)
      KnowledgeIndexingAttempt.cs             (novo — espelho)
    EmbeddingMetricsValues.cs                 (novo — Purpose, Outcome, FailurePhase)
    EmbeddingMetricsWriter.cs                 (novo — a escrita única, nunca lança)
    MeasuredEmbeddingGenerator.cs             (novo — a medida, nos dois sítios)
    KnowledgeIndexingAttemptContext.cs        (novo — tentativa contada?, fase, linhas)
  ExecutionMetrics/
    ExecutionMetricsScope.cs                  (modificado — RecordEmbeddingCall)
    ExecutionMetricsWriter.cs                 (modificado — AddRange das linhas de busca)
  Knowledge/
    Indexing/
      KnowledgeIndexingService.cs             (modificado — fase, contexto, a escrita)
      KnowledgeIndexingFailure.cs             (modificado — só o comentário de D5)
    Execution/
      KnowledgeToolSetResolver.cs             (modificado — embrulha o gerador)
  Infrastructure/
    AppDbContext.cs                           (modificado)
    Migrations/…                              (espelho — nunca contra banco real)
```

**Nada em `libs/`.** Não há segundo consumidor: `apps/api` só precisa do
**mapeamento** para migrar, e `apps/workers` do mapeamento **e** do escritor. É
o mesmo par de espelhos por cópia que `KnowledgeIndexingJobMessage` e as três
entidades da etapa 1 já são, e a convenção 2 pede dois a três consumidores
**reais** antes de extrair. O espelho é verificado, não confiado: o teste de
schema espelho é o que transforma disciplina em verificação.

**`EmbeddingMetrics/` no topo, e não sob `Knowledge/`:** a tabela tem duas
finalidades e uma delas (busca) nasce no fluxo do agente, não no de conhecimento.
Simetria com `ExecutionMetrics/` da etapa 1.

## Projeção (convenção 18) — décima terceira medição

Projeção declarada **antes** de escrever código; o fechamento **só compara** e
não reescreve a projeção.

**O que a série acumulou, e o que esta medição acrescenta:**

- A décima segunda errou **+70% em linhas** com os casos exatos, porque a régua
  conta casos e comentários e **não conta duplo com estado** (304 linhas num
  arquivo). **Correção aplicada aqui:** infraestrutura de duplo é **item
  separado** da projeção.
- **Item novo desta medição:** **código gerado é item separado também.** Na etapa
  1, `Designer.cs` + `ModelSnapshot` + migração somaram **1.792 das 4.475**
  inserções (`git diff -w`) — **40% do diff**, escritas por `dotnet ef`. Uma
  projeção de linhas que não separe isso está projetando o gerador, não o
  trabalho.
- Contagem de arquivos e de unidades públicas acertou cinco vezes.
- **Unidade do xUnit é o caso**, e a projeção declara qual está contando: aqui,
  **`[Fact]` conta 1 e `[Theory]` conta um por `[InlineData]`**.
- **Modificado se mede com `git diff -w`.**
- **Régua contada por busca textual mede texto**; assinatura se confere
  **compilando todos os `.csproj`** — não há `.sln` na raiz.

**Calibração:** etapa 1 (`git diff -w 2ee34d3..2a9ff6a -- apps libs tests`) =
**35 arquivos, 4.475 inserções**, das quais ~1.792 geradas, ~1.241 de produção e
~1.442 de teste.

### Projeção

| item | projeção | faixa |
|---|---|---|
| arquivos criados | **15** | 13–17 |
| arquivos modificados | **15** | 13–17 |
| tipos públicos novos (aninhados contam; classes de migração geradas **não**) | **14** | 12–16 |
| **casos de xUnit acrescentados** | **+38** | 34–42 |
| — em `apps/workers` | +34 | |
| — em `apps/api` | +4 | |
| **linhas de produção** (`git diff -w`, não geradas) | **880** | 760–1.000 |
| **linhas de teste — casos** | **710** | 600–820 |
| **linhas de teste — infraestrutura de duplo** *(item separado)* | **180** | 130–240 |
| **linhas geradas** (`Designer` + snapshot + migração, 2 apps) *(item separado)* | **1.700** | 1.550–1.900 |
| **total de inserções** (`git diff -w`) | **3.470** | 3.100–3.900 |
| classes na `WorkerHostCollection` | **14 → 14** | exato |

**Projeções que não são de tamanho, e que o fechamento compara igual:**

- ~~**Nenhum construtor de produção muda.**~~ **ERRADA — corrigida no fechamento
  (convenção 9).** A projeção afirmava que `KnowledgeIndexingService`,
  `KnowledgeToolSetResolver` e `EmbeddingGeneratorResolver` manteriam as
  assinaturas atuais. **`KnowledgeToolSetResolver` mudou**: ele passou a receber
  `IOptions<EmbeddingOptions>`.

  **A causa, e ela era previsível:** a linha de `embedding_calls` grava provedor,
  modelo e dimensão em **snapshot** (D9) — e o resolvedor da busca não tinha
  essa informação. Só `KnowledgeIndexingService` a tinha, porque só ele recebia
  as opções. A projeção mediu a *mecânica* da captura (embrulho no sítio de
  chamada, D4, que de fato não exige construtor) e não mediu os **dados que a
  linha precisa carregar**. São duas coisas diferentes, e a projeção juntou as
  duas.

  **Custo real, medido: um sítio de construção manual**
  (`KnowledgeToolSetResolverTests.cs:436`). Todo o resto passa pela interface
  registrada em DI — conferido por compilação dos **10 `.csproj`**, inclusive
  `tests/InboxOrchestratorRoundTrip.Tests`, que registra o resolvedor e não
  precisou mudar. Aquele arquivo entrou na lista fechada de escopo **com o
  motivo escrito**, não por violação silenciosa.

  **Alternativa recusada:** resolver `IOptions<EmbeddingOptions>` do escopo de DI
  que `SearchAsync` já abre. Manteria a projeção verdadeira e é service locator —
  o idioma da casa é injeção por construtor, e preservar uma projeção não é
  motivo para trocar de idioma.

  **Régua para a próxima:** projetar "nenhum construtor muda" exige conferir, por
  coluna, de onde vem **cada valor que a linha grava** — não só onde a medida é
  tomada.
- **Nenhum registro de DI novo.** `MeasuredEmbeddingGenerator` e
  `EmbeddingMetricsWriter` são construídos, não injetados.
- **`provider_calls` sai desta change byte a byte igual.**

**Baselines: não herdadas.** Os números que circulam — `apps/workers` 340/340,
`apps/api` 346/346, `apps/inbox` 202/203 — são da etapa 1, e a
`compactacao-historico` entrou depois. A tarefa 1.1 **mede** as três suítes com
`podman ps` em zero, antes de qualquer edição, e é esse número que o fechamento
compara. O flake conhecido de `apps/inbox` é registrado **pelo nome** na
medição, e a projeção de casos é sobre `apps/workers` e `apps/api` — `apps/inbox`
não é tocado e a baseline dele existe só para provar isso.

### Baseline MEDIDA (tarefa 1.1) — 23/09/2026, 00:11–00:21 `America/Sao_Paulo`

Contra `b95e3a6`, com `podman ps` (rodando) em **zero**, antes de qualquer
edição.

| suíte | medido | último registrado (etapa 1) | duração |
|---|---|---|---|
| `apps/workers` | **340 / 340** | 340/340 | 7 m 53 s |
| `apps/api` | **346 / 346** | 346/346 | 1 m 21 s |
| `apps/inbox` | **203 / 203** | 202/203 | 22 s |

**Achado da tarefa 1.2 — o flake não reproduziu, e a ordem explica.**
`apps/inbox` deu **203/203** em suíte cheia, onde o registro dizia 202/203.

**Isso confirma o mecanismo do item em vez de contrariá-lo.** O item do `02`
lê o flake como *"derrubado pelo resíduo da suíte pesada imediatamente
anterior"*, e nas três medições anteriores o alvo imediatamente anterior ao
`apps/inbox` era `apps/workers` — o pesado. Aqui a ordem foi
`workers → api → inbox`, com **`apps/api`** (1 m 21 s) entre os dois: um alvo
anterior **4 a 5 vezes mais leve**, e a reprovação some. É a primeira vez que a
previsão do item é testada **na direção de passar**.

**Consequência para a régua:** a baseline de `apps/inbox` **depende da ordem do
runner**, e quem a citar diz qual alvo rodou antes. Comparar contra "202/203"
mostraria melhora que não é da change. A baseline desta change é **203/203, com
`apps/api` imediatamente antes**. O gatilho registrado no item — reprovar
**isolado** com a máquina descarregada — não foi disparado.

**Regime da medição, colado (convenção 22):** `--no-restore --no-build`, porque
`dotnet test` trava indefinidamente no restore implícito neste ambiente (rede
bloqueada); e `TESTCONTAINERS_RYUK_DISABLED=true` junto do `DOCKER_HOST` do
Podman, porque o Ryuk tenta montar o socket como volume e o Podman recusa com
`operation not supported`. Sem as duas variáveis a suíte de integração não roda
aqui — e nenhuma das duas é propriedade da change.

## Fechamento da convenção 18 — décima terceira medição, COMPARADA

**A projeção acima não foi reescrita.** Medido com `git diff -w` sobre `apps`,
`libs` e `tests`, em 23/09/2026.

| item | projetado | faixa | **medido** | erro |
|---|---|---|---|---|
| arquivos criados | 15 | 13–17 | **15** | **0** |
| arquivos modificados | 15 | 13–17 | **15** | **0** |
| tipos públicos novos | 14 | 12–16 | **17** | +21%, fora |
| **casos de xUnit acrescentados** | +38 | 34–42 | **+51** | **+34%, fora** |
| — em `apps/workers` | +34 | | **+46** | +35% |
| — em `apps/api` | +4 | | **+5** | +25% |
| linhas de produção (não geradas) | 880 | 760–1.000 | **1.180** | **+34%, fora** |
| linhas de teste — casos | 710 | 600–820 | **874** | **+23%, fora** |
| linhas de teste — infraestrutura de duplo | 180 | 130–240 | **200** | +11%, dentro |
| linhas geradas (`Designer` + snapshot + migração, 2 apps) | 1.700 | 1.550–1.900 | **1.836** | +8%, dentro |
| **total de inserções** | 3.470 | 3.100–3.900 | **4.090** | +18%, fora |
| classes na `WorkerHostCollection` | 14 → 14 | exato | **14 → 14** | **0** |

### O que a décima terceira medição ensina

1. **A separação em três níveis funcionou — e os dois níveis novos foram os que
   acertaram.** Código gerado (+8%) e infraestrutura de duplo (+11%) caíram
   dentro da faixa; os dois níveis "à mão" erraram por +34% e +23%. É o oposto
   do que a série vinha sofrendo: a décima segunda errou +70% **porque** o duplo
   estava misturado nos casos. Separá-lo não só corrigiu aquele erro — mostrou
   que **o erro residual é de complexidade, não de contabilidade**.
2. **Contagem de arquivos acertou pela sexta vez**, agora nos dois sentidos
   (criados e modificados, 15 e 15, erro zero). É a régua mais estável da série,
   e a única que já pode ser usada sem faixa.
3. **Tipos públicos saiu da faixa pela primeira vez em seis medições** (+21%), e
   a causa é nomeável: três classes estáticas aninhadas de vocabulário
   (`Purpose`, `Outcome`, `FailurePhase`) mais dois enums que **a projeção não
   previu porque nasceram da necessidade dos guardas** (`EmbeddingFailureKind` e
   `EmbeddingUsageShape`, exigidos por `[InlineData]`). **Item para a décima
   quarta:** projetar os tipos que os *guardas* obrigam, e não só os que o
   desenho pede.
4. **O duplo continua vazando para dentro do arquivo de casos.** O
   `StubPipelineResponse` — **22 linhas** de `PipelineResponse` mínimo, para
   construir um `ClientResultException` com status — mora dentro de
   `KnowledgeIndexingTests.cs` e foi contado em "casos". Tirá-lo de lá levaria o
   nível "casos" a 852 e o "duplo" a 222, sem mudar o total. **É a lição da
   décima segunda reaparecendo em escala menor:** separar por *arquivo* não
   basta, porque o duplo cabe dentro de um arquivo de casos.
5. **Casos errou +34%, e erra junto com as linhas à mão** (+35% em
   `apps/workers`, +25% em `apps/api`). Os dois itens "à mão" — casos e linhas —
   erraram na **mesma proporção e na mesma direção**, o que aponta para uma
   causa só: a projeção subestimou **quantos pares** cada propriedade precisa.
   Onde ela previu um guarda, saíram dois (positivo e negativo); onde previu um
   `[Fact]`, saiu uma `[Theory]` de quatro.
6. **Régua da série, atualizada:** contagem de arquivos é confiável; os três
   níveis de linha são confiáveis *na ordem* (gerado < duplo < à mão, em erro
   crescente); e **tudo que é escrito à mão — linha e caso — continua sendo o
   que a série não sabe projetar**, três medições seguidas fora da faixa e
   sempre para cima. **Item para a décima quarta:** projetar o **par**, não o
   guarda; a convenção 15 pede os dois lados, e a projeção vem contando um.

## Rodada vermelha (convenção 15) — medida, por escopo, com a causa atribuída

**O experimento, e por que ele é o "HEAD + schema" que D11 descreve.** Contra
`HEAD` puro os guardas nem compilam (as entidades não existem), e vermelho de
compilação não prova nada. O schema entrou primeiro; depois a **coleta foi
neutralizada** — `EmbeddingMetricsWriter.WriteAsync` e
`ExecutionMetricsScope.RecordEmbeddingCall` retornando no topo —, deixando as
duas tabelas existindo e **vazias**. É a propriedade que falta, isolada.

**Resultado: 18 reprovações de 125 casos**, em 2 m 45 s. Cada escopo reprova
pelo **seu** motivo:

| escopo | casos vermelhos | asserção que reprovou |
|---|---|---|
| **linha por chamada / grão do lote** | 3 | `Assert.Equal` 3 × 0 linhas por lote; `Assert.Single` vazia no documento que cabe no lote; `Assert.Equal` 2 × 0 na falha do segundo lote |
| **status HTTP** | 2 | `Assert.Single` vazia — sem linha, não há `HttpStatus` nem `502` a conferir |
| **nulo não é zero** | 4 | `Assert.Single` vazia nos quatro (ausente, sem contagem, zero reportado, 1234) |
| **linha de tentativa / M30** | 3 | `Assert.Single` vazia no sucesso e no descarte tardio; `Assert.Equal` 3 × 0 nas três tentativas |
| **fase da falha** | 4 | a `[Theory]` inteira: `Assert.Single` vazia em `ProviderResolution`, `Gateway` e `Dimension`; `Assert.Equal` em `VectorCount` |
| **pai determinado pela finalidade (busca)** | 1 | `WaitForSearchCallsAsync` estourou o prazo aos 15 s — a linha de busca nunca chegou ao banco |
| **par "sem item"** | 1 | a metade **positiva**: `Assert.Single` vazia na linha de tentativa com `FailurePhase = Chunking` |

**Três guardas NÃO reprovaram, e isso é por construção — não é falha do
experimento.** Os três afirmam **ausência**, e a ausência é exatamente o que a
coleta neutralizada produz:

- `StaleRevisionDiscardedBeforeStarting_WritesNoAttemptAndNoCall`
- `Invoke_OutsideAnyExecution_WritesNoEmbeddingCall_AndDoesNotFail`
- `MetricsTableMissing_DoesNotChangeTheIndexingOutcome`

**O vermelho deles vem de outro experimento**, e fica nomeado aqui para não
parecer guarda vazio: os dois primeiros reprovam se a gravação passar a
acontecer **sempre** (removendo o `if (!context.Counted)` e o `if (scope is
null)`); o terceiro reprova se o `try/catch` de `WriteAsync` for removido — aí a
falha de gravação derruba a indexação, que é precisamente o que ele impede. São
pares negativos da quinta forma da convenção 15, e o teste positivo de cada um
já está do outro lado da tabela acima.

## Rodada verde — dois guardas reprovaram pelo motivo ERRADO, e o enunciado mudou antes da correção

Convenção 15: *"se algum guarda reprovar por motivo diferente do esperado, é
achado"*. Dois reprovaram na primeira rodada verde, e **nenhum dos dois por
defeito de produção**.

### Achado 1 — a ordem dos lotes NÃO é recuperável da tabela

`DocumentLargerThanTheBatch_WritesOneEmbeddingCallRowPerBatch` afirmava a
**sequência** `[2, 2, 1]` e mediu `[1, 2, 2]`. `embedding_calls` **não tem
coluna de ordem**, e nada faz a leitura devolver as linhas na ordem em que os
lotes rodaram.

**O enunciado estava errado, não o schema.** O que D3 promete é *"uma linha por
chamada, com o tamanho de cada chamada"* — e é isso que sustenta a afirmação
sobre o `502`: sabe-se que **um lote de N entradas** falhou com aquele status. A
**posição** do lote na sequência nunca foi prometida, e o guarda a exigia. A
asserção passou a comparar **conjunto ordenado**.

**O que isso custa, declarado:** não dá para dizer *"foi o segundo lote"* — dá
para dizer *"um lote de N entradas falhou com 502"*, que é o que o diagnóstico
de 20/09 precisava. **Se a etapa 3 ou 4 quiser a posição, é coluna nova** (um
`BatchOrdinal`), e é mudança de schema — não entra aqui por achado de teste.
Registrado para quem escrever a consulta de M30 por lote.

### Achado 2 — o caso `VectorCount` nunca exercitou a divergência

`FailurePhase_IsTheStepWhereItStopped(VectorCount)` esperava `Failed` e mediu
**`Indexed`**: a indexação **deu certo**. A causa é o arranjo, não o código —
o cenário usava o `Markdown` curto do arquivo, que produz **um** fragmento, e
`OverrideResultCount = 1` truncar uma resposta de um vetor para um vetor é um
**no-op**. A contagem conferia, e o guarda teria ficado verde sem nunca ter
exercitado a divergência que ele existe para pegar.

**É vacuidade da mesma família que a convenção 5 nomeia**, e escapou porque na
rodada vermelha ele reprovou — por falta de linha, como todos os outros —, o que
mascarou o defeito do arranjo.

### Régua nova da convenção 15 (registrada no `02`)

**Numa change de coleta, a rodada vermelha global não verifica guarda nenhum.**
O vermelho é obtido neutralizando a escrita, e então **a ausência das linhas
reprova todos os guardas de uma vez**. Ausência é um vermelho barato: ela **não
distingue guarda bom de guarda vazio**, porque um guarda que nunca alcança o
caminho que afirma reprova pelo mesmo `Assert.Single() — coleção vazia` que um
guarda correto. Foi literalmente o caso: 12 dos 18 vermelhos desta change
trazem essa mesma mensagem.

O sinal útil vem da **rodada verde** — guarda que passa sem nunca ter exercitado
a propriedade só aparece ali, ou não aparece.

**A forma curta: guarda vermelho na rodada vermelha não está verificado.** A
convenção 15 pede o vermelho; ela não diz que o vermelho basta, e até aqui a
base tratava como se dissesse.

O cenário passou a usar `MultiFragmentMarkdown(3)`, no mesmo arranjo que
`BatchReturningFewerVectorsThanSent` já usava, **com a precondição afirmada**
(mais de um fragmento) para não voltar a passar por vacuidade.

## Risks / Trade-offs

- **O gateway pode não reportar uso em embedding** → `InputTokens` fica nulo e
  M19 não tem número. Mitigação: a coluna é anulável desde o primeiro dia
  (D10), a verificação por execução real está no `tasks.md` **antes** do
  fechamento, e a etapa 4 tem de exibir "não reportado" e não zero (convenção
  13). Se a medição der nulo, isso vai escrito no `02` como propriedade do
  ambiente, não como defeito desta change.
- **Uma ida a mais ao banco por tentativa de indexação e uma inserção a mais por
  busca** → a busca está no **caminho quente** (uma por mensagem). Mitigação: a
  linha da busca **não** vai ao banco na hora — entra no `ExecutionMetricsScope`
  em memória e é gravada no `SaveChangesAsync` que a etapa 1 já faz no
  fechamento da execução. Custo no caminho quente: uma alocação e um `lock`.
- **`Restrict` na FK de `embedding_calls` para `task_executions`** → apagar uma
  linha de `task_executions` passa a falhar se houver busca gravada. Mitigação: é
  a mesma escolha que a etapa 1 já fez para `provider_calls` e
  `delegation_outcomes`, e nada no sistema apaga métrica hoje. Política de
  retenção é da etapa 3 em diante, e vai ter de tratar as cinco tabelas juntas.
- **`Purpose` como discriminador de nulidade** (D9) → é invariante de aplicação,
  não do banco. Mitigação: o espelho de schema afirma a nulidade das colunas, e
  os guardas afirmam o pareamento nas duas finalidades. Uma `CHECK` constraint
  foi considerada e recusada: seria a primeira do repositório, e a invariante
  tem um escritor só.
- **Dois regimes na mesma linha de trabalho** → a série de conversa começa em
  22/09 01:21 e a de embedding começa no deploy desta change. Mitigação: a etapa
  4 exibe **duas** datas, e as duas ficam registradas no `02` no dia do deploy,
  com o fuso (convenção 22).
- **O defeito de `Describe` fica no repositório com o achado escrito** → risco de
  alguém ler M30 como se o texto de tela fosse a classificação. Mitigação: o
  comentário ao lado do `switch`, a condição de correção em D5 e a entrada no
  `02`.

## Migration Plan

1. **Migração sai só de `apps/api`** (`AddEmbeddingMetrics`), e `apps/workers`
   gera a **espelho**, que nunca roda contra banco real — só contra
   Testcontainers. Precedente: `KnowledgeFragment` e `AddExecutionMetrics`.
2. **Só `CREATE TABLE`.** Nenhuma coluna existente muda de tipo ou de
   nulabilidade; nenhuma tabela é reescrita. Aplicar com a aplicação no ar é
   seguro.
3. **Rollback:** `Down` derruba as duas tabelas. Como nenhuma tabela existente
   muda e a coleta é graciosa, a versão anterior do binário roda contra o schema
   novo sem erro — as tabelas simplesmente ficam vazias.
4. **Ordem de deploy:** migração antes do binário. Se o binário subir primeiro, a
   escrita falha, vira `LogWarning` e a indexação e a busca seguem — é a
   degradação graciosa de D7 exercendo a função dela.
5. **No dia do deploy**, registrar no `02`: *"coleta de embedding medindo desde
   DD/MM/AAAA HH:MM, `America/Sao_Paulo`"*, ao lado da data da etapa 1, e o
   resultado da verificação de uso reportado pelo gateway.

## Open Questions

Nenhuma de negócio ou produto em aberto. As duas incertezas desta change são
**técnicas e verificáveis por execução**, estão nomeadas nas decisões e têm
tarefa no `tasks.md`:

- O gateway reporta uso em resposta de embedding? (D10)
- A exceção do `502` chega à borda de medição como `ClientResultException` com
  `Status = 502`? (D6)

Nenhuma das duas muda o schema nem as decisões: mudam o que a etapa 4 vai poder
exibir, e o que se escreve no `02`.

**As duas seguem ABERTAS no fechamento desta change, e o motivo é material.** O
`.env.prod` da cópia de trabalho **não define `EMBEDDING_MODEL` nem
`EMBEDDING_DIMENSIONS`** — só o `.env.prod.example` define, com `changeme` no
modelo — e aponta `OPENAI_BASE_URL` para `https://api.openai.com/v1`. **Não é o
ambiente que produziu a medição de 20/09:** a OpenAI não serve modelo de
embedding de **4.096 dimensões**, que é a dimensão da coluna `vector(4096)` do
índice; o gateway do piloto sobrescreve a base URL no servidor.

Rodar contra `api.openai.com` responderia sobre **outro provedor**, e com
dimensão incompatível a indexação pararia em `DimensionMismatch` antes de gravar
fragmento — não fecharia nem D10 nem D6. Decisão do dono em 23/09/2026: **as
duas ficam para o deploy**, que é quando o regime vai para o `02` de qualquer
forma.

**O que fica declarado enquanto isso**, e a etapa 4 não pode assumir o
contrário:

- **D10 está verificado só até a fonte existir** (`GeneratedEmbeddings<T>.Usage`
  com `InputTokenCount` anulável, medido por reflexão). Que o gateway **preencha**
  esse campo não foi medido. Se vier nulo, a tela exibe "não reportado" e **não**
  zero.
- **D6 está verificado só na hierarquia do tipo** (`ClientResultException` deriva
  de `Exception`, medido por execução). Que a exceção do `502` **chegue** à borda
  de medição com `Status = 502`, atravessando `Microsoft.Extensions.AI.OpenAI`,
  é inferência — apoiada, mas inferência.

### Correção de 23/09/2026, depois do deploy (convenção 9)

**As duas declarações acima eram verdadeiras quando foram escritas, e uma delas
deixou de ser.** Ficam onde estão, e não apagadas, porque explicam **por que a
change fechou antes da verificação** — e essa razão continua valendo para a
próxima etapa que dependa do ambiente do piloto.

**D10 está FECHADA.** A verificação em produção de 23/09/2026 mediu **seis
chamadas**, nas duas finalidades, com `InputTokens` preenchido em **todas** e
`sem_uso = 0`. **O gateway reporta uso.** A salvaguarda continua no código — a
coluna é anulável e a tela sabe exibir "não reportado" —, mas não vai precisar
ser exercida para M19. Mesma execução confirmou o grão do lote em produção (529
fragmentos → 250 + 250 + 29; 81 → uma chamada) e o pai correto nas duas
finalidades.

**D6 continua ABERTA, e é dela que a frase original ainda fala.** `HttpStatus`
veio **nulo nas seis — porque nenhuma falhou**, não porque a coluna não receba.
O que está verificado é a **estrutura**; o valor vindo de exceção real do
gateway, não. **Não se provoca de propósito:** derrubar o gateway do piloto
custaria indisponibilidade real para confirmar um braço de `switch` já lido.
Fecha sozinho na primeira falha real, pela consulta registrada no `02`.

**Então "fechar esta change" passou a significar** *as tabelas existem, a coleta
escreve, os guardas passam **e as duas finalidades foram medidas contra o
gateway do piloto**.* O que **não** significa, e a distinção é o ponto: **medido
contra uma falha do gateway** — isso segue em aberto, com gatilho.
