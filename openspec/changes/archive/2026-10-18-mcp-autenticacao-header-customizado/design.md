## Context

`McpServerAuthType { None, BearerToken }` existe duplicado em
`apps/api/src/Buteco.Api/McpServers/Entities/McpServerAuthType.cs` e
`apps/workers/src/Buteco.Workers/Mcp/Entities/McpServerAuthType.cs`. Os dois
`AppDbContext` mapeiam a coluna `AuthType` de `mcp_servers` com
`HasConversion<string>()` (`apps/api/.../Infrastructure/AppDbContext.cs:130`,
`apps/workers/.../Infrastructure/AppDbContext.cs:96`). O header
`Authorization: Bearer` é montado em dois lugares, replicados de propósito:
`McpConnectionTester.BuildTransport` (`apps/api/.../Connectivity/McpConnectionTester.cs:69`)
e `McpTransportFactory.BuildTransport`
(`apps/workers/.../Mcp/McpTransportFactory.cs:51`). Os dois usam
`HttpClientTransportOptions.AdditionalHeaders`.

A validação de `authType` está em `McpServerEndpoints.ValidateShape` e
`ValidateTestShape` (`apps/api/.../Endpoints/McpServerEndpoints.cs:174-213`),
com a mensagem fixa "Valores aceitos: None, BearerToken." e `Enum.TryParse`.
A credencial é obrigatória no create quando `AuthType != None` (`:49`). No
update, `UpdateMcpServerCommandHandler` mantém a credencial quando ela vem em
branco e limpa ao transicionar para `None` (`McpServer.UpdateDetails`, Decision
6 de `2026-08-02-backend-mcp-catalogo-vinculo`). `McpServerResponse` nunca
devolve credencial.

A issue #160 pede um tipo que envie um token fixo num header de nome
configurável (o caso real é `X-API-TOKEN`). A #161 (OAuth2 client
credentials) está na fila, sequenciada depois desta, e depende do lugar que
esta change escolhe para parâmetros não secretos de autenticação.

### Verificações (convenção 6)

Feitas antes de qualquer decisão. Os programas de medição ficaram fora do
repositório (scratchpad da sessão). As afirmações abaixo citam a fonte da
leitura ou o resultado medido.

**V1. O que o SDK faz com `AdditionalHeaders`.** Fonte: `ilspycmd` sobre
`apps/api/src/Buteco.Api/bin/Release/net10.0/ModelContextProtocol.Core.dll`
(2.0.0), mais uma medição com o SDK real e um handler capturando as
requisições.

- `StreamableHttpClientSessionTransport.CopyAdditionalHeaders` é chamado em
  **toda** requisição: no POST (`SendHttpRequestAsync`, que também leva a
  sonda `server/discover`), no GET de stream
  (`SendGetSseRequestWithRetriesAsync`) e no DELETE de sessão
  (`SendDeleteRequest`). O transporte SSE legado faz o mesmo no POST e no GET
  (`SseClientSessionTransport`, linhas 99 e 158 do código decompilado).
- Medido com `X-API-TOKEN`, `OwnsSession = true` e
  `EnableStandaloneGetStream = true`: o header chegou em `server/discover`,
  `initialize`, GET de stream, `notifications/initialized`, `tools/list` e
  DELETE. **Confirma a premissa da issue.**

**V2. Validação de nome no SDK.** `ValidateHeaderName` existe só em
`ModelContextProtocol.Server.McpHeaderAttribute`, do lado **servidor**. O lado
cliente não valida nada. `CopyAdditionalHeaders` primeiro acrescenta os
headers do próprio SDK (`Mcp-Session-Id`, `MCP-Protocol-Version`,
`Last-Event-ID`). Depois chama `headers.TryAddWithoutValidation(nome, valor)`
para cada header adicional e, se isso devolver `false`, lança:

```
throw new InvalidOperationException($"Failed to add header '{Key}' with value '{Value}' from AdditionalHeaders.");
```

**A mensagem carrega o valor do segredo.** Depois disso, o POST ainda chama
`AddMcpRequestHeaders`, que acrescenta `Mcp-Method`, `Mcp-Name` e `Mcp-Param-*`.

Medido, nome a nome, com o valor `VALOR-SECRETO-123`:

| nome | resultado |
|---|---|
| `Content-Type` | `InvalidOperationException` com o segredo na mensagem; nada enviado |
| `X Bad` (espaço) | idem |
| `Authorization` | aceito, enviado como está |
| `Host` | aceito, e **substitui o Host** da requisição |
| `Accept` | aceito, e **acrescentado** ao `Accept` do SDK (`application/json;text/event-stream;VALOR…`) |
| `Mcp-Session-Id` | aceito; depois do `initialize` vira `sessao-1;VALOR…` |
| `MCP-Protocol-Version` | aceito; no `initialize` **substitui** a versão, nos demais é acrescentado |
| `Mcp-Method` | aceito, e acrescentado ao método JSON-RPC |

Ou seja: o SDK não recusa nenhum header reservado. O que ele não consegue
adicionar vira exceção com o segredo, e o que colide corrompe o protocolo em
silêncio.

**V3. A exceção da V2 vaza.** Medido: a `InvalidOperationException` sai de
`McpClient.CreateAsync` sem embrulho. Em `apps/api`, `McpConnectionTester`
captura `Exception` e devolve `exception.Message` na resposta HTTP do teste
(`McpConnectionTester.cs:35-38` e `:60-63`). Em `apps/workers`,
`McpToolSetResolver` loga a exceção inteira em `Warning`
(`McpToolSetResolver.cs:85`). Com o nome validado em `apps/api`, o caminho
fica inalcançável a partir do cadastro. Continua alcançável por linha gravada
fora da validação (ver D3).

**V4. Valor com caractere de controle.** Medido com `SocketsHttpHandler` real
contra um socket TCP local, lendo os bytes recebidos:

- `SEGREDO\r\nX-Injetado: 1` foi escrito **cru**, e `X-Injetado: 1` chegou
  como uma linha de header própria. **Há injeção de header**: nem o
  `TryAddWithoutValidation` nem o handler barram CR/LF no valor;
- `\n` sozinho e `\0` também saem crus;
- `ç` falha com `HttpRequestException: Request headers must contain only
  ASCII characters`, sem o valor na mensagem.

Isso vale **também para o `BearerToken` de hoje**, cujo valor não é validado
(ver "Achados fora do escopo").

**V5. Log do `HttpClient` no .NET 10.** Fontes:

