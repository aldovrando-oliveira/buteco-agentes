Cada guarda marcado **(guarda)** só conta depois de visto reprovar contra o defeito que existe para pegar (convenção 15). Há duas formas:

- **Defeito reintroduzido:**
  1. a cópia do arquivo vai ao scratchpad, com o sha;
  2. o defeito nomeado é reintroduzido;
  3. o teste roda e reprova;
  4. o arquivo volta pela cópia, e o sha é conferido igual ao de antes.
- **Árvore de antes:** quando o defeito é "o HMAC ainda aceito" ou "o esquema de política de volta", o caso novo é copiado para o worktree `../buteco-agentes-182-baseline` (`8decb1e`), roda lá e reprova. Depois a cópia sai, e o `git status` do baseline volta limpo.

Fica registrado na tarefa:

- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes, ou o `git status` limpo do baseline. A guarda reintroduz só o defeito nomeado, nunca um diretório inteiro.

**Suítes:** pelo `~/.cache/buteco-agents/bin/suite-182.sh` (cópia do `suite-236.sh`), com a regra assimétrica: verde conta mesmo com a CPU caindo, e reprovação só conta com a CPU em 100 do início ao fim. O `apps/api` roda inteiro para o veredito verde e em cinco partes (`fila-236-api-partes.sh` com `WT=<worktree>`) para tempo e para reprovação válida.

**Baseline:** `~/.cache/buteco-agents/182D-baseline/r1/`, em `8decb1e`, as seis suítes aprovadas, com `nomes.txt` (1.975 resultados). Tempos com CPU 100: inbox 50 s, connectors 22 s, rt-inbox 55 s, rt-connectors 31 s, rt-crossapp 19 s. O `apps/api` (392 s, CPU mínima 25) não compara inteiro: as cinco partes da baseline rodam na tarefa 8.1.

**Comparação por nome:** `compare-trx.py` contra o `nomes.txt` da baseline. A diferença esperada é exatamente:

- os 49 que saem (46 da lista, 2 da Q3 e 1 da P3, design D5);
- os 8 renomeados;
- os 8 novos.

Qualquer outra diferença: PARE.

**#176:** uma reprovação do `OrphanDispatchRoundTripTests` (`Source3_…` ou `TerminalTask_…`, timeout em `WaitForTaskIdAsync`) é comentada na #176. Reprovação nos dois worktrees não bloqueia. Reprovação só no worktree da change: PARE.

**Parada:** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura além da lista do design (D5). Correção que só toca comentário ou documentação segue e fica registrada (convenção 9).

## 0. Verificação feita antes do apply (P4)

- [x] 0.1 (apps/inbox, apps/connectors) **(guarda)** `IssuerTokenValidationTests.Rs512WithThePublishedKey_IsUnauthorized`, o guarda da restrição a RS256 (design K5).
  - **Defeito:** `ValidAlgorithms = [SecurityAlgorithms.RsaSha256]` retirado do `Auth/IssuerTokenAuthentication.cs` (l. 122) dos dois apps.
  - **Reprova nos dois:** `Assert.Equal() Failure: Values differ / Expected: Unauthorized / Actual: OK`. Na mesma rodada, o `Hs256SignedWithTheHmacKey_IsUnauthorized` continuou verde, que é o motivo da P4.
  - **Voltou:** os dois arquivos pela cópia (`scratchpad/guarda-rs512/`), com o blob igual ao do `HEAD`: `9c4141c` no inbox, `3ba23ab` no connectors. Com a restrição de volta, os 2 + 2 casos aprovaram.

## 1. Medição da Q5 isolada (worktree descartável), antes de remover qualquer coisa

- [x] 1.1 (apps/inbox, apps/connectors) Worktree descartável `../buteco-agentes-182-medicao` em `8decb1e`, só com:
  - o `ValidateIssuerTokenConfiguration` lançando sem `Identity:Issuer` (D2), nos dois apps;
  - a `ConnectorsFactory` com o emissor ligado por padrão (D6);
  - as três fixtures do inbox com `UseIssuer()` vazio passando ao `UseIssuer(TestIssuer.Issuer)` (D6).
- [x] 1.2 (apps/inbox, apps/connectors) As duas suítes pelo `suite-182.sh`, com o relatório por nome. O conjunto reprovado precisa ser **exatamente**:
  - os 2 da Q3;
  - os dois `WithoutIssuer_ValidationIsOff_…`;
  - os dois casos da P3 (`WithoutTheApiAddress_TheSecretIsNotRequired`, `DefaultFactory_ResolvesApiBaseUrlAsNotConfigured`);
  - o `IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys`, cuja fixture muda (D6);
  - os casos do connectors que usam o HMAC com o emissor ligado e já estão na lista, se reprovarem.

  Reprovação de qualquer outro nome: PARE e traga o nome. Registrar a lista e as durações. O worktree descartável sai depois.

  **Feito em 04/11/2026** (`~/.cache/buteco-agents/182D-medicao-q5/`). **r1**, as duas suítes reprovadas válidas (CPU em 100 do início ao fim). O conjunto reprovado é **exatamente** o nomeado, 7 casos:
  - connectors: `ApiConfigurationValidationTests.DefaultFactory_ResolvesApiBaseUrlAsNotConfigured` e `.RealComposition_WithoutBaseUrl_StartsWarnsAndServesNavigation`, `IssuerTokenStartupValidationTests.IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys` e `.WithoutIssuer_ValidationIsOff_IssuerTokenIsUnauthorized_HmacWorks_AndBootWarns`, `ServiceClientConfigurationTests.WithoutTheApiAddress_TheSecretIsNotRequired`, `SyncEndpointsTests.WithoutApiBaseUrl_Is503SyncNotConfigured_WithoutAnyCall`;
  - inbox: `IssuerTokenStartupValidationTests.WithoutIssuer_ValidationIsOff_IssuerTokenIsUnauthorized_HmacWorks_AndBootWarns`.

  Nenhum caso HMAC da lista reprovou com o emissor ligado. 665 resultados, os mesmos nomes da baseline.

  **Durações** (as do xUnit, sem a compilação):
  - connectors: 13 s, contra 12 s na baseline;
  - inbox: 59 s na r1, contra 34 s. Os casos que mais desaceleraram não eram das três classes alteradas (ex.: `UpdateChannelCommandHandlerTests.Handle_WithoutCredential_DoesNotCallValidator`, de 0,05 s para 2,06 s). A **r2**, logo depois da baseline r2 na mesma condição, deu **34 s** contra **36 s** da baseline, com o mesmo único caso reprovado. A r1 do inbox foi variação da máquina, e não custo da mudança.

  O worktree descartável saiu.

