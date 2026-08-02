## Context

`apps/api` hoje só conhece agentes (`Agent`, CRUD via CQRS/`Mediator`,
`isActive` para ativar/desativar sem excluir) e o catálogo estático de
provedores de LLM (`Buteco.ProviderCatalog`, chaves sempre vindas de
configuração/env var, nunca do banco — ver
`openspec/changes/backend-multi-provedor-llm/design.md`). Não existe hoje
nenhuma relação N:N no schema, nem qualquer conceito de MCP.

Esta change adiciona o catálogo de servidores MCP remotos (HTTP) e o vínculo
N:N com agentes. Investigação prévia (via `/opsx:explore`) resolveu as três
decisões abaixo antes de qualquer código ser escrito; ver seção Decisions
para o raciocínio completo e as alternativas descartadas.

A execução real — o Worker descobrindo e chamando tools de um MCP vinculado
numa execução de agente — é uma change futura e não é tocada aqui, com uma
única exceção: o endpoint de teste de configuração precisa falar o
protocolo MCP de verdade (handshake `initialize`) para validar que uma
configuração está correta, então a dependência do SDK oficial do MCP entra
nesta fatia.

## Goals / Non-Goals

**Goals:**
- CRUD completo de `McpServer` (nome, descrição, URL, tipo de autenticação,
  credencial, `isActive`) em `apps/api`, mesmo padrão CQRS/validação/
  idempotência já usado em `Agent`.
- Credencial nunca trafega de volta em nenhuma resposta de leitura, e nunca
  é persistida em texto claro.
- Vínculo N:N entre `Agent` e `McpServer`, consultável nos dois sentidos
  (servidores de um agente; e, implicitamente, quais agentes usam um dado
  servidor, ainda que sem endpoint dedicado para essa segunda direção nesta
  fatia).
- Endpoint de teste de configuração que valida um servidor MCP com um
  handshake real de protocolo, cobrindo tanto configuração ainda não salva
  quanto um `McpServer` já cadastrado.
- Registrar explicitamente que "um agente não deve usar um MCP inativo" é
  responsabilidade da change de execução futura — esta change não impõe
  esse bloqueio em lugar nenhum porque não há execução aqui.

**Non-Goals:**
- MCP local via stdio — fora de escopo permanentemente. `apps/workers` roda
  containerizado e não há um bom modelo para spawnar/gerenciar processos
  locais dentro do container; isso não é uma limitação desta fatia, é uma
  decisão de arquitetura permanente do projeto.
- Conexão automática/implícita em `Create`/`Update` de `McpServer` — só a
  ação explícita de teste conecta de verdade.
- Descoberta ou uso de tools MCP numa execução real de agente (fica para a
  change de execução).
- Filtro de tool individual dentro de um MCP vinculado a um agente.
- Qualquer UI em `apps/frontend`.
- Qualquer mudança em `apps/workers`.
- Exclusão de `McpServer` (definitiva ou soft delete) — só
  ativação/desativação, mesmo padrão de `Agent`.
- Bloquear vínculo de um agente com um `McpServer` inativo — permitido
  nesta fatia (ver Decision 3).
- Persistir resultado do teste de configuração (sem coluna "último teste"/
  "último resultado" — ver Decision 2).

## Decisions

### Decision 1: Criptografia da credencial — AES-GCM manual com chave de env var, não Data Protection API

`Credential` é criptografada com AES-GCM (autenticado, nonce aleatório de 96
bits por valor, gerado e armazenado junto do ciphertext) usando uma única
chave simétrica de 256 bits lida de configuração/env var
(`Mcp:CredentialEncryptionKey`, base64), via `IOptions<McpCryptoOptions>` —
mesmo padrão exato já usado em `apps/workers` para
`ChatClientOptions.ApiKey`/`AnthropicOptions`/`GeminiOptions`: um secret,
uma env var, nunca lido do banco. Implementado atrás de uma interface
(`IMcpCredentialCipher.Encrypt(string)`/`Decrypt(string)`) para não acoplar
o restante do código à escolha de algoritmo e para permitir teste
determinístico.

**Alternativa considerada e descartada: `Microsoft.AspNetCore.DataProtection`.**
`IDataProtector.Protect`/`Unprotect` é, de fato, uma API de propósito geral
(confirmado — não é só para cookies/sessão) e seria o caminho idiomático do
framework para "criptografar antes de persistir" em outros contextos. Mas
seu modelo não é "uma chave raiz de configuração decifra tudo": ela mantém
um *key ring* versionado e exige decidir, separadamente, (a) onde esse ring
é persistido e (b) como ele é protegido em repouso.

