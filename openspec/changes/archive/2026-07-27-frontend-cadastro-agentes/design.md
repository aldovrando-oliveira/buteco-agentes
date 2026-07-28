## Context

`apps/frontend` é hoje o scaffold puro do Vite + React 19 + TypeScript +
Mantine 9 (`react@19.2.7`, `@mantine/*@9.4.2`, `vite@8.1.1`,
`typescript@~6.0.2`): um `AppShell` estático sem roteamento, sem chamadas
HTTP e sem framework de teste. `apps/api` já expõe o catálogo de agentes
(`POST /agents`, `GET /agents`, `GET /agents/{id:guid}`, ver
`Agents/Endpoints/AgentEndpoints.cs`) com `AgentResponse` serializado
camelCase (`{ id, name, instructions, createdAt, updatedAt }`) e validação
server-side de `name`/`instructions` obrigatórios via
`TypedResults.ValidationProblem`. Não há CORS configurado em `apps/api`
hoje — qualquer chamada de um browser em `http://localhost:5173` (porta
default do Vite) falha.

`tsconfig.app.json` usa `verbatimModuleSyntax: true` (exige `import type`
para importações somente-de-tipo) e `include: ["src"]` — qualquer arquivo
novo em `src/` entra automaticamente no escopo do `tsc -b` do build.

Versões verificadas no npm nesta data (2026-07-26), não assumidas de
conhecimento de treinamento:

| Pacote | Versão fixada | Observação |
| --- | --- | --- |
| `react-router` | `^8.3.0` | latest; `react-router-dom` está parado em `7.18.1` desde a fusão dos pacotes no v7 — não usar |
| `@tanstack/react-query` | `^5.101.4` | latest da linha 5.x |
| `vitest` | `^4.1.10` | latest; peer `vite` aceita `^6 \|\| ^7 \|\| ^8` — compatível com `vite@8.1.1` já usado |
| `@testing-library/react` | `^16.3.2` | latest |
| `@testing-library/jest-dom` | `^7.0.0` | latest; expõe entrypoint `@testing-library/jest-dom/vitest` |
| `@testing-library/user-event` | `^14.6.1` | latest |
| `jsdom` | `^29.1.1` | ambiente de teste do Vitest |
| `@mantine/core`/`form`/`hooks`/`notifications` | `^9.4.2` | já é a versão em uso — sem bump necessário |

## Goals / Non-Goals

**Goals:**
- Consumir os três endpoints de agentes já existentes via uma UI organizada
  por feature, sem `App.tsx` monolítico.
- Trocar o tema default para light com toggle claro/escuro de 2 estados.
- Introduzir roteamento e estado de servidor como infraestrutura reutilizável
  para features futuras (mcp-servers, inboxes), não só para agentes.
- Introduzir Vitest + Testing Library com cobertura mínima real (não
  decorativa).
- Habilitar CORS mínimo em `apps/api`, configurável, para o dev server do
  Vite.

**Non-Goals:**
- Edição ou exclusão de agente (endpoints não existem em `apps/api`).
- Login/autenticação de qualquer tipo.
- Vínculo de MCP/tools por agente (não existe no backend).
- Testes E2E (Playwright ou similar) — só componente/hook via Vitest.
- Qualquer mudança de domínio, contrato de resposta ou nova entidade em
  `apps/api` além da política de CORS.
- Suporte a múltiplos ambientes de CORS além de dev local (produção fica
  para quando `apps/frontend` tiver um destino de deploy definido).

## Decisions

### 1. Roteamento: `react-router` em modo declarativo (`BrowserRouter`/`Routes`/`Route`)

Não modo *data* (`createBrowserRouter`) nem *framework* (plugin Vite,
SSR/file-based routing). Como todo o data-fetching já é feito por
`@tanstack/react-query` (não por loaders/actions do router), o modo
declarativo é suficiente e evita acoplar navegação a data-fetching — mantém
os dois times de responsabilidade (routing vs. server state) separados, o
que facilita adicionar `mcp-servers`/`inboxes` depois sem reabrir o router
core.

`app/router.tsx`:
```tsx
<BrowserRouter>
  <Routes>
    <Route element={<AppShell />}>
      <Route index element={<Navigate to="/agents" replace />} />
      <Route path="agents">
        <Route index element={<AgentListPage />} />
        <Route path="new" element={<AgentCreatePage />} />
        <Route path=":id" element={<AgentDetailPage />} />
      </Route>
    </Route>
  </Routes>
</BrowserRouter>
```
A rota raiz (`/`) redireciona para `/agents` — não existe uma home própria
nesta fatia, e criar uma só para não deixar `/` vazia seria antecipar
escopo.