## 2. apps/connectors

- [x] 2.1 (apps/connectors) `Auth/IssuerTokenStartupValidation.cs`: sem `Identity:Issuer`, o boot falha com a mensagem do D2. A frase "deixe Identity:Issuer vazio para desligar…" sai das duas mensagens. Espelhar no inbox (3.1) com o comentário de espelhamento.
- [x] 2.2 (apps/connectors) `Auth/IssuerTokenAuthentication.cs` (D1, D3):
  - sai o ramo `!issuerOptions.Enabled`, o `AddPolicyScheme`, o `SelectScheme`, o `PolicySchemeName` e o `AddScheme<…, OperatorTokenAuthenticationHandler>`;
  - `AddAuthentication(IssuerSchemeName)`;
  - no `ChallengeWithoutKeysAsUnavailable`, o `503` só quando a requisição traz o cabeçalho `Authorization`;
  - o comentário do topo descreve um esquema só.

  O `IssuerTokenOptions.Enabled` sai se não sobrar leitor.

  Feito: o `503` fica para quem traz `Authorization` (`StringValues.IsNullOrEmpty`, então o cabeçalho vazio conta como ausente). O `IssuerTokenOptions.Enabled` ficou, porque a checagem de boot o lê.

- [x] 2.3 (apps/connectors) Saem `Auth/TokenService.cs`, `ITokenService.cs`, `TokenValidationResult.cs`, `TokenSigningOptions.cs` e `OperatorTokenAuthenticationHandler.cs`. O `Program.cs` perde a checagem da chave (l. 11-16) e as l. 19-20. O `appsettings.Development.json` perde a `Auth:TokenSigningKey`. O comentário do `ConnectorsSubjectAuthorizationHandler` (l. 31) é corrigido.
- [x] 2.4 (apps/connectors) Testes, pela lista do design D5:
  - saem os 19 + 2 (Q3) + 1 (P3) por nome; sai o `IssuerTokenSchemeSelectionTests.cs` inteiro;
  - R1, R4, o `PanelToken_CanSyncNow` renomeado e o `DefaultFactory_ResolvesApiBaseUrlToTheTestIssuer` reescrito;
  - o `IssuerWithoutApiBaseUrl_…` passa `Api:BaseUrl = ""` explícito;
  - `Support/`: a `ConnectorsFactory` com o emissor ligado por padrão, sem `Auth:TokenSigningKey`, `CountingTokenService` nem `UseIssuer()`; o `TestAuthentication` sem `TokenSigningKey`, `IssueToken` e `CreateClientAs`; o `TestIssuer` ganha o "segurar as chaves" da cópia do inbox.

  **Correção (convenção 9; sem mudar comportamento, contrato ou cobertura):** o `IssuerTokenSchemeSelectionTests.cs` **não sai inteiro**. Ele tem um caso fora da lista, o `AnonymousRoute_WithInvalidJwt_StillAnswersByTheRoute` (uma rota anônima com JWT inválido responde pela rota), que o diff por nome da r1 achou faltando. O arquivo voltou do `HEAD` (blob `c32d6a3`, conferido), saíram só os casos da lista, e a classe manteve o nome, para o caso manter o nome. O inbox tem o mesmo caso, e o mesmo tratamento vale lá (3.3).
  **Correção:** o `WithoutToken_BeforeTheKeys_…` ficou no `IssuerKeysLifecycleTests`, que já cria uma fábrica por caso, com o `TestIssuer` em `Availability = Unreachable`. O estado é o mesmo, chaves nunca obtidas, e o caso cobre também o cenário "Sobe com o apps/api fora do ar" da spec (health `200`, token `503` com `Retry-After`). O `TestIssuer` do connectors não precisou do "segurar as chaves".
  O token do formato antigo sai de `Support/OldSharedKeyToken.cs`, uma cópia do formato do `TokenService` removido, porque o app não o tem mais.

- [x] 2.5 (apps/connectors) **(guarda)** `IssuerTokenValidationTests.PanelToken_OnDescribeFolder_IsForbidden` (R1). Defeito: `operator` acrescentado à lista de descrever pasta no `ConnectorsSubjectAuthorizationHandler.SubjectRoutes`. Esperado: o caso reprova com `Values differ`.
  Visto: `Assert.Equal() Failure: Values differ / Expected: Forbidden / Actual: NotFound`. Voltou: `ConnectorsSubjectAuthorizationHandler.cs` (sha `a183e65c787d`, igual ao de antes).

