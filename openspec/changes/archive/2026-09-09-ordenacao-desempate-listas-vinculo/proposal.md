## Why

Oito consultas de `apps/api` ordenam sem desempate estável, e o critério que
usam não é único por construção. Nome não é único em nenhum catálogo desta base
— o `AppDbContext` de `apps/api` não tem nenhum índice único de nome (os únicos
`HasIndex` são `a2a_tasks.ContextId`, `a2a_tasks.State` e
`knowledge_documents.KnowledgeBaseId`), nenhum handler de criação valida nome
duplicado, e `knowledge-base-catalog` chega a ter o cenário *"Nome duplicado é
permitido"* como requisito. Com homônimos, a ordem entre eles fica a cargo do
plano do PostgreSQL: a mesma requisição pode responder em ordens diferentes sem
nada ter mudado no cadastro.

Carve de defeito pré-existente, no precedente já formado quatro vezes aqui
(`inbox-enums-json-string`, `crossapp-session-codec-encoder`,
`push-notification-config-codec-encoder`, `dedupe-global-nome-de-tool`) —
convenção 12: defeito pertence a quem expõe e se corrige lá, em change própria.
Feito agora porque é a única coisa da fila que não depende da chave de provedor.

**A verificação mudou o escopo em três pontos, e os três estão documentados no
`design.md` com a evidência.** O registro em `02-HISTORICO_E_STATUS.md` falava
de cinco sites; a varredura confirmou os cinco, achou **dois inéditos no mesmo
app**, e achou um **defeito de classe diferente** que o desempate por id não
conserta.

## What Changes

- **Desempate estável por `Id`** nas oito consultas de `apps/api` que alimentam
  resposta de API ordenando por critério não-único — estendendo o idioma que já
  existe no repositório (`AgentKnowledgeBaseLookup.cs:31-32` e
  `ListAgentsQueryHandler.cs:75-76` já fazem `OrderBy(Name).ThenBy(Id)`, com o
  motivo no código). Não é idioma novo: é o mesmo par, aplicado onde falta.
- **Ordenação de `mcpServers` e `delegatesTo` na listagem passa do processo para
  o banco** (`apps/api`). Hoje `GET /agents` ordena esses dois conjuntos em
  memória (comparador do .NET/ICU) enquanto `GET /agents/{id}` ordena em SQL
  (collation do PostgreSQL). São dois comparadores diferentes para a mesma
  lista, e eles **discordam de fato** — verificado nos dois runtimes reais
  (`postgres:18`, .NET 10): para `suporte-alfa` e `Suporte Alfa` o banco devolve
  `suporte-alfa` primeiro e o .NET devolve `Suporte Alfa` primeiro. O desempate
  por id não alcança esse caso, porque a divergência está no critério primário.
  Mesma correção aplicada a `knowledgeBases`, que tem o desempate desde a etapa
  3 mas ainda ordena em memória na listagem.
- **Nenhuma mudança de critério de ordenação.** Os quatro catálogos continuam
  por `CreatedAt` e os três vínculos continuam por nome — ver a correção de
  premissa no `design.md`, D1.

Sem mudança de contrato de fio, sem campo novo, sem migração, sem endpoint novo.
Nenhuma mudança é **BREAKING**: hoje a ordem entre empatados é indefinida, e
passar a defini-la não retira garantia de ninguém.

## Capabilities

### New Capabilities

- `api-response-ordering`: a garantia de que **nenhuma lista que `apps/api`
  devolve depende do plano do PostgreSQL para a sua ordem** — todo critério de
  ordenação exposto termina em desempate por identificador, e a ordem de uma
  mesma lista é a mesma nas duas superfícies que a servem (consulta por id e
  listagem).

  Capability nova, e não um requisito em cada capability de domínio, pelo mesmo
  motivo que fez `agent-tool-namespace` nascer em `dedupe-global-nome-de-tool`:
  **nenhuma capability existente é dona da união.** O defeito é um só, aparece
  em oito consultas espalhadas por cinco domínios (`agent-catalog`,
  `agent-mcp-binding`, `agent-delegation-binding`, `mcp-server-catalog`,
  `knowledge-base-catalog`, `knowledge-document-catalog`), e a decisão que o
  resolve — qual comparador é o canônico da API — é uma decisão só, que não tem
  onde morar se for repetida seis vezes.

  Há um segundo motivo, e ele é de qualidade de spec: cinco dessas seis
  capabilities estão com `Purpose` placeholder. Declará-las "Modified" por causa
  de uma linha de `ThenBy` acionaria o gatilho de `Purpose` (item aberto de
  09/09/2026) sobre cinco capabilities cujo código esta change quase não toca —
  produzindo exatamente a "prosa genérica" que o próprio item nomeia como pior
  que o placeholder. A capability nova nasce com `Purpose` real, escrito por
  quem tocou o código.

### Modified Capabilities

- `agent-knowledge-binding`: o requisito *"Bases de conhecimento vinculadas vêm
  ordenadas"* já exige nome + desempate por id **nas duas superfícies**, e é o
  único requisito de ordem que esta base tem hoje. Ele passa a dizer **qual
  comparador** define "ordem de nome" — o do banco —, porque hoje a listagem o
  cumpre com o comparador do .NET e a consulta por id com o do PostgreSQL, e os
  dois discordam. Requisito apertado, não acrescentado: a garantia prometida
  sempre foi "a mesma ordem nas duas", e ela não se sustenta com dois
  comparadores.

## Impact

**App afetado: `apps/api` apenas.** Nenhuma referência nova entre apps.
`apps/workers`, `apps/inbox` e `apps/frontend` não são tocados.

Produção (`apps/api`) — 6 arquivos, todos modificados, nenhum criado:

| Arquivo | Site(s) |
|---|---|
| `AgentDelegations/AgentDelegationLookup.cs` | 1 |
| `AgentMcpBindings/AgentMcpServerLookup.cs` | 2 |
| `Agents/Queries/ListAgents/ListAgentsQueryHandler.cs` | 3, 4, 6 e a ordenação de `knowledgeBases` |
| `KnowledgeBases/Queries/ListKnowledgeBases/ListKnowledgeBasesQueryHandler.cs` | 5 |
| `McpServers/Queries/ListMcpServers/ListMcpServersQueryHandler.cs` | 7 |
| `KnowledgeDocuments/Queries/ListKnowledgeDocuments/ListKnowledgeDocumentsQueryHandler.cs` | 8 |

Testes (`apps/api`) — 7 arquivos, todos existentes. Ver a projeção decomposta no
`design.md`.

Rotas cuja ordem de resposta passa a ser determinística: `GET /agents`,
`GET /agents/{id}`, `PUT /agents/{id}/mcp-servers`, `PUT /agents/{id}/delegations`,
`PUT /agents/{id}/knowledge-bases`, `GET /mcp-servers`, `GET /knowledge-bases`,
`GET /knowledge-bases/{id}/documents`.

**Fora de escopo, com evidência e gatilho no `design.md`** (seção "Achados fora
de escopo"): seis sites em `apps/inbox`, um em `PostgresTaskStore` de
`apps/api`, e um terceiro comparador em `apps/frontend`.
