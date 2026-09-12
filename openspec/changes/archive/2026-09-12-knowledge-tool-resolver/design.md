## Context

Etapa 4 de 5 da linha de bases de conhecimento. As quatro anteriores entregaram
catálogo (`1`), índice vetorial (`2a`), operação do índice (`2b`), vínculo
agente ↔ base (`3`) e painel (`5a-1`/`5a-2`). O índice de desenvolvimento tem
**272 fragmentos gravados** com `openai`/`qwen-qwen3-embedding-8b`/4096, e
**nenhum código lê a tabela `agent_knowledge_bases`** — a entidade espelho em
`apps/workers` foi criada na etapa 3 com um `<remarks>` dizendo literalmente que
"o resolvedor de conhecimento é a etapa 4".

Três rodadas de medição precedem esta change e fecharam decisões que **não são
relitigadas aqui**: `0b` (modelo de embedding e schema), `0c` (chunker, overlap,
`k`, limiar) e `0d` (roteamento entre bases, esquema de nome). Uma exploração
própria, também registrada no `02`, fechou o encaixe e a forma do resultado.

### Verificações feitas antes das decisões (convenção 6)

#### V1 — O encaixe em `AgentExecutionService`, contra a árvore de hoje

`ExecuteAsync` resolve hoje, nesta ordem: `chatClient` (compartilhado, **não**
descartável), `toolSet` MCP (`await using`, conexão viva), `delegationTools`
(sem `await using`), e concatena via
`toolNameDeduplicator.Deduplicate(agentId, toolSet.Tools, delegationTools)` no
`ChatOptions.Tools`.

A assinatura de `IMcpToolSetResolver.ResolveAsync(dbContext, agentId, ct)` serve
inteira para conhecimento: não são necessários `sourceAgent`, `contextId`,
`currentDepth` nem `messageInstant`, que são o que engorda a assinatura de
delegação. **Confirmado que não precisa de `await using`**: não há conexão
externa viva por trás, e o gerador de embedding é resolvido **dentro** da
invocação da tool, não na resolução do conjunto.

#### V2 — A consulta traduz em LINQ, e a distância é projetável

