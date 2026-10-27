**Base:** `origin/main` em `32d596b` (merge do PR #215), 25/10/2026. Worktree `../buteco-agentes-209`, branch `feat/209-limpeza-tokens-openiddict`. Baseline em worktree limpo separado, `../buteco-agentes-209-baseline`, no mesmo commit, sem edição nenhuma.

Saídas completas de baseline e de fechamento ficam em `~/.cache/buteco-agents/209-baseline/` e `~/.cache/buteco-agents/209-fechamento/`: `.log` inteiro, `.trx` e estado da máquina colado ao número, nunca filtrado (convenção 19). Variáveis em toda rodada: `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`. Cada suíte só começa com o load de 1 min abaixo de 8, o relógio da VM acertado e nenhum contêiner em `podman ps`.

Só o `apps/api` tem código nesta change. Os dois projetos de `tests/` não mudam, mas emitem `client_credentials` pelo `apps/api` real, que o D1 altera. O `apps/workers` fica fora: não há migration, e o espelho dele não mapeia tabela de identidade (design, "Árvore de pastas").

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/api`, `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests`) Baseline no worktree limpo em `32d596b`, pelo protocolo da 1.1 da #179, com load, `CPU_Speed_Limit`, relógio da VM e `podman ps` na mesma frase. Toda falha é investigada antes de ser chamada de pré-existente.

      **Resultado, 25/10/2026 (26/10 UTC), worktree `../buteco-agentes-209-baseline` em `32d596b`, sem edição nenhuma.** VM `podman-machine-default` (applehv, 6 CPUs, 6 GiB) recém-subida; relógio acertado antes de cada suíte (Mac e VM com no máximo 1 s de diferença na largada e no fim de cada uma); `CPU_Speed_Limit = 100`; **nenhum contêiner** em `podman ps` na largada e no fim de cada suíte; 12 núcleos. As três rodaram em sequência:

      | suíte | resultado | parede (vstest) | load de 1 min na largada → no fim |
      |---|---|---|---|
      | `apps/api` | **864/864** | 2m12s | 6,04 → 12,95 |
      | `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 18 s | 7,49 → 7,13 |
      | `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 7 s | 7,13 → 7,30 |

      Os mesmos números do fechamento da #208 (`apps/api` 864, round-trips 12 e 8). Saídas completas: `~/.cache/buteco-agents/209-baseline/{api,inbox-roundtrip,connectors-roundtrip}.{log,trx}`, estado em `*-estado.txt`.

## 2. Access token de `client_credentials` sem linha (`apps/api`, D1)

- [x] 2.1 `Identity/Storage/ClientCredentialsAccessTokenStorageHandler.cs`: `IOpenIddictServerHandler<GenerateTokenContext>`, com descritor de ordem `OpenIddictServerHandlers.Protection.CreateTokenEntry.Descriptor.Order - 500`; `CreateTokenEntry = false` só para `TokenType == access_token` com `Request.IsClientCredentialsGrantType()`. Comentário citando F1–F5 com arquivo e linha da tag `7.7.2`. Registro em `AddIdentityIssuer` (`server.AddEventHandler(...Descriptor)`).
- [x] 2.2 `ClientCredentialsAccessTokenStorageHandlerTests` (unidade): a matriz tipo × grant (access, refresh, id, código × `client_credentials`, `authorization_code`, `refresh_token`), em que só o par (access, `client_credentials`) sai com `CreateTokenEntry = false`; e, nas `OpenIddictServerOptions` compostas pelo builder real do servidor, o handler fica antes do `CreateTokenEntry` e depois do `AttachSecurityCredentials` no `Handlers` ordenado.

      **Feito, 26/10/2026.** 12 casos da matriz + 1 de ordem. As opções saem do `AddIdentityIssuer` real num `ServiceCollection` próprio, menos o `IdentityKeyRingOptionsConfigurer` (o mesmo recorte do `ComposeWithoutKeys`), ordenadas como o `PostConfigure` do OpenIddict ordena (`OpenIddictServerConfiguration.cs` l. 541). As padrão já vêm na lista desde a construção das opções (`OpenIddictServerOptions.cs` l. 355).
- [x] 2.3 `IdentityTokenStorageTests` (integração, cliente de fixture): um pedido de `client_credentials` deixa `openiddict_tokens` e `openiddict_authorizations` com zero linhas, e o token continua verificável pelo JWKS. **Verificar que reprova** com o registro do handler comentado (a contagem vira 1), e registrar aqui o resultado (convenção 15).

      **Verificado, 26/10/2026.** Passa com o handler. Com a linha `server.AddEventHandler(ClientCredentialsAccessTokenStorageHandler.Descriptor)` comentada, reprovam os dois guardas: `IdentityTokenStorageTests` com `Expected: 0, Actual: 1` na contagem de `openiddict_tokens`, e o teste de ordem da 2.2 com "o registro real não adicionou o handler às opções do servidor". Arquivo restaurado e conferido. O teste também afirma que o token sem linha não traz `oi_tkn_id` (F5).

## 3. Limpeza (`apps/api`, D3–D6)

- [x] 3.1 `Identity/Storage/IdentityTokenPrunePolicy.cs`: `MinimumAge` = 14 dias, `Interval` = 1 h, `AdvisoryLockKey` = `4779819235982397489` (`BUTECOP1`), cada uma com o motivo no comentário, no formato da `IdentityKeyRotationPolicy`. `IdentityTokenPrunePolicyTests` (unidade): a chave difere de `IdentityKeyRotationPolicy.AdvisoryLockKey`, e `MinimumAge` ≥ o `RefreshTokenLifetime` padrão do OpenIddict.
- [x] 3.2 `Identity/Storage/IdentityTokenPruner.cs`, `RunCycleAsync`: num escopo, `BeginTransaction` + `pg_try_advisory_xact_lock(@key)`; sem lock, devolve `Skipped`; com lock, noutro escopo, `IOpenIddictTokenManager.PruneAsync` e depois `IOpenIddictAuthorizationManager.PruneAsync` com `agora - MinimumAge` do `TimeProvider`; rollback da transação do lock no `finally`; devolve `Pruned(tokens, authorizations)`. **Conferir pelo `EmittedSqlCapture`**, e registrar aqui, que o `DELETE` de tokens compara `expiration_date` com o relógio do Postgres (`now()`), e que nenhum `DELETE` roda dentro da transação do lock (D3, D5). Se a tradução não for essa, parar e levar ao dono.

      **Conferido, 26/10/2026, sem parada.** O SQL capturado no `IdentityTokenPruneTests` (o teste afirma o fragmento em todo `DELETE` de tokens):

      ```sql
      DELETE FROM openiddict_tokens AS o
      WHERE o."Id" IN (
          SELECT o0."Id"
          FROM openiddict_tokens AS o0
          LEFT JOIN openiddict_authorizations AS o1 ON o0."AuthorizationId" = o1."Id"
          WHERE o0."CreationDate" < @date AND (((o0."Status" <> 'inactive' OR o0."Status" IS NULL) AND (o0."Status" <> 'valid' OR o0."Status" IS NULL)) OR (o1."Id" IS NOT NULL AND (o1."Status" <> 'valid' OR o1."Status" IS NULL)) OR o0."ExpirationDate" < now())
          ORDER BY o0."Id"
          LIMIT @p
      )
      ```

      O "vencido" é `o0."ExpirationDate" < now()`, o relógio do Postgres, como o D3 descreve; a coluna é `timestamp with time zone`. O limite (`@date`) é parâmetro, calculado pelo `TimeProvider`. O de autorizações é `"CreationDate" < @date AND (status não valid OU ad-hoc) AND NOT EXISTS (token)`, sem relógio do banco. Num ciclo com 5 tokens removíveis saem dois `DELETE` de tokens (5, depois 0, que encerra o laço) e dois de autorizações. **Nenhum `DELETE` roda na transação do lock:** essa transação termina sempre em rollback, e as remoções persistem depois do ciclo (o teste lê as tabelas por outro escopo e as encontra sem as linhas). Se rodassem dentro dela, o rollback as desfaria.

      Desvio pequeno: a interface `IIdentityTokenPruner` existe para o serviço receber o pruner falso nos testes de unidade (3.3).
- [x] 3.3 `Identity/Storage/IdentityTokenPruneService.cs` + `IdentityTokenPruneSchedule`: `BackgroundService` com `PeriodicTimer(schedule.Interval, timeProvider)` criado na construção, primeiro ciclo no primeiro tique, escopo novo por ciclo, `try/catch` por ciclo com `EventId` próprio (convenção 4), `Information` quando removeu, `Debug` quando pulou ou não havia nada. Registro em `AddIdentityIssuer`, só com o emissor ligado e o issuer válido. `IdentityTokenPruneServiceTests` (unidade, `FakeTimeProvider`, pruner falso): nenhum ciclo antes do primeiro tique, um por tique, falha de um ciclo vira log e o seguinte roda.

      **Feito, 26/10/2026.** `EventId` 1185 (`IdentityTokenPruneCycleFailed`, `Error`) e 1186 (`IdentityTokenPruned`, `Information`), na sequência dos 1183/1184 da rotação. As fixtures que andam o relógio por dias (`IdentityTokenPruneTests`) trocam o `IdentityTokenPruneSchedule` por `Timeout.InfiniteTimeSpan`: o `PeriodicTimer` recusa períodos acima de ~49 dias (`ArgumentOutOfRangeException` medido com 3.650 dias), e sem a troca o serviço rodaria a limpeza sozinho no meio dos passos.
- [x] 3.4 `IdentityIssuerTestClient`: variante com refresh para a fixture da limpeza. O servidor de teste ganha `AllowRefreshTokenFlow()`, e o cliente ganha `gt:refresh_token` e `offline_access`, só no teste. Conferir que o grant de refresh assina com o principal do token; se o OpenIddict não fizer isso sozinho, registrar um handler **de teste** que assina, e anotar aqui qual dos dois.

      **O OpenIddict assina sozinho**, sem handler de teste: o `AttachPrincipal` põe o principal do refresh token no contexto (`OpenIddictServerHandlers.Exchange.cs` l. 2095–2150), e o `HandleTokenRequest` faz o sign-in com ele quando nenhum handler o fez (l. 244). O `ClientCredentialsTokenHandler` só age no `client_credentials`. **Correção:** o servidor de teste precisa de `AllowRefreshTokenFlow()` de verdade, e não só do grant nas opções: é ele que registra o escopo `offline_access` (`OpenIddictServerBuilder.cs` l. 1012–1018). Só com o grant, o pedido sai `invalid_scope` (`ID2052`), medido. `EnsureRefreshClientAsync(services, clientId)` cria um cliente por sessão, para ter um `subject` por sessão.
- [x] 3.5 `IdentityTokenPruneTests` (integração, `FakeTimeProvider` começando 30 dias atrás), cobrindo os cenários da spec "Entradas mortas…" e "A limpeza preserva o efeito da revogação": remove o morto antigo; preserva o morto recente, a sessão viva recente e a sessão antiga ainda válida (que continua renovando); a sessão viva continua revogável depois da limpeza; o reuso de refresh resgatado há 1 dia revoga a cadeia; a sessão revogada e removida continua recusada. Usar o cliente de `client_credentials` sem refresh (D1 desligado só nessa fixture) para também ter uma linha de access token real emitida e removida. **Verificar que reprova** com `MinimumAge` trocado por zero (o refresh resgatado recente sai, e o reuso deixa de revogar a cadeia), e registrar aqui.

      **Feito e verificado, 26/10/2026.** Um teste em passos (a limpeza é global e o relógio só anda para a frente), seis sessões em seis clientes. O ciclo devolve `Pruned(5, 2)`: os três tokens da sessão de 30 dias, o primeiro refresh resgatado da sessão de 20 dias e o da sessão revogada; as autorizações dessas duas. Depois do ciclo, a sessão antiga ainda válida renova, a viva é revogada e recusada, o reuso do refresh resgatado ontem é recusado e deixa os refresh da autorização fora de `valid`, e a sessão revogada removida é recusada. Um passo a mais prova a premissa do F8 no prazo: refresh de agora com a linha apagada é recusado (`invalid_grant`).

      **Desvio:** a fixture não desliga o D1. O access token do grant de refresh tem linha (o D1 só cobre `client_credentials`), e é ele o access token real emitido e removido.

      **Guarda:** o defeito foi posto no `IdentityTokenPruner` (`agora - TimeSpan.Zero` no limite), e não na constante: com `MinimumAge` zero, a checagem de boot da 4.1 derrubaria o host, e o teste reprovaria pelo motivo errado. Com o defeito, reprova na contagem (`Expected: Pruned(5, 2)`, `Actual: Pruned(10, 2)`). Rodado de novo com o defeito e a contagem e as duas asserções de "resgatado fica" desligadas, para ver a consequência que a tarefa descreve: o refresh resgatado ontem é recusado (a linha sumiu), mas a cadeia **não** é revogada (`Assert.NotEqual() Failure: Expected: Not "valid", Actual: "valid"`). Os dois arquivos restaurados e conferidos.
- [x] 3.6 `IdentityTokenPruneQueryPlanTests` (integração, `[Trait("Category", "Medicao")]`, fora da suíte padrão): 150.000 linhas mortas e vivas no Postgres de teste, `EXPLAIN (ANALYZE, BUFFERS)` do `PruneAsync` de tokens (um lote), do de autorizações, da revogação por autorização e da revogação por subject. Registrar aqui plano e tempo. **Se qualquer um passar de 100 ms, parar e levar ao dono** com o índice proposto (D6).

      **Medido, 26–27/10/2026. Parou por passar de 100 ms em rodadas com throttling; decisão do dono, 27/10/2026: vale a rodada final 1, e o D6 fica sem índice nem migration.** O teste é um `[MeasurementFact]` que só roda com `BUTECO_MEDICAO=1` e entra na suíte padrão como ignorado. Ele mede o SQL que os managers emitem de verdade: um interceptor troca cada comando por `EXPLAIN (ANALYZE, BUFFERS)` do mesmo texto e dos mesmos parâmetros, numa transação desfeita. A carga são 150.000 tokens e 1.500 autorizações, metade com mais de 14 dias, seguidos de `ANALYZE`. Saídas em `~/.cache/buteco-agents/209-fechamento/medicao-3.6-*.log`.

      | rodada | `CPU_Speed_Limit` | prune de tokens (1 lote) | prune de autorizações | revogação por autorização | revogação por subject |
      |---|---|---|---|---|---|
      | 1, 23:04 | não conferido na largada | 211 ms | — (a asserção parava na primeira) | — | — |
      | 2 e 3, 23:06 e 23:08 | não conferido na largada; **23** às 23:14, logo depois do experimento | 217 / 213 ms | 20 / 20 ms | 17 / 17 ms | 236 / 251 ms |
      | **final 1 (a válida)**, 02:16Z | **100** na largada → 62 no fim | **36 ms** | **3,6 ms** | **3,5 ms** | **43 ms** |
      | final 2, 02:18Z | **67** na largada → 100 no fim | 1.588 ms | 9,9 ms | 6,6 ms | 46 ms |

      A válida é a única que começou com o limite em 100, mas **não ficou em 100 até o fim**: terminou em 62. As quatro consultas ficaram abaixo de 100 ms nela.

      O plano é o mesmo em todas as rodadas, com o mesmo número de buffers, tudo `shared hit` (6.505 no prune de tokens). A variação de 36 a 1.588 ms é do throttling, e não da consulta: na final 2, a varredura da tabela levou os mesmos 98 ms das outras rodadas lentas, e o resto do tempo ficou parado no `Hash Semi Join` sem nenhum buffer a mais.

      - **Prune de tokens:** `Hash Semi Join` do `DELETE … WHERE "Id" IN (subconsulta LIMIT 1000)`, com `Seq Scan` da tabela de fora. A subconsulta já usa a PK e leva cerca de 24 ms. A varredura vem da forma do SQL do OpenIddict, e não de índice faltando. Num experimento descartável (teste temporário, apagado depois), um índice em `"CreationDate"` **não mudou o plano**: o mesmo `Hash Semi Join` com `Seq Scan` da tabela de fora, em 236 ms na mesma máquina lenta. Um índice em `"Subject"` também não mexeu no plano do prune.
      - **Revogação por subject:** usa o índice composto `(ApplicationId, Status, Subject, Type)` por skip scan do Postgres 18 (`Index Searches: 7`), e acha as 3.100 linhas em cerca de 15 ms. O resto do tempo é o `UPDATE` de 2.000 linhas. A premissa do D6 ("varredura sequencial") estava errada nesse ponto, mas a seu favor.
      - **Escala (experimento descartável, máquina lenta):** prune de tokens 47 ms com 14.000 linhas e 130 ms com 70.000.
- [x] 3.7 `IdentityTokenPruneConcurrencyTests` (integração, no molde do `IdentityKeyRingConcurrencyTests`): quatro `RunCycleAsync` simultâneos, a soma removida igual ao removível, sem exceção, todos em menos de 30 s; o lock preso por fora → `Skipped` e nada removido, depois solto → removido; o lock da rotação preso → a limpeza roda. **Verificar que reprova** com a chave da limpeza trocada pela da rotação (o caso "lock da rotação preso" vira `Skipped`), e registrar aqui.

      **Feito e verificado, 26/10/2026.** 2.500 refresh resgatados de 30 dias (mais de dois lotes) e 500 válidos, gravados por SQL (`Support/IdentityTokenSeed.cs`), e quatro pruners com provedor de serviços próprio. Os três casos passam. Com a chave da limpeza trocada pela da rotação no `IdentityTokenPruner`, reprovam dois: o lock da rotação preso devolve `Skipped` (`Expected: Pruned(2500, 0)`), e o lock da limpeza preso deixa de bloquear (`Actual: Pruned(2500, 0)` onde se esperava `Skipped`). Arquivo restaurado e conferido.

## 4. Checagem de boot (`apps/api`, D3)

- [x] 4.1 `ValidateIdentityIssuerConfiguration`: falha se `IdentityTokenPrunePolicy.MinimumAge` < `RefreshTokenLifetime` do servidor composto (`ComposeWithoutKeys`), com mensagem que cita as duas durações e a #180.

      **Feito, 26/10/2026.** Além do caso maior, reprova também `RefreshTokenLifetime` nulo, que no OpenIddict é refresh token sem prazo e que nenhuma idade mínima cobre. A mensagem diz "sem prazo".
- [x] 4.2 `IdentityIssuerStartupValidationTests`: com `RefreshTokenLifetime` de 15 dias na composição real, o boot falha com a mensagem; com o padrão, sobe. **Verificar que reprova** com a checagem retirada do `ValidateIdentityIssuerConfiguration`, e registrar aqui.

      **Feito e verificado, 27/10/2026, com `CPU_Speed_Limit = 100`.** As 5 da classe passam: as 3 que existiam e as 2 novas. Uma nova é o refresh de 15 dias, que exige "Idade mínima da limpeza das entradas de token (14.00:00:00)" e "refresh token (15.00:00:00)" na mensagem. A outra é a composição padrão, que não falha por esta checagem. Com o bloco da checagem retirado, o caso de 15 dias reprova (`Assert.NotNull() Failure: Value is null`): o host sobe normalmente, sem banco, com o refresh acima do limite. Arquivo restaurado e conferido.

## 5. Documentação

- [x] 5.1 `01-ARQUITETURA_E_CONVENCOES.md`, seção "Autenticação": o que o emissor grava e não grava, a limpeza, a chave `BUTECOP1` ao lado da `BUTECOK1`, e a desigualdade do boot.
- [x] 5.2 `docs/architecture.md`: a limpeza no bloco do emissor. `docs/deployment.md`: nada a gerar, o `grep` de conferência pós-deploy e o rollback.
- [x] 5.3 Registro na #180 (D8: access token de usuário, duração do refresh, `id_token_hint`, lista de sessões) e na #186 (D2: token em circulação, sem `gt:refresh_token`, "último uso" próprio), **com o ok do dono para cada texto**.

      **Feito, 26–27/10/2026, com o ok do dono.**
      - **#186:** o item "Revogar corta na hora" entrou em "Decisões em aberto" (duração curta de 5 a 10 min ou introspecção), e há um comentário com os três fatos do D2.
      - **#180:** um comentário com os pontos do D8: access token do usuário, teto do refresh, `id_token_hint`, "último acesso" com campo próprio e lista só de sessões vivas (D9). Há um segundo comentário e um item em "Decisões em aberto" para revogar pelas autorizações, e não pelos tokens, com os tempos da 3.6.

## 6. Fechamento

- [x] 6.1 (`apps/api`, os dois projetos de `tests/`) Build `--no-incremental` nos dois worktrees, comparando o conjunto de avisos.

      **Feito, 27/10/2026.** Os três projetos de teste com `--no-incremental` nos dois worktrees, 0 erros. Os avisos de compilador são os mesmos (9 distintos no `Buteco.Api.Tests`, por arquivo e código; 0 nos dois de `tests/`). Os arquivos novos não trazem aviso. Saídas em `209-fechamento/build-*.log` e `avisos-*.txt`.
- [x] 6.2 (`apps/api`, os dois projetos de `tests/`) Fechamento das três suítes no mesmo protocolo da 1.1, comparado **por nome de teste** com os `.trx` da baseline. Divergência explicada antes de seguir.

      **Feito, 27/10/2026**, no worktree da change, pelo mesmo protocolo da 1.1. Antes de cada suíte, o script espera `CPU_Speed_Limit = 100` e load abaixo de 8. Em todas, o limite estava em 100 na largada e no fim, o relógio Mac × VM ficou com no máximo 1 s de diferença, e `podman ps` estava vazio. Comparação pelo `testName` dos `.trx`, como multiconjunto:

      | suíte | baseline em `32d596b` | fechamento | diferença por nome |
      |---|---|---|---|
      | `apps/api` | 864/864, 2m12s (load 6,04 → 12,95) | **889/889 + 1 ignorado**, 1m36s (load 6,39 → 10,14) | +26 novos: 25 aprovados e o de medição da 3.6, ignorado sem `BUTECO_MEDICAO=1`. 0 ausentes, 0 com resultado mudado |
      | `tests/InboxOrchestratorRoundTrip.Tests` | 12/12, 18 s (load 7,49 → 7,13) | **12/12**, 18 s (load 7,94 → 5,91) | os 12 com par igual |
      | `tests/ApiConnectorsRoundTrip.Tests` | 8/8, 7 s (load 7,13 → 7,30) | **8/8**, 6 s (load 6,00 → 5,69) | os 8 com par igual |

      Os dois de `tests/` emitem `client_credentials` pelo `apps/api` real, já com o D1, e validam no app vizinho pelo JWKS. Continuam iguais, como o D1 previa. Saídas em `~/.cache/buteco-agents/209-fechamento/{api,inbox-roundtrip,connectors-roundtrip}.{log,trx}`, com o estado em `*-estado.txt`.
- [x] 6.3 (`apps/api`) Verificação manual no stack local: `dotnet run` com `PublicUrl:BaseUrl`, o serviço registrado no log de start, e um ciclo forçado com intervalo curto (só em `Development`, sem mudar o código de produção) que loga o resultado. Saída em `~/.cache/buteco-agents/209-fechamento/manual/`.
  - Registro: o intervalo é constante, então o ciclo não foi forçado; a verificação esperou o primeiro tique. Start 02:45:27Z, ciclo 03:45:43Z: "Limpeza das entradas de token removeu 30 tokens e 0 autorizações." (`EventId` 1186); dos 35 tokens semeados sobraram os 5 `valid`.
- [x] 6.4 `/opsx:sync` e `/opsx:archive` dentro do worktree; registro no `02` (o que entrou, o que a implementação mediu, guardas, divergências, achados, herança para a #180 e a #186, deploy) e no `CHANGELOG.md`, **antes** do archive.
- [x] 6.5 Push e PR com `Closes #209`, só com autorização do dono naquela mensagem; mover a #209 para `In review`.
- [ ] 6.6 Depois do merge: remover os dois worktrees e a branch local.