- documentação oficial,
  [HttpClientFactory logging redacts header values by default](https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/redact-headers):
  desde o .NET 9, sem `RedactLoggedHeaders`, todos os valores de header são
  redigidos; **com** `RedactLoggedHeaders`, só os nomes listados são redigidos;
- código decompilado de `Microsoft.Extensions.Http` 10.0.10 (a versão de
  `Directory.Packages.props:22`): `LogHelper.ShouldRedactHeaderValue = header
  => true` e `HttpClientFactoryOptions.ShouldRedactHeaderValue` tem esse
  default. Os headers saem só em `Trace` (`LogHelper.LogRequestStart` e
  `LogRequestPipelineStart`);
- configuração atual: nenhum `RedactLoggedHeaders` nem
  `ConfigureHttpClientDefaults` em `apps/api` ou `apps/workers`. Os clients
  nomeados são registrados em `apps/api/.../Program.cs:93` e
  `McpTransportFactory.AddHttpClient`. Nenhum appsettings sobe
  `System.Net.Http` para `Trace`;
- medido com o `IHttpClientFactory` real em `Trace`: `X-API-TOKEN: *`, e o
  valor não aparece em nenhuma linha de log.

**Resposta:** com a configuração atual, o valor de `X-API-TOKEN` não aparece
em claro em nenhum nível do log do `HttpClient`. O risco que sobra é alguém
chamar `RedactLoggedHeaders` com uma lista, o que **desliga** a redação dos
headers fora dela. O guarda está em D8.

**V6. Leitura de valor desconhecido do enum.** Medido com
`EnumToStringConverter` do EF Core 10.0.10, que é o que `HasConversion<string>()`
usa: ler `"CustomHeader"` num enum sem esse membro lança
`InvalidOperationException: Cannot convert string value 'CustomHeader' from
the database to any value in the mapped 'AuthType' enum`. Em
`McpToolSetResolver.ResolveAsync`, a consulta dos vínculos
(`McpToolSetResolver.cs:47-53`) está **fora** do `try` por servidor, então a
exceção derruba a resolução inteira do agente, não só o servidor novo.

**V7. `Enum.TryParse` é frouxo.** Medido: `"1"`, `"2"`, `"99"`,
`" BearerToken"` e `"None,BearerToken"` devolvem `true`. `"2"` e `"99"` dão
valores não definidos, e `"None,BearerToken"` dá `BearerToken` por OU de bits.
Hoje `ValidateShape` aceita todos.

**V8. Precedente de modelo espelho e migração em `apps/workers`.**
`2026-08-03-apps-workers-execucao-mcp` (design.md, "Migration Plan"): o
`apps/workers` ganha uma migração espelho
(`20260803021548_AddMcpServerCatalog`) que só serve ao ferramental do EF e
**nunca é aplicada em runtime**. Só o `migrator` aplica as migrações de
`apps/api` e `apps/inbox` (`deploy/migrate/entrypoint.sh`). Esta change segue
o mesmo padrão.

**V9. Os caminhos de `apps/api` que conectam a partir do registro salvo.**
Lidos no código. Três handlers decifram a credencial da linha persistida e
chamam `IMcpConnectionTester` sem passar pela validação de entrada:

- `TestSavedMcpServerConnectionCommandHandler.cs:44` (`POST
  /mcp-servers/{id}/test`), que devolve o `McpConnectionTestResult` inteiro,
  com `message`;
- `ListMcpServerToolsQueryHandler` (`GET /mcp-servers/{id}/tools`), que
  devolve `message` em `McpServerToolsResponse`
  (`McpServerToolsResponse.cs:15`);
- `ReplaceAgentMcpServersCommandHandler.cs:121`, que põe `discovery.Message!`
  na falha de handshake (`:125`). `AgentMcpBindingEndpoints.cs:69` a devolve
  como `detail` de um 502.

Em todos, a mensagem é a de `McpConnectionTester`, que repassa
`exception.Message` (V3). Uma linha `CustomHeader` com nome que o .NET recusa
leva a `InvalidOperationException` da V2, **com o segredo**, até a resposta
HTTP dos três. Isso decide a extensão de D3.

**V10. Como o motivo de falha é consumido.** `McpConnectionTestFailureReason`
(`apps/api/.../Connectivity/McpConnectionTestFailureReason.cs`) vai para o fio
como string (`JsonStringEnumConverter`). No frontend, a união tem os mesmos
quatro valores (`apps/frontend/.../types/mcpServer.ts:30-31`), e a explicação
sai de um `Record` exaustivo (`ConnectionTestResultAlert.tsx:23-31`,
consultado em `:41`). Um valor novo vindo da API daria explicação `undefined`
até o frontend acompanhar.

Nenhuma verificação contrariou a premissa da issue. V2, V3, V4, V6 e V9 a
refinam e decidem D3, D5 e D8.

## Goals / Non-Goals

**Goals:**

- Cadastrar, testar e executar um `McpServer` que autentica por token fixo
  num header de nome configurável.
- Recusar no cadastro todo nome de header que faria o SDK lançar com o
  segredo na mensagem, ou que corromperia o protocolo.
- Tirar dos dois caminhos de log e de resposta a possibilidade de o segredo
  aparecer, inclusive quando a linha no banco não passou pela validação.
- Fazer o `apps/workers` sobreviver a um valor de `AuthType` que ele não
  conhece, degradando só aquele servidor.
- Provar o acordo api ↔ workers com a linha gravada de verdade.

**Non-Goals:**

