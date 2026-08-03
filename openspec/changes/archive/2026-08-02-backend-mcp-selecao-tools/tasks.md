## 1. Conectividade MCP — descoberta de tools (apps/api)

- [x] 1.1 Extrair a construção do transporte MCP (`HttpClientTransportOptions`,
      `HttpClientTransport`, `McpClientOptions`) de `McpConnectionTester.TestAsync`
      para um método privado compartilhado, sem mudar o comportamento atual do
      teste de conexão.
- [x] 1.2 Adicionar `McpToolDiscoveryResult` (sucesso com lista de tools, ou
      falha com motivo — mesmo espírito de `McpConnectionTestResult`/
      `McpConnectionTestFailureReason`).
- [x] 1.3 Adicionar `ListToolsAsync(string url, McpServerAuthType authType, string? credential, CancellationToken)`
      em `IMcpConnectionTester`, implementado em `McpConnectionTester` usando o
      helper da tarefa 1.1 e `McpClient.ListToolsAsync()` do SDK.
- [x] 1.4 Testes unitários/integração de `McpConnectionTester.ListToolsAsync`:
      handshake bem-sucedido retorna as tools do fixture mockado; host
      inalcançável; credencial rejeitada — reaproveitando o fixture de
      `HttpMessageHandler` já criado na change `backend-mcp-catalogo-vinculo`.

## 2. Endpoint de descoberta — `GET /mcp-servers/{id}/tools` (apps/api)

- [x] 2.1 Adicionar `McpServerToolResponse` (Name + Description).
- [x] 2.2 Adicionar `ListMcpServerToolsQuery`/`ListMcpServerToolsQueryHandler`
      (busca `McpServer` por id, 404 se não existir, decifra credencial e
      chama `IMcpConnectionTester.ListToolsAsync`).
- [x] 2.3 Registrar `GET /mcp-servers/{id}/tools` em `McpServerEndpoints.cs`.
- [x] 2.4 Testes de integração: handshake mockado com sucesso retorna a lista
      de tools; `McpServer` inexistente retorna 404; host inalcançável e
      credencial rejeitada retornam resultado de falha com motivo
      identificável; servidor `isActive: false` não bloqueia a descoberta.

## 3. Armazenamento de `allowedTools` (apps/api)

- [x] 3.1 Adicionar propriedade `AllowedTools` (`IReadOnlyList<string>`) a
      `AgentMcpServer`, com construtor atualizado.
- [x] 3.2 Mapear `AllowedTools` como jsonb em `AppDbContext.cs` (mesmo padrão
      de `A2ATaskRecord.Payload`), com default `[]`.
- [x] 3.3 Gerar migration EF Core aditiva (`AddAgentMcpServerAllowedTools`)
      adicionando a coluna `allowed_tools` (jsonb, `NOT NULL DEFAULT '[]'`) a
      `agent_mcp_servers`.
- [x] 3.4 Teste de integração: aplicar a migration sobre um banco com um
      `AgentMcpServer` pré-existente (sem a coluna) e confirmar que a linha
      passa a ter `allowed_tools = []` sem erro.

## 4. `PUT /agents/{id}/mcp-servers` — novo shape e validação (apps/api)

- [x] 4.1 Atualizar `ReplaceAgentMcpServersRequest` para
      `IReadOnlyList<{ McpServerId: Guid, AllowedTools: string[] }>` (novo
      shape, **BREAKING** em relação ao `Guid[]` anterior).
- [x] 4.2 Atualizar `ReplaceAgentMcpServersCommand`/`CommandHandler`: validar
      `McpServerId`s existentes (comportamento já existente); para cada
      `McpServerId` distinto do payload, chamar
      `IMcpConnectionTester.ListToolsAsync` e validar que todo nome em
      `AllowedTools` existe na resposta.
- [x] 4.3 Adicionar caso de erro `InvalidTools` (ou equivalente) em
      `ReplaceAgentMcpServersResult`, reportando `McpServerId` + nomes de
      tools rejeitados — rejeição atômica (nenhum vínculo do payload
      aplicado).
