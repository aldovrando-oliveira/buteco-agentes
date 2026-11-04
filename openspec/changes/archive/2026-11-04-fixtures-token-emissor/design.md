## Context

**Estado atual (`7dc7753`, merge do PR #239, 03/11/2026).** Desde a #235, nenhum serviço manda HMAC. O HMAC continua **aceito** nos três apps, e a suíte de integração ainda se autentica com ele de dois jeitos:

- **Implícito:** as fixtures padrão anexam o HMAC `operator` no `ConfigureClient`:
  - `apps/api`: a `ApiFactoryFixture` e mais cinco fixtures com `ConfigureClient` próprio (`A2ATaskLifecycleFixture`, `AgentDeactivationFixture`, `AgentProviderRejectionFixture`, `McpConnectionTestFixture` e `NoProvidersConfiguredFixture`), todas por `TestAuthentication.AttachOperatorToken`;
  - `apps/inbox`: a `InboxFactoryFixture`, a `OrchestrationFactoryFixture` e a `ReconciliationFactoryFixture`, que sobem com o emissor **desligado** (`UseIssuer("")`).
- **Explícito:**
  - o `apps/connectors` não tem token padrão: cada teste chama `TestAuthentication.CreateClientAs(factory, subject)` ou `IssueToken`, e a `ConnectorsFactory` padrão sobe com `Identity:Issuer` e `Api:BaseUrl` vazios;
  - no `apps/api`, os testes de subject assinam HMAC `service:*` direto pelo `ITokenService`;
  - em `tests/`, o `RoundTripFixture.OperatorClient` do `ApiConnectorsRoundTrip.Tests`.

**O desenho de referência** é o design da #182 (change `remover-hmac-token-servico`), preservado [num comentário da #182](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6593073248), sobre a base `14d4ba9`. Esta change implementa a seção D7 dele e o passo C de "Tamanho e divisão". Dos designs arquivados da #234 e da #235 vêm:

- o `ServiceClientTokens` (pedido de token dos clientes de serviço pelo `/connect/token` real);
- o `ServiceAccessTokenIssuer` estreito, cujo idioma de geração pela `IOpenIddictServerFactory` o `TestIssuerTokens` copia (H9 da #235: o principal vem pronto, com `oi_prst`);
- o `FakeTokenEndpoint`, o duplo do `/connect/token` que as fixtures do inbox e do connectors já usam.

### Medição: o que depende do HMAC hoje

**Como foi medido.** O worktree descartável `../buteco-agentes-236-medicao`, em `7dc7753`, recebeu uma linha no início do `TokenService.Validate` dos três apps: `if (token is not null) return TokenValidationResult.Invalid;`. As seis suítes rodaram com o `suite-236.sh`, cópia do `suite-235.sh`. A lista por nome sai do `.trx` pelo `nomes-trx.py`, com os GUID da #214 normalizados. As saídas completas estão em `~/.cache/buteco-agents/236-medicao/` (convenção 19), e a classificação por nome está em `classificacao.tsv` ao lado.

**Validade das rodadas** (regra assimétrica da #181: reprovação só conta com a CPU em 100 do início ao fim):

| suíte | rodada | CPU mínima | reprovados / total | veredito |
|---|---|---|---|---|
| `apps/api` inteira | r1 | 60 | 567 / 1.278 | inconclusiva |
| `apps/api` inteira | r2 | 41 | 567 / 1.278 | inconclusiva |
| `apps/api` em 5 partes (`--filter` por namespace) | r5 | 100 nas cinco | 3 + 162 + 122 + 4 + 276 = **567** / 1.278 | **válida** |
| `apps/inbox` | r1 | 100 | **88** / 322 | válida |
| `apps/connectors` | r1 | 100 | **65** / 338 | válida |
| `rt-inbox` | r1, r3, r4 | 100 | 2, 1, 2 / 15 | válidas, ver abaixo |
| `rt-connectors` | r1 | 100 | **8** / 10 | válida |
| `rt-crossapp` | r1 | 100 | **0** / 4 | aprovada |

- **O `apps/api` inteiro derruba a CPU sozinho.** Ela cai de 100 para 41–60 por volta dos 65 s de suíte, em toda rodada, inclusive na baseline limpa. Por isso a medição válida dele é em cinco partes, cada uma abaixo do tempo da queda (`fila-236-api-partes.sh`). As partes cobrem os 1.278 casos da baseline, por nome. Os 567 nomes reprovados são **idênticos** nas três rodadas (r1, r2 e r5).
- **`rt-inbox`.** O `OperatorHmacToken_SignedWithTheSharedKey_…` reprovou nas três rodadas. O `OrphanDispatchRoundTripTests.TerminalTask_CarriesStatusTimestamp_…(chatFails: True)` reprovou só na r1, e o `Source3_…` só na r4. Os dois são timeouts em `WaitForTaskIdAsync`, a assinatura da #176, e as chamadas deles usam o token de usuário do emissor, e não HMAC. **Não entram na medição.** Foram comentados na #176 ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/176#issuecomment-6621272879)).
- **Todas as reprovações são de autenticação.** As mensagens são `401`/`Unauthorized`, corpo vazio de `401` lido como JSON, ou timeout de quem esperava um ciclo que o `401` não deixou começar. Não apareceu nenhuma reprovação de outra natureza.

**Resultado: 729 casos dependem do HMAC hoje** (567 `apps/api`, 88 `apps/inbox`, 65 `apps/connectors`, 8 `rt-connectors` e 1 `rt-inbox`). O D7 de referência projetava até 779 implícitos mais 338 explícitos, contados por arquivo. A medição troca esse limite superior pelo número exato.

**Separação em dois grupos:**

| grupo | o que é | `api` | `inbox` | `connectors` | `tests/` | total |
|---|---|---|---|---|---|---|
| **G1** | prova uma regra **por meio** do HMAC: migra nesta change | 560 | 83 | 56 | 8 | **707** |
| **G2** | prova **só o HMAC**: intocado até a #182 | 7 | 5 | 7 | 1 | **20** |
| **Q3** | estado que a #182 elimina: fica no HMAC | — | — | 2 | — | **2** |

O G1 inclui os 20 casos de subject sem cliente (Q1). A Q3 são os 2 casos do connectors que exigem o app sem `Api:BaseUrl` (decisões do dono, abaixo).

**G2, os 20 casos medidos** (reprovam com o HMAC recusando tudo):

| suíte | caso | por que só prova o HMAC |
|---|---|---|
| api | `Identity.IssuerTokenAcceptanceTests.HmacToken_IsStillAccepted_AndAnInvalidOneStill401` | aceitação pela chave |
| api | `Identity.IdentityIssuerCoexistenceTests.InboxServiceToken_StillReachesItsRoute` | HMAC `service:inbox` convivendo com o emissor; o par do emissor é o `ServiceClientRouteTests` |
| api | `Knowledge.KnowledgeDocumentCatalogTests.DocumentWrites_WithOperatorHmacToken_RecordOperatorAsAuthor` | o autor registrado **pelo** HMAC `operator` |
| api | `Users.AccessLinks.AccessLinkCallerTests.OperatorHmac_Is403_OnTheFourRoutes_AndNothingIsWritten` | HMAC `operator` → `403` em `CurrentUser` (Q2) |
| api | `Users.Management.UserRoutesCallerTests.OperatorHmac_Is403_OnEveryRoute_AndNothingChanges` | idem (Q2) |
| api | `Users.TwoFactor.TwoFactorCallerTests.OperatorHmac_OnMyAccount_Is403` | idem (Q2) |
| api | `Users.TwoFactor.TwoFactorCallerTests.OperatorHmac_OnTheReset_Is403_AndChangesNothing` | idem (Q2) |
| inbox, connectors | `IssuerKeysLifecycleTests.BootsWithTheIssuerUnreachable_HmacWorks_AndIssuerTokenIsUnavailable` | o HMAC funcionando com o emissor fora |
| inbox, connectors | `IssuerTokenSchemeSelectionTests.OperatorHmacToken_StillAccepted_WithTheIssuerEnabled` | seletor por forma |
| inbox, connectors | `IssuerTokenSchemeSelectionTests.HmacTokens_ValidAndInvalid_NeverTouchTheIssuerKeys` | seletor por forma |
| inbox, connectors | `IssuerTokenSchemeSelectionTests.InvalidJwt_IsUnauthorized_WithoutCallingTheHmacValidator` | seletor por forma |
| inbox, connectors | `IssuerTokenStartupValidationTests.WithoutIssuer_ValidationIsOff_IssuerTokenIsUnauthorized_HmacWorks_AndBootWarns` | "só HMAC" sem `Identity:Issuer`, que a #182 transforma em falha de boot |
| connectors | `RouteAuthenticationTests.TokenDeOperadorAssinadoComAMesmaChave_Responde200` | aceitação pela chave |
| connectors | `IssuerTokenValidationTests.PanelToken_OnDescribeFolder_IsForbidden_LikeTheHmacOperator` | **misto:** afirma o painel e o HMAC `operator` no mesmo caso. Tirar a metade HMAC aqui afrouxaria a asserção, então ele fica inteiro e a #182 o reescreve |
| rt-inbox | `RoundTripTests.OperatorHmacToken_SignedWithTheSharedKey_IsAcceptedByInboxWithoutNetworkCallToApi` | já marcado para a #182 desde a #235 |

**G2 estático, 32 casos que a medição não enxerga.** Uma recusa total só derruba quem precisa que o HMAC seja **aceito**. Os casos que provam o HMAC pelo lado da recusa, ou o seletor em unidade, **passam** com o HMAC recusando tudo. Eles saíram de uma varredura dos métodos cujo corpo cita o HMAC e que não reprovaram (`estatico-236.py`), lida caso a caso. Os falsos positivos foram descartados: `TotpTests.NewSecret_HasTheSizeOfTheHmacOutput` é o HMAC do TOTP, e `IssuerTokenValidationTests.PanelToken_CanSyncNow_LikeTheHmacOperator` só cita o HMAC no nome.

| suíte | caso (casos de `Theory`) |
|---|---|
| api | `Identity.IssuerTokenSchemeSelectionTests.AnythingElse_GoesToTheHmacScheme` (6), `ThreeSegments_GoToTheIssuerScheme` (4) |
| api | `Identity.IssuerTokenAcceptanceTests.InvalidJwt_Is401_WithoutCallingTheHmacValidator` |
| inbox, connectors | `IssuerTokenSchemeSelectionTests.Selector_ChoosesBySegmentCount` (7 + 7) |
| inbox, connectors | `IssuerTokenValidationTests.Hs256SignedWithTheHmacKey_IsUnauthorized` (assina com a `TokenSigningKey`) |
| connectors | `RouteAuthenticationTests.TokenAssinadoComOutraChave_Responde401`, `TokenExpirado_Responde401`, `BootSemChaveDeAssinatura_Falha`, `BootComChaveDeAssinaturaVazia_Falha` (2) |

**G1, os 707 casos, por classe** (mais os 2 da Q3, contados no connectors):

- **`apps/api` (560):**
  - catálogo de conhecimento: `KnowledgeDocumentCatalogTests` 117, `KnowledgeBaseCatalogTests` 98, `KnowledgeDocumentUpdateTests` 17, `KnowledgeDocumentDeleteTests` 10, `KnowledgeDocumentReindexTests` 9, `KnowledgeDocumentIndexingContractTests` 8, `KnowledgeBaseIndexingSummaryTests` 8, `KnowledgeIndexDiagnosticsTests` 7, `KnowledgeRouteAuthenticationTests` 7, `KnowledgeEmptyCatalogTests` 2;
  - agentes: `AgentInsightsEndpointsTests` 31, `AgentEndpointsTests` 24, `AgentDelegationEndpointsTests` 21, `AgentKnowledgeBindingEndpointsTests` 20, `AgentMcpBindingEndpointsTests` 15, `AgentCardEndpointTests` 8, `AgentDeactivationTests` 5, `AgentA2AAddressesTests` 5, `AgentMcpBindingToolValidationTests` 4, `AgentUpdateProviderValidationTests` 4, `AgentCreateProviderValidationTests` 3, `AgentCardSecuritySchemeTests` 2;
  - MCP: `McpServerEndpointsTests` 27, `McpAuthHeaderValidationTests` 24, `McpConnectionTestEndpointsTests` 12, `McpServerCustomHeaderConnectionTests` 11, `McpServerToolsEndpointTests` 5;
  - outros: `InsightsEndpointsTests` 28, `ServiceScopeAuthorizationTests` 11, `SendMessageProviderRejectionTests` 6, `A2ATaskLifecycleTests` 5, `ProviderEndpointsTests` 2, `RouteAuthenticationTests` 2, `NoProvidersConfiguredTests` 1, `ServiceClientRouteTests` 1.
- **`apps/inbox` (83):** `ChannelEndpointsTests` 23, `MessageSummaryEndpointsTests` 20, `SessionSummaryEndpointsTests` 19, `SubjectAuthorizationTests` 7, `ContactEndpointsTests` 5, `MessagePersistenceTests` 5, `ChannelSessionEndpointsTests` 4.
- **`apps/connectors` (58):** `SubjectAuthorizationTests` 22, `ConnectorEndpointsTests` 20, `SyncEndpointsTests` 10, `SecretLeakTests` 3, `ConnectorRegistrationTests` 2, `ApiConfigurationValidationTests` 1.
- **`rt-connectors` (8):** `FolderValidationRoundTripTests`, todos pelo `RoundTripFixture.OperatorClient`.

Dos 707, **cerca de 680 migram sem tocar no corpo do teste**: o token muda na fixture. Os outros têm uma chamada explícita (~60 pontos de chamada, em 20 arquivos).

## Goals / Non-Goals

**Goals:**

- Os 707 casos do G1 passam com o token do emissor, com os mesmos nomes e as mesmas asserções.
- Depois da migração, a mesma rodada com o HMAC recusando tudo reprova **exatamente** os 20 do G2, por nome (critério de aceite, D6).
- Nenhum caso fora do G2 e do G2 estático usa `ITokenService`, `AttachOperatorToken`, `CreateClientAs`/`IssueToken` (HMAC) ou `TokenSigningKey` (critério da issue).
- O tempo das suítes não se multiplica (D7).

**Non-Goals:**

- **Remover qualquer caso ou peça do HMAC.** O `TokenService`, o seletor, a chave e os helpers HMAC das fixtures ficam. Saem na #182.
- **Mudar código de produção ou contrato.** Nenhum requisito de produção muda. O único delta é o requisito da suíte (Q5).
- **Reescrever o G2**, inclusive o caso misto: isso é da #182.
- **Teste novo de regra.** A change troca token. Os testes novos são só os do próprio helper (D1).

## Decisions

### D1. `apps/api`: `TestIssuerTokens`, pela `IOpenIddictServerFactory`

**`apps/api/tests/Buteco.Api.Tests/Support/TestIssuerTokens.cs`**, separado do `ServiceAccessTokenIssuer`, que continua estreito (decisão do dono, 02/11/2026). Ele copia o idioma de geração do issuer da #235: transação pela `IOpenIddictServerFactory`, principal pronto (`oi_crt_dt`, `oi_exp_dt`, `oi_aud`, `oi_scp`, `oi_prst`, `oi_iss`, `client_id`, `sub`) e `GenerateTokenContext` de access token JWT, sem entrada no banco.

- **Superfície:** `ForClient(IServiceProvider, string clientId, string subject)`, com `aud` `buteco-api`, escopo `buteco-api` e 2 h, e o atalho `Panel(IServiceProvider)`:
  - **`Panel`:** `client_id` `buteco-painel`, `sub` fixo `fixture-sem-usuario` (que não é id de usuário) e 2 h. É o comportamento do HMAC `operator` de hoje: passa nas rotas do catálogo, porque a tabela mapeia o `buteco-painel` para `operator`, e recebe `403` em `CurrentUser`, porque o `sub` não é usuário;
  - **`ForClient`:** os testes de subject. `buteco-servico-inbox` → `service:inbox`, `buteco-servico-connectors` → `service:connectors`.
- **2 h, e não os 15 min de produção:** o token é gerado uma vez por fixture e serve a classe inteira. 15 min venceriam no meio de uma classe lenta com a CPU estrangulada.
- **Cache por `IServiceProvider`** (`ConditionalWeakTable`): uma geração em memória por host, sem HTTP e sem Argon2.
- **Anel não carregado → nenhum cabeçalho.** O `ConfigureClient` é síncrono, e o `UnmigratedFactory` (`IdentityKeyRingGateTests`) chama `CreateClient()` **antes** de migrar, quando o anel não carrega. O helper só gera token com o `IdentityKeyRing.IsLoaded`, e sem ele o cliente sai sem `Authorization`. Antes de o anel carregar, o `IdentityKeyRingGate` responde `503` a toda rota, então o cabeçalho não muda nenhuma resposta. Os casos do gate continuam passando com os mesmos nomes. As fixtures que migram no `InitializeAsync` esperam o `ring.IsLoaded` depois do `MigrateAsync`, como o `IdentityKeyRingGateTests` já espera.
- **Guarda do próprio helper** (convenção 15): um teste `TestIssuerTokensTests` afirma que o token do `Panel` tem o mesmo conjunto de nomes de claims de um token do `/connect/token` do `buteco-painel`, que passa no `GET /agents` e que recebe `403` em `GET /account`. O guarda é verificado contra dois defeitos: o principal sem `oi_aud` e o `sub` de um usuário real.

**As seis fixtures** trocam o `AttachOperatorToken` por `TestAuthentication.AttachPanelToken(client, Services)`. O `AttachOperatorToken` (HMAC) fica, usado só pelos casos do G2 (Q2).

**Os casos de subject** (`ServiceScopeAuthorizationTests`, `KnowledgeRouteAuthenticationTests`, `KnowledgeSyncTestSeed`, `KnowledgeBaseCatalogTests.Deletion`) trocam `ITokenService.Issue(subject)` por `TestIssuerTokens.ForClient`. O cliente de cada subject sai da `IssuerClientSubjects` de produção, lida pelo teste, e não de uma tabela copiada.

**`WithWebHostBuilder`** (12 chamadas em 8 arquivos): o `ConfigureClient` do host derivado é o da fixture-mãe, e o token é gerado pelo anel da mãe. O host derivado lê o mesmo banco e as mesmas chaves, então aceita esse token. O caso que derivar com outra `PublicUrl:BaseUrl` receberia `401` pelo `iss`. Nenhum dos 8 arquivos deriva assim: o grep de `PublicUrl:BaseUrl` em `apps/api/tests` só acha a fixture e testes que não derivam dela.

### D2. `apps/inbox`: as três fixtures padrão sobem com o `TestIssuer`

- Cada fixture cria um `TestIssuer` (o da #208), troca o `UseIssuer()` por `UseIssuer(TestIssuer.Issuer)` e põe o handler dele como primário do cliente `IssuerKeys`. O `IssuerKeys` lê o discovery e o JWKS pelo `Api:BaseUrl` da fixture, e o handler responde pelo caminho, sem olhar o host.
- **Antes do primeiro cliente**, o `InitializeAsync` espera o `IssuerKeysState.Obtained` (o `Poll.UntilAsync` que o `IssuerTokenFactory` já usa). Sem isso, o primeiro pedido recebe o `503` da primeira obtenção.
- O `ConfigureClient` anexa `TestIssuer.Issue()`, que é o token `buteco-painel` com `aud` `buteco-inbox`, gerado no momento, com validade de 30 min por cliente.
- **Casos de subject** (`SubjectAuthorizationTests`): `operator` → `TestIssuer.Issue()`. Os subjects sem cliente são a Q1.
- O `TestAuthentication.AttachOperatorToken` (HMAC) fica, para o G2.

### D3. `apps/connectors`: a `ConnectorsFactory` padrão sobe com o `TestIssuer`

- Sem `issuer` explícito, a fábrica cria um `TestIssuer` próprio. O emissor **exige** `Api:BaseUrl` no `apps/connectors` (`IssuerTokenStartupValidation`), porque as chaves vêm por ele. Então, com o emissor ligado, a fábrica passa a configurar `Api:BaseUrl` = `TestIssuer.InternalBaseUrl`, como ela já faz quando recebe um `issuer`.
- **O agendador do ciclo é o efeito colateral.** Com `Api:BaseUrl`, o `SyncSchedulerService` entra e roda uma rodada logo depois do boot, contra o `FakeSyncApi` e o `FakeTokenEndpoint`. Isso somaria pedidos aos contadores que os testes afirmam. **Decisão (ajuste de teste, decisão do dono, 04/11/2026):** quando o teste não configurou o `Api:BaseUrl` nem passou `issuer`, o `ConfigureServices` da `ConnectorsFactory` remove do DI o `IHostedService` cuja implementação é o `SyncSchedulerService`. É uma substituição no DI da fixture, e não uma opção nova de configuração de produção (convenção 2): o `AddKnowledgeSync` e o `Program.cs` não mudam. Os testes do ciclo configuram o endereço e continuam com o agendador.
- **Guarda da remoção** (convenção 15): um caso da própria fábrica afirma que, sem `Api:BaseUrl` pedido, o `FakeSyncApi` e o `FakeTokenEndpoint` recebem zero pedidos durante 2 s depois do boot, e que, com `Api:BaseUrl` pedido, o agendador está entre os `IHostedService`. O guarda é visto reprovar com a remoção desligada.
- **`CreateIssuerClientAs(factory, clientId)`** e **`IssuerTokenFor(factory, clientId)`** (novos) substituem o `CreateClientAs`/`IssueToken` nos casos do G1. O `operator` usa `buteco-painel`, e o `service:api` usa `buteco-servico-api`. O `CreateClientAs`/`IssueToken` (HMAC) ficam, usados só pelo G2.
- **Os dois casos que exigem o app sem `Api:BaseUrl`** não têm token do emissor possível. São a Q3.

### D4. `tests/`

- **`ApiConnectorsRoundTrip.Tests`:** o `RoundTripFixture.OperatorClient(api)` passa a anexar o token do usuário real que o `apps/api` real emite pelo fluxo de login, como o `rt-inbox` já faz no `UserAccessTokenAsync`, em vez do `ITokenService.Issue("operator")`. O token é pedido uma vez por fixture. Os 8 casos mantêm o nome. A alternativa seria o `TestIssuerTokens` do projeto de teste do `apps/api`, mas o `rt-connectors` não referencia esse projeto e precisaria de link de arquivo. O fluxo real já existe no `rt-inbox` e prova mais.
- **`InboxOrchestratorRoundTrip.Tests`:** não muda. O único caso que depende do HMAC é o `OperatorHmacToken_…` (G2). A `Auth:TokenSigningKey` da fixture fica até a #182.
- **`CrossAppTaskStoreCompatibility.Tests`:** não muda (0 casos).

### D5. Nomes, asserções e renomeados

- Nenhum caso muda de nome nem de asserção. O corpo muda só na linha do token.
- **Lista de renomeados: vazia** (Q1, decisão do dono). A `Theory` de subject continua recebendo o subject, e o helper o converte em cliente: o `client_id` da tabela de produção que produz aquele subject, ou, se nenhum produz, um `client_id` igual ao texto do subject, desconhecido da tabela.
- Os nomes que citam o HMAC sem depender dele (`PanelToken_CanSyncNow_LikeTheHmacOperator`) ficam como estão. Quem renomeia é a #182, que remove o termo de comparação.

### D6. Critério de aceite e a lista da #182

1. **As suítes dos três apps e de `tests/` passam com os mesmos nomes da baseline r1** (`~/.cache/buteco-agents/236-baseline/r1/nomes.txt`: 1.967 resultados), pela comparação por nome do `compare-trx.py`. A exceção é a #176, pela regra dela.
2. **A rodada com o HMAC recusando tudo**, a mesma linha do worktree descartável aplicada sobre a árvore da change, reprova **exatamente os 20 do G2 e os 2 da Q3**, por nome. Não pode reprovar nem mais nem menos. O `apps/api` roda em partes, como na medição.
3. **Varredura estática:** nenhum método fora do G2, do G2 estático e da Q3 cita `ITokenService`, `AttachOperatorToken`, `CreateClientAs`, `IssueToken`, `OperatorHmacToken`, `TokenService` ou `TokenSigningKey`. A varredura é a do `estatico-236.py`, invertida (`varredura-236.py`).
4. **Lista de remoções da #182** = G2 (20) ∪ G2 estático (32) ∪ Q3 (2), publicada como comentário na #182 no fechamento. Cada caso leva o motivo, o caso misto vai marcado como "reescrever", e os dois da Q3 vão marcados como "estado que a #182 elimina".

### D7. Tempo das suítes

**Antes** (baseline `7dc7753`, worktree limpo):

| suíte | duração | CPU mínima | resultado |
|---|---|---|---|
| `apps/api` inteira (r1) | 269 s | 44 | 1.277 aprovados, 1 ignorado. A CPU cai em toda rodada, então a duração não compara |
| `apps/api` em 5 partes (r2) | 48 + 34 + 66 + 69 + 81 = **298 s** | 100 nas cinco | 1.277 aprovados, 1 ignorado, os mesmos da r1 |
| `apps/inbox` (r1) | 52 s | 100 | 322/322 |
| `apps/connectors` (r1) | 16 s | 100 | 337 + 1 ignorado |
| `rt-inbox` (r1) | 55 s | 100 | 15/15 |
| `rt-connectors` (r1) | 28 s | 100 | 10/10 |
| `rt-crossapp` (r1) | 19 s | 100 | 4/4 |

**Regra de comparação:** só rodadas com a CPU em 100 do início ao fim. **O `apps/api` inteiro não compara**, porque a própria suíte derruba a CPU em toda rodada. Ele compara pela soma das cinco partes, cada uma em CPU 100, antes e depois. Os outros comparam a suíte inteira. **Limite:** nenhuma suíte passa de 1,5× a duração de antes. Acima disso, a migração para e o custo é medido antes de seguir.

**O custo esperado é pequeno:**

- `apps/api`: uma geração em memória por host;
- `apps/inbox`: três fixtures passam a esperar a primeira obtenção de chaves, ~25 ms de poll contra o handler em memória;
- `apps/connectors`: uma obtenção por fábrica, e a fábrica é criada por teste. Nos 337 casos, é o custo que mais pode pesar. Fica medido, e não estimado.

### D8. Ordem do apply

1. `TestIssuerTokens` e o guarda dele (D1), antes de qualquer fixture depender.
2. `apps/api`: fixtures e casos de subject. Suíte em partes e comparação por nome.
3. `apps/inbox`, depois `apps/connectors`, cada um com a suíte e a comparação por nome.
4. `tests/` (`rt-connectors`).
5. A rodada do critério 2 do D6, no worktree descartável, sobre a árvore da change.
6. O comentário da lista na #182, o `02` e o `CHANGELOG.md`.

## Árvore de pastas proposta

```
apps/api/tests/Buteco.Api.Tests/
├── Support/
│   ├── TestIssuerTokens.cs                    NOVO: Panel e ForClient pela IOpenIddictServerFactory, cache por host (D1)
│   ├── TestAuthentication.cs                  + AttachPanelToken; AttachOperatorToken (HMAC) fica para o G2
│   ├── ApiFactoryFixture.cs                   ConfigureClient → AttachPanelToken; espera o anel depois de migrar
│   ├── A2ATaskLifecycleFixture.cs             ConfigureClient → AttachPanelToken
│   ├── AgentDeactivationFixture.cs            idem
│   ├── AgentProviderRejectionFixture.cs       idem
│   ├── McpConnectionTestFixture.cs            idem
│   └── NoProvidersConfiguredFixture.cs        idem
├── Identity/TestIssuerTokensTests.cs          NOVO: guarda do helper (D1)
├── ServiceScopeAuthorizationTests.cs          HMAC service:* → ForClient (cliente desconhecido em 2 casos, Q1)
└── Knowledge/
    ├── KnowledgeRouteAuthenticationTests.cs   idem
    ├── KnowledgeSyncTestSeed.cs               idem
    └── KnowledgeBaseCatalogTests.Deletion.cs  idem

apps/inbox/tests/Buteco.Inbox.Tests/
├── Support/
│   ├── TestAuthentication.cs                  + AttachPanelToken(TestIssuer); AttachOperatorToken fica
│   ├── InboxFactoryFixture.cs                 UseIssuer(TestIssuer.Issuer), IssuerKeys → TestIssuer, espera Obtained (D2)
│   ├── OrchestrationFactoryFixture.cs         idem
│   └── ReconciliationFactoryFixture.cs        idem
└── SubjectAuthorizationTests.cs               operator → TestIssuer; subjects sem cliente: Q1

apps/connectors/tests/Buteco.Connectors.Tests/
├── Support/
│   ├── TestAuthentication.cs                  + CreateIssuerClientAs, IssuerTokenFor; CreateClientAs/IssueToken (HMAC) ficam
│   └── ConnectorsFactory.cs                   TestIssuer por padrão; sem agendador quando o teste não pediu Api:BaseUrl (D3)
├── ConnectorEndpointsTests.cs, SyncEndpointsTests.cs, SecretLeakTests.cs,
│   SubjectAuthorizationTests.cs, ConnectorRegistrationTests.cs,
│   ApiConfigurationValidationTests.cs         CreateClientAs → CreateIssuerClientAs (Q1); os 2 da Q3 ficam no HMAC

tests/ApiConnectorsRoundTrip.Tests/Support/RoundTripFixture.cs   OperatorClient → token do usuário real (D4)

openspec/changes/fixtures-token-emissor/       proposal, design, tasks
└── specs/{api,inbox,connectors}-scaffold/      ADDED: a suíte de integração autentica pelo token do emissor (Q5)
docs: 02, CHANGELOG.md
```

`apps/workers`, `apps/frontend`, o código de produção dos cinco apps, `libs/` e o deploy não mudam. Nenhum pacote novo.

## Tamanho

**Âncora:** o D7 de referência projetou ~1.500 linhas de teste, contando os 32 arquivos de HMAC explícito reescritos. A medição mostra outra forma: dos 707 casos, ~680 migram pela fixture sem tocar no corpo, e o resto são ~60 linhas de chamada.

| componente | testes | duplos e suporte |
|---|---|---|
| `TestIssuerTokens` e guarda (D1) | +70 | +90 |
| fixtures do `apps/api` (6) e `TestAuthentication` | +40 / −10 | — |
| casos de subject do `apps/api` | ±30 | — |
| fixtures do `apps/inbox` (3) e `TestAuthentication` | +60 / −10 | — |
| `ConnectorsFactory`, `TestAuthentication` do connectors | +50 / −5 | — |
| chamadas do connectors (~45 linhas) | ±45 | — |
| `rt-connectors` (`RoundTripFixture`) | +30 / −10 | — |
| **total** | **~+330 / −80** | **~+90** |

**Pela correção já medida da convenção 18** (`compactacao-historico`, 22/09/2026: as linhas erraram +70%, quase tudo em duplo de teste), o intervalo realista é **+330 a +560**, com o `TestIssuerTokens` e o guarda dele como o item mais incerto. **Cabe num PR revisável. Não proponho divisão.** Se a Q1 sair pela alternativa (renomear a `Theory`), o total não muda de ordem.

## Risks / Trade-offs

- **[Ligar o emissor na fixture do inbox e do connectors põe um serviço em segundo plano a mais em todo host]** → é o `IssuerKeysRefreshService` da produção, contra o handler em memória. O risco é de tempo, medido no D7, e de contagem: os testes que contam pedidos ao `TestIssuer` já usam a instância deles.
- **[A remoção do `SyncSchedulerService` na fábrica padrão do connectors esconde um efeito de produção]** → em produção, o emissor ligado implica o ciclo ligado. A fábrica só desliga o agendador quando o teste não pediu o endereço. Os testes do ciclo e o `rt-connectors` continuam com ele.
- **[Token de 2 h no `apps/api` é diferente do de produção]** → a duração não é o que esses casos provam. O contrato do token é guardado pelo `TestIssuerTokensTests` contra o `/connect/token`.
- **[Q1 muda o caminho da recusa nos subjects sem cliente]** → ver Q1. O resultado (`403`) e o nome ficam iguais, e a troca de premissa é registrada.
- **[A medição por recusa não acha o HMAC provado pela recusa]** → G2 estático e critério 3 do D6.
- **[A #176 cai numa rodada de fechamento]** → regra da #176: comentar lá. Reprovação nos dois worktrees não bloqueia. Reprovação só no da change: para.

## Migration Plan

Sem deploy e sem configuração. O merge só muda testes. **Rollback:** reverter o commit.

## Decisões do dono (04/11/2026)

Na revisão deste proposal e deste design, as cinco perguntas foram aceitas como recomendadas.

- **Q1. Os 20 casos de subject sem cliente migram** com um `client_id` desconhecido da tabela, igual ao texto do subject, e com o mesmo nome. **Troca de premissa registrada:** com o HMAC, esses casos chegavam à tabela de rotas com o subject preenchido e fora dela. Com o token do emissor, o cliente fora da `IssuerClientSubjects` autentica sem `NameIdentifier`, e o mesmo handler recusa por subject nulo. O `403` e o nome não mudam. **Motivo:**
  - o ramo "subject preenchido e fora da tabela de rotas" **só é alcançável pelo HMAC**. Pelo emissor, o subject sai da `IssuerClientSubjects`, e a checagem de boot `ValidateIssuerClientSubjects` impede um cliente de apontar para subject sem rota. Com a #182, o ramo fica sem caminho que o alcance. Manter um caso que o provasse seria a forma da #83 da convenção 25: o guarda mantendo vivo um código que ninguém mais alcança;
  - o caso vizinho que continua possível, **cliente de serviço numa rota fora da lista dele**, já está coberto pela #234 (`151ad99`):
    - `apps/api`: `Identity.ServiceClientRouteTests.TheInboxClient_ReadsAnAgent_AndIsForbiddenOutsideItsRoutes` e `TheConnectorsClient_ListsTheSyncedBases_AndIsForbiddenOutsideItsRoutes`;
    - `apps/connectors`: `IssuerTokenValidationTests.ApiServiceClient_OpensDescribeFolder_AndIsForbiddenOnTheOperatorRoutes` e `ApiServiceClient_WithoutTheConnectorsAudience_IsUnauthorized`;
  - o caminho do cliente fora da tabela já tem caso próprio desde a #208 (`07567f1`): `IssuerTokenValidationTests.ClientOutsideTheTable_IsForbidden`, no `apps/inbox` e no `apps/connectors`. O `apps/inbox` não tem cliente de serviço na tabela, porque nenhum serviço o chama.
- **Q2. Os 4 casos "HMAC `operator` → `403` em `CurrentUser`" ficam no G2**, como o D7 de referência decidiu.
- **Q3. Terceira lista, "estado que a #182 elimina":** `SyncEndpointsTests.WithoutApiBaseUrl_Is503SyncNotConfigured_WithoutAnyCall` e `ApiConfigurationValidationTests.RealComposition_WithoutBaseUrl_StartsWarnsAndServesNavigation`, do `apps/connectors`. Os dois ficam no HMAC e entram na rodada do critério 2 do D6.
- **Q4. Critério de aceite em três partes** (D6): a rodada por nome, a varredura estática e a lista. **A lista da #182 = G2 ∪ G2 estático ∪ Q3**, publicada como comentário na #182 no fechamento.
- **Q5. Sem `specs/`**, salvo se o `openspec validate --strict` exigir delta. **Exigiu** ("Change must have at least one delta"). O delta é o critério de aceite da issue como requisito da suíte, ADDED nas três specs de scaffold (`api-scaffold`, `inbox-scaffold`, `connectors-scaffold`), que já guardam os requisitos de testes automatizados de cada app. Nenhum requisito de produção muda.
- **E um ajuste:** o desligamento do agendador do connectors é ajuste de teste, uma substituição no DI da fixture, e não opção nova de configuração de produção (convenção 2; D3).

## Correções na implementação (04/11/2026, convenção 9)

Nenhuma muda comportamento de produção, contrato ou cobertura. Todas são do desenho dos helpers de teste.

- **C1. O token do `Panel` não leva `oi_au_id`** (D1). O guarda de forma comparou com um token do painel do fluxo real e achou um claim a mais: `oi_au_id`, a autorização, que é a sessão do usuário. O token da fixture não tem usuário nem sessão. Quem lê esse claim são as rotas de sessão da conta, que já respondem `403` a ele. Inventar um id criaria uma sessão que não existe. O `TestIssuerTokensTests` compara o conjunto de nomes com essa única exceção, nomeada no teste.
- **C2. O token é carimbado pelo relógio de parede, e não pelo `TimeProvider` do host** (D1). A primeira rodada do `apps/api` reprovou 59 casos com `401`, todos em `InsightsEndpointsTests` e `AgentInsightsEndpointsTests`. Essas fixtures fixam o `TimeProvider` em 23/09/2026, o OpenIddict carimbou `iat`/`exp` por ele, e o `JwtBearer` do `apps/api` confere o `exp` pelo relógio de parede. Em produção os dois são o mesmo relógio. O "regerar quando o `FakeTimeProvider` avança" que eu tinha posto no cache era premissa falsa, porque o validador ignora esse relógio. O cache confere pelo relógio de parede.
- **C3. O anel antes do token** (D1). Primeiro desenho: sem o anel carregado, o helper devolvia `null`. Isso deixaria sem token, ou com o token errado, os hosts derivados por `WithWebHostBuilder`, cujo anel o aquecimento carrega em segundo plano. Desenho que entrou:
  - o helper **espera** o anel do host carregar (até 20 s), sem dispará-lo, porque os testes do gate contam as cargas;
  - as seis fixtures chamam `IdentityKeyRing.EnsureLoadedAsync` depois de migrar, o mesmo caminho do primeiro pedido no gate, em vez de esperar o aquecimento, que repete a cada 5 s;
  - a `ApiFactoryFixture` só anexa o token depois de migrar, e o `UnmigratedFactory` cria cliente sem cabeçalho antes disso.
- **C4. O emissor do connectors liga quando o teste pede o token, e não em toda fábrica** (D3). Ligar o emissor em toda `ConnectorsFactory` mudaria o ambiente dos casos de boot e de configuração que não chamam rota (`IssuerTokenStartupValidationTests`, `ApiConfigurationValidationTests` com `ftp://`, os da chave), sem que eles dependam do HMAC. A fábrica tem um `TestIssuer` próprio. O `TestAuthentication.CreateIssuerClientAs(factory, subject)` o liga antes de o host subir (`UseIssuer`), e lança erro claro se o host já subiu sem ele. O helper recebe o subject, como o do `apps/api`, e acha o cliente pela tabela de produção. Os 2 casos da Q3 nunca pedem o token e ficam como estavam.
- **C5. `rt-connectors`: o `SyncCycle` também chamava o `apps/connectors` com HMAC `operator`** (D4). O `RoundTripFixture` ganhou o `ConnectorsOperatorClientAsync`, com o token do Admin para `buteco-connectors`. Ele espera o `apps/connectors` obter as chaves, porque antes disso a rota responde `503`. O `WarmUpAsync`, que estava duplicado no `IssuerTokenRoundTripTests`, passou à fixture.
- **C6. `apps/inbox`: o `TestAuthentication.AttachOperatorToken` ficou sem chamador** (convenção 25) e saiu: os casos do G2 usam o `IssuerTokenFactory.OperatorHmacToken()`. O `TestIssuer` do inbox ganhou o "segurar as chaves" do guarda da 3.1, só nesta cópia, porque o guarda é das fixtures do inbox. As fixtures ganharam o `IssuerKeysWait` (15 s), que só o guarda encurta.
- **C7. O primeiro guarda da 3.1 estava errado.** Ele segurava as chaves por 10 s e soltava. O cliente `IssuerKeys` tem timeout fixo de 5 s, e a primeira busca falhada só repete depois de 10 s. A espera de 15 s da fixture vencia, e as três fixtures reprovaram na rodada r3. O guarda que entrou segura as chaves do início ao fim: a fixture que espera termina sempre em `TimeoutException`, e a que não espera termina bem e recebe `503` no primeiro pedido.
- **C8. A varredura estática achou um caso misto fora das listas:** `SecretLeakTests.NenhumLogDoCicloContemConteudoTokenChaveOuNomeDeArquivo`, do G1 (migrou de token), afirma também que nenhum log contém a `TokenSigningKey`. Tirar a asserção agora seria afrouxá-la. Ele fica como está e entra na lista da #182 como "reescrever", ao lado do `PanelToken_OnDescribeFolder_IsForbidden_LikeTheHmacOperator`. **A lista da #182 passa de 54 para 55 casos.** Os outros quatro achados da primeira versão da varredura eram falsos positivos do script (corte do corpo, comentário histórico, homônimo do helper do `apps/api`), corrigidos nele.

## Open Questions

Respondidas em "Decisões do dono", acima. O texto original de cada pergunta fica abaixo, como registro.

Cada uma com a recomendação.

- **Q1. Os 20 casos cujo subject não sai de nenhum `client_id` da tabela do app.** São eles:
  - `apps/api`: `ServiceScopeAuthorizationTests.ApiOutgoingSubject_OnKnowledgeBaseRoutes_ReturnsForbidden` (`service:api` no `apps/api`) e `UnknownSubject_OnOperatorRoute_ReturnsForbidden`;
  - `apps/connectors`: `SubjectAuthorizationTests.SubjectXRota` com `qualquer`, `service:connectors` e `service:inbox` (12 casos);
  - `apps/inbox`: `SubjectAuthorizationTests.GetChannels_WithSubjectOtherThanOperator_ReturnsForbidden` (`Operator`, `service:connectors`, `service:desconhecido`, `service:inbox`), `CreateChannel_WithServiceSubject_ReturnsForbiddenAndCreatesNothing` e `EveryAuthenticatedRoute_WithServiceSubject_ReturnsForbidden`.

  Com o HMAC, eles chegam à tabela de rotas com o subject preenchido, que não está nela, e recebem `403`. Com o token do emissor, o cliente que não está na `IssuerClientSubjects` autentica **sem** `NameIdentifier`, e o mesmo handler de autorização recusa por subject nulo. O `403` e o handler são os mesmos, mas o dado de entrada não é. Depois da #182, o caminho "subject preenchido e fora da tabela" só é alcançável por um cliente mapeado para um subject sem rota, e a checagem de boot `ValidateIssuerClientSubjects` impede isso. É troca de premissa, e por isso pergunto antes (critério de parada).
  - **Recomendo:** migrar com um `client_id` igual ao texto do subject (cliente desconhecido), mantendo o parâmetro e o nome da `Theory`, e registrar a troca de premissa no design, como a #235 fez com o `ConnectorsWithKeysFromAnotherIssuer`. O que esses casos protegem, "token que não é do operador não abre rota do operador", continua provado pelo caminho que existirá depois da #182. **O que se perde, e precisa da sua concordância:** o ramo "subject preenchido e fora da tabela de rotas" deixa de ser exercitado por requisição. No `apps/connectors`, ele continua coberto em unidade pelo `SubjectRouteValidationTests`. No `apps/api` e no `apps/inbox`, não achei teste de unidade da tabela com subject desconhecido. O ramo fica guardado só pela checagem de boot `ValidateIssuerClientSubjects`, que impede um cliente de chegar a ele.
  - **Alternativa:** mantê-los no HMAC e passá-los ao G2. A #182 os removeria. Isso reduz cobertura e exige a sua aprovação.
- **Q2. Os 4 casos "HMAC `operator` → `403` em `CurrentUser`"** (`AccessLinkCallerTests`, `UserRoutesCallerTests` e `TwoFactorCallerTests` ×2). O D7 de referência os põe no "só prova o HMAC". O mesmo `403` sai para o token `Panel` sem usuário.
  - **Recomendo:** mantê-los no G2, como o D7 de referência decidiu. Um painel com `sub` que não é usuário não existe em produção, então migrar provaria um estado artificial. O substituto real, um cliente de serviço em `CurrentUser` → `403`, é teste novo e fica para a #182 nomear.
  - **Alternativa:** migrar para o `Panel` com nome novo (`PanelTokenWithoutUser_Is403…`), o que entraria na lista de renomeados.
- **Q3. Os 2 casos do connectors que exigem o app sem `Api:BaseUrl`** (`SyncEndpointsTests.WithoutApiBaseUrl_Is503SyncNotConfigured_WithoutAnyCall` e `ApiConfigurationValidationTests.RealComposition_WithoutBaseUrl_StartsWarnsAndServesNavigation`). Sem `Api:BaseUrl`, o emissor não liga (`IssuerTokenStartupValidation`), e o único token aceito é o HMAC. Não há migração que mantenha o estado que eles provam.
  - **Recomendo:** mantê-los no HMAC nesta change, fora da lista "só prova o HMAC", numa terceira lista: "estado que a #182 elimina". Na #182, com `Identity:Issuer` obrigatório (Q5 de lá), o `Api:BaseUrl` também passa a ser obrigatório no connectors, e esses casos viram falha de boot. A #182 decide os dois com essa regra. O critério 2 do D6 passa a ser "reprova exatamente os 20 do G2 **e** estes 2".
  - **Alternativa:** migrá-los configurando `Api:BaseUrl` vazio só depois do boot. Isso falsificaria o estado que eles provam.
- **Q4. O critério de aceite (b) com o G2 estático.** A rodada com a recusa total só enxerga os 20 do G2 medido. Os 32 do G2 estático passam nela antes e depois.
  - **Recomendo:** critério em três partes (D6): a rodada por nome (exatamente G2, mais a Q3 se aceita), a varredura estática, e a lista da #182 = G2 ∪ G2 estático.
  - **Alternativa:** uma segunda mutação (HMAC aceitando tudo como `operator`), que derrubaria os do lado da recusa. Ela também derrubaria casos do G1 que afirmam `401` com token malformado, e a lista ficaria mais ruidosa que a varredura.
- **Q5. `specs/` vazio.** Nenhum requisito muda, então não há delta de spec. **Recomendo:** a change sem `specs/`. Se o `openspec validate` exigir delta, eu paro e trago o erro, sem inventar um requisito. Os cenários que citam o HMAC saem na #182.