- Frontend (D7, #168).
- Lista genérica de N headers (recusada na própria issue).
- Validar o valor da credencial `BearerToken` (achado fora do escopo, #167).
- OAuth2 client credentials (#161).
- Extrair a montagem de transporte para `libs/` (D6).

## Decisions

### D1. Nome do tipo: `CustomHeader`, fixado como string no fio e no banco

O membro novo se chama `CustomHeader` nos dois enums. O nome do membro **é**
o contrato em dois lugares: no JSON, via o
`JsonStringEnumConverter<McpServerAuthType>` já declarado no enum, e na coluna
`AuthType`, via `HasConversion<string>()`. Renomear o membro mudaria os dois
em silêncio e quebraria a leitura das linhas existentes (V6). A fixação vem
de dois testes que leem o **texto bruto**, e não um round-trip pelo mesmo
tipo, que é cego a isso (convenção 12): o JSON da resposta contém
`"authType":"CustomHeader"`, e a coluna gravada contém `CustomHeader`.

**Alternativas recusadas:**

- `ApiKey` / `ApiKeyHeader`: "API key" também designa chave em query string
  ou em `Authorization: ApiKey …`. O nome sugeriria um mecanismo que o tipo
  não implementa.
- `HeaderToken`: correto, mas não distingue do `BearerToken`, que também é
  token em header.
- `[JsonStringEnumMemberName]` com nome diferente do membro: criaria dois
  nomes para a mesma coisa, porque a coluna do banco continuaria usando o nome
  do membro.

### D2. Nome do header numa coluna em claro, `AuthHeaderName`

Coluna `AuthHeaderName text NULL` em `mcp_servers`, no padrão PascalCase das
colunas dessa tabela (`20260802163110_AddMcpServerCatalog.cs`). Ela é
devolvida em `McpServerResponse.AuthHeaderName` (`authHeaderName` no fio) e
editável. O segredo continua em `EncryptedCredential`. A convenção 3 trata de
**segredo**. O nome do header é configuração, e devolvê-lo é o que deixa o
formulário pré-preenchê-lo na edição sem estender o "deixe em branco para
manter" a um campo que não é segredo.

Régua da convenção 2: jsonb quando não há necessidade relacional **e** há
estrutura para guardar. Aqui há **um** campo escalar, e uma coluna anulável é
a forma mais barata de ler, validar e migrar.

**Alternativas recusadas:**

- Blob cifrado por tipo, no molde de `Channel.EncryptedCredentials`
  (`apps/inbox`): cifra configuração que precisa ser devolvida na resposta. O
  formulário não conseguiria mostrar o nome atual sem uma rota de decifragem,
  ou sem tratar o nome como segredo write-only, o que obrigaria o operador a
  redigitar o nome a cada edição.
- jsonb `AuthSettings` em claro já agora: estrutura para um consumidor só.
  **Registro para a #161:** OAuth client credentials traz 4 ou 5 parâmetros
  não secretos (endpoint de token, `client_id`, `scope`, `audience`/`resource`
  e método de autenticação do cliente). Se a #161 decidir por jsonb, migrar
  esta coluna para dentro dele é um `UPDATE` e um `DROP`, sem perda. A coluna
  não fecha a porta, e a decisão fica com quem tem o segundo consumidor real.

### D3. Validação do nome do header, e a revalidação em `apps/workers`

**Em `apps/api`**, quando `authType == CustomHeader`, nos três pontos de
entrada (`POST /mcp-servers`, `PUT /mcp-servers/{id}`, `POST
/mcp-servers/test`) e antes de qualquer conexão, o nome é recusado com 400 em
`authHeaderName` se cair em qualquer uma de três regras, nesta ordem:

1. **Sintaxe**: não é token HTTP (RFC 9110 §5.6.2, só `tchar`, ao menos um).
   Vazio ou só espaço entra aqui.
2. **Critério do próprio .NET**: `new HttpRequestMessage().Headers
   .TryAddWithoutValidation(nome, "x")` devolve `false`. É **exatamente** a
   chamada que o SDK faz (V2), com um valor fictício, então nenhum nome que o
   SDK transformaria em exceção com o segredo passa daqui. Isso cobre
   `Content-Type` (medido) e os demais headers de conteúdo do .NET, sem uma
   lista nossa que envelheceria.
3. **Reservados**, sem diferenciar caixa. Os que o SDK aceita mas que colidem
   ou mudam o protocolo:
   - **prefixo `Mcp-`**, que cobre `Mcp-Session-Id`, `MCP-Protocol-Version`,
     `Mcp-Method`, `Mcp-Name` e `Mcp-Param-*` (V2, medido para os três
     primeiros). É prefixo, e não lista, porque o SDK 2.0.0 já usa uma família
     aberta (`McpHttpHeaders.ParamPrefix`), e uma versão nova pode acrescentar
     membros sem que esta lista acompanhe;
   - `Accept` (medido: acrescentado) e `Last-Event-ID` (escrito pelo SDK no
     GET de retomada, lido no decompilado);
   - `Host` (medido: substitui o destino lógico da requisição);
   - os de conexão: `Connection`, `Keep-Alive`, `Proxy-Connection`,
     `Transfer-Encoding`, `TE`, `Trailer`, `Upgrade`, `Expect`. Estes estão
     **por RFC 9110 §7.6.1, não medidos**: são do salto, não da origem, e um
     segredo neles não chega ao servidor como credencial.

**`Authorization` é aceito.** O tipo envia o valor cru, sem prefixo, e há
esquemas reais em `Authorization` que não são `Bearer` (`Token …`,
`ApiKey …`). Recusá-lo exigiria um terceiro tipo para esses casos.
`Authorization` com `Bearer x` fica equivalente ao `BearerToken`, o que é
redundância inofensiva, não conflito.

`authHeaderName` enviado com tipo diferente de `CustomHeader` é **ignorado e
gravado nulo**, não recusado. O valor não é secreto nem usado, e recusar
obrigaria o formulário a limpar o campo a cada troca de tipo, sem ganho de
segurança.

**Valor do token** (`CustomHeader` apenas): recusado com 400 em `credential`
se tiver caractere fora de `0x20`–`0x7E`, ou espaço no início ou no fim. Isso
fecha a injeção da V4 e a falha por não ASCII **para o tipo novo**. A
mensagem nunca carrega o valor.

**Credencial ausente no teste não salvo** (`CustomHeader` apenas): `POST
/mcp-servers/test` com credencial ausente ou vazia responde 400 em
`credential`, sem conectar. Hoje o teste não salvo não exige credencial em
tipo nenhum (`ValidateTestShape`, `McpServerEndpoints.cs:174-190`). Para
`CustomHeader` isso enviaria o header com valor vazio, e o resultado
"credencial rejeitada" seria verdadeiro sobre um teste que não testou nada. O
`BearerToken` fica como está, porque mudá-lo é contrato de tipo existente.

**Em `apps/api`, revalidação da regra 2 dentro do `McpConnectionTester`.** A
validação de entrada não alcança os três caminhos que conectam a partir do
registro salvo (V9), e os três devolvem a mensagem do tester na resposta
HTTP. Por isso `TestAsync` e `ListToolsAsync` checam a regra 2
(`TryAddWithoutValidation(nome, "x")`, e nome nulo ou vazio) **antes de
`BuildTransport`**, quando o tipo é `CustomHeader`. Se a regra recusar o nome,
devolvem falha **sem nenhuma requisição à URL**, com uma mensagem que cita o
nome do header (configuração) e nunca o valor. É um ponto só para os três
caminhos, e é o mesmo critério do `apps/workers`.

Na rota não salva, a checagem é redundante com a validação de entrada, que já
responde 400 antes. Fica assim de propósito: a checagem mora no componente
que monta o transporte, e não depende de quem o chama ter validado.

**Motivo de falha: `Unknown`, não um motivo novo.**
- **Alcance:** o estado só se alcança por linha gravada fora da validação
  (SQL direto, ou validação regredida). Não é um caminho de operação normal,
  e por isso não ganha vocabulário próprio (convenção 2).
- **Contrato:** um motivo novo é mudança de contrato com o frontend
  (convenção 12, V10). Até a #168 acompanhar, a explicação do `Record`
  exaustivo sairia `undefined`, e a tela mostraria um alerta de falha sem
  explicação.
- **Diagnóstico:** com `Unknown`, a tela mostra "Falha desconhecida" e, como
  detalhe, a mensagem, que diz qual nome foi recusado e manda editar o
  servidor. O operador tem como agir.

Motivos existentes recusados: `CredentialRejected` afirmaria que o servidor
recusou algo que nunca recebeu, e `HostUnreachable` afirmaria um problema de
rede que não houve (convenção 13).

**Em `apps/workers`: revalida só a regra 2, e só para excluir sem vazar.**
Antes de `BuildTransport`, se o tipo é `CustomHeader` e o nome persistido é
nulo, vazio ou recusado por `TryAddWithoutValidation(nome, "x")`, o servidor é
excluído com um `Warning` que cita id e nome do servidor e **não** passa a
exceção ao logger. O motivo é a V3: sem isso, uma linha com nome recusado
(gravada antes da validação, por SQL direto, ou por um `apps/api` com
validação regredida) faz o SDK lançar com o segredo na mensagem, e o
`LogWarning(exception, …)` de `McpToolSetResolver.cs:85` grava o segredo.

**Alternativas recusadas:**

- Workers não revalida: deixa o vazamento no log alcançável a partir do
  banco, que é fonte que `apps/workers` não controla.
- Workers replica a validação inteira (regras 1 a 3): duas listas de
  reservados para manter em acordo entre apps, sem ganho. Um nome reservado
  que escapasse corromperia uma requisição, mas não vazaria nada, e a regra 2
  sozinha é a que fecha o vazamento.
- Workers deixa de passar a exceção ao logger em todo `catch`: perderia o
  diagnóstico de host inalcançável e timeout, que hoje depende do stack.
- No `apps/api`, revalidar em cada um dos três handlers: são três cópias da
  mesma checagem, e um quarto caminho futuro esqueceria a sua. O tester é o
  único ponto por onde todos passam.
- No `apps/api`, deixar de repassar `exception.Message` no `McpConnectionTester`:
  fecha este vazamento, mas tira do operador o detalhe que hoje distingue
  falha de DNS de recusa de conexão (Decision 3 de
  `2026-09-05-frontend-mcp-servidor-uso-e-diagnostico`). Esta change não
  troca esse diagnóstico por uma garantia que a checagem do tester já dá.

### D4. Transições no update: a credencial está vinculada ao tipo

| de → para | credencial em branco | credencial informada | `AuthHeaderName` |
|---|---|---|---|
| qualquer → `None` | limpa (como hoje) | ignorada e limpa | nulo |
| `None` → `BearerToken`/`CustomHeader` | 400 `credential` (como hoje) | cifra a nova | `CustomHeader`: obrigatório e gravado; `BearerToken`: nulo |
| `BearerToken` → `BearerToken` | mantém (como hoje) | substitui | nulo |
| `CustomHeader` → `CustomHeader` | **mantém** | substitui | obrigatório, pode mudar |
| `BearerToken` → `CustomHeader` | **400 `credential`** | cifra a nova | obrigatório e gravado |
| `CustomHeader` → `BearerToken` | **400 `credential`** | cifra a nova | nulo |

**Trocar de tipo entre dois tipos com credencial não reaproveita o segredo.**
Mecanismo: `McpServer.UpdateDetails` passa a limpar `EncryptedCredential`
sempre que o `AuthType` **muda**, não só quando vai para `None`. A checagem que
já existe no handler (`AuthType != None && EncryptedCredential is null` →
`CredentialMissing`, `UpdateMcpServerCommandHandler.cs:32`) produz o 400 sem
código novo de decisão, e o `SaveChanges` não roda, então o registro
persistido fica intacto.

Motivo: o segredo foi emitido e digitado para um esquema. Mandar um token
`Bearer` como valor cru de `X-API-TOKEN`, ou o inverso, é uma suposição
silenciosa que, quando errada, aparece como "credencial rejeitada" sem pista
da causa. Redigitar custa pouco.

`CustomHeader` → `CustomHeader` com nome de header novo **mantém** o segredo:
o tipo não mudou, e o caso real é o fornecedor renomear o header.

Isso **muda** o requisito vivo "Atualização de servidor MCP", que hoje mantém
a credencial sempre que "o tipo continuar exigindo credencial". Daí o BREAKING
de comportamento na proposta. Nenhum consumidor atual é afetado: o frontend
só oferece `None` e `BearerToken`, e `None` → `BearerToken` já exige
credencial.

**Alternativa recusada:** manter o segredo também na troca de tipo
(comportamento literal do requisito atual). É conveniente no caso de "mesmo
token, outro header", mas esconde o erro no caso comum de token diferente, e o
diagnóstico do operador seria "credencial rejeitada" sem saber que o valor era
o do tipo anterior.

### D5. Ordem de deploy: tolerância no `apps/workers` mais acordo pelo artefato real

**O problema medido (V6):** um `apps/workers` sem o membro `CustomHeader`, ao
ler uma linha com esse valor, derruba a resolução inteira de qualquer agente
vinculado ao servidor, porque a leitura está fora do `try` por servidor.

**Proteção, em duas camadas:**

1. **Tolerância de leitura em `apps/workers`, sem membro sentinela.** O
   `AuthType` do modelo espelho passa a ser `McpServerAuthType?`, mapeado por
   um conversor que devolve `null` para string que não é nome de membro
   definido, em vez de lançar. `McpToolSetResolver` exclui com `Warning` (id e
   nome do servidor, sem credencial) todo servidor com `AuthType` nulo, **sem
   decifrar a credencial e sem conectar**, seguindo a forma da degradação por
   servidor que o requisito já tem. A coluna continua `NOT NULL`: `null` no
   modelo significa "valor que esta versão não conhece", nunca "ausente".
   Também não vira `Unknown` no enum, porque um membro sentinela seria
   gravável por `apps/api` se alguém espelhasse o enum ao contrário, e
   apareceria nos `switch`.
2. **Teste de acordo com o artefato real** (convenção 11), no projeto que já
   existe para isso, `tests/CrossAppTaskStoreCompatibility.Tests` (o único que
   referencia os dois apps, Decisão 8 de quem o criou):
   - para **cada** valor de `Buteco.Api...McpServerAuthType`, `apps/api` grava
     um `McpServer` pelo próprio `AppDbContext`, e `apps/workers` lê pelo
     próprio contexto. O teste afirma que o `AuthType` lido tem **o mesmo
     nome** do gravado, e não só que não lançou. Com a tolerância da camada
     1, "não lançou" passaria com o membro faltando, e esse é o caso que o
     guarda existe para pegar;
   - uma linha `CustomHeader` gravada pelo `apps/api` com a cifra de
     `apps/api`, resolvida pelo `McpToolSetResolver` real de `apps/workers`
     com a cifra de `apps/workers` e a mesma chave, chega ao servidor fake com
     `X-API-TOKEN` e a credencial original.

**Por que a camada 1 não é abstração prematura (convenção 2):** há cenário
real e já enfileirado. A #161 acrescenta outro valor ao mesmo enum, e o
rollback só de `apps/workers` é a manobra comum quando a execução regride.
**O limite da camada 1 precisa ser dito:** ela protege **dos próximos** valores
em diante. O `apps/workers` que está em produção hoje não a tem. Para **este**
deploy, a proteção é de ordem (Migration Plan): `apps/workers` sobe no mesmo
`docker compose up` que `apps/api` (`docs/deployment.md`, passo 4, já faz
assim). O valor novo só passa a existir quando um operador grava um servidor
`CustomHeader`, o que exige o `apps/api` novo no ar e, na prática, o frontend
da #168 (D7).

**Alternativas recusadas:**

- Só ordem de deploy, sem tolerância: deixa o rollback de `apps/workers`
  derrubando execuções sem aviso, e a #161 herdaria o mesmo risco.
- Teste de acordo por reflexão, comparando os nomes dos dois enums: compara
  declarações, não o que o banco devolve (convenção 11). Ele passaria com um
  conversor que lê errado.
- Mover a leitura para dentro do `try` por servidor: a consulta é uma só para
  todos os vínculos, então uma linha ruim continuaria derrubando as demais. A
  tolerância por linha é o que dá a degradação por servidor.

### D6. Duplicação dos dois `BuildTransport`: registrada, não extraída

Cada `BuildTransport` passa de um `if` para um `switch` de três ramos sobre o
tipo. A montagem do header é a mesma lógica nos dois apps, e o crescimento
fica registrado num comentário em cada um, que aponta o outro: a montagem
duplicada passa de um tipo para dois, e a #161 levaria a três. A validação do
nome não é duplicada (D3): em `apps/workers` só existe a regra 2, por um
motivo diferente.

**Não vai para `libs/`.** A regra de `libs/` do `config.yaml` exige
justificativa, e a convenção 2 pede 2 ou 3 consumidores com código **idêntico
e estável**. Aqui são dois consumidores, mas com **diferenças deliberadas**
(`OwnsSession = false` só em `apps/api`; o `HttpClient` nomeado e o
`PooledConnectionLifetime` só em `apps/workers`), e a #161 vai mudar a forma
da montagem: o token é obtido e renovado, não lido do banco. Extrair agora
congelaria uma forma que a próxima change da mesma linha vai reabrir.
**Gatilho para reavaliar:** a #161 entregar a terceira variante, quando a
forma final da montagem for conhecida.

### D7. Sequenciamento: frontend na issue própria #168

A convenção 1 é catálogo → execução → UI. Esta change cobre catálogo
(`apps/api`) e execução (`apps/workers`). O frontend (seletor do tipo, campo
de nome do header, rótulo na tabela e no card) sai como **issue própria, a
#168**, e não como segunda change da #160. O motivo da forma é a convenção 24:
o PR desta change leva `Closes #160`, e uma segunda change da mesma issue
ficaria sem issue aberta para referenciar. O motivo de separar a UI tem três
razões:

1. **A ordem de deploy fica segura por construção (D5):** com o backend
   arquivado e implantado primeiro, o `apps/workers` já conhece
   `CustomHeader` quando o painel passa a oferecê-lo ao operador.
2. **Mudança de tela tem validação visual quadro a quadro** (convenção 14),
   e o archive dela espera essa conferência. Juntar as duas prenderia o
   backend, que é verificável pela suíte, à agenda da conferência visual.
3. **Há uma decisão de UI que esta change não deve tomar:** em edição, o
   "Testar conexão" com credencial em branco usa o **registro salvo**
   (`McpServerForm.tsx:101`, Decision 6 de
   `2026-09-05-frontend-mcp-servidor-uso-e-diagnostico`), então um nome de header
   editado e não salvo não seria testado. É a mesma limitação que a URL
   editada já tem, e a forma de dizê-la na tela é decisão da change de UI.

**Janela conhecida entre esta change e a #168:** um servidor `CustomHeader`
criado direto pela API aparece na tabela e no card com rótulo vazio
(`authTypeLabels[mcpServer.authType]`, `McpServerTable.tsx:87` e
`McpServerConfigCard.tsx:37`), e o formulário de edição não o salva sem o
campo novo. Não há perda de dado nem execução afetada. A janela é
**requisito de entrada da #168**, que a fecha.

**Referência de design da #168:** o protótipo aprovado pelo dono em
18/10/2026, canvas https://claude.ai/artifact/2apwGhmPQCwYWKc8i9DFEy, versão
`1792295069-a164` (quadros `Main.dc.html`, `Erros.dc.html` e
`Detalhe.dc.html`). As regras de uso do protótipo estão num comentário da
própria #168: cópia para `design/` da change, composição sem identidade
visual, componente real do Mantine, validação não duplicada, simulação não
vira produto, divergência registrada, conferência quadro a quadro e escopo
sem a #161. Esta change não usa o protótipo.

**Alternativas recusadas:**

- Uma change só, com as três camadas: é mais rápida de ponta a ponta, mas
  inverte a ordem segura de deploy e acopla o archive do backend à
  conferência visual.
- Segunda change da mesma #160: depois do merge desta, a #160 está fechada
  pelo `Closes`, e a change de UI nasceria sem issue aberta, contra a
  convenção 23.

### D8. Guarda da redação de header no log do `HttpClient`

V5 mostrou que o valor está protegido **pelo default**, e que esse default
**se desliga** com qualquer `RedactLoggedHeaders` que liste nomes. O guarda
afirma o default no lugar onde ele pode ser perdido, a composição real de
cada app: resolve `IOptionsMonitor<HttpClientFactoryOptions>.Get(nome)` do
container de produção, para `McpConnectionTester.HttpClientName` em
`apps/api` (pelo `WebApplicationFactory`) e para
`McpTransportFactory.HttpClientName` em `apps/workers`, e afirma que
`ShouldRedactHeaderValue("X-API-TOKEN")` é `true`.

O guarda é determinístico e não depende de um nível de log. Um teste que
ligasse `Trace` e procurasse o segredo nas linhas também passaria, mas só
enquanto o nível de log do teste for `Trace`, e esconderia o mecanismo.

### D9. `authType` validado por nome de membro (V7), dentro do escopo

`ValidateShape` e `ValidateTestShape` passam a aceitar só uma string que seja
nome de membro definido, comparada sem caixa contra
`Enum.GetNames<McpServerAuthType>()`. Ficam de fora `"2"`, `"99"` e
`"None,BearerToken"`. A mensagem de valores aceitos sai da mesma lista, então
um membro novo aparece nela sem que alguém lembre de editá-la. É a forma que
a mensagem fixa "None, BearerToken" já tinha de envelhecer, e que esta change
teria repetido.

Está no escopo, e não virou issue à parte, porque esta change **reescreve
exatamente essas duas funções** e muda a mensagem delas. Deixar o
`Enum.TryParse` frouxo faria `"2"` virar `CustomHeader` silenciosamente a
partir desta change.

## Estrutura de pastas proposta

```
apps/api/src/Buteco.Api/
  McpServers/
    Entities/
      McpServerAuthType.cs            # alterado: membro CustomHeader
      McpServer.cs                    # alterado: AuthHeaderName; UpdateDetails limpa a
                                      #   credencial quando o AuthType muda (D4)
    Validation/
      McpAuthHeaderRules.cs           # novo: regras 1-3 do nome e regra do valor (D3), e a
                                      #   validação de authType por nome de membro (D9)
    Endpoints/McpServerEndpoints.cs   # alterado: usa McpAuthHeaderRules nos 3 pontos de entrada
    Requests/
      CreateMcpServerRequest.cs       # alterado: AuthHeaderName
      UpdateMcpServerRequest.cs       # alterado: AuthHeaderName
      TestMcpServerConfigRequest.cs   # alterado: AuthHeaderName
    Responses/McpServerResponse.cs    # alterado: AuthHeaderName
    Commands/
      CreateMcpServer/                # alterados: command e handler levam AuthHeaderName
      UpdateMcpServer/                # alterados: command e handler levam AuthHeaderName
      TestUnsavedMcpServerConnection/ # alterados: command e handler levam AuthHeaderName
      TestSavedMcpServerConnection/   # alterado: handler passa o AuthHeaderName persistido
    Queries/ListMcpServerTools/       # alterado: handler passa o AuthHeaderName persistido
    Connectivity/
      IMcpConnectionTester.cs         # alterado: TestAsync/ListToolsAsync recebem authHeaderName
      McpConnectionTester.cs          # alterado: revalida a regra 2 antes de BuildTransport (D3,
                                      #   falha Unknown sem o valor); switch de 3 ramos (D6)
  AgentMcpBindings/Commands/ReplaceAgentMcpServers/
    ReplaceAgentMcpServersCommandHandler.cs  # alterado: repassa AuthHeaderName ao ListToolsAsync
  Infrastructure/
    AppDbContext.cs                   # alterado: mapeia AuthHeaderName
    Migrations/
      <ts>_AddMcpServerAuthHeaderName.cs (+ .Designer.cs, snapshot)  # novo: coluna text NULL

apps/api/tests/Buteco.Api.Tests/
  Support/FakeMcpServerHttpMessageHandler.cs  # alterado: RequiredHeader (nome, valor) e registro
                                              #   dos headers recebidos
  McpServerEndpointsTests.cs                  # alterado: criação, consulta e formato de fio
  McpAuthHeaderValidationTests.cs             # novo: regras do nome e do valor pelos endpoints
  McpServerCustomHeaderConnectionTests.cs     # novo: teste não salvo e salvo, descoberta, transições
                                              #   do update e a linha salva com nome recusado nos três
                                              #   caminhos, inclusive o PUT do vínculo (D3, D4)
  McpConnectionTesterListToolsTests.cs        # alterado: só a assinatura nova do tester
  McpServerAuthHeaderNameMigrationTests.cs    # novo: linha anterior à migração lê AuthHeaderName nulo
  HttpClientHeaderRedactionTests.cs           # novo: guarda D8 na composição real

apps/workers/src/Buteco.Workers/
  Mcp/
    Entities/
      McpServerAuthType.cs            # alterado: membro CustomHeader
      McpServer.cs                    # alterado: AuthType anulável (D5) e AuthHeaderName
    McpTransportFactory.cs            # alterado: switch de 3 ramos (D6)
    McpToolSetResolver.cs             # alterado: exclusão por AuthType desconhecido e por nome
                                      #   recusado, antes de decifrar e conectar (D3, D5)
  Infrastructure/
    AppDbContext.cs                   # alterado: conversor tolerante e AuthHeaderName
    Migrations/
      <ts>_AddMcpServerAuthHeaderName.cs (+ .Designer.cs, snapshot)  # novo: espelho, nunca
                                                                     #   aplicado em runtime (V8)

apps/workers/tests/Buteco.Workers.Tests/
  Mcp/Support/FakeMcpServerHttpMessageHandler.cs  # alterado: RequiredHeader e registro de headers
  Mcp/McpToolSetResolverTests.cs                  # alterado: CustomHeader, AuthType desconhecido,
                                                  #   nome recusado sem segredo no log
  Mcp/McpToolExecutionEndToEndTests.cs            # alterado: ponta a ponta exigindo X-API-TOKEN
  Mcp/HttpClientHeaderRedactionTests.cs           # novo: guarda D8 sobre McpTransportFactory.AddHttpClient

tests/CrossAppTaskStoreCompatibility.Tests/
  McpServerCrossAppCompatibilityTests.cs          # novo: acordo api ↔ workers (D5)
  Support/CrossAppFakeMcpServerHandler.cs         # novo: servidor fake mínimo deste projeto
```

Nada em `libs/` (D6). Nenhum `ProjectReference` novo entre apps.

### Divergências da implementação (convenção 9)

Nenhuma muda uma decisão D1 a D9, um requisito das specs ou a tabela de
guardas. Todas são de forma, e ficam aqui com a causa:

- **Arquivos de teste.** As transições do update, o teste salvo, a descoberta
  e o PUT de vínculo com linha salva ficaram numa classe nova,
  `McpServerCustomHeaderConnectionTests`, sobre a `McpConnectionTestFixture`.
  Eles precisam do servidor MCP simulado (o "mantém o segredo" se afirma por
  um re-teste salvo), que `McpServerEndpointsTests` não tem, e reunir os
  cenários do tipo novo evitou espalhar asserções novas por quatro classes
  existentes. O teste de migração usa a `MigrationPostgresCollection`
  compartilhada (D12 de `historico-documentos-base`), precedente mais recente
  que o citado na tarefa.
- **Requests.** `AuthHeaderName` entrou como último parâmetro, opcional, dos
  três requests. Os testes existentes os constroem de forma posicional e
  compilaram sem edição, o que era pré-requisito da regressão do Bearer.
- **Assinatura do tester.** `TestAsync`/`ListToolsAsync` ganharam
  `authHeaderName` antes do `CancellationToken`. As três chamadas diretas de
  `McpConnectionTesterListToolsTests` passaram `authHeaderName: null`, o único
  trecho alterado em teste existente, sem tocar asserção.
- **`apps/workers`.** O conversor chama
  `AppDbContext.ParseKnownAuthType`, pública porque roda dentro da expression
  tree. A checagem da regra 2 foi replicada em `McpToolSetResolver`
  (`IsAcceptedAsRequestHeader`), sem compartilhar com `apps/api`. O guarda D8
  do `apps/workers` roda sobre `McpTransportFactory.AddHttpClient`, o registro
  que o `Program.cs` chama, porque não há host de teste montado a partir do
  `Program.cs` (mesmo caminho do guarda do timeout de conexão). O que ele não
  cobre é um `ConfigureHttpClientDefaults` acrescentado direto no
  `Program.cs`.
- **Comentário da migração espelho.** O molde citado
  (`20260803021548_AddMcpServerCatalog.cs`) não tem comentário: o registro de
  "nunca aplicada" mora no `<summary>` de `AppDbContext`. A migração nova
  ganhou o comentário mesmo assim, com uma precisão que faltava nos dois: as
  fixtures de teste de `apps/workers` aplicam essas migrações.
- **Ordem das tarefas.** O guarda 4.6 e os testes 5.3 e 5.4 dependem de o
  tester enviar o header (6.2) e de o fake exigir um header (6.3). A primeira
  demonstração do 4.6, com a 6.2 por fazer, reprovou só pelo status, porque o
  nome inválido nunca chegava ao SDK. A demonstração válida rodou depois da
  6.2 e antes da 6.2a.

### O que a implementação confirmou com medição

- **V3 e V9 no app real.** Com a validação de entrada tirada, `POST
  /mcp-servers/test` devolveu num 200 `...'Content-Type' with value
  'SEGREDO-teste-nao-salvo'...`. Com a checagem do tester desligada, a
  descoberta de tools, o re-teste salvo e o 502 do PUT de vínculo devolveram o
  segredo, os três. Em `apps/workers`, sem a reconferência, o
  `LogWarning(exception, ...)` gravou `...with value 'SEGREDO-no-log'...`.
- **O conversor tolerante (D5)**, medido antes do grupo 7 com EF Core 10.0.10
  e Npgsql 10.0.3 contra Postgres real: `AuthType?` com `IsRequired()` gera
  `"AuthType" text NOT NULL`, e o modelo dá `IsNullable = false`. Na mesma
  consulta, `FuturoTipo` e `2` materializam `null` e as demais linhas vêm com o
  valor, inclusive em projeção anônima. Gravar `null` é recusado pelo banco
  (`23502`).
- **As duas migrações** saíram só com o `AddColumn`/`DropColumn` de
  `AuthHeaderName`. A do `apps/workers` não tem `AlterColumn` em `AuthType`,
  apesar da troca do modelo para `McpServerAuthType?`.

## Risks / Trade-offs

Cada risco tem uma contraparte verificável (convenção 10).

- **[Segredo na resposta do teste de conexão ou no log, pela exceção do SDK
  (V2, V3)]** → `apps/api` recusa o nome antes de conectar (D3).
  `apps/workers` exclui, sem passar a exceção ao logger, a linha cujo nome o
  .NET recusa. Contraparte: o cenário "Nome inválido no teste não salvo
  responde 400 sem o segredo e sem conectar" (`mcp-server-catalog`) e o
  cenário "Nome de header recusado no banco degrada o servidor sem gravar o
  segredo no log" (`mcp-tool-execution`). O teste de `apps/workers` captura
  as linhas reais do logger.
- **[Segredo na resposta HTTP de `apps/api` a partir de linha salva (V9)]** →
  o re-teste salvo, a descoberta de tools e o PUT de vínculo conectam com a
  linha persistida sem a validação de entrada, e os três devolvem a mensagem
  do tester. Mitigação: revalidação da regra 2 dentro do
  `McpConnectionTester`, antes de `BuildTransport`, com falha `Unknown` sem o
  valor (D3). Contraparte: o cenário "Nome de header recusado em servidor
  salvo não vaza o segredo" (`mcp-server-catalog`), com linha gravada por SQL.
  O PUT de vínculo é coberto por teste, sem cenário novo em `agent-mcp-binding`,
  porque o requisito de lá (handshake falho → 502) não muda e o que se afirma
  é a ausência do segredo no `detail`, que é propriedade do tester.
- **[Valor do header em claro no log do `HttpClient` (V5)]** → hoje está
  redigido pelo default. Contraparte: guarda D8 nos dois apps. Não há cenário
  de spec porque é propriedade da composição, não comportamento observável
  pela API, e a justificativa está registrada aqui.
- **[Quebra de leitura em `apps/workers` com valor de enum desconhecido
  (V6)]** → tolerância por linha (D5, camada 1) e acordo com o artefato real
  (D5, camada 2). Contraparte: os cenários "AuthType desconhecido degrada
  apenas aquele servidor" e "Todo tipo gravado pelo apps/api é conhecido pelo
  apps/workers". **Limite declarado:** a camada 1 não protege o `apps/workers`
  já implantado. Para este deploy vale a ordem do Migration Plan, que é
  procedimento, não teste.
- **[Regressão do `BearerToken`]** → a montagem passa de `if` para `switch`
  nos dois apps. Contraparte: os cenários "BearerToken continua enviando
  Authorization Bearer" nos dois specs, e os testes de Bearer existentes
  continuam verdes sem edição de asserção. Se algum precisar mudar de
  asserção, isso é sinal de regressão a investigar, não de ajuste.
- **[Header reservado corrompendo o protocolo (V2)]** → regra 3 de D3.
  Contraparte: cenários de `mcp-session-id` (caixa baixa) e `Host`. A lista
  de conexão (`Connection`, `TE` etc.) está por RFC, sem medição: o teste
  cobre a lista pelo comportamento da validação, não o efeito de cada nome no
  fio.
- **[Injeção de header pelo valor (V4)]** → regra do valor em D3, só para
  `CustomHeader`. Contraparte: cenário "Credencial com quebra de linha é
  rejeitada". **O `BearerToken` continua exposto**: é a #167 (abaixo).
- **[Troca de tipo passa a exigir credencial (D4)]** → é mudança de contrato.
  Contraparte: os quatro cenários de transição em "Atualização de servidor
  MCP". O cenário de 400 afirma também que o registro persistido não mudou.
- **[Janela de UI entre esta change e a #168 (D7)]** → rótulo vazio para
  servidor `CustomHeader` criado pela API. Não é testável nesta change, porque
  o código afetado é o da #168. Fica registrado em D7 e é requisito de
  entrada da #168.

## Demonstração de que cada guarda reprova (convenção 15)

Cada guarda novo é rodado contra o defeito reintroduzido de propósito antes
de a correção valer. O resultado (reprovou ou não, e com que mensagem) vai
para o `tasks.md`, inline na tarefa:

| guarda | defeito reintroduzido | componente onde precisa reprovar |
|---|---|---|
| teste não salvo com nome inválido | remover a chamada de `McpAuthHeaderRules` em `ValidateTestShape` | endpoint de `apps/api`: deve responder 200 com o segredo na mensagem |
| linha salva com nome recusado não vaza pelo `apps/api` | remover a checagem da regra 2 do `McpConnectionTester` | `McpConnectionTester`: `GET /mcp-servers/{id}/tools` e `POST /mcp-servers/{id}/test` devem responder com o segredo na mensagem |
| `apps/workers` sem segredo no log | remover a revalidação da regra 2 no resolver | `McpToolSetResolver`: o log capturado deve conter o segredo |
| `AuthType` desconhecido | voltar o mapeamento para `HasConversion<string>()` | `McpToolSetResolver.ResolveAsync`: deve lançar |
| acordo por nome lido | apagar `CustomHeader` do enum de `apps/workers` | teste de compatibilidade: deve reprovar pelo nome, não por exceção |
| redação no log (D8) | `.RedactLoggedHeaders(["Authorization"])` no client nomeado | teste de composição: `ShouldRedactHeaderValue("X-API-TOKEN")` vira `false` |
| `authType` por nome (D9) | voltar a `Enum.TryParse` | endpoint: `"2"` passa a criar o registro |
| troca de tipo exige credencial (D4) | `UpdateDetails` volta a limpar só em `None` | endpoint: o PUT responde 200 e reaproveita o segredo |
| `CustomHeader` sem `Authorization` | montar os dois headers no `switch` | fake: registra `Authorization` recebido |

## Migration Plan

1. **Migração de `apps/api`** `<ts>_AddMcpServerAuthHeaderName`: `ALTER TABLE
   mcp_servers ADD "AuthHeaderName" text NULL`. É aditiva, sem default e sem
   backfill: as linhas existentes são `None` ou `BearerToken`, e para elas o
   nome é nulo por definição. O `migrator` aplica a migração antes de
   `apps/api` subir.
2. **Migração espelho de `apps/workers`**: mesmo conteúdo, só para o
   ferramental do EF, nunca aplicada em runtime (V8, precedente
   `2026-08-03-apps-workers-execucao-mcp`).
3. **Deploy:** `apps/api` e `apps/workers` no **mesmo** `docker compose up`
   (`docs/deployment.md`, passo 4). O `apps/workers` antigo e o `apps/api` novo
   convivem só durante a troca de contêiner, e o valor `CustomHeader` só
   existe depois que um operador o grava.
4. **Rollback:**
   - rollback de `apps/api` sozinho: o `apps/api` antigo lê
     `"CustomHeader"` com o mesmo `HasConversion<string>()` e falha nas
     consultas que materializam aquela linha. **Antes de voltar o
     `apps/api`, converter ou remover os servidores `CustomHeader`.** A coluna
     nova é ignorada pelo modelo antigo;
   - rollback de `apps/workers` sozinho para **antes** desta change: mesmo
     risco da V6 para agentes vinculados a servidores `CustomHeader`. Mesma
     providência. Depois desta change, rollbacks de `apps/workers` para
     versões que já têm a tolerância (D5) são seguros para os tipos futuros;
   - o `Down` da migração remove a coluna, e os dois apps desta change passam
     a falhar ao consultar `mcp_servers`, porque os dois modelos mapeiam
     `AuthHeaderName`. Então o `Down` só roda **junto** com o rollback dos dois
     apps, e depois de converter ou remover os servidores `CustomHeader`, pelo
     motivo do item anterior. Não há perda de outro dado.

## Open Questions

Nenhuma de produto. As escolhas que pareciam de produto foram decididas com
o motivo em D3 (aceitar `Authorization`; ignorar `authHeaderName` fora de
`CustomHeader`) e em D4 (troca de tipo exige credencial nova). Se o dono
discordar de alguma, a decisão muda aqui antes do apply.

## Achados fora do escopo

Viram issue na hora (convenção 23), com o texto aprovado pelo dono antes da
criação. Entram no PR como `Refs`, porque ficam na fila (convenção 24):

- **#167: o valor da credencial `BearerToken` não é validado, e CR/LF nele
  injeta header na requisição ao servidor MCP (V4).** Reproduzido com
  `SocketsHttpHandler` real. A regra de valor de D3 resolve o caso para
  `CustomHeader`. Estendê-la ao `BearerToken` muda o contrato de um tipo
  existente e não é o pedido da #160.
- **#168: o frontend do tipo `CustomHeader`** (D7). Não é achado, é a camada
  de UI sequenciada depois desta change, com o protótipo aprovado como
  referência.
