## 1. Configuração e dependências (apps/api)

- [x] 1.1 Adicionar `ModelContextProtocol.Core` versão `2.0.0` ao
  `Directory.Packages.props` e referenciar em `apps/api/src/Buteco.Api/Buteco.Api.csproj`
- [x] 1.2 Adicionar seção `Mcp:CredentialEncryptionKey` (chave AES-256 em
  base64) ao `.env.example`, com comentário explicando o formato esperado e
  o mesmo princípio de "nunca no banco" já documentado para as chaves de
  provedor de LLM
- [x] 1.3 Criar `McpCryptoOptions` e registrar `IOptions<McpCryptoOptions>`
  a partir da seção `Mcp` da configuração em
  `InfrastructureServiceCollectionExtensions.cs` (ou extensão equivalente)

## 2. Criptografia da credencial (apps/api)

- [x] 2.1 Criar `IMcpCredentialCipher` (`Encrypt(string plaintext)` /
  `Decrypt(string stored)`) em `McpServers/Security/`
- [x] 2.2 Implementar `AesGcmMcpCredentialCipher`: AES-GCM, nonce aleatório
  de 96 bits por chamada, nonce+tag+ciphertext concatenados e codificados
  em base64 para persistência; lança exceção clara quando
  `Mcp:CredentialEncryptionKey` está ausente ou mal formatada
- [x] 2.3 Registrar `IMcpCredentialCipher` no DI (singleton, sem estado
  entre chamadas)
- [x] 2.4 Testes unitários de `AesGcmMcpCredentialCipher`: round-trip
  encrypt/decrypt, valores diferentes a cada chamada mesmo para o mesmo
  plaintext (nonce aleatório), falha ao decifrar payload corrompido/adulterado

## 3. Modelo e persistência (apps/api)

- [x] 3.1 Criar entidade `McpServer` em `McpServers/Entities/McpServer.cs`
  (Id, Name, Description, Url, AuthType enum `None`/`BearerToken`,
  `EncryptedCredential` privado, IsActive, CreatedAt, UpdatedAt), com
  construtor e `UpdateDetails`/`Activate`/`Deactivate` seguindo o mesmo
  estilo de `Agent`
- [x] 3.2 Criar entidade `AgentMcpServer` em
  `AgentMcpBindings/Entities/AgentMcpServer.cs` (AgentId, McpServerId, chave
  composta)
- [x] 3.3 Adicionar `DbSet<McpServer>` e `DbSet<AgentMcpServer>` a
  `AppDbContext`, com configuração fluente das duas tabelas
  (`mcp_servers`, `agent_mcp_servers`) em `OnModelCreating`, incluindo chave
  composta e `HasOne`/`WithMany` para as duas foreign keys de
  `AgentMcpServer`
- [x] 3.4 Gerar migration EF Core `AddMcpServerCatalog` e validar contra o
  Postgres de desenvolvimento (`dotnet ef database update`)

## 4. Catálogo de servidores MCP — CRUD (apps/api)

- [x] 4.1 Criar `CreateMcpServerCommand`/Handler, validando nome/URL
  obrigatórios e credencial obrigatória quando `AuthType != None`,
  criptografando a credencial via `IMcpCredentialCipher` antes de persistir
- [x] 4.2 Criar `UpdateMcpServerCommand`/Handler, mantendo a credencial
  existente quando nenhuma nova for enviada e `AuthType` continuar exigindo
  credencial; quando `AuthType` transicionar para `None`, limpar
  `EncryptedCredential` explicitamente (ver Decision 6 do design.md) — não
  manter o valor cifrado anterior órfão no banco
- [x] 4.3 Criar `GetMcpServerByIdQuery`/Handler e `ListMcpServersQuery`/Handler
- [x] 4.4 Criar `McpServerResponse` (sem nenhum campo de credencial) e
  `McpServerSummaryResponse` (Id, Name) para uso em `AgentResponse`
- [x] 4.5 Criar `McpServerEndpoints.cs` mapeando `POST /mcp-servers`,
  `GET /mcp-servers`, `GET /mcp-servers/{id}`, `PUT /mcp-servers/{id}`,
  seguindo o mesmo padrão de validação de forma + tradução de resultado já
  usado em `AgentEndpoints.cs`
- [x] 4.6 Testes de integração (`WebApplicationFactory` + Testcontainers
  Postgres): criar com sucesso (com e sem autenticação), validação de nome/
  URL ausentes, validação de credencial ausente com `BearerToken`,
  atualizar com sucesso, atualizar mantendo credencial existente, atualizar
  inexistente retorna 404, listar (incluindo inativos), consultar por id
  (incluindo inativo, incluindo inexistente → 404)
- [x] 4.7 Teste de integração dedicado confirmando que `Credential`/
  `EncryptedCredential` nunca aparece em nenhum JSON de resposta de
  `POST`, `GET` (lista e por id) ou `PUT`
