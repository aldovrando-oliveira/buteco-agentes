> Branch `feat/160-mcp-autenticacao-header-customizado`, a partir de `18a2f3b`.
> Todo o trabalho é em **`apps/api`**, **`apps/workers`** e
> **`tests/CrossAppTaskStoreCompatibility.Tests`**, salvo os artefatos e a
> documentação. **`apps/frontend` não é tocado**: é a #168 (design.md, D7).
>
> **Antes de cada execução de suíte:** `podman ps` vazio e `uptime` registrado
> junto com o número, no início e no fim. Variáveis: `DOCKER_HOST` do Podman,
> `TESTCONTAINERS_RYUK_DISABLED=true`, `TZ=America/Sao_Paulo`,
> `DOTNET_SYSTEM_NET_DISABLEIPV6=1`. Contagem de suíte nunca vem de memória:
> mede-se ou lê-se do `02`.
>
> **Regra de guarda (convenção 15):** toda tarefa marcada **[guarda]** só fecha
> depois de o teste reprovar contra o defeito reintroduzido da tabela
> "Demonstração de que cada guarda reprova" do `design.md`, **no componente
> indicado lá**. O resultado (reprovou, mensagem e número de execuções) fica
> escrito inline na tarefa.

## 1. Verificações antes do código (exploração, feita)

- [x] 1.1 [scratchpad] Decompilado `ModelContextProtocol.Core` 2.0.0
  (`ilspycmd`). `AdditionalHeaders` vai em todo POST, no GET de stream e no
  DELETE. O cliente não valida o nome, e `CopyAdditionalHeaders` lança com o
  valor do header na mensagem (design.md, V1 e V2).
- [x] 1.2 [scratchpad] Medido com o SDK real, nome a nome: `Content-Type` e
  `X Bad` lançam com o segredo; `Authorization` é aceito; `Host` substitui o
  destino; `Accept`, `Mcp-Session-Id`, `MCP-Protocol-Version` e `Mcp-Method`
  são acrescentados ao valor do SDK (V2).
- [x] 1.3 [scratchpad] Medido com `SocketsHttpHandler` real: CR/LF no valor
  injeta header; `\n` e `\0` saem crus; não ASCII falha sem o valor na
  mensagem (V4).
- [x] 1.4 [scratchpad] Redação no log do `HttpClient`: documentação oficial
  (.NET 9+), `Microsoft.Extensions.Http` 10.0.10 decompilado e medição em
  `Trace`. O valor sai como `*` (V5).
- [x] 1.5 [scratchpad] `EnumToStringConverter` (EF Core 10.0.10) lança
  `InvalidOperationException` com valor desconhecido; `Enum.TryParse` aceita
  `"2"`, `"99"` e `"None,BearerToken"` (V6, V7).
- [x] 1.6 Issue #160 movida para `In progress` ao abrir a change
  (convenção 24).

## 2. Baseline

- [x] 2.1 [apps/api] Rodar a suíte de `apps/api` na branch antes de qualquer
  código e registrar a contagem com `uptime` no início e no fim.
  **756/756**, 1m47s, 18/10/2026 00:51–00:53, sobre `18a2f3b`. `podman ps`
  vazio no início e no fim; os contêineres `postgres` e `rabbitmq` do compose
  de dev foram parados antes, para cumprir o protocolo. Load 3,26 no início e
  16,13 no fim (contêineres da própria suíte).
- [x] 2.2 [apps/workers] Idem para `apps/workers`. **397/397**, 6m19s,
  00:54–01:01. `podman ps` vazio; load 5,26 no início e 4,17 no fim.
- [x] 2.3 [tests/] Idem para `tests/CrossAppTaskStoreCompatibility.Tests`.
  **2/2**, 13 s, 01:01. `podman ps` vazio; load 4,40 no início e 4,56 no fim.
  Nenhuma falha em nenhuma das três, então nada pode ser classificado como
  "pré-existente" nesta change (convenção 19).

## 3. Catálogo: modelo, migração e formato de fio (`apps/api`)