- Persistir o ring no mesmo Postgres via `PersistKeysToDbContext` é a opção
  óbvia (já temos o banco), mas a própria documentação do ASP.NET Core
  avisa que especificar um repositório explícito **desregistra a
  criptografia em repouso padrão** — o ring fica gravado sem proteção. Isso
  deixaria, na mesma base, tanto o `Credential` criptografado quanto a
  chave que o decifra: um dump do Postgres compromete tudo, anulando o
  propósito de criptografar.
- Corrigir isso exige `ProtectKeysWithCertificate` com um certificado X.509
  vindo de fora do banco (ex. env var base64) — o que preserva o princípio
  "raiz de confiança nunca no banco", mas troca uma env var simples por um
  certificado inteiro: gerar, guardar, rotacionar, mais o pacote
  `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`, mais uma
  tabela/migration só para o key ring.

Ou seja: usar Data Protection API "direito" para este caso específico
(segredo de longa duração, precisa sobreviver restart/redeploy de
container, ameaça principal é leitura direta do Postgres) reintroduz uma
complexidade operacional equivalente ou maior do que implementar AES à mão
— só trocando "gerenciar uma chave" por "gerenciar um certificado". Como o
projeto já tem um padrão simples e testado para exatamente este problema
(secret único via env var, nunca no banco), AES-GCM manual foi escolhido
por manter esse padrão e não introduzir peça operacional nova.

### Decision 2: Endpoint de teste de configuração — handshake MCP real, efêmero, com SDK oficial

- **Duas formas de testar**: `POST /mcp-servers/test` recebe
  `Url`/`AuthType`/`Credential` direto no corpo, sem exigir que o
  `McpServer` exista (para validar antes de salvar); `POST
  /mcp-servers/{id}/test` re-testa um `McpServer` já cadastrado, usando a
  credencial já persistida (decifrada internamente, nunca exposta na
  resposta). Ambos delegam para a mesma lógica de conexão.
- **"Válido" = handshake real do protocolo MCP**, não só reachability HTTP.
  Usa o SDK oficial `ModelContextProtocol.Core` (NuGet, confirmado na
  versão `2.0.0`, GA, atual em agosto/2026 — pacote mínimo suficiente para
  ser só cliente: inclui cliente + tipos de baixo nível, sem os helpers de
  DI/hosting do pacote `ModelContextProtocol` nem o suporte a hospedar
  servidor do `ModelContextProtocol.AspNetCore`, nenhum dos dois necessário
  aqui). `McpClientFactory.CreateAsync(transport)` executa o `initialize`
  na conexão; para `AuthType = BearerToken`, o token vai em
  `HttpClientTransportOptions.AdditionalHeaders["Authorization"]`;
  `TransportMode.AutoDetect` (default) tenta Streamable HTTP e cai para SSE,
  cobrindo servidores remotos genéricos sem exigir que o operador saiba de
  antemão qual transporte o servidor fala.
- **Resultado é efêmero**: a resposta do teste (sucesso/falha + motivo) não
  é persistida — sem coluna "último teste"/"último resultado" em
  `McpServer`. Evita mais um estado para manter sincronizado sem
  necessidade concreta hoje; se um requisito futuro pedir histórico de
  testes, isso é uma migration aditiva, não uma decisão que precisa ser
  antecipada agora.
- **Distinção de motivo de falha**: falha de rede/conexão chega como
  `IOException` (`ClientTransportClosedException`) — mapeada para "host
  inalcançável" ou similar; falha HTTP 401/403 durante o handshake é
  mapeada para "credencial rejeitada"; qualquer outra exceção do SDK cai
  num motivo genérico. A resposta do endpoint (`McpConnectionTestResult`)
  expõe um enum de motivo + mensagem, não a exceção crua.
