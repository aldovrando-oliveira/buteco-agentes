## Context

`apps/frontend/src/theme.ts` é `createTheme({})`. Todo o painel — 35 telas, 379
testes — renderiza com os defaults do Mantine 9.4.2. Não há CSS module no
projeto e não há cor literal em componente: as 108 ocorrências de cor são nomes
semânticos (`c="dimmed"` ×49, `color="red"` ×38, `color="yellow"` ×10,
`color="gray"` ×6, `color="green"` ×3, `c="yellow"` ×2, `c="red"` ×1,
`color="teal"` ×1). Preencher o tema repinta tudo sem tocar em componente.

A fonte da identidade é o bundle `~/Downloads/sistema_gestao`: o protótipo
`Buteco Agentes.dc.html`, o `README.md` e o `AJUSTES_VISUAIS.md`. Este último
arbitra a premissa (o protótipo é a identidade) e propõe escalas prontas para
colar. **As escalas dele não podem ser coladas como estão** — a tabela de
ancoragem foi escrita sem executar o Mantine e erra em qual shade cada papel
consome. Esta design corrige, com a evidência lida do pacote instalado.

### O que o Mantine 9.4.2 realmente consome

Verificado em
`node_modules/@mantine/core/esm/core/MantineProvider/MantineCssVariables/get-css-color-variables.mjs`:

```js
// colorScheme === "light"   (primaryShade.light = 6)
'--mantine-color-X-filled':      'var(--mantine-color-X-6)',   // variant="filled"
'--mantine-color-X-text':        'var(--mantine-color-X-filled)', // <Text c="X">
'--mantine-color-X-light':       'var(--mantine-color-X-1)',   // variant="light" FUNDO
'--mantine-color-X-light-color': 'var(--mantine-color-X-9)',   // variant="light" TEXTO
'--mantine-color-X-outline':     'var(--mantine-color-X-6)',

// colorScheme === "dark"    (primaryShade.dark = 4)
'--mantine-color-X-text':        'var(--mantine-color-X-4)',
'--mantine-color-X-filled':      'var(--mantine-color-X-4)',
'--mantine-color-X-light':       darken(X[9], 0.5),            // opaco, derivado do 9
'--mantine-color-X-light-color': 'var(--mantine-color-X-0)',
```

E em `styles.css`:

```css
[data-mantine-color-scheme='light'] { --mantine-color-dimmed: var(--mantine-color-gray-6);
                                      --mantine-color-body: #fff; }
[data-mantine-color-scheme='dark']  { --mantine-color-dimmed: var(--mantine-color-dark-2);
                                      --mantine-color-body: var(--mantine-color-dark-7); }
.mantine-Paper-root { background-color: var(--mantine-color-body); }   /* linha 840 */
body                { background-color: var(--mantine-color-body); }   /* linha 30  */
[data-mantine-color-scheme='light'] .mantine-Paper-root { --paper-border-color: var(--mantine-color-gray-3) }
[data-mantine-color-scheme='dark']  .mantine-Paper-root { --paper-border-color: var(--mantine-color-dark-4) }
```

Três consequências que definem esta design:

1. **No tema claro, `variant="light"` usa shade 1 como fundo e shade 9 como
   texto** — não shade 0 e shade 8, como diz a tabela do `AJUSTES_VISUAIS.md`.
   O shade 0 não é usado por variante nenhuma no claro; ele só aparece como
   cor de texto no escuro.
2. **`c="dimmed"` no claro lê `gray[6]`**, não `gray[7]`. A escala do documento
   deixaria os 49 usos um passo mais claros que o `--mut` do protótipo.
3. **`<body>` e `Paper` leem a mesma variável.** O degrau de superfície do
   protótipo (página `#f6f5f3`, card `#ffffff`) é inalcançável por paleta:
   mexer em `--mantine-color-body` move os dois juntos.

## Goals / Non-Goals

**Goals:**
- Um único arquivo (`theme.ts`) como fonte da identidade visual, consumido por
  todas as telas atuais e futuras sem decisão de cor caso a caso.
- Reproduzir no navegador as cores que o protótipo mostra, nos dois esquemas.
- Zero edição de componente por causa de cor ou de tamanho de título.
- Deixar registrado por que os números divergem do `AJUSTES_VISUAIS.md`, para
  que a próxima pessoa não "corrija" de volta.

