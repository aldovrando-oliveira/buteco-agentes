## Context

`apps/api` já tem o catálogo de servidores MCP e o vínculo N:N com agentes
(change `backend-mcp-catalogo-vinculo`, arquivada em
`openspec/changes/archive/2026-08-02-backend-mcp-catalogo-vinculo/`). Hoje
`AgentMcpServer` só tem `AgentId` + `McpServerId` — nenhum filtro de tool
individual, deliberadamente adiado para esta change (ver comentário explícito
na entidade e Non-Goals daquele design.md).

Também já existe `IMcpConnectionTester`/`McpConnectionTester`
(`apps/api/src/Buteco.Api/McpServers/Connectivity/`), que usa o SDK oficial
`ModelContextProtocol.Core` (versão `2.0.0`, já confirmada e pinada em
`Directory.Packages.props`) para executar um handshake real (`initialize`)
contra um `McpServer` remoto, atrás de uma interface só para permitir
substituição em teste (mesmo espírito de `IChatClientResolver` em
`apps/workers`). `TestAsync` constrói o transporte (`HttpClientTransport`,
`HttpClientTransportOptions` com `AdditionalHeaders` para `BearerToken`,
`ProtocolVersion` fixo em `"2025-11-25"`), conecta, e descarta o `McpClient`
internamente (`await using` dentro do próprio método) — não expõe o client
nem nenhuma lista de tools.

O SDK confirma o método necessário para descoberta:
`McpClient.ListToolsAsync(RequestOptions?, CancellationToken)` retorna
`IReadOnlyList<McpClientTool>` (cada um com `Name`, `Description`,
`JsonSchema`), lidando com paginação internamente. Não há cache — toda
chamada busca a lista atual ao vivo do servidor, coerente com o fato de que
não existe (e não deve existir) nenhum catálogo estático de tools persistido
em `apps/api`, diferente do catálogo de Provider/Model
(`Buteco.ProviderCatalog`).

Investigação direta ao Postgres local (`agent_mcp_servers`) confirmou **0
linhas existentes** hoje — a change que introduziu esse vínculo foi o commit
mais recente do repositório antes desta, então nenhum vínculo real foi
configurado ainda. Isso é relevante para a Decision 4 (migração).

## Goals / Non-Goals

**Goals:**
- `GET /mcp-servers/{id}/tools` retorna, via handshake real (`tools/list`),
  a lista atual de tools de um `McpServer` cadastrado — nome + descrição —
  sem persistir nenhum catálogo.
- `PUT /agents/{id}/mcp-servers` passa a aceitar, por `McpServerId`, o
  conjunto de tools que o agente pode usar daquele servidor
  (`allowedTools: string[]`), validado contra o servidor real no momento da
  chamada.
- Dois agentes vinculados ao mesmo `McpServer` podem ter `allowedTools`
  diferentes entre si, sem nenhuma interferência mútua.
- Leitura do vínculo de um agente (`AgentResponse.McpServers` e afins) passa
  a expor as tools permitidas por servidor vinculado.

**Non-Goals:**
- Nenhuma mudança em `apps/workers` nesta fatia — usar o allow-list para
  filtrar o conjunto de tools entregue ao LLM numa execução real fica para a
  change futura `apps-workers-execucao-mcp`, que deve ser desenhada para
  consumir esse allow-list desde o início.
- Nenhuma UI em `apps/frontend`.
- Nenhuma revalidação periódica/automática de que as tools selecionadas
  ainda existem no servidor depois do vínculo criado — só valida no momento
  do `PUT` (ver Decision 2). Um `McpServer` pode remover uma tool depois de
  um vínculo já ter sido criado com ela em `allowedTools`, sem que isso seja
  detectado até uma execução real tentar usá-la (fora de escopo aqui) ou até
  o vínculo ser atualizado de novo via `PUT`.
- Bloquear vínculo com `McpServer` inativo — comportamento já decidido e
  inalterado pela change anterior (Decision 3 daquele design.md).

## Decisions

### Decision 1: Armazenamento de `allowedTools` — coluna jsonb em `AgentMcpServer`, não tabela filha

