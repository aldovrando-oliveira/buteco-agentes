## Context

`apps/frontend/src/app/router.tsx` tem hoje um único componente,
`AppRouter`, que devolve `<BrowserRouter>` envolvendo um `<Routes>` com
toda a árvore da aplicação: a rota pública `login`, e abaixo de
`ProtectedRoute` e `AppShell`, os três grupos de feature (`agents`,
`mcp-servers`, `channels`) mais o redirect da raiz. `main.tsx` só monta
`<AppRouter />` dentro dos providers do Mantine e do React Query.

Esse é o "declarative mode" do `react-router`. O "data mode"
(`createBrowserRouter` + `RouterProvider`) é o mesmo roteador com um
router object explícito, que mantém estado de navegação próprio e habilita
as APIs que dependem dele: `useBlocker`, `useNavigation`, `loader`,
`action`, `useRevalidator`. Desta change interessa **apenas** o primeiro.

Já existe `app/router.test.tsx`, com quatro casos que renderizam
`<AppRouter />` de verdade e verificam redirect da raiz, proteção por
token e permanência do `AppShell` ao navegar. Ele manipula o histórico
real do jsdom (`window.history.pushState` no `beforeEach`) e afirma
`window.location.pathname`. Esse acoplamento com o histórico global é o
que força a Decision 3.

O `frontend-scaffold` fixa hoje, com essas palavras, roteamento "em modo
declarativo" — é a única spec do repositório que menciona o modo, e por
isso é a única modificada aqui.

## Goals / Non-Goals

**Goals:**
- Deixar `apps/frontend` em data mode, de forma que `useBlocker` funcione
  para as features.
- Não mudar nenhum comportamento observável de navegação.
- Deixar a árvore de rotas montável fora do browser, para que testes
  (deste e dos próximos changes) exercitem as rotas reais da aplicação.

**Non-Goals:**
- Nenhum `loader`, `action`, `errorElement` ou `HydrateFallback`. O
  data-layer continua sendo React Query em cada página, sem exceção
  (o data mode habilita loaders, não os obriga).
- Nenhum hook de guarda de rascunho sujo — a API dele é moldada pelo
  consumidor, que só existe na change seguinte (Decision 4).
- Nenhuma rota nova, removida ou renomeada; nenhum redirect novo.
- Nenhuma mudança em `main.tsx`, no `AppShell`, no `ProtectedRoute` ou em
  qualquer página.
- Nenhuma biblioteca nova.

## Estrutura de pastas proposta

```
apps/frontend/src/app/
├── ProtectedRoute.tsx        # inalterado
├── ProtectedRoute.test.tsx   # inalterado (monta MemoryRouter próprio)
├── queryClient.ts            # inalterado
├── routes.tsx                # novo — appRoutes: RouteObject[]
├── router.tsx                # createBrowserRouter(appRoutes) + RouterProvider
└── router.test.tsx           # createMemoryRouter(appRoutes, ...)
```

Nenhum arquivo fora de `src/app/`.

## Decisions

### Decision 1: `createRoutesFromElements`, não configuração por objeto

A árvore continua escrita como JSX aninhado (`<Route>` dentro de
`<Route>`), convertida para `RouteObject[]` por
`createRoutesFromElements`. O diff fica restrito ao invólucro: as mesmas
linhas de rota, na mesma ordem, com a mesma indentação.

Isso importa porque o valor desta change é ser verificável por inspeção —
"nada mudou além do modo". Reescrever quinze rotas como objetos aninhados
produziria um diff em que uma rota trocada de lugar ou um `index` perdido
passariam despercebidos.

`createRoutesFromElements` continua exportado e não está marcado como
deprecado na versão instalada, e aceita `loader`/`action` como prop de
`<Route>` — então a change seguinte pode pendurar o redirect da rota
antiga sem trocar de estilo.

**Alternativa descartada**: configuração por objeto (`[{ path, element,
children }]`). É o estilo mais comum na documentação de data mode e lê
melhor como dado, mas o ganho é estético e o custo é um diff grande numa
change cujo propósito é não mudar nada.

### Decision 2: Router em escopo de módulo, árvore de rotas em módulo separado