**Non-Goals:**
- Reestruturar o `AppShell` e adotar o Lucide (etapa seguinte).
- Os ajustes estruturais da listagem de agentes — badges, colunas, filtros
  (etapa posterior).
- Preferência de densidade compacta/confortável e card "Primeiros passos"
  (adiados em 2026-09-05).
- Automação de browser para regressão visual.

## Decisions

### D1 — Sobrescrever os nomes existentes, não criar nomes novos

`red`, `yellow`, `green`, `gray` e `dark` mudam de valor; os 108 call sites
continuam escritos como estão. O accent entra como cor nova `butecoBlue` e vira
`primaryColor`, porque não substitui nada — hoje o primary é o `blue` default.

*Alternativa descartada:* cores semânticas novas (`ok`, `wa`, `da`). Seria mais
explícito, mas custaria renomear ~50 call sites e ainda assim os componentes
internos do Mantine (erro de input, por exemplo) continuariam lendo `red`.

### D2 — Ancoragem por papel, verificada, não pela tabela do handoff

Cada escala fixa os shades que o Mantine consome; os demais são preenchimento
livre para manter a rampa monotônica.

| shade | papel no Mantine | contrato |
|---|---|---|
| 0 | texto de `variant="light"` no **escuro** | tinta pálida |
| 1 | fundo de `variant="light"` no **claro** | fundo semântico do protótipo |
| 3 | borda de `Paper`/`Card` no claro (só `gray`) | `--bd` do protótipo |
| 4 | `c="X"` e `variant="filled"` no **escuro** | valor escuro do protótipo |
| 6 | `c="X"` e `variant="filled"` no **claro** | derivado |
| 9 | texto de `variant="light"` no **claro** | texto semântico do protótipo |

### D3 — As escalas

```ts
const butecoBlue = ['#f5f8fe','#eef3fb','#d6e3f6','#b0c8ee','#6495db',
                    '#4180d4','#2a6ecb','#245cad','#1e4d92','#1a3f78'];
//                            ↑ --acsf              ↑ --ac escuro   ↑ --ac

const green  = ['#f3faf6','#e9f6ee','#cfe9dc','#a0d7bd','#4cc38a',
                '#33ab72','#22935c','#1e8853','#1c814e','#1a7a49'];
//                        ↑ --okbg                      ↑ --ok escuro        ↑ --ok

const yellow = ['#fdf8ee','#fbf1de','#f4e0bb','#e8c489','#e2a63f',
                '#c78c28','#b07a1e','#9d6a13','#93620a','#8a5a00'];
const red    = ['#fef5f4','#fdeceb','#f9d3d0','#f3aea7','#f0766a',
                '#d94a3b','#cf3a2b','#c23325','#b92e21','#b02a1f'];

// neutro quente do protótipo
const gray   = ['#faf9f8','#f6f5f3','#f1f1f1','#e5e5e5','#d5d4d2',
                '#8b9097','#686d74','#4d5157','#33363b','#191a1c'];
//               ↑ --sf2   ↑ --bg    ↑ --bd2   ↑ --bd              ↑ --mut2  ↑ --mut  … ↑ --fg

// dark[] é invertido: 0 = texto mais claro
const dark   = ['#e9eaec','#c8cbd0','#9aa0a8','#7d838b','#343639',
                '#2b2e34','#24272c','#1b1d21','#16181b','#121316'];
//               ↑ --fg              ↑ --mut   ↑ --mut2  ↑ borda          ↑ --sf     ↑ --bg
```

`butecoBlue[4] = #6495db` é `color-mix(in oklch, #2a6ecb, white 26%)` calculado,
não estimado. `butecoBlue[1] = #eef3fb` é o accent a 8% composto sobre branco —
o `--acsf` que o protótipo usa no item de nav ativo. `gray[3] = #e5e5e5` e
`dark[4] = #343639` são as bordas do protótipo compostas sobre suas superfícies.

**`dark[7] = #1b1d21` é obrigatório**, não estético: é a variável que o Mantine
lê como `--mantine-color-body` no escuro, e portanto a cor de `Paper`, `Card`,
`Table` e `Modal`. O `AJUSTES_VISUAIS.md` põe a cor de superfície em `dark[7]`
por acaso certo e a de fundo em `dark[9]`, que o Mantine nunca lê — por isso ele
descreve `dark[9]` como "fundo do body".