- [x] 3.1 [apps/api] Membro `CustomHeader` em `McpServerAuthType`.
  Propriedade `AuthHeaderName` (string anulável) em `McpServer`; construtor e
  `UpdateDetails` recebem o nome e o gravam só quando o tipo é
  `CustomHeader`; `UpdateDetails` limpa `EncryptedCredential` sempre que o
  `AuthType` **muda** (design.md, D4). Comentário no método cita D4 e a
  Decision 6 anterior que ele estende.
- [x] 3.2 [apps/api] Mapear `AuthHeaderName` em `AppDbContext` e gerar a
  migração `AddMcpServerAuthHeaderName` (coluna `text NULL`, sem default).
  Conferir no `.cs` gerado que é só um `AddColumn`/`DropColumn`.
  Gerada `20261018040303_AddMcpServerAuthHeaderName`: `Up` é só
  `AddColumn<string>("AuthHeaderName", "mcp_servers", "text", nullable: true)`,
  `Down` é só o `DropColumn`. O snapshot ganhou só a propriedade
  `AuthHeaderName` (`HasColumnType("text")`); nenhuma outra operação.
- [x] 3.3 [apps/api] `AuthHeaderName` em `CreateMcpServerRequest`,
  `UpdateMcpServerRequest`, `TestMcpServerConfigRequest`, nos commands
  correspondentes e em `McpServerResponse`.
  Nos três requests, `AuthHeaderName` entrou como **último parâmetro,
  opcional** (`= null`), para que os testes existentes, que constroem os
  requests de forma posicional, compilem sem edição (pré-requisito do 6.6).
- [x] 3.4 [apps/api] Teste de migração (`McpServerAuthHeaderNameMigrationTests`,
  no molde de `AgentMcpServerAllowedToolsMigrationTests`): um servidor
  `BearerToken` gravado antes da migração continua lido com
  `AuthHeaderName` nulo e o mesmo `AuthType` depois dela.
  Usa a `MigrationPostgresCollection` compartilhada (precedente mais recente
  que o molde citado, D12 de `historico-documentos-base`), e afirma também a
  credencial intacta. Filtrado: **1 teste executado, 1 aprovado**.
- [x] 3.5 [apps/api] **[guarda]** Formato de fio: o texto bruto da resposta
  de `POST /mcp-servers` contém `"authType":"CustomHeader"` e
  `"authHeaderName":"X-API-TOKEN"`, e a coluna `AuthType` lida por SQL contém
  `CustomHeader`. Defeito para reprovar: renomear o membro do enum.
  Com a correção: 1 executado, 1 aprovado. **Defeito da tabela** (membro
  renomeado para `HeaderToken`, com as referências, como um rename de IDE): 1
  executado, **reprovou** — `Assert.Equal() Failure: Expected: Created, Actual:
  BadRequest`. Reprova, mas na **entrada**: o nome antigo deixa de ser aceito
  antes de chegar ao fio. Para provar que as asserções sobre o **texto**
  também pegam, segunda reintrodução: sem o `[JsonConverter]` do enum (não há
  conversor global em `apps/api`), 1 executado, **reprovou** —
  `Assert.Contains() Failure: Sub-string not found ... Not found:
  ""authType":"CustomHeader""`. Defeitos desfeitos (`git diff` do enum mostra
  só o membro novo e o comentário) e o guarda voltou a passar: 1 executado,
  1 aprovado.

## 4. Validação (`apps/api`)

- [x] 4.1 [apps/api] `McpServers/Validation/McpAuthHeaderRules.cs`: as regras
  1 a 3 do nome (token RFC 9110; `TryAddWithoutValidation(nome, "x")`;
  prefixo `Mcp-` e a lista de reservados, sem caixa) e a regra do valor
  (`0x20`–`0x7E`, sem espaço nas pontas). Cada regra com o comentário da
  medição ou da RFC que a sustenta (design.md, V2, V4 e D3). Nenhuma mensagem
  de erro recebe o valor da credencial.
- [x] 4.2 [apps/api] `authType` por nome de membro definido, sem caixa, com a
  mensagem de valores aceitos gerada de `Enum.GetNames` (D9). Vale em
  `ValidateShape` e `ValidateTestShape`.