**Alternativa considerada**: modo *data* com `createBrowserRouter` — rejeitada
porque introduziria dois lugares concorrentes para orquestrar
loading/erro/refetch (loaders do router vs. hooks do react-query), e o
brief já define react-query como a única fonte de estado de servidor.

### 2. Estado de servidor: um hook por operação, chave de query única por recurso

`features/agents/api/agentsApi.ts` expõe funções `fetch`-puras e tipadas
(`listAgents`, `getAgent`, `createAgent`) — sem `axios`, para não introduzir
uma dependência HTTP quando `fetch` nativo já cobre o caso de uso (3
chamadas simples, sem interceptors/retry customizado). Lê a base URL de
`import.meta.env.VITE_API_BASE_URL`. Em erro HTTP, lança uma `ApiError`
tipada carregando `status` e o `ValidationProblemDetails` já parseado
(quando presente), para que a camada de UI decida como exibir.

`features/agents/api/useAgents.ts` expõe:
- `useAgentsQuery()` → `queryKey: ['agents']`
- `useAgentQuery(id)` → `queryKey: ['agents', id]`
- `useCreateAgentMutation()` → em `onSuccess`: `setQueryData(['agents', created.id], created)`
  (evita um GET redundante ao navegar para o detalhe) + `invalidateQueries({queryKey: ['agents']})`
  (mantém a lista consistente na próxima visita).

Página nunca importa `agentsApi.ts` diretamente — só os hooks. Componente
apresentacional nunca importa `api/` nem hooks de query — só recebe dados e
callbacks via props. Essa borda é o que permite trocar a camada HTTP (ex.:
paginação, cache mais sofisticado) sem tocar em nenhum componente visual.

### 3. Formulário: `@mantine/form` com validação client-side espelhando a regra do servidor, mais mapeamento de erro 400

Validação client-side (`name`/`instructions` obrigatórios) evita um round
trip óbvio, mas a fonte de verdade continua sendo o servidor: no
`onError` da mutation, se o erro for uma `ApiError` com `status === 400` e
corpo `ValidationProblemDetails`, os erros por campo são aplicados via
`form.setFieldError('name', ...)` / `form.setFieldError('instructions', ...)`;
qualquer outro erro (rede, 5xx) vira uma notificação genérica via
`@mantine/notifications`. Sucesso também notifica e faz `navigate(`/agents/${created.id}`)`.

### 4. Tema: `<ColorSchemeScript defaultColorScheme="light"/>` oficial + toggle sem biblioteca de ícones nova

Substitui o script manual em `index.html` (hoje reimplementa, à mão, o que o
componente oficial do Mantine já faz — leitura de
`localStorage['mantine-color-scheme-value']`, fallback e escrita do atributo
`data-mantine-color-scheme` antes do primeiro paint). `MantineProvider`
passa a usar `defaultColorScheme="light"`. O toggle usa
`useMantineColorScheme()` (`colorScheme`, `setColorScheme`) num `ActionIcon`
no `Header`, alternando só entre `'light'`/`'dark'` (sem terceiro estado
`'auto'`, como decidido). Persistência e anti-flash continuam 100% a cargo
do Mantine — nenhuma lógica de storage escrita à mão.

`src/theme.ts` nasce como `createTheme({})` — sem overrides ainda — só para
existir como ponto único de customização quando a primeira necessidade real
aparecer (não adivinhar tokens agora).

**Ícone do toggle**: SVG inline (sol/lua, ~24x24) dentro do próprio
componente do toggle, não uma biblioteca de ícones (`@tabler/icons-react`
ou similar). O projeto não tem nenhuma dependência de ícones hoje e este é
o único uso; adicionar um pacote inteiro por dois glyphs estáticos não se
paga. Se uma segunda necessidade de ícone aparecer, essa decisão é revisada.

### 5. Navegação: remover itens sem página, não ocultar/desabilitar

`AppShell.tsx` perde `Dashboard` e `Inboxes` do array de nav — não existe
página nenhuma para eles nem nesta fatia nem hoje. Um `NavLink` desabilitado
ou "em breve" sinalizaria a existência de uma feature que não está no
roadmap confirmado; é mais simples reintroduzi-los quando a página existir
de fato. Só `Agents` fica, com `component={Link} to="/agents"` e
`active` calculado a partir de `useLocation()`.

### 6. Testes: `vitest` integrado ao `vite.config.ts`, `globals: false`, tsconfig dedicado

