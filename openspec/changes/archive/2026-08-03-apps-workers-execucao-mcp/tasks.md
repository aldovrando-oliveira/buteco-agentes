## 1. Mirror de entidades e infraestrutura (apps/workers)

- [x] 1.1 (apps/workers) Criar `Mcp/Entities/McpServerAuthType.cs` — mirror do enum de `apps/api`.
- [x] 1.2 (apps/workers) Criar `Mcp/Entities/McpServer.cs` — mirror read-only da entidade de `apps/api` (`Id`, `Name`, `Description`, `Url`, `AuthType`, `EncryptedCredential`, `IsActive`, timestamps).
- [x] 1.3 (apps/workers) Criar `Mcp/Entities/AgentMcpServer.cs` — mirror read-only, incluindo `AllowedTools` (`IReadOnlyList<string>`).
- [x] 1.4 (apps/workers) Adicionar `DbSet<McpServer>`/`DbSet<AgentMcpServer>` e mapeamento fluente (incluindo `AllowedTools` como jsonb) em `Infrastructure/AppDbContext.cs`.
- [x] 1.5 (apps/workers) Gerar migration EF Core aditiva (`AddMcpServerCatalog`) mirror do schema já existente — ferramental apenas, nunca aplicada em runtime por este app.
- [x] 1.6 (apps/workers) Adicionar `PackageReference` a `ModelContextProtocol.Core` no `.csproj` de `Buteco.Workers` (versão já pinada em `Directory.Packages.props`).

## 2. Segurança de credencial (apps/workers)

- [x] 2.1 (apps/workers) Criar `Mcp/Security/McpCryptoOptions.cs` — mirror (`Mcp:CredentialEncryptionKey`).
- [x] 2.2 (apps/workers) Criar `Mcp/Security/IMcpCredentialCipher.cs` e `AesGcmMcpCredentialCipher.cs` — mirror exato do algoritmo AES-GCM já usado em `apps/api`.
- [x] 2.3 (apps/workers) Testes unitários de `AesGcmMcpCredentialCipher` (encrypt/decrypt round-trip, chave inválida, payload corrompido) — mirror dos testes já existentes em `apps/api` adaptados ao namespace de `apps/workers`.

## 3. Conectividade MCP chamável (apps/workers)

- [x] 3.1 (apps/workers) Criar `Mcp/McpTransportFactory.cs` — constrói `HttpClientTransport`/`HttpClientTransportOptions`/`McpClientOptions`, mesmo padrão de `McpConnectionTester.BuildTransport` em `apps/api` (protocolo fixo `"2025-11-25"`, `HttpClient` nomeado via `IHttpClientFactory`).
- [x] 3.2 (apps/workers) Criar `Mcp/McpToolSet.cs` — `IAsyncDisposable`, expõe `IReadOnlyList<AITool> Tools`, dono do ciclo de vida dos `McpClient` abertos para a resolução.
- [x] 3.3 (apps/workers) Criar `Mcp/IMcpToolSetResolver.cs` e `Mcp/McpToolSetResolver.cs` — dado um `agentId`: lê `AgentMcpServer` vinculados com `McpServer.IsActive == true`, conecta a cada um, executa `tools/list`, filtra pela interseção com `AllowedTools`, aplica `WithName($"{mcpServer.Name}__{tool.Name}")` em cada tool resolvida, captura falha de conexão por servidor (log de aviso, exclui o servidor, não propaga), devolve `McpToolSet`.
- [x] 3.4 (apps/workers) Registrar DI em `Program.cs`: `IMcpCredentialCipher`, `HttpClient` nomeado para MCP, `IMcpToolSetResolver`.

## 4. Integração com a execução do agente (apps/workers)

