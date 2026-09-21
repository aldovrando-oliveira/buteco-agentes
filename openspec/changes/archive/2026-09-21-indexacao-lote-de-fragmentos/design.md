## Context

O diagnóstico está fechado e está no `proposal.md`: `IndexAsync` manda **todos**
os fragmentos do documento numa chamada só, e o gateway de embedding derruba a
chamada quando ela fica grande. O par medido é **442 falha, 267 passa**, com o
`01` (78 fragmentos) passando sempre e variando **4,4×** em duração no mesmo
trabalho — o que caracteriza o upstream como instável.

**O que NÃO se sabe, e este documento não vai fingir que sabe:** o **formato** do
teto do gateway. Por número de entradas, por bytes do corpo, ou por tempo de
resposta do upstream — nenhuma das três foi estabelecida. Toda decisão abaixo é
tomada sabendo disso, e o que se afirmar sobre o teto vem de medição, não de
inferência (convenção 6).

Estado corrente, lido no código:

- `KnowledgeIndexingService.cs:73` — a chamada única.
- `KnowledgeIndexingService.cs:75-79` — conferência de `embeddings.Count` contra
  `fragments.Count`, total.
- `KnowledgeIndexingService.cs:81-101` — o laço que casa `embeddings[i]` com
  `fragments[i]` e confere a dimensão de cada vetor.
- `KnowledgeIndexingService.cs:103` / `CommitAsync` — substituição integral dos
  fragmentos em **transação única**, condicionada a `ContentRevision`.
- `KnowledgeIndexingService.cs:105-122` — o `catch` com **duas** saídas:
  `RetryScheduled` enquanto houver tentativa, `Failed` ao esgotar.
- `KnowledgeIndexingQueues.MaxAttempts = 3`, e o XML doc diz o que decide a D2
  abaixo: *"Uma execução é uma tentativa — não uma chamada HTTP"*.
- `KnowledgeIndexingTests.cs:65` — `Embeddings_AreGeneratedInASingleBatch`,
  hoje afirmando `CallCount == 1` para o documento de teste.
- `FakeEmbeddingGenerator` (`KnowledgeIndexingHarness.cs:113`) — o duplo **já**
  tem `CallCount` e `LastBatchSize`. É o andaime compartilhado que a convenção 18
  manda tratar como termo da projeção, não como risco.

## Goals / Non-Goals

**Goals:**

- Nenhum documento falha a indexação **por tamanho** enquanto o tamanho do lote
  estiver abaixo do teto do gateway.
- O tamanho do lote é **variável em produção sem rebuild**, porque variá-lo é
  como o teto vai ser descoberto.
- A ordem dos vetores continua casando com a ordem dos fragmentos, **através da
  fronteira de lote** — um desalinhamento aqui corromperia o índice em silêncio,
  que é a classe de defeito que esta base mais persegue.
- Guarda vermelho contra o defeito, no componente que a correção toca
  (convenção 15).

**Non-Goals:**

- Descobrir o teto do gateway. Esta change dá o instrumento; a medição é
  operação, contra o gateway real, e não cabe num teste.
- Limitar documentos indexados **simultaneamente** (registrado no `02`).
- Mudar a política de tentativas, o `prefetchCount`, o número de instâncias, a
  persistência, o texto de falha da tela, ou instrumentar embedding.

## Decisions

### D1 — Padrão 250, e ele é declaradamente provisório

`Embedding:BatchSize`, `int`, padrão **250** no inicializador da propriedade.

O registro no código diz **exatamente** isto, e não mais que isto: *250 porque
267 passou e 442 falhou, com margem; não há teto medido.* Escrever "250 é o
limite seguro" seria afirmar um limite cujo **mecanismo** não foi estabelecido —
convenção 13, e é o mesmo defeito que a `lock-de-contexto-falha-terminal`
corrigiu na forma oposta (lá, um estado que o sistema não sabia afirmar).

A opção existe porque há cenário real de alguém precisar de outro valor
(convenção 2), e o cenário é duplo: **ajustar** quando o piloto mostrar, e
**medir** — variar o parâmetro é o instrumento de descoberta do teto.

