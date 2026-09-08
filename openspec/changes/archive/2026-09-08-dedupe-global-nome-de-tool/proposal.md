## Why

O conjunto de tools oferecido ao LLM é montado em `apps/workers` como
`toolSet.Tools.Concat(delegationTools)` (`AgentExecutionService.cs:208`), mas
**nenhum dos dois lados sabe que compartilha namespace com o outro**, e o lado
MCP não deduplica nem contra si mesmo. Quando dois nomes coincidem,
`FunctionInvokingChatClient` resolve pelo primeiro match ordinal e descarta o
resto em silêncio — sem erro, sem log — enquanto as duas declarações vão no
payload para o provedor com o mesmo nome. O operador cadastra uma tool e o
agente chama outra, sem nada no log que explique.

É defeito pré-existente de MCP + delegação, alcançável hoje sem que nada da
linha de bases de conhecimento exista. Sequenciado antes da etapa 4 pelo
precedente de `inbox-enums-json-string`, `crossapp-session-codec-encoder` e
`push-notification-config-codec-encoder` (convenção 12: defeito pertence a quem
expõe, e se corrige lá, em change própria sequenciada antes).

## What Changes

- **Dedupe global no ponto de concatenação** (`apps/workers`): um
  `ToolNameDeduplicator` novo passa a ser aplicado sobre o conjunto final
  MCP + delegação, não dentro de cada resolvedor. É o único ponto que sabe que
  os dois conjuntos dividem namespace.
- **Renomeação, não descarte** na colisão — verificado como seguro: o nome
  exposto é rótulo, nunca chave de resolução de volta (ver
  `design.md`, Verificação V1). Reusa o idioma de sufixo numérico que
  `AgentDelegationToolSetResolver` já usa, agora global.
- **Ordem de operações fixada**: sanitizar → truncar em 64 → deduplicar, com o
  sufixo cabendo **dentro** dos 64. Corrige um defeito real já presente: hoje
  `$"{baseName}-{suffix}"` em `AgentDelegationToolSetResolver.cs:53` produz 66
  caracteres quando `baseName` já está no limite, estourando o limite de 64
  caracteres verificado em fonte primária (ver `design.md`, V4).
- **Dedupe MCP × MCP passa a existir** (`apps/workers`): hoje não há nenhum.
  Dois `McpServer.Name` que só diferem em caractere não-alfanumérico
  (`"Zendesk MCP"` e `"Zendesk.MCP"`) sanitizam para o mesmo prefixo e uma
  sombreia a outra. Este é o caminho de colisão mais alcançável hoje, e é
  exatamente o que o requisito *"Distinção de tools com nomes iguais entre
  servidores diferentes"* de `mcp-tool-execution` já promete — o guarda atual
  passa verde porque só cobre servidores de nomes **diferentes**
  (convenção 15).
- **Determinismo da ordem de desempate** (`apps/workers`): a consulta de
  `AgentMcpServers` em `McpToolSetResolver.ResolveAsync` **não tem `orderby`**,
  então quem ganha o nome-base muda entre execuções conforme o plano do
  Postgres. Ganha `orderby` explícito, como a consulta de `AgentDelegations` já
  tem.
- **Precedência declarada, não herdada da ordem de duas linhas**: MCP mantém a
  precedência que hoje tem por acidente de `Concat`, agora como decisão
  registrada, e a tool renomeada é a que perde.
- **Log de aviso** ao renomear, identificando o agente, o nome pretendido, o
  nome final e a origem de cada conjunto.
- **`ToolNameSanitizerTests` novo** (`apps/workers`): o sanitizador é
  compartilhado pelos dois resolvedores e é o sítio da truncagem, e hoje
  **não tem arquivo de teste próprio**.

Sem **BREAKING** para operador ou API: nenhum contrato de fio muda. O nome
exposto ao LLM pode mudar para agentes que hoje sofrem a colisão — que é a
correção, não uma regressão.

## Capabilities

### New Capabilities
- `agent-tool-namespace`: garante que o conjunto final de tools entregue ao LLM
  de um agente (MCP + delegação, unidos) tem nomes únicos, dentro do limite de
  64 caracteres verificado em fonte primária, comparados de forma ordinal,
  estáveis entre execuções, e que toda renomeação por colisão é observável no
  log. Nenhuma capability existente é dona da **união**
  dos dois conjuntos — `mcp-tool-execution` e `agent-delegation-execution`
  cobrem cada uma o seu lado isoladamente, que é precisamente a lacuna que
  produziu o defeito.

### Modified Capabilities
- `mcp-tool-execution`: o requisito *"Distinção de tools com nomes iguais entre
  servidores diferentes"* ganha o caso que hoje falta — dois `McpServer` cujos
  **nomes** colidem após sanitização/truncagem — e um requisito de ordem
  determinística na resolução, hoje ausente da consulta.
- `agent-delegation-execution`: o requisito *"Nome estável e sem colisão para a
  tool de delegação"* passa de unicidade **dentro do conjunto de delegação**
  para unicidade **no conjunto final**, e ganha a garantia de que o sufixo de
  dedupe respeita o limite de 64 caracteres.

## Impact

Só `apps/workers`. `apps/api` e `apps/frontend` não são tocados — nenhum dos
dois consome o nome exposto ao LLM (verificado: as únicas ocorrências do
separador `"__"` em todo o monorepo estão em `McpToolSetResolver.cs`, linhas 19
e 115).

Código (`apps/workers/src/Buteco.Workers`):
- `Mcp/ToolNameDeduplicator.cs` — **novo**
- `Mcp/ToolNameSanitizer.cs` — expõe a truncagem para reuso do deduplicador
- `Agents/AgentExecutionService.cs` — ponto de concatenação
- `Mcp/McpToolSetResolver.cs` — `orderby` explícito
- `AgentDelegations/AgentDelegationToolSetResolver.cs` — dedupe local sai

Testes (`apps/workers/tests/Buteco.Workers.Tests`):
- `ToolNameSanitizerTests.cs` — **novo**
- `ToolNameDeduplicatorTests.cs` — **novo**
- `Agents/AgentToolNamespaceTests.cs` — **novo**, teste de acordo (convenção 11)
- `Mcp/McpToolSetResolverTests.cs`, `AgentDelegationExecutionTests.cs` — ajustes

Dependências: nenhuma nova. Nenhuma versão de runtime/biblioteca é escolhida
nesta change.