- [x] 4.8 Teste de integração cobrindo a transição `AuthType`
  `BearerToken` → `None` via `PUT /mcp-servers/{id}`: criar um `McpServer`
  com credencial, atualizar para `AuthType: None`, e confirmar (via
  consulta direta ao banco no teste, já que a API nunca expõe
  `EncryptedCredential`) que o valor cifrado anterior não permanece
  persistido

## 5. Ativação e desativação de servidor MCP (apps/api)

- [x] 5.1 Criar `ActivateMcpServerCommand`/Handler e
  `DeactivateMcpServerCommand`/Handler, mapeados em
  `POST /mcp-servers/{id}/activate` e `POST /mcp-servers/{id}/deactivate`
- [x] 5.2 Testes de integração: ativar/desativar com sucesso, idempotência
  nos dois sentidos, ativar/desativar inexistente retorna 404

## 6. Teste de conexão MCP (apps/api)

- [x] 6.1 Criar `IMcpConnectionTester` (`TestAsync(url, authType,
  credential)` retornando `McpConnectionTestResult`) em
  `McpServers/Connectivity/`
- [x] 6.2 Implementar `McpConnectionTester` usando `ModelContextProtocol.Core`:
  monta `HttpClientTransport` (via `HttpClient` nomeado de
  `IHttpClientFactory`, para permitir substituição em teste) com
  `AdditionalHeaders["Authorization"] = "Bearer {token}"` quando aplicável e
  `McpClientOptions.ProtocolVersion = "2025-11-25"` fixo (força o handshake
  `initialize` clássico de forma determinística — a revisão mais nova do
  protocolo, 2026-07-28, substituiu esse handshake por metadata
  por-requisição; ver comentário no código). Chama `McpClient.CreateAsync`
  (não `McpClientFactory`, que não existe em `ModelContextProtocol.Core` —
  só no pacote `ModelContextProtocol`). Confirmado via decompilação do SDK
  (2.0.0) que tanto falha de conectividade quanto HTTP 401/403 chegam como
  `HttpRequestException` — `StatusCode == Unauthorized`/`Forbidden` mapeia
  para "credencial rejeitada", qualquer outro caso (incluindo `StatusCode
  == null`, conexão recusada) mapeia para "host inalcançável"
- [x] 6.3 Criar `McpConnectionTestFailureReason` (enum), incluindo o valor
  `CredentialDecryptionFailed` para falha ao decifrar a credencial
  persistida (distinto de `CredentialRejected`, que é rejeição reportada
  pelo próprio servidor MCP) — e `McpConnectionTestResult`/`McpConnectionTestResponse`
- [x] 6.4 Criar `TestUnsavedMcpServerConnectionCommand`/Handler (recebe
  Url/AuthType/Credential no corpo, sem exigir `McpServer` existente) e
  `TestSavedMcpServerConnectionCommand`/Handler (recebe id, decifra a
  credencial persistida via `IMcpCredentialCipher`, delega para
  `IMcpConnectionTester`). No handler do segundo, capturar explicitamente a
  exceção lançada por `IMcpCredentialCipher.Decrypt` (chave rotacionada
  desde que a credencial foi salva, ou payload corrompido) e retornar
  `McpConnectionTestFailureReason.CredentialDecryptionFailed` em vez de
  propagar como erro não tratado — essa falha nunca deve chegar a tentar
  uma conexão de rede
- [x] 6.5 Mapear `POST /mcp-servers/test` e `POST /mcp-servers/{id}/test`
  em `McpServerEndpoints.cs`, incluindo 404 para id inexistente no segundo
- [x] 6.6 Criar fixture de teste `FakeMcpServerHttpMessageHandler` (ou
  equivalente) que simula, via `HttpMessageHandler`, os payloads JSON-RPC
  de um servidor MCP respondendo `initialize` com sucesso, e registrar
  `HttpClientTransport` construído com esse handler fake substituível via
  `WebApplicationFactory.ConfigureWebHost`, mesmo estilo do spy de
  `ITaskJobPublisher` em `AgentDeactivationFixture`
- [x] 6.7 Testes de integração de `POST /mcp-servers/test`: handshake
  bem-sucedido, host inalcançável (handler lança `HttpRequestException`),
  credencial rejeitada (handler responde 401/403) — todos sem rede real
- [x] 6.8 Testes de integração de `POST /mcp-servers/{id}/test`: mesmos três
  cenários usando um `McpServer` salvo, mais o caso de id inexistente → 404
  e o caso de servidor inativo (teste executa normalmente, sem bloqueio)
- [x] 6.9 Teste de integração cobrindo falha ao decifrar a credencial
  persistida em `POST /mcp-servers/{id}/test`: criar um `McpServer` com
  credencial válida e, via SQL direto no teste (mesmo padrão de
  `SeedLegacyAgentWithoutProviderOrModelAsync`), sobrescrever
  `EncryptedCredential` com um valor que não decifra sob a chave
  configurada — efeito observável idêntico ao de uma chave rotacionada,
  sem precisar reconfigurar a fábrica de testes com uma segunda chave.
  Confirma que o endpoint responde com
  `McpConnectionTestFailureReason.CredentialDecryptionFailed`, sem tentar
  nenhuma conexão de rede (contador de requisições do handler fake não
  incrementa) e sem retornar erro 500