*Alternativa recusada — constante de produto, no molde de `KnowledgeChunker` e
`AgentDelegationToolOptions`.* Ali a convenção 2 se aplica porque **não há**
cenário de outro valor: os parâmetros de fragmentação vieram de uma medição e
ninguém precisa mexer neles em produção. Aqui o valor é provisório **por
construção** e o operador é quem vai variá-lo. Constante obrigaria rebuild e
deploy a cada passo da medição.

*Alternativa recusada — derivar o lote de bytes em vez de contar entradas.* A
razão bytes/fragmento varia entre **1.004 e 1.173** nos três documentos medidos
(markdown deste repositório, três amostras, piloto de 20/09/2026): ±8%. Isso
basta para decidir grandeza e **não** basta para decidir teto — e, pior,
escolher a unidade "bytes" afirmaria que o teto é por bytes, que é uma das três
hipóteses em aberto. Contar entradas é a unidade que o código já tem e a que não
afirma nada sobre o mecanismo.

**Gatilho de recalibração (convenção 22):** qualquer medição contra o gateway
que estabeleça o formato do teto, ou a primeira falha de indexação com o lote em
vigor. O valor citado vale sobre o regime colado — piloto 20/09/2026, gateway
de embedding do `.env.prod`, modelo de 4.096 dimensões.

### D2 — A retentativa continua sendo do **documento inteiro**

É a decisão que decide o valor da change, e a resposta é: **não muda**.

Hoje a política reagenda o documento inteiro, três vezes, por filas de espera com
TTL fixo. Com lote, isso continua significando 442 fragmentos reprocessados
porque o segundo lote de dois falhou. **Esse custo é real e fica escrito:** com
lote de 250 e 442 fragmentos, uma falha no segundo lote desperdiça 250
embeddings já gerados, e o pior caso são 3 execuções × 442.

Por que mesmo assim não muda, e são três motivos independentes:

1. **Já estava decidido, com o motivo escrito.** D3 da `knowledge-base-indexacao`
   recusou explicitamente o retry dentro da execução: *"Não há retry da chamada
   ao provedor dentro da execução. Uma camada só, um contador só, um significado
   só."* Retentativa por lote é exatamente essa alternativa, reintroduzida com
   outro nome.
2. **Quebraria o significado de `IndexingAttempts`, que é texto de tela.** O XML
   doc de `MaxAttempts` afirma: *"Uma execução é uma tentativa — não uma chamada
   HTTP"*, e é isso que faz a tela de documentos poder dizer *"três tentativas, a
   última às 03:14"* sem mentir. Contar lotes faria a tela afirmar mais do que o
   sistema sabe (convenção 13), e o defeito seria invisível: o número continuaria
   saindo.
3. **A variante que "resume do lote que falhou" exige fracionar a persistência**,
   que é a D3 abaixo, e que tem guarda própria. Sem persistir progresso parcial,
   "retomar" não existe: os vetores dos lotes que passaram vivem em memória e vão
   embora com a execução.

*Alternativa considerada e recusada — retry por lote dentro da execução, em
segundos.* Além dos três acima, a medida 4 diz que ela também não resolve o caso:
o `01` variou 4,4× fazendo o **mesmo trabalho**, então uma repetição imediata do
mesmo lote grande cai no mesmo regime instável. O espaçamento de 1 e 5 minutos
das filas de espera existe justamente porque repetição em segundos não sobrevive
a um limite de janela de minuto.

*O que esta change faz em vez disso, e é o que de fato reduz o desperdício:* lote
menor é **menos trabalho a repetir por falha** e, sobretudo, **menos chance de
falhar** — que é o ponto. O desperdício de 250 embeddings só acontece num regime
em que hoje não se chega a gravar nada.

**Registro de item aberto (não é trabalho agora):** se a medição do gateway
mostrar falha de lote frequente **com o lote já pequeno**, a retentativa por lote
volta à mesa — e aí ela vem junto com a decisão de persistência parcial, porque
as duas são a mesma change. Gatilho: falha de indexação recorrente com
`BatchSize` ajustado.