### D4 — Fundo da página fora da paleta

Como `<body>` e `Paper` compartilham `--mantine-color-body`, o degrau vem de uma
regra própria em `index.css`, carregada depois de `@mantine/core/styles.css`
(a ordem de import em `main.tsx` já é essa), lendo uma variável que o
`cssVariablesResolver` declara:

```css
body { background-color: var(--buteco-page-bg); }
```

`--mantine-color-body` fica sendo a **superfície** nos dois esquemas, que é o
que `Paper`/`Card`/`Table`/`Modal` precisam.

O fundo da página **não é um tom da escala `gray`** — ver D13, que corrige a
primeira versão desta decisão.

*Alternativa descartada:* redefinir `--mantine-color-body` para a cor de fundo e
sobrescrever o background do `Paper` via `theme.components`. Inverte o default
de todos os componentes que leem a variável (12 no `styles.css`) para ganhar
nada.

### D5 — `cssVariablesResolver` só para papéis neutros que a paleta não expressa

Um único ajuste: `--mantine-color-default-border` lê `gray[4]`/`dark[4]`, mas a
borda do protótipo é a mesma do `Paper` (`gray[3]`). O resolver aponta os dois
para o mesmo valor, em vez de espremer `gray[3]` e `gray[4]` no mesmo tom e
perder um degrau da rampa.

### D6 — Tamanhos de título no tema, não nos 13 `<Title>`

`theme.headings.sizes` cobre `h1`–`h6` de uma vez. O `AJUSTES_VISUAIS.md` manda
editar cada chamada (`<Title order={2} fz={20} fw={600}>`), o que contraria a
própria regra dele de não tocar em componente. Título de página vira ~20px/600.

### D7 — Fontes self-hosted

`@fontsource/ibm-plex-sans@5.3.0` e `@fontsource/ibm-plex-mono@5.3.0`
(versões verificadas no registry em 2026-09-05), nos pesos usados — sans
400/500/600/700, mono 400/500/600 — reunidas em `src/fonts.ts` para não poluir
o `main.tsx`.

Importar subset a subset (`latin` e `latin-ext`) em vez do arquivo de peso
inteiro: o navegador só baixaria o latino de qualquer jeito, por causa do
`unicode-range`, mas o build emitiria cirílico, grego, vietnamita e japonês
junto — 4,2 MB de fontes em `dist` contra 168 KB.

*Alternativa descartada:* Google Fonts no `index.html`, como sugere o handoff.
O painel é entregue containerizado (commit `de9bbae`); uma fonte por CDN cria
dependência de internet em runtime e um terceiro no caminho de render de uma
ferramenta administrativa interna. Self-hosted entra no bundle e some do radar.

### D8 — `auto` como esquema padrão, `index.html` junto

O script inline do `index.html` tem `: 'light'` cravado como fallback quando o
`localStorage` está vazio. Trocar só o `defaultColorScheme` do `MantineProvider`
faria o painel piscar claro antes do React montar, em quem usa o SO no escuro.
Os dois mudam na mesma tarefa. A alternância manual e a persistência entre
sessões não mudam.

O `matchMedia` já está mockado em `src/test/setup.ts` com `matches: false`, então
a suíte resolve `auto` como claro e os testes atuais sobrevivem.

### D9 — Sombras completas, não só `xs`/`sm`

O protótipo tem uma sombra só (`0 1px 2px rgba(16,14,10,.05)`) e nenhuma no
escuro. Sobrescrever apenas `xs`/`sm` deixaria `md`/`lg`/`xl` com os defaults
pesados do Mantine — e `Modal` usa `xl`. Todas as cinco entram na mesma família.

### D10 — O teste unitário assere as âncoras, não a aparência

jsdom não enxerga contraste nem layout: os 379 testes são cegos a esta change.
O que **é** verificável é que os tokens valem o que a spec diz. Entra um
`theme.test.ts` afirmando cada âncora contratual da tabela do D2 e o
`primaryShade`. É a única regressão automatizável aqui, e pega o erro mais
provável de todos — alguém "consertar" as escalas de volta para a tabela do
handoff.

O teste de `AppShell` "abre no tema claro por padrão" muda de premissa junto com
o requisito do `frontend-scaffold`.

### D11 — O toggle de tema passa a ler o esquema *computado* (descoberto na implementação)

