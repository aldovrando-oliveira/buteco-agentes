## Context

**Estado atual (`8decb1e`, merge do PR #240, 04/11/2026).** Os três passos anteriores da divisão de 02/11/2026 estão em `main`:

- **#234 (A, receptores):** os clientes `buteco-servico-inbox` e `buteco-servico-connectors` existem no emissor, e o `apps/api` e o `apps/connectors` aceitam o token deles.
- **#235 (B, emissores):** os três serviços obtêm o token de serviço no emissor. **Nenhum serviço manda HMAC.**
- **#236 (C, fixtures):** a suíte de integração autentica pelo token do emissor. O que ainda usa o HMAC são os 55 casos da [lista publicada na #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6622921574), que provam o próprio HMAC.

O HMAC continua **aceito** nos três apps, e esta change o remove. O desenho completo é o design da #182 preservado [num comentário dela](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248), sobre `14d4ba9`, aqui chamado de "design de referência". Valem dele o D6, o D8 (passo D), o D9, as decisões Q3 e Q5 do dono e o passo D de "Tamanho e divisão". Dos designs arquivados da #234, da #235 e da #236 vêm o `TestIssuerTokens` (`Panel`, `ForClient`), o `FakeTokenEndpoint`, o `TestIssuer` das fixtures do inbox e do connectors e a lista por nome.

### Inventário em `8decb1e`

**Produção: o que sai inteiro** (474 linhas):

| peça | `apps/api` | `apps/inbox` | `apps/connectors` |
|---|---|---|---|
| `Auth/TokenService.cs` | 91 | 90 | 93 |
| `Auth/ITokenService.cs` | 8 | 8 | 8 |
| `Auth/TokenValidationResult.cs` | 6 | 6 | 6 |
| `Auth/TokenSigningOptions.cs` | 8 | 11 | 10 |
| `Auth/OperatorTokenAuthenticationHandler.cs` (esquema `OperatorToken`) | 44 | 43 | 42 |

**Produção: o que muda:**

| arquivo | o que sai ou muda |
|---|---|
| `apps/api/.../Identity/Validation/IssuerTokenAuthentication.cs` | o `AddPolicyScheme("Bearer")`, o `AddScheme<…, OperatorTokenAuthenticationHandler>` e o `SelectScheme`; o `AddAuthentication(IssuerSchemeName)` passa a ser o padrão; o `PolicySchemeName` sai |
| `apps/{inbox,connectors}/.../Auth/IssuerTokenAuthentication.cs` | os mesmos três, mais o ramo `!issuerOptions.Enabled`, que registra só o HMAC (inalcançável com a Q5, convenção 25); o `OnChallenge` ganha a condição do D3 |
| `apps/{inbox,connectors}/.../Auth/IssuerTokenStartupValidation.cs` | o ramo "sem `Identity:Issuer` → aviso, só HMAC" vira falha de boot (D2) |
| `apps/api/.../Program.cs` | `Configure<TokenSigningOptions>` e `AddSingleton<ITokenService, TokenService>` (l. 51 e 74), o comentário da l. 133 |
| `apps/inbox/.../Program.cs` | as mesmas duas linhas (l. 32-33) |
| `apps/connectors/.../Program.cs` | a checagem de boot da chave (l. 11-16), que é a única dos três (achado K1 da referência), e as duas linhas (l. 19-20) |
| `appsettings.Development.json` dos três | a `Auth:TokenSigningKey` |
| comentários | `OperatorPasswordHasher` (l. 8), `KnowledgeDocumentEndpoints.ReadAuthor` (l. 287, cita o handler HMAC), `ConnectorsSubjectAuthorizationHandler` (l. 31), `CurrentUser` (l. 11-13) |

Nenhuma outra referência ao esquema existe: o grep de `PolicySchemeName`, `OperatorTokenAuthenticationHandler.SchemeName` e `"OperatorToken"` fora do registro só acha os testes do seletor, que saem.

### Achados da exploração

- **K1. O desafio do emissor no inbox e no connectors responde `503` a qualquer desafio sem chaves obtidas** (`ChallengeWithoutKeysAsUnavailable`), **inclusive à requisição sem token.** Hoje a requisição sem token vai ao HMAC e recebe `401`, com ou sem chaves. Com o `JwtBearer` como único esquema, ela passaria a receber `503` até a primeira obtenção das chaves. Isso muda comportamento alcançável. Também tornaria intermitentes os casos "sem token → `401`" (`RouteAuthenticationTests.RotaSemToken_Responde401` do connectors cria o cliente logo depois do boot). Tratado no D3.
- **K2. Com `Identity:Issuer` obrigatório, o `Api:BaseUrl` do connectors também passa a ser obrigatório**, sem código novo: a checagem de boot que já existe (`IssuerTokenStartupValidation.cs:49-58`) recusa o emissor ligado sem o endereço interno. O caso que a guarda já existe desde a #208: `IssuerTokenStartupValidationTests.IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys`. **Consequência:** os ramos "sem `Api:BaseUrl`" do connectors ficam inalcançáveis: o aviso do `ValidateApiConfiguration`, a dispensa do segredo no `ServiceClientConfigurationValidation`, o registro condicional do agendador no `AddKnowledgeSync` e o `503 sync-not-configured` do "Sincronizar agora", que é contrato (`connectors-api`, `knowledge-sync-cycle`) e tem mapeamento no `apps/frontend` (`connectorErrors.ts:152`). Pergunta P1.
- **K3. A reescrita publicada dos 4 casos "HMAC `operator` → `403` em `CurrentUser`" não vê o defeito que eles guardam.** Cada um é guarda documentado contra "o `CurrentUser` retirado da rota" (ex.: `AccessLinkCallerTests`, "Verificado contra o defeito (tasks.md, 5.6): com o `CurrentUser` retirado de criar convite"). Com o token do `buteco-servico-inbox`, o `403` sai antes, no `ServiceScopeAuthorizationHandler`, porque o `service:inbox` só tem duas rotas. Retirado o `CurrentUser`, o caso continuaria verde. Pergunta P2.
- **K4. Dois casos fora da lista dependem de estado que a Q5 elimina** (varredura estática dos hosts com `Identity:Issuer` ou `Api:BaseUrl` vazios, no connectors). Pergunta P3. Os outros hosts com o emissor desligado só precisam ganhar um emissor na fixture, sem mudar nome nem asserção (D6).
- **K5. Os dois `Hs256SignedWithTheHmacKey_IsUnauthorized` não guardam nada, nem hoje nem reescritos.** O validador do emissor só conhece as chaves que o `IssuerKeysRetriever` tira do JWKS (`IssuerKeysRetriever.cs:123-124`, nos dois apps: `new JsonWebKeySet(json)` e `GetSigningKeys()`). Na fonte do `Microsoft.IdentityModel.Tokens` 8.19.2, a versão que os dois apps resolvem (tag `8.19.2`, `ee19952`), o `JsonWebKeySet.GetSigningKeys` só converte `RSA` (l. 143) e `EC` (l. 176). Qualquer outro tipo, inclusive `oct`, cai no `else` das l. 183-190: registra o `IDX10810` e só entrega a chave com `SkipUnresolvedJsonWebKeys = false`. O padrão é `true` (l. 94 e 101). Por isso nenhum HS256 tem chave para validar, seja assinado com a `TokenSigningKey`, com outra chave simétrica ou com uma `oct` publicada no JWKS, e a recusa não depende do `ValidAlgorithms`. **Visto em 04/11/2026**, com o `ValidAlgorithms = [RS256]` retirado do `IssuerTokenAuthentication.cs` (l. 122) dos dois apps: o `Hs256SignedWithTheHmacKey_IsUnauthorized` continuou verde. O `Rs512WithThePublishedKey_IsUnauthorized`, que assina com a chave RSA publicada, reprovou nos dois (`Expected: Unauthorized`, `Actual: OK`). Os arquivos foram restaurados pela cópia, com o blob conferido contra o `HEAD` (`9c4141c` no inbox, `3ba23ab` no connectors), e os dois casos aprovaram de novo. **O guarda da restrição a RS256 é o `Rs512WithThePublishedKey_IsUnauthorized`**, nos dois apps. Decisão do dono: P4, opção A.
- **K6. A lista publicada tem 9 reescritas, e não 8**: os 4 mistos, os 4 de `CurrentUser` e o `DocumentWrites_WithOperatorHmacToken_RecordOperatorAsAuthor`. Remover o `DocumentWrites` reduziria cobertura (a lista registra que nenhum outro caso afirma o autor da edição e da exclusão). Decisão do dono: P0, os 9. Com a P4, os dois `Hs256…` passam de "reescrever" para "sair": **ficam 7 reescritos**.

## Goals / Non-Goals

**Goals:**

- Nenhum app aceita HMAC. O `JwtBearer` é o único esquema em cada app.
- O `apps/inbox` e o `apps/connectors` não sobem sem `Identity:Issuer`.
- A requisição sem token recebe `401` com `WWW-Authenticate: Bearer` nos três apps, e no inbox e no connectors também antes das chaves.
- A lista de 55 casos é aplicada por nome. Fora dela, só saem ou mudam de asserção os casos que as perguntas aprovarem.
- Nenhuma ocorrência do HMAC de autenticação fica no código, nos testes, no compose, nos `.env` de exemplo e nos docs vivos (D8).
- O deploy sobe os três apps em qualquer ordem, e o rollback continua possível por uma semana.

**Non-Goals:**

- **Mudar o emissor, os clientes de serviço, as tabelas de clientes ou as tabelas de subjects.** O `ServiceScopeAuthorizationHandler`, o `ConnectorsSubjectAuthorizationHandler`, a `SubjectAuthorization` do inbox e as checagens de boot delas não mudam.
- **Mexer no `apps/workers`, no `apps/frontend` ou em `libs/`.** Os ramos "sem `Api:BaseUrl`" do connectors, o `sync-not-configured` e o mapeamento dele no painel ficam como estão: são a #241 (P1).
- **Restrição do subject `operator` ao Admin** (#184): a regra "HMAC aceito só para subjects de serviço" deixa de existir, e a #184 já foi comentada na #182 (ajuste das decisões do dono de 02/11/2026).
- **Placeholder `changeme`** (#55): a `TokenSigningKey` só sai da lista dela.

## Decisions

### D1. Um esquema por app, o `JwtBearer`, como padrão (D6 da referência)

Em cada app, `AddAuthentication(IssuerTokenAuthentication.IssuerSchemeName).AddJwtBearer(IssuerSchemeName, …)`. Saem o `AddPolicyScheme`, o `SelectScheme`, o `PolicySchemeName` e o `AddScheme<…, OperatorTokenAuthenticationHandler>`. O nome do esquema continua `IssuerToken`, para não mexer nos logs e nos `ClaimsIdentity(…, IssuerSchemeName)` dos mapeadores.

- **Token no formato antigo** (`payload.assinatura`, um ponto) passa a ir ao `JwtBearer`, que não o lê como JWS e responde `401` com `WWW-Authenticate: Bearer error="invalid_token"`. É o mesmo `401` de hoje para um HMAC inválido. Caso novo, por app: `HmacShapedToken_SignedWithTheOldKey_Is401` (D5), visto reprovar com o esquema HMAC de volta.
- **No inbox e no connectors, sai o ramo `!issuerOptions.Enabled`** do `AddIssuerTokenAuthentication`. Com a Q5, ele é inalcançável (convenção 25), e o registro passa a ser incondicional.
- **`IssuerTokenOptions.Enabled`** fica só se a checagem de boot ainda o ler. Se não sobrar leitor, sai.

### D2. `Identity:Issuer` obrigatório no inbox e no connectors (Q5)

O ramo `!options.Enabled` do `ValidateIssuerTokenConfiguration` deixa de avisar e passa a lançar, no idioma do issuer inválido logo abaixo, sem ecoar valor:

> `Identity:Issuer não configurado: é o issuer público do emissor do apps/api, o mesmo valor de PublicUrl:BaseUrl lá. Sem ele, este app não tem como autenticar requisição nenhuma.`

A frase "Para desligar a validação do token do emissor, deixe `Identity:Issuer` vazio" sai das duas mensagens de erro, nos dois apps.

**O `Api:BaseUrl` do connectors passa a ser obrigatório**, pela checagem existente (K2). O inbox já o exige no `Program.cs` (l. 79-80). Esta change não acrescenta outra checagem para ele. O que fazer com os ramos "sem `Api:BaseUrl`" que ficam inalcançáveis é a P1.

**Os 2 casos da Q3 saem, e não viram casos de falha de boot.** O estado que eles provam, o connectors de pé sem `Api:BaseUrl`, deixa de existir. Já a falha de boot correspondente (emissor ligado, `Api:BaseUrl` ausente → o boot falha nomeando as duas chaves) tem caso desde a #208, o `IssuerTokenStartupValidationTests.IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys`. Converter os dois duplicaria esse caso. Ele é o substituto nomeado dos dois.

**Casos novos** (convenção 15), um em cada app: `WithoutIssuer_FailsTheRealApplicationStartup_NamingTheKey`, no lugar do `WithoutIssuer_ValidationIsOff_…_HmacWorks_AndBootWarns`, que sai. Ele é visto reprovar com o ramo de aviso de volta.

### D3. `401` sem token, com `WWW-Authenticate: Bearer`, antes e depois das chaves (Q3 e K1)

- **No `apps/api`:** o desafio padrão do `JwtBearer` responde `401` com `WWW-Authenticate: Bearer` e corpo vazio. Antes de o anel carregar, o `IdentityKeyRingGate` já responde `503` a toda rota, e isso não muda.
- **No inbox e no connectors:** o `ChallengeWithoutKeysAsUnavailable` passa a responder `503` só quando **havia token** a validar. A requisição sem `Authorization` segue o desafio padrão, com `401` e `WWW-Authenticate: Bearer`, com ou sem chaves obtidas. **Motivo:** sem token, a resposta não depende das chaves. Hoje ela é `401` sempre, e responder `503` mandaria o cliente esperar por algo que não vai mudar o resultado. A condição é sobre a presença do cabeçalho, e não sobre o `AuthenticateFailure`, que também fica nulo quando o cabeçalho existe sem o esquema `Bearer`. Esse caso é token inválido, e não "sem token", e continua no `503` antes das chaves, como hoje para o token do emissor.
- **Casos novos** no inbox e no connectors: `WithoutToken_BeforeTheKeys_Is401_WithTheBearerChallenge`, com o `TestIssuer` segurando as chaves do início ao fim (o recurso do guarda da 3.1 da #236, que hoje só a cópia do inbox tem; a do connectors ganha o mesmo). O caso é visto reprovar sem a condição. **No `apps/api`:** `UnknownRoute_WithoutToken_Is401_WithTheBearerChallenge`, ao lado do caso de hoje da rota inexistente, visto reprovar com o esquema de política de volta.

**A frase do `01` reescrita** ("Autenticação", l. 607-611):

> **Rota inexistente sem token responde `401`**, e não `404`, no `apps/api`: o `FallbackPolicy` (`RequireAuthenticatedUser`) vale também para o pedido que não casa com rota, e o `401` sai com o corpo vazio e com o desafio padrão do `JwtBearer`, `WWW-Authenticate: Bearer`, igual ao da rota que existe. É intencional: quem chega sem token não descobre quais rotas existem, porque as duas respostas são iguais. Com token válido, a rota inexistente responde `404`. *(Até a #182, o `401` saía sem `WWW-Authenticate`, porque a requisição sem token caía no esquema HMAC.)*

Nenhum código do painel nem teste lê o cabeçalho (grep em `apps/` e `tests/`, sem ocorrência fora do `01`).

### D4. Chave fora da configuração; o `.env.prod` fica uma semana (D8 da referência)

- **Saem** a `Auth:TokenSigningKey` dos três `appsettings.Development.json`, o `Auth__TokenSigningKey` dos três serviços do `docker-compose.prod.yml` (l. 126, 168 e 235, com os comentários das l. 161 e 167), as l. 216-227 e 252-254 do `.env.example` e a `AUTH_TOKEN_SIGNING_KEY` do `.env.prod.example` (l. 151).
- **`Identity__Issuer` do inbox e do connectors** (l. 162 e 246) continua `${PUBLIC_DOMAIN}`. Com ele vazio, o compose já para antes de subir qualquer contêiner, pelo `${PUBLIC_DOMAIN:?…}` do serviço `api` (l. 125), porque o compose interpola o arquivo inteiro. Um `:?` repetido nos dois não mudaria nada observável. O comentário "Vazia, só o token HMAC é aceito" sai.
- **`docker-compose.yml`** (desenvolvimento): o grep não acha a chave. Não muda.
- **No `.env.prod` do servidor**, a `AUTH_TOKEN_SIGNING_KEY` **fica** por uma semana depois do deploy. A versão anterior de qualquer um dos três a lê: o compose anterior do `api` tem `:?`, e o connectors anterior tem a checagem de boot da chave. Sem ela, o rollback não sobe. O runbook (D7) diz a data e o comando para tirar, e o `02` registra o deploy com a data da retirada.

### D5. A lista dos 55 casos, por nome

**Saem 46**, mais os **2 da Q3**. São **7 reescritos** (K5, K6). Fora da lista, só mudam os casos da P3. Cada reescrito é visto reprovar contra o defeito da coluna própria (convenção 15), e o registro vai ao `tasks.md`.

**Saem (46):**

| suíte | casos |
|---|---|
| connectors (19) | `IssuerTokenValidationTests.Hs256SignedWithTheHmacKey_IsUnauthorized` (K5; o guarda da restrição a RS256 é o `Rs512WithThePublishedKey_IsUnauthorized`); `IssuerKeysLifecycleTests.BootsWithTheIssuerUnreachable_HmacWorks_AndIssuerTokenIsUnavailable`; `IssuerTokenSchemeSelectionTests.HmacTokens_ValidAndInvalid_NeverTouchTheIssuerKeys`, `.InvalidJwt_IsUnauthorized_WithoutCallingTheHmacValidator`, `.OperatorHmacToken_StillAccepted_WithTheIssuerEnabled` e `.Selector_ChoosesBySegmentCount` (7 casos); `IssuerTokenStartupValidationTests.WithoutIssuer_ValidationIsOff_IssuerTokenIsUnauthorized_HmacWorks_AndBootWarns`; `RouteAuthenticationTests.TokenDeOperadorAssinadoComAMesmaChave_Responde200`, `.TokenAssinadoComOutraChave_Responde401`, `.TokenExpirado_Responde401`, `.BootSemChaveDeAssinatura_Falha` e `.BootComChaveDeAssinaturaVazia_Falha` (2 casos) |
| api (13) | `Identity.IdentityIssuerCoexistenceTests.InboxServiceToken_StillReachesItsRoute`; `Identity.IssuerTokenAcceptanceTests.HmacToken_IsStillAccepted_AndAnInvalidOneStill401` e `.InvalidJwt_Is401_WithoutCallingTheHmacValidator`; `Identity.IssuerTokenSchemeSelectionTests.AnythingElse_GoesToTheHmacScheme` (6 casos) e `.ThreeSegments_GoToTheIssuerScheme` (4 casos) |
| inbox (13) | `IssuerTokenValidationTests.Hs256SignedWithTheHmacKey_IsUnauthorized` (K5; idem); `IssuerKeysLifecycleTests.BootsWithTheIssuerUnreachable_HmacWorks_AndIssuerTokenIsUnavailable`; `IssuerTokenSchemeSelectionTests.HmacTokens_ValidAndInvalid_NeverTouchTheIssuerKeys`, `.InvalidJwt_IsUnauthorized_WithoutCallingTheHmacValidator`, `.OperatorHmacToken_StillAccepted_WithTheIssuerEnabled` e `.Selector_ChoosesBySegmentCount` (7 casos); `IssuerTokenStartupValidationTests.WithoutIssuer_ValidationIsOff_IssuerTokenIsUnauthorized_HmacWorks_AndBootWarns` |
| rt-inbox (1) | `RoundTripTests.OperatorHmacToken_SignedWithTheSharedKey_IsAcceptedByInboxWithoutNetworkCallToApi` |

Com isso saem inteiros os três `IssuerTokenSchemeSelectionTests.cs` (248 linhas). Do `RouteAuthenticationTests.cs` do connectors ficam o `RotaSemToken_Responde401`, o `RotaAnonimaSemClassificacao_DerrubaOBoot` e os dois de CORS, e o comentário de cabeçalho perde a requisição "Token validado localmente com a chave compartilhada".

**Os 2 da Q3 saem** (D2), com o `IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys` como substituto: `SyncEndpointsTests.WithoutApiBaseUrl_Is503SyncNotConfigured_WithoutAnyCall` e `ApiConfigurationValidationTests.RealComposition_WithoutBaseUrl_StartsWarnsAndServesNavigation`.

**Os 7 reescritos:**

| # | suíte | caso hoje → depois | o que muda | visto reprovar contra |
|---|---|---|---|---|
| R1 | connectors | `IssuerTokenValidationTests.PanelToken_OnDescribeFolder_IsForbidden_LikeTheHmacOperator` → `PanelToken_OnDescribeFolder_IsForbidden` | sai a metade HMAC; fica o `403` do token do painel | `operator` acrescentado à lista de descrever pasta no `SubjectRoutes` |
| R4 | connectors | `SecretLeakTests.NenhumLogDoCicloContemConteudoTokenChaveOuNomeDeArquivo` (nome fica) | sai só a asserção da `TokenSigningKey`; ficam o conteúdo, o nome de arquivo, o segredo do cliente e os tokens enviados | o `ServiceAccessTokenSource` logando o access token obtido |
| R5 | api | `Knowledge.KnowledgeDocumentCatalogTests.DocumentWrites_WithOperatorHmacToken_RecordOperatorAsAuthor` → `DocumentWrites_WithThePanelToken_RecordOperatorAsAuthor` | o cliente padrão da fixture (`TestIssuerTokens.Panel`) no lugar do `ITokenService`; a asserção (3 eventos, autor `operator`) fica | a edição gravando o autor pelo `client_id` em vez do `ReadAuthor` |
| R6 | api | `Users.AccessLinks.AccessLinkCallerTests.OperatorHmac_Is403_OnTheFourRoutes_AndNothingIsWritten` → `OperatorWithoutUser_Is403_OnTheFourRoutes_AndNothingIsWritten` | `TestIssuerTokens.Panel` (operator sem usuário) no lugar do HMAC (P2); asserção igual | o `CurrentUser` retirado de criar convite (o defeito já registrado no caso) |
| R7 | api | `Users.Management.UserRoutesCallerTests.OperatorHmac_Is403_OnEveryRoute_AndNothingChanges` → `OperatorWithoutUser_Is403_OnEveryRoute_AndNothingChanges` | idem | o `CurrentUser` retirado de `PUT /users/{id}` (idem) |
| R8 | api | `Users.TwoFactor.TwoFactorCallerTests.OperatorHmac_OnMyAccount_Is403` → `OperatorWithoutUser_OnMyAccount_Is403` | idem | o `CurrentUser` retirado de `GET /account` |
| R9 | api | `Users.TwoFactor.TwoFactorCallerTests.OperatorHmac_OnTheReset_Is403_AndChangesNothing` → `OperatorWithoutUser_OnTheReset_Is403_AndChangesNothing` | idem | a redefinição sem o `CurrentUser` (idem) |

**Renomeado sem reescrita:** `IssuerTokenValidationTests.PanelToken_CanSyncNow_LikeTheHmacOperator` → `PanelToken_CanSyncNow`, do connectors, porque o termo de comparação deixa de existir (D5 da #236 deixou o nome para esta change).

**Lista de renomeados** (8): R1, R5, R6, R7, R8, R9, o `PanelToken_CanSyncNow` e, da P3, o `DefaultFactory_ResolvesApiBaseUrlAsNotConfigured` → `DefaultFactory_ResolvesApiBaseUrlToTheTestIssuer`. O R4 mantém o nome.

**Casos novos** (D1, D2, D3), cada um visto reprovar contra o defeito nomeado lá: `HmacShapedToken_SignedWithTheOldKey_Is401` (×3), `WithoutIssuer_FailsTheRealApplicationStartup_NamingTheKey` (×2), `WithoutToken_BeforeTheKeys_Is401_WithTheBearerChallenge` (×2) e `UnknownRoute_WithoutToken_Is401_WithTheBearerChallenge` (api).

**Os casos da P3** (fora da lista, decisão do dono): sai o `ServiceClientConfigurationTests.WithoutTheApiAddress_TheSecretIsNotRequired`, com o `IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys` como substituto. O `ApiConfigurationValidationTests.DefaultFactory_ResolvesApiBaseUrlAsNotConfigured` é reescrito como `DefaultFactory_ResolvesApiBaseUrlToTheTestIssuer` e visto reprovar sem o `UseSetting` do `Api:BaseUrl` na fábrica.

**Conta final**, por nome, contra a baseline (`~/.cache/buteco-agents/182D-baseline/r1/nomes.txt`, 1.975 resultados): saem 46 + 2 (Q3) + 1 (P3) = **49**, mudam de nome 8, entram 8 novos. Fora disso, nenhum nome muda.

### D6. Fixtures e helpers

| onde | o que sai | o que muda |
|---|---|---|
| `apps/api` `Support/TestAuthentication.cs` | `TokenSigningKey`, a entrada `Auth:TokenSigningKey` do `ConfigOverrides`, `AttachOperatorToken` | — |
| `apps/inbox` `Support/TestAuthentication.cs` | `TokenSigningKey` e a entrada do `ConfigOverrides` | `UseIssuer(string issuer = "")` perde o valor padrão |
| `apps/inbox` `Support/IssuerTokenFactory.cs` | `OperatorHmacToken()` e o contador do `ITokenService` | — |
| `apps/inbox` `DebounceOptionsValidationTests`, `InboxShutdownUnderKestrelTests`, `ServiceClientConfigurationTests` | — | `UseIssuer()` → `UseIssuer(TestIssuer.Issuer)`. Boot sem rede (as chaves vêm em segundo plano); nome e asserção ficam |
| `apps/connectors` `Support/ConnectorsFactory.cs` | `Auth:TokenSigningKey`, `CountingTokenService`, `UseIssuer()`, `_issuerEnabled` | **o emissor passa a ligado em toda fábrica**: `Identity:Issuer` = `TestIssuer.Issuer` e `Api:BaseUrl` = `TestIssuer.InternalBaseUrl` por padrão, sobrescrevíveis pelo `extraConfiguration`. A retirada do agendador da #236 continua igual: sem `issuer` passado e sem `Api:BaseUrl` pedido |
| `apps/connectors` `Support/TestAuthentication.cs` | `TokenSigningKey`, `IssueToken`, `CreateClientAs` | `CreateIssuerClientAs` deixa de ligar o emissor, que já vem ligado |
| `apps/connectors` `IssuerTokenStartupValidationTests.IssuerWithoutApiBaseUrl_…` | — | passa `Api:BaseUrl = ""` explícito, porque a fábrica deixa de o esvaziar; nome e asserção ficam |
| `tests/InboxOrchestratorRoundTrip.Tests` `Support/RoundTripFixture.cs` | `TokenSigningKey`, as duas entradas `Auth:TokenSigningKey`, `OperatorHmacToken()` | — |
| `tests/ApiConnectorsRoundTrip.Tests` `Support/RoundTripFixture.cs` | `TokenSigningKey` e as duas configurações | — |

**Tempo:** a mudança de fábrica do connectors põe o `IssuerKeysRefreshService` contra o handler em memória em ~280 hosts que hoje sobem sem ele. Vale a regra da #236 (D7 de lá): nenhuma suíte passa de 1,5× a duração da baseline, com a CPU em 100. Acima disso, o apply para e mede o custo antes de seguir.

### D7. Deploy (D8 da referência, passo D)

**Ordem livre.** Desde a #235, ninguém manda HMAC, e cada app novo só deixa de aceitar o que não chega mais.

**Antes de subir:** o `.env.prod` tem `PUBLIC_DOMAIN`, `SERVICE_CLIENT_SECRET_INBOX` e `SERVICE_CLIENT_SECRET_CONNECTORS` (já exigidos desde a #234 e a #235), e a `AUTH_TOKEN_SIGNING_KEY` **fica** (D4).

**Conferência pós-deploy** (o critério de aceite da issue, mais os quatro fluxos):

1. No log do `inbox` e do `connectors`: `Validação do token do emissor ligada: issuer …`, e nenhum `invalid_client`.
2. O painel entra e acessa uma rota de cada app: a lista de agentes (`apps/api`), Canais (`apps/inbox`) e "Sincronizar agora" numa base (`apps/connectors`).
3. Os quatro fluxos entre serviços: uma mensagem de canal chega ao agente (S1 `GET /agents/{id}`, S2 `POST /agents/{id}/a2a`), a sincronização da base termina (S3), e o cadastro de uma base sincronizada valida a pasta (S4).
4. Sem token, cada app responde `401` com `WWW-Authenticate: Bearer` (um `curl -si` por prefixo, como nas verificações da #218 e da #219).

**Rollback:** voltar o checkout do servidor ao commit anterior (o compose anterior) e as imagens anteriores, com `docker compose up -d`. A versão anterior aceita o token do emissor e o HMAC, e lê a `AUTH_TOKEN_SIGNING_KEY`, que por isso continua no `.env.prod`. **Depois de 7 dias estável,** o runbook manda tirar a linha do `.env.prod` (`sed -i '/^AUTH_TOKEN_SIGNING_KEY=/d' .env.prod`) e registrar a data no `02`. Depois disso, o rollback para antes da #182 exige recolocar a chave.

### D8. Documentação e o critério da varredura

**Reescritos:**

- **`01`, "Autenticação":** abre com um mecanismo só, o access token do emissor. Sai o parágrafo "Token stateless assinado com HMAC". A revogação da convenção "sem biblioteca JWT" fica em uma linha histórica. "Nenhuma requisição espera o `apps/api`" passa a falar só das chaves do JWKS e do anel. Em "Tokens de serviço", "a tabela protege contra defeito de código, não contra quem guarda a chave (#117)" vira a credencial por serviço. Na "Validação do token do emissor", saem "um esquema por token, pela forma" e "sem `Identity:Issuer`, a validação fica desligada". Em "O `apps/api` aceita o próprio token", saem "esquema de política pela forma do token" e "Até a #235…". E entra a frase do D3.
- **`SECURITY.md`:** o parágrafo "Authentication tokens" (l. 51 em diante), em inglês. O `apps/api` emite todos os tokens: o do usuário, pelo authorization code com PKCE, e o de cada serviço, pelo `client_credentials` com um segredo por serviço. Todos são RS256, com chaves em rotação e JWKS. Cada app valida localmente e restringe o subject por tabela. Isso fecha a #122. Em "Shared secrets across processes" (l. 91), a `TokenSigningKey` sai, e entram os dois segredos de serviço, cada um compartilhado só entre o `apps/api` e o serviço dele. O risco da #117 sai da lista de riscos aceitos.
- **`docs/architecture.md`:** l. 852-921 (os dois mecanismos, o HMAC, a chave no connectors), 1101 e 1141 ("HMAC → `403`") e o diagrama de l. 1166-1181, que fica com o `JwtBearer` só.
- **`docs/configuration.md`:**
  - saem as linhas da `TokenSigningKey` (l. 42, 75, 124, 245 e 267) e da `AUTH_TOKEN_SIGNING_KEY` (l. 373);
  - o `Identity__Issuer` passa a obrigatório no inbox e no connectors (l. 247);
  - a tabela "valor / boot / token do emissor / HMAC" (l. 295-299) perde a coluna HMAC e a linha "vazio";
  - a tabela do `Api:BaseUrl` do connectors (l. 278) passa a "ausente → o boot falha", pelo `Identity:Issuer`.
- **`docs/development.md`:** l. 163-165, 288, 302-303, 354 e 426-427. Entra a frase do D9 da referência: o segredo de cada serviço precisa ser igual no serviço e no `apps/api`. O `dotnet run` dos três continua igual, porque o `appsettings.Development.json` do inbox e do connectors já traz `Identity:Issuer` e `Api:BaseUrl`.
- **`docs/deployment.md`:**
  - seção nova "Remoção do HMAC (#182)" no topo de §3, com o D7;
  - a tabela de variáveis (l. 318) perde a linha da chave;
  - a tabela de diagnóstico da #208 (l. 828-847) troca "Validação desligada… só o HMAC funciona" pela mensagem de boot do D2;
  - a lista do `changeme` (l. 860) perde a chave;
  - "Risco aceito: chave compartilhada (#117)" (l. 867-881) vira uma linha: fechado pela #182.

**Critério de aceite da varredura:** depois da change, o grep de `TokenSigningKey`, `TOKEN_SIGNING`, `ITokenService`, `TokenService`, `OperatorToken`, `AttachOperatorToken` e `HMAC`/`Hmac` em `apps/`, `tests/`, `docker-compose*.yml`, `.env*`, `deploy/`, `SECURITY.md`, `01`, `docs/` e `openspec/specs/` (depois do sync) não acha ocorrência fora das exceções. O script é o `varredura-236.py`, adaptado aos termos.

**Exceções aceitas** (P5):

- `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md` e `openspec/changes/archive/`: são histórico;
- as **primitivas criptográficas** que não são o token: o HMAC-SHA1 do TOTP (`Totp*`, `two-factor-totp`, `SECURITY.md` l. 76, `docs/development.md` l. 512, `TotpTests.NewSecret_HasTheSizeOfTheHmacOutput`), o PBKDF2-HMAC-SHA256 do hash de senha, o `Aes256CbcHmacSha512` da cifra do anel;
- as **seções de deploys passados** do `docs/deployment.md` (#217, #219, #234, #235), que registram o que cada deploy fez na época. Cada uma ganha no título a marca "(histórico)", e o texto não muda;
- as notas históricas em itálico do `01` (o padrão "*(Até a #… dizia …)*") que citam o HMAC.

### D9. Nada em `libs/`

O que sai é código duplicado nos três apps. Nada entra. Os dois `IssuerTokenAuthentication` do inbox e do connectors continuam cópias espelhadas, com o comentário de espelhamento.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Auth/
│   ├── TokenService.cs, ITokenService.cs,
│   │   TokenValidationResult.cs, TokenSigningOptions.cs,
│   │   OperatorTokenAuthenticationHandler.cs        SAEM (D1)
│   ├── OperatorPasswordHasher.cs                     comentário
│   └── ServiceScopeAuthorizationHandler.cs           não muda
├── Identity/Validation/IssuerTokenAuthentication.cs  JwtBearer único, sem seletor (D1)
├── KnowledgeDocuments/Endpoints/KnowledgeDocumentEndpoints.cs   comentário do ReadAuthor
├── Users/CurrentUser.cs                              comentário
├── Program.cs                                        sem TokenSigningOptions/ITokenService
└── appsettings.Development.json                      sem Auth:TokenSigningKey

apps/inbox/src/Buteco.Inbox/
├── Auth/
│   ├── (os mesmos 5 arquivos)                        SAEM
│   ├── IssuerTokenAuthentication.cs                  JwtBearer único, registro incondicional, 401 sem token (D1, D3)
│   └── IssuerTokenStartupValidation.cs               sem Identity:Issuer → falha de boot (D2)
├── Program.cs, appsettings.Development.json          idem

apps/connectors/src/Buteco.Connectors/
├── Auth/
│   ├── (os mesmos 5 arquivos)                        SAEM
│   ├── IssuerTokenAuthentication.cs                  espelho do inbox
│   ├── IssuerTokenStartupValidation.cs               espelho do inbox
│   └── ConnectorsSubjectAuthorizationHandler.cs      comentário
├── Program.cs                                        sem a checagem da chave (l. 11-16)
└── appsettings.Development.json                      sem Auth:TokenSigningKey
    (os ramos "sem Api:BaseUrl" ficam: #241, P1)

apps/api/tests/Buteco.Api.Tests/
├── Support/TestAuthentication.cs                     sem AttachOperatorToken nem a chave (D6)
├── Identity/IssuerTokenSchemeSelectionTests.cs       SAI
├── Identity/IssuerTokenAcceptanceTests.cs            −2 casos, +HmacShapedToken, +UnknownRoute_WithoutToken
├── Identity/IdentityIssuerCoexistenceTests.cs        −1 caso
├── Knowledge/KnowledgeDocumentCatalogTests.cs        R5
└── Users/{AccessLinks,Management,TwoFactor}/*CallerTests.cs   R6–R9

apps/inbox/tests/Buteco.Inbox.Tests/
├── Support/{TestAuthentication,IssuerTokenFactory}.cs   sem HMAC (D6)
├── IssuerTokenSchemeSelectionTests.cs                SAI
├── IssuerKeysLifecycleTests.cs                       −1 caso
├── IssuerTokenStartupValidationTests.cs              −1, +WithoutIssuer_Fails…
├── IssuerTokenValidationTests.cs                     −Hs256 (K5), +WithoutToken_BeforeTheKeys, +HmacShapedToken
└── DebounceOptionsValidationTests.cs, InboxShutdownUnderKestrelTests.cs,
    ServiceClientConfigurationTests.cs                UseIssuer(TestIssuer.Issuer)

apps/connectors/tests/Buteco.Connectors.Tests/
├── Support/{ConnectorsFactory,TestAuthentication,TestIssuer}.cs   emissor sempre ligado; sem HMAC (D6)
├── IssuerTokenSchemeSelectionTests.cs                SAI
├── RouteAuthenticationTests.cs                       −6 casos (8 com os InlineData)
├── IssuerKeysLifecycleTests.cs                       −1
├── IssuerTokenStartupValidationTests.cs              −1, +WithoutIssuer_Fails…, ajuste do IssuerWithoutApiBaseUrl
├── IssuerTokenValidationTests.cs                     R1, −Hs256 (K5), renomeado CanSyncNow, +WithoutToken_BeforeTheKeys, +HmacShapedToken
├── SecretLeakTests.cs                                R4
├── SyncEndpointsTests.cs                             −1 (Q3)
├── ApiConfigurationValidationTests.cs                −1 (Q3), DefaultFactory reescrito (P3)
└── ServiceClientConfigurationTests.cs                −1 (P3)

tests/InboxOrchestratorRoundTrip.Tests/{RoundTripTests,Support/RoundTripFixture}.cs   −1 caso, sem a chave
tests/ApiConnectorsRoundTrip.Tests/Support/RoundTripFixture.cs                         sem a chave

docker-compose.prod.yml, .env.example, .env.prod.example     sem a chave (D4)
01-ARQUITETURA_E_CONVENCOES.md, SECURITY.md, docs/{architecture,configuration,development,deployment}.md   (D8)
openspec/changes/remover-hmac-token-servico/          proposal, design, specs (depois das respostas), tasks
docs: 02, CHANGELOG.md
```

`apps/workers`, `apps/frontend`, `libs/` e `docker-compose.yml` não mudam.

## Tamanho

**Âncora:** a referência projetou ~−650 de produção e ~−800 de teste para o passo D.

| componente | produção | testes |
|---|---|---|
| os 5 arquivos × 3 apps | −474 | — |
| registro de esquema, seletor e ramo desligado (D1) | −90 | — |
| `Program.cs`, checagem da chave, comentários | −30 | — |
| boot sem `Identity:Issuer` (D2) e `401` sem token (D3) | +25 | — |
| os 46 casos, os 2 da Q3 e o da P3 | — | −720 (os três arquivos do seletor são 248) |
| helpers HMAC das fixtures (D6) | — | −120 |
| fábrica do connectors com o emissor ligado (D6) | — | ±40 |
| 7 reescritos e 2 renomeados sem reescrita | — | ±70 |
| casos novos (8) e o "segurar as chaves" do `TestIssuer` do connectors | — | +200 |
| **total** | **~−590 / +25** | **~−820 / +300** |

Pela correção da convenção 18 (as linhas de teste novas erram para cima, sobretudo em duplo), o realista é +300 a +500 de teste. **É remoção quase pura, e cabe num PR revisável. Não proponho divisão.** A remoção dos ramos "sem `Api:BaseUrl`" (~−60 de produção no connectors, ~−15 no `apps/frontend`) ficou para a #241 (P1), para não misturar a remoção do HMAC com uma mudança de contrato do connectors e do painel.

## Risks / Trade-offs

- **[Um consumidor esquecido ainda manda HMAC]** → o grep da #235 já provou que nenhum serviço o assina, e a conferência pós-deploy cobre os quatro fluxos. Um HMAC que chegue recebe `401` com `invalid_token`, que aparece no log do destino.
- **[`Identity__Issuer` vazio em produção derruba o inbox ou o connectors]** → o `:?` do `PUBLIC_DOMAIN` no serviço `api` já barra o compose inteiro antes de subir (D4), e a mensagem de boot nomeia a chave (D2).
- **[A fábrica do connectors com o emissor ligado em ~280 hosts muda o tempo da suíte]** → medido contra a baseline, com o limite de 1,5× (D6).
- **[O `503` antes das chaves some para a requisição sem token]** → é a preservação do `401` de hoje (D3). O `503` continua para quem traz token.
- **[Rollback depois de tirar a chave do `.env.prod`]** → a janela de 7 dias (D4), com a data no `02`.
- **[Os casos fora da lista (P3) mostram que a lista não era completa]** → a lista da #236 foi medida com o HMAC recusando tudo e com a varredura dos métodos que citam o HMAC. Os casos da P3 não citam o HMAC: dependem do **emissor desligado**, que só a Q5 elimina. A primeira tarefa do apply mede a Q5 isolada (abaixo), para fechar a lista antes de remover.
- **[A #176 cai numa rodada]** → regra da #176: comentar lá; reprovação nos dois worktrees não bloqueia; só no da change, para.

## Migration Plan

**Ordem do apply** (para o `tasks.md`, depois das respostas):

1. **Medição da Q5 isolada**, num worktree descartável sobre `8decb1e`: o emissor obrigatório (D2) e a fábrica do connectors ligada (D6), sem remover nada. As suítes do inbox e do connectors, com o relatório por nome, contra a baseline. O conjunto reprovado precisa ser exatamente os 2 da Q3, os `WithoutIssuer_…` e os casos da P3. Se reprovar outro, o apply para e traz o nome.
2. `apps/connectors`, `apps/inbox` e `apps/api`, cada um com a suíte e a comparação por nome contra a baseline, menos a lista do D5.
3. `tests/`.
4. Compose, `.env` de exemplo, documentação (D8) e a varredura.
5. `02` e `CHANGELOG.md`, antes do archive.

**Deploy:** D7. **Rollback:** D7.

## Decisões do dono (04/11/2026)

Na revisão deste proposal e deste design, seis das sete perguntas foram aceitas como recomendadas. A P4 foi decidida depois, pela opção A, quando a hipótese H1 caiu na verificação pedida pelo dono.

- **P0.** São 9 reescritos, incluindo o `DocumentWrites_WithOperatorHmacToken_RecordOperatorAsAuthor`. Com a P4, os dois `Hs256…` saem, e **ficam 7** (K6).
- **P1.** Os ramos "sem `Api:BaseUrl`" do connectors, inclusive o `503 sync-not-configured` e o mapeamento dele no painel, **ficam** nesta change. A remoção deles do connectors, das 3 specs e do `apps/frontend` é a **#241**, sem milestone, que cita que ficaram inalcançáveis com a #182 (convenção 25).
- **P2.** Os quatro casos de `CurrentUser` usam o `TestIssuerTokens.Panel` (operator sem usuário), que chega ao `CurrentUser`, com os nomes `OperatorWithoutUser_…` (R6–R9). **Guarda:** o `CurrentUser` retirado da rota faz cada caso reprovar. O comentário da lista na #182 foi corrigido com essa troca e o motivo (K3).
- **P3.** O `ServiceClientConfigurationTests.WithoutTheApiAddress_TheSecretIsNotRequired` sai, com o `IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys` (#208) como substituto. O `DefaultFactory_ResolvesApiBaseUrlAsNotConfigured` é reescrito como `DefaultFactory_ResolvesApiBaseUrlToTheTestIssuer`. **A medição da Q5 isolada é o primeiro passo do apply.**
- **P4. Opção A.** A hipótese H1 caiu na leitura da fonte: o `JsonWebKeySet.cs` da 8.19.2 descarta a chave `oct` por padrão (l. 94, 101 e 183-190; K5). Os dois `Hs256SignedWithTheHmacKey_IsUnauthorized` saem. O `Rs512WithThePublishedKey_IsUnauthorized` fica nomeado como **o guarda da restrição a RS256** nos dois apps. Ele foi visto reprovar sem o `ValidAlgorithms` e aprovar com ele de volta, nos dois (K5). A lista passa a 46 que saem, 7 reescritos e os 2 da Q3.
- **P5.** A lista de exceções da varredura do D8, como proposta. Sem a reescrita dos `Hs256…`, sai dela o `SecurityAlgorithms.HmacSha256`.
- **P6.** Quem chega **sem token** recebe `401` com `WWW-Authenticate: Bearer`, mesmo antes das chaves. Quem chega **com token** recebe `503` antes das chaves (D3). **Guarda:** sem a condição, o `WithoutToken_BeforeTheKeys_Is401_WithTheBearerChallenge` reprova.

## Open Questions

Respondidas em "Decisões do dono", acima. O texto original de cada pergunta fica abaixo, como registro.

Cada uma com a recomendação.

- **P0. 9 reescritos, e não 8.** A lista publicada marca 9 (K6), contando o `DocumentWrites_WithOperatorHmacToken_RecordOperatorAsAuthor`. **Recomendo: aplicar os 9**, como a lista diz. Tirar o `DocumentWrites` deixaria sem caso o autor da edição e da exclusão. **Alternativa:** removê-lo, com perda de cobertura registrada.
- **P1. Os ramos "sem `Api:BaseUrl`" do connectors, inalcançáveis com a Q5** (K2): o aviso do `ValidateApiConfiguration`, a dispensa do segredo, o registro condicional do agendador e o `503 sync-not-configured`, que é contrato e tem mapeamento no painel.
  - **Recomendo:** não mexer neles nesta change, e abrir uma issue para tirar o `sync-not-configured` do contrato do connectors, das specs `connectors-api`, `knowledge-sync-cycle` e `knowledge-base-sync-state-ui` e do `apps/frontend`. A #182 fica como remoção do HMAC, e a mudança de contrato do painel é revisada à parte. O custo é a convenção 25 adiada por uma issue.
  - **Alternativa:** remover tudo aqui. Entram ~−60 de produção no connectors, o frontend e a suíte dele. Os casos unitários `ApiConfigurationValidationTests.Validate_WithoutBaseUrl_DoesNotThrowAndWarns` (3) e o `sync-not-configured` do `SyncCodesTests` também saem ou mudam, fora da lista.
- **P2. O token dos 4 casos de `CurrentUser`** (K3).
  - **Recomendo:** `TestIssuerTokens.Panel` (cliente `buteco-painel`, subject `operator` e `sub` `fixture-sem-usuario`, que não é usuário). É exatamente o estado do HMAC `operator` de hoje: passa na tabela de subjects e para no `CurrentUser`. Mantém a asserção e o defeito já registrado em cada caso. Os nomes passam de `OperatorHmac_…` a `OperatorWithoutUser_…`. Na #236, a Q2 o achou "estado artificial". Mas, sem o HMAC, ele é o único token que chega ao `CurrentUser` sem usuário, e o ramo `Guid.TryParse` falho do `CurrentUser` só é provado por ele.
  - **Alternativa:** o `buteco-servico-inbox`, como a lista publicada diz. Os casos passam a guardar a tabela de subjects, que já tem o `ServiceClientRouteTests`, e o "`CurrentUser` retirado da rota" fica sem caso nas quatro rotas. Isso reduz cobertura.
- **P3. Casos fora da lista que a Q5 alcança** (K4), no connectors:
  - `ServiceClientConfigurationTests.WithoutTheApiAddress_TheSecretIsNotRequired`: o estado "sem `Api:BaseUrl`" deixa de subir. **Recomendo: sai**, com o `IssuerWithoutApiBaseUrl_…` como substituto, igual aos 2 da Q3.
  - `ApiConfigurationValidationTests.DefaultFactory_ResolvesApiBaseUrlAsNotConfigured`: guarda da fixture, para que nenhum teste ligue o ciclo contra o `localhost:5017` do `appsettings.Development.json`. **Recomendo: reescrever** como `DefaultFactory_ResolvesApiBaseUrlToTheTestIssuer`, que afirma o `TestIssuer.InternalBaseUrl`. Ele é visto reprovar sem o `UseSetting` do `Api:BaseUrl`, que é o mesmo defeito de hoje.
  - A medição da Q5 (Migration Plan, passo 1) pode achar outros. Se achar, trago por nome antes de seguir.
- **P4. A recusa de HS256 nos dois `Hs256SignedWithTheHmacKey_IsUnauthorized`** (K5).
  - **Recomendo:** o `TestIssuer` do caso publica também uma chave `oct` no JWKS, e o token HS256 é assinado com ela. Nome: `Hs256SignedWithAKeyFromTheJwks_IsUnauthorized`. Assim o validador **conhece** a chave, e só o `ValidAlgorithms = [RS256]` recusa o token. O caso é visto reprovar sem a restrição. Depende de uma hipótese (H1): o `JsonWebKeySet` da IdentityModel 8.19.2 entrega a chave `oct` como chave de assinatura. É a primeira tarefa do apply. Se H1 for falsa, nenhum defeito alcançável faz o caso reprovar, e eu paro e trago a escolha entre removê-lo e mantê-lo como documentação.
  - **Alternativa:** a reescrita da lista ("outra chave simétrica"). O caso fica verde contra qualquer defeito, como já é hoje.
- **P5. Exceções da varredura** (D8): o histórico (`CHANGELOG`, `02`, `archive/`), as primitivas criptográficas que não são o token (TOTP, PBKDF2, `Aes256CbcHmacSha512`, o HS256 de R2/R3), as seções de deploys passados do `docs/deployment.md`, marcadas "(histórico)", e as notas históricas em itálico do `01`. **Recomendo esta lista.** **Alternativa:** reescrever as seções passadas do runbook sem o termo, o que apaga o registro do que cada deploy fez.
- **P6. O `401` sem token antes das chaves** (D3, K1). **Recomendo a condição do D3**, que preserva o comportamento de hoje. **Alternativa:** aceitar o `503` antes das chaves para a requisição sem token, e fazer as fixtures esperarem as chaves. Isso muda comportamento alcançável e o texto do `01`.
