Todas as tarefas afetam **apenas `apps/frontend`**, exceto 1.4 (`docs/`).

## 1. Assets no repositório

- [x] 1.1 Copiar de `~/Downloads/marca` para `apps/frontend/src/assets/brand/`, **sem editar byte**: `simbolo-duotone.svg`, `simbolo-acento.svg` `favicon-tinta.svg` (a cabeça em tinta, por D10 — a de acento fica só em `public/`) e `lockup-vertical-duotone.svg` (D11). Os seis `*-escuro.svg` ficam de fora — sob D2 a cor vem do token, não do arquivo
- [x] 1.2 Substituir `apps/frontend/public/favicon.svg` (hoje o losango roxo `#863bff` do scaffold do Vite) por `marca/favicon.svg`, e adicionar `public/app-icon.svg`
- [x] 1.3 Conferir que os arquivos copiados mantêm o bloco `<metadata><c2pa:manifest>` íntegro (D6) — `grep -c c2pa` deve devolver 1 em cada
- [x] 1.4 Versionar os lockups, o `compacto-*` e as demais peças não usadas em `docs/marca/`, junto do `README.md` do pacote, com uma nota de que o texto dos lockups precisa virar curvas antes de qualquer uso fora do produto (D5, Open Questions)

## 2. Pipeline de build

- [x] 2.1 Adicionar `vite-plugin-svgr` às devDependencies de `apps/frontend`
- [x] 2.2 Registrar o plugin em `vite.config.ts` com o `replaceAttrValues` de D2 — `#191a1c` → `currentColor`, `#2a6ecb` → `var(--mantine-primary-color-filled)`
- [x] 2.3 Adicionar `/// <reference types="vite-plugin-svgr/client" />` a `src/vite-env.d.ts` e rodar `npm run build` — sem isso o `tsc -b` quebra no `?react` enquanto os testes passam (Risks)
- [x] 2.4 Confirmar no bundle de produção que a reescrita de cor de D2 chegou ao JS emitido — `currentColor` e `var(--mantine-primary-color-filled)` presentes, nenhum `#191a1c` ou `#2a6ecb` literal vindo dos componentes de marca. O `<metadata>` C2PA **acompanha** o componente, por D6

## 3. Token de tinta

- [x] 3.1 Adicionar `--buteco-brand-ink` ao `cssVariablesResolver` de `src/theme.ts`: `gray[9]` no claro, `dark[0]` no escuro (D3)
- [x] 3.2 Estender `src/theme.test.ts` com a âncora correspondente, no mesmo formato das âncoras já guardadas

## 4. Componente de marca

- [x] 4.1 Criar `src/components/brand/Logo.tsx` com as props `variant` (`mark` | `symbol`, padrão `mark`) e `size` em px, importando os SVGs via `?react`
- [x] 4.2 Implementar a guarda de tamanho de D8 por resolução de props, sem lançar erro: abaixo de 24px o `variant` cai para `mark`; abaixo de 32px o duotone cai para a peça de cor única
- [x] 4.3 Aplicar a tinta por `color: var(--buteco-brand-ink)`, que o `currentColor` do desenho herda (D2, D3)
- [x] 4.4 Renderizar `aria-hidden` e sem `role`, neutralizando o `role="img"`/`aria-label` que vem nos arquivos (D9)
- [x] 4.5 Criar `src/components/brand/Logo.test.tsx` cobrindo os quatro cenários testáveis em jsdom: peça escolhida por tamanho nas duas pontas da guarda, variante respeitada acima do limiar, e ausência da marca na árvore de acessibilidade

## 5. Telas

- [x] 5.1 Remover `ProductMark` de `src/components/layout/AppShell.tsx` e pôr `<Logo variant="mark" size={24} />` no lugar, preservando o `<Text fw={600}>Buteco Agentes</Text>` ao lado (D5, D7)
- [x] 5.2 Pôr o lockup vertical acima do formulário em `src/features/auth/pages/LoginPage.tsx` (D11), com o `<Title order={2}>` guardando a semântica de cabeçalho e o nome acessível por `VisuallyHidden`
- [x] 5.3 Rodar a suíte de `apps/frontend` e conferir contra o número de baseline — `AppShell.test.tsx` e `LoginPage.test.tsx` não asserem a marca hoje, então o esperado é zero quebra

## 6. Identidade do documento

- [x] 6.1 Em `index.html`: trocar `<title>frontend</title>` pelo nome do produto, adicionar `<link rel="apple-touch-icon" href="/app-icon.svg">` e `<meta name="theme-color" content="#2a6ecb">`
- [x] 6.2 Abrir o painel e conferir que a aba mostra o nome e o ícone da marca, e não o losango do Vite — forçando recarga sem cache, que o navegador retém favicon (Risks)

## 7. Conferência visual

- [x] 7.1 Conferir a barra lateral nos dois esquemas: a cabeça legível a 24px e na tinta — escura no claro, `#e9eaec` no escuro —, com o acento aparecendo só no item de navegação ativo (D10). A premissa original desta tarefa era comparar dois azuis vizinhos (D4); a conferência mostrou que o problema era azul demais, não tom divergente
- [x] 7.2 Conferir a tela de login nos dois esquemas: duotone construindo forma, cabeça na tinta do esquema, respiro do manual (metade da largura da cabeça) preservado em volta
- [x] 7.3 Conferir que a marca troca de cor ao alternar o tema sem recarregar a página (requisito de `frontend-brand-identity`)

## 8. Documentação

- [x] 8.1 Registrar a entrada no `CHANGELOG.md`
- [ ] 8.2 Avisar quem mantém o pacote de marca sobre a divergência do acento escuro — `#5b8fd4` no `README.md` contra `#6495db` no tema (D4, Open Questions)