### D3 — Só a chamada ao gateway é loteada; a persistência não

`CommitAsync` continua igual: **uma transação**, apaga todos os fragmentos do
documento, insere os novos, atualiza o documento condicionado a
`ContentRevision`, tudo ou nada. É a garantia 2 de D9 da etapa 1, e ela tem
cenário de spec e teste.

Os vetores de todos os lotes são acumulados em memória e gravados **uma vez**, no
fim. O custo é memória: 442 fragmentos × 4.096 `float` ≈ **7,2 MB** de vetor por
documento em voo — que é a ordem de grandeza que o código **já** aloca hoje, já
que hoje a resposta única traz tudo de uma vez. O loteamento não piora isso.

*Alternativa recusada — gravar por lote.* Fracionar a substituição integral
criaria uma janela em que o documento tem **parte** dos fragmentos novos e parte
dos antigos, e uma busca nessa janela devolveria conteúdo misturado de duas
revisões, sem erro nenhum. É o mesmo tipo de corrupção silenciosa que a
`EmbeddingIndexConsistencyValidation` existe para impedir do lado do modelo. Se
um dia for preciso, é change própria, com spec própria.

### D4 — Lotes **sequenciais**

Um lote por vez, `await` em cada um, na ordem.

O motivo é medido, não intuitivo: a medida 4 mostra o upstream variando **4,4×**
com **uma chamada só**. Concorrência contra um upstream instável aumenta a chance
de uma das chamadas estar perto do limite no momento errado, e não há medição
nenhuma que diga que o gateway aguenta N chamadas simultâneas — a medida 1 diz o
contrário do que se gostaria: o `02` falhou **isolado**, sem o `01` concorrendo.

Além disso, as durações de 267 e 175 **não são limpas** (medida 5): as duas
partes começaram no mesmo instante, em instâncias diferentes, e medem contenção
junto com custo. Usá-las para argumentar ganho de paralelismo seria citar um
número respondendo a outra pergunta — a ocorrência 3 da convenção 22.

*Alternativa recusada — lotes concorrentes com grau de paralelismo configurável.*
Precisaria de motivo medido, e o que existe medido aponta na direção oposta. Se
a medição do gateway um dia mostrar folga, é decisão a reabrir **com o número na
mão**.

**Consequência a declarar:** com lote sequencial, o tempo total de um documento
grande é a **soma** dos lotes, não o máximo. Para o `02` inteiro, a projeção a
partir das partes (8,9 s + 14,1 s, ambas contaminadas por contenção) fica na
ordem de **dezenas de segundos** — bem dentro do que a fila de indexação própria
foi desenhada para absorver (D2 da `knowledge-base-indexacao`: fila própria
justamente para que indexação de minutos não bloqueie execução de agente).

### D5 — Lote inválido reprova o **boot**, e não é corrigido em silêncio

`BatchSize <= 0` derruba o processo no boot, com mensagem que nomeia o valor
encontrado e o que se espera. Molde da convenção 8, e a quarta checagem de boot
de `apps/workers` — depois de `ValidateTimeZoneConfiguration`,
`ValidateEmbeddingIndexConsistency` e (em `apps/api`) as de registro.

Por que não é paranoia: este é o **primeiro parâmetro desta base desenhado para
ser variado à mão em produção**, por um operador que está procurando um teto. O
modo de errar é digitar `0`, e o efeito seria `Chunk(0)` estourando **por
documento**, queimando as três tentativas e gravando `Failed` com motivo técnico
em cada documento da base — exatamente o que `docs/configuration.md:247` diz
sobre `MODEL`/`DIMENSIONS` serem obrigatórias no compose: *"a ausência não impede
o boot e só aparece na primeira indexação"*.

*Alternativa recusada — clampar para um mínimo.* Corrigir em silêncio faz o
sistema executar um valor que o operador não pediu, e a medição seguinte seria
feita sobre um número que ninguém escolheu. Numa change cujo propósito é **medir
variando o parâmetro**, clampar é o defeito, não a proteção.

