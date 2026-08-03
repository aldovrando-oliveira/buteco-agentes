## Why

`apps/api` já permite cadastrar servidores MCP (`mcp-server-catalog`) e vincular tools permitidas a um agente (`agent-mcp-binding`, com `allowedTools` validado ao vivo via `tools/list`), mas nenhuma dessas tools chega a ser oferecida ou chamada de fato — `apps/workers`, quem realmente executa um agente via Microsoft Agent Framework, não tem nenhuma visibilidade de `McpServer`/`AgentMcpServer` hoje. O catálogo e o vínculo configurados em `apps/api` são, na prática, inertes: um agente pode ter servidores MCP vinculados com tools selecionadas, mas nenhuma execução real jamais os usa. Esta change fecha esse ciclo — `apps/workers` passa a descobrir, filtrar e efetivamente invocar as tools MCP vinculadas a um agente durante o processamento de uma task.

## What Changes

- `apps/workers` ganha uma cópia própria (mesmo padrão de isolamento já usado para `Agent`) das entidades `McpServer` e `AgentMcpServer` (com `AllowedTools`) no seu `AppDbContext`, e uma implementação própria de `IMcpCredentialCipher` (AES-GCM, mesma `Mcp:CredentialEncryptionKey`) — sem `ProjectReference` para `apps/api`, sem migrar o schema (leitura apenas, mesmo padrão do mirror de `Agent`).
- Novo `IMcpToolSetResolver`: dado um `agentId`, conecta a cada `McpServer` vinculado e ativo, executa `tools/list`, filtra pela interseção com `allowedTools`, prefixa cada nome de tool pelo nome do servidor (evita colisão entre servidores diferentes), e devolve um conjunto de `AITool` prontos para uso — junto da posse do ciclo de vida das conexões MCP subjacentes (`IAsyncDisposable`).
- `AgentExecutionService` passa a popular `ChatOptions.Tools` com esse conjunto antes de `RunAsync`, mantendo as conexões MCP vivas durante toda a execução (não só durante a descoberta) — round-trip completo: o LLM decide chamar uma tool, o framework invoca automaticamente (via `FunctionInvokingChatClient`, já inserido por padrão pelo `ChatClientAgent`), a chamada chega ao servidor MCP real, o resultado volta para o LLM, e a conversa continua até `completed`.
- Falha de conexão com um `McpServer` específico (inalcançável, credencial não decifra, timeout) durante a resolução do conjunto de tools não derruba a task inteira — esse servidor fica de fora do conjunto daquela execução, e a execução segue com o que sobrou (log de aviso).

## Capabilities

### New Capabilities
- `mcp-tool-execution`: descoberta, filtragem por `allowedTools`, e execução real de tools MCP por `apps/workers` durante o processamento de uma task de agente.

### Modified Capabilities
_Nenhuma — `agent-mcp-binding` e `mcp-server-catalog` continuam com o mesmo contrato de API em `apps/api`; esta change só consome o que já existe, sem alterar seus requirements._

## Impact

- **`apps/workers`** (único app afetado):
  - `Infrastructure/AppDbContext.cs`: novos `DbSet<McpServer>`/`DbSet<AgentMcpServer>`, mapeamento fluente (inclui `AllowedTools` como jsonb).
  - Novo módulo de segurança (`IMcpCredentialCipher`/`AesGcmMcpCredentialCipher`), mirror do de `apps/api`.
  - Novo módulo de conectividade MCP "chamável" (constrói `HttpClientTransport`/`McpClient`, mantém vivo durante a execução) — não reaproveita `McpConnectionTester` de `apps/api` (isolamento entre apps).
  - `Agents/AgentExecutionService.cs`: monta `ChatOptions.Tools` a partir do `IMcpToolSetResolver` antes de `RunAsync`.
  - `Program.cs`: DI para o novo resolver, `IMcpCredentialCipher`, `HttpClient` nomeado.
  - Nova migration EF Core em `apps/workers` (schema idêntico ao já existente em `apps/api`, só para ferramental/detecção de divergência — nunca aplicada em runtime, mesmo padrão do mirror de `Agent`).
- **`apps/api`**: nenhuma mudança.
- **`apps/frontend`**: nenhuma mudança (ainda não existe UI para catálogo/vínculo/seleção de tools).
- **Dependências**: nenhum pacote NuGet novo — `ModelContextProtocol.Core` e `Microsoft.Agents.AI` já estão pinados em `Directory.Packages.props` (o primeiro precisa passar a ser referenciado também pelo `.csproj` de `apps/workers`, já é usado por `apps/api`).
