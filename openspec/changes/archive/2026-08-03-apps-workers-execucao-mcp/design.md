## Context

`apps/api` já tem o catálogo de servidores MCP (`mcp-server-catalog`) e o vínculo
N:N agente↔MCP com `allowedTools` (`agent-mcp-binding`, change
`backend-mcp-selecao-tools`, arquivada). Nenhuma dessas duas capabilities toca
`apps/workers` — são puramente de configuração. `apps/workers` é quem
efetivamente executa um agente (`AgentExecutionService.ExecuteAsync`, via
Microsoft Agent Framework, `Microsoft.Agents.AI` 1.15.0, já pinado em
`Directory.Packages.props`), e hoje não tem nenhuma visibilidade de
`McpServer`/`AgentMcpServer` — o catálogo e o vínculo configurados em
`apps/api` são inertes até esta change.

`apps/workers` já resolve exatamente este mesmo problema de isolamento para
`Agent`: uma cópia própria da entidade (`Agents/Entities/Agent.cs`), um
`AppDbContext` próprio apontando pro mesmo schema Postgres
(`Infrastructure/AppDbContext.cs`, comentário explícito: "só `apps/api` aplica
migrations... este contexto nunca chama `Database.MigrateAsync`"), sem
`ProjectReference` entre os dois apps. Esta change segue exatamente o mesmo
padrão para `McpServer`/`AgentMcpServer`, incluindo a coluna `AllowedTools`
(jsonb) adicionada pela change anterior.

`apps/api` também já tem toda a lógica de conexão MCP validada em produção
(`McpServers/Connectivity/McpConnectionTester.cs`): construção de
`HttpClientTransport`/`HttpClientTransportOptions` via `IHttpClientFactory`
nomeado, `ProtocolVersion` fixo `"2025-11-25"`, tratamento de
`HttpRequestException` (401/403 → credencial rejeitada; qualquer outra →
host inalcançável). Essa lógica não é reaproveitada diretamente (isolamento
entre apps, mesma regra que já vale para `Agent`), mas o padrão de construção
de transporte é replicado tal como está — é a mesma dependência
(`ModelContextProtocol.Core` 2.0.0, já pinada) e o mesmo protocolo.

A diferença real entre o que `apps/api` precisa e o que `apps/workers`
precisa: `apps/api` só lista tools (`tools/list`) para validação/descoberta,
conecta e descarta o `McpClient` na mesma chamada
(`await using var client = ...` dentro do método). `apps/workers` precisa
**invocar** uma tool de verdade quando o LLM decidir chamá-la — o que, como
investigado abaixo (Decision 1), significa manter a conexão viva durante toda
a execução do agente, não só durante a descoberta.

**Investigação técnica prévia** (via `/opsx:explore`, antes deste design):
reflection direta contra os assemblies reais já pinados no projeto
(`ModelContextProtocol.Core` 2.0.0, `Microsoft.Agents.AI` 1.15.0,
`Microsoft.Extensions.AI.Abstractions` 10.6.0, mesma versão puxada
transitivamente por `Microsoft.Agents.AI`) confirmou a forma exata das APIs
usadas nas Decisions abaixo — não é suposição sobre o SDK, é o formato real
dos tipos e membros públicos, cruzado com a documentação XML embutida no
pacote. Nenhuma versão nova de pacote é introduzida por esta change.

## Goals / Non-Goals

**Goals:**
- `apps/workers` ganha sua própria cópia de `McpServer`/`AgentMcpServer`
  (com `AllowedTools`) no seu `AppDbContext`, e sua própria implementação de
  `IMcpCredentialCipher` — mesmo padrão de isolamento já usado para `Agent`.
- Dado um `agentId`, `apps/workers` descobre as tools de cada `McpServer`
  vinculado e ativo, filtra pela interseção com `allowedTools`, e monta um
  conjunto de `AITool` prontos para uso pelo LLM durante uma execução real.
- `AgentExecutionService` passa a oferecer esse conjunto ao `ChatClientAgent`
  via `ChatOptions.Tools`, e o round-trip completo funciona: o LLM decide
  chamar uma tool, a chamada chega ao servidor MCP real via
  `McpClient.CallToolAsync`, o resultado volta pro LLM, a conversa continua
  até `completed`.
- Falha de conexão com um `McpServer` específico durante a resolução do
  conjunto de tools não derruba a task inteira.

**Non-Goals:**
- Nenhuma UI em `apps/frontend`.
- Nenhuma revalidação periódica de `allowedTools` fora de uma execução real
  (Non-Goal já herdado de `backend-mcp-selecao-tools`).
- Nenhuma alteração em `apps/api`.
- Nenhum cache/pool de conexão MCP **entre execuções de task diferentes**.
  Dentro de uma mesma execução, a conexão fica viva pelo `RunAsync` inteiro
  (ver Decision 4) — isso não é cache, é o ciclo de vida mínimo necessário
  para uma tool ser de fato chamável mais de uma vez durante o mesmo turno.
- Nenhum limite customizado de número de chamadas de tool por turno — ver
  Decision 6.
- Nenhum mecanismo de aprovação humana de tool call
  (`ToolApprovalAgent`/`ApprovalRequiredAIFunction` existem no SDK, mas ficam
  fora de escopo aqui). Toda tool do conjunto filtrado é invocada
  automaticamente, sem aprovação — mesmo comportamento default do framework
  quando nenhum decorator de aprovação é adicionado.
- Nenhuma tentativa de fazer cache do resultado de `tools/list` entre
  execuções — mesmo raciocínio já usado em `apps/api` (Decision 1/2 do
  design.md de `backend-mcp-selecao-tools`): tools não são um catálogo
  persistido em lugar nenhum.

## Decisions

### Decision 1: Tools MCP entram em `ChatOptions.Tools` como `AITool` sem nenhum adapter próprio

Confirmado por reflection direta: `ModelContextProtocol.Client.McpClientTool`
**estende** `Microsoft.Extensions.AI.AIFunction` (que por sua vez estende
`AIFunctionDeclaration` e implementa o contrato `AITool`) — não é
"conversível para", é um `AIFunction` de verdade, com `InvokeAsync`
sobrescrito para chamar `McpClient.CallToolAsync` internamente.
`Microsoft.Extensions.AI.ChatOptions.Tools` é `IList<AITool>`. Logo, os
`McpClientTool` retornados por `McpClient.ListToolsAsync()` (via
`ModelContextProtocol.Core`) entram diretamente nessa lista — nenhuma classe
`McpToolAdapter`/wrapper própria é necessária para "traduzir" uma tool MCP
para algo que o Microsoft Agent Framework entenda.

Também confirmado por reflection + XML doc do pacote: `ChatClientAgent`
insere automaticamente um decorator `FunctionInvokingChatClient` ao redor do
`IChatClient` fornecido, controlado por
`ChatClientAgentOptions.UseProvidedChatClientAsIs` (default `false` — os
decorators são aplicados por padrão). Documentação oficial do XML doc do
pacote: *"By default the ChatClientAgent applies decorators to the provided
IChatClient for doing for example automatic function invocation."* Ou seja,
`AgentExecutionService` não precisa montar o loop de tool-calling nem envolver
manualmente o `chatClient` com `.UseFunctionInvocation()` (extensão de
`ChatClientBuilder`) — basta popular `ChatOptions.Tools` com o conjunto
resolvido; o round-trip inteiro (LLM emite `FunctionCallContent` → framework
invoca `AIFunction.InvokeAsync` → resultado vira `FunctionResultContent`
enviado de volta → loop repete até não haver mais chamadas) já é
responsabilidade do framework, do mesmo jeito que seria para qualquer tool
futura não-MCP.

**Alternativa considerada e descartada**: implementar uma classe própria
(`McpToolAdapter : AIFunction` ou similar) que envolvesse um `McpClientTool`
e delegasse `InvokeAsync` para `CallToolAsync`. Rejeitada porque
`McpClientTool` já É essa classe — construir uma segunda camada de adaptação
sobre algo que já implementa o contrato exato necessário seria indireção sem
propósito, o oposto do princípio do projeto de não introduzir abstração sem
necessidade concreta.

### Decision 2: Filtragem por `allowedTools` — interseção viva, sem tratamento especial para conjunto vazio

Para cada `AgentMcpServer` vinculado ao agente cujo `McpServer.IsActive` for
`true`, o resolver conecta ao servidor, executa `tools/list` ao vivo (mesmo
princípio de "sem cache" já usado em `apps/api`), e cruza o resultado com
`AllowedTools` persistido — só as tools presentes nas duas listas (nome
exato) entram no conjunto final oferecido ao LLM.

- Uma tool presente em `AllowedTools` mas que o servidor não oferece mais
  (drift entre o momento do `PUT /agents/{id}/mcp-servers` em `apps/api` e a
  execução real) é simplesmente excluída da interseção, sem erro — este é
  exatamente o cenário que o Non-Goal de `backend-mcp-selecao-tools` previa
  ("só detectado numa execução real... change futura
  `apps-workers-execucao-mcp`").
- `AllowedTools` vazio produz zero tools daquele servidor — cai naturalmente
  da interseção (interseção de um conjunto com o vazio é vazia), sem
  `if (allowedTools.Count == 0)` explícito em lugar nenhum.
- `McpServer.IsActive == false` exclui o servidor inteiro do processo de
  resolução **antes** de qualquer tentativa de conexão — é um filtro local
  (leitura do banco), não uma falha de conectividade (ver Decision 5, que
  trata do caso em que a conexão é tentada e falha).

**Alternativa considerada e descartada**: nenhuma — esta decisão é a
aplicação direta do contrato já fixado por `agent-mcp-binding`
(`allowedTools` como allow-list, "tool inexistente no servidor... fora de
escopo aqui" já registrado como Non-Goal na change anterior). Não há
alternativa de design real a avaliar; registrado como Decision explícita só
para deixar por escrito onde exatamente a interseção acontece e por que
nenhum dos dois casos de borda (drift, vazio) precisa de tratamento
especial.

### Decision 3: Colisão de nome de tool entre servidores diferentes — prefixo incondicional via `McpClientTool.WithName`

Confirmado por reflection + XML doc: `McpClientTool.WithName(string)` existe
especificamente para este problema. Doc oficial do método: *"This method is
useful for optimizing the tool name for specific models or for prefixing the
tool name with a namespace to avoid conflicts... Preventing name collisions
when using tools from multiple sources."* — e confirma explicitamente que
`CallAsync`/`InvokeAsync` continuam chamando o nome **original** da tool no
servidor MCP real; só o nome exposto ao modelo (e, portanto, o nome que o
LLM usa para chamar a tool, e que o `FunctionInvokingChatClient` usa para
casar a chamada com o `AITool` correto na lista) muda.

O resolver aplica `tool.WithName($"{mcpServer.Name}__{tool.Name}")`
(separador `__`, improvável de colidir com caracteres válidos de nome de
tool/servidor) **incondicionalmente**, para toda tool resolvida de todo
servidor — não só quando uma colisão real é detectada entre dois servidores
do mesmo agente.

**Alternativa considerada e descartada**: detectar colisão (dois servidores
vinculados ao mesmo agente oferecendo uma tool de mesmo nome) e prefixar
somente nesse caso, mantendo nomes "limpos" (sem prefixo) quando não há
conflito. Rejeitada por exigir uma passada extra de detecção (agrupar por
nome, contar duplicatas) para um ganho puramente cosmético (nomes um pouco
mais curtos no caso comum) — prefixar sempre é mais simples de implementar e
testar (comportamento uniforme, sem ramificação condicional), e como efeito
colateral positivo dá ao LLM contexto explícito de qual servidor está
oferecendo cada tool, sem custo adicional.

### Decision 4: Ciclo de vida da conexão MCP — vive durante todo o `RunAsync`, não só durante a descoberta

Uma conexão nova por servidor a cada execução de task, sem cache entre
execuções diferentes — mas a investigação técnica encontrou uma nuance
importante sobre **o que conta como "a execução"**: como `McpClientTool`
mantém uma referência ao `McpClient` que o criou, e `CallAsync`
(`AIFunction.InvokeAsync` por baixo) é invocado **durante**
`aiAgent.RunAsync(...)` — potencialmente em múltiplas iterações do loop de
tool-calling do `FunctionInvokingChatClient`, já que uma tool pode ser
chamada mais de uma vez no mesmo turno —, a conexão **não pode** ser
aberta-e-descartada durante a etapa de descoberta, diferente do padrão usado
em `apps/api` (`McpConnectionTester.ListToolsAsync`, onde `await using var
client = ...` fecha a conexão na mesma chamada porque a tool ali nunca
precisa ser de fato invocada depois — é só descoberta/validação).

A conexão precisa ficar viva por todo o `RunAsync`. Isso muda a forma do
resolver: `IMcpToolSetResolver.ResolveAsync(agentId, ct)` não retorna só
`IReadOnlyList<AITool>` — retorna um `McpToolSet` (`IAsyncDisposable`) que
expõe `Tools` e é dono do ciclo de vida de todos os `McpClient` abertos para
aquela resolução. `AgentExecutionService` usa `await using` no escopo que
envolve `RunAsync`:

```
await using var toolSet = await mcpToolSetResolver.ResolveAsync(agent.Id, cancellationToken);
var aiAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    ...
    ChatOptions = new ChatOptions { Instructions = agent.Instructions, Tools = toolSet.Tools.ToList() },
    ...
});
...
var response = await aiAgent.RunAsync(userText, session, options: null, cancellationToken);
// toolSet.DisposeAsync() fecha todas as conexões MCP ao sair do escopo (try/finally implícito do await using)
```

Nenhuma conexão é reaproveitada entre execuções de task diferentes (cada
`ExecuteAsync` cria seu próprio `McpToolSet`), e dentro da mesma execução
todas as conexões (de todos os servidores vinculados) são abertas uma única
vez, na resolução, e fechadas uma única vez, ao final — sem pool, sem cache,
sem reconexão automática.

**Alternativa considerada e descartada**: abrir e fechar uma conexão nova a
cada chamada de tool individual (dentro de `AIFunction.InvokeAsync`), em vez
de manter a conexão viva pelo `RunAsync` inteiro. Rejeitada por dois
motivos: (1) mais caro em latência quando a mesma tool (ou tools do mesmo
servidor) é chamada mais de uma vez no mesmo turno — cada chamada pagaria um
handshake MCP completo (`initialize`) do zero; (2) não é o que o SDK
naturalmente favorece — `McpClientTool` já encapsula uma referência a um
`McpClient` vivo por design (é assim que `CallAsync` funciona), lutar contra
esse desenho para forçar reconexão por chamada exigiria descartar o
`McpClientTool` retornado por `ListToolsAsync` e reconstruir manualmente uma
tool equivalente a cada `InvokeAsync` — complexidade sem benefício
correspondente.

### Decision 5: Degradação por servidor MCP com problema — capturada dentro do resolver, nunca propagada para o `catch` genérico de `AgentExecutionService`

`AgentExecutionService.ExecuteAsync` já tem um `catch (Exception)` amplo que
mapeia qualquer falha para `FailAsync` (task termina como `failed`) — mas
esse catch existe para falhas *depois* que a execução já está em andamento
(chamada ao LLM, serialização de sessão, etc.), não para "um dos N
servidores MCP vinculados está fora do ar".

A falha de conexão com um `McpServer` específico (host inalcançável, timeout,
handshake falho, credencial que não decifra com a chave atual configurada)
durante a fase de resolução do conjunto de tools é capturada **dentro do
próprio `IMcpToolSetResolver`**, por servidor — nunca deixada propagar. O
servidor problemático fica de fora do `McpToolSet` daquela execução (na
prática: zero tools contribuídas por ele), e a resolução continua
normalmente para os demais servidores vinculados. A execução do agente segue
com o conjunto de tools que sobrou — inclusive com zero tools no total, se
todos os servidores vinculados falharem, caso em que o agente simplesmente
roda sem nenhuma tool disponível (mesmo comportamento de um agente sem
nenhum `McpServer` vinculado).

Cada exclusão é registrada via `ILogger.LogWarning` (motivo + `McpServerId`
+ nome do servidor) — visível para quem opera, mesmo padrão de
visibilidade mínima já usado para outras degradações silenciosas do projeto
(ex. falha de resumo de histórico). Não é reportado nenhum artefato/campo
visível ao usuário final da task (não é uma falha da task, é uma
degradação de capacidade).

`McpServer.IsActive == false` (Decision 2) não passa por este tratamento —
é um filtro que nunca chega a tentar conectar, então não há exceção para
capturar; a distinção importa porque um servidor inativo é um estado
configurado deliberadamente (mesmo espírito de `Agent.IsActive`), enquanto
uma falha de conexão é um problema não planejado.

**Alternativa considerada e descartada**: deixar a falha de conexão de
qualquer servidor propagar e falhar a task inteira (mesmo tratamento que
qualquer outra exceção em `ExecuteAsync`). Rejeitada porque um único
`McpServer` de terceiros fora do ar não deveria impedir a task de usar as
tools de outros servidores vinculados ao mesmo agente, nem impedir o agente
de responder sem nenhuma tool MCP — mesmo princípio de degradação graciosa já
aplicado a outras dependências opcionais do pipeline de execução (resumo de
histórico), explicitamente citado como precedente no proposal desta change.

### Decision 6: Limite de chamadas de tool por turno — proteção nativa do `FunctionInvokingChatClient`, nenhum código customizado

Confirmado por reflection + XML doc:
`Microsoft.Extensions.AI.FunctionInvokingChatClient.MaximumIterationsPerRequest`
já existe (propriedade pública, `int`, default **40**), documentada
exatamente como o stop condition procurado: *"Each request to this
FunctionInvokingChatClient might end up making multiple requests to the
inner IChatClient... This loop is repeated until there are no more function
calls to make, or until another stop condition is met, such as hitting
MaximumIterationsPerRequest."*

Como o `FunctionInvokingChatClient` já é inserido automaticamente pelo
`ChatClientAgent` (Decision 1), essa proteção já existe no pipeline sem
nenhuma linha de código nova — não há necessidade de um contador próprio,
nem de expor `MaximumIterationsPerRequest` como configurável por agente
nesta fatia (mesmo espírito de "constante global, não configurável por
agente" já aplicado a `MaxHistoryMessages`/`SummarizationTurnThreshold` em
`AgentExecutionService.cs`). Registrado como Decision explícita
propositalmente para documentar que a investigação prévia confirmou a
proteção nativa — evita que uma implementação futura reintroduza um limiar
customizado por desconhecer que o framework já resolve isso.

**Alternativa considerada e descartada**: implementar um contador próprio de
iterações de tool-calling (ex. decorator customizado em torno do
`IChatClient`, ou verificação dentro do `McpToolSet`). Rejeitada por
duplicar uma proteção que já existe nativamente no pipeline padrão do
`ChatClientAgent`, sem nenhum requisito concreto que justifique um valor
diferente de 40 para este projeto nesta fatia.

## Estrutura de pastas proposta

```
apps/workers/src/Buteco.Workers/
  Mcp/
    Entities/
      McpServer.cs                 # novo: mirror read-only de apps/api
      McpServerAuthType.cs         # novo: mirror do enum
      AgentMcpServer.cs            # novo: mirror, inclui AllowedTools (jsonb)
    Security/
      IMcpCredentialCipher.cs      # novo: mirror da interface
      AesGcmMcpCredentialCipher.cs # novo: mirror da implementação AES-GCM
      McpCryptoOptions.cs          # novo: mirror (Mcp:CredentialEncryptionKey)
    IMcpToolSetResolver.cs         # novo: interface, mesmo padrão de IChatClientResolver
    McpToolSetResolver.cs          # novo: implementação — conecta, lista, filtra,
                                    # prefixa (Decision 3), degrada por servidor (Decision 5)
    McpToolSet.cs                  # novo: IAsyncDisposable, Tools + McpClients (Decision 4)
    McpTransportFactory.cs         # novo: constrói HttpClientTransport/McpClientOptions,
                                    # mesmo padrão de McpConnectionTester.BuildTransport
                                    # em apps/api (não reaproveitado, replicado)
  Agents/
    AgentExecutionService.cs       # alterado: injeta IMcpToolSetResolver, popula
                                    # ChatOptions.Tools, await using no McpToolSet
                                    # durante RunAsync
  Infrastructure/
    AppDbContext.cs                # alterado: DbSet<McpServer>, DbSet<AgentMcpServer>,
                                    # mapeamento fluente das duas tabelas
    Migrations/
      <timestamp>_AddMcpServerCatalog.cs  # novo: mirror de schema, só para
                                            # ferramental do EF Core (detectar
                                            # divergência) — nunca aplicada em
                                            # runtime, mesmo padrão já documentado
                                            # no comentário de AppDbContext.cs
  Options/
    McpCryptoOptions.cs             # (ou dentro de Mcp/Security/, ver acima —
                                     # mesma localização usada em apps/api)
  Program.cs                        # alterado: DI de IMcpCredentialCipher,
                                     # HttpClient nomeado para MCP, IMcpToolSetResolver

apps/workers/tests/Buteco.Workers.Tests/
  Mcp/
    McpToolSetResolverTests.cs      # novo: filtragem, degradação, prefixo, round-trip
    Support/
      FakeMcpServerHttpMessageHandler.cs  # novo: simula tools/list e tools/call
                                            # via JSON-RPC, fixture próprio de
                                            # apps/workers (não reaproveita o de
                                            # apps/api — isolamento entre apps)
```

Nenhum conteúdo novo em `libs/` — a lógica de conexão MCP não é compartilhada
entre `apps/api` e `apps/workers` (mesma regra já aplicada ao mirror de
`Agent`/`ChatClientOptions`); os dois apps replicam o mesmo padrão de
construção de transporte de forma independente, cada um com o nível de
funcionalidade que precisa (descoberta/validação efêmera em `apps/api`,
conexão viva e chamável em `apps/workers`).

## Risks / Trade-offs

- **[Trade-off] Latência de N handshakes MCP reais a cada execução de task**
  (N = número de `McpServer`s distintos vinculados e ativos para o agente) →
  Aceito: mesmo princípio de "sem cache" já aceito em `apps/api` para
  `tools/list`; aqui o custo adicional é pago uma vez por execução (não por
  chamada de tool, ver Decision 4), e é proporcional ao número de servidores
  realmente vinculados — um agente sem `McpServer` vinculado não paga nada.
- **[Risco] Conexão MCP mantida viva durante todo o `RunAsync` aumenta a
  janela em que uma falha de rede tardia (servidor cai no meio da execução,
  depois da resolução inicial ter tido sucesso) pode interromper uma
  chamada de tool em andamento** → Mitigação parcial: esse cenário não é
  coberto pela degradação graciosa da Decision 5 (que só cobre falha *na
  resolução*, antes de `RunAsync` começar) — uma falha de tool call em
  andamento propaga como exceção normal e cai no `catch (Exception)` já
  existente de `ExecuteAsync`, terminando a task como `failed`. Aceito como
  comportamento correto: se uma tool que o LLM decidiu usar no meio da
  conversa parar de responder, não há um resultado parcial sensato para
  devolver — falhar a task é mais correto que silenciosamente omitir o
  resultado da tool e deixar o LLM inventar uma resposta.
- **[Risco] `McpClient` não documenta explicitamente garantia de
  thread-safety para chamadas concorrentes da mesma sessão** — relevante
  porque `ChatOptions.AllowMultipleToolCalls` permite ao
  `FunctionInvokingChatClient` invocar mais de uma tool call do mesmo turno
  potencialmente em paralelo → Mitigação: o protocolo MCP sobre HTTP
  correlaciona requisição/resposta por id (JSON-RPC), o que sustenta uso
  concorrente na prática; não foi encontrada documentação que proíba
  explicitamente chamadas concorrentes em uma mesma sessão. Se um problema
  real for observado em teste/produção, a mitigação é desabilitar
  `AllowMultipleToolCalls` para tools MCP especificamente (ajuste local,
  não uma mudança de arquitetura) — não é um risco que bloqueia esta
  fatia.
- **[Trade-off] Duplicação de lógica de transporte MCP entre `apps/api` e
  `apps/workers`** (Decision 4, `McpTransportFactory` replica
  `McpConnectionTester.BuildTransport`) → Aceito: mesma regra de isolamento
  já aplicada a toda a base (nenhum `ProjectReference` entre apps); o
  formato do transporte (`HttpClientTransportOptions`, `ProtocolVersion`
  fixo) é pequeno o suficiente para replicar sem risco real de divergência
  silenciosa — e diverge de propósito no que realmente importa (descarte
  imediato em `apps/api` vs. vida longa em `apps/workers`, Decision 4).
- **[Risco] Servidor MCP malicioso ou comprometido pode retornar resultado de
  tool adversarial (prompt injection indireto) que influencia o
  comportamento do LLM** → Aceito como risco conhecido e documentado pelo
  próprio framework (XML doc de `ChatClientAgent`: *"Data retrieved by
  tools... may contain adversarial content designed to influence LLM
  behavior or exfiltrate data through tool calls"*); fora do escopo desta
  fatia mitigar (não há sandboxing ou validação de conteúdo de resultado de
  tool aqui) — mesmo nível de confiança implícita já depositado em
  `McpServer` cadastrado por um operador via `apps/api`.

## Migration Plan

Migration EF Core aditiva em `apps/workers`
(`<timestamp>_AddMcpServerCatalog`), espelhando exatamente o schema já
criado por `apps/api` (`mcp_servers`, `agent_mcp_servers`, incluindo a coluna
`allowed_tools` jsonb) — mesmo padrão de `AddAgentProviderModel.cs` já
existente em `apps/workers/Infrastructure/Migrations/`: existe para
ferramental do EF Core (detectar divergência de schema, gerar o model
snapshot), **nunca é aplicada em runtime** por este app (só `apps/api`
chama `Database.MigrateAsync`/roda `dotnet ef database update` contra o
banco compartilhado).

Nenhum dado a migrar — as tabelas já existem no schema físico (criadas pelas
migrations de `apps/api`); a migration de `apps/workers` só ensina o
`AppDbContext` local a reconhecer as tabelas que já estão lá.

Rollback = reverter a migration local de `apps/workers`
(`dotnet ef database update <migration anterior>` rodado **contra o
`AppDbContext` de `apps/workers`**, que nunca é executado em produção de
qualquer forma) — sem nenhum efeito no banco físico compartilhado, que
continua gerenciado exclusivamente por `apps/api`.

## Open Questions

Nenhuma pergunta de negócio/produto em aberto — as seis Decisions acima
cobrem os pontos que precisavam de investigação prévia (`/opsx:explore`,
incluindo confirmação direta contra os assemblies reais do SDK) antes deste
design.md. Pontos de implementação a critério de quem implementar (não
bloqueiam o apply): separador exato do prefixo de nome de tool na Decision 3
(`__` sugerido, mas qualquer separador que não colida com caracteres válidos
de nome serve); nome exato da constante do `HttpClient` nomeado usado por
`McpTransportFactory` (mesmo espírito de
`McpConnectionTester.HttpClientName` em `apps/api`).