- **Falha ao decifrar a credencial persistida** (`POST
  /mcp-servers/{id}/test`, único dos dois endpoints de teste que lê
  `EncryptedCredential` do banco): `IMcpCredentialCipher.Decrypt` pode
  lançar — o cenário mais realista é `Mcp:CredentialEncryptionKey` ter
  mudado desde que a credencial foi salva (ver risco de perda de chave em
  Risks/Trade-offs), não só payload corrompido. `TestSavedMcpServerConnectionCommandHandler`
  captura essa exceção especificamente, antes de tentar qualquer conexão
  de rede, e mapeia para `McpConnectionTestFailureReason.CredentialDecryptionFailed`
  — um motivo distinto de "credencial rejeitada pelo servidor MCP", porque
  a causa raiz é local (chave de criptografia local) e não algo que o
  servidor remoto reportou. Não propagar essa exceção como erro não
  tratado (500) — é uma falha de teste esperada e reportável, mesma
  categoria dos outros motivos de falha do endpoint.
- **Isolamento de rede real**: a lógica de conexão fica atrás de
  `IMcpConnectionTester` (interface só para permitir substituição em
  teste, mesmo espírito de `IChatClientResolver` em `apps/workers` —
  construído por chamada, sem cache). Isso não é estritamente exigido pelo
  SDK — `HttpClientTransport` já aceita um `HttpClient` customizado, então
  os três cenários de teste (handshake bem-sucedido, host inalcançável,
  credencial rejeitada) podem ser simulados com um `HttpMessageHandler`
  fake sem nenhuma abstração própria — mas a interface é mantida por
  consistência com o padrão já estabelecido no projeto e para manter o
  handler do `Command` desacoplado dos detalhes do SDK.

### Decision 3: `IsActive` em `McpServer` — mesmo padrão de `Agent`, sem bloqueio de vínculo

`McpServer.IsActive` segue exatamente `Agent.IsActive`: bool, default
`true`, `Activate()`/`Deactivate()` via `POST /mcp-servers/{id}/activate` e
`/deactivate`, idempotentes, sem exclusão do registro em nenhum dos dois
estados.

Vincular um agente a um `McpServer` inativo **é permitido** nesta fatia —
configurar não é bloqueado pelo estado inativo, só o uso em tempo de
execução seria, e execução não existe nesta change (mesmo raciocínio já
aplicado a `Agent.IsActive`: um agente inativo pode ser editado
normalmente, só não deveria processar tasks novas).

**Registro explícito para a change de execução futura**: a regra "um
agente não deve usar tools de um `McpServer` inativo numa execução real" é
um Goal desta funcionalidade como um todo, mas **não é implementada aqui**
porque não há execução nesta fatia. A change seguinte (execução) é
responsável por checar `McpServer.IsActive` (e provavelmente também
`Agent.IsActive`, mesmo problema) antes de usar um MCP vinculado.

### Decision 4: Forma do vínculo agente↔MCP — substituição do conjunto inteiro via `PUT`

`PUT /agents/{id}/mcp-servers` recebe a lista completa de `mcpServerId`
que o agente deve passar a ter vinculados, substituindo o conjunto anterior
inteiro (remove os que saíram, adiciona os que entraram, idempotente se
enviado o mesmo conjunto duas vezes). `McpServerId`s inexistentes ou de
servidor inativo são aceitos (inativo, ver Decision 3) ou rejeitados
(inexistente → 400, mesmo padrão de validação usado no restante da API).

**Alternativa considerada e descartada**: `POST`/`DELETE` individuais
(`POST /agents/{id}/mcp-servers/{mcpServerId}`,
`DELETE /agents/{id}/mcp-servers/{mcpServerId}`) para adicionar/remover um
vínculo por vez. Mais granular e evita a necessidade do cliente conhecer o
conjunto inteiro antes de mudar um item, mas multiplica endpoints para uma
relação sem dado próprio além das duas chaves estrangeiras, e diverge do
estilo já usado em `UpdateAgent` (que já substitui o registro inteiro em
vez de fazer patch parcial). Substituição do conjunto inteiro via `PUT` foi
escolhida por manter menos endpoints e por ser consistente com o padrão de
"update" já estabelecido no projeto; pode ser revisitada se, na prática de
uso, o padrão de "adicionar um MCP de cada vez" se mostrar mais comum do
que "redefinir o conjunto inteiro".

### Decision 5: Nível de detalhe de `AgentResponse.McpServers` — id + name

