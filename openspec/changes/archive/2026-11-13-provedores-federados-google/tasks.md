Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-189.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-187.sh`, com os projetos `api`, `rt-inbox` e `rt-connectors`, e com o mesmo lock das suítes. A mutação toca só o trecho nomeado e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação e reprova por "no tests" não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/189-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-189.sh` (`api`, `rt-inbox`, `rt-connectors`), uma por vez, com o lock, e com o RabbitMQ de pé.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 8.
- O `rt-inbox` tem as intermitentes da #176 (`Source3_…` e, desde a baseline desta change, `Question2_WorkerFailurePath_…`): se aparecerem, o comentário vai na #176. Reprovação válida nos dois worktrees não bloqueia; só no da change, é parada.

**A baseline** está em `~/.cache/buteco-agents/189/baseline-final/`, em `3de31dd`, com a árvore limpa: `api` 1696 + 1 ignorado (CPU mínima 60), `rt-inbox` 16/16 (CPU 100), `rt-connectors` 12/12 (CPU 100), todas `APROVADA`, e o relatório por nome em `nomes.txt` (1725 linhas). Detalhes no design ("Baseline").

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Nada de commit parcial.** O trabalho fica na árvore até o dono pedir o commit.

**Sem tela.** A change não toca o `apps/frontend`; se passar a precisar, é parada.

**O login, o authorize, a troca do código e a renovação não mudam** (Q1). Nenhuma tarefa toca o `AuthorizeEndpoint`, o `UserGrantTokenHandler` nem as rotas `/auth/session*`, além de o retorno federado gravar o cookie pendente pelo `IssuerSession.SignInAsync` que já existe.

**Casos que saem ou mudam de asserção**, porque o requisito muda nesta change (delta specs). Nenhum sai. Todo outro caso existente fica sem remoção, renome nem afrouxamento:
- `apps/api`, **muda a lista esperada:** `RouteAuthenticationTests.TheAuthEntryPoints_AreTheKnownOnes` (l. 106–131) ganha `/auth/federated/{chave}/start`, `/auth/federated/callback`, `/auth/federated/providers` e `/auth/federated/refusal`. A asserção continua exata. Requisito: `route-authentication`, "Rotas anônimas do login federado";
- `apps/api`, **muda a matriz esperada:** `RoleMatrixTests.Expected` ganha `GET /identity/federated-providers` e `DELETE /users/{id:guid}/federated/{chave}` (só `admin`), e `POST /account/federated/{chave}/connect` e `DELETE /account/federated/{chave}` (os três papéis). Com elas mudam os resultados de `TheProductionTable_IsTheExpectedMatrix` (mesma asserção) e de `EachRoute_ForEachRole_FollowsTheTable` (12 resultados novos); o `SendAsync` do guarda ganha a substituição de `{chave}`, só arranjo. Requisito: `route-roles`, "Matriz de papéis por rota nos três apps";
- `apps/api`, **muda a lista, asserção igual:** a allowlist de rotas anônimas do `Program.cs` (l. 269–278) ganha as rotas federadas. Requisito: `route-authentication`, "Endpoints do emissor OIDC são anônimos, classificados e conferidos no boot". **Corrigido no apply (registrado):** o `IdentityIssuerEndpoints.Expected` não ganhou o retorno, que foi para um dicionário próprio do cliente (`ExpectedClient`, 3.4), e o `NginxRouteCoverageTests` ficou como estava (o retorno é rota mapeada sob `/auth`, que ele já confere): um item a menos, sem caso fora da lista;
- `apps/api`, **só arranjo:** os casos do `CallerPassword` que montam o corpo com `senha` continuam iguais; os records de pedido das seis rotas ganham `Codigo`, opcional. Requisito: `user-access-links`, "Criar convite, gerar novo link e gerar redefinição pedem a senha de quem chama".