- [x] 4.3 [apps/api] Ligar as regras nos três pontos de entrada (`POST
  /mcp-servers`, `PUT /mcp-servers/{id}`, `POST /mcp-servers/test`), antes de
  qualquer chamada ao mediator. `authHeaderName` com tipo diferente de
  `CustomHeader` é ignorado (D3).
- [x] 4.4 [apps/api] Testes de `McpAuthHeaderValidationTests` pelos
  endpoints: nome válido (`X-API-TOKEN`, `Authorization`) é aceito; nome com
  espaço, `Content-Type`, `mcp-session-id`, `MCP-Protocol-Version`, `Host`,
  `Accept` e `Transfer-Encoding` são recusados com 400 em `authHeaderName`;
  nome ausente ou só espaço com `CustomHeader` é recusado; `authHeaderName`
  com `BearerToken` é gravado nulo. Cada caso de recusa afirma também que
  nenhum registro foi criado.
  Em `McpAuthHeaderValidationTests` (sobre `McpConnectionTestFixture`): 2
  nomes aceitos, 9 recusados (os 7 da lista mais nulo e só espaço) e o
  `BearerToken` com nome ignorado. Cada recusa afirma 400 em `authHeaderName`,
  o segredo fora do texto bruto e nenhum registro criado.
- [x] 4.5 [apps/api] Testes do valor: credencial com `\r\n`, com `ç` e com
  espaço na ponta são recusadas com 400 em `credential`, e o texto bruto da
  resposta não contém a credencial. Mais o caso de `POST /mcp-servers/test`
  com `CustomHeader` e credencial ausente e vazia: 400 em `credential`, e o
  fake não recebeu nenhuma requisição (design.md, D3).
  4 valores recusados no cadastro (`\r\n`, `çã`, espaço no início, espaço no
  fim), ausente e vazia no teste não salvo, e não ASCII no teste não salvo sem
  conectar. Grupo 4 inteiro, filtrado por `McpAuthHeaderValidationTests`:
  **24 executados, 24 aprovados**.
- [x] 4.6 [apps/api] **[guarda]** `POST /mcp-servers/test` com `CustomHeader`,
  **Ordem mudada:** a primeira demonstração (defeito aplicado com a 6.2 ainda
  por fazer) reprovou só pelo status, com `{"success":true,...}` e 3
  requisições ao fake. A causa: o tester ainda não enviava o header
  customizado, então o nome inválido nunca chegava ao SDK. A demonstração
  válida exige a 6.2 feita e a 6.2a ainda não, e roda nessa janela (ver
  abaixo).
  **Demonstração válida** (6.2 feita, 6.2a ainda não; validação tirada de
  `ValidateTestShape`): 1 executado, **reprovou** —
  `Assert.DoesNotContain() Failure: Sub-string found ... "t-Type' with value
  'SEGREDO-teste-nao-sal"`. É o vazamento da V3 medido no app real: a resposta
  200 do teste não salvo carregava o segredo. Restaurado: 1 executado, 1
  aprovado.
  `authHeaderName: "Content-Type"` e credencial `SEGREDO-…`: 400, o texto
  bruto não contém o segredo, e o fake não recebeu nenhuma requisição.
  Defeito para reprovar: tirar a validação de `ValidateTestShape` (deve virar
  200 com o segredo na mensagem, V3).
- [x] 4.7 [apps/api] **[guarda]** `authType` `"2"`, `"99"` e
  `"None,BearerToken"` recusados com 400; `"Basic"` responde com mensagem que
  cita os três tipos. Defeito para reprovar: voltar a `Enum.TryParse`.
  Com a correção: aprovados. **Defeito** (`Enum.TryParse` de volta em
  `McpAuthHeaderRules.TryParseAuthType`): 3 executados, **3 reprovaram** —
  `Assert.Equal() Failure: Expected: BadRequest, Actual: Created` para `"2"`,
  `"99"` e `"None,BearerToken"`. Restaurado; o grupo voltou a 24/24.

## 5. Cadastro, atualização e transições (`apps/api`)

