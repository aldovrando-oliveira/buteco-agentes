Cada guarda marcado **(guarda)** só conta depois de visto reprovar contra o defeito que existe para pegar (convenção 15):

1. o defeito nomeado é reintroduzido;
2. o teste roda e reprova;
3. a árvore volta, só no trecho reintroduzido.

Fica registrado na tarefa:

- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes (guarda reintroduz só o defeito nomeado).

Suítes pelo `~/.cache/buteco-agents/bin/suite-236.sh`, com a regra assimétrica: verde conta mesmo com a CPU caindo, e reprovação só conta com a CPU em 100 do início ao fim. O `apps/api` roda inteiro para o veredito verde e em cinco partes (`fila-236-api-partes.sh`, `WT=<worktree>`) para tempo e para reprovação válida, porque a suíte inteira derruba a CPU em toda rodada.

A baseline é `~/.cache/buteco-agents/236-baseline/`, em `7dc7753`:

- **r1:** as seis suítes, com o relatório por nome em `nomes.txt` (1.967 resultados, GUID normalizado, #214);
- **r2:** o `apps/api` em cinco partes, 298 s, todas com CPU 100.

A medição é `~/.cache/buteco-agents/236-medicao/`, com a classificação em `classificacao.tsv`.

**#176:** uma reprovação do `OrphanDispatchRoundTripTests` (`Source3_…` ou o irmão `TerminalTask_…`, timeout em `WaitForTaskIdAsync`) é comentada na #176. Reprovação nos dois worktrees não bloqueia. Reprovação só no worktree da change: PARE.

**Parada:** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura. Correção que só toca comentário ou documentação segue e fica registrada (convenção 9). Nenhum caso é removido, nenhuma asserção é afrouxada, e nenhum caso muda de nome (D5). Se algum precisar, PARE.

## 1. `TestIssuerTokens` (apps/api), antes de qualquer fixture depender dele

- [x] 1.1 (apps/api) `Support/TestIssuerTokens.cs` (D1): `Panel`, `ForClient(services, clientId, subject)` e `ForSubject(services, subject)`.
  - Mesmo idioma de geração do `ServiceAccessTokenIssuer`: transação pela `IOpenIddictServerFactory`, e principal com `oi_crt_dt`, `oi_exp_dt` (2 h), `oi_aud` e `oi_scp` `buteco-api`, `oi_prst` = `client_id` e `oi_iss`, mais `client_id` e `sub`.
  - `Panel`: `client_id` `buteco-painel`, `sub` `fixture-sem-usuario`.
  - Cache por `IServiceProvider` e por `(clientId, subject)`, numa `ConditionalWeakTable`.
  - **Correções (design C2 e C3):** `iat`/`exp` e o cache pelo relógio de parede, que é o do `JwtBearer`, e não pelo `TimeProvider` do host. O helper **espera** o anel carregar (até 20 s, sem dispará-lo), em vez de devolver `null`.
  - O `ServiceAccessTokenIssuer` de produção não muda: diff vazio em `apps/*/src`.
- [x] 1.2 (apps/api) `Identity/TestIssuerTokensTests` (3 casos):
  - `PanelToken_HasTheShapeOfARealPanelToken`: o mesmo `typ`, `alg`, `kid` e `iss` de um token do painel do fluxo real, e o mesmo conjunto de nomes de claims, menos o `oi_au_id` (design C1, a exceção nomeada no teste);
  - `PanelToken_PassesTheCatalog_AndIs403OnTheCurrentUserRoutes`: com um usuário no banco, `GET /agents` → `200` e `GET /account` → `403`;
  - `ServiceClientToken_StaysOnItsRoutes`: o `buteco-servico-inbox` lê um agente (`200`) e recebe `403` em `GET /agents`.

  **(guarda)** Vistos reprovar no código final (`236-verificacao/guardas-final/`):
  - sem `oi_aud` → os 3 reprovam (`Assert.Equal() Failure: Collections differ` no de forma); voltou: `Support/TestIssuerTokens.cs` (sha igual ao de antes: `115476f0…`);
  - `sub` do `Panel` = id do primeiro usuário do banco → os 3 reprovam (`Strings differ` no de forma, `Values differ` no `403`); voltou: idem, sha igual.
- [x] 1.3 (apps/api) `TestAuthentication.AttachPanelToken(HttpClient, IServiceProvider)`. O `AttachOperatorToken` (HMAC) fica, com comentário que nomeia os `OperatorHmac_` do G2.

## 2. Fixtures e casos do apps/api

- [x] 2.1 (apps/api) As seis fixtures anexam o `AttachPanelToken` e chamam `IdentityKeyRing.EnsureLoadedAsync` depois de migrar (design C3). A `ApiFactoryFixture` só anexa depois de migrar, e o `UnmigratedFactory` cria cliente sem cabeçalho antes disso.

  **(guarda)** Defeito: a fixture anexa token antes de migrar (`if (_migrated)` → `if (true)`). Reprova o `IdentityKeyRingGateTests.BeforeMigration_EveryRouteIs503WithRetryAfter_AndAfterMigrationTheJwksAnswersWithoutRestart`, com `TimeoutException: O anel do emissor não carregou em 20 s: o banco do host foi migrado?`. Voltou: `Support/ApiFactoryFixture.cs` (sha igual ao de antes: `be747598…`).
- [x] 2.2 (apps/api) Os casos de subject passam ao `TestIssuerTokens.ForSubject`, que acha o cliente pela `IssuerClientSubjects` de produção. Subject sem cliente vira `client_id` igual ao texto do subject (Q1). São eles:
  - `ServiceScopeAuthorizationTests.ServiceScopedClient`;
  - `KnowledgeRouteAuthenticationTests`;
  - `KnowledgeSyncTestSeed.CreateClientAs`, de onde vêm `ApiOutgoingSubject_…` e `UnknownSubject_…`;
  - os 4 `AttachOperatorToken` de hosts derivados (`KnowledgeBaseCatalogTests.Deletion` e `.SyncedCreation`), que passam ao `AttachPanelToken`.

  O comentário do `IdentityIssuerCoexistenceTests`, que dizia que toda fixture provava o HMAC, foi corrigido.

  **(guarda)** Defeito: todo subject vira cliente desconhecido. Reprovam o `ServiceToken_OnGetAgentById_IsAuthorized`, o `ServiceToken_OnA2ASendMessage_IsAuthorized` e o `ConnectorsToken_IsAuthorizedOnEverySyncRoute` (`Values differ`). Voltou: `Support/TestIssuerTokens.cs` (sha igual ao de antes: `115476f0…`).
- [x] 2.3 (apps/api) **r1, a suíte inteira:** 59 reprovações, todas `401` em `InsightsEndpointsTests` e `AgentInsightsEndpointsTests`, numa rodada inconclusiva pela CPU (mínima 39). O defeito era determinístico: o design C2, corrigido. **r3, em cinco partes, com a CPU em 100 nas cinco:** 1.280 aprovados e 1 ignorado. Por nome, os 1.278 da baseline estão presentes com o mesmo resultado, mais 3 novos (`TestIssuerTokensTests`). Tempo: 50 + 32 + 57 + 58 + 67 = **264 s**, contra 298 s na baseline.

## 3. Fixtures e casos do apps/inbox

- [x] 3.1 (apps/inbox) As três fixtures:
  - têm um `TestIssuer` próprio (`Issuer`), `UseTestIssuer(Issuer)` e o handler dele como primário do `IssuerKeys`;
  - no `InitializeAsync`, esperam o `IssuerKeysState.Obtained` por `IssuerKeysWait` (15 s);
  - no `ConfigureClient`, anexam `Issuer.Issue()`.

  O `AttachOperatorToken` do inbox ficou sem chamador e saiu (design C6).

  **Guarda determinístico** (pedido do dono). O `TestIssuer` do inbox ganhou `HoldKeys`/`ReleaseKeys`. O `FixtureIssuerKeysWaitTests.Initialize_WithTheKeysHeld_DoesNotFinishWithoutThem`, uma `Theory` sobre as três fixtures, segura as chaves do início ao fim, com a espera em 2 s. A fixture que espera termina sempre em `TimeoutException("as chaves do emissor de teste não foram obtidas")`. O primeiro desenho do guarda estava errado (design C7).

  **(guarda)** Defeito: tirar a linha da espera, uma fixture de cada vez. As três reprovam com `A fixture terminou a inicialização com as chaves do emissor seguras; o primeiro pedido respondeu 503`. Voltou: `InboxFactoryFixture.cs` (`d05a469d…`), `OrchestrationFactoryFixture.cs` (`70ad48cb…`) e `ReconciliationFactoryFixture.cs` (`33ddf9f5…`), todos com o sha igual ao de antes.
- [x] 3.2 (apps/inbox) `SubjectAuthorizationTests.ClientAs`: o cliente sai da `IssuerClientSubjects`, e subject sem cliente vira `client_id` desconhecido (Q1). Os nomes e as asserções não mudam.

  **(guarda)** Defeito: o cliente desconhecido vira o painel. Reprovam os 4 `GetChannels_WithSubjectOtherThanOperator_ReturnsForbidden`, o `EveryAuthenticatedRoute_WithServiceSubject_ReturnsForbidden` e o `CreateChannel_WithServiceSubject_ReturnsForbiddenAndCreatesNothing`. Voltou: `SubjectAuthorizationTests.cs` (sha igual ao de antes: `9800b670…`).
- [x] 3.3 (apps/inbox) **r4**, CPU em 100: 325/325. Por nome, os 322 da baseline estão presentes e aprovados, mais 3 novos (o guarda da 3.1). Tempo: **52 s**, contra 52 s. Na r3, o primeiro desenho do guarda reprovou 3 casos (design C7).

## 4. Fixture e casos do apps/connectors

- [x] 4.1 (apps/connectors) `ConnectorsFactory` (D3 e design C4). A fábrica tem um `TestIssuer` próprio (`Issuer`), ligado por `UseIssuer()` antes de o host subir, com erro claro se ele já subiu sem o emissor. Ligado o emissor e sem `Api:BaseUrl` pedido pelo teste, o `ConfigureServices` remove o `IHostedService` do `SyncSchedulerService`. `AddKnowledgeSync` e `Program.cs` não mudam. Os 2 casos da Q3 nunca ligam o emissor.
- [x] 4.2 (apps/connectors) `ConnectorsFactoryIssuerTests` (2 casos): sem `Api:BaseUrl` pedido, zero pedidos ao `FakeTokenEndpoint` e ao `FakeSyncApi` em 2 s, e o agendador fora do DI; com `Api:BaseUrl` pedido, o agendador está no DI.

  **(guarda)** Defeito: a remoção desligada (`if (withoutScheduler)` → `if (false)`). Reprova o `IssuerOn_WithoutApiBaseUrlRequested_NothingReachesTheApiFakesAfterBoot` (`Values differ`, pedidos de token ≠ 0). Voltou: `Support/ConnectorsFactory.cs` (sha igual ao de antes: `2e26e789…`).
- [x] 4.3 (apps/connectors) `TestAuthentication.CreateIssuerClientAs(factory, subject)` e `IssuerTokenFor(factory, subject)`. Recebem o subject, e não o `client_id`, como no `apps/api` (design C4). `CreateClientAs`/`IssueToken` (HMAC) ficam, com comentário que nomeia o G2 e a Q3.
- [x] 4.4 (apps/connectors) São 27 chamadas trocadas em `ConnectorEndpointsTests`, `SyncEndpointsTests`, `SecretLeakTests`, `ConnectorRegistrationTests`, `SubjectAuthorizationTests` e `ApiConfigurationValidationTests`. Ficam no HMAC a da Q3 em `SyncEndpointsTests` e a da Q3 em `ApiConfigurationValidationTests`.

  **(guarda)** Defeito: o mapeamento ignora a tabela (todo subject vira cliente desconhecido). Reprovam o `SubjectXRota(subject: "service:api", …, allowed: True)` (`Assert.NotEqual() Failure`) e os casos de `operator` do `ConnectorEndpointsTests` (`403`). **Correção do registro:** o `ApiNasRotasDoOperador_Recebe403` que a tarefa nomeava continua passando, porque o cliente desconhecido também recebe `403`. Voltou: `Support/TestAuthentication.cs` (sha igual ao de antes: `31c407d2…`).
- [x] 4.5 (apps/connectors) **r3**, CPU em 100: 339 aprovados e 1 ignorado. Por nome, os 338 da baseline estão presentes com o mesmo resultado, mais 2 novos. Tempo: **16 s**, contra 16 s.

## 5. tests/

- [x] 5.1 (tests/) `ApiConnectorsRoundTrip.Tests` (D4 e design C5):
  - o `OperatorClientAsync(api)` usa o token do Admin do bootstrap, pelo fluxo real, um por host;
  - o `ConnectorsOperatorClientAsync(connectors)` usa o token do Admin para `buteco-connectors` e espera as chaves do `apps/connectors`;
  - o `WarmUpIssuerAsync` passou à fixture.

  A `Auth:TokenSigningKey` da fixture fica até a #182.
- [x] 5.2 (tests/) Todos com a CPU em 100:
  - `rt-connectors` r3: 10/10, os mesmos nomes. **29 s** contra 28 s.
  - `rt-crossapp` r3: 4/4. **20 s** contra 19 s.
  - `rt-inbox`: o diff do que ele compila é vazio. Na r3, reprovaram o `Source3_…` e o `MessageReceived_TriggersFullRoundTrip_TaskCarriesChannelTypeAndContactExternalIdInRawPersistedJson`. Nas 3 rodadas alternadas por worktree:
    - o `Source3_…` reprovou em c1, c3, b1, b2 e b3;
    - o `MessageReceived_…` não reprovou mais (0 de 3 na change, 0 de 4 na baseline).

    Comentado na #176 ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/176#issuecomment-6622424428)). Tempo: 51–53 s contra 52–56 s. **Pela regra da #176, a reprovação do `MessageReceived_…` só no worktree da change é PARE: a decisão é do dono.**

## 6. Critério de aceite (D6)

- [x] 6.1 (todos) **Rodada com o HMAC recusando tudo** no worktree da medição, levado à árvore da change (`236-verificacao/criterio-r2`, CPU em 100 em todas as partes que reprovaram):
  - `apps/api`: 7;
  - `apps/inbox`: 5;
  - `apps/connectors`: 9 (7 do G2 e 2 da Q3);
  - `rt-inbox`: 1;
  - `rt-connectors` e `rt-crossapp`: 0.

  Os **22 esperados reprovaram todos, e nenhum outro da lista ficou de fora.** O único caso a mais foi o `TerminalTask_CarriesStatusTimestamp_…(chatFails: True)`, irmão da #176 que já tinha reprovado na medição sobre o código da baseline. Repetido duas vezes no mesmo worktree (`criterio-rt-r3`, `r4`), o rt-inbox reprovou só o `OperatorHmacToken_…`.

  A `criterio-r1` não vale para o `apps/api`: as partes rodam com `--no-build`, e o binário de teste da medição era o antigo. Ela foi guardada como `criterio-r1-api-binario-antigo`.
- [x] 6.2 (todos) **Varredura estática** (`varredura-236.py`): saída `[]`, com o caso misto do design C8 classificado como `M-misto`. A primeira versão do script tinha 4 falsos positivos, corrigidos no script.

  **(guarda)** Defeito: um `AttachOperatorToken` reintroduzido no `ServiceScopeAuthorizationTests.UnknownSubject_OnOperatorRoute_ReturnsForbidden`. A varredura nomeia o método. **Incidente:** a primeira restauração foi por `git checkout`, que levou o arquivo ao HEAD e desfez a migração dele. O arquivo foi recuperado da cópia feita antes da mutação, com o sha de antes (`71009542…`), idêntico à cópia da árvore da medição.
- [x] 6.3 (todos) **Tempo**, só rodadas com a CPU em 100. Nenhuma suíte passou de 1,05×:

  | suíte | antes | depois | razão |
  |---|---|---|---|
  | `apps/api` (5 partes) | 298 s | 264 s | 0,89 |
  | `apps/inbox` | 52 s | 52 s | 1,00 |
  | `apps/connectors` | 16 s | 16 s | 1,00 |
  | `rt-inbox` | 52–56 s | 51–53 s | ~0,97 |
  | `rt-connectors` | 28 s | 29 s | 1,04 |
  | `rt-crossapp` | 19 s | 20 s | 1,05 |

## 7. Documentação e fechamento

- [x] 7.1 (docs) `CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`.
- [x] 7.2 (openspec) `openspec validate fixtures-token-emissor --strict` verde, e o archive com sync: os três requisitos ADDED entram em `api-scaffold`, `inbox-scaffold` e `connectors-scaffold` (ok do dono, 04/11/2026).
- [x] 7.3 (issue) Comentário na #182 com a lista de remoções: G2 (20) ∪ G2 estático (32) ∪ Q3 (2) ∪ o misto do design C8 (1) = **55**, por nome e com o motivo de cada caso ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/182#issuecomment-6622921574)). Na revisão do dono (04/11/2026), 9 casos ficaram marcados "reescrever", e os outros 46 saem:
  - os 4 mistos;
  - os 4 "HMAC `operator` → `403` em `CurrentUser`", que trocam o HMAC pelo token do `buteco-servico-inbox` e mantêm a asserção;
  - o `DocumentWrites_WithOperatorHmacToken_RecordOperatorAsAuthor`, que passa ao token do painel. Com o painel, só o cadastro tem o autor provado (`CreateDocument_RecordsExactlyOneCreatedEvent`); a edição e a exclusão não têm caso que afirme o autor.

  Os da Q3 vão marcados "estado que a #182 elimina".
- [x] 7.4 Worktree descartável `../buteco-agentes-236-medicao` removido (ok do dono, 04/11/2026). Antes, conferido: ele diferia da árvore da change só nas três linhas da medição no `TokenService`, estava desanexado em `7dc7753`, sem commit próprio e sem stash. Commit, push e PR só com a autorização do dono naquela mensagem.