Cada um é listado por nome na comparação da 12.3, com o requisito que o mudou. Um caso fora desta lista que precise sair ou afrouxar é parada.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-189.sh`, cópia do `guarda-187.sh` com os projetos `api`, `rt-inbox` e `rt-connectors`, com o lock das suítes. Conferir com uma mutação nula (recusa com "defeito não aplicado") e uma real em `apps/api/src` (o arquivo volta com o mesmo sha).
  **Feito:** `guarda-189.sh` = `guarda-187.sh` sem o `rt-crossapp` (diff: o cabeçalho, o uso e a linha do `case`). Nula: "defeito não aplicado"; real (comentário em `Identity/IdentityTokenLifetimes.cs`): `AdvisoryLockKeysTests` 6/6, voltou com o sha `1c893d96…`.
- [x] 0.2 Reler no fonte do OpenIddict 7.7.2 (`~/.cache/buteco-agents/182-fonte/openiddict-core`) e registrar no design qualquer diferença do D1, D4 e D11 (convenção 6):
  - o `CookieBuilder` do `OpenIddictClientAspNetCoreOptions`: o nome com o sufixo do nonce, o `Path` e a expiração pelo state token;
  - o redirection endpoint em passthrough (`EnableRedirectionEndpointPassthrough`) e o que a rota recebe (`AuthenticateAsync` do esquema do cliente): o principal, as propriedades do Buteco e o erro;
  - o `RedeemStateTokenEntry` com o `AddCore().UseEntityFrameworkCore()` que já existe: o tipo de token gravado e a ligação a autorização;
  - o WebIntegration do Google: o `ProviderName`, o `prompt`, o userinfo e o `DisableIssuerParameterValidation`;
  - como trocar o handler primário do `HttpClient` do `OpenIddict.Client.SystemNetHttp` nos testes (o duplo do D11).
  **Feito, com quatro diferenças registradas no design (D15):** a validação do retorno acontece no `AuthenticateAsync` da rota, e os erros só chegam a ela com `EnableErrorPassthrough` (APPLY-8); a URI comparada com a `redirect_uri` sai do host da requisição (APPLY-5); o handler primário do `HttpClient` tem de ser `HttpClientHandler`, e o duplo entra como `DelegatingHandler` na frente dele; o `response_mode` não vai no pedido (APPLY-9). O resto conferiu: o cookie com o sufixo do nonce, o state token de uso único em `openiddict_tokens` (tipo `urn:openiddict:params:oauth:token-type:state_token`), o builder do Google (`SetProviderName`, `SetRegistrationId`, `AddScopes`, `SetPrompt`).

## 1. Pacotes, opções e checagem de boot (apps/api; `federated-providers`, D2, D3)

- [x] 1.1 `OpenIddict.Client.AspNetCore`, `OpenIddict.Client.SystemNetHttp` e `OpenIddict.Client.WebIntegration` 7.7.2 no `Directory.Packages.props` e no `Buteco.Api.csproj`. `dotnet build` sem aviso novo.
  **Feito.** O `Microsoft.Extensions.Configuration` central subiu de 10.0.10 para 10.0.11: o cliente puxa o `Configuration.Binder` 10.0.11, e a restauração dos testes do `apps/api` reprovava por downgrade (NU1605). Só o `Buteco.Api.Tests` e o `libs/ProviderCatalog.Tests` o referenciam; os dois compilam (APPLY-1). Sem aviso novo.
- [x] 1.2 `Identity/Federated/FederatedProviderOptions.cs`: a leitura da seção, o tipo `google` com o que é fixo (issuer, nome, escopos, `prompt`, `response_mode`), o `HostedDomain`. Sem segredo em `ToString` nem em log.
  **Feito:** `Identity/Federated/FederatedProviders.cs` (os tipos, o `FederatedProvider` sem o segredo no `ToString`, o `FederatedProviderCatalog`, que lê sem lançar).
- [x] 1.3 `ValidateFederatedProvidersConfiguration` com as dez regras e as mensagens do D3, chamada no `Program.cs` ao lado das outras checagens do emissor. Sem banco e sem rede.
  **Feito:** `Identity/Federated/FederatedProvidersStartupValidation.cs`, chamada no `Program.cs` depois da `ValidateIdentityIssuerConfiguration`. A regra 10 ganhou mensagem própria ("…ClientSecret igual a <nome>: o segredo do provedor precisa ser distinto…"): a `DistinctKeysMessage` é da chave das 2 etapas (APPLY-2).
- [x] 1.4 Um caso por regra, no molde do `IdentityIssuerStartupValidationTests` (composição real, mensagem citada, o valor do segredo ausente da mensagem), mais a seção vazia e o provedor fora do ar (sobem).
  **Feito:** `FederatedProvidersStartupValidationTests`, 15/15.
- [x] 1.5 **(guarda)** "Segredo no arquivo JSON derruba o boot", com a variante "o mesmo segredo pela variável passa". **Defeito:** a regra 7 retirada da checagem.
  **Guarda feito:** `WithTheSecretInAJsonFile_FailsNamingTheSetting_WithoutTheValue` reprovou ("Assert.NotNull() Failure: Value is null"; o caso pela variável passou); voltou `Identity/Federated/FederatedProvidersStartupValidation.cs` (`a50f8ea2…`).
- [x] 1.6 **(guarda)** "Dois provedores Google derrubam o boot". **Defeito:** a regra 6 retirada.
  **Guarda feito:** `WithTwoGoogleProviders_FailsNamingBothAndTheIssuer` reprovou ("Assert.NotNull() Failure: Value is null"); voltou `a50f8ea2…`.
- [x] 1.7 **(guarda)** "Campo com erro de digitação derruba o boot". **Defeito:** a regra 8 retirada (o binder ignora `ClientID` em silêncio).
  **Guarda feito:** `WithAMistypedField_FailsNamingTheField` reprovou ("Assert.NotNull() Failure: Value is null"); voltou `a50f8ea2…`.

## 2. O vínculo no banco (apps/api; `federated-login`, D6)

- [x] 2.1 `Users/Federated/UserFederatedIdentity.cs`, com a configuração EF e o `DbSet`: `user_id` FK `Cascade`, `issuer`, `subject`, `email`, `connected_at`, `last_used_at`, `UNIQUE (issuer, subject)` e `UNIQUE (user_id, issuer)`.
  **Feito:** `Users/Federated/UserFederatedIdentity.cs` e a configuração em `AppDbContext`.
- [x] 2.2 Migration `AddUserFederatedIdentities`, só acréscimo. Conferir o SQL e o `Down`.
  **Feito:** `20261113033350_AddUserFederatedIdentities` (só `CreateTable` e os dois índices; o `Down` apaga a tabela).
- [x] 2.3 **(guarda)** "Uma conta do provedor liga um usuário só", gravando direto pelo `DbContext`. **Defeito:** o índice único `(issuer, subject)` retirado da migration.
  **Guarda feito:** `UserFederatedIdentityStorageTests.OneProviderAccount_LinksOneUserOnly` reprovou ("Assert.IsType() Failure: Value is null / Expected: typeof(DbUpdateException)"); voltou a migration (`4b85df11…`). O defeito foi na migration, e não na configuração: o banco do teste é migrado pela migration (como a 1.4 da #187).

## 3. O cliente OIDC (apps/api; `federated-login`, `route-authentication`, D1, D4)

- [x] 3.1 `AddClient()` no `AddIdentityIssuer`: uma registração do WebIntegration por chave configurada (`ProviderName` = a chave), `redirect_uri` `<PublicUrl:BaseUrl>/auth/federated/callback`, `SetStateTokenLifetime(10 min)`, o redirection endpoint em passthrough. Sem provedor configurado, o cliente não registra nada.
  **Feito.** O cliente só é registrado com provedor configurado (APPLY-3): sempre registrado, o `IdentityIssuerEndpointsPassthroughTests.WithBothRoutesAnonymousAndClassified_Passes`, fora da lista do topo, reprovava, porque o host do caso não mapeia o retorno; sem provedor, nada do cliente entra. A URI de retorno é a base do painel (APPLY-4). Mais o `FederatedRequestUriHandler` (APPLY-5) e o `EnableErrorPassthrough` (APPLY-8).
- [x] 3.2 As credenciais de assinatura e de cifra do cliente pelo anel do emissor: um `IConfigureOptions<OpenIddictClientOptions>` no molde do `IdentityKeyRingOptionsConfigurer`, com o mesmo `IOptionsChangeTokenSource`. Nenhuma chave nova.
  **Feito:** `Identity/Keys/IdentityKeyRingClientOptionsConfigurer.cs`, com o change token.
- [x] 3.3 O cookie do fluxo: o `CookieBuilder` com o nome `buteco-fluxo-externo`, `SameSite=Lax`, `HttpOnly`, `Path=/auth/federated/callback`, `SecurePolicy` como o do `buteco-sessao` (`Always` fora de `Development`).
  **Feito** pelo `OpenIddictClientAspNetCoreOptions.CookieBuilder`.
- [x] 3.4 `IdentityIssuerEndpoints.Expected` com `/auth/federated/callback` (passthrough, `AuthEntryPoint`), e a checagem do boot conferindo também as URIs do cliente, nos dois sentidos. Caso: "Retorno do cliente sem rota derruba o boot".
  **Feito:** `IdentityIssuerEndpoints.ExpectedClient` e a conferência no `ValidateIdentityIssuerEndpoints`, com mensagens próprias do cliente; as do servidor não mudaram. Os testes de boot existentes: 43/43. Com o dicionário à parte, o `NginxRouteCoverageTests` não mudou (o retorno é rota mapeada sob `/auth`, que ele já confere): um item a menos na lista do topo.
- [x] 3.5 **(guarda)** "Atributos do cookie" (`Lax`, `Path`, 10 min, sem o `code_verifier` nem o `retorno` no valor). **Defeito:** (a) `SameSite=None`; (b) `Path=/`.
  **Guarda feito:** `Start_ReturnsTheGoogleUrl_AndTheFlowCookie` reprovou nos dois defeitos ("Assert.Contains() Failure: Sub-string not found"); voltou `IdentityIssuerServiceCollectionExtensions.cs` (`57c121e1…`) nas duas.

## 4. O duplo do Google (testes do apps/api; D11)

- [x] 4.1 `Support/FakeGoogle.cs`: o `HttpMessageHandler` posto no `HttpClient` do cliente, respondendo por `https://accounts.google.com/` ao discovery, ao JWKS (chave RSA do teste), ao token (confere o `code_verifier` contra o `code_challenge` do pedido; código de uso único) e ao userinfo. Um construtor do id token com cada desvio: `iss`, `aud`, `nonce`, chave de assinatura, `email_verified`, `hd`, `exp`, e o `error=access_denied` no lugar do código.
  **Feito:** `Support/FakeGoogle.cs` (um `DelegatingHandler` na frente do `HttpClientHandler` primário, que o OpenIddict exige).