- [x] 2.6 (apps/connectors) **(guarda)** `SecretLeakTests.NenhumLogDoCicloContemConteudoTokenChaveOuNomeDeArquivo` (R4). Defeito: o `ServiceAccessTokenSource` loga o access token obtido. Esperado: o caso reprova na asserção dos tokens enviados.
  Visto: `Assert.All() Failure: 6 out of 6 items` com `Assert.DoesNotContain() Failure: Sub-string found`. Voltou: `ServiceAccessTokenSource.cs` (sha `1346c0dc9417`, igual ao de antes e ao do `HEAD`).

- [x] 2.7 (apps/connectors) **(guarda)** `ApiConfigurationValidationTests.DefaultFactory_ResolvesApiBaseUrlToTheTestIssuer` (P3). Defeito: a `ConnectorsFactory` sem o `UseSetting` do `Api:BaseUrl`, que resolve o `localhost:5017` do `appsettings.Development.json`. Esperado: o caso reprova.
  Visto: `Strings differ / Expected: "http://apps-api.test" / Actual: "http://localhost:5017"`. Voltou: `Support/ConnectorsFactory.cs` (sha `f784fdaff019`, igual ao de antes).

- [x] 2.8 (apps/connectors) Casos novos e o guarda de cada um:
  - **(guarda)** `IssuerTokenStartupValidationTests.WithoutIssuer_FailsTheRealApplicationStartup_NamingTheKey`. Defeito: o ramo de aviso de volta (`return` em vez de `throw`);
  - **(guarda)** `IssuerTokenValidationTests.WithoutToken_BeforeTheKeys_Is401_WithTheBearerChallenge`, com o `TestIssuer` segurando as chaves do início ao fim. Afirma `401` e `WWW-Authenticate: Bearer`. Defeito: o `503` sem a condição do cabeçalho;
  - **(guarda)** `IssuerTokenValidationTests.HmacShapedToken_SignedWithTheOldKey_Is401`. Afirma `401` e `WWW-Authenticate: Bearer` para um `payload.assinatura` assinado com a chave das fixtures antigas. Visto reprovar na árvore de antes (`200`).
  Vistos:
  - `WithoutIssuer_…`: `Assert.ThrowsAny() Failure: No exception was thrown`. Voltou: `IssuerTokenStartupValidation.cs` (sha `243b41571705`, igual);
  - `WithoutToken_BeforeTheKeys_…`: `Expected: Unauthorized / Actual: ServiceUnavailable`. Voltou: `IssuerTokenAuthentication.cs` (sha `57d7a4c1d9fa`, igual);
  - `HmacShapedToken_…`: o caso e o `OldSharedKeyToken` copiados para o `../buteco-agentes-182-baseline`, `Expected: Unauthorized / Actual: OK`. Isso prova também que o helper reproduz o formato antigo. Depois, `git status` do baseline limpo, em `8decb1e`.

  **Ocorrência do executor de guardas:** na primeira passada, a restauração por `copy2` preservou o mtime antigo, e o build incremental manteve dois defeitos compilados. A r2 da suíte reprovou 3 casos com a fonte correta (sha conferido). O executor passou a restaurar com mtime novo, e os seis guardas foram repetidos do zero, com os resultados acima. Os seis casos e o `HmacShapedToken` aprovam no código final.

- [x] 2.9 (apps/connectors) Suíte pelo `suite-182.sh` e comparação por nome, só a parte do connectors da diferença esperada. Duração com CPU 100: no máximo 1,5× os 22 s da baseline. Acima disso, PARE e meça o custo da fábrica com o emissor ligado (D6).

  **r3** (`182D-change/connectors-r3`): APROVADA, 320 aprovados e 1 ignorado, 11 s, CPU em 100. Baseline: 12 s. Por nome: saem 25 (os 22 do D5 e os 3 renomeados), entram 6 (3 renomeados e 3 novos). Conta: 340 − 22 + 3 = 321 resultados.

## 3. apps/inbox

- [x] 3.1 (apps/inbox) `Auth/IssuerTokenStartupValidation.cs` e `Auth/IssuerTokenAuthentication.cs`: o espelho de 2.1 e 2.2.
  Feito: o `IssuerTokenAuthentication.cs` do inbox foi gerado a partir do novo do connectors, com as mesmas trocas da diferença original. A diferença entre os dois é a mesma de antes, conferida por `diff` dos dois diffs normalizados. Também corrigidos o comentário do `Program.cs` (l. 135) e, no connectors, o do `IssuerTokenStartupValidation`, que ainda chamava o `Api:BaseUrl` de opcional.

- [x] 3.2 (apps/inbox) Saem os 5 arquivos de `Auth/`. O `Program.cs` perde as l. 32-33, e o `appsettings.Development.json` perde a `Auth:TokenSigningKey`.
- [x] 3.3 (apps/inbox) Testes, pela lista do design D5:
  - saem os 13 por nome; sai o `IssuerTokenSchemeSelectionTests.cs` inteiro;
  - `Support/TestAuthentication.cs` sem `TokenSigningKey`, e o `UseIssuer` sem valor padrão; `Support/IssuerTokenFactory.cs` sem `OperatorHmacToken()` nem o contador;
  - `DebounceOptionsValidationTests`, `InboxShutdownUnderKestrelTests` e `ServiceClientConfigurationTests` com `UseIssuer(TestIssuer.Issuer)`.
  Como no connectors (2.4), o `IssuerTokenSchemeSelectionTests.cs` fica com o `AnonymousRoute_WithInvalidJwt_StillAnswersByTheRoute`, que está fora da lista. O `WithoutToken_BeforeTheKeys_…` ficou no `IssuerKeysLifecycleTests`, com o `TestIssuer` em `Unreachable`. O `Support/OldSharedKeyToken.cs` é a mesma cópia do connectors.

