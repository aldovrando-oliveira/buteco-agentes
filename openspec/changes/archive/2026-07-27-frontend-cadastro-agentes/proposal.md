## Why

`apps/frontend` hoje é só o scaffold inicial do Vite (`AppShell` estático, sem
rotas, sem chamadas HTTP) e `apps/api` já expõe o catálogo de agentes
(`POST /agents`, `GET /agents`, `GET /agents/{id}`) sem nenhuma UI que o
consuma. Sem uma interface, cadastrar e consultar agentes só é possível via
HTTP direto (curl/Postman), o que trava qualquer avaliação end-to-end do
produto. Esta mudança fecha esse ciclo com o menor escopo possível: as três
telas que espelham exatamente os endpoints que já existem, mais a
infraestrutura mínima (roteamento, estado de servidor, tema, CORS) para
sustentar telas futuras sem reescrever a base.

## What Changes

- Adiciona roteamento client-side (`react-router`, modo declarativo/SPA, sem
  framework mode) com `app/router.tsx`, e o `AppShell` passa a renderizar
  `<Outlet/>` em vez de receber `children` estático.
- Adiciona `@tanstack/react-query` como camada de estado de servidor
  (`app/queryClient.ts`) para list/detail/create de agentes, com
  loading/erro/refetch automático após criar.
- Introduz a feature `features/agents/` (api tipada, hooks de query/mutation,
  componentes apresentacionais, páginas) com três telas novas:
  - Lista de agentes (`GET /agents`) com link para cada detalhe e botão de
    criação.
  - Criação de agente (`POST /agents`) via `@mantine/form`, com feedback de
    sucesso/erro por `@mantine/notifications` e redirect para o detalhe do
    agente criado.
  - Detalhe de agente (`GET /agents/{id}`), somente leitura.
- Reestrutura `AppShell.tsx`: remove os itens de navegação sem página
  (`Dashboard`, `Inboxes`), mantém só `Agents` linkado à rota real.
- Troca o tema default de `auto`/dark para light, com um toggle claro/escuro
  de 2 estados (`ActionIcon` no Header via `useMantineColorScheme()`) —
  substitui o script manual de color-scheme em `index.html` pelo
  `<ColorSchemeScript/>` oficial do Mantine. Introduz `src/theme.ts` como
  ponto único de customização do tema (hoje vazio).
- Introduz Vitest + Testing Library como primeiro framework de teste do
  frontend, com cobertura mínima de componente/hook (sem E2E).
- **BREAKING** (infraestrutura de dev, não runtime): `apps/api` passa a
  exigir uma origin configurada em `Cors:AllowedOrigins` para aceitar
  requisições de um browser; sem essa configuração, chamadas do
  `apps/frontend` em dev param no CORS. Habilita `AddCors`/`UseCors` com a
  origin do Vite dev server lida de configuração (nunca hardcoded).
- Adiciona `apps/frontend/.env.example` documentando `VITE_API_BASE_URL`.

## Capabilities

### New Capabilities
- `agent-catalog-ui`: interface web (apps/frontend) para listar, cadastrar e
  consultar o detalhe de agentes cadastrados via `apps/api`, incluindo os
  estados de carregamento/erro/sucesso de cada operação.

### Modified Capabilities
- `frontend-scaffold`: o `AppShell` deixa de ser uma tela estática e passa a
  compor rotas (`<Outlet/>`, navegação real); o tema default deixa de ser
  `auto`/dark e passa a ser light com toggle claro/escuro.

## Impact

- **apps/frontend**: novas dependências de runtime (`react-router`,
  `@tanstack/react-query`) e de desenvolvimento (`vitest`,
  `@testing-library/react`, `jsdom`, e afins); `App.tsx` é substituído pela
  árvore de rotas; `AppShell.tsx`, `main.tsx` e `index.html` são alterados;
  novo diretório `src/features/agents/`; novo `src/theme.ts`; novo
  `.env.example`.
- **apps/api**: `Program.cs` ganha `AddCors`/`UseCors`; nova
  `Options/CorsOptions.cs`; `appsettings.Development.json` ganha a origin do
  Vite dev server. Nenhuma mudança de domínio, endpoint ou contrato de
  resposta.
- **Dependências externas**: nenhuma migração de banco, nenhuma mudança em
  `apps/workers`.
- **Non-goals explícitos**: sem edição/exclusão de agente (API não expõe
  esses endpoints ainda), sem UI de login/autenticação, sem vínculo de
  MCP/tools por agente (não existe no backend ainda), sem testes E2E
  (Playwright ou similar).