`AgentMcpServer` ganha uma coluna `AllowedTools` (`IReadOnlyList<string>`,
mapeada para `jsonb` via `HasColumnType("jsonb")` — mesmo padrão já usado em
`A2ATaskRecord.Payload`, `AppDbContext.cs:43`), em vez de uma tabela filha
`(AgentId, McpServerId, ToolName)`.

Tools não são um catálogo relacional persistido em lugar nenhum — são
descobertas ao vivo via `tools/list`, conforme Decision 2. Não há FK possível
de `ToolName` para nenhuma tabela (não existe `McpServerTool` persistido), e
não há hoje nenhum caso de uso que precise de índice ou `JOIN` por tool
individual (ex. "quais agentes têm a tool X liberada, em qualquer servidor").
Uma coluna jsonb evita uma tabela e uma migration a mais sem ganho concreto.

**Alternativa considerada e descartada**: tabela filha
`agent_mcp_server_tools (AgentId, McpServerId, ToolName)` com FK composta
para `agent_mcp_servers`. Mais "correta" relacionalmente e permitiria
`JOIN`/índice por tool individual no futuro, mas esse futuro não tem um
requisito concreto hoje — se surgir, é uma migration aditiva nova, não uma
razão para pagar o custo agora. Rejeitada pelo mesmo raciocínio geral do
projeto de não antecipar abstração sem necessidade concreta.

### Decision 2: Validação das tools submetidas — `tools/list` ao vivo no `PUT`, rejeição atômica

Ao processar `PUT /agents/{id}/mcp-servers`, para cada
`{ mcpServerId, allowedTools }` do payload, o handler executa um `tools/list`
ao vivo contra o `McpServer` correspondente (reaproveitando a extensão de
`IMcpConnectionTester` da Decision 3 abaixo) e valida que toda tool em
`allowedTools` existe na resposta. Mesmo princípio de fail-fast já usado para
Provider/Model contra `GET /providers` na validação de `Agent`.

- **Tool inexistente no servidor** → rejeita com HTTP 400, sem aplicar
  nenhum vínculo do payload (mesmo padrão de `InvalidMcpServerIds` já usado
  em `ReplaceAgentMcpServersResult` — um tipo de erro irmão, ex.
  `InvalidTools`, reportando `McpServerId` + nomes rejeitados).
- **Handshake de validação falha** (`McpServer` inalcançável, credencial
  rejeitada) para qualquer `McpServer` do payload → a operação inteira é
  rejeitada, nenhum vínculo do payload é aplicado — nem os de `McpServer`s
  que responderam com sucesso no mesmo `PUT`. Consistente com o
  comportamento já existente de `InvalidIds` (rejeição atômica, sem
  aplicação parcial); não introduzir semântica de sucesso parcial, que não
  existe em nenhum outro endpoint do projeto. O motivo da falha é mapeado
  para um tipo de erro distinto de "tool inexistente" (mesmo espírito de
  `McpConnectionTestFailureReason` — host inalcançável / credencial
  rejeitada —, não um HTTP 500 genérico); o status HTTP exato dessa
  rejeição é a Decision 6.

**Trade-off explícito**: `PUT /agents/{id}/mcp-servers` passa a depender de
conectividade de rede com **todo** `McpServer` referenciado no payload, a
cada chamada — o SDK não faz cache (Decision 2 confirma: toda chamada a
`ListToolsAsync` busca ao vivo), então cada `PUT` agora tem a latência de N
handshakes MCP reais (N = número de `McpServer`s distintos no payload), e a
disponibilidade de servidores de terceiros passa a bloquear uma operação de
configuração que antes era puramente local ao Postgres. Aceito porque é o
mesmo princípio de fail-fast já aplicado a Provider/Model, e porque a
alternativa (aceitar qualquer string sem validar) desloca o erro para tempo
de execução, onde é mais caro de diagnosticar (ver alternativa abaixo).