- [x] 3.4 (apps/inbox) Casos novos e o guarda de cada um, como em 2.8: `WithoutIssuer_FailsTheRealApplicationStartup_NamingTheKey`, `WithoutToken_BeforeTheKeys_Is401_WithTheBearerChallenge` (em `GET /channels`) e `HmacShapedToken_SignedWithTheOldKey_Is401`, cada um **(guarda)** contra o mesmo defeito do connectors.
  Vistos:
  - `WithoutIssuer_…`: `Assert.ThrowsAny() Failure: No exception was thrown`. Voltou: `IssuerTokenStartupValidation.cs` (sha `381f560825b7`, igual à cópia de antes);
  - `WithoutToken_BeforeTheKeys_…`: `Expected: Unauthorized / Actual: ServiceUnavailable`. Voltou: `IssuerTokenAuthentication.cs` (sha `6c9435627c36`, igual);
  - `HmacShapedToken_…` na árvore de antes: `Expected: Unauthorized / Actual: OK`. Baseline limpo depois, em `8decb1e`.

- [x] 3.5 (apps/inbox) Suíte e comparação por nome. Duração com CPU 100: no máximo 1,5× os 50 s da baseline.

  **r1** (`182D-change/inbox-r1`): APROVADA, 315/315, 41 s, CPU em 100. Baseline: 34 s (r1) e 36 s (r2), então 1,2×. Por nome: saem exatamente os 13 da lista e entram os 3 novos (325 − 13 + 3 = 315).

## 4. apps/api

- [x] 4.1 (apps/api) `Identity/Validation/IssuerTokenAuthentication.cs` (D1): `AddAuthentication(IssuerSchemeName)`. Saem o `AddPolicyScheme`, o `SelectScheme`, o `PolicySchemeName` e o registro do handler HMAC. O comentário do topo descreve um esquema só.
- [x] 4.2 (apps/api) Saem os 5 arquivos de `Auth/`. O `Program.cs` perde as l. 51 e 74 e o comentário da l. 133. O `appsettings.Development.json` perde a `Auth:TokenSigningKey`. Corrigir os comentários do `OperatorPasswordHasher` (l. 8), do `KnowledgeDocumentEndpoints.ReadAuthor` (l. 287) e do `CurrentUser` (l. 11-13).
  Também os comentários de `UserEndpoints` e `UserTwoFactorResetEndpoint`, que citavam o token antigo.

- [x] 4.3 (apps/api) Testes, pela lista do design D5:
  - saem os 13 por nome; sai o `Identity/IssuerTokenSchemeSelectionTests.cs` inteiro;
  - R5 e R6–R9, com os nomes do D5;
  - `Support/TestAuthentication.cs` sem `TokenSigningKey`, a entrada do `ConfigOverrides` e `AttachOperatorToken`;
  - os comentários de cabeçalho das três classes `*CallerTests` passam a citar o operador sem usuário.
  O `Identity/IssuerTokenSchemeSelectionTests.cs` saiu inteiro: só tinha os dois `Theory` da lista e o helper privado. O `IssuerTokenAcceptanceTests.Fixture` perdeu o contador e virou `LocalUsersFixture` puro. Os comentários de teste que citavam o token antigo (`GoneLoginTests`, `ServiceClientRouteTests`, `TestIssuerTokens`, `TestIssuerTokensTests`, `SyncedCreation`, os dois `ServiceTokenDelegatingHandlerTests`) foram reescritos. Os dados de teste com "HMAC" no texto (nomes e e-mails) viraram "sem usuário".

- [x] 4.4 (apps/api) **(guarda)** `KnowledgeDocumentCatalogTests.DocumentWrites_WithThePanelToken_RecordOperatorAsAuthor` (R5). Defeito: a edição de documento grava o autor pelo claim `client_id`, e não pelo `ReadAuthor`. Esperado: o caso reprova (autor `buteco-painel`).
  Visto: com a edição (`KnowledgeDocumentEndpoints.cs:132`) gravando o autor pelo `client_id`: `Assert.All() Failure: 1 out of 3 items` / `Expected: "operator" / Actual: "buteco-painel"`. Voltou: `KnowledgeDocumentEndpoints.cs` (sha `51f5e9a6101f`, igual).

