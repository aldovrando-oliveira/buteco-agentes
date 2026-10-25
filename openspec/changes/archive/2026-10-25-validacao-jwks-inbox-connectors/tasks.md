**Base:** `origin/main` em `f216338` (merge do PR #213), 24/10/2026. Worktree `../buteco-agentes-208`, branch `feat/208-validacao-jwks-inbox-connectors`. Baseline em worktree limpo separado, `../buteco-agentes-208-baseline`, no mesmo commit, sem edição nenhuma.

Saídas completas de baseline e de fechamento ficam em `~/.cache/buteco-agents/208-baseline/` e `~/.cache/buteco-agents/208-fechamento/`: `.log` inteiro, `.trx` e estado da máquina colado ao número, nunca filtrado (convenção 19). Variáveis em toda rodada: `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`. Script da rodada: `~/.cache/buteco-agents/208-baseline/run.sh` (espera o load de 1 min abaixo de 8 antes de cada suíte e grava o estado antes e depois).

## 1. Baseline e dependência, antes de tocar código

- [x] 1.1 (`apps/inbox`, `apps/connectors`, `apps/api`) Baseline das três suítes que esta change toca, no worktree limpo em `f216338`: as duas que ganham a validação e o `apps/api`, pelo `client_id` (D6). Registrar o número de cada uma com load, `CPU_Speed_Limit`, relógio da VM e `podman ps` na mesma frase. Toda falha é investigada antes de ser chamada de pré-existente.

      **Resultado, 24/10/2026, worktree `../buteco-agentes-208-baseline` em `f216338`, sem edição nenhuma.** Ambiente: VM `podman-machine-default` (applehv, 6 CPUs, 6 GiB); relógio da VM acertado com o do Mac antes de cada suíte (Mac 00:33:34 UTC / VM 00:33:34; 00:34:44 / 00:34:45; 00:35:55 / 00:35:55); `CPU_Speed_Limit = 100`; **nenhum contêiner** em `podman ps` antes de cada suíte. As três rodaram em sequência, cada uma só depois de o load de 1 min cair abaixo de 8.

      | suíte | resultado | parede (vstest) | load na largada → no fim |
      |---|---|---|---|
      | `apps/inbox` | **252/252** | 51 s | 5,47 → 7,52 |
      | `apps/connectors` | **261/261**, mais 1 ignorado | 10 s | 7,52 → 13,07 |
      | `apps/api` | **863/863** | 4m01s | 7,62 → 13,53 |

      O ignorado é `ManualGoogleDriveTests.ListarARaizEEntregarOMarkdown_NoDriveReal`, com `Skip` no código (precisa de credencial real do Drive): não é falha nem ambiente. O `NU1903` do `SSH.NET` no restore do `Buteco.Inbox.Tests` é o da #146, já na baseline.

      Saídas completas: `~/.cache/buteco-agents/208-baseline/{inbox,connectors,api}.{log,trx}`, estado em `*-estado.txt`.

      **Acréscimo, 25/10/2026: as duas suítes de `tests/`**, que a 1.1 não cobria e a comparação de fechamento pede (7.3, 9.1). Rodadas no mesmo worktree limpo em `f216338` (`git status` vazio), com `~/.cache/buteco-agents/bin/suite-208.sh` (o mesmo protocolo do `run.sh`, generalizado para os cinco projetos):

      | suíte | resultado | parede (vstest) | load na largada → no fim | relógio Mac / VM | `podman ps` antes |
      |---|---|---|---|---|---|
      | `tests/InboxOrchestratorRoundTrip.Tests` | **11/11** | 18 s | 2,82 → 4,44 | 05:20:26 / 05:20:27 | vazio |
      | `tests/ApiConnectorsRoundTrip.Tests` | **7/7** | 6 s | 4,44 → 9,06 | 05:21:22 / 05:21:23 | vazio |

      `CPU_Speed_Limit = 100` nas duas. Saídas em `~/.cache/buteco-agents/208-baseline/{rt-inbox,rt-connectors}.{log,trx}`.
- [x] 1.2 (`apps/inbox`, `apps/connectors`) Medir o restore das duas opções de pacote antes de decidir (design, Context). **Resultado, 24/10/2026:** `JwtBearer` 10.0.13 acrescenta 8 pacotes e nenhum `NU16xx` ou aviso novo; `OpenIddict.Validation.AspNetCore` + `SystemNetHttp` 7.7.2 acrescenta 24, com a cadeia `Http.Polly` da #179, também sem aviso nestes dois grafos. Árvore revertida depois da medição. Logs em `~/.cache/buteco-agents/208-baseline/pacotes/`.
- [x] 1.3 (raiz, `apps/inbox`, `apps/connectors`) `Directory.Packages.props`: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13, com comentário apontando este design (D1); `PackageReference` nos dois apps. `dotnet build --no-incremental` dos dois apps e das duas suítes, comparado com o worktree de baseline: **o mesmo conjunto de avisos**, sem `NU1605`/`NU1608`.

## 2. `client_id` no access token (`apps/api`)

- [x] 2.1 (`apps/api`) ~~`Identity/AccessTokenClientIdHandler.cs`~~ **Retirada na implementação (convenção 9):** o OpenIddict 7.7.2 já põe `client_id` no access token, em `PrepareAccessTokenPrincipal` (`OpenIddictServerHandlers.cs` l. 3722–3724), e o F8 do design estava errado. Pego ao ver o guarda da 2.2 reprovar: com o registro do handler comentado, o teste continuou passando, e o payload sem o handler trazia `"client_id":"cliente-teste"`. Handler e registro removidos; o `apps/api` não muda em código de produção. Design (F8, D6, árvore, riscos, migração) e proposal corrigidos.
- [x] 2.2 (`apps/api`) `IdentityIssuerTokenTests.AccessToken_CarriesClientId_AndAtJwtType`: o token de `client_credentials` traz `client_id` igual ao do cliente e `typ` `at+jwt` (spec `oidc-issuer`, delta), como **teste de contrato** do comportamento do OpenIddict de que o `apps/inbox` e o `apps/connectors` dependem. Visto reprovar uma vez (convenção 15) com um handler temporário, ordenado em `PrepareAccessTokenPrincipal.Order + 1`, que retirava `client_id` do `AccessTokenPrincipal`: 1 reprovado / 4 aprovados, na l. 69 (`TryGetPayloadValue(client_id)`), o payload sem o claim. Handler temporário desfeito; 5/5 aprovados de volta. Nenhum handler de teste permanente.

## 3. Validação do token do emissor (`apps/inbox`)

- [x] 3.1 (`apps/inbox`) `Auth/IssuerTokenOptions.cs` (`Identity:Issuer`, normalizado com barra final), `IssuerTokenAudience.cs` (`buteco-inbox`), `IssuerClientSubjects.cs` (`buteco-painel` → `operator`), `IssuerKeysSchedule.cs` (constantes do D4).
- [x] 3.2 (`apps/inbox`) `Auth/IssuerKeysRetriever.cs` e `IssuerKeysState.cs`: discovery e JWKS por `{Api:BaseUrl}` pelo cliente nomeado `IssuerKeys` (timeout 5 s, `IHttpClientFactory` a cada busca), conferência ordinal do `issuer` com erro que nomeia os dois valores, espera de 10 s depois de uma falha sem HTTP, `Obtained` marcado na primeira busca que der certo (D3, D4). **Desvios (design, D4):** o log cita a chave `Api:BaseUrl` e não o valor; JWKS sem chave de assinatura conta como falha.
- [x] 3.3 (`apps/inbox`) `Auth/IssuerTokenAuthentication.cs`: `AddIssuerTokenAuthentication(configuration)`:
  - sem `Identity:Issuer`, registra só o HMAC como antes e guarda o aviso para o boot (D7);
  - com ele, o esquema de política `Bearer` com o seletor por contagem de pontos (D2), o `JwtBearer` `IssuerToken` com o `ConfigurationManager` próprio (`AutomaticRefreshInterval` 1 h, `RefreshInterval` 1 min), `ValidAlgorithms` RS256, `ValidTypes` `at+jwt`, `ValidIssuer`, `ValidAudience`, tolerância de 30 s, `MapInboundClaims = false`;
  - `OnTokenValidated` reconstrói o principal pela tabela de clientes (D6); `OnChallenge` responde `503` com `Retry-After: 10` enquanto `Obtained` for falso (D4).
  - **Desvio (design, F11 e D4):** `UseLastKnownGoodConfiguration = false`, achado ao reler o `ConfigurationManager` na versão resolvida (8.19.2, e não a 8.23.0 do Context).
- [x] 3.4 (`apps/inbox`) `Auth/IssuerKeysRefreshService.cs`: tentativa a cada 10 s até obter, depois tique de 10 min chamando `GetConfigurationAsync`; cada iteração captura a própria falha e só loga (convenção 4).
- [x] 3.5 (`apps/inbox`) `Auth/IssuerTokenStartupValidation.cs`: `ValidateIssuerTokenConfiguration()` e `ValidateIssuerClientSubjects()` (D7). `Program.cs`: `AddIssuerTokenAuthentication` no lugar do `AddAuthentication` atual, as duas validações depois do `Build()`. `appsettings.json` (`Identity:Issuer` vazio) e `appsettings.Development.json` (`http://localhost:5017/`).

## 4. Testes do `apps/inbox`, sem o `apps/api` real

- [x] 4.1 (`apps/inbox`) `Support/TestIssuer.cs` e `Support/IssuerTokenFactory.cs` (D9): chaves, emissão com claims controláveis, handler de discovery/JWKS com contagem, publicar e retirar chave, "fora do ar" e `503`; fixture com `IssuerKeysSchedule` curto. **Desvio (design, D7):** `Identity:Issuer` vai por `UseSetting` (`TestAuthentication.UseIssuer`), e as cinco fixtures existentes com `WebApplicationFactory` o fixam vazio; sem isso o registro via o do `appsettings.Development.json`. O `HealthCheckTests`, `WebApplicationFactory` cru, fica como está: sobe com o emissor ligado contra `localhost:5017` e afirma só `/health`.
- [x] 4.2 (`apps/inbox`) `IssuerTokenSchemeSelectionTests`: HMAC do operador `200`; HMAC válido e inválido sem nenhuma requisição ao emissor; JWT inválido `401` sem chamar o `ITokenService` (spy); rotas anônimas com JWT inválido continuam respondendo pela rota.
- [x] 4.3 (`apps/inbox`) `IssuerTokenValidationTests`: os cenários dos requisitos "aceitam o access token" e "mapeado pelo cliente" da spec `issuer-token-validation`, incluindo `HS256` assinado com a `Auth:TokenSigningKey`, `none`, `typ` errado, três audiences e `sub` `operator` forjado.
- [x] 4.4 (`apps/inbox`) `IssuerKeysLifecycleTests`: sobe com o emissor fora (`/health` `200`, HMAC `200`, emissor `503` com `Retry-After`); recupera sem reinício; fora depois de obtidas (`200`); rajada de vinte com no máximo uma busca; `kid` novo (`401`, uma busca, depois `200`); dois `kid`s inventados com no máximo uma busca; rotação; issuer divergente (`503` e log com os dois valores); requisições só ao endereço interno.
- [x] 4.5 (`apps/inbox`) Unidade: `IssuerKeysRetrieverTests` (conferência do issuer, espera entre falhas, `Obtained`) e `IssuerKeysRefreshServiceTests` com `FakeTimeProvider` (tentativa até obter, depois tique, falha capturada).
- [x] 4.6 (`apps/inbox`) `IssuerTokenStartupValidationTests`: sem `Identity:Issuer` (aviso no log, emissor `401`, HMAC `200`); issuer sem esquema e com query derrubam o boot pela **composição real**; tabela de clientes com subject inexistente reprova. Ver o teste da composição reprovar com a chamada removida do `Program.cs` (convenção 15).

      **Verificação contra o defeito (convenção 15), 25/10/2026, cada defeito reintroduzido, o teste rodado e a árvore restaurada (conferida por `diff` contra a cópia):**

      | guarda | defeito reintroduzido | resultado |
      |---|---|---|
      | G1, D7 | `app.ValidateIssuerTokenConfiguration()` comentado no `Program.cs` | `InvalidIssuer_FailsTheRealApplicationStartup`: **4 de 4 reprovados** (sem esquema, com query, com fragmento, `ftp`) |
      | G2, D2 | `FallbackPolicy` com os dois esquemas (`OperatorToken` e `IssuerToken`), o "tentar um depois o outro" | `IssuerTokenSchemeSelectionTests`: **2 reprovados** — `InvalidJwt_IsUnauthorized_WithoutCallingTheHmacValidator` (o JWT passou pelo `ITokenService`) e `HmacTokens_ValidAndInvalid_NeverTouchTheIssuerKeys` (o HMAC passou a consultar as chaves) |
      | G3, F11 | `UseLastKnownGoodConfiguration = true` | `RotatedKey_BothValidWhilePublished_OldOneRefusedAfterRetired`: **reprovado** (a chave retirada continuou `200`) |
      | G4, D6 | `NameIdentifier` tirado do `sub`, e não da tabela de clientes | `IssuerTokenValidationTests`: **5 reprovados**, entre eles `ForgedOperatorSub_DoesNotBecomeTheOperator` e `TokenWithoutClientId_IsForbidden` |
- [x] 4.7 (`apps/inbox`) Suíte inteira do `apps/inbox` verde, comparada por nome com a 1.1: os 252 de antes com o mesmo resultado, mais os novos. **302/302: os 252 com par igual, mais 50 novos** (ver 9.1).

## 5. Validação do token do emissor (`apps/connectors`)

- [x] 5.1 (`apps/connectors`) Os nove arquivos de `Auth/` copiados do `apps/inbox` (D10), com `buteco-connectors`, a tabela `buteco-painel` → `operator` e a conferência contra as chaves de `ConnectorsSubjectAuthorizationHandler.SubjectRoutes`. Comentário de cabeçalho em cada cópia nomeando a outra, como o `TokenService` faz.
- [x] 5.2 (`apps/connectors`) `Program.cs`: `AddIssuerTokenAuthentication` no lugar do `AddAuthentication` atual; `ValidateIssuerTokenConfiguration()` (com a regra do `Api:BaseUrl` obrigatório quando o issuer está ligado) e `ValidateIssuerClientSubjects()` depois do `Build()`, ao lado das checagens existentes. `appsettings.json` e `appsettings.Development.json`.

## 6. Testes do `apps/connectors`, sem o `apps/api` real

- [x] 6.1 (`apps/connectors`) `Support/TestIssuer.cs` e a variante do `ConnectorsFactory` com o emissor: `Identity:Issuer`, `Api:BaseUrl` do `TestIssuer` e o `FakeSyncApiHandler` no cliente do ciclo de sincronização.
- [x] 6.2 (`apps/connectors`) Os mesmos cinco grupos de teste da seção 4, com as rotas do `apps/connectors`; mais o `403` do `buteco-painel` em "descrever pasta" e o boot reprovado com `Identity:Issuer` e sem `Api:BaseUrl`. Guarda da composição real visto reprovar (convenção 15). **Verificação contra o defeito, 25/10/2026:** C1 (`ValidateIssuerTokenConfiguration` comentado no `Program.cs`): **7 de 9** de `IssuerTokenStartupValidationTests` reprovados (os 2 que passaram são as checagens puras da tabela de clientes); C2 (`FallbackPolicy` com os dois esquemas): os mesmos **2** do G2; C3 (`Api:BaseUrl` não exigido): `IssuerWithoutApiBaseUrl_*` **reprovado**. Unidade com o `ManualTimeProvider` da suíte (design, D9).
- [x] 6.3 (`apps/connectors`) Suíte inteira verde, comparada por nome com a 1.1: os 261 + 1 ignorado de antes com o mesmo resultado, mais os novos. **315/315 + 1 ignorado: os 262 com par igual, mais 54 novos**, depois de normalizar o GUID de `SubjectXRota` (#214; ver 9.1).

## 7. Acordo com o emissor real (convenção 11)

- [x] 7.1 (`tests/InboxOrchestratorRoundTrip.Tests`) `RoundTripFixture` com o emissor do `apps/api` ligado (`PublicUrl:BaseUrl`, `Identity:SigningKeyEncryptionKey`) e o cliente `IssuerKeys` do `apps/inbox` apontando para o `TestServer` do `apps/api`; depois do `MigrateAsync`, escopo de teste com recurso `buteco-inbox` e cliente confidencial `buteco-painel`. `IssuerTokenRoundTripTests`: token real `200` em `GET /channels`, e `200` de novo com o handler do `apps/api` falhando.
- [x] 7.2 (`tests/ApiConnectorsRoundTrip.Tests`) O mesmo para o `apps/connectors`, com recurso `buteco-connectors`, e o token só com `buteco-inbox` recebendo `401`.

      **Verificação contra o defeito (convenção 15), 25/10/2026:** com um handler temporário no `apps/api` que retirava o `client_id` do access token (o defeito da premissa F8), os **dois** `IssuerTokenRoundTripTests` reprovaram; handler desfeito. Os dois fixtures fixam a url pública do `apps/api` e passam o `Identity:Issuer` sem barra final (design, D9).
- [x] 7.3 (`apps/api`) Suíte do `apps/api` verde, comparada por nome com a 1.1, e as duas suítes de `tests/` acima verdes. **864/864 (863 com par igual + 1), 12/12 (11 + 1) e 8/8 (7 + 1)** (ver 9.1).

## 8. Configuração e documentação

- [x] 8.1 (raiz) `docker-compose.prod.yml`: `Identity__Issuer: ${PUBLIC_DOMAIN}` no `inbox` e no `connectors`. Conferir com `docker compose -f docker-compose.prod.yml --env-file .env.prod.example config` que os dois recebem o mesmo valor do `PublicUrl__BaseUrl` do `api`. **Conferido, 25/10/2026, com `podman compose` (não há `docker` nesta máquina):** `api PublicUrl__BaseUrl`, `inbox Identity__Issuer` e `connectors Identity__Issuer` = `https://changeme.exemplo.com`, o mesmo valor. Saída em `~/.cache/buteco-agents/208-fechamento/compose-config.yml`.
- [x] 8.2 (docs) `docs/configuration.md`: `Identity:Issuer` nos dois apps, o que liga e o que acontece sem ele. `docs/deployment.md`: ordem de deploy, o aviso de "validação desligada", o erro de issuer divergente e o `503` do token do emissor antes da primeira obtenção. `docs/architecture.md`: o caminho das chaves no diagrama de autenticação.
- [x] 8.3 (docs) `01`, seção "Autenticação": o princípio reescrito (D8), com o texto antigo citado e datado, o `apps/connectors` nomeado, e a validação pelo JWKS na lista do emissor.

## 9. Fechamento

- [x] 9.1 (`apps/inbox`, `apps/connectors`, `apps/api`) Rodada de fechamento das três suítes e das duas de `tests/`, no protocolo da 1.1, em `~/.cache/buteco-agents/208-fechamento/`. Comparação por nome de teste contra a 1.1 registrada aqui; divergência investigada antes de qualquer conclusão (convenção 19).

      **Resultado, 25/10/2026, árvore final no worktree desta change** (`~/.cache/buteco-agents/bin/suite-208.sh`; comparação por `~/.cache/buteco-agents/bin/compare-trx.py`, pelo `testName` dos `.trx` como multiconjunto, com GUID gerado no teste normalizado). `CPU_Speed_Limit = 100`, relógio da VM 1 s à frente do Mac e `podman ps` vazio na largada de cada suíte:

      | suíte | baseline | fechamento | load largada → fim | por nome |
      |---|---|---|---|---|
      | `apps/inbox` | 252/252 | **302/302**, 45 s | 3,00 → 5,84 | 252 com par igual; +50 aprovados |
      | `apps/connectors` | 261/261 + 1 ignorado | **315/315 + 1 ignorado**, 11 s | 5,84 → 5,19 | 262 com par igual; +54 aprovados |
      | `apps/api` | 863/863 | **864/864**, 1m38s | 6,90 → 11,53 | 863 com par igual; +1 aprovado |
      | `tests/InboxOrchestratorRoundTrip.Tests` | 11/11 | **12/12**, 18 s | 5,19 → 5,11 | 11 com par igual; +1 aprovado |
      | `tests/ApiConnectorsRoundTrip.Tests` | 7/7 | **8/8**, 6 s | 5,11 → 5,28 | 7 com par igual; +1 aprovado |

      **Divergência investigada:** sem normalização, o `apps/connectors` acusava 5 ausentes e 5 novos. São os casos de `SubjectAuthorizationTests.SubjectXRota` cuja rota leva `Guid.NewGuid()` (l. 15), truncada com `···` no nome; com o GUID normalizado, todos têm par. Fora do escopo: **#214**. Três rodadas, mesmos números (as anteriores em `rodada-1/` e `rodada-2/`; o `apps/api` é da 2, sem mudança depois). Build `--no-incremental` dos cinco projetos de teste nos dois worktrees: o mesmo conjunto de avisos (`208-fechamento/rebuild-final/`).
- [x] 9.2 (todos) Verificação manual no stack local: `apps/inbox` e `apps/connectors` sobem com o `apps/api` parado (log do aviso de busca falha, `/health` `200`); com o `apps/api` de pé, o log mostra a obtenção pelo endereço interno. Saída registrada aqui.

      **Feito em 25/10/2026** (`208-fechamento/manual/`): com o `apps/api` parado, os dois sobem, logam `Validação do token do emissor ligada: issuer http://localhost:5017/, ...` e o aviso `Busca das chaves do emissor pelo Api:BaseUrl (...) falhou: Connection refused (localhost:5017)` a cada 10 s; `/health` `200` nos dois; `Bearer a.b.c` recebe `503` com `Retry-After: 10`. Com o `apps/api` de pé: `Chaves do emissor obtidas pelo Api:BaseUrl (/.well-known/jwks): kids fniY4qt8pBcSxBan9KihTw`, o `kid` do JWKS do `apps/api`, e o mesmo `Bearer a.b.c` passa a `401`. **Achado e corrigido aqui:** a linha de sucesso, que não existia (acrescentada para esta conferência), listava o `kid` duas vezes, porque `GetSigningKeys` devolve uma chave para `n`/`e` e outra para `x5c`; passou a `Distinct`, e as quatro suítes afetadas rodaram de novo (9.1).
- [x] 9.3 (raiz) Desvios do design registrados nele (convenção 9). `/opsx:sync`, `/opsx:archive`, entrada no `02` (incluindo a herança para a #180: `buteco-painel` público, três escopos, a `aud` `buteco-api` e a pergunta do `libs/` com o terceiro consumidor) e `CHANGELOG.md`. Só então push e PR com `Closes #208`, movendo a #208 para `In review`.
- [ ] 9.4 Depois do merge: remover os worktrees `../buteco-agentes-208` e `../buteco-agentes-208-baseline` e a branch local, conferindo cada um antes (`git branch -d`).