`AgentResponse` passa a incluir `McpServers: McpServerSummaryResponse[]`,
onde cada item tem `Id` e `Name` (não o objeto `McpServer` completo, e não
só o `Id`). Suficiente para renderizar uma lista legível sem forçar o
consumidor a manter o catálogo completo de `McpServer` em cache só para
mostrar o nome ao lado do vínculo, e sem vazar `Url`/`AuthType`/
`Description` num payload que não é sobre o `McpServer` em si (quem quiser
o registro completo usa `GET /mcp-servers/{id}`).

**Alternativa considerada e descartada**: só `mcpServerId[]`. Mais simples,
mas obriga qualquer consumidor a resolver nome via uma segunda chamada ou
manter cache do catálogo — custo desnecessário dado que `Name` já está
disponível na mesma query (`Include`/`join` no EF Core não tem custo
adicional relevante aqui).

### Decision 6: `PUT /mcp-servers/{id}` limpa `EncryptedCredential` ao transicionar `AuthType` para `None`

Quando `UpdateMcpServerCommand` muda `AuthType` de `BearerToken` (ou
qualquer valor que exija credencial) para `None`, `UpdateMcpServerCommandHandler`
SHALL limpar `EncryptedCredential` explicitamente (persistir `null`), em vez
de manter o valor cifrado anterior órfão no banco. A regra de "manter a
credencial existente quando nenhuma nova é enviada" (ver Requirement de
Atualização de servidor MCP) só se aplica quando `AuthType` permanece
exigindo credencial — não quando a transição torna a credencial
funcionalmente inútil.

