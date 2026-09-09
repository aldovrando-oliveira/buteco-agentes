## Why

`apps/api` entrega o catálogo de bases de conhecimento inteiro desde
`2026-09-07-knowledge-base-catalogo-documentos` — seis rotas, criação, edição e
ativação — e `apps/frontend` não tem nenhuma tela para ele. Hoje uma base de
conhecimento só existe por chamada HTTP direta, o que significa que a descrição
da base — o texto que o modelo lê para decidir se a base é relevante para a
pergunta — é escrita por quem tem `curl`, não por quem opera o produto.

Esta é a etapa **5a-1** da linha de bases de conhecimento: o catálogo na UI,
lista e detalhe. É a primeira etapa de UI da linha, e a convenção 1 a coloca
depois do catálogo e do vínculo no backend, que já estão prontos.

## What Changes

- **Lista de bases** (`/knowledge-bases`) em `apps/frontend`: tabela com nome,
  descrição, **quais agentes consultam a base** e estado, busca com normalização
  de acentos e filtro por estado, no molde de `frontend-listas-busca-e-colunas`.
  Busca e filtro rodam no cliente — `GET /knowledge-bases` não tem busca nem
  paginação, dívida já assumida.
- **Detalhe da base** (`/knowledge-bases/{id}`) em `apps/frontend`: nome,
  descrição e estado, com `Editar`, `Ativar` e `Desativar`. A descrição ganha
  card próprio, rotulado como o texto que o modelo lê — não é subtítulo de
  página, de propósito.
- **Card "agentes que consultam esta base"** no detalhe, derivado no cliente a
  partir de `GET /agents`. **Entrou em escopo durante a conferência manual**
  (design.md, D20): estava fora por sequenciamento, e a conferência mostrou que
  a dependência já estava satisfeita — `AgentResponse.KnowledgeBases` é populado
  desde `knowledge-base-vinculo-agente`, então custa uma requisição, não N.
- **Criação e edição** (`/knowledge-bases/new`, `/knowledge-bases/{id}/edit`) em
  `apps/frontend`, com nome e descrição obrigatórios e não vazios, como a API já
  impõe.
- **Quarto item na navegação lateral**, `Conhecimento`, em `apps/frontend`.
- **Estados vazios explícitos**: catálogo sem nenhuma base, e o detalhe sem a
  área de documentos.
- **Nenhuma mudança em `apps/api`, `apps/workers` ou `apps/inbox`.** A etapa
  consome apenas rotas existentes.

Fora de escopo, e cada um com o motivo: gestão de documentos (5a-2, depende da
etapa 2 de indexação), aba de vínculo no detalhe do agente (5b), diagnóstico do
índice (5c).

**Duas colunas que o protótipo pede não entram**, porque o dado não existe:
`Documentos` e `Indexação` — as duas exigiriam uma requisição por base, com 100+
bases declaradas pelo handoff. `Consultada por` entra, porque sai de **uma**
requisição a `GET /agents` (D21). A conferência protótipo × código está no
`design.md`, com as decisões D1, D9, D20 e D21; o handoff que elas geram para a
etapa 2 vai para `02-HISTORICO_E_STATUS.md`.

## Capabilities

### New Capabilities
- `knowledge-base-catalog-ui`: o catálogo de bases de conhecimento no painel do
  operador — listagem com busca e filtro por estado, detalhe, criação, edição,
  ativação e desativação, e a apresentação da descrição como texto de runtime
  lido pelo modelo, não como texto decorativo de UI.

### Modified Capabilities

Nenhuma.

O quarto item de navegação **não** modifica `frontend-app-shell`: a requisição
"Navegação com ícone e estado ativo por grupo de rotas" já está escrita de forma
genérica — "um item para cada área do painel que possua página real" — então uma
área nova é satisfeita pela requisição existente, não uma mudança nela. Os
cenários de ícone, rótulo acessível e estado ativo em rota profunda passam a
valer para `Conhecimento` sem reescrita.

## Impact

**Apenas `apps/frontend`.** Nenhum arquivo de `apps/api`, `apps/workers` ou
`apps/inbox` é tocado, e nenhuma referência de projeto entre apps é criada.

- **Nova feature** `src/features/knowledge-bases/` (`api/`, `components/`,
  `pages/`, `types/`), na anatomia de `src/features/mcp-servers/`. Cliente HTTP
  próprio, com `request<T>`/`ApiError` finos importando `src/auth/token` —
  convenção 7, sem cliente compartilhado entre features.
- **Modificados**: `src/app/routes.tsx` (quatro rotas novas no grupo protegido),
  `src/components/layout/AppShell.tsx` (item de navegação + ícone),
  `src/components/layout/DetailHeader.tsx` (prop `showDescription`, D17) e
  `src/features/agents/types/agent.ts` (campo `knowledgeBases`, que o tipo do
  frontend ainda não tinha — 22 fixtures de teste em 21 arquivos acompanham).
- **Reusados sem alteração**: `components/data/SectionedCard`,
  `components/data/SectionLabel`, `components/layout/DetailHeader`,
  `components/layout/BackLink`, e a aparência de badge declarada no tema.
- **Rotas consumidas** (todas existentes): `GET /agents` (só para derivar quem
  consulta cada base), `GET /knowledge-bases`,
  `GET /knowledge-bases/{id}`, `POST /knowledge-bases`,
  `PUT /knowledge-bases/{id}`, `POST /knowledge-bases/{id}/activate`,
  `POST /knowledge-bases/{id}/deactivate`.
- **Dependência nova**: nenhuma. O ícone sai de `lucide-react`, já no
  `package.json`.
- **Verificação**: a suíte cobre contrato, não aparência (convenção 14), então a
  conferência manual nos dois esquemas de cor é tarefa própria e iterativa.
- **Dívida registrada, não introduzida**: busca e filtro no cliente sobre a
  resposta inteira. O handoff informa volume real de 100+ bases; com esse número
  a ausência de `GET /knowledge-bases?q=` e paginação passa a incomodar, e o
  pedido ao backend fica registrado como handoff.