- [x] 4.2 `Support/GoogleFlow.cs`, no molde do `IssuerUserFlow`: começa pelo `start`, lê o `url`, tira `state`, `nonce` e `code_challenge`, registra o código no duplo e chama o retorno com os cookies; devolve a resposta e os cookies.
  **Feito:** `Support/GoogleFlow.cs`, com o `FederatedLoginFixture` e o `HostedDomainFederatedLoginFixture`. O `GoogleFlow.Client` é anônimo: o fixture anexa o token do painel a todo `CreateClient`.
- [x] 4.3 O duplo testado por si: um pedido sem o `code_verifier` certo recebe `invalid_grant`, e o mesmo código não vale duas vezes. Sem isso, os guardas do PKCE passariam contra um duplo que aceita tudo.
  **Feito:** `FakeGoogleTests` 3/3 (o verifier certo devolve o id token e o código vale uma vez; o verifier errado é `invalid_grant`; o segredo errado é `invalid_client`).

## 5. Início, retorno e recusas (apps/api; `federated-login`, `issuer-user-login`, D1, D5, D7)

- [x] 5.1 `POST /auth/federated/{chave}/start` (anônima, `AuthEntryPoint`, barreiras do login, limite global): `{ retorno? }` conferido pelo prefixo, `200 { url }`, `404` "Provedor não configurado.".
  **Feito:** `Identity/Federated/FederatedLoginEndpoints.cs` e `FederatedChallenge.cs` (o `302` do desafio vira `200 { url }`, com o `Set-Cookie` na mesma resposta).
- [x] 5.2 `GET /auth/federated/callback` em passthrough: lê o resultado do cliente; `falha` com o log 1199 em toda recusa do cliente; a decisão na ordem do D7; a entrada grava o cookie pendente com `IssuerSession.SignInAsync(…, twoFactorDone: false)` e responde `302 /entrar?federado=…&segundoPasso=…[&retorno=…]`; o vínculo regrava `email` e `last_used_at`.
  **Feito.**
- [x] 5.3 As recusas: `302 /entrar?recusa=<motivo>&provedor=<chave>`, o log 1198, o cookie do fluxo apagado, nenhuma falha no bloqueio por e-mail.
  **Feito.**
- [x] 5.4 O `buteco-recusa` (Data Protection, propósito `Buteco.FederatedRefusal`; `HttpOnly`, `Strict`, `Path=/auth/federated/refusal`, `Max-Age=120`) nas recusas `convite-pendente`, `nao-conectada` e `sem-acesso`, e `POST /auth/federated/refusal` (anônima, barreiras do login): `200` uma vez e apaga; `404` vazio nos outros casos.
  **Feito:** `FederatedRefusalCookie.cs`. Acrescentado no apply: um id de uso único, guardado no `IMemoryCache` pelo prazo do cookie; sem ele, o mesmo valor reenviado em 120 s responderia `200` de novo (APPLY-6).
- [x] 5.5 Os casos de cada cenário de `federated-login` ("Início do login federado", "O cookie do fluxo externo", "Decisão do login no retorno", "O e-mail confirmado da recusa") e de `issuer-user-login` ("A sessão pendente também nasce do retorno"): o segundo passo e o cadastro completam a sessão com o cookie do retorno, e o authorize seguinte emite o código.
  **Feito:** `FederatedLoginTests` (25 casos) e `FederatedLoginHostedDomainTests` (3), inclusive a limpeza do state morto (`issuer-token-storage`).
- [x] 5.6 **(guarda)** "O par decide, e não o e-mail" e "O e-mail mudou no provedor". **Defeito:** a decisão achando o usuário pelo `NormalizedEmail` do e-mail confirmado, em vez do par.
  **Guarda feito:** `ThePairDecides_NotTheEmail` ("Expected: nao-conectada Actual: null") e `TheEmailChangedAtTheProvider_EntersAsTheSameUser_AndOnlyTheLinkChanges` ("Expected start: /entrar?federado=") reprovaram; voltou `FederatedLoginEndpoints.cs` (`e81b8cb5…`).
- [x] 5.7 **(guarda)** "O cookie pendente do retorno não emite código" (o authorize com o cookie do retorno responde `302 /entrar`). **Defeito:** o retorno gravando a sessão completa (`twoFactorDone: true`): o login pelo Google entrando sem o código.
  **Guarda feito:** `LinkedActive_WithTwoFactor_GetsThePendingSession_AndTheCodeCompletesIt` reprovou ("Expected start: /entrar": o authorize emitiu o código com o cookie do retorno); voltou `e81b8cb5…`.
- [x] 5.8 **(guarda)** "Cookie de outro fluxo não serve" e "Retorno sem o cookie do fluxo é recusado". **Defeito:** a conferência do valor anti-falsificação retirada (o handler `ValidateRequestForgeryProtection` removido da lista do cliente): o cookie aceito sem bater com o state.
  **Guarda feito:** `Return_WithoutTheFlowCookie_IsRefused` e `Return_WithTheCookieOfAnotherFlow_IsRefused` reprovaram ("Expected: falha Actual: null"), com o `ResolveRequestForgeryProtection` e o `ValidateRequestForgeryProtection` removidos (só o segundo não basta: o primeiro recusa o cookie ausente); voltou `IdentityIssuerServiceCollectionExtensions.cs` (`57c121e1…`).
- [x] 5.9 **(guarda)** "State token usado uma vez só". **Defeito:** `DisableTokenStorage()` no cliente.
  **Guarda feito:** `Return_Replayed_IsRefused` reprovou ("Expected: falha Actual: null"); voltou `57c121e1…`.