**Alternativa considerada e descartada**: aceitar qualquer string em
`allowedTools` sem validar contra o servidor, descobrindo apenas em tempo de
execução (change futura `apps-workers-execucao-mcp`) que uma tool não
existe. Rejeitada porque desloca um erro de configuração (nome de tool
digitado errado, tool removida do servidor entre a consulta de descoberta e
o `PUT`) para uma falha silenciosa ou tardia em produção, numa execução real
de agente — pior experiência de diagnóstico do que uma rejeição imediata e
explícita no momento da configuração.

### Decision 3: Extensão de `IMcpConnectionTester` para descoberta de tools — não duplicar lógica de conexão

`IMcpConnectionTester` ganha um segundo método,
`ListToolsAsync(string url, McpServerAuthType authType, string? credential, CancellationToken)`,
que devolve a lista de tools descobertas (ou uma falha, no mesmo formato de
motivo já usado por `TestAsync`). A construção do transporte
(`HttpClientTransportOptions`, `HttpClientTransport`, `McpClientOptions` com
`ProtocolVersion` fixo) é extraída de `TestAsync` para um método privado
compartilhado dentro de `McpConnectionTester`, usado tanto pelo handshake de
teste quanto pela nova listagem — que, após conectar, chama
`client.ListToolsAsync()` do SDK em vez de só validar o `initialize`.

Isso é usado em dois pontos: o novo endpoint `GET /mcp-servers/{id}/tools`
(descoberta explícita, para o cliente escolher tools antes de montar o
`PUT`) e a validação de `PUT /agents/{id}/mcp-servers` da Decision 2 — os
dois pontos chamam o mesmo método, evitando duas lógicas de conexão MCP
paralelas no código.

**Alternativa considerada e descartada**: duplicar a lógica de transporte
numa classe nova (`McpToolDiscoverer`) separada de `McpConnectionTester`.
Rejeitada por criar duas implementações do mesmo transporte MCP
(`HttpClientTransportOptions`/`HttpClientTransport`/`ProtocolVersion`) que
precisariam ser mantidas em sincronia manualmente — o método privado
compartilhado dentro da mesma classe já resolve isso sem indireção extra.

### Decision 4: Semântica de `allowedTools` vazio — estado válido

Um vínculo pode existir com `allowedTools: []` (nenhuma tool liberada ainda)
sem erro de validação no `PUT`. Mesma filosofia já aplicada a
`McpServer.IsActive`/`Agent.IsActive` (Decision 3 do design.md da change
anterior): configurar um estado "inerte" é permitido — só a execução (fora
de escopo aqui) decidiria o que fazer com zero tools liberadas (na prática,
nenhuma tool seria oferecida ao LLM para aquele servidor). Isso habilita o
fluxo natural "vincular o servidor primeiro, selecionar tools depois" sem
forçar o cliente a já conhecer pelo menos um nome de tool no momento do
`PUT`.

**Alternativa considerada e descartada**: rejeitar `allowedTools: []` com
HTTP 400, exigindo pelo menos uma tool selecionada para o vínculo existir.
Rejeitada por acoplar a existência do vínculo à seleção de pelo menos uma
tool — inconveniente se o fluxo de UI/consumidor natural for "linkar o
servidor, depois abrir a lista de tools descobertas e marcar algumas",
que exige um estado intermediário sem tools.

### Decision 5: Migração dos vínculos existentes — `allowedTools: []` por ausência de dado real a migrar

A nova coluna `allowed_tools` (jsonb, default `[]`) é adicionada a
`agent_mcp_servers` via migration aditiva. Qualquer linha pré-existente
recebe `[]` (nenhuma tool permitida) pelo default da coluna — decisão de
baixo custo confirmada por investigação direta: `agent_mcp_servers` tem
**0 linhas** no ambiente atual (a change que criou essa tabela é o commit
mais recente do repositório antes desta), então não há vínculo real cujo
comportamento mude silenciosamente. Se o ambiente tivesse vínculos reais em
uso, tratar como "nenhuma tool permitida" revogaria acesso hoje implícito —
o que pesaria contra essa escolha; como não é o caso, o argumento decisivo
aqui é simplicidade, não segurança.