Um único `vite.config.ts` usando `defineConfig` de `vitest/config` (que
reexporta o `defineConfig` do Vite estendido com o campo `test`), em vez de
um `vitest.config.ts` separado — evita duas configs de plugin divergindo.
`test.environment: 'jsdom'`, `test.setupFiles: ['./src/test/setup.ts']`
(importa `@testing-library/jest-dom/vitest`, que aumenta o `expect` do
Vitest via *side effect*, sem precisar declarar `vitest/globals` em
nenhum `types[]`). `test.globals: false` — cada arquivo de teste importa
`describe`/`it`/`expect`/`vi` explicitamente de `'vitest'`, para não
precisar adicionar `"vitest/globals"` a `tsconfig.app.json` (que hoje é a
mesma config usada pelo `tsc -b` do `build`).

Para manter o `build` rápido e sem misturar o type-check de teste com o de
produção, `tsconfig.app.json` ganha `exclude: ["src/**/*.test.ts",
"src/**/*.test.tsx", "src/test/**"]` e um novo `tsconfig.vitest.json`
(referenciado em `tsconfig.json`, mesmo padrão já usado para
`tsconfig.node.json`) cobre só os arquivos de teste. Testes ficam
colocados ao lado do código (`Componente.test.tsx` na mesma pasta), padrão
mais comum em Testing Library e mais fácil de manter sincronizado que uma
árvore `__tests__/` espelhada.

Cobertura mínima desta fatia (não exaustiva):
- `features/agents/api/useAgents.ts`: cada hook com sucesso e erro
  (`QueryClientProvider` de teste + `fetch` mockado).
- `features/agents/components/AgentForm.tsx`: submit válido chama o
  callback; submit vazio mostra erro de validação sem chamar o callback.
- `features/agents/pages/AgentListPage.tsx`: estado de loading, lista
  renderizada, estado de erro.

### 7. CORS em `apps/api`: `CorsOptions` seguindo o padrão de `RabbitMqOptions`

```csharp
// Options/CorsOptions.cs
public sealed class CorsOptions
{
    public const string SectionName = "Cors";
    public string[] AllowedOrigins { get; set; } = [];
}
```
`Program.cs` chama `builder.Services.AddCors(...)` lendo `CorsOptions` da
configuração e `app.UseCors()` antes do mapeamento dos endpoints (depois de
`UseHttpsRedirection()`). `appsettings.Development.json` ganha
`"Cors": { "AllowedOrigins": ["http://localhost:5173"] }`;
`appsettings.json` (base, sem ambiente) fica com `AllowedOrigins: []` —
nenhuma origin liberada por padrão fora do dev explícito, para não abrir
CORS "por acidente" num ambiente futuro sem configuração própria.

**Alternativa considerada**: uma única string `Cors:AllowedOrigin` —
rejeitada porque `RabbitMqOptions` e o resto do projeto já usam esse
padrão de Options com tipos simples, e um array custa nada a mais hoje
enquanto já cobre o caso (comum) de existir mais de um origin de dev/preview
no futuro sem precisar de outra mudança de schema de configuração.

### 8. Estrutura de pastas final

```
apps/frontend/
├── .env.example                        (NOVO — documenta VITE_API_BASE_URL)
├── index.html                          (MODIFICADO — <ColorSchemeScript/> oficial)
├── vite.config.ts                      (MODIFICADO — + campo `test` via vitest/config)
├── tsconfig.json                       (MODIFICADO — + referência a tsconfig.vitest.json)
├── tsconfig.app.json                   (MODIFICADO — exclude de arquivos de teste)
├── tsconfig.vitest.json                (NOVO)
└── src/
    ├── app/
    │   ├── router.tsx                  (NOVO)
    │   └── queryClient.ts              (NOVO)
    ├── components/
    │   └── layout/
    │       ├── AppShell.tsx            (MODIFICADO — <Outlet/>, nav só "Agents", toggle no Header)
    │       └── AppShell.test.tsx       (NOVO)
    ├── features/
    │   └── agents/
    │       ├── api/
    │       │   ├── agentsApi.ts        (NOVO)
    │       │   ├── useAgents.ts        (NOVO)
    │       │   └── useAgents.test.ts   (NOVO)
    │       ├── components/
    │       │   ├── AgentTable.tsx      (NOVO)
    │       │   ├── AgentForm.tsx       (NOVO)
    │       │   ├── AgentForm.test.tsx  (NOVO)
    │       │   └── AgentDetailCard.tsx (NOVO)
    │       ├── pages/
    │       │   ├── AgentListPage.tsx   (NOVO)
    │       │   ├── AgentListPage.test.tsx (NOVO)
    │       │   ├── AgentCreatePage.tsx (NOVO)
    │       │   └── AgentDetailPage.tsx (NOVO)
    │       └── types/
    │           └── agent.ts            (NOVO)
    ├── test/
    │   └── setup.ts                    (NOVO)
    ├── theme.ts                        (NOVO)
    ├── main.tsx                        (MODIFICADO — QueryClientProvider, RouterProvider/BrowserRouter, defaultColorScheme="light")
    └── App.tsx                         (REMOVIDO — substituído pela árvore de rotas em app/router.tsx)

apps/api/src/Buteco.Api/
├── Options/
│   └── CorsOptions.cs                  (NOVO)
├── Program.cs                          (MODIFICADO — AddCors/UseCors)
├── appsettings.json                    (MODIFICADO — Cors:AllowedOrigins: [])
└── appsettings.Development.json        (MODIFICADO — Cors:AllowedOrigins: ["http://localhost:5173"])
```