- [x] 4.1 (apps/workers) Injetar `IMcpToolSetResolver` em `AgentExecutionService`.
- [x] 4.2 (apps/workers) Em `ExecuteAsync`, resolver o `McpToolSet` do agente (`await using`) antes de montar o `ChatClientAgent`, popular `ChatOptions.Tools` com `toolSet.Tools`, manter o `await using` vivo durante toda a chamada de `RunAsync`.
- [x] 4.3 (apps/workers) Confirmar que uma falha de tool call em andamento (durante `RunAsync`, depois da resolução) continua caindo no `catch (Exception)` já existente de `ExecuteAsync` (task termina como `failed`) — sem tratamento novo necessário, só validar via teste.

## 5. Testes (apps/workers)

- [x] 5.1 (apps/workers) Criar `Mcp/Support/FakeMcpServerHttpMessageHandler.cs` — simula `initialize`, `tools/list` e `tools/call` via JSON-RPC sobre HTTP, fixture próprio (não reaproveita o de `apps/api`).
- [x] 5.2 (apps/workers) Teste: tool permitida e oferecida pelo servidor entra no conjunto resolvido.
- [x] 5.3 (apps/workers) Teste: tool não presente em `AllowedTools` não entra no conjunto.
- [x] 5.4 (apps/workers) Teste: tool em `AllowedTools` que o servidor não oferece mais é excluída sem erro.
- [x] 5.5 (apps/workers) Teste: `AllowedTools` vazio resulta em zero tools daquele servidor.
- [x] 5.6 (apps/workers) Teste: agente sem nenhum `McpServer` vinculado processa a task normalmente até `completed`, sem nenhuma tool oferecida ao LLM e sem nenhuma tentativa de conexão MCP (verificar que `FakeMcpServerHttpMessageHandler` nunca é invocado) — distinto do teste de `AllowedTools` vazio (5.5), que ainda tem um `McpServer` vinculado.
- [x] 5.7 (apps/workers) Teste: `McpServer` com `IsActive: false` é excluído sem tentativa de conexão.
- [x] 5.8 (apps/workers) Teste: `McpServer` inalcançável é excluído do conjunto, task processa normalmente até `completed`.
- [x] 5.9 (apps/workers) Teste: todos os `McpServer`s vinculados e ativos de um agente (mais de um) estão inalcançáveis — task ainda assim chega a `completed`, com zero tools disponíveis ao LLM no total (não apenas um servidor faltando entre vários, já coberto em 5.8).
- [x] 5.10 (apps/workers) Teste: falha ao decifrar credencial persistida degrada apenas aquele servidor.
- [x] 5.11 (apps/workers) Teste: dois `McpServer`s vinculados ao mesmo agente com tool de nome igual — ambas aparecem no conjunto resolvido, distinguíveis por prefixo.
- [x] 5.12 (apps/workers) Teste de round-trip completo: LLM (fake `IChatClient`) decide chamar uma tool MCP, chamada chega ao `FakeMcpServerHttpMessageHandler`, resultado volta e afeta o texto final da resposta da task.
- [x] 5.13 (apps/workers) **[Prioridade alta — valida a Decision 4]** Teste: LLM fake chama a mesma tool MCP duas vezes no mesmo turno — confirmar que a segunda chamada é roteada com sucesso e que nenhuma nova tentativa de conexão/handshake (`initialize`) acontece entre a primeira e a segunda chamada (contar quantas requisições de `initialize` o `FakeMcpServerHttpMessageHandler` recebe — deve ser uma só, não duas).
- [x] 5.14 (apps/workers) Teste: `McpToolSet.DisposeAsync` fecha as conexões MCP abertas ao final da execução (verificar, por exemplo, que uma chamada de tool após o dispose falha/não é mais possível).

## 6. Configuração e documentação operacional

- [x] 6.1 (apps/workers) Documentar `Mcp:CredentialEncryptionKey` no `.env.example`/configuração de `apps/workers` (mesma chave já usada por `apps/api`, compartilhada entre os dois apps para que a credencial cifrada por um seja decifrável pelo outro).