- [x] 5.1 [apps/api] Handlers de create e update passam `AuthHeaderName` à
  entidade. O update mantém a ordem atual: `UpdateDetails`, depois a
  credencial nova se houver, depois a checagem `CredentialMissing`, antes do
  `SaveChanges`.
  Feito junto com a 3.1/3.3: o handler de create passa o nome ao construtor,
  e o de update ao `UpdateDetails`, mantida a ordem
  `UpdateDetails` → credencial nova → `CredentialMissing` → `SaveChanges`.
- [x] 5.2 [apps/api] Testes em `McpServerEndpointsTests`: criar com
  `CustomHeader` (201, nome devolvido, sem credencial no texto bruto); criar
  `CustomHeader` sem credencial (400 `credential`); `GET` por id e listagem
  devolvem `authHeaderName`.
  Quatro métodos novos (5 casos, com a teoria de credencial ausente e vazia),
  mais os 21 existentes sem edição: `McpServerEndpointsTests` **27/27**.
- [x] 5.3 [apps/api] Testes das transições do update, cada um da tabela de
  D4: `CustomHeader` → `CustomHeader` sem credencial e com nome novo mantém o
  segredo (afirmado pelo teste de conexão salvo, que envia o segredo antigo
  no header novo); `CustomHeader` → `None` limpa credencial e nome;
  `BearerToken` → `CustomHeader` com credencial nova grava os dois;
  `CustomHeader` → `BearerToken` com credencial nova zera o nome.
  Em `McpServerCustomHeaderConnectionTests`, porque o "mantém o segredo"
  precisa do re-teste salvo contra o fake, que `McpServerEndpointsTests`
  (sobre `ApiFactoryFixture`) não tem. **Ordem:** feita depois da 6.2/6.3,
  que dão o envio do header e o `RequiredHeader` do fake.
- [x] 5.4 [apps/api] **[guarda]** `BearerToken` → `CustomHeader` sem
  credencial e `CustomHeader` → `BearerToken` sem credencial respondem 400 em
  `credential`, e uma leitura posterior mostra o registro **inalterado**
  (tipo, nome e um teste de conexão salvo que ainda envia o segredo
  anterior). Defeito para reprovar: `UpdateDetails` limpando só em `None`.
  Com a correção: aprovados. **Defeito** (`UpdateDetails` limpando só em
  `None`): 2 executados, **2 reprovaram** — `Assert.Equal() Failure:
  Expected: BadRequest, Actual: OK` nos dois sentidos. Restaurado.

## 6. Conexão em `apps/api`

- [x] 6.1 [apps/api] `IMcpConnectionTester.TestAsync`/`ListToolsAsync`
  recebem `authHeaderName`. Repassar em
  `TestUnsavedMcpServerConnectionCommandHandler`,
  `TestSavedMcpServerConnectionCommandHandler`,
  `ListMcpServerToolsQueryHandler` e `ReplaceAgentMcpServersCommandHandler`.
  As três chamadas diretas de `McpConnectionTesterListToolsTests` ganharam
  `authHeaderName: null` (adaptação de assinatura, nenhuma asserção mudou).
- [x] 6.2 [apps/api] `McpConnectionTester.BuildTransport` com `switch` de três
  ramos. Comentário registra a duplicação crescente com
  `McpTransportFactory.BuildTransport` e o gatilho de reavaliação (D6).
- [x] 6.2a [apps/api] Em `TestAsync` e `ListToolsAsync`, antes de
  `BuildTransport` e quando o tipo é `CustomHeader`, checar a regra 2 (nome
  nulo, vazio ou recusado por `TryAddWithoutValidation(nome, "x")`). Se
  recusar, devolver falha `Unknown` sem requisição, com mensagem que cita o
  nome e nunca o valor. Comentário registra V9 e os três caminhos que a
  checagem cobre (D3).
  A falha cita o nome entre aspas e diz que nenhuma requisição foi feita;
  nunca o valor.
- [x] 6.3 [apps/api] `FakeMcpServerHttpMessageHandler` de `apps/api` ganha
  `RequiredHeader` (nome e valor; 401 se ausente ou diferente) e registra os
  headers recebidos por requisição.
  `RequiredHeader` é `(Name, Value)?`; os headers de cada requisição vão para
  `ReceivedHeaders`.