*Alternativa recusada — validar no primeiro uso, dentro de `IndexAsync`.* É o
comportamento que a decisão acima existe para evitar: falha por documento, tarde,
com motivo técnico na tela do operador.

*Alternativa recusada — `AddOptions<EmbeddingOptions>().Validate(...).ValidateOnStart()`.*
Existe e funciona (verificado em `Microsoft.Extensions.Options` 10.0.0:
`OptionsBuilderExtensions.ValidateOnStart<TOptions>`, e
`AddOptionsWithValidateOnStart`) — mas introduz um idioma que este repositório não
usa em lugar nenhum, troca a forma de registro de `EmbeddingOptions`, e a falha
sai como `OptionsValidationException` genérica. O idioma da casa é
`host.ValidateAlgumaCoisa()` chamado do `Program.cs`, com mensagem escrita para
quem opera, e ele já aparece três vezes. Consistência ganha.

**O custo desta forma, declarado:** nenhum teste desta suíte prova o **registro**
da chamada no `Program.cs` — os testes de `apps/workers` montam o host à mão, e o
próprio `Program.cs` já diz isso em comentário sobre o
`NonTerminalTaskDetectorService`. Remover a chamada deixa o teste da extensão
verde. Isso vira **conferência manual de escopo** no `tasks.md`, no molde que a
convenção 8 já descreve para a forma `IServiceCollection`.

### D6 — Onde o loteamento mora: dentro de `IndexAsync`, sem extrair nada

Convenção 2: o lote é **local ao serviço de indexação**. Um laço sobre
`fragments.Chunk(batchSize)`, acumulando em `List<Embedding<float>>`. Nenhum tipo
novo, nenhuma interface, nada em `libs/` — não há segundo consumidor, e o
resolvedor de tool de conhecimento (`KnowledgeToolSetResolver`) gera **um**
embedding por mensagem, nunca um lote.

A conferência de contagem passa a ser **por lote** (`vetores recebidos` ×
`entradas enviadas`), que é onde a informação é local e a mensagem pode nomear o
lote. A conferência total continua implicada por ela; a de **dimensão** continua
onde está, no laço de gravação, que é o único lugar onde a dimensão real é
conhecida.

### D7 — O alinhamento vetor↔fragmento ganha guarda próprio

Esta é a única forma de o loteamento corromper o índice **em silêncio**: se a
concatenação errar a ordem, cada fragmento fica com o vetor de outro, a busca
continua respondendo, e nada reprova. É a mesma família da divergência de modelo
que `ValidateEmbeddingIndexConsistency` cobre, e merece o mesmo tratamento.

O guarda é possível de graça porque `FakeEmbeddingGenerator` **já** deriva o
vetor do texto (`text.GetHashCode(...)`): o mesmo texto dá sempre o mesmo vetor.
O cenário afirma que, para um documento que atravessa **mais de um lote**, o vetor
gravado de cada fragmento é o que o duplo produz para **o texto daquele
fragmento** — incluindo o primeiro fragmento do segundo lote, que é onde um
off-by-batch apareceria.

### D8 — Os guardas, e como eles são exercitados

**Vermelho contra `HEAD`, pela propriedade:** um documento com **mais fragmentos
que o tamanho do lote** produz **mais de uma** chamada ao gerador de embedding.
Contra `HEAD`, uma só. Asserção sobre **número e tamanho das chamadas** — nunca
sobre texto de mensagem de erro, que é o que já mordeu nesta linha de trabalho.

**O par que passa nos dois lados** (regressão contra a correção errada, que seria
lotear sempre, inclusive quando não precisa): um documento **menor** que o lote
produz **exatamente uma** chamada. Esse par já existe: é o
`Embeddings_AreGeneratedInASingleBatch` de `KnowledgeIndexingTests.cs:65`. Ele
**não** é apagado — é renomeado e recomentado para dizer o que passou a afirmar.