- [x] 5.10 **(guarda)** "Nenhuma recusa grava sessão" (os sete motivos, sem `Set-Cookie: buteco-sessao`, e o authorize seguinte manda a `/entrar`). **Defeito:** o `SignInAsync` movido para antes da decisão.
  **Guarda feito:** `NoRefusal_WritesASession` reprovou ("Assert.Null() Failure: Value is not null … buteco-sessao=…"); voltou `e81b8cb5…`.
- [x] 5.11 **(guarda)** "O log não leva e-mail nem sub" (todos os desfechos, com o log capturado). **Defeito:** o e-mail confirmado acrescentado ao template do log 1198.
  **Guarda feito:** `NoLogLine_CarriesTheEmailOrTheSub` reprovou ("Assert.DoesNotContain() Failure: Filter matched in collection"); voltou `e81b8cb5…`.
- [x] 5.12 **(guarda)** "Id token com iss diferente é recusado" (inclusive `accounts.google.com` sem esquema). **Defeito:** `ValidateIssuer = false` nos parâmetros de validação do cliente.
  **Guarda feito:** `AnIdTokenWithAnotherIssuer_IsRefused` (os dois casos) reprovou ("Expected: falha Actual: null"), com `ValidateIssuer = false` num handler depois do `ResolveTokenValidationParameters`; voltou `57c121e1…`.
- [x] 5.13 **(guarda)** "Usuário desativado sem vínculo não vê 'desativado'". **Defeito:** a recusa `desativado` decidida pelo e-mail, sem o vínculo.
  **Guarda feito:** `DeactivatedWithoutALink_IsNoAccess` reprovou ("Expected: sem-acesso Actual: desativado"); voltou `e81b8cb5…`.
- [x] 5.14 **(guarda)** "Domínio fora do permitido, mesmo vinculado". **Defeito:** a regra do `hd` só para quem não tem vínculo.
  **Guarda feito:** `AnotherDomain_IsRefused_EvenWhenLinked` e `WithoutTheClaim_IsRefused` reprovaram ("Expected: dominio-nao-permitido Actual: null"); voltou `e81b8cb5…`.
- [x] 5.15 **(guarda)** "O retorno nunca manda ao authorize". **Defeito:** o `302` direto ao `retorno`.
  **Guarda feito:** `LinkedActive_WithTwoFactor_…` reprovou ("Expected start: /entrar?"); voltou `e81b8cb5…`.
- [x] 5.16 **(guarda)** "O painel lê o e-mail uma vez" e "A rota não revela nada sem o cookie". **Defeito:** (a) a resposta `200` sem apagar o cookie; (b) `400` para o cookie adulterado, diferente do `404` do ausente.
  **Guarda feito:** (a) `TheRefusalCookie_IsReadOnce` reprovou ("Assert.Contains() Failure: Filter not matched in collection"); (b) `TheRefusalRoute_RevealsNothingWithoutTheCookie` reprovou ("Expected: NotFound Actual: BadRequest"); voltou `FederatedRefusalCookie.cs` (`2119d2ed…`) nas duas.

- [x] 5.17 (APPLY-11, decisão do dono de 13/11/2026) O início, e o do conectar, buscam o discovery do provedor antes do desafio, com o prazo explícito de 10 s (`FederatedLogin.ProviderTimeout`); fora do alcance, `503` com `problem+json` e log 1199. O duplo ganhou os estados `Down` e `Hanging`, com os fixtures `UnreachableGoogleFixture` e `HangingGoogleFixture`.
  **Feito:** `FederatedChallenge.ProviderReachableAsync`; `FederatedProviderUnreachableTests` 2/2, `FederatedProviderHangingTests` 1/1 (o `503` em ~10 s) e `FederatedLoginTests.TheReturn_WithTheProviderDown_IsFailure_WithoutSession` (o retorno continua em `falha`).
- [x] 5.18 **(guarda)** "Provedor fora do alcance no início". **Defeito:** sem a busca prévia do discovery, o `400` em texto do OpenIddict de volta.
  **Guarda feito:** `TheStart_WithTheProviderDown_IsA503ProblemJson_WithoutCookie` reprovou: com o defeito, o OpenIddict escreve o `400` em texto e o desafio aborta, e o servidor de teste devolve "HttpRequestException : Error while copying content to a stream … O desafio do cliente OIDC para o provedor 'corporativo' não respondeu com o redirecionamento ao provedor."; voltou `Identity/Federated/FederatedChallenge.cs` (`8bc05769…`).

- [x] 5.19 (antes do merge, pedido do dono de 13/11/2026) A busca prévia do discovery passa pelo cache do cliente OIDC? Conferido no fonte: sim, o `registration.ConfigurationManager` (`OpenIddictClientConfiguration.cs` l. 122–127; o desafio lê o mesmo, `OpenIddictClientHandlers.cs` l. 520–522). O duplo ganhou o contador `DiscoveryRequests`, e o `DiscoveryCountFixture` começa com o cache vazio.
  **Feito:** `FederatedDiscoveryCacheTests.TwoStartsInARow_FetchTheDiscoveryOnce` (dois inícios, um pedido).
- [x] 5.20 **(guarda)** "O discovery vem do cache". **Defeito:** a busca sem cache (um `ConfigurationManager` novo, com o mesmo `OpenIddictClientRetriever`, a cada início).
  **Guarda feito:** `TwoStartsInARow_FetchTheDiscoveryOnce` reprovou ("Assert.Equal() Failure: Values differ / Expected: 1 / Actual: 3": um pedido por início mais o do desafio); voltou `Identity/Federated/FederatedChallenge.cs` (`8bc05769…`). A primeira tentativa do defeito não compilou (faltou o namespace de `OpenIddictConfiguration`) e foi descartada e refeita.

## 6. A confirmação de quem chama com o código (apps/api; `user-access-links`, `user-management`, `external-applications`, `local-users`, D8)

- [x] 6.1 `CallerPassword.RejectAsync` passa a receber `senha` e `codigo` e o `TwoFactorVerifier`: quem tem senha, só a senha; quem não tem, só o código do app, por um método novo do verificador que aceita só o TOTP (com o `UPDATE` condicional do último passo); `401` "Código inválido."; `500` no segredo que não decifra. O `423` e a contagem, como hoje.
  **Feito:** `CallerPassword.RejectAsync(caller, senha, codigo, hasher, verifier, throttle)` e `TwoFactorVerifier.VerifyAppCodeAsync`.
- [x] 6.2 Os records de pedido das seis rotas ganham `Codigo` (opcional), e as seis chamadas passam os dois campos.
  **Feito** (cinco records, seis chamadas).
- [x] 6.3 Os casos dos cenários novos de `user-access-links`, `user-management` ("Admin sem senha promove com o código do app"), `external-applications` e `local-users`, com o usuário sem senha montado direto no banco (na 189a nenhuma rota o cria; D8).
  **Feito:** `CallerConfirmationTests` 9/9.
