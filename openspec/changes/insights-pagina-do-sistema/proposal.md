**Issue:** #52 · **Bloqueada por:** #65 · **Achados com issue própria:** #51 (L1), #66 (L2, L3, L5), #67 (L4)

## Why

As etapas 1 a 3 da linha `metricas-de-operacao` coletam, preservam nulo ponta a
ponta e agregam — e **nada disso é visível**. `GET /insights/system` responde em
produção com 27 métricas, um mapa de regimes de medição e códigos de
parcialidade, e enquanto não houver tela esse trabalho não chega a quem opera.

E a tela é exatamente onde o trabalho das três etapas pode ser desfeito **por uma
linha**: um `?? 0` na camada de apresentação colapsa "o provedor não reportou"
em "o provedor reportou zero", e o painel passa a afirmar o que o sistema não
sabe. Esta change existe tanto para mostrar os números quanto para fixar, em
comportamento verificável, a gramática que os separa.

## What Changes

- **Nova página `/insights`** em `apps/frontend`, escopo do sistema, servida pela
  rota única `GET /insights/system` — montada a partir do artboard aprovado
  `Main.dc.html`.
- **A gramática de quatro estados vira código com guarda**: número medido, célula
  vazia (ausência de fonte), travessão (dado desconhecido) e zero contado. As
  asserções que a protegem são **negativas** — afirmar que **não** há `0` onde a
  origem é nula.
- **O "medindo desde" sai por regime, não por tela.** A resposta traz um mapa com
  dois regimes hoje (`execution`, `embedding`), e cada grupo de métricas declara
  a qual pertence. Um texto único mentiria sobre um dos dois.
- **Período pedido maior que a medição ganha estado próprio**: faixa hachurada,
  nunca zero. Um dia ausente da série é não medido; um dia presente com `0` é
  medido e vazio. **Hoje a rota não faz essa segunda metade** — ela omite os dois
  casos igual, contra a própria spec —, e a decisão desta change é que **a rota
  corrige e a tela lê**, em vez de a tela reconstruir. É a #65, e ela bloqueia
  esta change.
- **Escala do mapa de calor como variável por esquema**, no tema: o papel troca
  de ponta da escala entre claro e escuro (convenção 15), e nenhum tom fica
  cravado no componente.
- **Item `Insights` na navegação** da casca lateral.
- **Os `caveats` da resposta são renderizados**, não ignorados: cada um vira
  texto ao lado do número que ele limita.
- **Cinco lacunas entre o protótipo e a rota entram declaradas, não escondidas**
  — no estado que o `Estados.dc.html` desenhou para métrica sem fonte. Card
  ausente é invisível; card com lacuna declarada é item aberto que se vê. São
  elas: motivo das recusas (L1, #51), separação turno × compactação (L2), cache
  lido por modelo (L3), três colunas por agente (L4, #67) e a mediana da duração
  da task (L5) — L2, L3 e L5 na #66. **Cada uma tem issue**, porque o
  `design.md` desta change é apagado do radar no archive e a lacuna não pode ir
  junto.

**Nenhuma mudança de backend nesta change** — mas uma **dependência** dela.
`apps/api`, `apps/workers`, `apps/inbox`, `libs/` e o `nginx.conf` ficam
intocados aqui — o prefixo `insights` já roteia, e a
rota de página é coberta pelo `Sec-Fetch-Mode: navigate` do bloco. Onde a tela
precisa de dado que a rota não serve, o achado é **registrado e sequenciado**
(convenção 1), nunca corrigido de improviso dentro da change de tela.

## Capabilities

### New Capabilities

- `system-insights-ui`: a página Insights do escopo do sistema em
  `apps/frontend` — a gramática dos quatro estados de valor, a distinção entre
  período não medido e período medido sem uso, o "medindo desde" por regime, a
  renderização dos códigos de parcialidade e o estado de lacuna declarada para
  métrica que o protótipo aprovou e a rota não serve.

### Modified Capabilities

- `frontend-visual-theme`: a escala de intensidade do mapa de calor passa a ser
  contrato do tema, declarada **nos dois esquemas** — a rampa inverte entre eles
  (no escuro a intensidade cresce clareando; no claro, escurecendo), e por isso
  não pode ser tom cravado no componente.

## Impact

**App afetado: `apps/frontend`, e só ele.**

- **Código novo:** `src/features/insights/` — cliente da rota com o seu próprio
  `request<T>`/`ApiError` (convenção da casa: sem cliente HTTP compartilhado
  entre features), hook de consulta, tipos do contrato, utilitários puros de
  janela/regime/escala, componentes de apresentação e a página.
- **Código modificado:** `src/app/routes.tsx` (rota `/insights`),
  `src/components/layout/AppShell.tsx` (item de navegação) e `src/theme.ts`
  (variáveis da escala de calor no `cssVariablesResolver`).
- **Dependência de segunda consulta:** o nome do agente **não vem** em
  `/insights/system` — a resposta traz `agentId`. A tabela por agente cruza com
  `useAgentsQuery`, que já existe e já é consumida pelo inventário.
- **Sem dependência nova de pacote.** Os gráficos saem em SVG inline e CSS, como
  no protótipo; nenhuma biblioteca de charts entra.
- **Verificação:** a suíte de `apps/frontend` não usa Testcontainers e roda
  rápido, mas jsdom não enxerga cor, contraste, layout nem quebra de linha. A
  conferência manual nos dois esquemas é **tarefa própria, do dono** (convenção
  14), e o agente produz os estados a conferir.

**Não afetados, e a razão:** `apps/api` (a rota já serve o contrato desta tela),
`apps/workers` e `apps/inbox` (não participam), `libs/` (nada a compartilhar),
`stack/nginx.conf` (o prefixo `insights` já roteia desde a etapa 3).