**A fronteira**, que nenhum dos dois pega: fragmentos em número **exatamente
igual** ao lote produzem **uma** chamada. É o off-by-one do `Chunk`.

**O par "sem item" da convenção 5, escrito para não reprovar por vacuidade:**
documento com zero fragmentos não chama o gerador — **com asserção explícita da
precondição** (o texto tem conteúdo, e é a fragmentação que devolve vazio), para
que o cenário não fique verde por nunca ter chegado à chamada. É a mesma forma de
vacuidade que a convenção 8 nomeia no `SELECT DISTINCT` sobre tabela vazia.

**Como o volume é exercitado, e por que não com 442 fragmentos:** o tamanho do
lote é do arnês. Os cenários usam **lote pequeno** (por exemplo 2) contra um
documento de poucos fragmentos, porque a propriedade é **escala-livre** — "mais
fragmentos que o lote" não depende de o lote ser 250. Montar um documento de 442
fragmentos gravaria ~7 MB de vetor por cenário num Postgres real e empurraria a
suíte na direção da referência de duração (**6m37s com 14 classes e zero
containers de dev**, recalibrada em 20/09/2026), que é onde a convenção 22 manda
não mexer sem recalibrar.

**Onde os testes moram, e é decisão:** os cenários de loteamento entram em
`KnowledgeIndexingTests` (já na `WorkerHostCollection`), e o teste da checagem de
boot é **unitário puro**, sem `WorkerInfrastructureFixture` e sem a collection.
Motivo: uma classe nova com `IClassFixture<WorkerInfrastructureFixture>` seria a
**15ª** da coleção — par de containers próprio, e recalibração da referência de
duração como tarefa desta change (convenção 22: recalibrar é de quem acrescenta a
classe). O ganho de legibilidade de um arquivo separado não paga isso.

## Árvore de pastas

Só o que esta change toca. `(novo)` marca arquivo criado; o resto é modificado.

```
buteco-agents/
├── apps/
│   └── workers/
│       ├── src/Buteco.Workers/
│       │   ├── Knowledge/Indexing/
│       │   │   ├── KnowledgeIndexingService.cs          # laço de lotes
│       │   │   └── EmbeddingBatchSizeValidation.cs      # (novo) checagem de boot
│       │   ├── Options/
│       │   │   └── EmbeddingOptions.cs                  # + BatchSize
│       │   └── Program.cs                               # + host.ValidateEmbeddingBatchSize()
│       └── tests/Buteco.Workers.Tests/Knowledge/
│           ├── Support/KnowledgeIndexingHarness.cs      # BatchSizes no duplo; batchSize no Build
│           ├── KnowledgeIndexingTests.cs                # guardas de lote
│           └── EmbeddingBatchSizeValidationTests.cs     # (novo) unitário puro, sem fixture
├── docker-compose.prod.yml                              # EMBEDDING_BATCH_SIZE, com default
├── .env.prod.example                                    # EMBEDDING_BATCH_SIZE=250
├── docs/configuration.md                                # duas tabelas
├── 02-HISTORICO_E_STATUS.md                             # registros + posição na fila
└── CHANGELOG.md
```

**Nada em `libs/`.** Não há segundo consumidor: o loteamento é local ao serviço
de indexação, e a única outra chamada de embedding do monorepo
(`KnowledgeToolSetResolver`, consulta do agente) gera **um** vetor por mensagem.

**`appsettings.Development.json` deliberadamente não entra.** O padrão vive no
inicializador da propriedade; repeti-lo ali criaria duas fontes para o mesmo
número, que divergem na primeira vez que alguém mudar uma só — é o mesmo
raciocínio que manteve a credencial de embedding numa seção só
(`EmbeddingOptions`, XML doc).

## Projeção (convenção 18)

Feita **depois** de fechar a verificação (blast radius lido no código, idioma de
boot verificado no SDK) e **antes** de escrever código. O fechamento apenas
compara.

