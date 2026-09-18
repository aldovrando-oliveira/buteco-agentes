## Why

As duas rotas agregadas de atividade de `apps/inbox` — `GET /sessions/summary`
(`startedCount`) e `GET /messages/summary` (`inboundCount`) — foram entregues nas
changes `inbox-sessoes-por-periodo` e `inbox-mensagens-recebidas-periodo` e **não
têm nenhum consumidor**. O inventário, que é a tela de entrada do painel, responde
"o que existe cadastrado e as consultas responderam?", mas não responde "o sistema
está sendo usado?".

O D10 do `design.md` arquivado de `frontend-inventario-catalogos` deixou os quadros
de atividade de fora por dois motivos: nenhuma rota filtrava por intervalo, e
definir qual "mensagens" contar era trabalho de backend. As duas changes de inbox
resolveram os dois. **Esta change fecha aquele D10; não o contradiz.**

## What Changes

- `InventoryPage.tsx` ganha **dois itens de atividade**, na mesma grade e depois
  dos quatro de catálogo: **Sessões iniciadas** e **Mensagens recebidas**, cada um
  com a contagem dos **últimos 7 dias**. Não há rota de frontend nova, item de
  navegação novo nem seção separada.
- A janela é **fixa e rolante**: `[agora − 168h, agora]`, calculada no instante da
  consulta. Não há seletor de período.
- Cada item de atividade **declara a janela nos quatro estados** (valor, zero
  medido, não sei, carregando), porque ela é propriedade da pergunta, não da
  resposta.
- Os itens de atividade **não têm atalho**. Nenhuma listagem do painel conta o
  mesmo conjunto que eles contam.
- Cada item de atividade herda sem redecidir o que os quatro de catálogo já têm:
  proveniência em quatro estados ("Nenhuma sessão iniciada", nunca `0`), falha e
  nova tentativa independentes, e nenhum portão de carregamento combinado.
- **A grade passa a ter no máximo quatro colunas.** A regra vale para os seis
  itens, inclusive os quatro de catálogo que já existem. Com seis itens, o
  `auto-fit` sem teto dava 5 + 1 exatamente na largura de trabalho real
  (~1860px); com o teto, dá 4 + 2.
- `sessionsApi.ts` ganha `getSessionsSummary` e `getMessagesSummary`, e
  `useSessions.ts` ganha os dois hooks. Não nasce `messagesApi.ts`.
- **Muda requisito vivo**: o inventário deixa de ser só de catálogos. Isso altera
  a regra de derivação da contagem, a regra do atalho e a proveniência, que hoje
  são escritas "por catálogo".

## Capabilities

### New Capabilities

Nenhuma. Os itens de atividade entram em `catalog-inventory-ui`, e não numa
capability própria: o requisito de falha independente precisa falar dos **seis**
itens juntos ("o estado de um não determina os outros"), e partido em duas specs
ele não teria onde morar. O `design.md` registra o que isso custa: o nome
`catalog-*` fica mais estreito que o conteúdo, e a proposta anterior tinha
reservado `dashboard-*` para este dia.

### Modified Capabilities

- `catalog-inventory-ui`:
  - **"Inventário dos catálogos como entrada do painel"** (MODIFIED): passa a
    cobrir itens de atividade. A regra de apuração única é generalizada — a
    contagem de catálogo continua vindo da consulta da listagem, e a de atividade
    vem só da rota agregada, sem recontagem no cliente. O atalho passa a ser
    exigido **por critério**: existe quando há uma listagem que conta exatamente
    aquele conjunto, e é proibido quando essa listagem não existe.
  - **"Proveniência da contagem de cada catálogo"** e **"Falha e nova tentativa
    independentes por catálogo"** (MODIFIED): passam de "por catálogo" para
    "por item". Os cenários existentes continuam valendo como estão.
  - **"O inventário não reproduz a explicação das listagens"** (MODIFIED): hoje
    ele exige atalho em todo item. Passa a descrever os dois tipos de item e
    proíbe prosa explicativa nos dois.
  - **"A contagem de atividade declara a sua janela"** (ADDED): janela fixa de
    7 dias rolante, visível nos quatro estados.
  - **`Purpose` reescrito** na passada do sync, direto no arquivo vivo, como já
    fizeram `2026-09-13-frontend-knowledge-base-form-orientacao` (tasks 11.2) e
    `…-resumo-indexacao` (tasks 11.2).

## Impact

Afeta **apenas `apps/frontend`**. Nenhuma mudança em `apps/api`, `apps/inbox` ou
`apps/workers`, e nenhuma rota de backend nova: as duas rotas já existem e têm
spec viva (`inbox-session-period-summary`, `inbox-message-period-summary`).

- Arquivo novo: `features/inventory/utils/activityWindow.ts`, mais o teste dele.
- Modificados, cada um com o seu teste: `sessionsApi.ts`, `useSessions.ts`,
  `InventoryPage.tsx` e `router.test.tsx`. Neste último, `sessionsApi` hoje não é
  mockado, e passa a precisar de mock parcial.
- **`apps/inbox` pesa mais na entrada do painel**: passa de uma consulta (Canais)
  para três. Risco declarado no `design.md`, sem reabrir a leitura anterior.
- O protótipo final (`Inventario Buteco Agentes.dc.html`, com seis itens) é
  anexado em `design/` como referência da conferência visual manual (convenção
  14). A grade do protótipo **não** é a do app; a divergência está registrada no
  `design.md`.