- [x] 6.4 **(guarda)** "Quem tem senha não confirma com o código" e "Admin com senha não gera segredo com o código". **Defeito:** o código aceito para todo mundo (sem conferir se há senha gravada).
  **Guarda feito:** os dois casos reprovaram ("Expected: Unauthorized Actual: Created"); voltou `Users/AccessLinks/CallerPassword.cs` (`8777167a…`).
- [x] 6.5 **(guarda)** "Código de recuperação não confirma". **Defeito:** a confirmação pelo `VerifyAsync` de hoje, que aceita o de recuperação.
  **Guarda feito:** `ARecoveryCode_DoesNotConfirm_AndStaysUnused` reprovou ("Expected: Unauthorized Actual: Created"); voltou `8777167a…`.
- [x] 6.6 **(guarda)** "O código do app não vale duas vezes". **Defeito:** a conferência do TOTP sem o `UPDATE` condicional do último passo.
  **Guarda feito:** `TheAppCode_DoesNotWorkTwice` reprovou ("Expected: Unauthorized Actual: Created"); voltou `Users/TwoFactor/TwoFactorVerifier.cs` (`f13f2203…`).
- [x] 6.7 **(guarda)** "Login por senha de quem não tem senha" (o `401` genérico, a falha contada). **Defeito:** a verificação de senha nula contra o hash fictício trocada por um `return Failed` antes do cálculo, sem contar a falha.
  **Guarda feito:** `PasswordLogin_OfWhoHasNoPassword_IsTheGenericRefusal_AndCounts` reprovou ("Expected: 1 Actual: 0"); voltou `Users/Login/SessionEndpoints.cs` (`ecff3624…`).

## 7. Conectar e desconectar (apps/api; `federated-login`, D6)

- [x] 7.1 `POST /account/federated/{chave}/connect`: a confirmação de quem chama, `200 { url }`, a intenção `conectar` com o id e o stamp no state token; o retorno do conectar na ordem da spec, `302 /account?conectado=…` ou `/account?recusa=…`; o log 1200.
  **Feito:** `Users/Federated/FederatedAccountEndpoints.cs` (a chave é conferida antes da confirmação: um `404` não gasta o código de ninguém).
- [x] 7.2 `DELETE /account/federated/{chave}` e `DELETE /users/{id}/federated/{chave}`: a regra da última forma de entrada sob `SELECT … FOR UPDATE` na mesma transação; no do Admin, a troca do stamp e a revogação das sessões (`UserSessions`) na mesma transação, o `403` a si mesmo; o log 1201.
  **Feito.**
- [x] 7.3 Os casos de "Conectar o provedor em Minha conta", "Desconectar pela própria pessoa", "Desconectar pelo Admin" e "Vínculo de provedor que saiu da configuração".
  **Feito:** `FederatedConnectTests` 15/15. O cenário "Duas desconexões ao mesmo tempo" foi trocado por "A conferência trava a linha do usuário" (APPLY-7, ponto para o dono no design).
- [x] 7.4 **(guarda)** "Só o provedor não sai" e "Vínculo de provedor fora da configuração não conta". **Defeito:** (a) a conferência da última forma retirada; (b) a contagem incluindo o vínculo de provedor fora da configuração.
  **Guarda feito:** (a) `OnlyTheProvider_DoesNotLeave` reprovou ("Expected: Conflict Actual: NoContent"); voltou `FederatedAccountEndpoints.cs` (`a8970c45…`); (b) `ALinkOfAProviderOutOfTheConfiguration_DoesNotCount` reprovou ("Expected: Conflict Actual: NoContent"); voltou `Users/Federated/FederatedSignInForms.cs` (`b3ce4a02…`).
- [x] 7.5 **(guarda)** "Duas desconexões ao mesmo tempo" (uma `204`, uma `409`, e a consulta emitida pede o `FOR UPDATE` antes de apagar). **Defeito:** o `FOR UPDATE` retirado.
  **Guarda feito, sobre o cenário trocado (APPLY-7):** `TheCheck_LocksTheUserRow_BeforeTheDelete` reprovou ("a consulta não pediu o FOR UPDATE da linha do usuário"); voltou `a8970c45…`.
- [x] 7.6 **(guarda)** "Admin desconecta e as sessões caem". **Defeito:** a desconexão pelo Admin sem trocar o stamp.
  **Guarda feito:** `TheAdminDisconnects_AndTheSessionsFall` reprovou ("Assert.NotEqual() Failure: Strings are equal"); voltou `a8970c45…`.
- [x] 7.7 **(guarda)** "Desconectar a si mesmo mantém as sessões". **Defeito:** a desconexão pela própria pessoa trocando o stamp.
  **Guarda feito:** `WithAPassword_TheProviderLeaves_AndOwnSessionsStay` reprovou ("Strings differ"); voltou `a8970c45…`.
- [x] 7.8 **(guarda)** "Stamp trocado no meio" e "Conta do provedor de outro usuário". **Defeito:** (a) o retorno do conectar sem conferir o stamp do state token; (b) o vínculo regravado para o usuário novo (upsert pelo par).
  **Guarda feito:** (a) `Connect_WithTheStampChangedInTheMiddle_IsSessionExpired` e (b) `Connect_AnAccountOfAnotherUser_IsInUse` reprovaram ("Actual: /account?conectado=corporativo"); voltou `FederatedLoginEndpoints.cs` (`e81b8cb5…`) nas duas.
- [x] 7.9 **(guarda)** "Conectar sem a confirmação". **Defeito:** o conectar sem chamar a confirmação de quem chama.
  **Guarda feito:** `Connect_WithoutTheConfirmation_IsRefused_AndCounts` reprovou ("Expected: Unauthorized Actual: OK"); voltou `a8970c45…`.

## 8. As rotas dos provedores e as formas de entrada (apps/api; `federated-providers`, `user-management`, `user-account`, `route-roles`, `route-authentication`, D9)

- [x] 8.1 `GET /identity/federated-providers` (Admin pelo papel gravado) e `GET /auth/federated/providers` (anônima), com os campos da spec.
  **Feito.**
- [x] 8.2 `formasDeEntrada` com `"google"` (formato de hoje, strings) em `/users` e `/users/{id}`; `contasConectadas` em `/users/{id}` e `/account`; `formasDeEntrada` em `/account`. O comentário de `UserResponses.cs` l. 7 ("na M1 é a única forma") atualizado.
  **Feito:** `FederatedSignInForms`; o comentário de `UserResponses.cs` atualizado.
