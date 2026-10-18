**Issue:** #160

## Why

Hoje o `McpServer` aceita só `None` e `BearerToken`. Um serviço MCP que
queremos usar exige um token fixo no header `X-API-TOKEN`, e a plataforma
sempre o envia em `Authorization: Bearer`. Sem um tipo que envie o segredo num
header de nome configurável, esse servidor não pode ser cadastrado nem
executado.

O transporte do SDK já envia headers arbitrários por
`HttpClientTransportOptions.AdditionalHeaders`. O trabalho está no cadastro, na
validação e na leitura pelo `apps/workers`. A verificação feita antes de
escrever esta proposta (`design.md`, seção "Verificações") achou três coisas
que a issue não previa e que decidem o desenho:

- o SDK **não valida** o nome do header. Quando o .NET recusa o nome, o SDK
  lança uma `InvalidOperationException` **com o valor do segredo na
  mensagem**. Essa mensagem chega à resposta do teste de conexão em
  `apps/api` e ao log de `apps/workers`;
- o SDK **aceita** `Host`, `Accept` e os headers `Mcp-*`, e em colisão
  **acrescenta** o valor ao header que ele próprio envia;
- um `apps/workers` que não conhece o valor novo do enum **não exclui só
  aquele servidor**. A materialização lança exceção fora do `try` da
  resolução, e a resolução de tools inteira do agente falha.

## What Changes

- **apps/api**: tipo novo `CustomHeader` em `McpServerAuthType`, gravado como
  a string `"CustomHeader"` no banco e no JSON. Coluna nova em claro
  `AuthHeaderName` em `mcp_servers`, devolvida em `McpServerResponse` como
  `authHeaderName`. O segredo continua em `EncryptedCredential`, cifrado e
  nunca devolvido.
- **apps/api**: validação do nome do header (sintaxe de token HTTP, critério
  do próprio .NET para header de requisição, lista de reservados) e do valor do
  token (só ASCII visível). A validação vale no cadastro, na atualização e no
  teste de configuração não salva. Um nome recusado responde 400 **antes** de
  qualquer conexão.
- **apps/api**: `authType` passa a aceitar só o **nome** de um membro
  definido. Hoje `"2"`, `"99"` e `"None,BearerToken"` passam. A mensagem de
  valores aceitos deixa de ser texto fixo.
- **apps/api**: **BREAKING (comportamento de API)**: trocar o `authType` entre
  dois tipos com credencial (`BearerToken` ↔ `CustomHeader`) exige credencial
  nova. O "deixe em branco para manter" passa a valer só quando o tipo não
  muda.
- **apps/api**: `McpConnectionTester.BuildTransport` passa a montar o header
  customizado.
- **apps/workers**: membro `CustomHeader` no enum espelho, propriedade
  `AuthHeaderName` no modelo espelho, migração espelho (nunca aplicada em
  runtime), e `McpTransportFactory.BuildTransport` passa a montar o header
  customizado.
- **apps/workers**: valor de `AuthType` desconhecido (deploy fora de ordem,
  rollback só do workers) e nome de header que o .NET recusa passam a
  **excluir só aquele servidor** da execução, com log sem o segredo. Hoje o
  primeiro derruba a resolução inteira, e o segundo grava o segredo no log.
- **tests/** (projeto de compatibilidade entre apps): teste de acordo
  api ↔ workers com a linha gravada pelo `apps/api` de verdade e lida pelo
  `apps/workers` de verdade, para cada valor do enum, e com o header
  customizado chegando ao servidor fake.
- **apps/api**: `McpConnectionTester` revalida o nome do header antes de
  montar o transporte. Assim, o re-teste salvo, a descoberta de tools e o PUT
  de vínculo não devolvem o segredo na resposta quando a linha salva tem um
  nome que o .NET recusa (`design.md`, V9 e D3).
- **apps/frontend**: **fora desta change**. O formulário, a tabela e o card de
  configuração são a **#168**, sequenciada depois desta (`design.md`, D7).
- **Fora do escopo**: validar o valor da credencial `BearerToken` é a
  **#167**.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `mcp-server-catalog`: o cadastro, a atualização, a consulta e o teste de
  configuração passam a conhecer o tipo `CustomHeader` e o campo
  `authHeaderName`. A atualização muda a regra de manter a credencial na troca
  de tipo. A validação de `authType` passa a ser por nome.
- `mcp-tool-execution`: requisito novo de autenticação da conexão na execução,
  com `Authorization: Bearer` e com o header customizado. A degradação por
  servidor passa a cobrir tipo de autenticação desconhecido e nome de header
  recusado, sem segredo no log.

## Impact

- **apps/api**: `McpServers/Entities` (enum e entidade),
  `McpServers/Endpoints/McpServerEndpoints.cs`, requests e response de
  `McpServers`, os handlers de create e update, `McpConnectionTester`, os dois
  handlers que testam ou listam a partir do registro salvo,
  `Infrastructure/AppDbContext.cs` e uma migração aditiva
  (`AddMcpServerAuthHeaderName`, coluna anulável, sem backfill).
- **apps/workers**: `Mcp/Entities` (enum e modelo espelho),
  `Mcp/McpTransportFactory.cs`, `Mcp/McpToolSetResolver.cs`,
  `Infrastructure/AppDbContext.cs` e a migração espelho.
- **tests/CrossAppTaskStoreCompatibility.Tests**: cenários novos de acordo
  sobre `mcp_servers`.
- **Deploy**: `apps/workers` precisa subir no mesmo `docker compose up` que o
  `apps/api`, e o rollback do `apps/workers` sozinho para antes desta change
  não é seguro (`design.md`, D5). Nenhuma variável de ambiente nova.
- **Dependências**: nenhuma nova.
- **Nenhuma referência de projeto** entre `apps/api`, `apps/workers` e
  `apps/frontend`. O único projeto que referencia dois apps continua sendo o de
  compatibilidade em `tests/`, que já existe com esse fim.
