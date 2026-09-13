## Context

A etapa **5c** é a última tela prevista da linha de bases de conhecimento, e a
única cuja dependência de backend foi proposta e entregue **por causa dela**:
`GET /knowledge-index/diagnostics`, de
`openspec/changes/archive/2026-09-13-knowledge-index-diagnostics/`.

Esta change **implementa o que já foi decidido** naquele `design.md` e no
`02-HISTORICO_E_STATUS.md`. O que entra aqui de novo é o que só a verificação
contra a árvore de hoje e o **percurso do protótipo** podiam produzir.

### Os três corpos reais da rota — o contrato que o cliente consome

Capturados à mão, com `apps/api` de pé contra um `pgvector/pgvector:pg18` real e
token de operador, e guardados na seção *"Corpos reais da rota"* do `design.md`
arquivado. **Não são o que um teste construiu.** Reproduzidos aqui só na forma,
porque é sobre eles que o tipo do cliente é escrito:

| estado | resposta |
|---|---|
| índice vazio | `200` com `[]` (e `401` sem token) |
| uma combinação | `200` com um item: `provider`, `model`, `dimensions: 4096`, `fragmentCount` |
| **duas combinações** | `200` com dois itens — `nomic-embed-text-v1.5` antes de `qwen-qwen3-embedding-8b` |

Três fatos que decidem a tela e que **não** se reavaliam aqui:

1. **A ordem vem da collation do banco.** A rota ordena na consulta porque
   `api-response-ordering` exige o comparador do banco como canônico. A tela
   **não reordena no cliente** — e isso não é preferência: o painel já tem um
   item aberto porque `features/agents/utils/knowledgeBaseRows.ts:26-27`
   introduziu um terceiro comparador (`localeCompare`) onde a spec diz
   *"reproduz a ordem que a API devolve"*
   (`02-HISTORICO_E_STATUS.md:4426-4437`, gatilho imediato, change de
   `apps/frontend`). Acrescentar um quarto comparador nesta base, na mesma
   semana, seria repetir o defeito com o item aberto ainda aberto.
2. **A proveniência é do sistema, não da base.** A rota não aceita identificador
   de base; `AppDbContext.cs:250` declara `vector(4096)`, e gravar outra dimensão
   é erro na hora, não divergência a detectar depois.
3. **Mais de uma combinação é resposta válida** — `200`, dois itens. É o estado em
   que `apps/workers` reprova o boot
   (`EmbeddingIndexConsistencyValidation.cs:72-82`) e `apps/api` continua de pé.
   É **o** estado em que esta tela é aberta.

### Conferência protótipo × código, feita ANTES das decisões (convenção 6)

O protótipo foi lido pela conexão com o Claude Design e **percorrido** em
navegador headless a 1860×1200, nos dois esquemas de cor. Os três arquivos que
valem estão anexados em `design/` — `Buteco Agentes.dc.html`, `support.js` (sem
ele o `.dc.html` não roda) e `CONHECIMENTO.md` (a revisão 2 do handoff) —,
**byte a byte idênticos** aos do projeto no dia da leitura (163.947 / 69.150 /
17.656 bytes).

**O que a página já tem na mão, conferido na árvore:**

| dado da aba | de onde sai | requisição nova? |
|---|---|---|
| provedor, modelo, dimensão, fragmentos por combinação | `GET /knowledge-index/diagnostics` | **sim**, uma |
| documentos com fragmentos no índice, X de Y | `documentsQuery.data` (`KnowledgeBaseDetailPage.tsx:44`) | não |
| fragmentos desta base | idem, somando `fragmentCount` | não |
| documentos em falha | idem, `indexingStatus === 'Failed'` | não |
| motivo da falha + reindexar | **já renderizados** por `KnowledgeDocumentsCard` (5a-2) | não |

`ListKnowledgeDocumentsQueryHandler` **não pagina**, então a listagem traz
`fragmentCount`, `indexingStatus`, `indexedAt` e `failureReason` de **todos** os
documentos da base. As três derivações são exatas.

**O que o percurso do protótipo achou, e a leitura do código não teria achado:**

- No estado vazio (base `Rotinas Internas`), a aba renderiza **o bloco
  explicativo e as três linhas com `—` ao mesmo tempo**, mais
  `Documentos indexados: 0 de 0` e `Fragmentos no índice: 0` logo abaixo, tudo
  sob o cabeçalho `COMO O ÍNDICE FOI CONSTRUÍDO`. Dizer a mesma coisa duas vezes é
  o menor dos problemas: `—` já significa **"não sei"** nesta área, por decisão
  registrada da 5a-3 (`utils/indexingSummary.ts`, a gramática de três estados).
  Índice vazio não é "não sei" — é fato conhecido e medido.
- A semente **não consegue exibir** o defeito da soma de fragmentos: nenhum
  documento dela está no estado "indexou e falhou ao reindexar". Ou seja, o
  percurso confirma o que o `02` já dizia sobre o método — *"uma lista construída
  só olhando texto de tela não teria achado nenhuma soma errada"* — e também o
  contrário: **percorrer não substitui ler o código do mock**, e nenhum dos dois
  substitui o outro.
- A aba repete, **inteira**, a faixa de falha da tabela de documentos: título,
  motivo completo e botão `Reindexar documento`. No protótipo isso é duplicação
  do próprio protótipo (as duas abas mostram o mesmo bloco); no painel real a
  faixa da tabela é entrega da 5a-2 e está viva.
- O cabeçalho de detalhe do protótipo carrega badges extras (`Nada indexado`,
  `1 documento falhou`) que o `DetailHeader` real não tem. Fora de escopo aqui, e
  **já recusados em substância** pela C10 da 5a-3, pelo mesmo motivo: repetem, em
  tom de alerta, o que a linha vizinha já diz.