`useMantineColorScheme()` devolve a preferência crua, que com o padrão `auto`
é a string `'auto'` — nunca `'dark'`. O `AppShell` decidia o rótulo do botão e o
alvo do clique com `colorScheme === 'dark'`, o que sob `auto` fica falso mesmo
com o sistema operacional no escuro: o botão diria "Mudar para tema escuro" com
a tela já escura, e o primeiro clique não mudaria nada visível.

A correção é `useComputedColorScheme('light')`, que resolve `auto` para o
esquema efetivo. É consequência direta do D8 — sem ela, o requisito de
alternância manual do `frontend-scaffold` deixaria de valer — e está coberta
pelo caso novo de `AppShell.test.tsx` que roda com `matchMedia` reportando
`prefers-color-scheme: dark`.

*Por que não estava previsto:* a design tratou a troca de `light` para `auto`
como configuração, sem notar que ela muda o **tipo** do valor devolvido pelo
hook que o `AppShell` já consumia.

### D12 — O conteúdo ganha superfície própria (descoberto na conferência visual)

O D4 pintou o fundo da página e deixou `--mantine-color-body` como superfície.
A conferência mostrou que **não havia superfície para receber a cor**: as três
tabelas, os dois formulários e as duas colunas do detalhe de canal são
renderizados diretamente na página, sem `Paper` nem `Card`. O `<Table>` do
Mantine não tem fundo próprio — enquanto o `<body>` era branco, isso passava
despercebido; com o fundo cinza, o conteúdo principal do painel apareceu sobre
o cinza.

É regressão introduzida por esta change, não dívida anterior revelada: antes do
D4 a aparência estava correta por acidente. A correção entra aqui, e não numa
etapa seguinte, porque foi aqui que quebrou — deixar para depois entregaria o
painel visivelmente pior do que estava antes.

Tabelas ganham `Paper withBorder radius="md"` com `overflow: hidden`, para o
conteúdo respeitar o raio do card; formulários e as colunas de sessão ganham
`Paper withBorder` com padding.

*O que isto custa:* o "zero edição de componente" deixa de valer na forma
absoluta. A premissa se mantém para **cor, tipografia e raio** — que é o que os
108 call sites de cor comprovam — mas não para o degrau de superfície, porque
esse não é um valor que se troca, é uma camada que precisa existir no markup.
A design original não distinguiu as duas coisas.

*Alternativa descartada:* reverter o D4 e adiar "fundo cinza + superfícies"
para uma etapa única. Manteria o E1 puro, ao custo de o painel ficar sem o
degrau do protótipo por tempo indeterminado, com a regra de fundo pronta e
desligada.

### D13 — O fundo da página sai da escala neutra (descoberto na conferência visual)

A primeira versão do D4 lia o fundo da página de `gray[1]`, onde estava o
`--bg` do protótipo. O Mantine lê `gray[1]` em **quinze** lugares como
"superfície sutil elevada": fundo do `SegmentedControl`, hover de item, linha
selecionada, `Code`. Igualar os dois fez todos esses elementos sumirem contra
exatamente o fundo do qual deviam se destacar — o filtro por situação da
listagem apareceu sem container, restando só os separadores (`gray[3]`).

Regressão desta change, pela mesma razão do D12: antes, `gray[1]` era o
`#f1f3f5` padrão do Mantine sobre página branca, e o controle aparecia.

A causa é conceitual. O protótipo tem três neutros claros distintos — página
`--bg #f6f5f3`, superfície sutil `--sf2 #faf9f8`, superfície `--sf #ffffff` —
e as superfícies são *mais claras* que a página. O Mantine assume que a página
é branca e que `gray[0]`/`gray[1]` são tints que se destacam sobre ela. Os dois
modelos não cabem na mesma escala.

Correção: `gray[1]` passa a ser `--sf2`, o papel que o Mantine de fato lhe dá, e
o fundo da página vira `--buteco-page-bg`, declarada pelo `cssVariablesResolver`
e consumida só pelo `body`. Um valor que a paleta não contém.

O teste `theme.test.ts` guarda a invariante: o fundo da página tem que ser
distinto de **todos** os dez tons de `gray`.

### Árvore de arquivos