**Alternativa considerada e descartada**: migrar vínculos existentes para
"todas as tools atualmente oferecidas pelo servidor", conectando em cada
`McpServer` real durante a migration EF Core. Rejeitada por ser frágil (a
migration falharia ou ficaria incompleta se um `McpServer` estivesse fora do
ar no momento do deploy) e desnecessária dado que não há linha real para
preservar comportamento — a fragilidade seria paga sem nenhum benefício
correspondente.

### Decision 6: Status HTTP da falha de handshake durante validação do `PUT` — 502, não 400

Quando o `tools/list` de validação (Decision 2) falha por motivo de conexão
(`McpServer` inalcançável, timeout, ou credencial persistida rejeitada pelo
servidor remoto) para qualquer `McpServer` referenciado no payload de
`PUT /agents/{id}/mcp-servers`, a API responde **HTTP 502 Bad Gateway** —
não 400. É um único status, não uma escolha em aberto para quem implementar:
o contrato HTTP é algo que todo cliente da API precisa conhecer de antemão,
não um detalhe de implementação.

A distinção semântica em relação às outras rejeições do mesmo endpoint
importa: 400 (já usado para `McpServerId` inexistente e para tool inexistente
no `tools/list`, Decision 2) significa "a requisição em si está malformada ou
referencia algo que não existe" — o cliente errou o payload. Já uma falha de
handshake acontece com um payload perfeitamente válido; a causa é a API, atuando
como gateway, não conseguir completar a validação porque um servidor de
terceiro (o `McpServer`) falhou ou está inacessível. 502 é o status que
descreve exatamente essa relação (API como gateway para um upstream que
falhou), e evita que o cliente interprete a falha como "seu payload está
errado" quando na verdade o problema é externo e pode se resolver sozinho
numa nova tentativa.

**Alternativa considerada e descartada**: HTTP 400, por consistência
superficial com as outras rejeições do mesmo endpoint (`InvalidIds`,
tool inexistente). Descartada porque 400 carrega uma semântica específica
("requisição malformada/inválida") que não se aplica aqui — o payload pode
estar perfeitamente correto (`McpServerId` válido, nomes de tool corretos) e
a rejeição ainda assim acontecer, por um motivo inteiramente externo ao
payload. Usar 400 para os dois casos obrigaria o cliente a inspecionar o
corpo da resposta para distinguir "corrija seu payload" de "tente de novo
mais tarde", perdendo a distinção que o próprio protocolo HTTP já oferece
via status code.

## Estrutura de pastas proposta

```
apps/api/src/Buteco.Api/
  McpServers/
    Connectivity/
      IMcpConnectionTester.cs          # alterado: novo método ListToolsAsync
      McpConnectionTester.cs           # alterado: transporte extraído para helper privado
      McpToolDiscoveryResult.cs        # novo: sucesso (tools) ou falha (motivo)
    Queries/
      ListMcpServerTools/
        ListMcpServerToolsQuery.cs
        ListMcpServerToolsQueryHandler.cs
    Endpoints/
      McpServerEndpoints.cs            # alterado: GET /mcp-servers/{id}/tools
    Responses/
      McpServerToolResponse.cs         # novo: Name + Description
  AgentMcpBindings/
    Entities/
      AgentMcpServer.cs                # alterado: nova propriedade AllowedTools
    Requests/
      ReplaceAgentMcpServersRequest.cs # alterado: novo shape com allowedTools
    Commands/
      ReplaceAgentMcpServers/
        ReplaceAgentMcpServersCommand.cs         # alterado
        ReplaceAgentMcpServersCommandHandler.cs  # alterado: valida allowedTools contra tools/list
        ReplaceAgentMcpServersResult.cs          # alterado: novo caso InvalidTools / ValidationFailed
    AgentMcpServerLookup.cs            # alterado: inclui AllowedTools na leitura
  McpServers/Responses/
    McpServerSummaryResponse.cs        # alterado: novo campo AllowedTools (ou tipo irmão específico do binding)
  Infrastructure/
    AppDbContext.cs                    # alterado: mapeamento de AllowedTools (jsonb)
    Migrations/
      <timestamp>_AddAgentMcpServerAllowedTools.cs
```