**Das onze correções de protótipo vivas, quais tocam esta tela** — conferidas uma
a uma, não presumidas:

| # | correção | toca a 5c? |
|---|---|---|
| C8 | soma de fragmentos filtra por estado e subconta | **sim, é o centro desta tela** |
| C1 | `0 fragmentos` → o separador é `indexedAt`, nunca o estado | **sim**, é a mesma regra que C8 aplica à soma |
| C11 | tom de alerta só na parcela de falha | **sim**, a aba tem parcela de falha |
| C10 | badge `Nada indexado` em base sem o que indexar | **sim, por analogia** — o estado vazio da aba diz a mesma frase |
| C2 | "documentos muito grandes tendem a bater no limite" | **não** — a frase é `reason` de dado, não cópia estática; a aba renderiza o motivo como veio |
| C9 | coluna distingue pendente de indexando e a rota não | não — é do catálogo |
| C3–C7 | upload/multipart, busca com acento, base inativa no modal, aviso do modal, ordem dos arquivos | não |

Esta change acrescenta a **C12** e a **C13**, nas decisões D9 e D10.

## Goals / Non-Goals

**Goals:**

- Servir, no detalhe da base, a **proveniência gravada do índice** — do índice
  inteiro, dita como do sistema.
- Representar com honestidade os **quatro** estados que o cliente pode ver:
  índice vazio, uma combinação, **mais de uma combinação** (corrupção), e
  **falha ao ler** — que não é nenhum dos três.
- Derivar o volume desta base do que a página já carregou, com o predicado certo
  (`indexedAt != null`), sem requisição a mais.
- Fazer nascer a barra de abas do detalhe da base, no desenho já estabelecido.

**Non-Goals:**

- **Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/inbox`.** Nenhuma
  referência entre apps.
- **Nenhuma rota nova**, nenhum campo novo em resposta nenhuma. Se faltar dado, é
  achado a reportar e sequenciar (convenção 1), não backend de improviso.
- **Nenhuma ação de reindexação em massa.** Ela não existe no backend; oferecê-la
  seria afirmar uma capacidade que o sistema não tem.
- **Nenhuma métrica de uso** — consultas por base, popularidade de documento,
  taxa de acerto. O sistema não coleta nada disso, e o protótipo já diz isso na
  tela, com razão.
- **Nenhuma barra de progresso percentual.** O sistema conhece o estado, não o
  percentual.
- **Nenhuma extração de componente compartilhado.** Ver D13.
- **Nenhuma correção do terceiro comparador de `knowledgeBaseRows.ts`.** É item
  aberto de outra feature, com gatilho imediato e change própria; puxá-lo para cá
  misturaria dois escopos.

## Árvore de pastas proposta

```
apps/frontend/src/features/knowledge-bases/
├── api/
│   ├── knowledgeBasesApi.ts                     (existe, NÃO muda — ver D1)
│   ├── knowledgeDocumentsApi.ts                 (existe, não muda)
│   ├── knowledgeIndexApi.ts                     (novo)
│   ├── knowledgeIndexApi.test.ts                (novo)
│   ├── useKnowledgeIndex.ts                     (novo)
│   └── useKnowledgeIndex.test.ts                (novo)
├── components/
│   ├── KnowledgeDocumentsCard.tsx               (existe, não muda)
│   ├── KnowledgeIndexDiagnosticsTab.tsx         (novo)
│   └── KnowledgeIndexDiagnosticsTab.test.tsx    (novo)
├── pages/
│   ├── KnowledgeBaseDetailPage.tsx              (modificado: abas + painéis)
│   └── KnowledgeBaseDetailPage.test.tsx         (modificado)
├── types/
│   └── knowledgeIndex.ts                        (novo)
└── utils/
    ├── documentIndexing.ts                      (modificado: a soma por indexedAt)
    ├── documentIndexing.test.ts                 (modificado)
    ├── indexDiagnostics.ts                      (novo)
    └── indexDiagnostics.test.ts                 (novo)