`createBrowserRouter` é chamado uma vez, no escopo do módulo
`router.tsx`, nunca dentro do corpo de `AppRouter`. Um router criado a
cada render perderia o estado de navegação; criado com
`useState(() => ...)` sobreviveria, mas amarraria o ciclo de vida do
router ao de um componente sem necessidade.

A consequência é que importar `router.tsx` tem efeito colateral (cria o
histórico do browser). Por isso a **árvore de rotas mora em `routes.tsx`**,
sem efeito colateral nenhum: quem quiser montar as rotas — o teste desta
change, os testes das próximas — importa `routes.tsx` e não paga o
histórico global.

**Alternativa descartada**: manter tudo em `router.tsx` e exportar
`appRoutes` de lá. Rejeitada porque todo teste que importasse as rotas
criaria também um browser router vivo e inútil, sobre o `window.location`
compartilhado do jsdom.

### Decision 3: `router.test.tsx` passa a usar `createMemoryRouter` e a afirmar conteúdo

Os quatro casos existentes são preservados em intenção — redirect da raiz,
bloqueio sem token, permanência do `AppShell` ao navegar, navegação para
canais — mas passam a montar `createMemoryRouter(appRoutes, {
initialEntries: ['/'] })` dentro de `<RouterProvider>`, e a afirmar o que
está na tela em vez de `window.location.pathname`.

A mudança não é cosmética, é obrigatória: com o router em escopo de módulo
(Decision 2), o `window.history.pushState({}, '', '/')` do `beforeEach`
deixa de reposicionar o router, que é criado uma única vez no primeiro
import e mantém a própria localização entre os testes. O primeiro caso
navegaria para `/login` e o segundo começaria de lá, com falhas em cascata
que não dizem nada sobre a aplicação.

Afirmar conteúdo renderizado também é o teste mais honesto dos dois: o
requisito é "a raiz leva o operador para uma feature existente", não "a
barra de endereços mostra tal string".

**Alternativa descartada**: manter `<AppRouter />` no teste e recriar o
módulo a cada caso com `vi.resetModules()` e import dinâmico. Rejeitada
por trocar um acoplamento (histórico global) por outro pior (ordem de
import), para preservar asserções que já eram as mais frágeis do arquivo.

### Decision 4: Nenhum hook de guarda de rascunho nesta change

`useBlocker` não é chamado em lugar nenhum aqui. A change entrega a
capacidade, não o consumidor.

O hook de guarda tem decisões de produto embutidas — se bloqueia troca de
aba além de saída de rota, o que faz com o rascunho ao confirmar, qual a
cópia do diálogo — e todas pertencem à change das abas, onde há uma tela
real para responder por elas. Um hook desenhado aqui, sem consumidor,
provavelmente teria a API errada e ainda assim precisaria de um harness
sintético para ser testado.

Consequência assumida: nesta change nenhum teste exercita `useBlocker`. A
prova de que o data mode está ativo é indireta e suficiente — a árvore
montada por `createMemoryRouter` é um data router, e a suíte inteira
continua verde.

### Decision 5: `AppRouter` mantém nome e assinatura

`main.tsx` não muda. `AppRouter` continua sendo o componente exportado,
agora com corpo de uma linha (`<RouterProvider router={router} />`).
Preservar o ponto de entrada mantém o diff dentro de `src/app/` e evita
tocar no arquivo que monta os providers.

## Risks / Trade-offs

- [Risco] Fast refresh: com o router em escopo de módulo, editar
  `routes.tsx` durante o `npm run dev` pode forçar um reload completo em
  vez de atualização parcial → [Mitigação] custo de desenvolvimento
  desprezível e comportamento padrão de qualquer app em data mode; não
  afeta build nem produção.
- [Risco] O tratamento de 401 em cada `request<T>` faz
  `window.location.href = '/login'`, que é navegação de documento e passa
  por fora do router → [Mitigação] continua funcionando exatamente como
  hoje; vira ponto de atenção só na change seguinte, quando um aviso de
  `beforeunload` puder se sobrepor a uma expiração de sessão. Registrado
  aqui para não se perder, sem ação nesta change.
- [Trade-off] A change não entrega nada visível ao operador. É custo de
  infraestrutura pago adiantado para que a change das abas seja avaliada
  só pela UI que entrega.

## Open Questions

(nenhuma)