- [x] 6.4 [apps/api] Testes: teste não salvo com `CustomHeader` contra um fake
  que exige `X-API-TOKEN` (sucesso, sem `Authorization` em nenhuma
  requisição recebida); fake recusando com 401 (motivo credencial rejeitada);
  teste salvo e `GET /mcp-servers/{id}/tools` com `CustomHeader`.
  `McpServerCustomHeaderConnectionTests`: **11/11** com 6.5, 6.5a, 5.3 e 5.4.
- [x] 6.5 [apps/api] **[guarda]** "`CustomHeader` sem `Authorization`":
  afirmar a **ausência** de `Authorization` em todas as requisições
  registradas pelo fake. Defeito para reprovar: montar os dois headers.
  **Defeito** (`Authorization: Bearer` também no ramo `CustomHeader`): 1
  executado, **reprovou** — `Assert.All() Failure: 3 out of 3 items ...
  ["Authorization"] = "Bearer SEGREDO-abc"`. Restaurado.
- [x] 6.5a [apps/api] **[guarda]** Linha salva com nome recusado não vaza:
  gravar por SQL um servidor `CustomHeader` com `AuthHeaderName =
  'Content-Type'` e credencial cifrada que decifra para `SEGREDO-…`. `GET
  /mcp-servers/{id}/tools` e `POST /mcp-servers/{id}/test` respondem falha
  `Unknown`, o fake não recebe nenhuma requisição, e o texto bruto das duas
  respostas não contém o segredo. No mesmo teste, ou em
  `AgentMcpBindingToolValidationTests`, o `PUT /agents/{id}/mcp-servers` com
  uma tool desse servidor responde 502 sem o segredo no `detail`. Defeito
  para reprovar: remover a checagem do 6.2a (as respostas passam a conter o
  segredo, V3 e V9).
  **Defeito** (checagem do 6.2a desligada): 1 executado, **reprovou** —
  `Assert.DoesNotContain() Failure: Sub-string found ... "t-Type' with value
  'SEGREDO-linha-salva'"`. Como a primeira asserção esconde as outras, uma
  rodada de diagnóstico com o mesmo defeito leu os três corpos: **descoberta
  de tools, re-teste e 502 do vínculo continham o segredo** (`tools=True
  test=True binding=502:True`). Restaurado: aprovado.
- [x] 6.6 [apps/api] Regressão do Bearer: rodar os testes de Bearer
  existentes **sem editar nenhuma asserção**. Se algum precisar mudar, parar
  e investigar como regressão (design.md, Risks).
  Nenhum teste existente teve asserção editada: o `git diff` dos testes
  existentes só tem adições, mais as três chamadas do 6.1. Filtro `Mcp` no
  `apps/api`: **111 executados, 111 aprovados**, incluindo todos os de Bearer.

## 7. Execução (`apps/workers`)

- [x] 7.1 [apps/workers] Membro `CustomHeader` no enum espelho;
  `AuthHeaderName` no modelo espelho; `AuthType` passa a
  `McpServerAuthType?`, com conversor que devolve `null` para string que não é
  nome de membro definido (D5). O comentário do conversor registra V6 e o
  limite de D5 (não protege o `apps/workers` já implantado).
  **Ponto crítico medido antes do resto do grupo** (programa no scratchpad, EF
  Core 10.0.10 + Npgsql 10.0.3 contra `pgvector/pgvector:pg18` no
  Testcontainers): com `AuthType?` + `IsRequired()` + conversor que devolve
  `null` para nome não definido, o script gerado tem `"AuthType" text NOT
  NULL`, e o modelo dá `IsNullable = False` com `ClrType =
  Nullable<AuthType>`. Linhas `BearerToken`, `FuturoTipo`, `CustomHeader`,
  `None` e `2` na mesma consulta: `FuturoTipo` e `2` vêm `null`, as demais vêm
  com o valor, inclusive numa projeção anônima no formato da consulta do
  resolver. Gravar `null` é recusado pelo banco (`23502`). A D5 se mantém. O
  conversor usa `ParseKnownAuthType`, público em `AppDbContext` porque é
  chamado de dentro da expression tree.
