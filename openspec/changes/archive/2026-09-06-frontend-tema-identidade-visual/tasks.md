## 1. Fontes

- [x] 1.1 Adicionar `@fontsource/ibm-plex-sans@5.3.0` e `@fontsource/ibm-plex-mono@5.3.0` às dependências de `apps/frontend`
- [x] 1.2 Importar em `src/main.tsx` apenas os pesos usados — sans 400/500/600/700, mono 400/500/600 — e conferir que o build de produção emite os arquivos de fonte no bundle, sem requisição a domínio externo

## 2. Tema

- [x] 2.1 Declarar em `src/theme.ts` as seis escalas do design (`butecoBlue`, `green`, `yellow`, `red`, `gray`, `dark`), tipadas como `MantineColorsTuple`, com um comentário curto marcando cada tom ancorado e o papel que ele serve
- [x] 2.2 Definir `primaryColor: 'butecoBlue'` e `primaryShade: { light: 6, dark: 4 }`
- [x] 2.3 Declarar `fontFamily` e `fontFamilyMonospace` com as famílias IBM Plex e stacks de fallback do sistema
- [x] 2.4 Declarar `fontSizes` e `lineHeights` na escala densa da identidade (corpo 13px, linha 1.45)
- [x] 2.5 Declarar `headings` com `fontFamily`, `fontWeight` e `sizes` de `h1` a `h6`, com título de página em ~20px/600 (D6) — sem editar nenhum `<Title>`
- [x] 2.6 Declarar `radius` (5/7/9/11/14px), `defaultRadius: 'sm'` e as cinco `shadows` na mesma família de sombra leve (D9)

## 3. Superfícies e papéis neutros

- [x] 3.1 Adicionar em `src/index.css` a regra de fundo da página por esquema de cor, lendo `gray[1]` no claro e `dark[9]` no escuro (D4), e confirmar que ela é carregada depois de `@mantine/core/styles.css`
- [x] 3.2 Adicionar o `cssVariablesResolver` ao `MantineProvider` em `src/main.tsx` apontando `--mantine-color-default-border` para o mesmo tom da borda de `Paper` (D5)
- [x] 3.3 Conferir no browser que card, tabela e modal ficam na cor de superfície e a página no degrau atrás deles, nos dois esquemas

- [x] 3.4 Dar superfície própria ao conteúdo que dependia do body branco (D12): `Paper withBorder` nas três tabelas com `overflow: hidden`, nos dois formulários e nas duas colunas do detalhe de canal
- [x] 3.5 Tirar o fundo da página da escala `gray` (D13): `gray[1]` volta a ser `--sf2` e o fundo vira `--buteco-page-bg` no resolver, com teste guardando que o fundo difere dos dez tons
- [x] 3.6 Rodar a suíte após envolver — nenhum teste assere estrutura de superfície, esperado zero quebra

## 4. Esquema de cor padrão

- [x] 4.1 Trocar `defaultColorScheme` para `auto` no `MantineProvider` em `src/main.tsx`
- [x] 4.2 Trocar o fallback `: 'light'` do script inline de `index.html` para resolver a preferência do sistema operacional quando o `localStorage` está vazio (D8)
- [x] 4.3 Verificar as duas pontas com o SO no escuro: sem preferência salva a página já pinta escura, e a escolha manual salva continua sobrepondo o SO — coberto por teste automatizado em `AppShell.test.tsx`, com `matchMedia` reportando `prefers-color-scheme: dark`, em vez de verificação manual

## 5. Testes

- [x] 5.1 Criar `src/theme.test.ts` afirmando, para `butecoBlue`, `green`, `yellow` e `red`, os tons ancorados nos índices 1, 4, 6 e 9, e para `gray`/`dark` os tons de fundo, borda, texto secundário e texto principal
- [x] 5.2 Afirmar no mesmo teste `primaryColor` e `primaryShade` (`6` no claro, `4` no escuro)
- [x] 5.3 Atualizar `src/components/layout/AppShell.test.tsx`: o caso "abre no tema claro por padrão" passa a exercitar `auto` resolvendo para claro com o `matchMedia` do `setup.ts` reportando `matches: false`, e a persistência da escolha manual continua coberta
- [x] 5.4 Rodar a suíte inteira e corrigir o que quebrar por consequência do tema — esperado: nada, já que nenhum teste assere cor

## 6. Conferência visual

- [x] 6.1 Subir a pré-visualização e comparar lado a lado com `~/Downloads/sistema_gestao/Buteco Agentes.dc.html` as duas listagens e o detalhe do agente nas três abas, nos dois esquemas
- [x] 6.2 Repetir para detalhe do servidor MCP, formulários de agente e de servidor, e ao menos um modal — as telas que ninguém analisou e onde a escala densa tem mais chance de reflowar. O painel não tem modal além dos de confirmação de ação, que foram conferidos e estão corretos
- [x] 6.3 Registrar em nota da change qualquer divergência que não seja de token (posição, tamanho de componente, estrutura), para alimentar as etapas seguintes em vez de corrigir aqui

## 7. Fechamento

- [x] 7.1 Rodar lint, typecheck, `format:check` e build de produção
- [x] 7.2 Sincronizar as specs `frontend-visual-theme` (nova) e `frontend-scaffold` (modificada) e arquivar a change