- [x] 6.10 Teste de integração confirmando que nenhum dos dois endpoints de
  teste persiste alteração em `McpServer` nem em nenhuma nova tabela
  (comparar estado antes/depois da chamada)

## 7. Vínculo agente ↔ servidor MCP (apps/api)

- [x] 7.1 Criar `ReplaceAgentMcpServersCommand`/Handler
  (`AgentId`, `McpServerId[]`): valida que o agente existe (404 se não),
  valida que todos os `McpServerId` existem (400 se algum não existir,
  independente de estar ativo ou inativo), substitui o conjunto de
  `AgentMcpServer` inteiro dentro de uma transação
- [x] 7.2 Criar `ReplaceAgentMcpServersRequest` e mapear
  `PUT /agents/{id}/mcp-servers` em `AgentMcpBindingEndpoints.cs`
- [x] 7.3 Testes de integração: vincular conjunto inicial, substituir por
  conjunto diferente, remover todos os vínculos (lista vazia), idempotência
  ao reenviar o mesmo conjunto, rejeitar `McpServerId` inexistente (400,
  sem alterar vínculos existentes), agente inexistente → 404, vincular a
  `McpServer` inativo é aceito
- [x] 7.4 Teste de integração cobrindo os dois lados do N:N: mesmo
  `McpServer` vinculado a dois agentes diferentes sem interferência mútua;
  mesmo agente vinculado a dois `McpServer` diferentes

## 8. AgentResponse com servidores MCP vinculados (apps/api)

- [x] 8.1 Adicionar campo `McpServers: McpServerSummaryResponse[]` a
  `AgentResponse`, populado a partir de `AgentMcpServer` (lista vazia,
  nunca nula, quando não há vínculo)
- [x] 8.2 Ajustar `GetAgentByIdQueryHandler` e `ListAgentsQueryHandler`
  para incluir (`Include`/projeção) os servidores MCP vinculados na
  consulta
- [x] 8.3 Testes de integração: `GET /agents/{id}` e `GET /agents` refletem
  o conjunto de servidores MCP vinculados (id + name) após
  `PUT /agents/{id}/mcp-servers`; agente sem vínculo retorna `mcpServers`
  como lista vazia em ambos os endpoints

## 9. Revisão final (apps/api)

- [x] 9.1 Rodar a suíte completa de `Buteco.Api.Tests` e confirmar que os
  testes existentes de `Agent` continuam passando com o novo campo
  `mcpServers` na resposta. Primeira rodada: 79 aprovados, 4 falhas —
  confirmado (via `git stash` contra o baseline) que as 4 eram
  pré-existentes e não relacionadas a esta change: `NoProvidersConfiguredTests`,
  `ProviderEndpointsTests`, `AgentCreateProviderValidationTests` e
  `AgentUpdateProviderValidationTests` assumiam Anthropic/Gemini como não
  configurados por padrão, mas dependiam disso implicitamente do conteúdo
  de `appsettings.Development.json` (que tinha `ApiKey: "changeme"` para os
  dois — um valor não vazio, portanto "configurado" para
  `LlmProviderConfigurationExtensions.IsConfigured`). Corrigido isolando os
  testes do arquivo de configuração local: `ApiFactoryFixture` e
  `NoProvidersConfiguredFixture` agora limpam explicitamente
  `Anthropic:ApiKey`/`Gemini:ApiKey` via `ConfigureAppConfiguration`, mesmo
  padrão que `NoProvidersConfiguredFixture` já usava para `OpenAI:ApiKey` —
  os testes deixam de depender do conteúdo de `appsettings.Development.json`,
  que fica livre para quem desenvolve ajustar para uso manual da API (ver
  incidente: um agente cadastrado com `provider: "anthropic"`/`"gemini"`
  usando o `changeme` de conveniência local passou a ser rejeitado em
  runtime real depois de uma tentativa anterior, incorreta, de corrigir os
  testes editando `appsettings.Development.json` diretamente — revertida).
  Suíte completa: **83/83 aprovados**, estável em múltiplas rodadas
- [x] 9.2 Revisar que nenhum código novo referencia `apps/workers` ou
  `apps/frontend`, e que nenhuma mudança foi feita fora de `apps/api`.
  Confirmado via `git status`/`git diff --name-only`: todos os arquivos
  tocados estão em `apps/api/**`, `.env.example` ou `Directory.Packages.props`
  (versão de pacote compartilhada, não código); nenhuma referência a
  `Buteco.Workers`/`apps/frontend` em código (só uma menção em comentário,
  como analogia de padrão, em `IMcpConnectionTester.cs`)
