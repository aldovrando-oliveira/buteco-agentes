## Context

**Estado atual (`05aae2f`, merge do PR #237, 03/11/2026).**

- **Receptores prontos (#234).** O catálogo do emissor mantém os clientes confidenciais `buteco-servico-inbox` e `buteco-servico-connectors`: só `client_credentials`, só o endpoint de token, só o escopo `buteco-api`. Os segredos ficam em `Identity:ServiceClients:{Inbox,Connectors}:Secret`. As tabelas de clientes mapeiam:
  - no `apps/api`, os dois clientes para `service:inbox` e `service:connectors`;
  - no `apps/connectors`, `buteco-servico-api` para `service:api`.
- **Emissores ainda HMAC.** Os três `ServiceTokenDelegatingHandler` assinam um token novo a cada requisição, com a `Auth:TokenSigningKey` e 5 min de validade:
  - `apps/inbox/.../Auth/ServiceTokenDelegatingHandler.cs:23`, nos clientes `AgentReferenceValidator` (S1) e `A2AClientFactory` (S2), em `Program.cs:86-101`;
  - `apps/connectors/.../Auth/ServiceTokenDelegatingHandler.cs:30`, no cliente `SyncApiClient` (S3), em `Sync/SyncRegistrationExtensions.cs:20-31`;
  - `apps/api/.../Auth/ServiceTokenDelegatingHandler.cs:29`, no cliente `ConnectorsFolderClient` (S4), em `Program.cs:109-120`.
- **Deploy.** `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS` já estão no `.env.prod` e, com `:?`, no serviço `api` do compose. O `inbox` e o `connectors` ainda não os recebem (Non-Goal da #234).

**O desenho completo** é o design da #182 (change `remover-hmac-token-servico`), preservado [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248), sobre a base `14d4ba9`. Aqui ele é o "design de referência", e as decisões Q1–Q6 dele são do dono, de 02/11/2026. Esta change implementa:

- **D3:** o `ServiceAccessTokenSource`;
- **D4:** o `ServiceAccessTokenIssuer`, estreito;
- **D7:** só o round trip de `tests/`;
- **D8:** o deploy do passo B;
- **o passo B de "Tamanho e divisão"**.

O inventário S1–S4 e os achados H1–H6 de lá valem aqui e não são repetidos. O design arquivado da #234 (`openspec/changes/archive/2026-11-03-clientes-servico-emissor/design.md`) traz os clientes, os segredos e o isolamento do catálogo. O registro da #234 no `02` diz que o segredo em base64 com `+`, mandado sem codificação, chega como espaço e recebe `invalid_client`.

### Achados novos (fonte do OpenIddict 7.7.2, tag `950cb13`, e código em `05aae2f`, lidos em 03/11/2026)

| # | fato | onde |
|---|---|---|
| H9 | A geração **não** monta o principal. O `GenerateTokenContext` só assina o que recebe. `iss`, `aud`, `client_id`, `iat`, `exp` e `scope` vêm dos claims privados do principal (`oi_iss`, `oi_aud`, `oi_crt_dt`, `oi_exp_dt`, `oi_scp`). Quem os põe no `/connect/token` é o `PrepareAccessTokenPrincipal`, no sign-in, e não a geração. O `typ` `at+jwt` sai do `TokenType`, o `jti` é sorteado na geração, e a credencial é sempre `Options.SigningCredentials.First()`. | `OpenIddict.Server/OpenIddictServerHandlers.Protection.cs` l. 1450–1490 (credencial), 1583–1740 (`AttachTokenSubject`, `AttachTokenMetadata`); `OpenIddictServerHandlers.cs` l. 3676–3735 (`PrepareAccessTokenPrincipal`), 4840–4890 (`GenerateAccessToken`) |
| H10 | O `aud` do token de `client_credentials` sai dos **recursos dos escopos pedidos**. Um pedido sem `scope` gera token sem `aud`, que o destino recusa com `401`. O pedido do serviço precisa levar `scope=buteco-api`. | `OpenIddictServerHandlers.cs` l. 3720 (`SetAudiences(GetResources())`); `ClientCredentialsTokenHandler` do `apps/api` (#179 D2) |
| H11 | O `TokenEntryStorageHandler` do `apps/api` já desliga a linha em `openiddict_tokens` para todo access token, na geração. Vale também para o token gerado no processo, sem código novo. | `apps/api/.../Identity/Storage/TokenEntryStorageHandler.cs:48-57` |
| H12 | As chaves do anel chegam às opções do OpenIddict pelo `IdentityKeyRingOptionsConfigurer`, que só lê o snapshot. Antes da primeira carga, a montagem das opções lança `ID0085`. Quem impede isso nas requisições é o `IdentityKeyRingGate`, que responde `503` antes de qualquer rota. | `Identity/Keys/IdentityKeyRingOptionsConfigurer.cs:20-43`, `IdentityKeyRingGate.cs` |
| H13 | Os três chamadores já tratam a `HttpRequestException` da pilha do `HttpClient` como "destino indisponível": o `AgentReferenceValidator` (`CommunicationFailure`), o despacho A2A (`DebounceSweepService.IsTransportFailure`, que repete) e o `SyncApiClient` (`Unreachable`). Uma exceção lançada de dentro do `DelegatingHandler` chega a eles pelo mesmo caminho de uma conexão recusada. | `Agents/AgentReferenceValidator.cs:36-45`; `Orchestration/DebounceSweepService.cs:221, 396-404`; `Sync/SyncApiClient.cs:84-91` |

O H9 precisa do H6 do design de referência ("o que o principal precisa trazer pronto", que ficou sem verificação). O emissor no processo monta o principal como o `PrepareAccessTokenPrincipal` monta, e o teste de contrato do D1 é o guarda disso.

## Goals / Non-Goals

**Goals:**

- Nenhum serviço manda HMAC. O `apps/inbox` e o `apps/connectors` mandam o token do emissor, obtido com o próprio segredo, e o `apps/api` manda o que ele mesmo gera.
- O token gerado no processo é indistinguível, no contrato, de um token do `/connect/token`. Isso é provado antes de qualquer código depender dele (H6).
- Com o emissor fora do ar ou o segredo divergente, o chamador vê o `apps/api` indisponível, como hoje vê uma conexão recusada, e uma rajada não vira um timeout por chamada.
- O `main` continua implantável em qualquer ordem, porque os receptores aceitam os dois formatos desde a #234.

**Non-Goals:**

- **Fixtures** (#236): as fixtures dos três apps continuam anexando o HMAC `operator`. Esta change só toca fixture para o que a troca de emissor exige: segredo de teste e um `/connect/token` falso padrão (D6).
- **Remoção** (#182): o HMAC, o seletor, a `Auth:TokenSigningKey`, o `TokenService` e a checagem de boot da chave no `apps/connectors` continuam (D7).
- **O caso `OperatorHmacToken_SignedWithTheSharedKey_IsAcceptedByInboxWithoutNetworkCallToApi`** continua até a #182.
- **`apps/workers` e `apps/frontend`.**

## Decisions

### D1. Primeira tarefa: o H6, verificado antes de qualquer código depender dele

**O que se prova:** um token gerado no processo, pela `IOpenIddictServerFactory`, e um token do `/connect/token` real precisam ter:

- **o mesmo cabeçalho:** `typ` `at+jwt`, `alg`, e `kid` igual ao da credencial que assina, que é a primeira do anel (H9);
- **os mesmos claims de contrato:**
  - `iss` igual à url pública;
  - `client_id` e `sub`;
  - `aud` com um valor só;
  - `exp` 15 min depois do `iat`;
- **o mesmo conjunto de nomes de claims**, além dos valores acima. O guarda pega um claim a mais ou a menos que a lista não preveja, por exemplo um `oi_*` vazado ou o `scope` ausente (Q3).

O `JwtBearer` do `apps/connectors` também precisa aceitar o token gerado no processo.

**Onde.** São dois testes, porque nenhum projeto de teste de app referencia outro app:

- **`apps/api`, `ServiceAccessTokenIssuerTests`:**
  - o formato contra um token do `/connect/token` do `buteco-servico-inbox`, pedido pelo helper `ServiceClientTokens` da #234;
  - o `TestServer` com o anel carregado.
- **`tests/ApiConnectorsRoundTrip.Tests`:**
  - o `apps/connectors` real aceita o token na rota "descrever pasta";
  - recusa o mesmo token nas rotas do operador (`403`).

**O caminho principal:**

1. Abrir um escopo e chamar `IOpenIddictServerFactory.CreateTransactionAsync()`.
2. Despachar um `GenerateTokenContext`:
   - `TokenType` access token;
   - `TokenFormat` JWT;
   - `CreateTokenEntry = false`, que o H11 já garante.
3. O principal traz o que o `PrepareAccessTokenPrincipal` poria:
   - `sub` e `client_id` = `buteco-servico-api`;
   - `oi_aud` = `buteco-connectors`;
   - `oi_scp` = `buteco-connectors` (Q3);
   - `oi_iss` = `Options.Issuer`;
   - `oi_crt_dt` = agora, pelo `TimeProvider` das opções;
   - `oi_exp_dt` = agora + 15 min.

   **Correção na implementação (03/11/2026, tarefa 1.2; convenção 9).** A lista acima não tinha o presenter. O token do `/connect/token` sai com `oi_prst` igual ao `client_id`, que o `AttachDefaultPresenters` põe no sign-in e o `PrepareAccessTokenPrincipal` sempre mantém. O guarda do conjunto de nomes reprovou na primeira rodada por isso, e o principal passou a levar `oi_prst` = `buteco-servico-api`. **Caminho que entrou: o principal, pela `IOpenIddictServerFactory`; o H6 vale (tarefa 1.4).**

**Se o H6 cair** (a geração exige o `HttpContext`, ou o pipeline recusa o contexto fora de uma requisição), entra a alternativa sem parada: o `JsonWebTokenHandler` com a credencial ativa do `IdentityKeyRing.Snapshot`. Os dois testes acima guardam o formato nos dois caminhos, e a troca muda só a implementação. **O caminho que entrou é registrado no `02` e informado no fechamento.**

A tarefa termina com o teste de formato verde e visto reprovar contra dois defeitos, um principal sem `oi_iss` e um sem `oi_scp` (convenção 15). Só então o `ServiceTokenDelegatingHandler` do `apps/api` passa a depender do emissor.

### D2. `ServiceAccessTokenIssuer` no `apps/api` (referência D4)

- **Estreito por construção** (decisão do dono, 02/11/2026). A interface pública é `ValueTask<string> GetAsync(CancellationToken)` mais `Invalidate()`, usada pelo `401` (abaixo). O `client_id` `buteco-servico-api`, a `aud` `buteco-connectors` e os 15 min são constantes privadas.
- **Teste de superfície:** por reflexão, nenhum método público tem parâmetro além do `CancellationToken`. O guarda é visto reprovar contra um `GetAsync(string audience, …)`.
- **Cache:** o token serve até faltar 1 min para o `exp`, pelo `TimeProvider`.
- **Sem pedido único.** A geração é em memória e idempotente: duas gerações simultâneas custam duas assinaturas RSA e nenhum efeito, e um semáforo seria código para um custo que não existe. A margem e o `401` seguem o D3, para os três emissores se comportarem igual diante do destino.
- **Sem espera de falha.** O gate do anel (H12) responde `503` antes da rota que chama o `apps/connectors`, então quando a chamada sai o anel já carregou. Envolver o `ID0085` numa `HttpRequestException` seria código inalcançável (convenção 25).
- **Singleton.** Cada geração abre um escopo pelo `IServiceScopeFactory`, porque a `IOpenIddictServerFactory` e o dispatcher são registrados com escopo.
- **Log `Information`** no primeiro token gerado e depois de cada `401`, com o `client_id` e a validade, no mesmo formato do D3. Custa uma linha e deixa os três emissores visíveis da mesma forma no log.
- **`buteco-servico-api` continua fora do catálogo** (#234 D1).

### D3. `ServiceAccessTokenSource` no `apps/inbox` e no `apps/connectors` (referência D3)

O que o design de referência fixou (endereço interno, `HttpClient` nomeado `IssuerToken` sem o `DelegatingHandler`, cache com 1 min de margem, `SemaphoreSlim(1)` com reconferência, espera de 10 s, `401` sem repetição, linha `Information`, `ServiceAccessTokenSchedule`) vale como está. Este design acrescenta:

- **O pedido** é `FormUrlEncodedContent` com:
  - `grant_type=client_credentials`;
  - `client_id`, constante do código: `buteco-servico-inbox` ou `buteco-servico-connectors`;
  - `client_secret`, de `ServiceClient:Secret`;
  - **`scope=buteco-api`**, sem o qual o token sai sem `aud` (H10).

  Nunca vai string montada à mão (o achado do `+` da #234). O teste do duplo usa um segredo com `+`, `/` e `=` e confere o valor decodificado no servidor. O acordo de `tests/` usa um segredo de teste com `+` contra o emissor real.
- **A resposta.** Só `access_token` e `expires_in` são lidos. Sem `expires_in` válido, ou sem `access_token`, a resposta é falha, como um `503`. O cache é contado pelo `expires_in`, sem decodificar o JWT: o serviço não confere o token que recebe, e quem confere é o destino.
- **Toda falha vira `HttpRequestException`**, com a mensagem curta do motivo (`503`, `invalid_client`, `timeout`, `resposta sem access_token`):
  - o timeout de 5 s do `IssuerToken` também, com um `CancellationTokenSource` próprio ligado ao do chamador, para o cancelamento do chamador continuar `OperationCanceledException`;
  - os três chamadores já tratam a exceção como destino indisponível (H13), e nenhum muda.
- **O `invalid_client` é `Error`**; os outros motivos são `Warning`. A linha leva o `client_id` e o motivo, sem o segredo nem o `Api:BaseUrl`.
- **`401` do destino:** o handler chama `Invalidate()` só se o token anexado ainda for o do cache. Um `401` atrasado não derruba um token novo.
- **Registro:**
  - **no `apps/inbox`:** o source e o `IssuerToken` entram sempre, porque o `Api:BaseUrl` é obrigatório lá;
  - **no `apps/connectors`:** entram dentro do `AddSyncCycle`, junto do `SyncApiClient`, só com `Api:BaseUrl`.
- **Duas cópias, sem `libs/`** (referência D10): dois consumidores, cada um provado contra o emissor real em `tests/`. O comentário de cabeça de cada cópia aponta a outra, como o `IssuerKeysRetriever`.

### D4. Os três `ServiceTokenDelegatingHandler`

- Cada handler passa a receber o source (inbox, connectors) ou o issuer (api), e não o `ITokenService`. Anexa `Bearer <token>`, manda, e no `401` invalida o cache, conforme o D3.
- **Saem as constantes `ServiceSubject`, `ConnectorsSubject` e `ApiSubject` dos handlers.** O handler deixa de conhecer subject: o subject sai do `client_id`, pela tabela do destino. As quatro referências de teste passam às constantes do receptor:
  - `ServiceScopeAuthorizationHandler.InboxSubject` e `ConnectorsSubject`, no `apps/api`;
  - `ConnectorsSubjectAuthorizationHandler.ApiSubject`, no `apps/connectors`;
  - o literal `service:inbox`, no teste do `apps/inbox`, que não tem constante de receptor para ele.

  Sem isso, ficariam três constantes sem leitor de produção (D7).

### D5. Configuração e boot nos serviços

- **`ServiceClient:Secret`.** É o mesmo valor do `Identity:ServiceClients:{Inbox,Connectors}:Secret` do `apps/api`. Em `ServiceClientOptions` (`SectionName = "ServiceClient"`). O `client_id` é constante, e não opção (convenção 2).
- **Boot, depois do `Build()`** (convenção 8, forma de configuração, sem I/O). O segredo precisa:
  - estar presente;
  - ser base64 válido;
  - ter pelo menos 32 bytes decodificado;
  - no `apps/inbox`, ser distinto, por bytes, da `Inbox:CredentialEncryptionKey` (Q2).

  A mensagem nomeia `ServiceClient:Secret` e nunca ecoa o valor.
  - **`apps/inbox`:** sempre.
  - **`apps/connectors`:** só com `Api:BaseUrl` presente, depois do `ValidateApiConfiguration`, que já decide se o ciclo existe. Sem `Api:BaseUrl`, o processo sobe sem segredo, como hoje sobe sem ciclo.
- **Segredo divergente** não é pego no boot, porque não há I/O ali. Aparece no primeiro pedido como `invalid_client` (`Error`), e a conferência pós-deploy procura essa linha (D8).
- **`appsettings.Development.json`:** cada serviço recebe o mesmo segredo de desenvolvimento que o do `apps/api` para ele, marcado como de desenvolvimento. O `dotnet run` dos três continua funcionando sem nada novo.

### D6. Fixtures: só o que a troca de emissor exige

- **`apps/inbox`:**
  - o `TestAuthentication.ConfigOverrides` ganha o `ServiceClient:Secret` de teste, porque o boot passa a exigi-lo;
  - a `InboxFactoryFixture`, que já troca o handler primário do `AgentReferenceValidator`, troca também o do `IssuerToken` por um `FakeTokenEndpoint` que responde sempre um token opaco. Sem isso, o S1 dos testes de canal que usam o `AgentApiHandler` (`ChannelEndpointsTests`, `SubjectAuthorizationTests`) iria à rede no pedido de token.

  O `OrchestrationFactoryFixture` usa o `FakeA2AClientFactory` e não passa pelo handler, então não muda.
- **`apps/connectors`:** a `ConnectorsFactory` faz o mesmo quando configura `Api:BaseUrl`, que é quando o `SyncApi` falso existe.
- **`apps/api`:** nenhuma fixture muda. O `ConnectorsFolderClient` das fixtures passa a anexar o token do issuer real, porque o anel já carrega nelas.
- **Asserções existentes que mudam com o requisito**, e não por premissa falsa. É a mudança das specs desta change, declarada:

| teste | hoje afirma | passa a afirmar |
|---|---|---|
| `apps/api` `ServiceTokenDelegatingHandlerTests` (2 casos) | HMAC `service:api` de 5 min; um token por requisição | o token do issuer (`client_id`, `aud`); duas requisições, uma geração; `401` invalida |
| `apps/connectors` `ServiceTokenDelegatingHandlerTests` (1 caso) | HMAC `service:connectors` de 5 min por requisição | o token do source é anexado; `401` invalida sem repetir |
| `apps/api` `KnowledgeBaseCatalogTests.CreateSynced_SendsServiceApiTokenToConnectors` | o `ITokenService` valida o token enviado, `sub` `service:api` | o token enviado é JWT do emissor com `client_id` `buteco-servico-api` e `aud` `buteco-connectors` |
| `rt-connectors` `SyncCycle_TokenOfTheConnectors_IsAcceptedByTheRealApi` | o `ITokenService` do `apps/api` valida o token, `sub` `service:connectors` | JWT com `client_id` `buteco-servico-connectors` e `aud` `buteco-api`; nenhuma chamada `401`/`403` |
| `rt-connectors` `CreateSynced_ConnectorsWithADifferentSigningKey_Is502ConnectorsError401` | chave HMAC divergente → `502` `connectors-error` `401` | a mesma resposta, com o `apps/connectors` obtendo as chaves de outro emissor que anuncia o mesmo issuer (Q4, revista na parada da 5.7); o caso passa a `CreateSynced_ConnectorsWithKeysFromAnotherIssuer_Is502ConnectorsError401` |

Nenhum caso sai. A comparação por nome no fechamento lista as renomeações.

### D7. O HMAC continua aceito; o que fica sem uso de produção

**Continua em uso:**

- **A validação HMAC** (`TokenService.Validate`, `OperatorTokenAuthenticationHandler`, o seletor por forma) nos três apps: as fixtures (#236) e o `OperatorHmacToken_…` mandam HMAC `operator`;
- **a `Auth:TokenSigningKey`** nos três apps e no compose;
- **a checagem de boot da chave no `apps/connectors`** (`Program.cs:12-16`).

**Sem chamador de produção depois desta change:**

| peça | onde | quem ainda usa | sai em |
|---|---|---|---|
| `ITokenService.Issue` e `TokenService.Issue` | os três apps | só testes: `TestAuthentication.AttachOperatorToken`, `CreateClientAs`, `RoundTripFixture.OperatorClient` e `OperatorHmacToken`, e os testes de HMAC explícito | #182 (passo D), com o `TokenService` inteiro; as fixtures deixam de usá-lo na #236 |

O `Issue` já era previsto assim pelo passo B do design de referência ("nenhum serviço manda HMAC; o HMAC continua aceito"). Tirá-lo agora quebraria as fixtures que a #236 migra. **Não há código morto novo além dele:** as três constantes de subject dos handlers saem aqui (D4), e nenhum outro membro perde o último leitor (grep de `ServiceTokenDelegatingHandler.` em `apps/` e `tests/`, em `05aae2f`).

### D8. Deploy (referência D8, passo B)

**`docker-compose.prod.yml`:**

- **`inbox`:** `ServiceClient__Secret: ${SERVICE_CLIENT_SECRET_INBOX:?defina SERVICE_CLIENT_SECRET_INBOX — o mesmo do serviço api — use --env-file .env.prod}`.
- **`connectors`:** `ServiceClient__Secret: ${SERVICE_CLIENT_SECRET_CONNECTORS:?…}`, com `:?` porque o compose sempre passa `Api__BaseUrl`.
- **`Auth__TokenSigningKey`** continua nos três. O comentário do `connectors` ("Terceiro processo com a chave") passa a dizer que ele só valida HMAC.
- **Sem `depends_on` novo:** o `inbox` não depende do `api` hoje, e a espera de 10 s do D3 cobre o `api` subindo depois.

**`.env.prod.example`:** sem variável nova. O comentário dos dois segredos passa a dizer que cada um vai ao `api` **e** ao serviço dele.

**Ordem:** qualquer uma. Os receptores aceitam HMAC e token do emissor desde a #234.

**Conferência pós-deploy** (critério de aceite da issue):

- no log do `inbox` e do `connectors`, a linha `Information` do primeiro token obtido, e nenhum `invalid_client`;
- uma mensagem de canal chega ao agente (S1 no cadastro do canal, S2 no despacho);
- "Sincronizar agora" (S3);
- cadastrar uma base sincronizada (S4).

**Rollback:** voltar a imagem. A anterior manda HMAC, que continua aceito, e ignora `ServiceClient__Secret`.

### D9. Testes

Cada guarda é visto reprovar contra o defeito entre parênteses (convenção 15): o defeito é reintroduzido, o teste reprova, e a árvore volta.

**`apps/inbox` e `apps/connectors`, `ServiceAccessTokenSourceTests`** (unidade, sem contêiner). Usam o `FakeTokenEndpoint` como handler primário do `IssuerToken`. Ele conta pedidos, lê o formulário, segura a resposta até o teste soltar, responde `503`, `invalid_client` ou o que o teste mandar, e fica sem responder. Os testes usam também `FakeTimeProvider` e um `ServiceAccessTokenSchedule` curto. A mesma lista vale nos dois apps:

| caso | afirma | guarda contra |
|---|---|---|
| cache | duas chamadas, um pedido | source sem cache |
| margem de 1 min | com `expires_in` 900, um pedido em t+839 s reaproveita e um em t+841 s pede de novo | margem zero (usar até o `exp`) |
| pedido único | 10 chamadas simultâneas com o pedido segurado fazem 1 pedido, e as 10 recebem o mesmo token | sem o semáforo; semáforo sem reconferir o cache depois de entrar |
| formulário | `grant_type`, `client_id` constante, `scope=buteco-api`, e o segredo com `+/=` chega idêntico | corpo montado à mão sem codificar; pedido sem `scope` |
| `503` | `HttpRequestException`; `Warning` com `client_id` e motivo | devolver `null` ou lançar `InvalidOperationException`, que os chamadores não tratam |
| `invalid_client` | `HttpRequestException`; `Error` com `client_id`; nenhuma linha contém o segredo nem o `Api:BaseUrl` | linha que interpola o segredo; `Warning` no lugar de `Error` |
| timeout | o duplo não responde, e a falha sai no timeout do schedule como `HttpRequestException`; o cancelamento do chamador continua `OperationCanceledException` | sem timeout próprio (o default de 100 s do `HttpClient`); timeout que vira `TaskCanceledException` sem envolver |
| espera de 10 s | depois de uma falha, chamadas dentro da espera falham sem pedido; depois dela, pedem | sem a espera |
| recuperação | a linha `Information` sai no primeiro token e depois da primeira obtenção que segue uma falha, e não a cada token | log a cada obtenção, ou só no primeiro |
| resposta sem `access_token` ou sem `expires_in` | falha como `503` | cache com validade zero ou infinita |

**Handler (`ServiceTokenDelegatingHandlerTests`, nos três apps).** O `apps/inbox` ganha o arquivo; o do api e o do connectors são reescritos (D6).

| caso | afirma | guarda contra |
|---|---|---|
| anexa | `Authorization: Bearer <token do source>` | handler que ainda anexa HMAC |
| `401` do destino | uma chamada ao destino só (sem repetição); a próxima pede token novo | repetir dentro do handler; não invalidar |
| `401` atrasado | um `401` de um token que já não é o do cache não invalida o novo | invalidar sem comparar |

**Boot:**

- **`apps/inbox`, `ServiceClientConfigurationTests`:** ausente, fora de base64 e curto derrubam o boot nomeando a chave, sem o valor na mensagem. Igual à `Inbox:CredentialEncryptionKey` também derruba (Q2). Inclui a composição real com `Program.cs`. Guarda contra a remoção da chamada no `Program.cs` e contra a mensagem que interpola o valor.
- **`apps/connectors`:** os mesmos casos com `Api:BaseUrl`. Sem `Api:BaseUrl`, sobe sem segredo. Guarda contra a checagem incondicional.

**`apps/api`:**

| teste | afirma | guarda contra |
|---|---|---|
| `ServiceAccessTokenIssuerTests`: formato (D1) | cabeçalho, claims de contrato e conjunto de nomes iguais aos do `/connect/token` | principal sem `oi_iss`; sem `oi_scp` |
| `ServiceAccessTokenIssuerTests`: superfície | nenhum método público com parâmetro além do `CancellationToken`; `client_id`, `aud` e 15 min exatos | `GetAsync(string audience, …)` |
| `ServiceAccessTokenIssuerTests`: cache | duas chamadas, uma geração; depois da margem, outra; `Invalidate()` força outra | sem cache; margem zero |
| `ServiceAccessTokenIssuerTests`: sem linha | nenhuma linha nova em `openiddict_tokens` (H11) | `CreateTokenEntry = true` |
| `KnowledgeBaseCatalogTests.CreateSynced_SendsServiceApiTokenToConnectors` (D6) | o token enviado é o do emissor | handler que ainda anexa HMAC |

**`tests/`: acordos dos quatro fluxos com o emissor real** (referência D7; acordo de fio, convenção 11).

- **`InboxOrchestratorRoundTrip.Tests`.** A `RoundTripFixture` liga o `IssuerToken` e o `AgentReferenceValidator` do `apps/inbox` ao `TestServer` do `apps/api`, com o `ServiceClient:Secret` igual ao do catálogo. Os segredos de teste das duas pontas trocam por valores com `+`. Antes do primeiro pedido, a fixture espera o catálogo sincronizar os clientes de serviço, porque o pedido antes disso recebe `invalid_client` e abre a espera de 10 s.
  - **S2 (novo caso):** uma mensagem de canal chega ao agente. Todas as chamadas do inbox ao `apps/api` levam JWT com `client_id` `buteco-servico-inbox` e `aud` `buteco-api`, nenhuma recebe `401`/`403`, e há exatamente um `POST /connect/token`. Os 12 casos de hoje passam pelo mesmo caminho sem mudar.
  - **S1 (novo caso):** o cadastro de canal no `apps/inbox`, autenticado com o token real de usuário da fixture, valida o `AgentId` no `apps/api` real. Agente existente → criado; agente inexistente → a recusa de hoje.
  - **Segredo divergente (novo caso):** o `apps/inbox` com outro segredo. O `/connect/token` responde `invalid_client`. O cadastro de canal responde a falha de comunicação de hoje, e o despacho segue o caminho de "`apps/api` indisponível". Nenhuma chamada leva HMAC.

    **Correção na implementação (03/11/2026, tarefa 5.4; convenção 9).** A primeira versão dizia que a linha "continua pendente e é tentada de novo, e não vira falha terminal". O caminho de transporte devolve a linha a pendente só até `Debounce:MaxDispatchAttempts`, e depois a encerra como perda, como hoje com o `apps/api` fora. O caso afirma esse caminho: um aviso de transporte por tentativa e a perda só depois da última, nunca a rejeição de protocolo A2A. O comportamento não muda; o cenário da spec foi corrigido no mesmo sentido.
  - **`OperatorHmacToken_…`** continua até a #182.
- **`ApiConnectorsRoundTrip.Tests`.** A `RoundTripFixture` liga o `IssuerToken` do `apps/connectors` ao `TestServer` do `apps/api`, com o segredo do catálogo.
  - **S3:** o `SyncCycle_TokenOfTheConnectors_…` muda a asserção (D6). O caso novo de **segredo divergente**: `invalid_client`, o ciclo fica `Unreachable`, e nenhum upsert chega ao `apps/api`.
  - **S4:** os três `CreateSynced_*` passam pelo issuer real. O caso novo de **aceitação do H6**: o `apps/connectors` real aceita o token gerado no processo na "descrever pasta" e o recusa nas rotas do operador. O caso de chave divergente muda de causa (D6, Q4).
- **`CrossAppTaskStoreCompatibility.Tests`:** não muda.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Auth/ServiceTokenDelegatingHandler.cs        reescrito: token do ServiceAccessTokenIssuer, 401 invalida (D4)
├── Identity/ServiceTokens/
│   └── ServiceAccessTokenIssuer.cs              NOVO: geração no processo, estreito, cache (D1, D2)
└── Program.cs                                   registra o issuer; handler sem ITokenService

apps/inbox/src/Buteco.Inbox/
├── Auth/
│   ├── ServiceClientOptions.cs                  NOVO: ServiceClient:Secret; client_id constante (D5)
│   ├── ServiceClientConfigurationValidation.cs  NOVO: boot do segredo (D5)
│   ├── ServiceAccessTokenSource.cs              NOVO: cache, pedido único, espera, falha (D3)
│   ├── ServiceAccessTokenSchedule.cs            NOVO: margem 1 min, espera 10 s, timeout 5 s
│   └── ServiceTokenDelegatingHandler.cs         reescrito (D4)
├── Program.cs                                   IssuerToken, source, checagem de boot
└── appsettings.Development.json                 + ServiceClient:Secret de desenvolvimento

apps/connectors/src/Buteco.Connectors/
├── Auth/                                        os mesmos cinco arquivos do apps/inbox, cópia (referência D10)
├── Sync/SyncRegistrationExtensions.cs           IssuerToken e source dentro do AddSyncCycle (D3)
├── Program.cs                                   checagem do segredo com Api:BaseUrl (D5)
└── appsettings.Development.json                 + ServiceClient:Secret de desenvolvimento

apps/api/tests/Buteco.Api.Tests/
├── Identity/ServiceAccessTokenIssuerTests.cs    NOVO (D1, D9)
├── ServiceTokenDelegatingHandlerTests.cs        reescrito (D6)
├── ServiceScopeAuthorizationTests.cs            constante do receptor no lugar da do handler (D4)
└── Knowledge/KnowledgeBaseCatalogTests.SyncedCreation.cs   asserção do token (D6)

apps/inbox/tests/Buteco.Inbox.Tests/
├── Support/FakeTokenEndpoint.cs                 NOVO: duplo do /connect/token
├── Support/TestAuthentication.cs                + ServiceClient:Secret de teste
├── Support/InboxFactoryFixture.cs               IssuerToken → FakeTokenEndpoint
├── ServiceAccessTokenSourceTests.cs             NOVO
├── ServiceTokenDelegatingHandlerTests.cs        NOVO
├── ServiceClientConfigurationTests.cs           NOVO
└── SubjectAuthorizationTests.cs                 literal no lugar da constante do handler (D4)

apps/connectors/tests/Buteco.Connectors.Tests/
├── Support/FakeTokenEndpoint.cs                 NOVO (cópia)
├── Support/ConnectorsFactory.cs                 + segredo e IssuerToken → FakeTokenEndpoint com Api:BaseUrl
├── ServiceAccessTokenSourceTests.cs             NOVO
├── ServiceTokenDelegatingHandlerTests.cs        reescrito
└── ServiceClientConfigurationTests.cs           NOVO

tests/InboxOrchestratorRoundTrip.Tests/
├── Support/RoundTripFixture.cs                  IssuerToken e AgentReferenceValidator → apps/api; segredos com "+"
└── ServiceTokenRoundTripTests.cs                NOVO: S1, S2, segredo divergente

tests/ApiConnectorsRoundTrip.Tests/
├── Support/RoundTripFixture.cs                  IssuerToken do connectors → apps/api; segredo; Identity:Issuer configurável
├── FolderValidationRoundTripTests.cs            caso divergente com a causa nova (Q4); aceitação do H6
└── FolderValidationRoundTripTests.SyncCycle.cs  asserção do token; segredo divergente

docker-compose.prod.yml, .env.prod.example                                 (D8)
docs/configuration.md, docs/development.md, docs/deployment.md, 01 (Autenticação), 02, CHANGELOG.md
```

`apps/workers` e `apps/frontend` não mudam. Nada vai para `libs/`: são dois consumidores do source, e o `apps/api` não tem source, só o issuer (referência D10). Nenhum pacote novo.

## Tamanho

**Âncoras decompostas** (convenção 18), só código:

| PR | issue | produção | testes |
|---|---|---|---|
| #215 | #208 | 1.286 linhas, 26 arquivos | 2.459 linhas, 29 arquivos |
| #237 | #234 | +315 / −39, 7 arquivos | +568 / −6, 8 arquivos (+79 de suporte) |

**Projeção por componente** (rascunho; a da #234 errou +85% na produção, e esta erra para baixo pelo mesmo motivo: o que só aparece no apply):

| componente | produção | testes | duplos e suporte |
|---|---|---|---|
| `apps/api`: issuer, handler, `Program.cs` (D1, D2, D4) | +130 | +260 | — |
| `apps/inbox`: source, schedule, opções, boot, handler, `Program.cs` (D3–D5) | +260 | +420 | +110 (`FakeTokenEndpoint`) |
| `apps/connectors`: a mesma cópia, registro no `AddSyncCycle` | +265 | +400 | +110 |
| fixtures e asserções declaradas (D6) | — | ±120 | +40 |
| `tests/`: S1–S4, segredo divergente, aceitação do H6 (D9) | — | +450 | +80 (fixtures) |
| compose, `.env.prod.example`, `appsettings.Development.json` | +15 | — | — |
| **total** | **~+670** | **~+1.650** | **~+340** |

**Conclusão: cabe numa change revisável.** São ~0,5 vez a produção e ~0,7 vez os testes da #208. Mesmo com o erro de +85% da #234, a produção (~1.240) fica no tamanho da #208. Não proponho divisão (Q1). Se o dono quiser dividir, o corte natural é:

- **B1:** o `apps/api` (D1, D2, o S4 de `tests/`);
- **B2:** o `apps/inbox` e o `apps/connectors` (D3, D5, S1–S3).

Cada um sobe sozinho e em qualquer ordem, porque os receptores aceitam os dois formatos. Nenhuma issue é criada antes da decisão.

## Risks / Trade-offs

- **[O H6 é falso]** → o D1 decide antes de qualquer código depender dele. A alternativa preserva o formato, guardado pelos mesmos dois testes, e entra sem parada.
- **[O token gerado no processo diverge do `/connect/token` num claim que ninguém lê hoje]** → o guarda compara o conjunto de nomes de claims, e não só a lista de contrato (D1, Q3).
- **[Segredo divergente entre o `apps/api` e o serviço]** → não é pego no boot. Aparece como `Error` `invalid_client` no primeiro pedido, e a conferência pós-deploy procura a linha. Tem acordo em `tests/` para os dois serviços.
- **[O serviço pede token antes de o catálogo sincronizar o cliente, no primeiro deploy do `api` com a #234]** → `invalid_client` e a espera de 10 s, e depois o pedido seguinte passa. Em produção, o `api` já sincronizou desde o deploy da #234. Nas fixtures de `tests/`, a fixture espera a sincronização (D9).
- **[`401` do destino descarta o cache sem repetir]** → a chamada falha uma vez, e a próxima pede token novo. Sem caso operacional hoje (referência D3).
- **[O `TokenService.Issue` fica sem chamador de produção até a #182]** → previsto pelo passo B, e usado pelas fixtures até a #236 (D7).
- **[Segredo do serviço igual à chave de cifra do próprio serviço]** → recusado no boot do `apps/inbox` (Q2). O `apps/connectors` não tem chave de cifra.

## Migration Plan

1. Conferir que o `.env.prod` tem `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS`, desde a #234.
2. Subir o `api`, o `inbox` e o `connectors`, em qualquer ordem. Não há `migrator`.
3. Fazer a conferência do D8.
4. **Rollback:** voltar as imagens. O HMAC continua aceito, e os segredos ficam no `.env.prod`.

## Decisões do dono (03/11/2026)

Na revisão deste proposal e deste design, as quatro perguntas foram aceitas como recomendadas:

- **Q1.** Um PR só, sem divisão.
- **Q2.** O segredo do `apps/inbox` também precisa ser distinto, por bytes, da `Inbox:CredentialEncryptionKey`. A checagem é no boot, e a mensagem nomeia as duas chaves e nunca o valor. Há guarda visto reprovar contra a checagem ausente (D5, D9).
- **Q3.** O token gerado no processo leva `scope` `buteco-connectors`. O guarda do D1 compara o conjunto de nomes de **todos** os claims com o de um token do `/connect/token`, sem exceção, além dos valores de contrato.
- **Q4.** O caso S4 de chave divergente fica com a mesma asserção (`502` `connectors-error` `401`, nenhuma base criada), e a causa passa a ser um `Identity:Issuer` divergente no `apps/connectors`.

  **Revista na parada da 5.7 (decisão do dono, 03/11/2026).** Medido no `rt-connectors`: com o `Identity:Issuer` divergente, o `apps/connectors` nunca obtém as chaves, porque o `IssuerKeysRetriever` recusa o discovery de outro issuer. Sem chaves, ele responde `503`, e o `apps/api` responde `502` com `detail` `"503"`. A causa passou a ser **as chaves de outro emissor com o mesmo issuer**: o `TestIssuer` do projeto de teste do `apps/connectors`, ligado por link ao `rt-connectors`, com `AnnouncedIssuer` igual à url pública do `apps/api`. O `kid` do token gerado no processo não está entre as chaves, o `JwtBearer` responde `401`, e a asserção decidida vale. É a causa mais próxima da antiga, a chave divergente. O cenário da spec `knowledge-sync-folder-validation` é renomeado. **Troca de premissa registrada:** o caso provava que o `apps/connectors` recusa um token de serviço que ele não reconhece, e a causa era a chave HMAC. Com o `apps/api` mandando o token do emissor, a chave HMAC deixou de entrar no S4. A recusa passa a ser provada pelo `iss` que o `apps/connectors` não aceita. O contrato do `apps/api` diante do `401` não muda.

## Open Questions

Nenhuma.

<details>
<summary>Texto original das perguntas, como registro</summary>

- **Q1. Divisão.** Um PR só, ou B1 (`apps/api`) e B2 (serviços)? **Recomendo um PR só.** A projeção cabe na régua da #208 mesmo com o erro da #234. O risco novo do `apps/api` (H6) é pequeno e decidido na primeira tarefa. Dividir custa outra baseline, outro archive e outra conferência de deploy, sem ganho de ordem: os dois sobem em qualquer ordem de todo jeito.
- **Q2. O segredo do `apps/inbox` precisa ser distinto da `Inbox:CredentialEncryptionKey`?** **Recomendo que sim**, no boot, por bytes, com a mensagem nomeando as duas chaves. É a régua que o dono fixou na #234 para o `apps/api`: o segredo trafega na rede interna e vive em dois contêineres, e a chave de cifra decifra as credenciais dos canais. No `apps/connectors` não há chave de cifra com que comparar (a `GoogleDrive:ServiceAccountKeyBase64` é um JSON de credencial, e não 32 bytes).
- **Q3. O token gerado no processo leva `scope` `buteco-connectors`?** **Recomendo que sim.** Ninguém lê o `scope` hoje, mas com ele o token é exatamente o que um `/connect/token` daria a um cliente com esse escopo, e o guarda do D1 pode comparar o conjunto inteiro de nomes de claims. Sem ele, o guarda precisaria de uma exceção na comparação.
- **Q4. O caso de `tests/` "chave de assinatura divergente" do S4.** Com o `apps/api` mandando o token do emissor, a chave HMAC do `apps/connectors` deixa de importar, e o caso passaria a criar a base. **Recomendo manter o caso e a asserção (`502` `connectors-error` `401`, nenhuma base criada), trocando a causa:** o `apps/connectors` sobe com um `Identity:Issuer` divergente e recusa o `iss` do token. O cenário da spec `knowledge-sync-folder-validation` muda de nome. A alternativa, um `apps/connectors` com outro JWKS, exigiria um segundo emissor na fixture.

</details>