**Blast radius, medido por compilação e não por `grep`** — a régua mudou de
ferramenta na oitava medição, onde o `grep` deu 4 e a compilação deu 5. Aqui o
resultado é **zero modificações forçadas**: `EmbeddingOptions` ganha propriedade
com inicializador e todos os sítios de construção usam inicializador de objeto;
`FakeEmbeddingGenerator` ganha membro e `LastBatchSize` **permanece**, porque
`KnowledgeIndexingTests.cs:75` o usa. Nenhuma assinatura pública muda.

**Contagem de unidades públicas** (acertou nas duas últimas medições — mantida):

| unidade | quantas |
|---|---|
| propriedade de options nova | 1 (`EmbeddingOptions.BatchSize`) |
| método de extensão novo | 1 (`ValidateEmbeddingBatchSize`) |
| membro novo no duplo de teste | 1 (`BatchSizes`) |
| parâmetro novo no `Build` do arnês | 1 |
| tipo novo | **0** |

**Arquivos, criados e modificados contados separadamente** (o método que acertou
duas vezes seguidas), **em pares com o teste de cada um**:

| | arquivos | linhas projetadas |
|---|---|---|
| criados (produção) | 1 | ~55 |
| criados (teste) | 1 | ~55 |
| modificados (produção) | 3 | ~120 |
| modificados (teste) | 2 | ~125 |
| configuração de stack e docs | 3 | ~30 |
| registro (`02`, `CHANGELOG`) | 2 | ~130 |
| **total, à mão, ex-`openspec/`** | **12** | **~515** (faixa 460–580) |

**Mistura comentário:lógica em produção, classificada antes de multiplicar** — a
nona medição achou a distinção, e ela é por tipo de registro:

| registro de mecanismo | tipo | custo |
|---|---|---|
| 250 é provisório e não há teto medido | reconstrói uma **ausência** | caro, ~31 |
| por que a retentativa continua por documento | reconstrói um **contrafactual** | caro, ~31 |
| por que sequencial (a medida 4,4×, com regime) | número com **arquivo e linha** | barato, ~14 |
| por que reprova o boot em vez de clampar | cita o idioma existente | barato, ~14 |
| por que só o gateway é loteado | cita a garantia existente | barato, ~14 |

~104 linhas de comentário contra ~45 de lógica em produção: **~2,3:1**, a mesma
proporção da `lock-de-contexto-falha-terminal` (48:111) e pelo mesmo motivo —
o entregável inclui registro de decisão, e nessas o comentário é o produto.

**Cenários de teste: 7**, contados pelos **estados observáveis** da delta de
spec e não pelas afirmações (a régua da 5a-4, onde contar afirmações subestimou
6 contra 8):

1. mais fragmentos que o lote → mais de uma chamada, com os tamanhos certos
2. menor que o lote → exatamente uma chamada *(o existente, renomeado)*
3. fronteira: igual ao lote → uma chamada
4. zero fragmentos → nenhuma chamada, precondição afirmada
5. alinhamento vetor↔fragmento através da fronteira de lote
6. `BatchSize <= 0` reprova o boot
7. `BatchSize` válido não reprova *(o par)*

Custo unitário ~19-21 linhas, **menos o andaime compartilhado**: o duplo já conta
chamadas e o arnês já semeia documento. A convenção 18 diz que esse termo reduziu
o número projetado **duas vezes seguidas** e já é previsível — então ele entra
como termo, não como risco nomeado. Os cenários 1, 2, 3 e 5 compartilham arranjo
quase inteiro; o 5 é o mais caro (~30), por precisar ler os vetores gravados de
volta e recomputar o esperado.

**Suíte:** baseline `apps/workers` **268/268**, com o escopo colado — **14
classes** no `WorkerHostCollection`, critério de medição `podman ps` devolvendo
zero antes de rodar, referência de duração 6m37s, e rodada acima de ~10 min não é
medição. Projeção: **274/274** (+6; o cenário 2 é renomeação, não teste novo), e
**14 classes continuam 14** — o arquivo de teste novo é unitário puro.

**Duas direções de erro, nomeadas sabendo que a 3ª medição errou as duas e o
desvio veio de um terceiro lugar** (então isto não substitui a contagem acima,
que é o que carrega a projeção):