- [x] 8.3 `RoleRoutes` com as quatro rotas novas; a allowlist anônima do `Program.cs` e o `TheAuthEntryPoints_AreTheKnownOnes` com as quatro rotas federadas (lista do topo).
  **Feito** (lista do topo): `RoleMatrixTests` e `RouteAuthenticationTests`; `FederatedProviderRoutesTests` 5/5; as classes federadas, a matriz e as rotas anônimas: 329/329.
- [x] 8.4 **(guarda)** "Admin lê os provedores sem segredo" (o corpo não contém o valor do segredo nem o `HostedDomain`). **Defeito:** o record da resposta com o `ClientSecret`.
  **Guarda feito:** `TheAdmin_ReadsTheProviders_WithoutTheSecret` reprovou ("Assert.DoesNotContain() Failure: Sub-string found"); voltou `a8970c45…`.
- [x] 8.5 **(guarda)** "Só o Admin lê os provedores e desconecta outro usuário" (a matriz). **Defeito:** (a) `GET /identity/federated-providers` com `All`; (b) `DELETE /users/{id}/federated/{chave}` com `Operate`.
  **Guarda feito:** (a) e (b) reprovaram `RoleMatrixTests.TheProductionTable_IsTheExpectedMatrix` e os `EachRoute_ForEachRole_FollowsTheTable` da rota mudada; voltou `Auth/RoleRoutes.cs` (`e358a4c4…`) nas duas.
- [x] 8.6 **(guarda)** "O login lê só chave e tipo". **Defeito:** a rota anônima devolvendo o record da rota do Admin.
  **Guarda feito:** `TheLogin_ReadsOnlyKeyAndType` reprovou ("Collections differ … [chave, clientId, issuer, tipo]"); voltou `e81b8cb5…`.
- [x] 8.7 **(guarda)** "Vínculo com o provedor" (`["senha", "google"]` e `["google"]`). **Defeito:** o `formasDeEntrada` trocado por objetos (o contrato que o painel lê).
  **Guarda feito:** `TheSignInForms_AreStrings_InTheListTheDetailAndTheAccount` reprovou ("The requested operation requires an element of type 'String', but the target element has type 'Object'"); voltou `Users/Management/UserResponses.cs` (`885489a6…`).

## 9. Acordos

- [x] 9.1 (`tests/InboxOrchestratorRoundTrip.Tests` e `tests/ApiConnectorsRoundTrip.Tests`) Sem mudança de código: o login por senha do `IssuerUserFlow` e o boot do host real sem provedor configurado continuam como na baseline. Rodam no fechamento.
  **Feito** no fechamento (12.2): `rt-inbox` 16/16 e `rt-connectors` 12/12, sem mudança de código.
- [x] 9.2 (`apps/api`, emissor real em processo, com o duplo) De ponta a ponta, sem semeador: um Admin entra por senha, conecta o provedor pela rota (com a senha), sai; entra pelo provedor, passa pelo segundo passo, troca o código no `/connect/token`; o access token tem o `iss`, o `aud`, o `client_id` e o `sub` do login por senha; `GET /account` mostra o vínculo.
  **Feito:** `FederatedEndToEndTests.ConnectByTheRoute_ThenEnterByTheProvider_WithTheButecoCode`.

## 10. Docs

- [x] 10.1 `docs/configuration.md`: a seção `Identity:FederatedProviders`, os campos, o que é fixo pelo tipo, o segredo só por variável, as dez regras do boot.
  **Feito.**
- [x] 10.2 `docs/deployment.md`: o cliente OAuth no Google Cloud (a tela de consentimento, o cliente "Aplicativo da Web", as URIs, sem origem JavaScript); o `federated-providers.env` (permissão 600, fora do repositório); **Docker Compose 2.24.0 ou mais novo**, com `docker compose version` no primeiro passo e a alternativa do arquivo sempre presente para versão anterior (D13); a migration; a conferência; a volta atrás.
  **Feito:** "Provedores federados (#189)".
- [x] 10.3 `docker-compose.prod.yml`: o `env_file` opcional só no `api`; `federated-providers.env.example` sem segredo; o comentário no `.env.prod.example`. Conferir o arquivo com `docker compose config` (ou `podman-compose`) com e sem o arquivo.
  **Feito:** o `env_file` no `api`, o `federated-providers.env.example`, o comentário no `.env.prod.example` e o `federated-providers.env` no `.gitignore`. O `podman-compose config` processou o arquivo com e sem o `federated-providers.env`, e o `podman-compose run` com `required: false` (12/11) entregou a variável só com o arquivo presente.
- [x] 10.4 `01-ARQUITETURA_E_CONVENCOES.md`, seção "Autenticação": o cliente OIDC atrás do emissor, o cookie do fluxo, o cookie pendente pela cadeia cross-site, o vínculo, as recusas, a confirmação de quem chama com o código, as rotas novas, os `EventId` 1198–1201, o state token em `openiddict_tokens`.
  **Feito.**
