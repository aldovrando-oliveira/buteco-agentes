Cada guarda marcado **(guarda)** só conta depois de visto reprovar contra o defeito que existe para pegar (convenção 15):

1. o defeito nomeado é reintroduzido;
2. o teste roda e reprova;
3. a árvore volta, só no trecho reintroduzido.

Fica registrado na tarefa:

- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes (guarda reintroduz só o defeito nomeado).

Suítes pelo `~/.cache/buteco-agents/bin/suite-235.sh`, com a regra assimétrica: verde conta mesmo com a CPU caindo, e reprovação só conta com a CPU em 100 do início ao fim. A baseline é `~/.cache/buteco-agents/235-baseline/r1/`, em `05aae2f`, com o relatório por nome em `nomes.txt` (GUID normalizado, #214). O `rt-inbox` da baseline tem uma reprovação válida intermitente do `OrphanDispatchRoundTripTests.Source3_WorkerStopsBetweenTerminalWriteAndPush_…` na `r1`, e passou na `r2`. Uma reprovação desse caso no fechamento é comparada com esse histórico antes de ser atribuída à change.

Parada: quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura. Correção que só toca comentário ou documentação segue e fica registrada (convenção 9).

## 1. Verificação do H6 e do H9 (apps/api, tests/), antes de qualquer código depender dela

- [x] 1.1 (apps/api) Esboço do `Identity/ServiceTokens/ServiceAccessTokenIssuer.cs`, só a geração, sem cache e sem uso no handler:
  - escopo pelo `IServiceScopeFactory`, e `IOpenIddictServerFactory.CreateTransactionAsync()`;
  - `GenerateTokenContext` de access token em JWT, com `CreateTokenEntry = false`;
  - o principal com `sub` e `client_id` `buteco-servico-api`, `oi_aud` e `oi_scp` `buteco-connectors`, `oi_iss` = `Options.Issuer`, e `oi_crt_dt`/`oi_exp_dt` pelo `TimeProvider` das opções, com 15 min (design D1, H9; Q3).
  - **Feito:** `Identity/ServiceTokens/ServiceAccessTokenIssuer.cs`, registrado como singleton no `AddIdentityIssuer`. O principal leva também o presenter (`oi_prst` = `client_id`), achado na 1.2.
- [x] 1.2 (apps/api) `Identity/ServiceAccessTokenIssuerTests`, formato, no `TestServer` com o anel carregado. Pedir um token ao `/connect/token` do `buteco-servico-inbox` pelo helper `ServiceClientTokens` da #234, gerar outro pelo issuer, e afirmar:
  - **cabeçalho:** `typ` `at+jwt`, o mesmo `alg`, e o `kid` igual nos dois e igual ao da primeira credencial do anel;
  - **claims de contrato:** `iss` igual nos dois; no do issuer, `client_id` e `sub` `buteco-servico-api`, `aud` só `buteco-connectors`, `scope` `buteco-connectors`, e `exp` − `iat` = 900;
  - **o mesmo conjunto de nomes de claims nos dois, sem exceção** (decisão do dono, Q3).

  **(guarda)** Contra dois defeitos, um de cada vez:
  - o principal sem `oi_iss`;
  - o principal sem `oi_scp`.
  - **Feito:** `TheTokenGeneratedInProcess_HasTheSameShape_AsTheOneFromTheTokenEndpoint`.
  - **Achado (convenção 9, só documentação):** a primeira rodada reprovou no conjunto de nomes. O token do `/connect/token` traz `oi_prst` (o presenter, igual ao `client_id`), que o `AttachDefaultPresenters` põe no sign-in e o `PrepareAccessTokenPrincipal` sempre mantém. Payloads medidos:
    - `/connect/token`: `iss, exp, iat, aud, scope, jti, sub, oi_prst, client_id`;
    - no processo, antes da correção: os mesmos menos `oi_prst`.

    O principal passou a levar `SetPresenters(ClientId)`, e o design (D1) foi corrigido. O comportamento pedido (mesmo conjunto de claims, sem exceção) não mudou.
  - **Guardas vistos reprovar** (`~/.cache/buteco-agents/bin/guarda-235.sh`, logs em `235-verificacao/guardas/`):
    - sem `oi_iss` → `Assert.Equal() Failure: Strings differ`, `Expected: "https://api.exemplo.test/"`;
    - sem `oi_scp` → `IDX14304: Claim … does not exist in the JsonClaimSet` (o `scope`).

    Voltou nas duas vezes: `apps/api/src/Buteco.Api/Identity/ServiceTokens/ServiceAccessTokenIssuer.cs` (sha igual `3fad040b`).
- [x] 1.3 (tests/) `ApiConnectorsRoundTrip.Tests`: o `apps/connectors` real aceita o token gerado pelo issuer na "descrever pasta" (nem `401` nem `403`) e o recusa com `403` em `GET /connectors/providers`. O token é obtido do issuer resolvido no `apps/api` da fixture. **(guarda)** Contra o principal com `oi_aud` `buteco-api`: a chamada recebe `401`.
  - **Feito:** `tests/ApiConnectorsRoundTrip.Tests/ServiceAccessTokenRoundTripTests.TheTokenGeneratedInProcess_IsAcceptedByTheRealConnectors_OnlyOnTheFolderRoute`. A "descrever pasta" não recebe `401`, `403` nem `503`, e `GET /connectors/providers` recebe `403`.
  - **Guarda visto reprovar:** com `oi_aud` `buteco-api` → `Assert.NotEqual() Failure: Values are equal`, `Actual: Unauthorized`. Voltou: `ServiceAccessTokenIssuer.cs` (sha igual).
- [x] 1.4 (apps/api) **Decisão do caminho.**
  - Se a 1.2 e a 1.3 passam, o H6 vale, e o caminho é a `IOpenIddictServerFactory`.
  - Se a geração exige o `HttpContext` ou o pipeline recusa o contexto, entra sem parada a alternativa: o `JsonWebTokenHandler` com a credencial ativa do `IdentityKeyRing.Snapshot`. Os mesmos testes 1.2 e 1.3 guardam o formato, e os guardas da 1.2 são refeitos contra os defeitos equivalentes (sem `iss`, sem `scope`).

  Registrar aqui qual caminho entrou e por quê, com a mensagem do OpenIddict se o H6 caiu. O fechamento informa o caminho.

  - **Caminho que entrou: a `IOpenIddictServerFactory` (H6 vale).**
    - `CreateTransactionAsync()` fora de requisição, mais um `GenerateTokenContext` despachado, geraram o token sem `HttpContext`, com a primeira credencial do anel (`kid` igual) e sem linha (`CreateTokenEntry = false`).
    - O H9 se confirmou: o principal precisa trazer `oi_iss`, `oi_aud`, `oi_scp`, `oi_crt_dt`, `oi_exp_dt`, `oi_prst`, `sub` e `client_id`, e os guardas da 1.2 provam dois deles.
    - A alternativa com o `JsonWebTokenHandler` não foi necessária.

## 2. ServiceAccessTokenIssuer completo e handler do apps/api (apps/api)

- [x] 2.1 (apps/api) O issuer completo:
  - **interface:** `ValueTask<string> GetAsync(CancellationToken)` e `Invalidate()`; `client_id`, `aud`, escopo e duração como constantes privadas;
  - **cache:** até faltar 1 min para o `exp`, pelo `TimeProvider`, sem pedido único (D2);
  - **log:** `Information` no primeiro token gerado e no primeiro depois de um `Invalidate()`, com `client_id` e validade, sem o token;
  - singleton no `Program.cs`.
  - **Feito:** cache com `Lock`, a margem pelo `TimeProvider` do DI e a linha `Information` ("Token de serviço buteco-servico-api gerado no processo, válido até …"), sem o token.
  - **Divergência (convenção 9, só forma):** o `Invalidate` ficou `internal void Invalidate(string token)`, e não `public Invalidate()`. O handler precisa do token para não derrubar o novo com um `401` atrasado (D4), e um método público com parâmetro violaria a superfície estreita (decisão do dono). O comportamento é o do design.
- [x] 2.2 (apps/api) `ServiceAccessTokenIssuerTests`, superfície: por reflexão, nenhum método público tem parâmetro além do `CancellationToken`. **(guarda)** Contra uma sobrecarga pública `GetAsync(string audience, CancellationToken)`.
  - **Feito:** `ServiceAccessTokenIssuerCacheTests.ThePublicSurface_TakesNothingButTheCancellationToken`. Afirma que o único método público declarado é `GetAsync`, só com `CancellationToken`.
  - **Guarda visto reprovar:** com a sobrecarga pública `GetAsync(string audience, …)` → `Expected ["GetAsync"]`, `Actual ["GetAsync", "GetAsync"]`. Voltou: `ServiceAccessTokenIssuer.cs` (sha igual `1ea7e6d7`).
- [x] 2.3 (apps/api) `ServiceAccessTokenIssuerTests`, cache, com `FakeTimeProvider`:
  - duas chamadas, uma geração;
  - em t+839 s, o mesmo token; em t+841 s, outro;
  - `Invalidate()` força outro.

  **(guarda)** Contra dois defeitos, um de cada vez: sem cache; margem zero.
  - **Feito:** `TheToken_IsReused_UntilOneMinuteBeforeItExpires` e `TheLine_IsLoggedForTheFirstToken_AndAfterAnInvalidation_NotForEachRenewal`. O `Invalidate` é provado pelo handler, porque é interno (2.1).
  - **Guardas vistos reprovar:**
    - sem cache → `Assert.Equal() Failure: Strings differ` (tokens diferentes na segunda chamada);
    - margem zero → `Assert.NotEqual() Failure: Strings are equal` em t+841 s.

    Voltou nas duas vezes: `ServiceAccessTokenIssuer.cs` (sha igual).
- [x] 2.4 (apps/api) `ServiceAccessTokenIssuerTests`, sem linha: a contagem de `openiddict_tokens` não muda depois de gerar (H11). **(guarda)** Contra `CreateTokenEntry = true` no contexto e o `TokenEntryStorageHandler` fora do caminho. Se não der para reintroduzir esse defeito sem tocar o handler global, registrar por quê, e o guarda vira o assert de contagem visto reprovar contra o handler global desligado.
  - **Feito:** `Generating_WritesNoTokenRow`, pelo `IOpenIddictTokenManager.CountAsync()`.
  - **Guarda:** o defeito precisou das duas peças, `CreateTokenEntry = true` no contexto **e** o `TokenEntryStorageHandler` fora do registro. Com as duas, o caso reprova com `InvalidOperationException: The application entry cannot be found in the database.`, no `CreateTokenEntry` do OpenIddict, porque o `buteco-servico-api` não está no catálogo. A gravação da linha falha antes de existir, e o teste reprova pela exceção, e não pela contagem.
  - **Controle:** só o `CreateTokenEntry = true`, com o handler global ligado, passa. O handler global já cobre o token gerado no processo (H11).

  Voltou: `ServiceAccessTokenIssuer.cs` e `IdentityIssuerServiceCollectionExtensions.cs` (sha igual).
- [x] 2.5 (apps/api) `Auth/ServiceTokenDelegatingHandler.cs` reescrito:
  - recebe o issuer;
  - anexa `Bearer`;
  - no `401`, chama `Invalidate()` só se o token anexado for o do cache;
  - não repete;
  - sai a constante `ApiSubject` (D4).
  - **Feito:** o handler recebe o `ServiceAccessTokenIssuer`, anexa, e no `401` chama `issuer.Invalidate(token)`. Saiu a constante `ApiSubject`. O registro no `Program.cs` não mudou: o `AddTransient<ServiceTokenDelegatingHandler>()` resolve o issuer, que é singleton do `AddIdentityIssuer`.
- [x] 2.6 (apps/api) `ServiceTokenDelegatingHandlerTests` reescrito (mudança de asserção declarada no design, D6). Afirma:
  - o token anexado é o do issuer (`client_id`, `aud`);
  - duas requisições, uma geração;
  - `401` → uma chamada ao destino, e a próxima gera outro;
  - `401` de um token já trocado não invalida o novo.

  **(guarda)** Contra três defeitos, um de cada vez:
  - o handler anexando HMAC;
  - repetir no `401`;
  - invalidar sem comparar.
  - **Feito:** `ServiceTokenDelegatingHandlerTests` com três casos (`Send_AttachesTheInProcessToken_AndReusesItAcrossRequests`, `Unauthorized_DiscardsTheToken_WithoutRetrying`, `ALateUnauthorized_ForATokenAlreadyReplaced_KeepsTheNewOne`). Eles substituem os dois de antes (`Send_AttachesBearerTokenWithServiceApiSubjectAndFiveMinuteLifetime`, `Send_TwoRequests_EachGetsItsOwnIssuedToken`), como declarado no D6.
  - **Guardas vistos reprovar:**
    - outro token que não o do issuer (`"hmac-legado"`) → os 3 casos reprovam, e o `CreateSynced_SendsServiceApiTokenToConnectors` também (`IDX14100: JWT is not well formed`);
    - repetir no `401` → `Unauthorized_DiscardsTheToken_WithoutRetrying` reprova (`Expected: Unauthorized`, `Actual: OK`);
    - invalidar sem comparar → `ALateUnauthorized_…` reprova (`Strings differ`).

    Voltou: o handler (sha igual `e01aed86`) e o issuer (sha igual `1ea7e6d7`).
- [x] 2.7 (apps/api) `ServiceScopeAuthorizationTests`: a referência a `ServiceTokenDelegatingHandler.ApiSubject` passa ao literal `service:api`, já que o `apps/api` não tem constante de receptor para ele. `KnowledgeBaseCatalogTests.CreateSynced_SendsServiceApiTokenToConnectors`: a asserção passa do `ITokenService.Validate` ao JWT do emissor com `client_id` `buteco-servico-api` e `aud` `buteco-connectors` (D6, declarada). **(guarda)** Contra o handler anexando HMAC.
  - **Feito:**
    - `ApiOutgoingSubject_OnKnowledgeBaseRoutes_ReturnsForbidden` usa o literal `service:api`, e saiu o `Assert.Equal` que só conferia a constante do handler;
    - `CreateSynced_SendsServiceApiTokenToConnectors` afirma `typ` `at+jwt`, `client_id` `buteco-servico-api` e `aud` `buteco-connectors`.
  - **Guarda:** o mesmo defeito do 2.6 ("outro token") reprova este caso (registro no 2.6).

## 3. ServiceAccessTokenSource, handler e boot (apps/inbox)

- [x] 3.1 (apps/inbox) `Auth/ServiceClientOptions.cs`:
  - seção `ServiceClient`, com `Secret`;
  - `ClientId = "buteco-servico-inbox"` constante;
  - `Auth/ServiceAccessTokenSchedule.cs`: margem de 1 min, espera de 10 s, timeout de 5 s; registrado no DI, trocável pelos testes.
  - **Feito:** `ServiceClientOptions` com as constantes `SectionName`, `SecretConfigurationName` e `ClientId`, e o `ServiceAccessTokenSchedule`, registrado com `TryAddSingleton`.
- [x] 3.2 (apps/inbox) `Support/FakeTokenEndpoint.cs`, nos testes: o duplo do `/connect/token`. Ele:
  - conta pedidos;
  - guarda os formulários recebidos;
  - segura a resposta até o teste soltar;
  - responde `200` com `access_token` e `expires_in`, `503`, `400 invalid_client`, `200` incompleto;
  - fica sem responder.
  - **Feito:** `Support/FakeTokenEndpoint.cs`. Sem nada programado, responde um token opaco novo (`token-de-servico-N`, `expires_in` 900). Tem `Hold()`, `Hang()`, `RespondStatus`, `RespondInvalidClient` e `WaitForRequestsAsync`.
- [x] 3.3 (apps/inbox) `Auth/ServiceAccessTokenSource.cs` (D3):
  - `HttpClient` nomeado `IssuerToken`, com `BaseAddress` = `Api:BaseUrl` e sem `DelegatingHandler`;
  - `FormUrlEncodedContent` com `grant_type`, `client_id`, `client_secret` e `scope=buteco-api`;
  - cache pelo `expires_in`;
  - `SemaphoreSlim(1)` com reconferência;
  - timeout próprio ligado ao token do chamador;
  - toda falha vira `HttpRequestException`;
  - espera de 10 s;
  - log `Error` no `invalid_client` e `Warning` nos demais, com `client_id` e motivo, sem segredo nem `Api:BaseUrl`;
  - `Information` no primeiro token e na recuperação;
  - `Invalidate(token)`.

  O comentário de cabeça aponta a cópia do `apps/connectors`.
  - **Feito:** `Auth/ServiceAccessTokenSource.cs`, mais o `ServiceAccessTokenRegistration.AddServiceAccessToken`. O registro inclui:
    - as opções;
    - o schedule;
    - o source singleton;
    - o cliente `IssuerToken`, com `BaseAddress` = `Api:BaseUrl` e `Timeout` infinito, porque o prazo é o do schedule no próprio pedido;
    - o handler.

    O `TimeProvider` entra com `TryAddSingleton(TimeProvider.System)`, como no `AddIssuerTokenAuthentication`.
- [x] 3.4 (apps/inbox) `ServiceAccessTokenSourceTests`, com o `FakeTokenEndpoint`, `FakeTimeProvider` e schedule curto. Cada caso é um **(guarda)** contra o defeito entre parênteses:
  - duas chamadas, um pedido (source sem cache);
  - t+839 s reaproveita, t+841 s pede (margem zero);
  - dez simultâneas com a resposta segurada → um pedido, o mesmo token nas dez. São dois guardas, um de cada vez: sem o semáforo; semáforo sem reconferir o cache;
  - formulário com segredo `+/=` chega idêntico, com `scope=buteco-api` e o `client_id` constante. São dois guardas: corpo montado à mão sem codificar; pedido sem `scope`;
  - `503` → `HttpRequestException` e `Warning` com `client_id` (lançar `InvalidOperationException`);
  - `invalid_client` → `HttpRequestException`, `Error`, e nenhuma linha contém o segredo nem o `Api:BaseUrl`. São dois guardas: linha que interpola o segredo; `Warning` no lugar de `Error`;
  - sem resposta → `HttpRequestException` no timeout do schedule, e o cancelamento do chamador continua `OperationCanceledException`. São dois guardas: sem timeout próprio; timeout sem envolver em `HttpRequestException`;
  - dentro da espera falha sem pedido, depois dela pede (sem a espera);
  - exatamente duas linhas `Information` em primeiro token, renovação, falha e recuperação (log a cada obtenção);
  - `200` sem `access_token` e `200` sem `expires_in` falham e não entram no cache (cache com validade zero ou infinita).
  - **Feito:** 12 casos em `ServiceAccessTokenSourceTests`, sem contêiner. O segredo de teste tem `+`, `/` e `=`.
  - **Guardas vistos reprovar** (`guarda2-235.sh`), um de cada vez, cada um com `ServiceAccessTokenSource.cs` de volta (sha igual `2c2b27f4`):

    | defeito | reprovou | mensagem |
    |---|---|---|
    | sem cache | `TwoCalls_MakeOneRequest`, `TheToken_IsReused_…`, `TenConcurrentCalls_…` | `Expected: "token-de-servico-1"` |
    | margem zero | `TheToken_IsReused_…` | `Strings are equal` em t+841 s |
    | sem semáforo (`SemaphoreSlim(10, 10)`) | `TenConcurrentCalls_…` | `Expected: 1`, `Actual: 3` |
    | semáforo sem reconferir o cache | `TenConcurrentCalls_…` | `Expected: 1`, `Actual: 10` |
    | corpo montado à mão sem codificar | `TheForm_ReachesTheEndpointIntact` | o segredo chega com espaço no lugar do `+` (`Strings differ`, pos 1) |
    | pedido sem `scope` | `TheForm_ReachesTheEndpointIntact` | `KeyNotFoundException: 'scope'` |
    | `503` lança `InvalidOperationException` | `ServiceUnavailable_…`, `AfterAFailure_…`, `TheObtainedLine_…` | `Expected HttpRequestException`, `Actual InvalidOperationException` |
    | linha de `invalid_client` interpolando o segredo | `InvalidClient_…` | `Sub-string found` (o segredo no `Error`) |
    | `Warning` no lugar de `Error` | `InvalidClient_…` | `Assert.Single() … did not contain any matching items` |
    | sem timeout próprio | `NoAnswer_FailsAtTheScheduleTimeout_…` | a chamada não terminou em 10 s |
    | timeout sem envolver | `NoAnswer_…` | `Actual: TaskCanceledException` |
    | sem a espera de 10 s | `AfterAFailure_…` | `No exception was thrown` |
    | log a cada obtenção | `TheObtainedLine_…` | `Expected: 2`, `Actual: 3` |
    | resposta sem `expires_in` com validade infinita (86.400 s) | `AnIncompleteAnswer_…` (sem `expires_in`) | `No exception was thrown` |
    | resposta sem `expires_in` com validade zero | o mesmo caso | `No exception was thrown` |
- [x] 3.5 (apps/inbox) `Auth/ServiceTokenDelegatingHandler.cs` reescrito: recebe o source, anexa, no `401` invalida se for o do cache, não repete. Sai `ServiceSubject`. `Program.cs`: `IssuerToken`, source e schedule, sem o `ITokenService` no handler. `SubjectAuthorizationTests`: as duas referências a `ServiceTokenDelegatingHandler.ServiceSubject` passam ao literal `service:inbox` (D4).
  - **Feito:**
    - o handler recebe o `ServiceAccessTokenSource`, e saiu `ServiceSubject`;
    - o `Program.cs` chama `AddServiceAccessToken`, no lugar do `AddTransient<ServiceTokenDelegatingHandler>()`, e o comentário do `AgentReferenceValidator` cita o token do emissor;
    - o `SubjectAuthorizationTests` usa o literal `service:inbox` nas duas referências.
- [x] 3.6 (apps/inbox) `ServiceTokenDelegatingHandlerTests` (novo): anexa o token do source; `401` → uma chamada e invalida; `401` de token já trocado não invalida. **(guarda)** Contra três defeitos, um de cada vez:
  - anexar HMAC;
  - repetir no `401`;
  - invalidar sem comparar.
  - **Feito:** `ServiceTokenDelegatingHandlerTests` com três casos (`Send_AttachesTheTokenFromTheSource`, `Unauthorized_DiscardsTheToken_WithoutRetrying`, `ALateUnauthorized_ForATokenAlreadyReplaced_KeepsTheNewOne`), sobre o source real e o `FakeTokenEndpoint`.
  - **Guardas vistos reprovar:**
    - outro token (`"hmac-legado"`) → os 3 casos (`Collections differ`);
    - repetir no `401` → `Unauthorized_…` (`Expected: Unauthorized`, `Actual: OK`) e `ALate…`;
    - invalidar sem comparar → `ALate…` (`Collections differ`, pos 3).

    Voltou: o handler (sha igual `9aa1b198`) e o source (sha igual `2c2b27f4`).
- [x] 3.7 (apps/inbox) `Auth/ServiceClientConfigurationValidation.cs`, chamado no `Program.cs` depois do `Build()`. Confere presente, base64, ≥ 32 bytes, e distinto por bytes da `Inbox:CredentialEncryptionKey` (decisão do dono, Q2). As mensagens nomeiam a chave, ou as duas chaves iguais, sem o valor.
  - **Feito:** `ValidateServiceClientConfiguration()`, chamada no `Program.cs` logo depois do `ValidateIssuerClientSubjects()`. A comparação com a chave de cifra só entra quando ela decodifica: o formato dela é conferido pelo `AesGcmChannelCredentialCipher`.
- [x] 3.8 (apps/inbox) `ServiceClientConfigurationTests`, composição real pela `WebApplicationFactory`:
  - ausente, fora de base64 e 16 bytes derrubam o boot nomeando `ServiceClient:Secret`;
  - igual à `Inbox:CredentialEncryptionKey` derruba nomeando as duas;
  - nenhuma mensagem contém o valor;
  - com o segredo válido, sobe.

  **(guarda)** Contra três defeitos, um de cada vez:
  - a chamada retirada do `Program.cs`;
  - a comparação com a `Inbox:CredentialEncryptionKey` retirada: só o caso dela reprova;
  - a mensagem que interpola o valor.
  - **Feito:** `ServiceClientConfigurationTests`, pela `WebApplicationFactory` da aplicação real, sem banco. Os casos:
    - ausente;
    - fora de base64;
    - 16 bytes;
    - igual à `Inbox:CredentialEncryptionKey`;
    - válido sobe.
  - **Guardas vistos reprovar:**
    - chamada retirada do `Program.cs` → os 4 casos de recusa (`No exception was thrown`); voltou `Program.cs` (sha igual `48194d90`);
    - comparação com a chave retirada → só o caso dela;
    - mensagem de tamanho interpolando o valor → o caso de 16 bytes (`Sub-string found`).

    Voltou: `ServiceClientConfigurationValidation.cs` (sha igual `bf9c15c6`).
- [x] 3.9 (apps/inbox) Fixtures (D6):
  - `TestAuthentication.ConfigOverrides` ganha o `ServiceClient:Secret` de teste, gerado com `openssl rand -base64 32`, distinto da `Inbox:CredentialEncryptionKey` de teste;
  - a `InboxFactoryFixture` troca o primário do `IssuerToken` por um `FakeTokenEndpoint` que sempre responde.

  A suíte do `apps/inbox` sobe inteira, e os casos de canal (`ChannelEndpointsTests`, `SubjectAuthorizationTests`) continuam com os mesmos nomes e resultados.
  - **Feito:** `TestAuthentication.ServiceClientSecret` (`openssl rand -base64 32`) em `ConfigOverrides`, e o `TokenEndpoint` da `InboxFactoryFixture` no `IssuerToken`.
  - **Suíte inteira do `apps/inbox`** (`suite-235.sh`, `235-trabalho/inbox`): 322/322, CPU mínima 100. Pelo `compare-trx.py` contra a `235-baseline/r1`, os 302 de antes estão todos presentes, com o mesmo resultado, e há 20 novos aprovados.
- [x] 3.10 (apps/inbox) `appsettings.Development.json`: `ServiceClient:Secret` igual ao `Identity:ServiceClients:Inbox:Secret` de desenvolvimento do `apps/api`, marcado como de desenvolvimento.
  - **Feito:** `ServiceClient:Secret` de desenvolvimento igual ao `Identity:ServiceClients:Inbox:Secret` do `apps/api`, com comentário "SÓ PARA DESENVOLVIMENTO".

## 4. ServiceAccessTokenSource, handler e boot (apps/connectors)

- [x] 4.1 (apps/connectors) Cópia de 3.1 a 3.3, com `ClientId = "buteco-servico-connectors"`. O `IssuerToken`, o source e o schedule são registrados dentro do `AddSyncCycle` (`Sync/SyncRegistrationExtensions.cs`), só com `Api:BaseUrl`. Os comentários de cabeça das duas cópias apontam uma para a outra.
  - **Feito:** cópia de `ServiceClientOptions`, `ServiceAccessTokenSchedule` e `ServiceAccessTokenSource`, com `ClientId = "buteco-servico-connectors"`. Os comentários de cabeça apontam uma cópia para a outra. Registro pelo `AddServiceAccessToken`, chamado dentro do `AddKnowledgeSync`.
  - **Divergência (convenção 9, só forma):** o source e o `IssuerToken` são registrados **sempre**, e não só com `Api:BaseUrl`, como o `SyncApiClient` e o handler já eram. O `BaseAddress` só entra com `Api:BaseUrl`. Sem o endereço, ninguém chama o source: o ciclo não roda, e o "Sincronizar agora" responde `sync-not-configured` antes. A checagem de boot continua só com `Api:BaseUrl` (4.5).
- [x] 4.2 (apps/connectors) `ServiceAccessTokenSourceTests`: a mesma lista da 3.4, cada caso **(guarda)** visto reprovar contra o mesmo defeito, nesta cópia.
  - **Feito:** os mesmos 12 casos, com o `ManualTimeProvider` do projeto. O `Microsoft.Extensions.TimeProvider.Testing` não é referenciado aqui, e nenhum pacote novo entrou. A captura de log é local ao teste.
  - **Guardas vistos reprovar:** os mesmos 15 defeitos da 3.4, um de cada vez, cada um reprovando nos mesmos casos. Voltou: `apps/connectors/src/Buteco.Connectors/Auth/ServiceAccessTokenSource.cs` (sha igual `1346c0dc`). Logs em `235-verificacao/guardas/4.2*`.
- [x] 4.3 (apps/connectors) `Auth/ServiceTokenDelegatingHandler.cs` reescrito, como a 3.5. Sai `ConnectorsSubject`.
  - **Feito:** o handler recebe o source, e saiu `ConnectorsSubject`. O `SyncRegistrationExtensions` troca o `AddTransient<ServiceTokenDelegatingHandler>()` pelo `AddServiceAccessToken`.
- [x] 4.4 (apps/connectors) `ServiceTokenDelegatingHandlerTests` reescrito (mudança de asserção declarada, D6), com os casos da 3.6. **(guarda)** Contra os mesmos três defeitos.
  - **Feito:** três casos, que substituem o `EveryRequest_CarriesAFreshServiceConnectorsToken`, como declarado no D6.
  - **Guardas vistos reprovar:**
    - outro token → os 3 casos;
    - repetir no `401` → `Unauthorized_…` e `ALate…`;
    - invalidar sem comparar → `ALate…`.

    Voltou: o handler (sha igual `e96a753d`) e o source (sha igual `1346c0dc`).
- [x] 4.5 (apps/connectors) Checagem do segredo depois do `ValidateApiConfiguration`, só com `Api:BaseUrl`: presente, base64, ≥ 32 bytes, sem ecoar o valor.
  - **Feito:** `Auth/ServiceClientConfigurationValidation.cs`, chamado no `Program.cs` logo depois do `ValidateApiConfiguration()`. Sai sem conferir quando `Api:BaseUrl` não está configurado.
- [x] 4.6 (apps/connectors) `ServiceClientConfigurationTests`:
  - com `Api:BaseUrl`, ausente, fora de base64 e curto derrubam o boot nomeando `ServiceClient:Secret`, sem o valor;
  - sem `Api:BaseUrl` e sem segredo, sobe.

  **(guarda)** Contra dois defeitos, um de cada vez:
  - a checagem incondicional: o caso sem `Api:BaseUrl` reprova;
  - a chamada retirada do `Program.cs`.
  - **Feito:** `ServiceClientConfigurationTests`, pela `ConnectorsFactory`:
    - com `Api:BaseUrl`, ausente, fora de base64 e 16 bytes falham nomeando a chave, sem o valor;
    - sem `Api:BaseUrl` e sem segredo, sobe;
    - com segredo válido, sobe.
  - **Guardas vistos reprovar:**
    - checagem incondicional → `WithoutTheApiAddress_TheSecretIsNotRequired`; voltou `ServiceClientConfigurationValidation.cs` (sha igual `2157e5e9`);
    - chamada retirada do `Program.cs` → os 3 casos de recusa; voltou `Program.cs` (sha igual `337de434`).
- [x] 4.7 (apps/connectors) `ConnectorsFactory`: com `Api:BaseUrl`, o segredo de teste e o `IssuerToken` → `FakeTokenEndpoint` que sempre responde. A suíte sobe inteira, com os mesmos nomes e resultados nos casos do ciclo.
  - **Feito:** `ConnectorsFactory` com `ServiceClient:Secret` de teste sempre (por `UseSetting`) e `IssuerToken` → `TokenEndpoint` sempre.
  - **Suíte inteira do `apps/connectors`** (`235-trabalho/connectors`): 337 aprovados e 1 ignorado, CPU mínima 100. Pelo `compare-trx.py`, a única ausência é o `EveryRequest_CarriesAFreshServiceConnectorsToken`, substituído como declarado no D6. Há 21 novos aprovados.
- [x] 4.8 (apps/connectors) `SecretLeakTests`: o `ServiceClient:Secret` entra na lista de valores que nenhum log pode conter. **(guarda)** Contra a linha de `invalid_client` interpolando o segredo.
  - **Feito:** o caso do ciclo também procura o `ServiceClientSecret` nos logs, e há um caso novo, `NenhumLogDoSegredoRecusadoContemOSegredoNemOEndereco`: o emissor recusa com `invalid_client`, o "Sincronizar agora" responde `503`, como com o `apps/api` fora, e nada chega ao `apps/api` falso.
    - O segredo é procurado em todas as categorias.
    - O endereço é procurado só nas categorias do app. As linhas do `HttpClient` do framework trazem a url do pedido em `Information`, e o `appsettings.Production.json` do `apps/connectors` já as corta em `Warning`.
  - **Medido ao escrever o caso:** a primeira versão procurava o endereço em todas as categorias e reprovou na linha `Start processing HTTP request GET http://…` do `System.Net.Http.HttpClient.SyncApi`. É log do framework, que já existia com o HMAC, e não desta change.
  - **Guarda visto reprovar:** linha de `invalid_client` interpolando o segredo → `Sub-string found`. Voltou: `ServiceAccessTokenSource.cs` (sha igual `1346c0dc`).
- [x] 4.9 (apps/connectors) `appsettings.Development.json`: `ServiceClient:Secret` igual ao `Identity:ServiceClients:Connectors:Secret` de desenvolvimento do `apps/api`.
  - **Feito:** `ServiceClient:Secret` de desenvolvimento igual ao `Identity:ServiceClients:Connectors:Secret` do `apps/api`, com comentário "SÓ PARA DESENVOLVIMENTO".

## 5. Acordos de tests/ com o emissor real (tests/)

- [x] 5.1 (tests/) `InboxOrchestratorRoundTrip.Tests/Support/RoundTripFixture.cs`:
  - liga o `IssuerToken` e o `AgentReferenceValidator` do `apps/inbox` ao `TestServer` do `apps/api`;
  - `ServiceClient:Secret` igual ao `Identity:ServiceClients:Inbox:Secret`;
  - os dois segredos de teste do `apps/api` trocam por valores com `+`, gerados com `openssl rand -base64 32` até sair um `+`;
  - um gravador das chamadas do inbox ao `apps/api` (método, caminho, status, token) e um contador de `POST /connect/token`;
  - antes do primeiro pedido, espera o catálogo sincronizar os clientes de serviço.

  Os 12 casos de hoje continuam, com os mesmos nomes, incluído o `OperatorHmacToken_…`, que fica até a #182.
  - **Feito:**
    - os segredos de teste do `apps/api` são `InboxServiceClientSecret` e `ConnectorsServiceClientSecret`, os dois com `+`;
    - o `WarmUpIssuerAsync` roda no `InitializeAsync`, depois da migração do `apps/api`;
    - o inbox recebe `ServiceClient:Secret`;
    - os clientes A2A, `AgentReferenceValidator` e `IssuerToken` vão ao `TestServer` do `apps/api` por um `RecordingHandler`, que grava em `InboxToApi` (`ServiceCall`: cliente, método, caminho, status, token);
    - o `BuildExtraInboxAsync(secret, calls, logs)` monta outro inbox num banco próprio do mesmo contêiner, para as varreduras dele não disputarem as linhas do principal.

    O `OperatorHmacToken_…` continua.
- [x] 5.2 (tests/) `ServiceTokenRoundTripTests` (novo), S2: uma mensagem de canal chega ao agente. Todas as chamadas do inbox ao `apps/api` levam JWT com `client_id` `buteco-servico-inbox` e `aud` `buteco-api`, nenhuma recebe `401`/`403`, e há exatamente um `POST /connect/token`. **(guarda)** Contra o pedido sem `scope`: o despacho recebe `401`.
  - **Feito:** `ServiceTokenRoundTripTests.S2_AChannelMessageReachesTheAgent_WithTheIssuerToken_AndOneTokenRequest`. Toda chamada A2A leva JWT `at+jwt` com `client_id` `buteco-servico-inbox` e `aud` `buteco-api`, nenhuma recebe `401`/`403`, e há um único `POST /connect/token` na vida do inbox da classe.
  - **Guarda visto reprovar:** com o pedido sem `scope`, o S2 e o S1 reprovam (o S1 com `Expected: Created`, `Actual: BadGateway`). Voltou: `ServiceAccessTokenSource.cs` (sha igual `2c2b27f4`).
- [x] 5.3 (tests/) S1: o cadastro de canal no `apps/inbox`, com o token real de usuário da fixture, valida o `AgentId` no `apps/api` real. Agente existente → `201`; agente inexistente → a recusa de hoje. **(guarda)** Contra o `AgentReferenceValidator` sem o handler de saída: o cadastro recebe `502`.
  - **Feito:** `S1_TheChannelRegistration_ValidatesTheAgentAgainstTheRealApi_WithTheIssuerToken`. Com o token real de usuário do escopo `buteco-inbox`:
    - agente existente → `201`;
    - agente inexistente → `400`, a recusa de hoje (`AgentNotFound`);
    - as validações levam o token do `buteco-servico-inbox`, e o `apps/api` real responde `200` e `404`.
  - **Guarda visto reprovar:** com o `AgentReferenceValidator` sem o handler de saída, o S1 e o caso divergente reprovam (`Expected: Created`). Voltou: `apps/inbox/src/Buteco.Inbox/Program.cs` (sha igual `48194d90`).
- [x] 5.4 (tests/) Segredo divergente, com o `apps/inbox` montado com outro segredo:
  - o `/connect/token` responde `invalid_client`;
  - o cadastro de canal responde `502`, sem canal criado;
  - o despacho deixa a mensagem pendente para nova tentativa, sem falha terminal;
  - nenhuma chamada leva HMAC.

  **(guarda)** Contra o source lançando `InvalidOperationException` em vez de `HttpRequestException`: o despacho vira falha terminal, ou o cadastro vira `500`.
  - **Feito:** `ADivergentSecret_IsInvalidClient_AndBothFlowsFailAsTheApiDown`, com um inbox extra de outro segredo:
    - o cadastro de canal responde `502`, sem canal criado;
    - o despacho loga 2 avisos "Falha de transporte" e depois "Mensagem de usuário perdida após 3 tentativas" (o `Debounce:MaxDispatchAttempts` da fixture é 3), nunca "rejeitado no nível de protocolo";
    - há um `Error` de `invalid_client`;
    - todas as chamadas do inbox extra são pedidos de token recusados, e nenhuma chegou às rotas do `apps/api` nem levou HMAC.
  - **Medido:** o emissor real responde `invalid_client` com **`401`**, e não `400`. O source decide pelo campo `error`, então não mudou. O `FakeTokenEndpoint` dos dois apps passou a responder `401`, como o real.
  - **Correção (convenção 9; spec e design):** o cenário "Segredo divergente no despacho" dizia que a mensagem "não é encerrada como falha terminal". O caminho de transporte a devolve a pendente só até `Debounce:MaxDispatchAttempts`, e depois a encerra, como hoje com o `apps/api` fora. O cenário passou a afirmar isso, e o design (D9) registra a correção. O comportamento não muda, e a cobertura não cai.
  - **Guarda visto reprovar:** com o source lançando `InvalidOperationException` no lugar de `HttpRequestException`, o caso reprova já no cadastro (`Expected: BadGateway`, `Actual: InternalServerError`), antes da metade do despacho. Voltou: `ServiceAccessTokenSource.cs` (sha igual `2c2b27f4`).
  - **`rt-inbox` inteiro** (`235-trabalho/rt-inbox`): 15/15, CPU mínima 100. Os 12 da baseline estão todos presentes, e o `Source3_…` (#176), que reprovou na `r1` da baseline, passou. Há 3 casos novos.
- [x] 5.5 (tests/) `ApiConnectorsRoundTrip.Tests/Support/RoundTripFixture.cs`:
  - liga o `IssuerToken` do `apps/connectors` ao `TestServer` do `apps/api`;
  - `ServiceClient:Secret` igual ao `Identity:ServiceClients:Connectors:Secret`, com `+`;
  - o `Build` passa a receber o segredo e o `Identity:Issuer` do `apps/connectors`, no lugar da chave HMAC, que fica fixa;
  - espera a sincronização do catálogo.
  - **Feito:**
    - `Build(connectorsServiceClientSecret = ConnectorsServiceClientSecret, connectorsIssuer = ApiPublicBaseUrl)`, com a chave HMAC fixa, e os chamadores passaram a `Build()`;
    - os segredos de teste do `apps/api` são os mesmos com `+` do `rt-inbox`;
    - o `IssuerToken` do `apps/connectors` vai ao `TestServer` do `apps/api` por um gravador próprio (`ConnectorsTokenRequests`), à parte do `ConnectorsToApi`, para as contagens de upsert não mudarem;
    - o `InitializeAsync` sincroniza o catálogo depois de migrar.
- [x] 5.6 (tests/) S3:
  - `SyncCycle_TokenOfTheConnectors_IsAcceptedByTheRealApi` passa a afirmar JWT com `client_id` `buteco-servico-connectors` e `aud` `buteco-api`, e nenhuma chamada `401`/`403` (D6, declarada);
  - caso novo, **segredo divergente:** `invalid_client`, o ciclo fica `Unreachable`, e nenhum upsert chega ao `apps/api`.

  **(guarda)** Contra dois defeitos, um de cada vez: o handler anexando HMAC, que reprova a asserção do token; o source engolindo o `invalid_client` e mandando sem token, que reprova o caso novo pelo `401` em vez de `Unreachable`.
  - **Feito:**
    - `SyncCycle_TokenOfTheConnectors_IsAcceptedByTheRealApi` afirma JWT `at+jwt` com `client_id` `buteco-servico-connectors` e `aud` `buteco-api`, como declarado no D6;
    - caso novo, `SyncCycle_WithADivergentSecret_IsInvalidClient_AndNothingReachesTheApi`: o "Sincronizar agora" responde `503`, como com o `apps/api` fora; o único pedido de token recebe `401` do emissor real; nenhuma chamada do ciclo chega ao `apps/api`; nenhum documento é criado.
  - **Guardas vistos reprovar:**
    - o handler anexando outro token → `SyncCycle_TokenOfTheConnectors_…` (`Expected: Accepted`, `Actual: BadGateway`);
    - o handler engolindo a falha do token e mandando sem `Authorization` → o caso divergente (`Expected: ServiceUnavailable`, `Actual: BadGateway`).

    Voltou: `apps/connectors/src/Buteco.Connectors/Auth/ServiceTokenDelegatingHandler.cs` (sha igual `e96a753d`).
- [x] 5.7 (tests/) S4: os três `CreateSynced_*` passam pelo issuer real, sem mudança de asserção. `CreateSynced_ConnectorsWithADifferentSigningKey_Is502ConnectorsError401` passa a `CreateSynced_ConnectorsWithADifferentIssuer_Is502ConnectorsError401`, com a mesma asserção e o `Identity:Issuer` do `apps/connectors` divergente (decisão do dono, Q4; troca de premissa registrada no design). **(guarda)** O caso com a causa antiga, a chave HMAC divergente, precisa reprovar (criar a base) com o handler novo. É a prova de que a causa antiga deixou de valer, registrada aqui.
  - **PARADA (03/11/2026): premissa falsa que muda o contrato decidido (Q4).** Com o `Identity:Issuer` divergente, o `apps/connectors` nunca obtém as chaves: o `IssuerKeysRetriever` recusa o discovery cujo `issuer` difere do configurado. Sem chaves, o `JwtBearer` responde **`503`** ("ainda sem chave", `ChallengeWithoutKeysAsUnavailable`), e não `401`. O `apps/api` então responde `502` `connectors-error` com `detail` **`"503"`**, e o teste reprova (`Expected: "401"`, `Actual: "503"`, CPU em 100, reprovação válida). A asserção decidida não é alcançável com essa causa.
  - **Decisão do dono na parada (03/11/2026):** a causa passa a ser **as chaves de outro emissor com o mesmo issuer**, e a asserção (`502` `connectors-error` `401`, nenhuma base criada) fica.
  - **Feito:**
    - o `TestIssuer` do projeto de teste do `apps/connectors` entra no `rt-connectors` por link, como o `FakeConnector`;
    - o `Build` ganhou `connectorsKeysFrom`, e saiu o `connectorsIssuer`;
    - o caso passou a `CreateSynced_ConnectorsWithKeysFromAnotherIssuer_Is502ConnectorsError401`, com um `TestIssuer` de `AnnouncedIssuer` igual à url pública do `apps/api`;
    - o cenário da spec foi renomeado para "Chaves de outro emissor no apps/connectors", e o design registra a revisão da Q4.

    Os três `CreateSynced_*` passam sem mudança de asserção.
  - **Guarda visto reprovar:** a causa antiga (chave HMAC divergente no `apps/connectors`, sem o emissor de teste), com o handler novo, cria a base (`Expected: BadGateway`, `Actual: Created`). É a prova de que a causa antiga deixou de valer. Voltou: a fixture e o teste (sha igual).
- [x] 5.8 (tests/) Rodar `rt-inbox`, `rt-connectors` e `rt-crossapp` e comparar por nome com a baseline. Esperado:
  - `rt-inbox`: os 12 de antes, mais os novos;
  - `rt-connectors`: 7 iguais, 1 renomeado (5.7), mais os novos;
  - `rt-crossapp`: 4 iguais.
  - **Feito** (`235-trabalho/`), comparado por nome com a `235-baseline/r1`:

    | suíte | resultado | por nome | CPU mínima |
    |---|---|---|---|
    | `rt-inbox` | 15/15 | os 12 de antes presentes; o `Source3_…` (#176), reprovado na `r1` da baseline, passou; 3 novos | 100 |
    | `rt-connectors` | 10/10 | 7 iguais; 1 renomeado (5.7); 3 novos: S3 divergente, S4 de chaves de outro emissor com o nome novo, aceitação do H6 | 100 |
    | `rt-crossapp` | 4/4 | iguais | 100 |

## 6. Deploy e documentação (deploy, docs)

- [x] 6.1 (deploy) `docker-compose.prod.yml`: `ServiceClient__Secret: ${SERVICE_CLIENT_SECRET_INBOX:?…}` no `inbox` e `${SERVICE_CLIENT_SECRET_CONNECTORS:?…}` no `connectors`. O `Auth__TokenSigningKey` continua nos três. O comentário do `connectors` sobre a chave passa a dizer que ela só valida HMAC (D8).
  - **Feito:** `ServiceClient__Secret` com `:?` no `inbox` (`SERVICE_CLIENT_SECRET_INBOX`) e no `connectors` (`SERVICE_CLIENT_SECRET_CONNECTORS`). Os comentários do `Auth__TokenSigningKey` nos dois dizem que a chave só valida o HMAC que ainda chega.
- [x] 6.2 (deploy) `.env.prod.example`: o comentário dos dois segredos diz que cada um vai ao `api` e ao serviço dele.
  - **Feito:** o comentário diz que cada segredo vai ao `api` e ao serviço dele, e que o do inbox não pode ser igual a `INBOX_CREDENTIAL_ENCRYPTION_KEY`.
- [x] 6.3 (deploy) Conferência do compose, com a saída registrada aqui:
  - sem `SERVICE_CLIENT_SECRET_INBOX`, o `config` falha citando a variável;
  - com as duas, o `api`, o `inbox` e o `connectors` recebem os valores esperados.
  - **Feito** com `podman-compose`, porque não há `docker` nesta máquina, sobre cópias do `.env.prod.example` com valores de teste:
    - sem `SERVICE_CLIENT_SECRET_INBOX` → saída 1, `ValueError: required variable SERVICE_CLIENT_SECRET_INBOX is missing a value: …`;
    - sem `SERVICE_CLIENT_SECRET_CONNECTORS` → saída 1, a mesma mensagem com `SERVICE_CLIENT_SECRET_CONNECTORS`;
    - com as duas → saída 0, e cada valor chega aos seus dois serviços:

      ```
      api Identity__ServiceClients__Connectors__Secret: segredo-connectors-teste
      api Identity__ServiceClients__Inbox__Secret: segredo-inbox-teste
      connectors ServiceClient__Secret: segredo-connectors-teste
      inbox ServiceClient__Secret: segredo-inbox-teste
      ```

    O `podman-compose` cita a primeira ocorrência da variável, que é a do serviço `api`.
- [x] 6.4 (docs) `docs/configuration.md`: `ServiceClient:Secret` no `apps/inbox` (obrigatório, distinto da `Inbox:CredentialEncryptionKey`) e no `apps/connectors` (obrigatório com `Api:BaseUrl`).
  - **Feito:**
    - a tabela "mesmo valor em mais de um processo" ganhou os dois pares de segredo de serviço, com o sintoma de cada um, e a linha da `Auth:TokenSigningKey` diz que ela só valida;
    - `ServiceClient__Secret` nas tabelas do `apps/inbox` e do `apps/connectors`;
    - as duas variáveis do `.env.prod`;
    - a frase "o ciclo assina `service:connectors`" foi reescrita.
- [x] 6.5 (docs) `docs/development.md`: o segredo de cada serviço precisa ser igual no serviço e no `apps/api`. Com o `apps/api` parado, as chamadas falham no pedido de token, com a espera de 10 s.
  - **Feito:** no `apps/inbox`, o `ServiceClient:Secret` precisa ser igual ao do `apps/api`, e com o `apps/api` parado as chamadas falham no pedido de token, com a espera de 10 s. No `apps/connectors`, o `ServiceClient:Secret` entra na lista do que é lido.
- [x] 6.6 (docs) `docs/deployment.md`, seção do passo B:
  - ordem livre;
  - conferência pós-deploy: a linha `Information` do primeiro token no `inbox` e no `connectors`, nenhum `invalid_client`, mensagem de canal ao agente, "Sincronizar agora", cadastro de base sincronizada;
  - rollback: voltar a imagem.
  - **Feito:** a seção "Serviços obtêm o token no emissor (#235)", antes da da #234, com:
    - ordem livre;
    - os greps da conferência (a linha do primeiro token no `inbox` e no `connectors`, nenhum `invalid_client`, a linha do `api`);
    - os três fluxos;
    - uma tabela de sintomas;
    - o rollback.

    A tabela de variáveis diz que cada segredo vai ao `api` e ao serviço dele.

## 7. Fechamento

- [x] 7.1 (apps/api, apps/inbox, apps/connectors) Stack local, com `TZ=America/Sao_Paulo` e `DOTNET_SYSTEM_NET_DISABLEIPV6=1`. Sobem o `apps/api` (Postgres descartável migrado), o `apps/inbox` e o `apps/connectors` por `dotnet run`, com a configuração de desenvolvimento. Registrar em `235-verificacao/manual/`:
  - a linha `Information` do primeiro token no inbox e no connectors, e a do issuer no `apps/api`;
  - um cadastro de canal (S1);
  - "Sincronizar agora" (S3);
  - o `apps/api` parado: o inbox loga a falha com a espera de 10 s, sem o segredo.
  - **Feito** em `235-verificacao/manual/` (`RESUMO.md`, os quatro logs, `conferencia*.txt`; script `~/.cache/buteco-agents/bin/conferencia-235.sh`). O stack teve Postgres e RabbitMQ descartáveis nas portas de dev, os quatro apps por `dotnet run` com a configuração de desenvolvimento, o `apps/connectors` com a chave do Drive de validação e o `apps/workers` com o gateway de LLM local.
    - **Linhas do primeiro token:**
      - no `connectors`, na primeira rodada do ciclo, logo depois do boot;
      - no `inbox`, na primeira validação de agente;
      - no `api`, "gerado no processo", no cadastro da base.

      Nenhum `invalid_client`, e nenhum `401`/`403` nas chamadas de saída do inbox.
    - **S1:** canal WAHA com agente existente → `201`; com agente inexistente → `400`. O `AgentReferenceValidator` recebeu `200` e `404` do `apps/api`.
    - **S2:** webhook → `200`; A2A → `200`; task criada no `apps/api` para o agente, e o worker a executou. O LLM respondeu `404 model_not_found` (o gateway local não tem o `gpt-5.6-sol`, e o catálogo do `apps/api` só aceita os modelos fixos), então a task terminou `Failed`. O push de volta falhou porque o `PublicUrl:BaseUrl` de desenvolvimento do inbox aponta para um túnel antigo. Nenhum dos dois tem relação com a change.
    - **S4:** base sincronizada na pasta de validação → `201`.
    - **S3:** "Sincronizar agora" → `202`; ciclo concluído sem erro, com 5 documentos gravados e 3 ignorados.
    - **Com o `apps/api` parado:** não conferido no stack local. É coberto pelos casos de espera, `503` e timeout dos dois sources (3.4, 4.2) e pelo caso divergente de `tests/` (5.4).
- [x] 7.2 (apps/api, apps/inbox, apps/connectors, tests/) Suítes pelo `suite-235.sh`: `api`, `inbox`, `connectors`, `rt-inbox`, `rt-connectors` e `rt-crossapp`. Comparação por nome contra a `235-baseline/r1`, com o GUID normalizado. Cada ausente, renomeado ou mudado aparece com o motivo, e as renomeações são as declaradas no D6. Uma reprovação do `Source3_…` é comparada com a `r1` da baseline antes de ser atribuída à change.
  - **`r1`** (`235-fechamento/r1`): a suíte do `apps/api` reprovou 3 casos de `IdentityIssuerStartupValidationTests` (`RealComposition_WithoutPublicUrl_FailsNamingTheSetting` ×2, `RealComposition_WithPublicUrlWithoutScheme_FailsNamingTheSetting`), com "Some services are not able to be constructed" no lugar da mensagem que nomeia a `PublicUrl:BaseUrl`. A rodada foi inconclusiva pela CPU (mínimo 55), mas o defeito é determinístico e meu: o `ServiceAccessTokenIssuer` era registrado dentro do `AddIdentityIssuer`, que sai cedo sem url pública, e o handler ficava sem dependência na validação do DI. **Corrigido:** o registro foi para o `Program.cs`, ao lado do handler. Os 30 casos afetados passaram, e as outras cinco suítes da `r1` passaram.
  - **`r2`** (`235-fechamento/r2`, código final): as seis aprovadas. A comparação por nome está no `02`:
    - `api` 1.277 + 1 ignorado: 2 ausentes declarados no D6 e 8 novos;
    - `connectors` 337 + 1 ignorado: 1 ausente declarado no D6 e 21 novos;
    - `inbox` 322: 20 novos;
    - `rt-inbox` 15: os 12 presentes e 3 novos;
    - `rt-connectors` 10: 1 renomeado (5.7) e 3 novos;
    - `rt-crossapp` 4: iguais.

    O `Source3_…` (#176) passou nas duas rodadas do fechamento.
- [x] 7.3 (todos) Medição da convenção 18: produção, testes e duplos medidos contra a projeção do design ("Tamanho"), cada coluna com a própria comparação.
  - **Feito** (tabela no `02`):
    - produção +1.057 / −82, contra ~670 projetado (+58%): os dois sources e as checagens de boot;
    - testes +1.525 / −126, contra ~1.650 (−8%);
    - duplos e suporte +385 / −20, contra ~340 (+13%).
- [x] 7.4 (docs) `02-HISTORICO_E_STATUS.md`:
  - o que entrou;
  - **o caminho do H6 (1.4)**;
  - as decisões do dono;
  - as correções de implementação (convenção 9);
  - o que a implementação mediu;
  - o `TokenService.Issue` sem chamador de produção até a #182 (D7).

  `CHANGELOG.md`.
  - **Feito:** entrada `servicos-token-emissor` no `02`, com o que entrou, o caminho do H6 (o principal, pela `IOpenIddictServerFactory`), as decisões do dono, as correções (convenção 9), as suítes, a convenção 18, a conferência local e "Para as próximas". O `CHANGELOG` ganhou o item da #235.
- [x] 7.5 (docs) `01-ARQUITETURA_E_CONVENCOES.md`, seção Autenticação: os serviços obtêm o token no emissor, e o `apps/api` gera o seu no processo. O HMAC continua aceito até a #182.
  - **Feito:**
    - o parágrafo do HMAC diz que, desde a #235, nenhum serviço assina HMAC, com o texto anterior registrado em itálico;
    - o item "Tokens de serviço" descreve o source dos dois serviços (endereço interno, cache, pedido único, falha como "`apps/api` indisponível", espera de 10 s), o gerador estreito do `apps/api` e o `401` sem repetição;
    - a tabela de subjects por rota continua como estava.
- [x] 7.6 (openspec) `openspec validate servicos-token-emissor --strict` e o archive com sync das specs, depois de o 02 e o CHANGELOG estarem escritos (7.4). Commit, push e PR só com o pedido do dono.
  - **Feito:** `openspec validate --strict` limpo, e archive com sync das specs depois do `02` e do `CHANGELOG`. Commit, push e PR pedidos pelo dono em 03/11/2026.