Nenhum arquivo de `apps/workers` é tocado; nenhum import cruzado entre
`apps/api` e `apps/frontend`.

## Risks / Trade-offs

- **[Risco] `App.tsx` remover é uma mudança visível para qualquer um com
  branch aberto tocando nele.** → Mitigação: escopo pequeno e já revisado
  no proposal; `git log`/`git blame` não mostram nenhum trabalho recente
  além do scaffold inicial.
- **[Risco] CORS mal configurado (origin errada ou ausente) quebra
  silenciosamente todas as chamadas do frontend em dev, com erro só visível
  no console do browser.** → Mitigação: `.env.example` documenta
  `VITE_API_BASE_URL` e o design fixa a origin default de dev
  (`http://localhost:5173`) diretamente em `appsettings.Development.json`,
  então o caminho feliz (`docker compose up` + `dotnet run` + `npm run dev`
  nas portas default) funciona sem nenhuma configuração manual adicional.
- **[Risco] `tsconfig.app.json` com `exclude` de testes pode divergir do
  `tsconfig.vitest.json` e deixar arquivo de teste fora de ambos (sem
  type-check nenhum).** → Mitigação: `tsconfig.vitest.json` usa
  `include: ["src/**/*.test.ts", "src/**/*.test.tsx", "src/test/**"]` —
  exatamente o inverso do `exclude` do app, cobrindo o mesmo conjunto de
  arquivos por construção.
- **[Trade-off] Validação client-side duplica a regra "nome e instruções
  obrigatórios" que já existe no servidor.** → Aceito: é uma regra trivial
  (não-vazio) e a UX de erro imediato no formulário vale a duplicação; o
  servidor continua sendo a fonte de verdade (mapeamento de erro 400 cobre
  qualquer divergência).
- **[Trade-off] `react-router` em modo declarativo não dá acesso a
  loaders/actions.** → Aceito: nenhuma tela desta fatia precisa de
  data-fetching amarrado à navegação (ex.: prefetch antes de renderizar) —
  `@tanstack/react-query` já cobre loading/erro/refetch.

## Migration Plan

Não há dado existente para migrar (nenhum usuário real de
`apps/frontend` ainda — README do monorepo já documenta que o frontend
"ainda não consome o backend"). Deploy é local/dev nesta fatia:

1. `apps/api`: aplicar as mudanças de CORS primeiro (`CorsOptions`,
   `Program.cs`, appsettings) — endpoint continua funcionando para
   qualquer cliente não-browser sem nenhuma mudança de contrato.
2. `apps/frontend`: instalar dependências novas, aplicar a reestruturação
   de pastas e as três telas, rodar `npm run lint`, `npm run format:check`
   e a suíte Vitest antes de considerar a fatia pronta.
3. Validar manualmente o fluxo ponta a ponta: `docker compose up -d`
   (Postgres/RabbitMQ) → `dotnet run` em `apps/api` → `npm run dev` em
   `apps/frontend` → criar um agente pela UI → conferir que aparece na
   lista e no detalhe.

Rollback: reverter o commit/branch da mudança — não há migração de banco
nem dado persistido a desfazer.

## Open Questions

- Nenhuma pendência de produto/negócio identificada para este escopo — os
  pontos de ambiguidade levantados durante a exploração (nav placeholders,
  `theme.ts`, toggle de tema, formato do CORS) já foram decididos com o
  usuário e estão registrados nas Decisions acima.
