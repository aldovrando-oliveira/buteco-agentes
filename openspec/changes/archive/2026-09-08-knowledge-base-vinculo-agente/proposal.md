## Why

A etapa 1 (`knowledge-base-catalogo-documentos`) entregou bases e documentos,
mas nenhuma base pertence a agente nenhum: o catálogo existe e não há como
dizer *quais* bases um agente pode consultar. Sem isso não há o que resolver na
etapa de execução — o resolvedor de tool da etapa 4 precisa de um conjunto por
agente, e hoje esse conjunto não existe em lugar nenhum.

Esta é a **etapa 3 de 5** da linha de bases de conhecimento (catálogo → vínculo
→ execução → UI, convenção 1). Ela **não depende da etapa 2 (indexação)**: o
vínculo só precisa que `KnowledgeBase` exista, e existe desde a etapa 1. Quem
depende de indexação é a etapa 4.

## What Changes

- **`apps/api` ganha o vínculo N:N entre `Agent` e `KnowledgeBase`**
  (`AgentKnowledgeBase`: `AgentId`, `KnowledgeBaseId`, e **nada mais**).
  Tabela relacional, não jsonb, porque os dois lados são entidades catalogadas
  com identidade própria (convenção 2). Sem nenhuma coluna extra: sem análogo
  de `AgentMcpServer.AllowedTools` (seleção de documentos permitidos), sem
  `TopK` por vínculo, sem flag "injetar sempre" — decisões já fechadas nas
  explorações anteriores, implementadas aqui, não reavaliadas.
- **Uma rota só: `PUT /agents/{id}/knowledge-bases`**, substituição integral do
  conjunto (replace-all, tudo-ou-nada), no mesmo idioma dos dois lotes que já
  existem no repositório. Responde `AgentResponse` completo, como os dois
  precedentes.
- **`AgentResponse` ganha `knowledgeBases`** (id + nome por item, mesmo nível
  de detalhe de `mcpServers` e `delegatesTo`), servido por **todas** as
  respostas de agente: listagem, consulta por id, criação, atualização,
  ativação/desativação e os dois `PUT` de vínculo já existentes.
- **`apps/workers` ganha só o espelho de EF Core** da entidade nova e a
  migração equivalente — nenhum resolvedor, nenhuma tool, nenhuma leitura em
  runtime. É o mesmo tratamento que a etapa 1 deu às duas entidades dela.
- **Base inativa continua vinculável e editável.** O filtro por `IsActive`
  pertence à *resolução* (`where kb.IsActive`, idioma do `McpToolSetResolver`),
  não à remoção do vínculo. Nesta etapa não há resolução, então essa metade é
  **contrato declarado no `design.md`**, herdado decidido pela etapa 4 — não
  requisito sem gatilho verificável aqui (o padrão de falha que a convenção 10
  nomeia, e que a etapa 1 já evitou com as garantias de reindexação).
- **Sem rota inversa** (`GET /knowledge-bases/{id}/agents`). A projeção
  original previa "replace + list"; a verificação mostrou que a visão inversa
  do MCP — `McpServerAgentsCard`, "agentes que usam este servidor" — é derivada
  **no cliente** a partir de `GET /agents`, sem rota de API. Como `GET /agents`
  passa a carregar `knowledgeBases`, a mesma derivação serve a etapa 5b.

Fora de escopo, explicitamente: nenhuma tool, nenhum resolvedor em
`apps/workers`, nenhuma UI, nenhum fragmento, nenhum embedding.

## Capabilities

### New Capabilities

- `agent-knowledge-binding`: cadastro/persistência do vínculo N:N entre agente
  e base de conhecimento em `apps/api` — substituição integral do conjunto,
  atomicidade da rejeição, deduplicação silenciosa, vínculo com base inativa, e
  durabilidade em PostgreSQL. Capability própria pelo mesmo motivo que
  `agent-mcp-binding` e `agent-delegation-binding` são separadas dos catálogos
  que ligam: o vínculo tem contrato próprio, e a execução que o consome é outra
  capability, de outra etapa.

### Modified Capabilities

- `agent-catalog`: as respostas de listagem e de consulta por id passam a
  incluir `knowledgeBases` (id + nome de cada base vinculada), com o par
  "com item"/"sem item" — lista vazia, nunca nula. É requisito de spec, não
  detalhe de implementação: é o campo que a etapa 5b lê.

## Impact

- **`apps/api`**: uma entidade de vínculo (`AgentKnowledgeBase`), um comando
  CQRS (`ReplaceAgentKnowledgeBases`), um lookup compartilhado, um
  `KnowledgeBaseSummaryResponse`, um grupo de endpoints com **uma** rota, e uma
  migração. **Blast radius verificado**: `AgentResponse.FromEntity` tem **8
  sites de construção existentes** (`ListAgents`, `GetAgentById`,
  `CreateAgent`, `UpdateAgent`, `ActivateAgent`, `DeactivateAgent`,
  `ReplaceAgentMcpServers`, `ReplaceAgentDelegations`), todos tocados; o handler
  novo desta change é o **nono** site, e o décimo ponto é
  `AgentResponseWireFormatTests`, que constrói o record posicionalmente e
  **quebra a compilação** se o campo novo não for adicionado lá.
- **`apps/workers`**: apenas o espelho de EF Core da entidade e a migração
  equivalente, mantida sincronizada por disciplina e verificada por teste de
  schema contra Testcontainer. **Nenhum** código de runtime lê a tabela nesta
  etapa; a migração de `apps/workers` continua sem nenhum caminho de aplicação
  em runtime (regra operacional registrada em `02-HISTORICO_E_STATUS.md`, com o
  `42P07` já reproduzido).
- **`apps/frontend`**: nenhuma mudança. Verificado: `Agent` é interface TS sem
  validação de schema em runtime (sem zod/valibot), então campo novo na
  resposta é aditivo e ignorado. A quarta aba é a etapa 5b.
- **`apps/inbox`**: nenhuma mudança; banco próprio, sem tabela em comum.
- **Rotas**: a rota nova é autenticada por padrão — nenhuma entrada nova na
  allowlist de rotas anônimas (`AnonymousRouteClassification`).
- **Dependências**: nenhum pacote novo, nenhuma versão de runtime ou framework
  alterada.
- **Sequenciamento**: independente da etapa 2 e de `0b` (head-to-head
  semântico, bloqueado por chave de provedor junto de R7/R8). `0b` bloqueia a
  *proposta* da etapa 2, não esta. A etapa 4 depende das duas.