- [x] 4.5 (apps/api) **(guarda)** R6–R9, cada um contra o defeito já registrado no comentário da classe, um por vez:
  - `AccessLinkCallerTests.OperatorWithoutUser_Is403_OnTheFourRoutes_AndNothingIsWritten`: o `CurrentUser` retirado de criar convite;
  - `UserRoutesCallerTests.OperatorWithoutUser_Is403_OnEveryRoute_AndNothingChanges`: o `CurrentUser` retirado de `PUT /users/{id}`;
  - `TwoFactorCallerTests.OperatorWithoutUser_OnMyAccount_Is403`: o `CurrentUser` retirado de `GET /account/two-factor`;
  - `TwoFactorCallerTests.OperatorWithoutUser_OnTheReset_Is403_AndChangesNothing`: o `CurrentUser` retirado da redefinição.

  Esperado: cada caso reprova contra o seu defeito. Isso prova que o token chega ao `CurrentUser` (P2).
  Vistos, cada um sozinho:
  - R6: criar convite com quem chama lido como o primeiro usuário do banco, sem o `CurrentUser`, o mesmo defeito da 5.6 da change convites-redefinicao-senha. Resultado: `POST /users/invitations → 401`, porque a rota passou da recusa e parou na conferência da senha. Voltou: `InvitationEndpoints.cs` (sha `6180705dd3ae`, igual);
  - R7: o mesmo defeito em `PUT /users/{id}` (`UserEndpoints.cs:81`). Resultado: `PUT /users/… → 200`. Voltou: `UserEndpoints.cs` (sha `552f341d603a`, igual);
  - R8 e R9: **o defeito "primeiro usuário do banco" não serviu.** No R8 o banco do caso está vazio, e no R9 o primeiro usuário é o próprio alvo, que recebe o `403` de "a si mesmo". Os dois casos ficaram verdes contra ele. Passei ao defeito do guarda original da 6.1 da change verificacao-2-etapas-totp (`218-verificacao/guardas/6.1-redefinicao-aceita-hmac-defeito.diff`): a recusa sai, e quem chama segue nulo;
  - R8: `GET /account/two-factor` sem a recusa. Resultado: `Expected: Forbidden / Actual: InternalServerError`. Voltou: `AccountTwoFactorEndpoints.cs` (sha `02b55036f26b`, igual);
  - R9: a redefinição sem a recusa (`caller?.Id == id`). Resultado: `Expected: Forbidden / Actual: NoContent`, ou seja, a redefinição foi feita. Voltou: `UserTwoFactorResetEndpoint.cs` (sha `2238eefc0695`, igual).

- [x] 4.6 (apps/api) Casos novos:
  - **(guarda)** `Identity.IssuerTokenAcceptanceTests.UnknownRoute_WithoutToken_Is401_WithTheBearerChallenge`: `GET /rota-que-nao-existe` e `GET /agents` sem token, os dois `401`, com corpo vazio e `WWW-Authenticate: Bearer`. Visto reprovar na árvore de antes (sem o cabeçalho);
  - **(guarda)** `Identity.IssuerTokenAcceptanceTests.HmacShapedToken_SignedWithTheOldKey_Is401`. Visto reprovar na árvore de antes (`200`).
  Vistos na árvore de antes: `HmacShapedToken_…` → `Expected: Unauthorized / Actual: OK`; `UnknownRoute_WithoutToken_…` → `Assert.Single() Failure: The collection was empty`, o `401` sem `WWW-Authenticate`. Baseline limpo depois, em `8decb1e`.

- [x] 4.7 (apps/api) Suíte inteira (veredito) e em cinco partes (tempo), e a comparação por nome.

  **r1, a suíte inteira** (`182D-change/api-r1`): APROVADA, 1.269 aprovados e 1 ignorado, CPU mínima 44 (verde conta). Por nome: saem os 13 da lista e os 5 nomes antigos dos renomeados (R5–R9), e entram os 5 renomeados e os 2 novos (1.281 − 18 + 7 = 1.270). As cinco partes, para o tempo, rodam na 8.1, junto com as da baseline.

## 5. tests/

- [x] 5.1 (tests/InboxOrchestratorRoundTrip.Tests) Sai o `RoundTripTests.OperatorHmacToken_SignedWithTheSharedKey_IsAcceptedByInboxWithoutNetworkCallToApi`. Do `Support/RoundTripFixture.cs` saem a `TokenSigningKey`, as duas entradas `Auth:TokenSigningKey` e o `OperatorHmacToken()`.
  O `IssuerKeysGate` da fixture fica: o `IssuerTokenRoundTripTests` ainda o usa. Comentários reescritos: o de `ServiceTokenRoundTripTests` ("nenhuma levou HMAC") e os dois de `ApiConnectorsRoundTrip`.

- [x] 5.2 (tests/ApiConnectorsRoundTrip.Tests) Do `Support/RoundTripFixture.cs` saem a `TokenSigningKey` e as duas configurações.
- [x] 5.3 (tests/) As três suítes (`rt-inbox`, `rt-connectors`, `rt-crossapp`) e a comparação por nome. O `rt-crossapp` não muda.

  **r1** (`182D-change/rt-r1`), as três aprovadas: rt-inbox 14/14 (20 s), rt-connectors 10/10 (17 s), rt-crossapp 4/4 (11 s). Por nome: sai só o caso da lista, e não entra nenhum.

## 6. Deploy

- [x] 6.1 (deploy) `docker-compose.prod.yml`:
  - saem o `Auth__TokenSigningKey` dos três serviços (l. 126, 168 e 235) e os comentários das l. 161 e 167;
  - o `Identity__Issuer` do inbox e do connectors continua `${PUBLIC_DOMAIN}` (D4).
  Os comentários do `Identity__Issuer` e do `Api__BaseUrl` do connectors passaram a dizer que são obrigatórios.

- [x] 6.2 (deploy) `.env.example` (l. 216-227 e 252-254) e `.env.prod.example` (l. 151) sem a chave. O `docker-compose.yml` não muda (o grep não acha a chave).
  O bloco do connectors no `.env.example` também dizia que o `Api:BaseUrl` era opcional, com `503 sync-not-configured`. Passou a obrigatório.