Nenhum conteúdo novo em `libs/` — `apps/workers` não é tocado nesta fatia, e
não há necessidade de compartilhar código novo com `apps/frontend` (que
também não é tocado).

## Risks / Trade-offs

- **[Trade-off] Latência e disponibilidade de terceiros acopladas a
  `PUT /agents/{id}/mcp-servers`** (Decision 2) → Aceito: mesmo princípio de
  fail-fast já usado para Provider/Model; mitigado por rejeição atômica
  explícita e por reaproveitar a mesma infraestrutura de conexão já testada
  no endpoint de teste existente.
- **[Risco] Tool removida do servidor depois do vínculo criado não é
  detectada até uma execução real ou um novo `PUT`** (Non-Goal explícito) →
  Aceito nesta fatia; revalidação periódica fica para trabalho futuro se se
  mostrar necessária na prática.
- **[Trade-off] `AllowedTools` como jsonb não é consultável via SQL
  relacional (`JOIN`/índice por tool individual)** (Decision 1) → Aceito:
  nenhum caso de uso concreto hoje precisa disso; se surgir, é uma migration
  aditiva nova.
- **[Risco] Extrair a construção do transporte de `McpConnectionTester` para
  um helper compartilhado pode introduzir regressão no endpoint de teste já
  existente** → Mitigação: cobrir com os testes de integração já existentes
  de `POST /mcp-servers/test` e `POST /mcp-servers/{id}/test` continuando a
  passar, sem alteração de comportamento esperado neles.
- **[Trade-off] 502 em `PUT /agents/{id}/mcp-servers` (Decision 6) é incomum
  para um endpoint de configuração** — a maioria dos clientes HTTP trata 5xx
  como "tentar de novo mais tarde" e 4xx como "corrigir e reenviar"; um
  cliente que não distinguir os dois pode tratar 502 como falha transitória
  e tentar novamente sem mudar nada, o que é o comportamento correto aqui,
  mas exige que o corpo da resposta deixe claro qual `McpServer` falhou →
  Aceito: a semântica correta vale mais que a familiaridade, e o corpo da
  resposta (motivo + `McpServerId` afetado) cobre o caso de o cliente querer
  diagnosticar antes de tentar de novo.

## Migration Plan

Migration EF Core aditiva única (`AddAgentMcpServerAllowedTools`): adiciona
coluna `allowed_tools` (jsonb, `NOT NULL DEFAULT '[]'`) a `agent_mcp_servers`,
sem alterar nenhuma outra tabela. Vínculos pré-existentes recebem `[]` pelo
default da coluna (Decision 5) — sem passo de migração de dados adicional,
já que não há linha real hoje no ambiente investigado.

`PUT /agents/{id}/mcp-servers` muda de shape — **BREAKING**, documentado no
proposal. `AgentResponse.McpServers` ganha um campo novo (`allowedTools`,
sempre presente, lista vazia quando nenhuma tool foi liberada) — aditivo no
shape JSON, mas consumidores com parsing estrito precisam tolerar o campo
novo.

Rollback = reverter a migration (`dotnet ef database update <migration
anterior>`), seguro porque nenhuma tabela existente além da coluna nova é
alterada, e a coluna nova não é referenciada por nenhuma FK.

## Open Questions

Nenhuma pergunta de negócio/produto em aberto — as seis decisões acima
cobrem os pontos que precisavam de investigação prévia (`/opsx:explore`)
antes deste proposal. Pontos de implementação a critério de quem implementar
(não bloqueiam o apply): nome exato do tipo de resultado de erro em
`ReplaceAgentMcpServersResult` para tool inválida vs. handshake falho (podem
ser o mesmo caso ou casos distintos, desde que mapeados para 400 e 502
respectivamente — Decision 6), e se `AllowedTools` estende
`McpServerSummaryResponse` in-place ou vira um tipo de resposta irmão restrito
ao contexto de binding.