- [x] 10.5 Antes do archive: `02-HISTORICO_E_STATUS.md` (a entrada da change e a fila da M3 com #189, #274, #190) e `CHANGELOG.md`, com a medição da convenção 18 contra a projeção do design, em colunas separadas para produção, testes, duplos e casos.
  **Feito:** `02-HISTORICO_E_STATUS.md` ("`provedores-federados-google`", com a fila da M3) e `CHANGELOG.md` (Added e Changed).

## 11. Lista dos guardas (conferência)

| tarefa | caso | defeito |
|---|---|---|
| 1.5 | segredo no arquivo JSON derruba o boot | a regra 7 retirada |
| 1.6 | dois provedores Google derrubam o boot | a regra 6 retirada |
| 1.7 | campo com erro de digitação derruba o boot | a regra 8 retirada |
| 2.3 | uma conta do provedor liga um usuário só | o índice único `(issuer, subject)` retirado |
| 3.5 | atributos do cookie do fluxo | (a) `SameSite=None`; (b) `Path=/` |
| 5.6 | o par decide, e não o e-mail | o vínculo decidido pelo e-mail |
| 5.7 | o cookie do retorno não emite código | o login pelo Google entrando sem o código (sessão completa no retorno) |
| 5.8 | cookie de outro fluxo, retorno sem cookie | o cookie aceito sem bater com o state |
| 5.9 | state token de uso único | `DisableTokenStorage()` |
| 5.10 | nenhuma recusa grava sessão | o `SignInAsync` antes da decisão |
| 5.11 | o log não leva e-mail nem sub | o e-mail no template do log |
| 5.12 | id token com `iss` diferente | `ValidateIssuer = false` |
| 5.13 | desativado sem vínculo cai em `sem-acesso` | `desativado` pelo e-mail |
| 5.14 | `hd` mesmo vinculado | a regra do `hd` só sem vínculo |
| 5.15 | o retorno nunca manda ao authorize | `302` direto ao `retorno` |
| 5.16 | a rota da recusa: uso único, nada sem o cookie | (a) não apaga; (b) status diferente para o adulterado |
| 5.18 | provedor fora do alcance no início → `503` (APPLY-11) | sem a busca prévia: o `400` em texto de volta |
| 5.20 | o discovery vem do cache (dois inícios, um pedido) | a busca sem cache |
| 6.4 | quem tem senha não confirma com o código | o código aceito de quem tem senha |
| 6.5 | código de recuperação não confirma | o `VerifyAsync` de hoje |
| 6.6 | o código do app não vale duas vezes | sem o `UPDATE` condicional |
| 6.7 | login por senha de quem não tem senha | sem o hash fictício e sem contar a falha |
| 7.4 | a última forma de entrada não sai | (a) sem a conferência; (b) contando provedor fora da configuração |
| 7.5 | a conferência trava a linha do usuário (cenário trocado no apply, APPLY-7) | sem o `FOR UPDATE` |
| 7.6 | o Admin desconecta e as sessões caem | sem trocar o stamp |
| 7.7 | desconectar a si mesmo mantém as sessões | trocando o stamp |
| 7.8 | stamp trocado no meio; conta de outro usuário | (a) sem conferir o stamp; (b) upsert pelo par |
| 7.9 | conectar sem a confirmação | sem chamar a confirmação |
| 8.4 | a rota do Admin sem segredo | o record com o `ClientSecret` |
| 8.5 | a matriz das rotas novas | (a) `All`; (b) `Operate` |
| 8.6 | a rota anônima só com chave e tipo | o record da rota do Admin |
| 8.7 | `formasDeEntrada` em strings | trocado por objetos |

**Os 32 reprovaram contra o defeito nomeado** (os 30 do plano, o do APPLY-11 e o do cache do discovery), e cada arquivo mutado voltou com o sha de antes; o teste, a mensagem e o "voltou" estão em cada tarefa, e as saídas em `~/.cache/buteco-agents/189-guardas/<tarefa>/`.

## 12. Fechamento

- [x] 12.1 `dotnet build` sem aviso novo; `openspec validate provedores-federados-google --strict`.
  **Feito:** 17 avisos distintos na baseline e na change, nos três projetos de teste; nenhum novo. `openspec validate --strict`: válido.
- [x] 12.2 Suítes da change pelo `suite-189.sh`, uma por vez, em `~/.cache/buteco-agents/189/fechamento/`, sem contêiner do Testcontainers antes de cada suíte e com o `rabbitmq_1` no `podman ps` do início de cada uma.
  **Feito** (sem contêiner do Testcontainers antes de cada suíte, `rabbitmq_1` no `podman ps` do início): `api` 1ª rodada (`189/fechamento/`) **INCONCLUSIVA**, 1 reprovação com a CPU em 60 — `KnowledgeBaseIndexingSummaryTests.SummaryQuery_CostDoesNotGrowWithNumberOfBases` (`Expected: 3, Actual: 2`), o mesmo caso e a mesma mensagem da rodada 1 da baseline, fora da change; 2ª rodada (`189/fechamento-api2/`) **APROVADA**, 1789 + 1 ignorado, 351 s, CPU mín. 60. `rt-inbox` **APROVADA** 16/16, CPU 100. `rt-connectors` **APROVADA** 12/12, CPU 100.
- [x] 12.3 Comparação por nome contra a baseline (`nomes-trx.py`, GUID normalizado, multiconjunto). Nenhum caso some nem muda fora da lista do topo, e cada caso novo fica ligado ao requisito.
  **Feito:** `189/fechamento-final/nomes.txt` (1818): nenhum caso da baseline saiu nem mudou de resultado; os da lista do topo (`TheAuthEntryPoints_AreTheKnownOnes`, `TheProductionTable_IsTheExpectedMatrix`) com o mesmo nome e aprovados. Entraram 93, todos aprovados: `FederatedLoginTests` 28, `FederatedConnectTests` 15, `FederatedProvidersStartupValidationTests` 15, `CallerConfirmationTests` 9, `FederatedProviderRoutesTests` 5, `FakeGoogleTests` 3, `FederatedLoginHostedDomainTests` 3, `UserFederatedIdentityStorageTests` 2, `FederatedEndToEndTests` 1, e 12 resultados novos do `RoleMatrixTests.EachRoute_ForEachRole_FollowsTheTable` (4 rotas × 3 papéis).
- [x] 12.5 (depois do APPLY-11) A suíte do `apps/api` de novo pelo `suite-189.sh`, com a comparação por nome contra a rodada aprovada. Os acordos não rodam outra vez: a mudança está só no início do fluxo federado, que o `IssuerUserFlow` não percorre.
  **Feito** (`189/fechamento-apply11/`): **APROVADA**, 1793 + 1 ignorado, 359 s, CPU mín. 58. Contra a 2ª rodada aprovada: nenhum caso saiu nem mudou; entraram os 4 do APPLY-11 (`TheStart_WithTheProviderDown_IsA503ProblemJson_WithoutCookie`, `Connecting_WithTheProviderDown_IsA503ProblemJson`, `TheStart_WithTheProviderSilent_GivesUpAtTheExplicitTimeout`, `TheReturn_WithTheProviderDown_IsFailure_WithoutSession`).
- [x] 12.6 (depois do caso do cache, antes do merge) A suíte do `apps/api` de novo, com a comparação por nome contra a rodada aprovada da 12.5.
  **Feito:** 1ª rodada (`189/fechamento-cache/`) **INCONCLUSIVA**, CPU mín. 58, pela intermitente da #232 (`Expected: 2, Actual: 4`, comentada lá); 2ª (`189/fechamento-cache-r2/`) **APROVADA**, 1794 + 1 ignorado, 342 s, CPU mín. 58. Contra a 12.5: nenhum caso saiu nem mudou; entrou `FederatedDiscoveryCacheTests.TwoStartsInARow_FetchTheDiscoveryOnce`.
- [x] 12.4 O design corrigido com toda divergência achada no apply (convenção 9).
  **Feito:** D15 no design (APPLY-1 a APPLY-11), com dois pontos para o dono: APPLY-7 (o cenário trocado) e APPLY-11 (o `start` sem rede).

## 13. Conferências

**Com o duplo, nos testes (feitas no apply):** o fluxo inteiro nas duas intenções (login e conectar), as sete recusas do login e as do conectar, a correlação e o state de uso único, os atributos dos três cookies (`buteco-fluxo-externo`, `buteco-sessao` do retorno, `buteco-recusa`), o `iss`, o `aud`, o `nonce`, a assinatura, o PKCE, o `hd`, o `email_verified`, o vínculo e a unicidade, a última forma de entrada em corrida, o stamp, a matriz e as rotas sem segredo. O duplo não prova nada sobre o navegador nem sobre o Google de verdade.

- [x] 13.1 Com o `apps/api` local, num banco descartável, e o duplo **fora** do processo (o `apps/api` sem rede externa): o boot com a seção válida, com cada regra quebrada (as mensagens), e sem a seção; `GET /auth/federated/providers` e `GET /identity/federated-providers` pelo `curl`; `POST /auth/federated/refusal` sem cookie (`404` vazio). O banco é apagado no fim. Resultado no `02`.
  **Feito** (`~/.cache/buteco-agents/bin/conferencia-189.py`, saídas em `~/.cache/buteco-agents/189/conferencia/`): banco `buteco_agents_conf189` copiado do de desenvolvimento por `pg_dump` (a partir do `template0`: o `template1` tem collation divergente, e não foi tocado), migrado pelo worktree, **apagado no fim**. O `apps/api` com `HTTP(S)_PROXY` morto. Sem a seção: sobe, `providers` vazio. Regras 1 a 6 e 8 a 10: o boot cai com a mensagem de cada uma (a 7 só nos testes: a fonte JSON seria editar o `appsettings` do worktree). Com o provedor: sobe sem rede; `GET /auth/federated/providers` só chave e tipo; `POST /auth/federated/refusal` sem cookie e com cookie adulterado: `404` vazio; chave inexistente: `404` "Provedor não configurado."; retorno com state forjado: `302 /entrar?recusa=falha`; um Admin real (link pelo `password-reset-link`, cadastro das 2 etapas no login, authorize, token): `GET /identity/federated-providers` `200` sem o segredo, com a `uriDeRetorno` `http://localhost:5173/auth/federated/callback`; `GET /account` com `formasDeEntrada` `["senha"]`. **Achado (APPLY-11):** o `start` sem rede responde `400` em texto (ID2170) depois de ~15 s.

**Manual, com o cliente OAuth real: na homologação da M3, como a validação visual (decisão do dono, 13/11/2026).** Nenhum segredo no repositório.

**Preparação (uma vez):**
1. No Google Cloud Console, um projeto; em "APIs e serviços → Tela de consentimento OAuth", o tipo "Interno" (conta do Workspace) ou "Externo" em modo de teste, com as contas de teste listadas (a conta que vai entrar e uma segunda, para o caso `sem-acesso`).
2. Em "Credenciais → Criar credenciais → ID do cliente OAuth", o tipo "Aplicativo da Web", com as URIs de redirecionamento autorizadas `https://<PUBLIC_DOMAIN>/auth/federated/callback` (homologação) e `http://localhost:5173/auth/federated/callback` (local). Sem origem JavaScript autorizada.
3. No servidor de homologação, o `federated-providers.env` (permissão 600) com `Type=google`, o `ClientId` e o `ClientSecret`, na chave `corporativo`; `docker compose version` ≥ 2.24.0; `up -d api`. Localmente, as mesmas três variáveis em `~/.config/buteco-agents/.env`.
4. Conferir `curl -s https://<PUBLIC_DOMAIN>/auth/federated/providers` → `{"provedores":[{"chave":"corporativo","tipo":"google"}]}`.

**Conferências:**

- [ ] 13.2 **O pedido.** `POST /auth/federated/corporativo/start` (pelo painel da #190, ou pelo `curl` com `-H 'Content-Type: application/json' -H 'Origin: https://<PUBLIC_DOMAIN>' -d '{}'`): o `url` abre a tela do Google, com a escolha de conta (`prompt=select_account`), os escopos de e-mail e perfil, sem erro de `redirect_uri_mismatch`.
- [ ] 13.3 **O id token real.** Com a conta conectada, depois do retorno, o log do `api` sem `FederatedLoginFailed` (1199). Para ler as claims sem pôr dado pessoal em log: no ambiente local, um ponto de parada no `ProviderIdentity.From` (a troca do código com o Google é do servidor; o navegador não a vê). Conferir: `iss` = `https://accounts.google.com` (com esquema; sem esquema o Buteco recusa, D1); `email_verified` = `true`; `hd` presente numa conta do Workspace e ausente numa conta pessoal.
- [ ] 13.4 **O `iss` na resposta.** Na aba de rede, o `GET /auth/federated/callback?…` traz ou não o parâmetro `iss`; com ou sem ele, o retorno é aceito (o WebIntegration desliga a conferência para o Google enquanto o discovery não anunciar o suporte).
- [ ] 13.5 **Os cookies, em Chrome, Firefox e Safari**, cada um numa janela anônima, com as ferramentas do navegador abertas (aba Rede, "preservar log"; aba Aplicativo/Armazenamento → Cookies):
  1. no `start`: a resposta grava `buteco-fluxo-externo.<nonce>` com `SameSite=Lax`, `Path=/auth/federated/callback`, `HttpOnly`, `Secure`;
  2. na volta do Google: o pedido `GET /auth/federated/callback` **leva** o `buteco-fluxo-externo.<nonce>` (Lax numa navegação de topo vinda de outro site);
  3. a resposta desse retorno **grava** o `buteco-sessao` (`SameSite=Strict`) — e, num caso de recusa (`sem-acesso`, com a segunda conta de teste), o `buteco-recusa` (`Strict`, `Path=/auth/federated/refusal`, `Max-Age=120`). No Safari, conferir especialmente: se o cookie `Strict` não aparecer no armazenamento depois do retorno, é **parada** (abaixo);
  4. o pedido do `/entrar?…` que vem do `302` **não leva** o `buteco-sessao` (a navegação ainda é cross-site);
  5. o `POST /auth/session/second-factor` feito pelo painel **leva** o `buteco-sessao` e responde `204`; na recusa, o `POST /auth/federated/refusal` **leva** o `buteco-recusa` e responde `200` com o e-mail, e um segundo `POST` responde `404`;
  6. a navegação do painel ao `retorno` (`/connect/authorize?…`) leva o `buteco-sessao` completo e volta com o `code`.
- [ ] 13.6 **E-mail não verificado.** Uma conta Google criada com um e-mail que não é Gmail e não foi verificado: a recusa `email-nao-verificado`, sem sessão.
- [ ] 13.7 **Cancelar no Google.** Na tela do Google, "Cancelar": o retorno chega com `error=access_denied`, e o painel recebe `/entrar?recusa=falha`, sem sessão; o log tem o 1199 com `access_denied`.
- [ ] 13.8 **Conectar e desconectar de verdade.** Com a senha, `POST /account/federated/corporativo/connect` → Google → `/account?conectado=corporativo`; o login seguinte pelo Google pede o código do Buteco; `DELETE /account/federated/corporativo` → `204`, e o login pelo Google seguinte cai em `nao-conectada`.

Se a 13.5 falhar em algum navegador (o `Strict` não gravado na resposta do retorno), é parada: a saída do Risks (um passo de entrega `Lax` de uso único) muda o contrato.