- [x] 6.3 (deploy) Conferência, sem guarda (o comportamento já existe pelo `:?` do serviço `api`, D4): `docker compose -f docker-compose.prod.yml --env-file <cópia do .env.prod.example com PUBLIC_DOMAIN vazio> config` falha citando `PUBLIC_DOMAIN`; com os valores de exemplo, o `config` passa e nenhum serviço traz `Auth__TokenSigningKey`. Registrar as duas saídas.

  Com `podman-compose` (a máquina não tem `docker`): `PUBLIC_DOMAIN` vazio → exit 1, `required variable PUBLIC_DOMAIN is missing a value`; com o `.env.prod.example` → exit 0, nenhuma `TokenSigningKey` no compose renderizado, e o `Identity__Issuer` e o `Api__BaseUrl` presentes no inbox e no connectors. Saídas em `scratchpad/compose/`.

## 7. Documentação (design D8)

- [x] 7.1 (docs) `01-ARQUITETURA_E_CONVENCOES.md`, seção "Autenticação": a reescrita do D8, com a frase do D3 no lugar da l. 607-611.
  O bullet "Sem operador único desde a #217" deixou de nomear o `Auth:OperatorTokenLifetime`, já removido, porque o nome batia no termo `OperatorToken` da varredura. As notas históricas em itálico ficaram.

