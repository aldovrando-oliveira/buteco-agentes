## Why

O `theme.ts` de `apps/frontend` é `createTheme({})` — literalmente vazio. O painel
inteiro roda com os defaults do Mantine: azul padrão, fonte do sistema, corpo de
16px, superfícies todas brancas. Não existe identidade visual, apenas ausência de
decisão.

O handoff do Claude Design (`~/Downloads/sistema_gestao`) partia do pressuposto
contrário: o README manda três vezes "usar os tokens do tema Mantine do projeto",
imaginando que havia um tema para convergir. O documento `AJUSTES_VISUAIS.md` do
mesmo bundle corrige a premissa e arbitra: **o protótipo passa a ser a identidade
visual do painel**. Esta change executa essa decisão.

Ela vem primeiro entre as etapas visuais por um motivo mecânico: `apps/frontend`
não tem nenhum CSS module e nenhuma cor literal em componente — as 108 ocorrências
de cor são todas nomes semânticos do Mantine (`c="dimmed"`, `color="red"`,
`color="yellow"`, `color="green"`, `color="gray"`). Preencher o tema repinta as 35
telas sem tocar em um único componente. Qualquer ajuste de componente feito antes
disso seria refeito depois.

## What Changes

- `apps/frontend/src/theme.ts` deixa de ser vazio e passa a declarar a paleta, a
  tipografia, os raios e as sombras derivados do protótipo.
- As escalas de cor sobrescrevem os nomes que o painel já usa — `red`, `yellow`,
  `green`, `gray` e `dark` — em vez de introduzir nomes novos. **Nenhum componente
  é editado por causa de cor.** O accent entra como `butecoBlue` e vira
  `primaryColor`.
- As âncoras das escalas seguem o que o Mantine 9 realmente consome, não a tabela
  do `AJUSTES_VISUAIS.md`, que está deslocada em uma casa nas duas pontas. A
  justificativa fica registrada em `design.md`, com a evidência lida do pacote
  instalado.
- Tipografia passa a ser IBM Plex Sans/Mono, servida self-hosted via
  `@fontsource`, com escala densa (corpo 13px, linha 1.45) e tamanhos de título
  definidos em `theme.headings.sizes` — não em cada `<Title>`.
- O fundo da página deixa de ser branco e ganha o degrau de superfície do
  protótipo (`#f6f5f3` claro / `#121316` escuro). Não dá para fazer isso pela
  paleta: no Mantine o `<body>` e o `Paper` leem a *mesma* variável
  (`--mantine-color-body`), então mexer nela move os dois juntos e o degrau
  continua não existindo.
- **BREAKING (comportamento visível)**: o esquema de cor padrão deixa de ser
  `light` e passa a ser `auto`, seguindo a preferência do sistema operacional em
  quem nunca escolheu. O script inline do `index.html` muda junto, senão o painel
  pisca claro antes do React montar.

Fora de escopo, por decisão explícita: a reestruturação do shell e a adoção do
Lucide (etapa seguinte), os ajustes estruturais da listagem de agentes (etapa
posterior), a preferência de densidade compacta/confortável e o card "Primeiros
passos" (adiados em 2026-09-05).

## Capabilities

### New Capabilities
- `frontend-visual-theme`: a identidade visual do painel como contrato — paleta
  semântica e seus papéis por esquema de cor, família e escala tipográfica,
  raios, sombras e o degrau de superfície entre página e card. É a base que as
  etapas seguintes e as telas futuras consomem em vez de decidir cor a cor.

### Modified Capabilities
- `frontend-scaffold`: o requisito "Tema claro com alternância manual" fixa
  `light` como esquema padrão e exige que a aplicação abra em claro sem
  preferência salva. Passa a exigir `auto` — a preferência do sistema
  operacional na primeira visita — mantendo a alternância manual e a
  persistência entre sessões, que não mudam.

## Impact

Afeta **apenas `apps/frontend`**. Nenhuma mudança em `apps/api`, `apps/workers`
ou no contrato de nenhuma rota HTTP.

- `src/theme.ts` — de vazio a arquivo central da identidade visual.
- `src/main.tsx` — `defaultColorScheme` e o `cssVariablesResolver` do
  `MantineProvider`.
- `index.html` — fallback do script inline de esquema de cor.
- `src/index.css` — a regra de fundo da página que cria o degrau de superfície.
- `src/components/layout/AppShell.test.tsx` — o teste "abre no tema claro por
  padrão" muda de premissa junto com o requisito.
- Dependências novas: `@fontsource/ibm-plex-sans` e `@fontsource/ibm-plex-mono`,
  self-hosted em vez de Google Fonts, para não criar dependência de internet em
  runtime num painel que já é entregue containerizado (commit `de9bbae`).
- Efeito colateral esperado e desejado: **todas as 35 telas mudam de aparência**.
  Os 379 testes da suíte são cegos a isso — jsdom não enxerga contraste nem
  layout — então a conferência é visual e manual, tela a tela, contra o
  protótipo. Não há automação de browser no projeto.