Verificado executando contra o Postgres de desenvolvimento com os pacotes exatos
do repositório (`Pgvector.EntityFrameworkCore` 0.3.0,
`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3). A expressão
`orderby f.Embedding.CosineDistance(q)` com
`select new { ..., Distance = f.Embedding.CosineDistance(q) }` emite:

```sql
SELECT k0."Title", k."Text", k."Embedding" <=> @query AS "Distance"
FROM knowledge_fragments AS k
INNER JOIN knowledge_documents AS k0 ON k."KnowledgeDocumentId" = k0."Id"
WHERE k."KnowledgeBaseId" = @baseId
ORDER BY k."Embedding" <=> @query
LIMIT @p
```

25 ms de ponta a ponta, materialização do EF incluída, contra os 272 fragmentos
reais. **Projetar a distância é a parte não-óbvia**, e sem ela a decisão de `0c`
("devolver os k com a distância explícita") não seria implementável sem SQL cru.
Não precisa de SQL cru.

#### V3 — Latência, e o gatilho do índice ANN

Medido em `pgvector/pgvector:pg18`, busca exata `ORDER BY <=> LIMIT 5` sobre
`vector(4096)`, buffers quentes:

| fragmentos **na base consultada** | latência |
|---|---|
| 272 (índice real de dev) | ~14 ms |
| 272, com join e projeção do texto | ~27 ms |
| 272, **primeira consulta após ociosidade** | **265 ms** |
| 2.000 | 111 ms |
| 7.500 | 333 ms |
| 20.000 | ~950 ms |

Inclinação ~47 µs por fragmento, linear. O gatilho registrado (p95 > 200 ms) fica
em **~4.300 fragmentos na maior base**, e o filtro é `KnowledgeBaseId`, então o
que conta é a maior base e não o índice inteiro. **Nada a fazer nesta change** —
a correção do `01` e do `docs/architecture.md` já foi aplicada em sessão própria.

Orçamento por mensagem: ~14 ms típicos, ~265 ms de pior caso na busca, mais
~40 ms típicos / ~300 ms de pior caso no embedding (`0b`). Contra a segunda ida
ao LLM, que custa segundos, é ruído.

#### V4 — `IEmbeddingGenerator` na consulta: não há desalinhamento

`Microsoft.Extensions.AI.Abstractions` 10.8.1 publica
`EmbeddingGeneratorExtensions.GenerateVectorAsync<TInput,TElement>(generator, value, options, ct)`,
que devolve `ReadOnlyMemory<TElement>` e lança `InvalidOperationException` se o
gerador não produzir exatamente um embedding. **A consulta não precisa de caminho
próprio**: o `IEmbeddingGeneratorResolver` da etapa 2a serve direto, numa linha.

#### V5 — A checagem de integridade do boot cobre a consulta

`EmbeddingIndexConsistencyValidation` roda no boot de `apps/workers`, faz
`SELECT DISTINCT` sobre as três colunas de proveniência e derruba o processo em
divergência ou em mais de uma combinação. **A consulta não precisa reconferir
dimensão**, porque o vetor da query vem do mesmo `Embedding:Model` que o boot já
comparou com o índice. Confirmado no banco de dev: uma combinação só.

#### V6 — O análogo do vazamento de `IChatClient` não se repete

`EmbeddingGeneratorResolver` constrói um cliente novo por chamada. Isso é seguro
mesmo na frequência por-mensagem que esta change cria, e o motivo está verificado
por decompilação (C4 do `design.md` de `fix-vazamento-httpclient-chat`):
`System.ClientModel` 1.14.0 serve o transporte de
`HttpClientPipelineTransport.Shared`, com um `private static readonly HttpClient`
— um por processo. O resolvedor só constrói `openai`; `anthropic` e `gemini`
lançam por ausência de capacidade.

**O gatilho já foi plantado no `default:` do `switch` daquele resolvedor**, em
sessão própria: um provedor de embedding não-OpenAI precisa nascer **com** cache
por `(provider, model)`, porque nele o vazamento apareceria na frequência de
mensagem, ordens de grandeza acima da de indexação onde foi descoberto.

#### V7 — Censo de nome de tool, estendido ao terceiro conjunto

A consulta de V8 da change `dedupe-global-nome-de-tool`, rodada contra o Postgres
de desenvolvimento com **dois esquemas candidatos em paralelo**, para o censo
decidir em vez de confirmar:

```
 tool_name                            | n | origem
--------------------------------------+---+-------------------------------
 delegate_to_gestor-de-reservas       | 1 | delegação
 Gestor_de_Agentes__search            | 1 | conhecimento [B: Nome__search]
 Informa__es_Gerais__get_menu_info    | 1 | mcp
 … (mais 9 de MCP)
 search_gestor-de-agentes             | 1 | conhecimento [A: search_slug]
```

**13 nomes, todos distintos, nos dois esquemas.** Inventário: 5 agentes, 2
`McpServer` (3 vínculos, 12 tools), 1 delegação, 1 base vinculada; o nome mais
longo tem 41 caracteres. Dev é o único ambiente que existe, então é o censo
completo.

O censo entrega mais que o zero — ver D3.

#### V8 — `KnowledgeFragment` é ignorado fora de Npgsql

`AppDbContext.OnModelCreating` mapeia `KnowledgeFragment` dentro de
`if (Database.IsNpgsql())` e faz `modelBuilder.Ignore<KnowledgeFragment>()` no
`else`, porque o EF valida o **modelo inteiro** e o `Vector` não tem construtor
vinculável fora do provider relacional. **Consequência direta: todo teste desta
change exige Postgres real**, e a `WorkerHostCollection` vai de 11 para 12
classes — ver R6.

#### V9 — O texto gravado já carrega o caminho de cabeçalhos

`KnowledgeChunker.Emit` injeta `PrefixFor(documentTitle, path)` no **próprio
texto emitido** (`"Título > Seção > Subseção\n\n" + corpo`), e
`ChunkedFragment.HeadingPath` documenta que não é persistido por isso.
Confirmado nos dados reais:

```
0,2658  02 HISTORICO E STATUS  | Buteco Agentes — Histórico e Status > Changes aplicadas, por linha de …
```

Repare que o `Title` do catálogo (`02 HISTORICO E STATUS`, dado pelo operador) e
o `#` do markdown (`Buteco Agentes — Histórico e Status`) **são diferentes** — o
primeiro não está no fragmento. Ver D5.

#### V10 — Custo de contexto de `k = 5`, medido no índice real

Média de **1.146 caracteres** por fragmento (mín. 147, máx. 1.600 — o invariante
I2 respeitado nos dados reais). Um top-5 sorteado deu **5.209 caracteres**, ou
~1.400-1.600 tokens por chamada de tool, por mensagem. Os "67% mais contexto"
que `0c` registrou para `k=5` sobre `k=3` batem, e agora têm valor absoluto.

#### V11 — Custo de DI, contável antes (régua de `0a`)

`grep -rn "AddSingleton<IMcpToolSetResolver" apps/workers/tests tests` devolve
**13 arquivos** que constroem `AgentExecutionService` e registram os resolvedores
nulos. Injetar uma dependência nova obriga a tocar todos, mais dois duplos nulos
novos (um por projeto de teste). **15 arquivos de custo de DI**, que não são
trabalho de teste.

## Goals / Non-Goals

**Goals:**

- Uma `AITool` por base de conhecimento **vinculada ao agente e ativa**, com a
  `Description` da base como descrição da tool.
- Busca vetorial exata por distância de cosseno, filtrada pela base, `k = 5`,
  **sem limiar**, com a **distância explícita** em cada resultado.
- O terceiro conjunto de tools integrado ao namespace compartilhado, com
  precedência declarada e renomeação observável.
- Base vinculada sem conteúdo indexado tratada como caso próprio, distinto de
  "a busca não achou nada".

**Non-Goals:**

- **Busca unificada entre bases** (`WHERE KnowledgeBaseId IN (...)`). Medida por
  `0d` como superior fim a fim, **sob equilíbrio de tamanho entre bases**, e
  registrada como carve-out com **posição na fila** logo após esta change. Não
  entra aqui porque depende do resolvedor, da consulta e dos guardas que esta
  constrói, e porque o dado que falta (caso real com bases desiguais) não existe
  ainda.
- **Reranker.** Item aberto de `0c`, escopo novo (segunda chamada de modelo por
  consulta), gatilho próprio.
- **Índice ANN / coluna derivada `halfvec`.** Gatilho medido em ~4.300 fragmentos
  na maior base; o índice de dev tem 272.
- **Limiar de distância, híbrido lexical, reescrita de query.** Reprovados por
  medição em `0b`/`0c`.
- **Qualquer mudança em `apps/api`, `apps/inbox`, `apps/frontend`, no schema ou
  no contrato HTTP.** Nenhuma migração.
- **Expor na UI qual base o agente consultou.** Nada coleta isso hoje; propô-lo
  seria a UI afirmando mais do que o sistema sabe.

## Decisions

### D1 — Uma tool por base vinculada, não uma tool genérica com parâmetro

Decidido antes desta change e **não relitigado**: a `Description` da base é o que
dá ao modelo o critério de escolha, e `apps/api` já a exige não-vazia desde a
etapa 1 exatamente por isso. Mesmo idioma de `AgentDelegationToolSetResolver`,
que já faz uma `AITool` por vínculo via `AIFunctionFactory.Create`.

**Alternativa recusada antes:** uma tool genérica com parâmetro de base — o
modelo passaria a escolher por um valor de argumento em vez de por seleção de
ferramenta, sem ganho e perdendo a descrição por base.

**Alternativa que `0d` mediu e que NÃO é esta:** a busca unificada (Non-Goal
acima). `0d` mediu roteamento de **61,0%** contra um bar cuja faixa de baixo
começava em 75%, e fim a fim 57,8% (por base) contra 77,1% (unificada). **Mas a
decomposição diz o contrário do agregado**: quando o roteamento acerta, o desenho
por base entrega **92,3%** de R@5, quinze pontos acima da unificada — o
break-even é 83,5% de roteamento. O desenho por base não é pior; ele é melhor e
desperdiça a vantagem no passo anterior. Por isso ele é o que se constrói, e a
unificada é carve-out com posição, não substituição.

### D2 — A tool devolve três campos, e `headingPath` não é um deles

Por resultado: **`documento`** (o `Title` do catálogo), **`trecho`** (o `Text`
gravado) e **`distancia`**.

`headingPath` **não entra** porque V9 mostrou que ele já está dentro do `Text` —
o chunker o injeta como prefixo do próprio texto vetorizado. Acrescentá-lo
duplicaria texto em toda chamada e faria o fragmento e o campo divergirem no dia
em que o prefixo mudasse.

O `Title` do catálogo **entra** porque é o único nome que o operador controla e o
único que **não** está no fragmento: V9 mostrou os dois lado a lado, diferentes.
Sem ele o modelo não consegue dizer ao usuário de onde veio a informação com o
nome que o painel exibe.

**Alternativa recusada:** devolver também `knowledgeDocumentId`/`ordinal`. São
identificadores internos que o modelo não pode usar para nada — não há rota que
os resolva, e citá-los numa resposta ao usuário final seria ruído.

### D3 — Nome da tool: `search_<slug>`, e o censo é quem decide

`ToolNameSanitizer.Sanitize($"search_{DelegationToolNameSlugifier.Slugify(base.Name)}")`.

V7 rodou dois esquemas. O que decide **não é o zero de colisões** — é que só um
dos dois tem caminho de colisão real nesta base:

- **Esquema recusado, `<Sanitize(Nome)>__search`**: o censo contém
  `Informa__es_Gerais__get_menu_info`, de um servidor MCP real chamado
  *"Informações Gerais"* cujo `__` **não é o separador** (`ç` e `õ` viram um `_`
  cada). Uma base homônima produz `Informa__es_Gerais__search`, que colide com
  esse servidor se ele expuser uma tool chamada `search` — e `search` é um nome de
  tool MCP comum.
- **Esquema adotado, `search_<slug>`**: colidir com MCP exigiria um servidor cujo
  nome sanitizado começasse por `search_`; com delegação é impossível pelo
  prefixo `delegate_to_`. Sobra a colisão entre duas bases de mesmo slug — que é
  exatamente o caso que o deduplicador já resolve para agentes homônimos.

Reusa `DelegationToolNameSlugifier`, que remove diacríticos via `FormD` (o
esquema recusado não removia). **É o segundo consumidor**, que é o que a
convenção 2 pede para extrair: o tipo sai de `AgentDelegations/` para um espaço
neutro, sem mudar comportamento.

**`search_` contra `buscar_` foi medido por `0d` e não discriminou**: 62,7%
contra 60,2%, abaixo do piso de 5 pontos declarado antes. A escolha vai para
`search_` pelo idioma da casa — `delegate_to_` é o único prefixo de nome de tool
que existe no repositório, e é inglês com descrição em português.

### D4 — Nenhum limiar, e a `Description` não pode conter dígito de corte

`0c` fechou contra o limiar com 20 negativas: as médias separam (0,3357 com alvo
contra 0,4742 sem), as caudas não — a pior positiva fica a 0,5154 e a negativa
mais próxima a 0,3865, e 16 de 83 positivas têm topo mais longe que a negativa
mais próxima.

**A consequência para a `Description`, que é onde a decisão se sustenta ou cai:**
ela **não pode** conter um número de corte. Um "distâncias acima de 0,45
provavelmente não respondem" seria o limiar reprovado, reintroduzido em prosa e
sem nem o benefício de ser testável. Distância exposta que o modelo não sabe ler
é limiar escondido; distância exposta com um corte em prosa é o limiar reprovado
com outro nome.

O que a `Description` afirma, e é tudo verdadeiro (convenção 13 aplicada à tool):

- devolve **sempre** os 5 trechos mais próximos, **inclusive quando nenhum
  responde**;
- a distância ordena, menor é mais próximo, e **não é medida de acerto**;
- o critério de relevância é ler o trecho e ver se ele fala do que foi
  perguntado.

**A tool não diz "encontrei a resposta", porque o sistema não sabe isso.** Ela
diz "os 5 trechos mais próximos", que é literalmente o que ela sabe.

### D5 — Base sem conteúdo indexado é caso próprio, não "não achei nada"

Sem limiar, um índice povoado devolve **sempre** `min(k, n)` resultados. Logo
"zero resultados" só acontece quando a base vinculada tem **zero fragmentos** —
documentos `Pending`, `Failed`, ou base vazia.

Dizer "a busca não encontrou nada relacionado" nesse caso seria **falso**: o
sistema afirmaria ter procurado e não achado, quando não havia onde procurar. É a
convenção 13 na direção forte, e os dois estados são distinguíveis por uma
contagem. A tool diz que a base ainda não tem conteúdo indexado.

**São dois cenários de spec, não um** — o par "com item"/"sem item" da convenção
5 tem aqui três pontas: base com conteúdo e resultado, base com menos de `k`
fragmentos, e base sem fragmento nenhum.

### D6 — Base inativa filtrada na resolução, não relida na chamada

`where kb.IsActive` na consulta de vínculos, idioma de `McpToolSetResolver`
(`where server.IsActive`), **não** o de `AgentDelegationToolSetResolver`, que
relê o Target fresco do banco a cada invocação.

O motivo é verificável, não estilístico: a delegação relê porque
`WaitForTerminalStateAsync` pode segurar a tool por **minutos**, e uma
desativação no meio disso é cenário real (Decision 4/5 daquela change). Aqui a
janela entre resolver o conjunto e invocar a tool é a mesma `RunAsync` — segundos.
Reler seria uma consulta a mais por invocação para cobrir uma janela que não
existe.

Decisão herdada da etapa 3, que a declarou explicitamente fora da sua spec: *"o
estado inativo da base afeta apenas o momento em que o conhecimento é oferecido
ao agente em execução — comportamento que pertence à capability de execução e não
a esta"*.

### D7 — Sem `await using`, e o comentário que já está lá é o que protege

O resolvedor devolve `IReadOnlyList<AITool>`, não um tipo descartável. Não há
conexão externa viva por trás: a busca usa o `AppDbContext` já aberto pelo
chamador, e o gerador de embedding é resolvido **dentro** da invocação da tool.

Isso põe o terceiro resolvedor ao lado de um `await using` (o do `toolSet` MCP) e
de um `chatClient` que **não pode** ser descartado — e é exatamente o sítio onde
alguém acrescenta simetria. As 18 linhas de comentário plantadas por
`fix-vazamento-httpclient-chat` em `AgentExecutionService.cs` estão no caminho de
quem editar, e esta change **as reforça em vez de contrariá-las**: depois dela,
dois dos três resolvedores não têm `using` e só o MCP tem, o que torna a
assimetria majoritária e mais fácil de ler.

### D8 — Precedência MCP → delegação → conhecimento

`Deduplicate(agentId, mcpTools, delegationTools, knowledgeTools)`, com
`ToolOrigin.Knowledge` acrescentado ao enum.

Conhecimento é o último a entrar, e o motivo é o mesmo que pôs delegação depois
de MCP: a ordem dos parâmetros **é** a precedência declarada, e quem chega depois
é o renomeado. Conhecimento é o conjunto mais novo e o que tem menos nome em uso
— renomear uma tool de conhecimento é a mudança que quebra menos.

**Alternativa recusada:** dedupe local dentro do resolvedor de conhecimento.
Seria o defeito que `0a` corrigiu, reintroduzido — dois mecanismos para a mesma
invariante, e o local é o que não sabe que divide namespace com os outros. O
`orderby` determinístico continua sendo responsabilidade do resolvedor, como nos
outros dois.

### D9 — O gerador de embedding é resolvido dentro da invocação, não na resolução

`embeddingResolver.Resolve()` acontece no corpo da tool, a cada chamada, e o
resultado **não é descartado** — mesmo tratamento que `KnowledgeIndexingService`
já dá.

Duas razões:

1. **Resolver na montagem do conjunto gastaria uma construção por mensagem mesmo
   quando o modelo não chama tool nenhuma** — e o caso comum é justamente esse.
2. **Mantém o ciclo de vida fora de `AgentExecutionService`**, que é onde a
   assimetria de descarte já é delicada (D7).

V6 verificou que construir por chamada não vaza no caminho `openai`.

### D10 — Consulta em LINQ, não em SQL cru

V2 verificou que `Pgvector.EntityFrameworkCore` traduz `CosineDistance` no
`ORDER BY` **e** na projeção. SQL cru só se justificaria se a distância não fosse
projetável, e ela é.

O `JOIN` com `knowledge_documents` é necessário e barato (é o `Title` do D2);
medido em ~27 ms contra ~14 ms sem ele, sobre 272 fragmentos.

### Árvore de pastas proposta

```
apps/workers/src/Buteco.Workers/
├── Agents/
│   └── AgentExecutionService.cs                      (M) resolve e concatena o 3º conjunto
├── Knowledge/
│   ├── Chunking/                                     (=) sem alteração
│   ├── Embedding/                                    (=) sem alteração
│   ├── Entities/                                     (=) sem alteração — ganham o primeiro leitor
│   ├── Execution/                                    (+) NOVA PASTA
│   │   ├── IKnowledgeToolSetResolver.cs              (+)
│   │   ├── KnowledgeToolSetResolver.cs               (+)
│   │   ├── KnowledgeSearchResult.cs                  (+) documento / trecho / distância
│   │   └── KnowledgeToolDescription.cs               (+) o texto que ensina a ler a distância
│   └── Indexing/                                     (=) sem alteração
├── Mcp/
│   ├── ToolNameDeduplicator.cs                       (M) terceiro conjunto
│   ├── ToolNameSanitizer.cs                          (=) reusado
│   └── ToolOrigin.cs                                 (M) terceiro valor
├── Naming/                                           (+) NOVA PASTA
│   └── ToolNameSlugifier.cs                          (+) movido de AgentDelegations/
├── AgentDelegations/
│   ├── DelegationToolNameSlugifier.cs                (-) movido para Naming/
│   └── AgentDelegationToolSetResolver.cs             (M) usa o tipo movido
└── Program.cs                                        (M) registro

apps/workers/tests/Buteco.Workers.Tests/
├── Knowledge/
│   ├── KnowledgeToolSetResolverTests.cs              (+) resolvedor direto
│   └── KnowledgeToolExecutionEndToEndTests.cs        (+) pipeline real
├── Agents/AgentToolNamespaceTests.cs                 (M) cenários do 3º conjunto
├── Support/NullKnowledgeToolSetResolver.cs           (+)
└── (12 harnesses)                                    (M) 2 linhas cada

tests/InboxOrchestratorRoundTrip.Tests/Support/
├── NullKnowledgeToolSetResolver.cs                   (+)
└── RoundTripFixture.cs                               (M) 2 linhas
```

**Nada em `libs/`.** O slugificador é movido dentro de `apps/workers`, não
promovido a biblioteca compartilhada: ele tem dois consumidores **no mesmo app**,
e `libs/` existe para o que `apps/api` e `apps/workers` compartilham de verdade
(hoje só `ProviderCatalog`).

## Risks / Trade-offs

**[R1] Recuperação irrelevante devolvida como boa.** `0c` reproduziu: pergunta
sem alvo no corpus devolveu topo com score de boa aparência. Sem limiar, a defesa
é a `Description` e a distância explícita.
→ **Contraparte:** cenário com query sem alvo, asserção sobre a **forma** do que
volta (k trechos + distância), mais a **asserção negativa** (convenção 13): o
resultado não contém campo `resposta`/`relevante`/`confianca`, e a `Description`
não contém dígito de corte. É o guarda que reprova a regressão bem-intencionada
de "deixar mais útil".

**[R2] A ordenação por distância é a quinta forma da convenção 15.** Com fixture
pequena, o Postgres às vezes devolve na ordem certa **sem** o `ORDER BY` — e
"ordenado porque o `ORDER BY` existe" e "ordenado porque o plano calhou" são a
mesma observação.
→ **Contraparte:** par determinístico, precedente de
`ordenacao-desempate-listas-vinculo` — capturar o `Executed DbCommand` do EF e
afirmar que o `ORDER BY` é a expressão `<=>` com `LIMIT`. O SQL emitido está em
V2, verificado.

**[R3] O guarda de "base inativa não é oferecida" passa vago.** Se o arranjo usar
base inativa **sem fragmentos**, ele mede a ausência de conteúdo e não o filtro —
primeira forma da convenção 15.
→ **Contraparte:** o arranjo usa base inativa **vinculada e com fragmentos
gravados**, que produziria a tool se o filtro não existisse. Reintroduzir a
remoção do `where kb.IsActive` e ver reprovar.

**[R4] O guarda de colisão no componente errado.** Segunda forma da convenção 15,
e `0a` errou assim três vezes.
→ **Contraparte:** o guarda reprova no `ToolNameDeduplicator`, não no resolvedor
novo. Cenário: duas bases cujos nomes slugificam igual, mais uma tool MCP
homônima.

**[R5] Roteamento entre bases em 61%.** Medido por `0d`, abaixo do bar. Um agente
com várias bases vinculadas vai consultar a errada em ~4 de cada 10 perguntas, e
o erro é **irrecuperável** (0 de 31 nas medições).
→ **Contraparte:** não há teste — é propriedade do modelo, não do código.
Registrado como carve-out **com posição na fila** (busca unificada), e a
`Description` da base passa a ter consequência operacional nomeada: duas bases
cujas descrições se contêm roteiam mal de forma assimétrica, a genérica engolindo
a específica. **Vai para o `01` como orientação de operação**, não como requisito.

**[R6] A suíte fica mais pesada por dentro.** V8: todo teste desta change exige
Postgres real; a `WorkerHostCollection` vai de **11 para 12**, e o limiar de
carga declarado está calibrado para **7** — já reprovou 1/212 com carga dentro
dele em 11.
→ **Contraparte:** recalibrar o limiar é **tarefa desta change**, com as três
rodadas medidas (baseline, fechamento, repetição) registradas. E esta é a
**quarta ocorrência** da família *"referência medida citada depois que o estado
mudou"*, que o item aberto diz decidir a promoção à convenção no `01`.

**[R7] Custo de contexto por mensagem.** ~1.400-1.600 tokens de trecho (V10) mais
a segunda ida ao LLM, num consumidor com `prefetchCount: 1`.
→ **Contraparte:** dimensionado com número medido, e `0c` registrou o custo de
`k=5` sobre `k=3` explicitamente. A dívida de amplificação (N conversas
concorrentes prendendo N workers) já está registrada como item aberto e **não é
desta change**.

**[R8] Fragmento-atrator residual de 12%.** A mitigação de `0c` (repetir cabeçalho
de tabela) é parcial e vale para tabela; glossário, lista de códigos e índice
remissivo reproduzem o defeito sem ter cabeçalho para repetir.
→ **Contraparte:** não tem, e afirmar que tem seria o erro. Fica com o gatilho de
`0c` (o mesmo fragmento no topo de consultas sem relação), e **a distância
explícita é o que dá ao operador como enxergá-lo** — 0,37 de média contra mediana
0,54 é visível no resultado da tool.

**[R9] Documento excluído entre a busca e a resposta.** A FK em `Cascade` apaga
os fragmentos; o texto já está na janela de contexto.
→ **Aceito e nomeado.** A janela é de uma `RunAsync`. Evitá-lo exigiria segurar
transação durante a chamada ao LLM, que é pior. Escrito aqui para não ser
redescoberto como defeito.

**[R10] Injeção via documento.** Conteúdo de base entra na janela de contexto do
agente sem sanitização.
→ **Mitigação segue procedimental**, sem mudança: hoje só o operador alimenta as
bases. Gatilho já registrado para o dia em que usuário final alimentar uma.

**[R11] Mover `DelegationToolNameSlugifier` toca a delegação.** Refatoração de
namespace num caminho em produção.
→ **Contraparte:** é movimento puro de tipo, sem mudança de comportamento, e
`DelegationToolNameSlugifierTests` já existe e prende a saída. Se a suíte de
delegação passar sem alteração de asserção, o movimento está correto.

## Migration Plan

Não há migração de banco, de configuração nem de dado. A change é **aditiva no
comportamento**: um agente sem base vinculada recebe exatamente o conjunto de
tools que recebe hoje.

**Efeito de implantação:** agentes com base vinculada passam a receber tools
novas. Os vínculos existentes foram criados pela etapa 3 e estão parados desde
então — no banco de dev, **um** vínculo, do agente *Atendente Ambiente Software*
para a base *Gestor de Agentes*.

**Rollback:** reverter a change. Nenhum estado persistido muda, então não há o
que desfazer no banco. Um `conversationSession` gravado durante a vigência pode
citar uma tool de conhecimento que deixou de existir — e V7 de `0a` já verificou
que o pipeline local (`FunctionInvokingChatClient`/`ChatClientAgent`) é
transparente a histórico que referencia função ausente da lista atual. A pergunta
que sobra (algum provedor rejeita?) é o item aberto R7 de `0a`, sem mudança.

## Open Questions

1. **A `Description` da tool deve citar o nome da base, além de a base já estar
   no nome da tool?** Dois canais dizendo a mesma coisa custa tokens em toda
   mensagem; um só pode ser pouco quando o slug trunca em 64 caracteres. Decidir
   na implementação, com o texto na mão.
2. **O limite de 64 caracteres do nome vale para quantos provedores?** Continua o
   R8 de `0a`, sem mudança: verificado em fonte primária para OpenAI (Chat
   Completions) e Gemini, **não** para Anthropic. Esta change não altera o
   argumento nem depende dele — só acrescenta um terceiro produtor de nomes ao
   mesmo sanitizador.
3. **A busca unificada muda de posição na fila se o primeiro operador vincular
   bases muito desiguais?** `0d` mediu sob equilíbrio de 2,5:1 e declarou que o
   resultado favorável **não transfere** para desequilíbrio grande. O dado que
   decide é um caso real, e ele não existe ainda.
