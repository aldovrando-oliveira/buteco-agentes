## Context

`InventoryPage.tsx` (`apps/frontend/src/features/inventory/pages/`) é a tela de
entrada do painel desde `frontend-inventario-catalogos`. A rota é `/inventory`, e
a raiz redireciona para ela. A tela tem quatro itens de catálogo, escritos à mão
um a um, cada um com o próprio estado de carregamento e de erro.

O D10 do `design.md` arquivado daquela change deixou de fora "qualquer quadro de
atividade", por dois motivos escritos:

> **Sessões por período.** Nenhuma rota filtra por intervalo.
>
> **Mensagens processadas.** Nenhuma rota conta. E o termo tem três referentes
> nesta base [...] **Definir qual é trabalho de backend, não da tela.**

Os dois motivos foram resolvidos depois, no backend:

| motivo do D10 | resolvido por | contrato vivo |
|---|---|---|
| nenhuma rota filtra por intervalo | `inbox-sessoes-por-periodo` (PR #23) | `GET /sessions/summary` → `startedCount` |
| qual "mensagens" contar | `inbox-mensagens-recebidas-periodo` (PR #24) | `GET /messages/summary` → `inboundCount` (entrada, mensagem distinta, por `OccurredAt`) |

**Esta change fecha aquele D10; não o contradiz.** As duas rotas exigem `from` e
`to` e recusam período implícito ("SHALL NOT assumir [...] um período relativo ao
instante da requisição"). Por isso a janela é responsabilidade do cliente, e é
daí que vem a D3.

**Precedente de spec, corrigido.** Esta não é a primeira vez que uma change nova
edita a spec de uma change de UI anterior: `knowledge-base-catalog-ui` recebeu
`MODIFIED` de `2026-09-13-frontend-knowledge-base-form-orientacao` e de
`2026-09-13-frontend-knowledge-base-resumo-indexacao`. É esse precedente que a D1
segue para o `Purpose`.

**O protótipo final** está em `design/Inventario Buteco Agentes.dc.html`, com seis
itens, vindo do projeto Claude Design "Sistema Gestão de Agentes". É uma cópia
**conferida por tamanho**: 17706 bytes, igual ao do projeto. O `support.js` é o
runtime do Claude Design, byte-idêntico às cópias já arquivadas
(`951ae391…`). As 16 combinações de estado × esquema do card de atividade foram
capturadas e aprovadas (`capturas/r7/`). **A grade do protótipo não é a do app**:
ver D6 e D8.

## Goals / Non-Goals

**Goals:**
- A tela de entrada responder também "o sistema está sendo usado?", com duas
  contagens de atividade e a janela declarada.
- Proveniência, falha isolada e nova tentativa por item idênticas às dos quatro
  itens de catálogo, sem regra nova para os itens de atividade.
- Uma apuração só para cada contagem, generalizando a regra que o inventário já
  tinha.
- A grade não degradar na largura de trabalho real (~1860px).

**Non-Goals:**
- **Seletor de período.** A janela é fixa. Gatilho para reabrir: *o operador
  precisar comparar janelas diferentes*. Não há prazo.
- **Métrica derivada entre os dois itens**, como mensagens por sessão. A spec
  viva `inbox-message-period-summary` diz que os dois números "SHALL NOT ser
  tratados como implicando um ao outro", e itens lado a lado convidam a fazer a
  conta.
- **Tendência ou comparação com o período anterior.**
- **`TimeProvider` novo em `apps/inbox`.** Decisão herdada das changes de
  backend, não desta.
- **Qualquer rota de backend.** As duas já existem e têm spec viva.
- **Container de largura máxima** (o `max-width:1584px` do protótipo). Continua
  recusado pelo D7 anterior; ver D8.

## Decisions

### D1 — Uma capability só, com o `Purpose` reescrito no sync

Os itens de atividade entram em `catalog-inventory-ui`.

**Alternativa recusada: `activity-summary-ui` para os dois itens novos.** O
requisito "Falha e nova tentativa independentes" precisa falar dos seis itens ao
mesmo tempo ("o estado de uma consulta SHALL NOT determinar o que é apresentado
para os outros itens"). Com duas capabilities, a independência entre um item de
catálogo e um de atividade não teria spec onde morar — ou seria escrita duas
vezes, cada uma apontando para a outra.

**O que isso custa, escrito.** A proposta de `frontend-inventario-catalogos`
escolheu `catalog-*` e não `dashboard-*` porque "'Dashboard' promete atividade",
e reservou o nome: *"o nome `dashboard-*` fica livre para o dia em que a
atividade existir, que é outra capability com outras dependências"*. Esse dia
chegou, e esta change **não** usa a reserva: o argumento de independência acima
pesa mais que a precisão do nome. Resultado: `catalog-inventory-ui` passa a cobrir
catálogo **e** atividade, e o nome fica mais estreito que o conteúdo. O `Purpose`
reescrito é o que carrega o sentido real.

**Nenhuma capability foi renomeada neste projeto.** Conferido: toda pasta de
`archive/*/specs/` existe em `openspec/specs/`, e os `RENAMED` do arquivo são de
requisito. Renomear exigiria remover todos os requisitos e recriá-los noutra
pasta, sem precedente. Pela mesma razão, o cabeçalho do requisito "Inventário dos
catálogos como entrada do painel" fica como está: o `MODIFIED` casa pelo nome, e
renomeá-lo juntaria um `RENAMED` a um `MODIFIED` no mesmo requisito, sem ganho de
contrato.

**O `Purpose` não vai na delta.** O sync não o lê de lá. Ele é reescrito **no
arquivo vivo, na passada do sync**, como fizeram
`…-form-orientacao` (tasks 11.2) e `…-resumo-indexacao` (tasks 11.2). A armadilha
registrada nesse precedente: trocar só uma palavra deixa a outra metade da frase
falsa. O `Purpose` atual afirma **duas** coisas que deixam de ser verdade — "um
item por catálogo" e "o atalho para a listagem correspondente". Texto proposto:

> A tela de entrada do painel: um item por catálogo que o painel consome e um item
> por contagem de atividade num período, cada um com a sua contagem e o estado da
> consulta que a produziu. O item de catálogo leva à listagem correspondente; o
> item de atividade declara o período a que a contagem se refere e não leva a
> lugar nenhum, porque nenhuma listagem conta o mesmo conjunto. Define a
> proveniência de cada contagem — os quatro estados que não podem ser colapsados
> um no outro —, a apuração única de cada contagem, o comportamento independente
> de falha e de nova tentativa por item, e a recusa de reproduzir a explicação que
> cada listagem já dá.

### D2 — Quatro `MODIFIED` e um `ADDED`, não dois `MODIFIED`

A exploração previa dois `MODIFIED`: a regra de derivação e a de atalho. Lida a
spec viva inteira, **as duas moram no mesmo requisito**, e **outros três** são
escritos "por catálogo" ou exigem atalho em todo item:

| requisito vivo | por que muda | forma |
|---|---|---|
| Inventário dos catálogos como entrada do painel | derivação "da mesma consulta da listagem" + atalho obrigatório | MODIFIED |
| Proveniência da contagem de cada catálogo | "para cada catálogo" | MODIFIED (catálogo → item) |
| Falha e nova tentativa independentes por catálogo | "para os outros catálogos" | MODIFIED (catálogo → item) |
| O inventário não reproduz a explicação das listagens | "Cada item SHALL apresentar [...] o atalho" | MODIFIED |
| — | a janela | ADDED |

Deixar os dois do meio como estão faria o item de atividade não ser governado pela
proveniência nem pela independência, que são exatamente o que ele herda. Os
cabeçalhos ficam com "catálogo" pela mesma razão da D1: o `MODIFIED` casa pelo
nome.

**Os cenários vivos continuam válidos sob o texto reescrito.** Conferidos um a
um: os cinco do primeiro requisito falam de catálogo e descrevem casos que o texto
novo ainda exige. O atalho existe para o catálogo porque a listagem dele conta
exatamente o mesmo conjunto (`array.length` da mesma consulta, D6 anterior). Os
cenários de proveniência e independência só trocam "catálogo" por "item". O
cenário de unidade no singular passa a valer para qualquer item. **Nenhum** cenário
existente precisou ser negado; o texto novo é generalização.

**A regra de apuração única, generalizada.** O texto vivo protege "nunca duas
apurações do mesmo fato", e a forma concreta dele é "a mesma consulta da
listagem". O item de atividade não tem listagem, então a forma concreta dele é
outra: a contagem é a que a rota agregada devolve, e a tela SHALL NOT recontar
nem derivar. O modo de falha que a regra pega é específico: `useChannelSessionsQuery`
já existe, e somar as sessões dos canais parece uma alternativa barata. Mas conta
**outro** conjunto: sessões recortadas por canal, sem período, e só dos canais
que a tela percorresse.

**A regra do atalho, por critério.** O texto é *"atalho quando existe uma
listagem cujo conjunto de registros é exatamente o que a contagem conta"*. Os dois
itens de atividade caem do lado do "não":

| item | listagem mais próxima | por que não é o mesmo conjunto |
|---|---|---|
| Sessões iniciadas | `/channels/:id` (sessões do canal) | recorte por canal, sem período |
| Mensagens recebidas | `/channels/:id/sessions/:sessionId` | recorte por sessão, as duas direções, sem período |

Uma exceção nomeada ("exceto sessões e mensagens") envelheceria no primeiro item
novo. O critério decide sozinho.

### D3 — A janela: função pura, rolante, "agora" injetado, fora da chave de cache

**Onde:** `features/inventory/utils/activityWindow.ts`, no molde de
`knowledge-bases/utils/indexingSummary.ts` e `documentIndexing.ts`: regra pura,
sem timer, sem efeito colateral.

```ts
export function lastSevenDaysWindow(now: Date): { from: string; to: string }
// to   = now.toISOString()
// from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString()
```

**Rolante, não de calendário.** A alternativa — desde a meia-noite local de 6
dias atrás — exige decidir de qual fuso é a meia-noite (do navegador? do
operador?) e muda de tamanho com o horário de verão. Rolante independe de fuso e é
aritmética de milissegundos. O rótulo "últimos 7 dias" descreve os dois; o que se
consulta é o rolante, e a spec o fixa ("7 × 24 horas").

**UTC sem conversão manual.** `toISOString()` sempre emite UTC com `Z`. As duas
specs de backend aceitam valor com ou sem offset e normalizam para UTC, então o
formato nunca esteve em risco. Com `toISOString()`, a pergunta nem se coloca.

**"Agora" é parâmetro.** A função não chama `new Date()`: o teste passa um
instante fixo e confere os dois limites, sem fake timers e sem depender do relógio
real.

**A chave de cache é fixa, e a janela é calculada dentro do `queryFn`.** Este é o
achado que mais importa para a implementação:

```ts
// ERRADO — janela calculada no render entra na chave:
const w = lastSevenDaysWindow(new Date());
useQuery({ queryKey: ['sessions', 'summary', w.from, w.to], ... })
//   → cada render gera uma chave nova → consulta nova a cada render →
//     o item fica preso em "Consultando…"

// CERTO:
useQuery({
  queryKey: ['sessions', 'summary', 'last-7-days'],
  queryFn: () => { const w = lastSevenDaysWindow(new Date()); return getSessionsSummary(w.from, w.to); },
})
```

Com a chave fixa, a nova tentativa, o refetch por foco e o remount calculam a
janela **no instante da consulta**, que é o que o último cenário do `ADDED` exige.
As chaves são `['sessions', 'summary', 'last-7-days']` e
`['messages', 'summary', 'last-7-days']`. A primeira compartilha o prefixo
`['sessions', …]` com as chaves de `useSessions.ts`; hoje não há nenhum
`invalidateQueries(['sessions'])` no código, e se surgir, refazer o resumo junto é
inofensivo.

`new Date()` dentro do `queryFn` é o único ponto impuro, e fica no hook. O teste
do hook confere os limites enviados com `vi.setSystemTime`, que muda o relógio
sem instalar timers falsos.

### D4 — Os dois clientes em `sessionsApi.ts`

`getSessionsSummary(from, to)` e `getMessagesSummary(from, to)` entram em
`features/sessions/api/sessionsApi.ts`, e os hooks em `useSessions.ts`.

**Alternativa recusada: `messagesApi.ts` novo.** Traria a **sétima** cópia de
`request<T>`/`ApiError` (hoje há seis: `auth`, `agents`, `knowledge-bases`,
`sessions`, `mcp-servers`, `channels`) para uma função só, e uma pasta
`features/messages/` sem nenhuma tela.

**Por que uma rota `/messages/...` mora em `sessionsApi.ts`.** O módulo já é, na
prática, *o cliente de conversas de `apps/inbox`*, e não *o cliente do recurso
`/sessions`*: ele já hospeda `getSessionMessages` (`/sessions/{id}/messages`) e
`listChannelSessions` (`/channels/{id}/sessions`). O arquivo ganha um comentário
dizendo isso, para o próximo leitor não "corrigir" movendo a função. **Gatilho
para separar:** surgir uma tela própria de mensagens.

A duplicação de `request<T>` por módulo segue o padrão estabelecido. Esta change
não introduz cliente HTTP compartilhado.

Tipos de resposta: `{ startedCount: number }` e `{ inboundCount: number }`, com os
nomes de campo do contrato. A tela lê `startedCount` e `inboundCount`, e nunca um
`count` genérico: é o nome do campo que carrega a semântica que o backend fixou.

### D5 — Os dois itens de atividade: composição e contrato

Escritos **inline**, depois dos quatro de catálogo, na mesma forma deles
(convenção 2: extrair só repetição observada). Com seis itens a repetição fica
mais visível. **A extração continua fora desta change**, porque os seis não são
iguais: dois não têm atalho, dois têm janela, e um fala com outro processo. O
comentário do topo de `InventoryPage.tsx` ("OS QUATRO ITENS ESTÃO ESCRITOS À MÃO")
é atualizado para seis, com essa razão.

| | Sessões iniciadas | Mensagens recebidas |
|---|---|---|
| ícone (`lucide-react`) | `CirclePlay` | `MessageSquare` |
| `data-testid` | `inventario-sessions` | `inventario-messages` |
| valor | `42` + `sessões` / `1` + `sessão` | `128` + `mensagens` / `1` + `mensagem` |
| zero medido | **Nenhuma sessão iniciada** | **Nenhuma mensagem recebida** |
| não sei | `—` + "Não foi possível consultar as sessões iniciadas." + Tentar novamente | `—` + "Não foi possível consultar as mensagens recebidas." + Tentar novamente |
| carregando | Skeleton + "Consultando…" | idem |
| janela | "últimos 7 dias", **nos quatro estados** | idem |
| atalho | nenhum | nenhum |

Os dois ícones foram conferidos no `lucide-react` instalado: `CirclePlay` (o
`play-circle` do protótipo) e `MessageSquare` existem. `MessageSquare` (Mensagens)
e `MessagesSquare` (Canais) são parecidos; o rótulo ao lado desfaz a ambiguidade,
e foi a escolha do protótipo aprovado.

**Rótulos.** "Sessões iniciadas" espelha `startedCount`: diz *iniciadas* e impede
a leitura "sessões ativas". "Mensagens recebidas" é o termo que o contrato de
backend recusou para o **campo**, por ser ambíguo quanto ao ponto de vista, e
reservou justamente para a **tela**, onde o ponto de vista é o do operador.

**Composição interna.** O protótipo aprovado define:

```
┌────────────────────────────┐
│                            │ ← bloco centralizado verticalmente,
│  ◷ SESSÕES INICIADAS       │   sem rodapé de link
│  42 sessões                │ ← substantivo INLINE com o número,
│  últimos 7 dias            │   como "4 agentes"
│                            │ ← janela em linha própria, abaixo
└────────────────────────────┘
```

No app: o `Paper` do item de atividade leva `justifyContent: 'center'` para
centralizar rótulo, contagem e janela juntos. Isso corrige o espaço morto da
primeira versão do protótipo, em que o bloco ficava preso no topo de um card
esticado pela linha. A janela é `<Text size="xs" fw={500} c="dimmed">`, porque
`xs` é 12px, que é o tamanho do protótipo, e `dimmed` é o tom lido pelo tema a
5,21:1 e 6,40:1 (D8 anterior). Ela é renderizada **fora** do ternário de estado,
e é isso que garante a presença nos quatro estados por construção, não por
repetição em quatro ramos.

**A razão da falha segue o app, não o protótipo.** O protótipo diz "A requisição
falhou." em âmbar (`--wa`); os quatro itens do app dizem "Não foi possível
consultar os X." em `dimmed`. Os dois novos seguem os quatro existentes: a regra
é herdar sem redecidir.

### D6 — Grade com teto de quatro colunas

```css
grid-template-columns:
  repeat(auto-fit, minmax(max(min(256px, 100%),
    calc((100% - 3 * var(--mantine-spacing-md)) / 4)), 1fr));
```

Sem o teto, com seis itens, o `auto-fit` põe 5 + 1 na faixa de 1600–1871px de
janela, que inclui a largura de trabalho real (~1860px). A primeira linha
misturaria os quatro catálogos com um item de atividade, e o outro ficaria sozinho
na segunda linha. O `max(…, (100% − 3·gap)/4)` impede que caibam mais de quatro
faixas.

**Medido no motor de layout** (Chrome headless, cromo do app = 224 + 2 × 16 =
256px):

| janela | sem teto, 6 itens | **com teto, 6 itens** |
|---|---|---|
| 1872px | 6 × 256px | **4 × 392px (4 + 2)** |
| 1860px | 5 × 308px (**5 + 1**) | **4 × 389px (4 + 2)** |
| 1600px | 5 × 256px (5 + 1) | **4 × 324px (4 + 2)** |
| 1599px | 4 × 323,75px (4 + 2) | 4 × 323,75px (4 + 2) |
| 1328px | 4 × 256px | 4 × 256px |
| 1327px | 3 × 346px (3 + 3) | 3 × 346px (3 + 3) |
| 1056px | 3 × 256px | 3 × 256px |
| 1055px | 2 × 391,5px (2 + 2 + 2) | 2 × 391,5px |
| 784px | 2 × 256px | 2 × 256px |
| 783px | 1 × 527px | 1 × 527px |

**Pontos de reflow do app, com o teto:** 4 colunas ≥ **1328px**, 3 ≥ **1056px**,
2 ≥ **784px**, 1 abaixo. São os três números da change anterior, e isso não é
herança: os pontos dependem só do mínimo de 256px, do gap e do cromo, não da
quantidade de itens. O que a quantidade muda é **acima** de 1328px: sem o teto,
surgiriam 1600px (5 colunas) e 1872px (6 colunas), que o `auto-fit` escondia com
quatro itens. O teto os elimina.

**A mudança é funcional e atinge os quatro itens de catálogo.** A regra da grade
é uma só, e os quatro passam a ser dispostos por ela. Com quatro itens sozinhos a
geometria não mudaria (o `auto-fit` já colapsava as faixas vazias). Com seis, o
teto é o que decide onde os quatro de catálogo ficam. A 1860px os seis têm
**389px**, que é a largura já conferida na 5.2 da change anterior. O que é novo a
389px é o **conteúdo** do item de atividade (janela, sem rodapé), não a largura.

### D7 — Testes

Cada peça com o seu teste, no mesmo módulo, em `apps/frontend` (vitest + Testing
Library):

- `activityWindow.test.ts`: limites exatos a partir de um `now` fixo; `to`
  igual a `now`; `from` exatamente 604 800 000 ms antes; os dois limites em UTC
  (terminam em `Z`); e uma janela que atravessa a mudança de horário de verão
  continuando com 168h.
- `sessionsApi` / `useSessions`: URL e query string dos dois clientes
  (`from`/`to` codificados); a chave fixa; e, com `vi.setSystemTime`, que um
  segundo `refetch` envia uma janela **posterior** à primeira (último cenário do
  `ADDED`).
- `InventoryPage.test.tsx`: os quatro estados dos dois itens novos; a janela nos
  quatro estados; ausência de atalho; os dois itens em estados diferentes
  simultaneamente; a nova tentativa refazendo só a consulta daquele item;
  contagem igual à da rota **sem** chamada a `listChannelSessions`; a negativa
  estrutural de prosa. Os onze testes existentes continuam, com
  `getSessionsSummary`/`getMessagesSummary` mockados, porque sem o mock eles
  escapariam para a rede.
- `router.test.tsx`: mock parcial de `sessionsApi` (ver Riscos).

A suíte não enxerga grade nem contraste (convenção 14). A conferência visual é
tarefa própria.

### D8 — Divergências do protótipo (convenção 17)

| | protótipo | app | por quê |
|---|---|---|---|
| mínimo da coluna | 248px | **256px** | o app já usa 256px e os pontos de reflow foram conferidos com ele |
| container | `max-width:1584px` | **nenhum** | recusado no D7 anterior; nenhuma página tem container |
| teto de colunas | nenhum (6 numa linha a 1860px, 250,7px cada) | **4** (4 + 2, 389px) | D6 |
| razão da falha | "A requisição falhou." em `--wa` | "Não foi possível consultar …" em `dimmed` | herdar dos quatro itens do app |
| "Consultando…" | `--mut2` (3,21:1, reprova AA) | `dimmed` | sétima divergência, já registrada |

**Consequência para a conferência visual:** o protótipo é o alvo do **interior**
do item (hierarquia, estados, janela, centralização), e **não** da grade. As
capturas aprovadas a 1860px mostram seis itens de ~250px numa linha. O app a
1860px mostra 4 + 2 com 389px. O item de atividade a 389px **não foi visto em
nenhuma rodada de protótipo** e é o ponto a olhar primeiro.

### Árvore de arquivos

```
apps/frontend/src/
├── app/
│   └── router.test.tsx                     (M) mock parcial de sessionsApi com
│                                               os dois resumos; beforeEach de
│                                               describe('AppRouter') com os seis
├── features/
│   ├── inventory/
│   │   ├── pages/
│   │   │   ├── InventoryPage.tsx           (M) dois itens de atividade; grade
│   │   │   │                                   com teto de 4 colunas; comentário
│   │   │   │                                   do topo de quatro para seis
│   │   │   └── InventoryPage.test.tsx      (M) estados, janela, sem atalho,
│   │   │                                       independência, apuração única
│   │   └── utils/
│   │       ├── activityWindow.ts           (A) lastSevenDaysWindow(now)
│   │       └── activityWindow.test.ts      (A)
│   └── sessions/
│       ├── api/
│       │   ├── sessionsApi.ts              (M) getSessionsSummary,
│       │   │                                   getMessagesSummary, comentário D4
│       │   ├── sessionsApi.test.ts         (A) URL e query string dos dois
│       │   ├── useSessions.ts              (M) useSessionsSummaryQuery,
│       │   │                                   useMessagesSummaryQuery
│       │   └── useSessions.test.ts         (M) chave fixa, janela no queryFn
│       └── types/
│           └── session.ts                  (M) SessionsSummary, MessagesSummary
└── (nada em components/, nada em libs/)

openspec/changes/frontend-inventario-atividade-periodo/
├── proposal.md · design.md · tasks.md
├── specs/catalog-inventory-ui/spec.md      (A) 4 MODIFIED + 1 ADDED
└── design/
    ├── Inventario Buteco Agentes.dc.html   (A) protótipo final, 17706 bytes
    └── support.js                          (A) runtime do Claude Design
```

Nenhum arquivo em `apps/api`, `apps/inbox` ou `apps/workers`. Nenhuma dependência
nova, e por isso nenhuma versão a verificar.

## Risks / Trade-offs

- **`apps/inbox` pesa mais na entrada do painel** → passa de uma consulta
  (Canais) para três (Canais, Sessões, Mensagens). No total, a entrada dispara
  seis requisições contra dois processos. A leitura do risco anterior continua
  valendo, e esta change não a reabre: falha isolada por item, nenhum item
  bloqueia a casca, e `apps/inbox` fora do ar já quebrava `/channels`. As duas
  rotas novas são agregadas, com custo independente de N por spec. *Mitigação:*
  nenhum portão de carregamento combinado; cada item com o próprio estado.
- **`router.test.tsx` não mocka `sessionsApi` hoje** → com a raiz levando ao
  inventário, `getSessionsSummary` e `getMessagesSummary` **escapariam para a
  rede**. É o modo de falha registrado no próprio arquivo (`router.test.tsx:42-50`,
  convenção 18): contra uma API real a chamada volta 401, `request<T>` chama
  `clearToken()` e navega para `/login`, e quem reprova é o teste **seguinte**.
  *Mitigação:* mock **parcial** (`importOriginal`, preservando `request`/`ApiError`
  e as funções usadas pelas telas de canal), com os dois resumos no `beforeEach`
  de `describe('AppRouter')`, que hoje configura os quatro catálogos pela mesma
  razão de vazamento entre describes.
- **Janela recalculada em cada refetch** → com `refetchOnWindowFocus` padrão, o
  número pode mudar enquanto o operador olha, porque a janela andou. Isso é o
  comportamento correto para "últimos 7 dias", não defeito.
- **Nome da capability mais estreito que o conteúdo** → D1. Aceito, com o
  `Purpose` carregando o sentido.
- **Item de atividade a 389px nunca visto** → D8. *Mitigação:* a conferência
  visual começa por ele.

## Migration Plan

Uma implantação, sem etapas: frontend estático. Nenhuma migração de dados,
nenhuma feature flag, nenhum estado persistido no navegador. As duas rotas de
backend já estão na `main` (PRs #23 e #24) e sobem junto com `apps/inbox`; não há
produção ainda.

**Rollback:** reverter o commit. A tela volta a quatro itens e à grade sem teto.

## Open Questions

**Nenhuma pergunta de produto em aberto.** Nome da capability, atalho, janela,
clientes, grade e contrato do item fecharam na exploração e no protótipo.

**Uma pendência de verificação, com gatilho:**

> **Conferência visual do app contra o protótipo (convenção 14)**, nos dois
> esquemas, cruzando **1328 / 1056 / 784px** e a largura de trabalho **~1860px**.
> O alvo do interior do item está definido pelas 16 capturas aprovadas. A grade
> 4 + 2 e o item de atividade a 389px **não** estão (D8).
> **Gatilho:** antes do fechamento do apply. Se o item de atividade alargado
> reprovar, a saída é reabrir a D6 com o dado na mão, e não improvisar um `maw`.