```
apps/frontend/
├── index.html                        (M) fallback do script de esquema de cor → auto
├── package.json                      (M) + @fontsource/ibm-plex-sans, -mono
└── src/
    ├── fonts.ts                      (N) imports de fonte por subset (D7)
    ├── index.css                     (M) + regra de fundo da página (D4)
    ├── main.tsx                      (M) import de fontes, defaultColorScheme,
    │                                     cssVariablesResolver (D5)
    ├── theme.ts                      (M) de createTheme({}) à identidade completa,
    │                                     + o cssVariablesResolver (D5)
    ├── theme.test.ts                 (N) âncoras contratuais e contraste (D10)
    ├── components/layout/
    │   ├── AppShell.tsx              (M) useComputedColorScheme (D11)
    │   └── AppShell.test.tsx         (M) premissa do esquema padrão
    └── features/                     (M) superfície própria (D12)
        ├── agents/components/AgentTable.tsx
        ├── agents/components/AgentForm.tsx
        ├── mcp-servers/components/McpServerTable.tsx
        ├── mcp-servers/components/McpServerForm.tsx
        ├── channels/components/ChannelTable.tsx
        └── channels/pages/ChannelDetailPage.tsx
```

Nenhum arquivo em `apps/api` ou `apps/workers`. Nenhuma referência entre apps.

## Risks / Trade-offs

**`variant="filled"` em `green` e `yellow` não passa AA com texto branco** —
`#22935c` dá 3.89:1 e `#b07a1e` dá 3.72:1. É inerente: verde e âmbar claros o
bastante para parecer "verde" e "âmbar" não sustentam texto branco.
→ *Mitigação:* os badges de estado migram para `variant="light"` na etapa
seguinte, que é o que o próprio handoff pede (seção 3, item 3) e passa
confortável: 4.81:1 no verde, 5.29:1 no âmbar. Enquanto isso, `filled` fica
restrito a `red` (5.53:1) e ao accent (5.01:1), que passam.

**As demais âncoras passam AA e estão medidas** — `variant="light"` no claro:
verde 4.81, âmbar 5.29, vermelho 5.75, accent 9.30. Escuro sobre superfície:
7.62 / 7.84 / 6.03 / 5.52. `c="dimmed"`: 5.21 sobre card e 4.78 sobre a página
no claro, 6.40 no escuro.

**No escuro, `variant="light"` é opaco, não translúcido** — o protótipo pede
fundos semânticos a 13% de opacidade; o Mantine calcula `darken(shade[9], 0.5)`,
uma cor sólida. → *Mitigação:* aceitar. Chegar lá exigiria um
`variantColorResolver` customizado, que é muito peso para uma diferença que só
aparece quando há algo colorido por trás do alerta — e não há.

**Toda a aparência do painel muda de uma vez, sem rede de segurança** — a suíte
não vê cor nem layout, e não há automação de browser. → *Mitigação:* o `theme.test.ts`
cobre os tokens; a conferência é manual, tela a tela, contra o protótipo aberto
ao lado, antes do commit. As telas em risco são as que ninguém analisou: detalhe
do agente em abas, detalhe do servidor MCP, formulários e modais.

**A escala densa encolhe o corpo de 16px para 13px** — tabelas de cinco colunas
e cards podem reflowar de modo não previsto. → *Mitigação:* é justamente o que a
conferência manual procura; e por ser tudo token, ajustar é editar `fontSizes`,
não caçar componente.

**Rampas comprimidas no extremo escuro de `green` e `red`** — como o valor de
texto do protótipo já é escuro, os shades 6–9 ficam próximos. É feio de ler no
arquivo e inconsequente na tela: cada slot serve a um papel distinto.

## Migration Plan

Uma implantação, sem etapas: é frontend estático, o build sai inteiro. Não há
migração de dados, feature flag nem compatibilidade a manter — nenhuma rota,
nenhum contrato de API muda.

**Rollback:** reverter o commit. `theme.ts` volta a `createTheme({})` e o painel
volta aos defaults do Mantine. A única coisa que sobrevive ao rollback é a
preferência de esquema de cor já salva no `localStorage` de cada operador, que
continua válida nos dois mundos.

## Open Questions

Nenhuma. As decisões de produto que estavam abertas foram fechadas em
2026-09-05: o protótipo é a identidade definitiva, o accent é o azul `#2a6ecb`
sem seletor, a tipografia é IBM Plex, o esquema padrão é `auto`, e as fontes são
self-hosted.