**Por que registrar isso explicitamente em vez de deixar implícito**: sem
essa limpeza, um segredo cifrado continuaria persistido indefinidamente sem
nenhum uso funcional possível (nada no sistema decifra credencial de um
`McpServer` com `AuthType: None`), mas ainda dependente da mesma chave de
criptografia — outro motivo para perda de acesso ao valor não importar em
termos de segurança (o valor nunca mais é lido), mas o dado em si continuar
existindo é desnecessário e potencialmente confuso numa inspeção direta do
banco ("por que esse registro sem autenticação tem um blob cifrado
associado?").

**Alternativa considerada e descartada**: manter `EncryptedCredential`
inalterado na transição, já que ele nunca mais é lido enquanto `AuthType`
for `None`. Rejeitada por deixar um dado morto e potencialmente confuso sem
necessidade — limpar no momento da transição é uma operação trivial (um
`UPDATE` a mais no mesmo comando) contra um benefício claro de higiene de
dados.

## Estrutura de pastas proposta

```
apps/api/src/Buteco.Api/
  McpServers/
    Entities/
      McpServer.cs
    Security/
      IMcpCredentialCipher.cs
      AesGcmMcpCredentialCipher.cs
      McpCryptoOptions.cs
    Connectivity/
      IMcpConnectionTester.cs
      McpConnectionTester.cs
      McpConnectionTestResult.cs
      McpConnectionTestFailureReason.cs
    Commands/
      CreateMcpServer/
        CreateMcpServerCommand.cs
        CreateMcpServerCommandHandler.cs
      UpdateMcpServer/
        UpdateMcpServerCommand.cs
        UpdateMcpServerCommandHandler.cs
      ActivateMcpServer/
        ActivateMcpServerCommand.cs
        ActivateMcpServerCommandHandler.cs
      DeactivateMcpServer/
        DeactivateMcpServerCommand.cs
        DeactivateMcpServerCommandHandler.cs
      TestUnsavedMcpServerConnection/
        TestUnsavedMcpServerConnectionCommand.cs
        TestUnsavedMcpServerConnectionCommandHandler.cs
      TestSavedMcpServerConnection/
        TestSavedMcpServerConnectionCommand.cs
        TestSavedMcpServerConnectionCommandHandler.cs
    Queries/
      GetMcpServerById/
        GetMcpServerByIdQuery.cs
        GetMcpServerByIdQueryHandler.cs
      ListMcpServers/
        ListMcpServersQuery.cs
        ListMcpServersQueryHandler.cs
    Endpoints/
      McpServerEndpoints.cs
    Requests/
      CreateMcpServerRequest.cs
      UpdateMcpServerRequest.cs
      TestMcpServerConfigRequest.cs
    Responses/
      McpServerResponse.cs
      McpServerSummaryResponse.cs
      McpConnectionTestResponse.cs
  AgentMcpBindings/
    Entities/
      AgentMcpServer.cs
    Commands/
      ReplaceAgentMcpServers/
        ReplaceAgentMcpServersCommand.cs
        ReplaceAgentMcpServersCommandHandler.cs
    Endpoints/
      AgentMcpBindingEndpoints.cs
    Requests/
      ReplaceAgentMcpServersRequest.cs
  Agents/
    Responses/
      AgentResponse.cs                # alterado: novo campo McpServers
    Queries/
      GetAgentById/GetAgentByIdQueryHandler.cs   # alterado: Include do vínculo
      ListAgents/ListAgentsQueryHandler.cs       # alterado: Include do vínculo
  Infrastructure/
    AppDbContext.cs                   # alterado: DbSet<McpServer>,
                                       # DbSet<AgentMcpServer>, configuração
                                       # fluente das duas novas tabelas
    Migrations/
      <timestamp>_AddMcpServerCatalog.cs
```

`AgentMcpBindings` fica como módulo próprio (não dentro de `Agents/` nem de
`McpServers/`) porque é, conceitualmente, a segunda capability desta
change (`agent-mcp-binding`, distinta de `mcp-server-catalog`) — referencia
as duas entidades mas não pertence a nenhuma das duas. `Agents/Responses/AgentResponse.cs`
referencia `McpServers/Responses/McpServerSummaryResponse.cs` diretamente
(mesmo projeto `Buteco.Api`, sem violar o isolamento entre `apps/api`,
`apps/workers` e `apps/frontend`, que é a única fronteira que a regra do
monorepo protege).

Nenhum conteúdo novo em `libs/` — não há necessidade concreta de
compartilhar código entre `apps/api` e `apps/workers` nesta fatia
(`apps/workers` não é tocado).

## Risks / Trade-offs

- **[Risco] Perda da chave de criptografia (`Mcp:CredentialEncryptionKey`)
  torna toda credencial já salva irrecuperável** → Mitigação: mesma
  categoria de risco que perder uma API key de provedor de LLM hoje — a
  resposta operacional é reconfigurar o `McpServer` com uma nova
  credencial, não recuperar a antiga. Documentar isso no `.env.example` e
  no design, para não ser uma surpresa em produção.
- **[Trade-off] `PUT /agents/{id}/mcp-servers` substituindo o conjunto
  inteiro pode causar race condition** se dois clientes lerem o conjunto
  atual e escreverem versões divergentes ao mesmo tempo → Aceito por ora:
  mesma característica já presente em `PUT /agents/{id}` (substituição
  completa dos campos editáveis), sem controle de concorrência otimista em
  nenhum lugar da API hoje; não é uma regressão introduzida por esta
  change.
- **[Risco] `ModelContextProtocol.Core` é uma dependência nova e
  relativamente recente (SDK oficial, mas o ecossistema .NET para MCP
  ainda está amadurecendo)** → Mitigação: isolado atrás de
  `IMcpConnectionTester`, meramente usado no endpoint de teste (não em
  nenhum caminho crítico de execução); se o SDK tiver uma breaking change
  futura, o blast radius fica contido a essa única integração.
- **[Trade-off] Testar handshake MCP real via `HttpClient` fake nos testes
  de integração exige simular o payload JSON-RPC de `initialize`
  manualmente** (não existe um transporte de teste "batteries-included" no
  SDK oficial) → Aceito: o custo é um fixture de teste a mais
  (`FakeMcpServerHttpMessageHandler` ou similar), consistente com o nível
  de esforço já aceito em outros fixtures do projeto (`FakeTaskJobPublisher`).

## Migration Plan

Migration EF Core aditiva única (`AddMcpServerCatalog`): cria `mcp_servers`
e `agent_mcp_servers`, sem alterar nenhuma tabela existente. `AgentResponse`
ganha um campo novo (`mcpServers`, sempre presente, lista vazia quando o
agente não tem vínculo nenhum) — aditivo no shape JSON, mas marcado como
**BREAKING** no proposal porque consumidores que fazem parsing estrito
precisam tolerar um campo novo. Sem dado a migrar (tabelas novas, vazias).
Rollback = reverter a migration (`dotnet ef database update <migration
anterior>`), seguro porque nenhuma tabela existente foi alterada.

## Open Questions

Nenhuma pergunta de negócio/produto em aberto — as decisões acima cobrem
os pontos que precisavam de investigação antes do proposal. Pontos de
implementação que ficam a critério de quem implementar (não bloqueiam o
apply): nomes exatos de classes internas, formato exato da mensagem de
motivo de falha no teste de conexão.