- [x] 7.2 [apps/workers] Mapear em `AppDbContext` e gerar a migração espelho
  `AddMcpServerAuthHeaderName`, com o comentário de "nunca aplicada em
  runtime" no molde de `20260803021548_AddMcpServerCatalog` (V8). Conferir no
  `.cs` gerado e no snapshot que a migração tem só o
  `AddColumn`/`DropColumn` de `AuthHeaderName`, **sem nenhum `AlterColumn`
  em `AuthType`**: o modelo passou a `McpServerAuthType?`, e a coluna
  continua `NOT NULL` (D5).
  Gerada `20261018042026_AddMcpServerAuthHeaderName`: só
  `AddColumn`/`DropColumn` de `AuthHeaderName`, sem `AlterColumn` em
  `AuthType`. O snapshot mantém `AuthType` `IsRequired()` `text` e ganhou só
  `AuthHeaderName`. **O molde citado não tem o comentário**: o arquivo
  `20260803021548_AddMcpServerCatalog.cs` só tem `<inheritdoc />`, e o
  registro de "nunca aplicada" mora no `<summary>` de `AppDbContext`
  (linhas 16-19). O comentário foi posto na migração nova mesmo assim, com uma
  precisão que nenhum dos dois dizia: as fixtures de teste de `apps/workers`
  aplicam estas migrações (`WorkerInfrastructureFixture.MigrateAsync`).
- [x] 7.3 [apps/workers] `McpToolSetResolver`: antes de decifrar a credencial,
  excluir com `Warning` (id e nome, **sem** passar exceção ao logger) o
  servidor com `AuthType` nulo, e o `CustomHeader` com nome nulo, vazio ou
  recusado por `TryAddWithoutValidation(nome, "x")` (D3).
  O log da exclusão cita id, nome do servidor e nome do header, nunca a
  credencial, e não recebe exceção.
- [x] 7.4 [apps/workers] `McpTransportFactory.BuildTransport` com `switch` de
  três ramos, e o comentário espelho do 6.2 (D6).
- [x] 7.5 [apps/workers] `FakeMcpServerHttpMessageHandler` de `apps/workers`
  ganha `RequiredHeader` e o registro de headers recebidos, no mesmo formato
  do 6.3 (sem compartilhar código, isolamento entre apps).
  `RequiredHeader` fica em `FakeMcpServerConfig` (por servidor, como o
  `RequiredBearerToken` já ficava); os headers recebidos são lidos por
  `HeadersReceivedBy(url)`.
- [x] 7.6 [apps/workers] Testes em `McpToolSetResolverTests`: `CustomHeader`
  inclui as tools e não envia `Authorization`; `BearerToken` continua
  incluindo as tools; nome nulo exclui sem requisição HTTP.
  `McpToolSetResolverTests`: **26/26** (21 existentes sem edição de
  asserção, mais 5 novos). O helper `SeedMcpServerAsync` ganhou o parâmetro
  opcional `authHeaderName`.
- [x] 7.7 [apps/workers] **[guarda]** `AuthType` desconhecido: gravar por SQL
  uma linha com `AuthType = 'FuturoTipo'` ao lado de um servidor válido, os
  dois vinculados ao mesmo agente. A resolução não lança, o desconhecido não
  recebe requisição e as tools do válido estão no conjunto. Defeito para
  reprovar: voltar a `HasConversion<string>()` (deve lançar em
  `ResolveAsync`).
  Com a correção: aprovado. **Defeito** (`HasConversion<string>()` de
  volta): 1 executado, **reprovou** lançando dentro de `ResolveAsync` —
  `System.InvalidOperationException : Cannot convert string value
  'FuturoTipo' from the database to any value in the mapped
  'McpServerAuthType' enum`. Restaurado.
