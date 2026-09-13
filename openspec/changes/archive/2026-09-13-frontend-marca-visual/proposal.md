## Why

O painel não tem marca. O topo da barra lateral desenha um quadrado de 24px com
a letra `B` em monoespaçada (`AppShell.tsx`, `ProductMark`), a tela de login abre
com um `<Title>` de texto puro, o `<title>` do documento é literalmente
`"frontend"` e o `public/favicon.svg` ainda é o losango roxo `#863bff` que veio
no scaffold do Vite. Três pontos de contato com a identidade do produto, três
placeholders.

O pacote de marca chegou (`~/Downloads/marca`): símbolo Robô-garçom na versão de
cor `R-D1`, com lockups, favicon e ícone de aplicativo. A escolha do `R-D1` foi
feita justamente para não pedir cor nova — ele usa `#191a1c` e `#2a6ecb`, que já
são `gray[9]` e `butecoBlue[6]` no `theme.ts`. O trabalho de tema já está feito;
falta pendurar a marca nele.

## What Changes

- Os SVGs do símbolo entram em `apps/frontend/src/assets/brand/`, **byte a byte
  como entregues** — manifesto C2PA de proveniência incluído. O favicon e o
  ícone de aplicativo vão para `public/`.
- Nasce `src/components/brand/Logo.tsx`, o único lugar do painel autorizado a
  desenhar a marca. Duas variantes (`mark`, a cabeça; `symbol`, cabeça e
  gravata) e a regra de guarda de tamanho do manual da marca vivendo em código,
  que é onde ela consegue impedir um duotone quebrado numa lista densa.
- `vite-plugin-svgr` entra como devDependency. Os SVGs viram componente React
  inlineado no DOM, e as duas cores da marca são reescritas em tempo de build
  para tokens do tema (`currentColor` e `var(--mantine-primary-color-filled)`).
  Isso dá troca de tema sem `if` em componente e **dispensa os seis arquivos
  `*-escuro.svg` do pacote**.
- `ProductMark` sai do `AppShell.tsx` e vira `<Logo variant="mark" size={24} />`.
  O nome do produto continua sendo o `<Text>` HTML que já está lá.
- A tela de login ganha o símbolo acima do formulário — a única tela com espaço
  vertical para a peça principal da marca.
- `index.html` ganha `<title>` de verdade, `apple-touch-icon` e `theme-color`;
  `public/favicon.svg` deixa de ser o losango do Vite.
- `theme.ts` ganha **uma** variável no `cssVariablesResolver`:
  `--buteco-brand-ink`, o `#191a1c`/`#e9eaec` que o manual chama de tinta. Não é
  cor nova — são `gray[9]` e `dark[0]`, que já existem; é o papel que precisava
  de nome, porque troca de ponta entre os esquemas.
- **O lockup vertical assina a tela de login**, com o espaçamento óptico e a
  escala tipográfica que o pacote desenhou. O nome desenhado lê os mesmos tokens
  que o resto da marca e troca de tema junto; a semântica de cabeçalho e o nome
  acessível ficam no `<h2>`, por texto visualmente oculto.
- **O lockup horizontal não é usado.** Na barra lateral o nome do produto já é
  um `<Text>` HTML que não sai, e substituí-lo por `<text>` SVG só tiraria
  seleção e busca. Ele e as demais peças não usadas ficam versionados em
  `docs/marca/`, fora do bundle.

Fora de escopo, por decisão explícita: conversão dos lockups em curvas (não é
tarefa de código), PNGs de fallback (nenhum fluxo do produto exige raster) e
ícone de aplicativo em formato de sistema — `.icns`/`.ico` só quando existir
empacotamento desktop.

## Capabilities

### New Capabilities
- `frontend-brand-identity`: a marca do painel como contrato — em que pontos de
  contato ela aparece (barra lateral, login, aba do navegador, ícone de
  aplicativo), qual peça serve qual tamanho, a regra de guarda que impede o
  duotone abaixo do mínimo legível, e a exigência de que a marca seja servida
  por um componente único em vez de desenhada tela a tela.

### Modified Capabilities
- `frontend-app-shell`: o requisito "Ícones vindos de uma biblioteca
  compartilhada" proíbe, sem exceção, SVG declarado dentro de componente de
  feature ou de layout. A marca não vem — nem pode vir — de biblioteca de
  ícones. O requisito passa a distinguir ícone de interface (que continua vindo
  do `lucide-react`) de marca do produto (que vem de arquivo versionado em
  `src/assets/brand/`, através do componente `Logo`), mantendo a proibição de
  SVG transcrito à mão em qualquer um dos dois casos.

## Impact

Afeta **apenas `apps/frontend`**. Nenhuma mudança em `apps/api`, `apps/workers`,
`apps/inbox` ou no contrato de qualquer rota HTTP.

- `src/assets/brand/` — diretório novo. Não existe `src/assets/` hoje, e não há
  um único `import` de SVG no frontend inteiro: esta change estabelece o padrão.
- `src/components/brand/Logo.tsx` + teste — componente novo.
- `src/components/layout/AppShell.tsx` — `ProductMark` removido.
- `src/features/auth/pages/LoginPage.tsx` — símbolo acima do formulário.
- `src/theme.ts` + `src/theme.test.ts` — uma variável no resolver, com o teste
  de âncora correspondente.
- `index.html`, `public/favicon.svg`, `public/app-icon.svg`.
- `vite.config.ts` — o plugin e o mapa de reescrita de cor, que é o que faz a
  marca seguir o tema.
- `src/vite-env.d.ts` — tipos de `vite-plugin-svgr/client`, senão o `?react` não
  compila no `tsc -b`.
- `docs/marca/` — os lockups e o manual, versionados fora do bundle.
- Dependência nova: `vite-plugin-svgr` (devDependency). É a única.
- **Divergência conhecida a resolver**: o `README.md` do pacote especifica
  `#5b8fd4` como acento do tema escuro, mas `butecoBlue[4]` do `theme.ts` é
  `#6495db` — e é ele que pinta o item de navegação ativo, a oito pixels da
  gravata do logo na mesma barra. A change resolve em favor do tema; a
  justificativa fica em `design.md`.