- para baixo, se a conferência por lote exigir tratar `GeneratedEmbeddings<T>`
  como algo mais que `IReadOnlyList` na concatenação;
- para cima, se os cenários 1, 2 e 3 couberem num `[Theory]` — o que reduziria
  três arranjos a um.

## Risks / Trade-offs

| risco | contraparte verificável (convenção 10) |
|---|---|
| **250 continua acima do teto real, e o `02` segue falhando** | Não é testável em suíte: o teto é do gateway. A contraparte é operacional e **não é tarefa desta change**: vira item aberto no `02`, com gatilho (o primeiro deploy com esta change aplicada) e posição (a janela do deploy da posição 5) — reindexar o `02` contra o gateway real, e o `BatchSize` é ajustável sem rebuild exatamente para isso. Fica dito: **esta change não prova que 250 resolve**; ela torna o número ajustável e o defeito mensurável, e fechá-la nunca significa que o `502` acabou |
| **Desalinhamento vetor↔fragmento na concatenação** (corrupção silenciosa) | Cenário 5 da delta: o vetor gravado de cada fragmento é o do **próprio texto**, através da fronteira de lote |
| **Off-by-one no `Chunk`** (lote a mais, ou último lote perdido) | Cenários 1 e 3: tamanhos de **cada** chamada afirmados, e a fronteira "igual ao lote → uma chamada" |
| **A correção errada: lotear sempre, inclusive quando não precisa** | Cenário 2, que passa nos dois lados e reprova essa correção |
| **Guarda vazio por vacuidade** (documento sem fragmento nunca chega à chamada) | Cenário 4 afirma a **precondição** explicitamente, não só o resultado |
| **`BatchSize` mal digitado em produção** | D5 e cenários 6/7 — reprova o boot, com o valor encontrado na mensagem |
| **Desperdício de re-embedding na retentativa por documento** | Aceito e escrito em D2, com o número (250 embeddings por falha de segundo lote, pior caso 3 × 442). Registrado como item aberto com gatilho, não como risco a mitigar agora |
| **`Program.cs` sem cobertura** (remover a chamada deixa tudo verde) | Não é testável nesta suíte, e o motivo é estrutural e já registrado no próprio `Program.cs`. Contraparte: conferência manual de escopo no `tasks.md` |
| **Tempo total maior por documento** (soma dos lotes, D4) | Absorvido pela fila própria de indexação (D2 da `knowledge-base-indexacao`), que existe para que indexação de minutos não bloqueie execução de agente |

## Migration Plan

Sem migração de banco, sem mudança de contrato de fila, sem mudança de formato de
fio. O índice gravado não muda de forma: mesmo provedor, mesmo modelo, mesma
dimensão — `ValidateEmbeddingIndexConsistency` continua passando sem reindexação.

1. Aplicar. `EMBEDDING_BATCH_SIZE` **não** é obrigatória: sem ela, o default 250
   vale, e o compose sobe.
2. Deploy de `apps/workers`.
3. Reindexar o `02` pela tela, contra o gateway real. É a verificação que a suíte
   não faz.
4. Se falhar, **baixar** `EMBEDDING_BATCH_SIZE` e repetir — é assim que o teto vai
   ser descoberto, e cada passo é uma medida a registrar no `02` com o regime
   colado.

**Rollback:** subir `EMBEDDING_BATCH_SIZE` para um número maior que qualquer
documento restaura o comportamento de `HEAD` sem redeploy. Isso é consequência do
desenho, não um modo suportado.

## Open Questions

Nenhuma de negócio ou produto. As incertezas técnicas que sobram estão nomeadas
como tais e **não** bloqueiam a implementação:

- **O formato do teto do gateway** (entradas, bytes, ou tempo de resposta) segue
  desconhecido, e é o que o passo 4 da migração existe para estabelecer.
- **Se o gateway aguenta lotes concorrentes** — D4 recusa por falta de medição,
  não por medição contrária.