- [x] 7.8 [apps/workers] **[guarda]** Nome recusado no banco sem segredo no
  log: linha `CustomHeader` com `AuthHeaderName = 'Content-Type'` e
  credencial cifrada que decifra para `SEGREDO-…`. O servidor é excluído sem
  requisição, e **nenhuma** linha capturada do logger real (mensagem
  formatada e exceção) contém o segredo. Defeito para reprovar: remover a
  revalidação do 7.3 (o log passa a conter o segredo, V3).
  Com a correção: aprovado. **Defeito** (reconferência do 7.3 desligada): 1
  executado, **reprovou** — `Assert.All() Failure ... "Não foi possível
  conectar ao McpServer ..." ... Sub-string found ... "t-Type' with value
  'SEGREDO-no-log' from"`. É o `LogWarning(exception, ...)` do resolver
  gravando o segredo, medido. Restaurado.
- [x] 7.9 [apps/workers] Ponta a ponta em `McpToolExecutionEndToEndTests`: um
  agente vinculado a um servidor `CustomHeader` cujo fake exige
  `X-API-TOKEN`, com o LLM fake chamando a tool. O resultado chega à resposta
  final da task, e o fake não registra `Authorization`.
  Filtro `Mcp` no `apps/workers`, com os guardas restaurados: **42
  executados, 42 aprovados**.

## 8. Acordo api ↔ workers (`tests/CrossAppTaskStoreCompatibility.Tests`)

- [x] 8.1 [tests/] `McpServerCrossAppCompatibilityTests`, com o Postgres
  migrado pelo `apps/api` (molde de `PostgresTaskStoreCompatibilityTests`).
  Suíte do projeto: **4/4** (2 existentes e 2 novos).
- [x] 8.2 [tests/] **[guarda]** Para cada valor de
  `Buteco.Api.McpServers.Entities.McpServerAuthType`, gravar pelo contexto
  de `apps/api` e ler pelo de `apps/workers`; afirmar que o nome lido é igual
  ao gravado (não basta "não lançou", D5). Defeito para reprovar: apagar
  `CustomHeader` do enum de `apps/workers` (deve reprovar por nome, já que a
  tolerância do 7.1 impede a exceção).
  Com a correção: aprovado. **Defeito:** apagar o membro não compila (o
  resolver e a fábrica o referenciam), então o estado "o `apps/workers` não
  conhece `CustomHeader`" foi reproduzido renomeando o membro, com as duas
  referências, para `CustomHeaderDesconhecido`. Uma primeira rodada **não
  contou**: o `sed` do macOS não entende `\b`, as referências ficaram, o build
  falhou e **0 testes rodaram** (convenção 21). Refeita com a troca certa: 1
  executado, **reprovou pelo nome, sem exceção** — `Assert.Equal() Failure:
  Strings differ, Expected: "CustomHeader", Actual: null`. Restaurado; suíte
  4/4.
- [x] 8.3 [tests/] Linha `CustomHeader` gravada com a cifra de `apps/api`,
  resolvida pelo `McpToolSetResolver` real de `apps/workers` com a cifra de
  `apps/workers` e a mesma chave. O fake deste projeto
  (`Support/CrossAppFakeMcpServerHandler.cs`) recebe `X-API-TOKEN` com a
  credencial original.
  Afirma também que nenhuma requisição levou `Authorization`.

## 9. Log do `HttpClient` (D8)

- [x] 9.1 [apps/api] **[guarda]** `HttpClientHeaderRedactionTests`: pelo
  `WebApplicationFactory`, `IOptionsMonitor<HttpClientFactoryOptions>.Get(
  McpConnectionTester.HttpClientName).ShouldRedactHeaderValue("X-API-TOKEN")`
  é `true`. Defeito para reprovar:
  `.RedactLoggedHeaders(["Authorization"])` no registro do `Program.cs`.
  Teoria com `X-API-TOKEN` e `Authorization`, sobre o `ApiFactoryFixture`:
  2/2. **Defeito** aplicado: 2 executados, **1 reprovou** — o caso
  `X-API-TOKEN` (`Assert.True() Failure, Expected: True, Actual: False`),
  enquanto `Authorization` continuou redigido. É o mecanismo da V5: a lista
  desliga a redação de quem está fora dela. Restaurado.