- [x] 4.4 Adicionar caso de erro para falha de handshake durante a validação
      (host inalcançável / credencial rejeitada), distinto de `InvalidTools`
      — rejeição atômica, sem aplicar nenhum vínculo do payload.
- [x] 4.5 Persistir `AllowedTools` por vínculo ao criar/atualizar
      `AgentMcpServer` no handler.
- [x] 4.6 Atualizar o endpoint (`AgentMcpBindingEndpoints.cs` ou equivalente)
      para mapear os novos casos de erro para os status HTTP corretos: 400
      para tool inválida e para `McpServerId` inexistente, 404 para agente
      inexistente, e **502** para falha de handshake durante a validação
      (Decision 6 do design.md).

## 5. Leitura do vínculo com `allowedTools` (apps/api)

- [x] 5.1 Estender `McpServerSummaryResponse` (ou criar um tipo irmão
      específico do binding) para incluir `AllowedTools`.
- [x] 5.2 Atualizar `AgentMcpServerLookup.GetLinkedMcpServersAsync` para
      projetar `AllowedTools` de cada `AgentMcpServer`.
- [x] 5.3 Confirmar que `AgentResponse.FromEntity`, `ListAgentsQueryHandler`
      e `GetAgentByIdQueryHandler` continuam compilando e refletindo o novo
      campo sem mudança adicional de código (já usam
      `AgentMcpServerLookup`/`McpServerSummaryResponse` — validar via teste).

## 6. Testes de comportamento (apps/api)

- [x] 6.1 Migrar os testes existentes de `PUT /agents/{id}/mcp-servers`
      herdados da change `backend-mcp-catalogo-vinculo` para o novo shape
      (`{ mcpServerId, allowedTools }[]` em vez de `Guid[]`) — esses testes
      hoje cobrem substituir o conjunto por um diferente, remover todos os
      vínculos, `McpServerId` inexistente, agente inexistente, vínculo com
      `McpServer` inativo, e os dois lados do N:N (mesmo servidor em dois
      agentes; mesmo agente em dois servidores). Atualizar o payload de cada
      teste para o novo shape (incluindo `allowedTools` válidas quando o
      handshake de validação entrar no caminho do teste) e confirmar que
      cada cenário original continua coberto e passando sob a nova
      assinatura — não apenas fazer o build compilar.
- [x] 6.2 `PUT /agents/{id}/mcp-servers` aceita `allowedTools` válidas e
      retorna o vínculo criado com o `allowedTools` correspondente.
- [x] 6.3 `PUT /agents/{id}/mcp-servers` rejeita tool inexistente no
      servidor com HTTP 400, sem alterar vínculos existentes do agente.
- [x] 6.4 `PUT /agents/{id}/mcp-servers` rejeita a operação inteira com HTTP
      502 quando o handshake de validação falha para qualquer `McpServer` do
      payload, mesmo havendo outros `McpServer`s no mesmo payload cujo
      handshake teve sucesso (Decision 6 do design.md).
- [x] 6.5 Dois agentes vinculados ao mesmo `McpServer` com `allowedTools`
      diferentes entre si — atualizar um não afeta o `allowedTools` do
      outro.
- [x] 6.6 `allowedTools: []` é aceito como vínculo válido (sem erro).
- [x] 6.7 Migration aplicada sobre vínculos pré-existentes resulta em
      `allowedTools: []` (ver tarefa 3.4).
- [x] 6.8 Reenviar o mesmo payload (`mcpServerId` + `allowedTools`) duas
      vezes seguidas continua idempotente.

## 7. Documentação e revisão

- [x] 7.1 Atualizar qualquer documentação de API (ex. coleção
      HTTP/OpenAPI, se existente em `apps/api`) refletindo o novo shape de
      `PUT /agents/{id}/mcp-servers` e o novo endpoint
      `GET /mcp-servers/{id}/tools`. N/A: o único arquivo `.http` do projeto
      (`Buteco.Api.http`) é o placeholder padrão do template ASP.NET Core
      (`/weatherforecast`), nunca atualizado para nenhum endpoint real da
      API (nem `/agents`, nem `/mcp-servers` existentes) — não há
      documentação viva para manter em sincronia nesta fatia.