- [x] 7.2 (docs) `SECURITY.md`: "Authentication tokens" e "Shared secrets across processes", em inglês (fecha a #122).
- [x] 7.3 (docs) `docs/architecture.md`: l. 852-921, 1101, 1141 e o diagrama de l. 1166-1181.
  A abertura da seção também estava defasada desde antes da #182 (operador único, `POST /auth/login`, módulo de token sobre `sessionStorage`) e foi reescrita junto. As duas menções históricas fora do `01` foram reescritas sem os termos, porque a exceção da P5 cobre só as notas em itálico do `01`.

- [x] 7.4 (docs) `docs/configuration.md`: as linhas do D8, com o `Identity__Issuer` obrigatório e a tabela do `Api:BaseUrl` do connectors.
  Também: `Identity__Issuer` e `Api__BaseUrl` na lista de fail-fast, o `ServiceClient__Secret` do connectors como fail-fast, e a nota de que o tratamento de "sem `Api__BaseUrl`" ficou inalcançável e sai na #241.

- [x] 7.5 (docs) `docs/development.md`: as linhas do D8 e a frase do segredo por serviço.
- [x] 7.6 (docs) `docs/deployment.md`:
  - a seção "Remoção do HMAC (#182)" no topo de §3, com o D7: ordem livre, a conferência em quatro passos, o rollback e a retirada da `AUTH_TOKEN_SIGNING_KEY` do `.env.prod` 7 dias depois, com o comando;
  - a tabela de variáveis, a tabela de diagnóstico da #208, a lista do `changeme` e o "Risco aceito (#117)";
  - as seções de deploys passados (#217, #219, #234 e #235) ganham "(histórico)" no título, sem mudar o texto.
  A seção nova ficou antes do "Emissor OIDC (#179)". O `curl` da conferência usa os prefixos reais do nginx (`/agents`, `/channels`, `/connectors/providers`, com `Sec-Fetch-Mode: cors`, como as verificações existentes).
  **Correções da lista da P5 (convenção 9; só documentação):**
  - a seção do #180 (com a subseção "Primeiro Admin") também cita o token antigo e é de deploy passado, então ganhou "(histórico)" como as quatro da lista;
  - a seção nova da #182 precisa nomear a `AUTH_TOKEN_SIGNING_KEY` para o passo de tirá-la do `.env.prod`, que o dono pediu. Ela entra como exceção da varredura;
  - a seção da #208 é diagnóstico em uso, e não histórico: foi reescrita (o app sem `Identity__Issuer` não sobe; sem token, `401`).

- [x] 7.7 (todos) **Varredura** (critério do D8): o `varredura-236.py`, adaptado aos termos `TokenSigningKey`, `TOKEN_SIGNING`, `ITokenService`, `TokenService`, `OperatorToken`, `AttachOperatorToken`, `HMAC` e `Hmac`, sobre `apps/`, `tests/`, `docker-compose*.yml`, `.env*`, `deploy/`, `SECURITY.md`, `01` e `docs/`. Nenhuma ocorrência fora das exceções da P5. A saída fica registrada, com cada exceção pelo nome do arquivo.

  Script: `scratchpad/bin/varredura-182.py` (`git grep --untracked`, para pegar os arquivos novos; ignora `#` dentro de bloco de código ao achar a seção). Saída: `scratchpad/varredura-182.txt`. **Resultado: 0 violações, 34 ocorrências, todas em exceção:**
  - 12 primitivas criptográficas (TOTP HMAC-SHA1, PBKDF2-HMAC-SHA256, `Aes256CbcHmacSha512`, `TotpTests.NewSecret_HasTheSizeOfTheHmacOutput`);
  - 6 do guarda do token antigo: os três `OldSharedKeyToken.cs` e os três `HmacShapedToken_…`. **Correção da lista da P5:** eles provam a recusa do token antigo e precisam gerá-lo;
  - 8 em seções de deploy passado, "(histórico)";
  - 5 no runbook da #182, o passo da `AUTH_TOKEN_SIGNING_KEY`;
  - 3 em notas históricas em itálico do `01`.

  Ao rodar, a varredura achou e corrigiu: o `Auth:OperatorTokenLifetime` do `01` (7.1), e os campos `OperatorTokens` e `ConnectorsOperatorTokens` do `RoundTripFixture` do rt-connectors, caches do token de usuário do emissor da #236, renomeados para `UserAccessTokens` e `ConnectorsUserAccessTokens`.

## 8. Fechamento

- [x] 8.1 (todos) As seis suítes no worktree da change e o `apps/api` em cinco partes, e as cinco partes da baseline no worktree `../buteco-agentes-182-baseline`, para o tempo. Comparação por nome: exatamente a diferença esperada (cabeçalho). Tempos com CPU 100: nenhuma suíte acima de 1,5× a baseline.
  - **Registro (04/11/2026):** rodada em `~/.cache/buteco-agents/182D-change/final` (rebuild dos dois worktrees, 0 erros; baseline com 0 arquivos alterados). Na primeira rodada o inbox reprovou 1 caso, o `DebounceSweepServiceTests.InfrastructureFailure_ProcessingOneCandidate_IsLoggedAndDoesNotBlockAnotherCandidate` (`TimeoutException` no `PollUntil`, caso de tempo e fora da autenticação), com a CPU oscilando entre 23 e 100: rodada inconclusiva. Repetido sozinho em `final-inbox-r2`: 315 aprovados. Pela validade assimétrica, o verde conta mesmo com a CPU em 23.
  - **Resultado:** api 1.269 aprovados + 1 ignorado; inbox 315; connectors 320 + 1 ignorado; rt-inbox 14; rt-connectors 10; rt-crossapp 4. Zero reprovados.
  - **Por nome**, contra `182D-baseline/r1`: 1.973 → 1.932. Saem 57 nomes e entram 16. São 8 pares de renomeação: R5 e R6–R9 no api; R1, `PanelToken_CanSyncNow` e `DefaultFactory_…` no connectors. Ficam 49 que saem e 8 novos (2 no api, 3 no inbox, 3 no connectors). Nenhum nome fora da lista.
  - **Partes do api**, change contra baseline: p1 224 / 235 (os 11 removidos de `Identity/`), 2 min 27 s / 2 min 57 s; p2 183, 2 min / 1 min 58 s; p3 188, 58 s / 3 min 50 s; p4 273, 4 min 03 s / 3 min 57 s; p5 401, 4 min 30 s / 5 min 15 s. Todas abaixo de 1,5×, mas **com CPU 23** nos dois lados.
  - **Tempo com CPU 100**, onde houve medida: connectors 11 s / 12 s; inbox 41 s / 34–36 s (1,2×); rt 17 / 11 / 20 s contra 15 / 10 / 20 s. O api completo não teve rodada com CPU 100 (a `api-r1` teve mínimo 44, 3 min 42 s contra 6 min 04 s com mínimo 25). A máquina seguiu em `CPU_Speed_Limit` 23 até o fim. **Decisão do dono (04/11/2026):** para o api, vale a comparação das cinco partes com a CPU em 23 nos dois worktrees, porque as duas medições rodaram sob a mesma limitação. Todas as partes ficaram abaixo de 1,5×, e o critério está atendido.
- [x] 8.2 (apps/api, apps/inbox, apps/connectors) **Conferência local:** os três apps com `dotnet run`, e o painel pelo `npm run dev`.
  - O painel entra e abre Agentes, Canais e "Sincronizar agora".
  - O log do inbox e do connectors tem `Validação do token do emissor ligada`.
  - `curl -si` sem token em cada app responde `401` com `WWW-Authenticate: Bearer`.
  - O inbox, subido sem `Identity__Issuer`, não sobe e nomeia a chave.
  - **Registro (04/11/2026):** a saída está em `~/.cache/buteco-agents/182D-verificacao/manual/`.
    - **Stack:** Postgres e RabbitMQ descartáveis no Podman (15532/15772), migrations do api e do inbox, os três apps em `Development`, o connectors com a chave do Drive de validação. Sem o `apps/workers`, que não autentica em app nenhum.
    - **Logs:** inbox e connectors com `Validação do token do emissor ligada: issuer http://localhost:5017/, audience buteco-inbox` (ou `buteco-connectors`), `chaves pelo Api:BaseUrl`.
    - **Sem token e com o token antigo:** sem token, `GET /agents` (api), `GET /channels` (inbox) e `GET /connectors/providers` (connectors) respondem `401` com `WWW-Authenticate: Bearer`. O token no formato antigo (`payload.assinatura`, `Sub` `operator`, assinado com a `Auth:TokenSigningKey` de dev de antes da #182) recebe `401` com `WWW-Authenticate: Bearer error="invalid_token"` nos três. A saída está em `sem-token-e-token-antigo.txt`.
    - **S1–S4**, em `conferencia.txt`, pelo script da #235 adaptado:
      - S1: `POST /channels` dá 201, e com agente inexistente dá 400;
      - S2: o webhook dá 200, e a task `1b46aa36…` é criada no apps/api (`Submitted`, sem worker);
      - S4: `POST /knowledge-bases` (Drive) dá 201;
      - S3: `POST …/sync` dá 202, com `lastCompletedAt` preenchido, `lastError` nulo, 5 documentos e 3 ignorados.
      - Os tokens de serviço aparecem uma vez em cada log, sem nenhum `invalid_client`. Nas chamadas de saída do inbox e do connectors, 29 respostas 200 e um 404 (o agente inexistente do S1); nenhum 401 nem 403.
    - **Painel** (`npm run dev` com as três `VITE_*_BASE_URL`): entra com o operador de dev; Agentes (apps/api) e Canais (apps/inbox) listam o que o script criou; "Sincronizar agora" (apps/connectors) dá 202. A primeira subida do Vite foi sem `VITE_CONNECTORS_BASE_URL`, e o botão ficou desabilitado pelo próprio painel; refeita com a variável. O primeiro clique depois disso registrou **um `503`** na aba de rede, perto das 06:14. Isso coincidiu com a renovação do token de serviço do connectors, 15 min depois do boot. Os cinco cliques seguintes, quatro com captura do corpo pelo `fetch`, deram `202` em cerca de 110 ms. Nos logs, nenhuma chamada de saída falhou (34 iniciadas e 34 concluídas, todas 200), e as chaves do emissor estavam obtidas desde o boot, o que exclui o 503 do desafio. Corpo não capturado; não reproduzido.
    - **Investigação do 503** (pedido do dono, 04/11/2026), em `~/.cache/buteco-agents/182D-verificacao/manual/503/`:
      - **Rastros da manhã:** nenhum `warn`, `fail` ou `crit` nos logs do connectors, do api, do inbox e do Vite. Esses logs de console não tinham timestamp nem linha de acesso (`Microsoft.AspNetCore` em Warning). No api, todos os `/connect/token` deram certo; o único erro foi um `refresh_token` antigo do navegador (`invalid_grant`), antes do login. O log do primeiro Vite foi sobrescrito no restart, e o do segundo não tem erro. A requisição foi direto à 5037, onde só o processo do connectors escuta; o Vite não está no caminho. **O 503 saiu do connectors, mas os logs não dizem de qual ramo.**
      - **Fonte:** o `IssuerKeysState` só tem o `MarkObtained()`, que põe `true` e é chamado pelo `IssuerKeysRetriever` depois de uma busca bem-sucedida. A busca que falha só marca `_lastFailureAt` e relança. É um singleton único, e o estado não volta a "não obtido". **Não é defeito da P6.**
      - **Duas tentativas atravessando a renovação**, com o connectors reiniciado e com timestamp e linha de acesso ligados:
        - r1: boot 22:03:34, renovação do token de serviço às 22:17:36 (200). O curl, a cada 5 s do minuto 13 ao 16, teve 36/36 → 202. O painel renovou o próprio token às 22:16:55, e os 17 cliques deram 202.
        - r2: boot 22:20:06, renovação às 22:34:04 (200). O curl teve 36/36 → 202. O painel renovou às 22:32:20, e os 16 cliques deram 202.
        - No connectors, 105 linhas de acesso do `POST …/sync`, todas 202. Nenhum `warn`, `fail` ou `crit`.
      - **Resultado:** não reproduzido em duas tentativas. Segue sem issue, como decidido pelo dono.
    - **Sem `Identity__Issuer`:** o inbox termina com código 134 e `InvalidOperationException: Identity:Issuer não configurado: é o issuer público do emissor do apps/api…` (`inbox-sem-issuer.log`), sem abrir a porta.
- [x] 8.3 (docs) `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, antes do archive. O `02` traz a data do deploy e a data da retirada da `AUTH_TOKEN_SIGNING_KEY` do `.env.prod` (D4).
- [x] 8.4 (openspec) Archive com sync. O `Purpose` do `openspec/specs/connectors-scaffold/spec.md` cita a "chave compartilhada": a frase é corrigida à mão no sync, porque delta não muda `Purpose`. Depois do sync, a varredura de 7.7 roda também sobre `openspec/specs/`, e só a exceção do TOTP (`two-factor-totp`, "Código TOTP pelo RFC 6238") e o PBKDF2-HMAC-SHA256 de `local-users` sobram.
  - **Registro (04/11/2026, 22:40):** `openspec archive -y` aplicou as 14 deltas: 20 requisitos modificados, 2 removidos e 4 renomeados. O CLI datou a pasta com o dia UTC, `2026-11-05-remover-hmac-token-servico`. O `openspec validate --specs --strict` passou (86 de 86).
    - **Purposes corrigidos à mão:** o do `connectors-scaffold` (a "chave compartilhada" virou o access token do emissor, validado com as chaves públicas dele). A varredura com `--specs` achou **mais dois** que a tarefa não nomeava, também fora do alcance da delta: o do `issuer-token-validation` ("ao lado do token HMAC: escolha do esquema pela forma do token") e o do `oidc-issuer` ("a convivência com … a validação HMAC"). Os dois foram corrigidos do mesmo jeito (convenção 9: só texto de Purpose, nenhum requisito muda).
    - **Depois da correção:** além das exceções de antes, sobram o TOTP do `two-factor-totp` e o PBKDF2 de `local-users`, como previsto. Sobra também **uma exceção a mais**: no `operator-authentication`, a frase "`Auth:OperatorUsername` e `Auth:OperatorTokenLifetime` SHALL NOT ser lidas, e nenhum app SHALL ler chave simétrica de assinatura de token". Ela já existia desde a #217, a delta a manteve, e é uma proibição. Tirá-la enfraqueceria o contrato, então fica como exceção, do mesmo tipo do guarda do token antigo. A saída está em `~/.cache/buteco-agents/182D-verificacao/varredura-182-specs.txt`.
- [x] 8.5 (repo) PR com `Closes #182`, `Closes #117` e `Closes #122`, cada um em linha própria. Os três cards vão a In review. A #55 perde a `TokenSigningKey` da lista (comentário).