- [x] 9.2 [apps/workers] **[guarda]** O mesmo, sobre o host real de
  `apps/workers` e `McpTransportFactory.HttpClientName`. Mesmo defeito,
  aplicado em `McpTransportFactory.AddHttpClient`.
  Sobre `McpTransportFactory.AddHttpClient`, o registro que o `Program.cs`
  chama: o `apps/workers` não tem host de teste montado a partir do
  `Program.cs`, e o precedente é o guarda do timeout de conexão. 2/2.
  **Defeito:** 2 executados, **1 reprovou**, o caso `X-API-TOKEN`, com a mesma
  mensagem. Restaurado.

## 10. Fechamento

- [x] 10.1 [apps/api, apps/workers, tests/] Rodar as três suítes e comparar
  com a baseline do grupo 2, com `uptime` no início e no fim. Toda diferença
  de contagem é explicada pelos testes novos.
  `apps/api` **800/800** (1m50s, 01:30–01:32, load 5,77 → 9,46);
  `CrossAppTaskStoreCompatibility.Tests` **4/4** (13 s, load 5,37 → 7,98);
  `apps/workers` **405/405** na 2ª rodada (7m51s, 01:42–01:50, load 5,49 →
  5,66). `podman ps` vazio no início e no fim de cada uma. Por nome, contra a
  baseline: api +44 (24 `McpAuthHeaderValidationTests`, 11
  `McpServerCustomHeaderConnectionTests`, 6 `McpServerEndpointsTests`, 2
  `HttpClientHeaderRedactionTests`, 1 `McpServerAuthHeaderNameMigrationTests`),
  workers +8 (5 `McpToolSetResolverTests`, 2 `HttpClientHeaderRedactionTests`,
  1 `McpToolExecutionEndToEndTests`), compatibilidade +2; **nenhum teste
  removido**. **A 1ª rodada do `apps/workers` deu 404/405** (7m20s,
  01:32–01:40): `KnowledgeIndexingTests.EachFragmentKeepsTheVectorOfItsOwnText_AcrossTheBatchBoundary`,
  `Expected: 5, Actual: 4`, a mesma assinatura da #130. **Não está
  classificada** como pré-existente, porque não apareceu na baseline (convenção
  19): 1 falha em 2 rodadas nesta árvore e 0 em 1 na baseline. Fica para o
  dono decidir.
- [x] 10.2 [docs] Atualizar `01-ARQUITETURA_E_CONVENCOES.md:46` e
  `docs/architecture.md:153`, que listam `None`/`BearerToken`, com o tipo novo
  e a coluna `AuthHeaderName`.
  Feito nos dois, com a regra de credencial vinculada ao tipo e, no
  `docs/architecture.md`, as regras do nome e a tolerância do `apps/workers`.
- [x] 10.3 [docs] Nota de deploy em `docs/deployment.md`: `apps/api` e
  `apps/workers` no mesmo `up`, e a providência antes de qualquer rollback
  para antes desta change (design.md, Migration Plan).
  Na seção "Redeploy": os dois apps no passo 4, a consulta dos servidores
  `CustomHeader` antes de um rollback e o limite do `Down` da migração.
- [x] 10.4 [docs] Entrada no `02-HISTORICO_E_STATUS.md` e no `CHANGELOG.md`,
  escritas **antes** do archive. A entrada registra que o frontend é a #168 e
  que a validação do valor do `BearerToken` é a #167.
  `02`: entrada nova no fim, com as medições, os guardas, a falha da #130 não
  classificada e a ordem de deploy. `CHANGELOG`: em `Added` (Integração MCP) e
  em `Changed`.
- [x] 10.5 [design] Toda divergência entre implementação e `design.md`
  corrigida no `design.md` com a causa (convenção 9).
  Seções "Divergências da implementação" e "O que a implementação confirmou
  com medição", antes de Risks, e a árvore de pastas ajustada aos arquivos
  reais. Nenhuma divergência muda decisão.
- [ ] 10.6 [PR] Depois do archive e do push (convenção 24), e só com
  autorização do dono, abrir o PR com estas linhas no corpo, cada uma em
  linha própria:

  ```
  Closes #160
  Refs #167
  Refs #168
  ```

  A #167 e a #168 ficam na fila e não são fechadas por este PR.