```

**Nada em `libs/`.** `libs/` é compartilhamento entre **apps** do backend; o
frontend não participa dele, e nenhuma linha desta change é candidata.

## Decisions

### D1 — Módulo de API próprio, e não uma função a mais em `knowledgeBasesApi`

`api/knowledgeIndexApi.ts`, com o seu próprio `listKnowledgeIndexDiagnostics()`,
importando `request<T>` de `knowledgeBasesApi` — o mesmo que
`knowledgeDocumentsApi.ts:1` já faz, com o comentário dele dizendo por quê: é o
módulo da **mesma feature**, não um cliente HTTP compartilhado entre features. A feature é `knowledge-bases` por **conceito
de domínio**, não por origem da rota (convenção 7): `/knowledge-index/diagnostics`
não é rota de base, mas o índice de conhecimento é assunto desta feature.

Dois argumentos, um de forma e um **medido**:

- **Forma:** a feature já separa módulo por recurso (`knowledgeBasesApi` +
  `knowledgeDocumentsApi`). `/knowledge-index` é um terceiro recurso.
- **Blast radius, enumerado de graça** (convenção 18, terceira dimensão):
  `grep -rl "vi.mock(.*knowledgeBasesApi" src/` devolve **6** arquivos —
  `app/router.test.tsx` (outra feature), `agents/pages/AgentDetailPage.test.tsx`
  (outra feature) e os quatro testes de página de `knowledge-bases`. Todos mockam
  **parcialmente**, com `importOriginal`, e o comentário de `router.test.tsx:41-50`
  registra o modo de falha: *"toda função deste módulo que alguma rota da árvore
  chame precisa estar aqui — a que faltar escapa para a rede"*, e quem reprova é o
  **teste seguinte**. Foi exatamente o que aconteceu quando
  `listKnowledgeBaseIndexingSummary` nasceu. Módulo próprio deixa os 6 intocados.

**Alternativa considerada:** acrescentar a função a `knowledgeBasesApi.ts`.
Recusada pelo par acima — e a recusa é barata de reverter se um dia houver um
segundo recurso de índice.

### D2 — O tipo do fio sai dos corpos reais, sem campo opcional

```ts
export interface KnowledgeIndexProvenance {
  provider: string;
  model: string;
  dimensions: number;
  fragmentCount: number;
}
```

Os quatro nomes vêm dos corpos capturados, não de leitura do C# — é a forma que a
convenção 12 pede, e foi o `a2A` que ensinou por que ler o **texto** do JSON e
não o tipo de origem. Nenhum opcional: a rota devolve os quatro sempre, ou não
devolve o item.

`fragmentCount` é `long` no servidor e `number` aqui. Registrado porque é o tipo
de coisa que alguém "conserta" para `bigint`: o teto de `Number.MAX_SAFE_INTEGER`
é 9×10¹⁵ e o gatilho de D6 do backend (500 MB de heap ≈ 320 mil fragmentos) está
dez ordens de grandeza abaixo.

### D3 — As abas nascem aqui, no desenho de `frontend-agente-detalhe-abas`

Cumprindo a D3 da 5a-1, que adiou a barra até existirem duas abas de verdade.
Reuso do desenho, não invenção:

- aba ativa em `?tab=`, com `documentos` **canônica sem parâmetro** e
  `diagnostico` com;
- `parseTab(value)` pura, valor desconhecido caindo na primeira aba **sem
  reescrever o endereço** (poluiria o histórico);
- `keepMounted={false}`: só a aba ativa existe no DOM.

O `keepMounted={false}` aqui não é cópia cega — ele é o que sustenta o `enabled`
de D5 e o que impede a aba inativa de manter um `refetchInterval` vivo.

**Conteúdo da aba `Documentos`:** descrição, tabela de documentos e card de
agentes, na ordem que a página já tem. É o que o protótipo faz (`kTabDocs` envolve
os três) e nada no código contradiz.

### D4 — Contador só na aba `Documentos`, e só quando a listagem respondeu

`Documentos` recebe o contador; `Diagnóstico do índice` não recebe nenhum — não há
número que o descreva (o protótipo já o deixa vazio).

O contador aparece **apenas** quando a listagem carregou e a contagem é maior que
zero. Enquanto carrega, com erro, ou com zero documentos, não há badge. Isso é a
mesma semântica de `TabCounter` no detalhe do agente (que devolve `null` em zero)
e a mesma do protótipo (`hasN: n !== '' && n !== '0'`) — mas aqui a razão é mais
forte, e vale escrever: no detalhe do agente a contagem **já está na mão** quando
a barra renderiza; aqui ela vem de uma segunda requisição, e um `0` durante o
carregamento afirmaria uma contagem que ninguém fez (convenção 13, o terceiro caso
— ausência de linha é desconhecido, não zero).

### D5 — A aba busca a proveniência só quando está ativa

`useKnowledgeIndexDiagnosticsQuery({ enabled: activeTab === DIAGNOSTICS_TAB })`,
no molde que a 5b já usa para o catálogo de bases na aba de conhecimento do
agente.

Aqui o motivo é mais caro que lá. A consulta é **`Seq Scan` + `HashAggregate`
sobre o heap inteiro** de `knowledge_fragments` — medido no `design.md` do
backend em **0,24–0,82 s para 150.000 fragmentos**, sobre a VM do podman com
disco virtualizado e `shared_buffers` no default de 128 MB (as **páginas** valem
em qualquer lugar; os milissegundos, só naquele disco). Buscar isso ao abrir o
detalhe cobraria a varredura de quem só quer ver a lista de documentos.

### D6 — Frescor: polling condicional, e a condição é a vacuidade **mais** a
transição

A proveniência muda **exatamente uma vez**: quando o primeiro documento termina
de indexar. Logo:

```
poll  ⟺  o índice está vazio  E  esta base tem documento não terminal
```

A segunda metade é `hasNonTerminalDocument(documents)`, a função pura que a 5a-2
extraiu para o `refetchInterval` da listagem (convenção 20) — **reuso, não
invenção**. A primeira metade é o que impede o pior caso: com o índice já
povoado, repetir a varredura de heap a cada 4 s pagaria 0,8 s de CPU do banco por
um valor que não muda mais.

A condição inteira vira função pura exportada em `utils/indexDiagnostics.ts`,
`shouldPollIndexDiagnostics(diagnostics, documents)`, pelos dois motivos que a
convenção 20 registra: é testável sem timer, e o guarda é **pareado** — uma
asserção determinística que resolve a opção real contra a query real do cache, e
uma comportamental com timers falsos.

**Limite declarado, não escondido:** um documento que indexe em **outra** base não
acorda esta aba, porque a página só conhece os documentos desta. Não é lacuna a
consertar aqui — o `QueryClient` do painel não tem `defaultOptions`, então valem
`staleTime: 0` e `refetchOnMount: true`, e remontar a página refaz a consulta. A
alternativa (polling incondicional) compraria esse caso pagando a varredura para
sempre.

**Armadilha registrada para quem escrever o teste** (convenção 20): o react-query
usa `notifyOnChangeProps: 'tracked'` e devolve um **proxy que registra quais
props foram lidas**. Se a primeira espera tocar só `isSuccess`, `data` nunca entra
no conjunto rastreado e a mudança seguinte não provoca re-render.

### D7 — Dois grupos, duas escalas, e o do sistema dito como do sistema

O protótipo empilha cinco linhas numa lista só — três do sistema (provedor,
modelo, dimensão) e duas da base (documentos indexados, fragmentos) — sob o
cabeçalho `Como o índice foi construído`, que descreve **só as três primeiras**.
Com a proveniência global isso deixa de ser detalhe visual e vira **afirmação
errada**: o cabeçalho passa a dizer que as duas últimas também são do sistema, ou
que as três primeiras também são da base. As duas leituras são falsas.

Dois cards:

| grupo | o que diz | fonte |
|---|---|---|
| **Como o índice foi construído (sistema)** | provedor, modelo, dimensão e fragmentos por combinação | `GET /knowledge-index/diagnostics` |
| **Volume desta base** | documentos com fragmentos no índice (X de Y), fragmentos desta base, documentos em falha | listagem de documentos já carregada |

O rótulo `(sistema)` é a parte que carrega a correção — sem ele, os dois cards
lado a lado continuam insinuando que o de cima é desta base.

### D8 — O gate do vazio é a vacuidade do índice INTEIRO

`diagnostics.length === 0`, nunca `indexados > 0` da base. Com a proveniência
global, o gate por base produz a anomalia direta: base A com travessão e base B,
ao lado, mostrando o modelo — a convenção 13 na direção de **negar o que o
sistema sabe**, que é a menos exercida das duas.

Corolário que o backend já garante por construção: `apps/api` **não tem** a seção
`Embedding` (ela existe em um único `appsettings` do repositório, o de
`apps/workers`). Não há configuração para a tela mostrar, nem por acidente. A
asserção negativa existe assim mesmo, porque ela guarda a construção.

### D9 — No estado vazio, as três linhas do sistema NÃO são renderizadas (C12)

**Décima segunda correção de protótipo**, achada **percorrendo**.

O protótipo, no vazio, mostra o bloco explicativo **e** `Provedor de embedding —`,
`Modelo de embedding —`, `Dimensão do vetor —`. Nesta área do painel `—` tem
significado fixado por decisão registrada da 5a-3 (`utils/indexingSummary.ts`):

```
valor   há linha, há o que dizer
vazio   há linha, não há o que dizer
—       NÃO há linha: a consulta não respondeu, ou o dado não veio
```

Índice vazio é o **segundo** caso, não o terceiro: a rota respondeu `200`, o fato
é conhecido e medido. Renderizar `—` ali empresta a esta tela o vocabulário de
"não sei" para dizer "sei que não existe", e some com a distinção justamente na
tela em que "não sei" vai existir de verdade (D11).

Então: no vazio, o card do sistema contém **só** a explicação — em uma frase que
não diz `nesta base`, e que não nomeia provedor nem modelo nenhum.

### D10 — A lista de falhas **aponta**, não repete (C13)

Décima terceira correção. A decisão que o prompt pede explicitamente, com o
motivo:

A faixa de falha já existe, viva, na tabela de documentos da aba vizinha — com o
**motivo completo, sem truncar** e o botão `Reindexar documento`
(`KnowledgeDocumentsCard.tsx`, entrega da 5a-2). Repeti-la na aba de diagnóstico
cria **duas cópias do mesmo estado em telas vizinhas**, e as duas cópias não são
equivalentes:

- duas superfícies que disparam a **mesma** mutação, com estados de carregamento
  independentes — o `reindexingId` da página vale para uma das duas;
- dois lugares onde a cópia do motivo pode divergir na próxima change;
- e a duplicação é do **próprio protótipo**: as duas abas dele mostram o mesmo
  bloco, o que não é desenho, é falta de uma decisão.

A aba de diagnóstico exibe, no grupo *Volume desta base*, a **contagem** de
documentos em falha e um controle que leva à aba `Documentos`, onde o motivo e a
ação vivem. Quando a contagem é zero, a linha some (não vira "0 falhas" em tom de
alerta — C11).

**Alternativa considerada e recusada:** omitir a falha inteiramente da aba.
Recusada porque é justamente aqui que o operador está tentando explicar um número
de fragmentos menor do que esperava, e falha é a explicação mais provável. Dizer
quantas e onde ver é diferente de repetir o quê.

### D11 — Quatro estados, e "falhou ao ler" não é "índice vazio"

O card do sistema tem quatro renderizações, e a que falta na maioria das telas é
a quarta:

| estado | o que a tela diz |
|---|---|
| carregando | carregando, sem número nenhum |
| `[]` | índice vazio, com a explicação de D9 |
| 1 item | as três linhas mais `fragmentCount` |
| **≥ 2 itens** | corrupção nomeada, uma linha por combinação com a sua contagem |
| **erro** | *não foi possível ler a proveniência* — nunca "índice vazio" |

O quinto caso é o que a convenção 13 chama de terceiro zero: uma requisição que
não respondeu **não é evidência de ausência**. `KnowledgeDocumentsCard` já trata
assim a listagem (*"a listagem não foi lida, então não há como dizer quantos
existem"*), e a aba usa a mesma régua.

**Na corrupção, a tela NÃO elege uma combinação como a certa.** Ela não tem como:
`apps/api` não conhece a configuração declarada, e a rota não diz qual é a
pretendida. Marcar uma como "atual" seria a convenção 13 na direção de afirmar
mais do que se mediu — e é a regressão bem-intencionada mais provável desta tela,
então a asserção que a impede é **negativa** e é exercida contra o defeito.

O que a tela diz na corrupção, e é verdade conferida no código: vetores de
modelos diferentes são **incomparáveis**, a busca continua devolvendo resultados
errados sem erro nenhum, e a checagem de integridade de `apps/workers` recusa o
boot neste estado — sem modo de tolerância e sem bypass
(`01-ARQUITETURA_E_CONVENCOES.md`, convenção 8, quinto caso). A tela **não**
afirma que o processo está no chão agora: ela não mede processo nenhum.

### D12 — A soma mora onde a regra já mora, e o rótulo muda com ela

Duas funções puras novas em `utils/documentIndexing.ts` — o arquivo que já
carrega, em comentário, a causa da regra e o trecho de `KnowledgeIndexingService`
que a produz:

- `indexedFragmentTotal(documents)` — soma `fragmentCount` dos documentos com
  `indexedAt !== null`;
- `documentsWithFragmentsCount(documents)` — quantos documentos satisfazem o
  mesmo predicado.

O predicado é `indexedAt != null`, **qualquer que seja o estado**, exatamente
como a spec viva de `knowledge-document-indexing` já diz. O protótipo soma com
`status === 'indexed'` e subconta o índice real (C8), pelo mesmo mecanismo que a
C1 já tinha corrigido para a célula da tabela.

**E o segundo caminho para o mesmo subcontar fica fechado aqui por construção:**
no mock, `reindex` (`.dc.html:1695`) e a atualização de conteúdo (`:1923`, `:1941`)
gravam `chunks: 0` na hora, enquanto o backend **preserva**. Quem consertar só o
`reduce` não fecha esse buraco — mas no painel real ele não existe, porque a
contagem vem da resposta da API, não de uma escrita local. Registrado para que
ninguém "espelhe" o comportamento do mock numa atualização otimista.

**Consequência de rótulo, e ela é obrigatória:** o rótulo do protótipo é
`Documentos indexados`, e a aba vizinha usa a palavra **Indexado** como badge de
`indexingStatus`. Com o predicado certo, um documento `Falhou` na tabela **conta**
aqui — duas telas usando a mesma palavra com predicados diferentes é a confusão
que a C1 já custou uma vez. O rótulo passa a ser **`Documentos com fragmentos no
índice`**, que é o que a linha mede.

**E o guarda do rótulo é de dois níveis, no formato que a C9 usou na 5a-3.** A
função pura afirma o **predicado**; o componente afirma o **texto renderizado**.
A asserção do rótulo tem de ser sobre o texto no DOM, **nunca** constante contra
constante: duas constantes iguais passam verde quando alguém devolve o rótulo
para `Documentos indexados`, e esse é precisamente o movimento que a regra existe
para impedir — a palavra antiga é familiar e a nova parece burocrática. O modo de
falha real é alguém "melhorar a cópia" sem tocar em `documentIndexing.ts`,
caminho em que o teste da função pura fica **intocado e verde** (convenção 15,
segunda forma: o guarda precisa reprovar no componente que a correção toca).

### D13 — Nenhum componente compartilhado nasce nesta change

A linha rótulo/valor que os dois cards usam já existe como `ConfigRow`, **local**
a `McpServerConfigCard.tsx`. Com esta change passam a ser **duas** ocorrências. O
gatilho da convenção 2 no frontend é repetição **já observada**, e os três
componentes compartilhados do painel saíram de cinco cópias idênticas, quatro
estruturas iguais e três cabeçalhos repetidos — **contados antes de extrair**.
Duas não é o gatilho. A cópia local fica, e o gatilho para extrair é a terceira.

O mesmo vale para `TabCounter`, hoje local ao detalhe do agente: duas
ocorrências, e as duas **não são iguais** (D4), o que é argumento a mais para não
unificar agora.

### D14 — A ordem é a da resposta, e isso é asserção, não omissão

A aba renderiza as combinações na ordem em que vieram. O teste que protege isso
monta a resposta **fora de ordem alfabética** e afirma a ordem do DOM — é a
quinta forma da convenção 15: com um arranjo já ordenado, o guarda passa verde
com o defeito presente, porque ordenar no cliente produziria o mesmo resultado.

## Projeção de tamanho, por componente

Feita **depois** que a verificação fechou (convenção 18), separando **criados** de
**modificados**, só sobre código, e com os modificados **em pares com o teste**.
Âncora decomposta: a 5a-3 (`frontend-knowledge-base-resumo-indexacao`) projetou
11 arquivos e entregou 12; a 5b entregou `knowledgeBaseRows.ts` com ~45 linhas e
o teste dele com ~75.

**Criados — 9 arquivos, 600–810 linhas**

| componente | linhas | âncora |
|---|---|---|
| `types/knowledgeIndex.ts` | 25–40 | `types/knowledgeDocument.ts`, que também carrega o porquê de cada campo |
| `api/knowledgeIndexApi.ts` | 15–25 | uma função, no molde de `listKnowledgeBaseIndexingSummary` |
| `api/knowledgeIndexApi.test.ts` | 45–70 | caminho feliz + o par vazio (convenção 5) + 401 |
| `api/useKnowledgeIndex.ts` | 30–45 | `useKnowledgeDocumentsQuery` tem 12 linhas de código e 25 de comentário |
| `api/useKnowledgeIndex.test.ts` | 70–110 | o guarda **pareado** de polling: determinístico + comportamental com timers falsos |
| `utils/indexDiagnostics.ts` | 35–55 | `agentUsage.ts` tem 14 (um `filter`); `knowledgeBaseRows.ts`, ~45 |
| `utils/indexDiagnostics.test.ts` | 70–110 | par com/sem item em cada função |
| `components/KnowledgeIndexDiagnosticsTab.tsx` | 180–240 | cinco renderizações (D11) + dois cards; `KnowledgeDocumentsCard.tsx` tem 252 |
| `components/KnowledgeIndexDiagnosticsTab.test.tsx` | 130–190 | ~7 cenários a 19–21 linhas, mais três **negativos**, que custam arranjo próprio (25–40) |

**Modificados — 4 arquivos, 130–210 linhas**

| componente | linhas | o que muda |
|---|---|---|
| `pages/KnowledgeBaseDetailPage.tsx` | 45–70 | `Tabs`, `parseTab`, `useSearchParams`, dois painéis, o `enabled` e a condição de polling |
| `pages/KnowledgeBaseDetailPage.test.tsx` | 55–90 | troca de aba, aba desconhecida, o mock do módulo novo; os testes existentes continuam valendo porque `Documentos` é a aba default |
| `utils/documentIndexing.ts` | 15–25 | duas funções sobre a regra que já está escrita ali |
| `utils/documentIndexing.test.ts` | 15–25 | o par com/sem, mais o caso "indexou e falhou" — o que a semente do protótipo não tem |

**Total de código: 13 arquivos, 730–1.020 linhas.** Com os quatro artefatos
OpenSpec e a documentação, algo entre 20 e 22 arquivos.

**Um risco de tamanho que a verificação ELIMINOU, e vale dizer porque era
esperado:** a terceira dimensão de blast radius (todo arquivo que mocke o módulo)
estava projetada em até 6 arquivos a mais. D1 a zerou — `grep -rl` enumerou os 6,
e nenhum deles toca um módulo novo. É o caso que a convenção 18 nomeia de
projeção que erraria **para cima** se feita antes da verificação.

## Guardas exercidos contra o defeito real

Convenção 15: um guarda só vale depois de ter falhado contra o defeito que ele
guarda. Cada inversão abaixo foi aplicada, confirmada **vermelha** e desfeita. A
coluna da direita registra as **duas** metades quando elas existem — sem a metade
que continuou verde, o item vale o mesmo que não ter verificado.

| inversão aplicada | o que reprovou |
|---|---|
| predicado da soma trocado para `status === 'Indexed'` | **4 cenários, nos dois níveis**: 3 na função pura (documento que indexou e falhou; documento em reindexação; a contagem de documentos) e 1 no DOM (`conta o documento que indexou e falhou depois`). Os dois níveis reprovando é o que prova que o guarda está no componente que a correção toca. |
| **rótulo devolvido para `Documentos indexados`**, sem tocar `documentIndexing.ts` | **A metade de DOM reprova** (`o rótulo renderizado não reusa a palavra do badge de estado`) e **a função pura continua VERDE, 26/26**. É a evidência de que a asserção precisava ser sobre texto renderizado: constante contra constante não pegaria isso, e o teste de predicado não vê cópia. |
| combinações reordenadas no cliente com `localeCompare` | `preserva a ordem da resposta, sem reordenar no cliente` — com o arranjo **fora** de ordem alfabética. Com a lista já ordenada o DOM seria idêntico e o guarda passaria verde (convenção 15, quinta forma). |
| três linhas de travessão no estado vazio | 2 cenários: `não representa o vazio com travessão numa linha de proveniência` e `não exibe nome de provedor, de modelo nem dimensão` (o rótulo das linhas volta junto). |
| primeira combinação marcada como `Combinação atual` | `não elege nenhuma combinação como atual, correta ou configurada`. |
| lista de falhas repetida na aba (motivo + botão de reindexar) | 2 cenários: `não repete o motivo da falha nem a ação de reindexar` e `informa a contagem e oferece caminho para a aba de documentos` (o botão único deixa de existir). |
| gate do vazio trocado para a contagem da base | 3 cenários, e o que importa é `exibe a proveniência normalmente numa base sem documento, com índice povoado` — é ele que separa a proveniência global da por base. |
| caminho de erro colapsado no texto de índice vazio | 2 cenários, o positivo (`informa a indisponibilidade`) e o negativo (`não vira índice vazio`). |
| `enabled` removido da página | `não busca a proveniência com a aba de documentos ativa` — **na suíte da página**. O teste do hook continuou verde, e corretamente: ele passa `enabled: false` direto, então mede o hook, não a fiação. As duas camadas medem coisas diferentes. |
| primeira metade da condição de polling removida | **4 cenários**: 2 na função pura (`não acompanha com índice povoado, mesmo com documento não terminal`; o caso indefinido) e 2 no hook, incluindo o determinístico `não agenda recarga com o índice já povoado, mesmo indexando`. |

**Um guarda foi corrigido por ter reprovado pelo motivo errado**, e o registro
vale mais que o acerto: a primeira versão da asserção de travessão varria o
`textContent` inteiro do bloco vazio, e reprovou contra o **travessão de prosa**
da própria cópia (`está vazio — nenhum fragmento gravado`). Um guarda que reprova
por pontuação dá a impressão de estar funcionando e não mede o defeito. A versão
final mira o travessão como **valor de linha** — texto exato mais a ausência de
qualquer linha de proveniência —, e a cópia passou a usar dois-pontos.

## Conferência manual (convenção 14) — o que cada rodada achou

Painel real contra um stub de `apps/api` com estado **estático**, a 1860×1200, nos
dois esquemas de cor, oito cenários nomeados: índice vazio sem documento; índice
vazio com documento indexando; uma combinação; **duas combinações**; base com
documento em falha; base com documento que **indexou e falhou depois**; erro da
proveniência; erro da listagem de documentos.

**Rodada 1 — achado DIMENSIONAL, e só medindo.** A barra de abas sai com
**34px** enquanto a listagem de documentos não respondeu e **40px** depois que o
contador aparece: ela **cresce 6px sob o conteúdo já renderizado**, no
carregamento normal da tela. "Está parecido" não distingue isso. A correção tem
precedente na própria base — a faixa de cabeçalho do `SectionedCard` tem altura
fixa em vez de padding, pelo mesmo motivo (*"a faixa que carrega um botão ficava
mais alta que a que carrega só o rótulo"*). `h={40}` nas duas abas; **medido de
novo: 40px nos dois estados.**

**Rodada 2 — achado de leitura, no cenário que a tela existe para mostrar.** Com
duas combinações, as oito linhas corriam juntas: dois `Provedor de embedding`
seguidos, sem separação. A tela lia como uma lista com rótulos repetidos em vez
de **duas combinações**, justamente onde o operador precisa decidir qual parte
reindexar. Corrigido com divisor e um rótulo **posicional** (`Combinação 1 de 2`)
— posicional e nada mais: não diz atual, correta nem configurada, e a asserção
negativa que guarda isso continua verde.

**Rodada 3 — nada novo.** As duas correções conferidas nos dois esquemas; os
demais cenários passaram sem achado.

**Rodada 4 — um achado que a medição DESMENTIU, e ele vale escrito.** O cenário
de erro da proveniência aparecia com o spinner *"Lendo a proveniência do
índice..."* em vez do alerta de indisponibilidade. Lido como defeito, seria uma
correção errada — o caminho de erro está certo e testado. Medido: em **t=2,5 s**
a tela mostra o spinner, em **t=14,5 s** mostra o erro. É a janela de **retry
padrão do `QueryClient`**, o mesmo item que a 5a-3 já nomeou para a validação do
operador. A captura da rodada 3 tinha sido feita 2,5 s depois de navegar — o
instrumento é que estava medindo cedo demais, não a tela errada.

**Rodada 5 — vazia.** Nada novo nos oito cenários, nos dois esquemas.

**A aba `Documentos` depois de envolvida em `Tabs`** foi percorrida nos dois
esquemas: tabela, faixa de falha e card de agentes mantiveram medida e
espaçamento, e o painel continua de largura cheia (a aba de diagnóstico é que tem
`maw` de 860px, como o protótipo especifica).

### O que a conferência NÃO cobriu — nomeado para a validação do operador

- **A janela de ~12 s entre a consulta falhar e o alerta aparecer** (retry padrão
  do `QueryClient`). A medição diz que a tela não mente nesse intervalo — ela diz
  que está lendo, e está. Se ela *parece* travada é julgamento que a automação não
  faz.
- **A largura real do monitor.** A conferência mediu a 1860px.
- **O nome de modelo mais longo que existir.** Medido com
  `qwen-qwen3-embedding-8b` (166px de valor, com folga até a borda do card em
  1083px). Um nome substancialmente maior encostaria no rótulo.
- **A transição do polling com indexação real.** O stub alterna o cenário, mas
  quem exercita a condição de ponta a ponta é `apps/workers` indexando de fato.

## Risks / Trade-offs

Cada risco com contraparte verificável (convenção 10): cenário e teste, ou a
justificativa explícita de por que não é testável.

| risco | contraparte |
|---|---|
| A tela afirmar que provedor/modelo são **desta base** | Cenário com duas bases e a mesma proveniência exibida, **mais** a asserção negativa de que nenhuma requisição da aba leva id de base na URL. A negativa é a que importa: impede a regressão bem-intencionada de "filtrar por base, já que a aba é por base". |
| Índice vazio renderizar algo que pareça configuração | Cenário "resposta `[]` não exibe nome de provedor nem de modelo", com asserção negativa sobre o texto do DOM. Garantido por construção (`apps/api` não tem a seção `Embedding`) — o teste guarda a construção. |
| **Corrupção ficar invisível** | Cenário "dois itens são exibidos, cada um com a sua contagem". É o cenário que mede a decisão: com um só item renderizado, ele reprova. |
| A tela eleger uma combinação como "a atual" | Asserção **negativa**: com dois itens, nenhum texto marca um deles como atual, correto ou configurado. Exercida contra o defeito (marcar o primeiro) antes de valer. |
| A soma de fragmentos subcontar (C8) | Cenário com documento `Failed` **e** `indexedAt` não nulo, que a semente do protótipo não consegue produzir. O guarda reprova contra o predicado `status === 'Indexed'`. |
| O rótulo voltar para `Documentos indexados` (D12) | Asserção sobre o **texto renderizado** da linha, negando a palavra do badge — nunca constante contra constante. Exercida devolvendo o rótulo antigo, e o registro diz as **duas** metades: a asserção de DOM reprova e o teste da função pura **continua verde**. |
| Falha de leitura virar "índice vazio" | Cenário de erro da consulta afirmando o texto de indisponibilidade **e** negando o texto de vazio. São dois caminhos que renderizam pouco, e é fácil colapsá-los. |
| O `—` voltar para o estado vazio (D9) | Asserção negativa de que o card do sistema, com `[]`, não contém travessão. |
| A lista de falhas ser reintroduzida na aba (D10) | Asserção negativa de que a aba de diagnóstico não contém botão de reindexar nem o texto do `failureReason`. |
| Reordenar as combinações no cliente | Cenário com a resposta **fora de ordem alfabética**, afirmando a ordem do DOM. Sem o arranjo desordenado o guarda passa verde com o defeito (convenção 15, quinta forma). |
| O polling pagar a varredura de heap para sempre | Guarda pareado: asserção determinística de que `refetchInterval` resolve para `false` com o índice já povoado, e comportamental com timers falsos para o caso vazio + não terminal. **O tempo da rota não é assertável aqui** — é outro processo, e um limite em ms viraria teste intermitente. Está escrito em vez de fingido. |
| A aba buscar a proveniência estando inativa | Asserção de que a função de listagem não foi chamada com a aba `Documentos` ativa. É barata e pega a remoção do `enabled`. |
| Quebrar a tela de documentos ao envolvê-la em `Tabs` | Os testes existentes de `KnowledgeBaseDetailPage` continuam valendo **sem alteração de asserção**, porque `Documentos` é a aba default — se algum precisar mudar de asserção, isso é sinal de que o painel default não é o que era. |
| Guarda que nunca falhou contra o defeito real (convenção 15) | Cada asserção negativa acima é exercida contra o defeito **antes** de valer, e o registro diz as **duas** metades: qual cenário reprovou e qual continuou verde. Tarefa própria em `tasks.md`. |

**Trade-off assumido:** a aba não acorda quando um documento de **outra** base
termina de indexar (D6). Comprado de propósito: o caso se resolve remontando a
página, e a alternativa cobraria uma varredura de heap a cada 4 s para sempre.

## Medição contra a projeção (convenção 18)

**Contagem de arquivo: 13 projetados, 13 entregues — exata**, decomposta em 9
criados (9 projetados) e 4 modificados (4 projetados). É o terceiro acerto
seguido do método de contar criados e modificados **separadamente, com o blast
radius lido no código antes de projetar**, depois de `knowledge-base-vinculo-agente`
(25×25) e da metade de criados de `frontend-knowledge-base-catalogo` (21×21).

**Linhas: erraram para BAIXO, e por uma causa só.** 1.197 criadas contra 600–810
projetadas (+48% sobre o topo) e 400 acrescentadas nos modificados contra 130–210
(+90%). Sete dos nove criados ficaram acima da faixa.

| componente | projetado | entregue |
|---|---|---|
| `types/knowledgeIndex.ts` | 25–40 | **34** |
| `api/knowledgeIndexApi.ts` | 15–25 | **21** |
| `api/knowledgeIndexApi.test.ts` | 45–70 | 108 |
| `api/useKnowledgeIndex.ts` | 30–45 | 59 |
| `api/useKnowledgeIndex.test.ts` | 70–110 | 225 |
| `utils/indexDiagnostics.ts` | 35–55 | **50** |
| `utils/indexDiagnostics.test.ts` | 70–110 | 118 |
| `components/…DiagnosticsTab.tsx` | 180–240 | 278 |
| `components/…DiagnosticsTab.test.tsx` | 130–190 | 304 |

**Separando as duas causas, como a convenção exige — medição de método só compara
o escopo que estava projetado:**

**1. Escopo acrescentado depois da projeção** (não é erro de projeção):

- as **duas correções da conferência manual** — altura fixa da barra de abas e a
  separação das combinações com divisor e rótulo posicional;
- o **guarda do rótulo em nível de componente** (5.8a), acrescentado ao
  `tasks.md` depois de o `design.md` estar escrito;
- `isIndexEmpty` e `isIndexCorrupted`, que a projeção não previa — ela contava só
  `shouldPollIndexDiagnostics`.

**2. Erro de projeção, com causa nomeável: a unidade contada estava errada.** A
projeção usou *"cenário da spec"* como unidade, a 19–21 linhas cada. O que a
suíte entrega é **uma asserção por modo de falha**, e as asserções **negativas se
multiplicam**: a spec tem 31 cenários, e os quatro arquivos de teste entregaram
**51 `it()`**. O componente sozinho projetava ~7 cenários e entregou 24 — não
porque cada um ficou caro (12,6 linhas por `it()`, **abaixo** da âncora), mas
porque são muito mais.

**A régua que sai daqui, e é reusável:** numa tela cujo valor está no que ela
**se recusa a afirmar**, projetar por cenário de spec subestima por construção.
Cada asserção negativa vira um `it()` próprio — é o que a convenção 15 exige, já
que um teste que reprova por dois motivos não diz qual guarda pegou. **Contar
asserções negativas como cenários**, não como cláusulas de um cenário.

**E uma segunda causa, menor e também nomeável: a âncora de densidade de
comentário veio de um arquivo de outra natureza.** `knowledgeBaseRows.ts` (~45
linhas) é uma função de cruzamento; os arquivos desta change carregam a **causa**
de cada decisão em comentário, no estilo de `utils/documentIndexing.ts` e
`utils/indexingSummary.ts`, que têm 40% de comentário. Âncora de linhas precisa
vir de arquivo do **mesmo tipo**, não só do mesmo tamanho.

**Blast radius da terceira dimensão: projetado ZERO, entregue ZERO.** É a
primeira vez nesta base que essa dimensão foi projetada **antes** em vez de
descoberta depois. `grep -rl "vi.mock(.*knowledgeBasesApi"` devolve os mesmos 6
arquivos de antes, e o `git diff` deles está vazio; só
`KnowledgeBaseDetailPage.test.tsx`, que já ia mudar, mocka o módulo novo.

**OpenSpec:** 4 artefatos, 1.494 linhas. Headline do commit não serve de nada
nesta base, e por isso não está aqui.

## Migration Plan

Não há migração. A change é aditiva no cliente: a rota já está no ar desde
13/09/2026, o detalhe da base ganha uma barra de abas cuja primeira aba é o que a
tela já era, e endereços antigos (`/knowledge-bases/{id}`, sem `?tab=`) continuam
abrindo exatamente a mesma tela. Rollback é reverter o commit.

## Open Questions

Nenhuma incerteza de negócio ou produto em aberto. As três que existiam foram
fechadas na verificação e estão em D9 (o que o vazio renderiza), D10 (repetir,
apontar ou omitir a lista de falhas) e D12 (o rótulo da linha de documentos).
