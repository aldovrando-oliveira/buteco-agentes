## Context

**Estado atual (`32d596b`, 25/10/2026).** O `apps/api` é emissor OIDC desde a #179 (`emissor-oidc-api`), com OpenIddict 7.7.2 (`OpenIddict.Server.AspNetCore` + `OpenIddict.EntityFrameworkCore`), só `client_credentials` e nenhum cliente cadastrado em produção. As quatro tabelas do OpenIddict existem desde a migration `AddIdentityIssuer` (`openiddict_applications`, `_authorizations`, `_scopes`, `_tokens`), mapeadas pelo `modelBuilder.UseOpenIddict()` do `AppDbContext`. A #208 (`validacao-jwks-inbox-connectors`) fez o `apps/inbox` e o `apps/connectors` aceitarem o access token **pelo JWKS**, sem consultar banco nem o `apps/api` por requisição.

**Quem vai gravar tokens**, pelas issues abertas:

| issue | fluxo | quem pede token | frequência |
|---|---|---|---|
| #180 | authorization code + PKCE, refresh, encerramento de sessão; cliente público `buteco-painel` (herança da #208) | o painel, por usuário | login + renovação a cada vencimento do access token (sessão curta, ex. 15 min) |
| #182 | token de serviço assimétrico; a forma (client credentials ou par de chaves) é decisão em aberto da #182 | `apps/inbox`, `apps/api`, `apps/connectors` | contínua |
| #186 | `client_credentials` de aplicação externa | cada aplicação | a premissa desta issue: a cada 10 min |

**Funcionalidade já decidida que depende de linha gravada:** "Encerrar todas as sessões" e desativar usuário (#180, tela Editar usuário), revogar segredo e desativar aplicação (#186), revogação no logout (#180).

**Restrições herdadas da #179, que valem aqui:** validação de configuração só depois do `Build()` (migration bundle); nenhuma leitura de banco no boot (as fixtures sobem o host antes de migrar); `BackgroundService` com intervalo injetável, `try/catch` por ciclo (convenção 4) e `FakeTimeProvider` nos testes; o único advisory lock do `apps/api` é o da rotação, `IdentityKeyRotationPolicy.AdvisoryLockKey = 4779819235982396209` (`BUTECOK1`), na forma `bigint`.

### Achados da fonte do OpenIddict 7.7.2

Tag `7.7.2` do `openiddict-core` (commit `950cb13`, 10/10/2026), lida em 25/10/2026. Caminhos relativos a `src/`.

| # | fato | onde |
|---|---|---|
| F1 | Todo token emitido passa por um `GenerateTokenContext`, e é o `CreateTokenEntry` desse contexto que decide se nasce linha. Cada gerador o preenche com `!DisableTokenStorage`: access token, código de autorização, refresh token, id token, request token, user code e issued token. A única exceção interna é o device code trocado no endpoint de verificação. | `OpenIddict.Server/OpenIddictServerHandlers.cs` l. 4865 (access), 4931 (código), 4997 (device), 5073 (issued), 5149 (request), 5213 (refresh), 5475 (user code), 5539 (id token) |
| F2 | `CreateTokenEntry` é propriedade pública com `set`. | `OpenIddictServerEvents.Protection.cs` l. 47 |
| F3 | O handler que grava a linha, `Protection.CreateTokenEntry`, tem o filtro `RequireTokenEntryCreated`, que lê `context.CreateTokenEntry`. A ordem dele é `AttachSecurityCredentials.Order + 1_000`, e a do `AttachSecurityCredentials` é `int.MinValue + 100_000`. | `OpenIddictServerHandlers.Protection.cs` l. 1458, 1516–1520; `OpenIddictServerHandlerFilters.cs` l. 717–726 |
| F4 | O dispatcher avalia os filtros de cada handler **no momento de chamá-lo**, em ordem. Um handler que roda antes do `CreateTokenEntry` e põe `CreateTokenEntry = false` faz o handler de gravação não rodar. | `OpenIddictServerDispatcher.cs` l. 40–45 e 85–122 |
| F5 | O `CreateTokenEntry` grava `authorization_id`, `creation_date`, `expiration_date`, `status` (`valid`, ou `inactive` para device code), `subject` (o `sub` do principal), `type` e `application_id`, e põe o id da linha no principal (`oi_tkn_id`). Token sem linha não carrega `oi_tkn_id`. | `OpenIddictServerHandlers.Protection.cs` l. 1528–1577 |
| F6 | Linha em `openiddict_authorizations` só nasce como autorização **ad hoc**, e só quando a resposta inclui código de autorização, device code ou refresh token, e o principal ainda não traz id de autorização. **`client_credentials` sem `offline_access` não cria autorização.** | `OpenIddictServerHandlers.cs` l. 3502–3586 (`AttachAuthorization`) |
| F7 | No endpoint de token, refresh token só é gerado com o escopo `offline_access`, e esse escopo é recusado se o servidor não habilitou o grant de refresh (`ID2035`) ou se o cliente não tem a permissão `gt:refresh_token` (`ID2065`). | `OpenIddictServerHandlers.cs` l. 3443–3463; `OpenIddictServerHandlers.Exchange.cs` l. 380–389 e 1326–1336 |
| F8 | Ao validar um token que traz `oi_tkn_id`, se a linha **não existe**, o token é recusado com `invalid_token` (`RestoreTokenEntryProperties`). Se a linha existe e não está `valid`, também é recusado (`ValidateTokenEntry`), e um token **resgatado** que é apresentado de novo revoga todos os tokens da autorização (`RevokeByAuthorizationIdAsync`), salvo refresh token dentro do `RefreshTokenReuseLeeway` (30 s). A autorização ausente ou não `valid` recusa o token (`ValidateAuthorizationEntry`). | `OpenIddictServerHandlers.Protection.cs` l. 751–864, 1207–1381, 1384–1446; `OpenIddictServerOptions.cs` l. 302 |
| F9 | No grant de refresh com refresh rotativo (o padrão), o refresh token apresentado é marcado `redeemed` e um novo é emitido. Código de autorização e device code também são marcados `redeemed` no uso. | `OpenIddictServerHandlers.cs` l. 2992–3086 (`RedeemTokenEntry`) |
| F10 | `IOpenIddictTokenManager.PruneAsync(threshold)` remove linhas com `creation_date < threshold` **e** (status diferente de `valid` e de `inactive`, **ou** autorização associada não `valid`, **ou** `expiration_date < DateTime.UtcNow`). Em lotes de 1.000 por `ExecuteDeleteAsync`, ordenados por `Id`, até 1.000 lotes por chamada; cada lote é um comando próprio. | `OpenIddict.EntityFrameworkCore/Stores/OpenIddictEntityFrameworkCoreTokenStore.cs` l. 583–634; o caminho em lote vale para .NET Core pelo `SUPPORTS_BULK_DBSET_OPERATIONS` (`Directory.Build.targets` l. 61) |
| F11 | `IOpenIddictAuthorizationManager.PruneAsync(threshold)` remove autorizações com `creation_date < threshold`, (status não `valid` **ou** tipo `ad-hoc`) **e** sem nenhum token. Uma autorização ad hoc vive enquanto tiver token; uma permanente e válida nunca é removida. Mesmo regime de lotes. | `OpenIddictEntityFrameworkCoreAuthorizationStore.cs` l. 572–681 |
| F12 | O "vencido" do F10 compara com `DateTime.UtcNow` **dentro da consulta**, e o limite é calculado pelo chamador. O `OpenIddict.Quartz` calcula o limite com o `TimeProvider` das opções. | store l. 613; `OpenIddict.Quartz/OpenIddictQuartzJob.cs` l. 70 e 115 |
| F13 | `OpenIddict.Quartz`: job a cada 1 h, primeira execução 1 a 10 min depois do start, idade mínima padrão de 14 dias para tokens e para autorizações, tokens antes de autorizações. | `OpenIddict.Quartz/OpenIddictQuartzExtensions.cs` l. 57–59; `OpenIddictQuartzOptions.cs` l. 34 e 40 |
| F14 | Durações padrão do servidor: código de autorização 5 min, access token 1 h, id token 20 min, **refresh token 14 dias**. O id token apresentado como `id_token_hint` (autorização e encerramento de sessão) é validado **sem** checar a validade. | `OpenIddictServerOptions.cs` l. 245, 252, 271, 296; `OpenIddictServerHandlers.cs` l. 2057–2073 |
| F15 | O servidor e o core usam o `TimeProvider` registrado no DI, quando há um. | `OpenIddictServerConfiguration.cs` l. 38; `OpenIddictCoreConfiguration.cs` l. 29 |

## O que cada fluxo grava, e quando a linha pode sair

Pela fonte (F1, F5, F6, F9). "Morta" é o que o `PruneAsync` considera removível (F10, F11); a linha só sai quando está morta **e** tem mais que a idade mínima (D3).

| fluxo | momento | `openiddict_tokens` | `openiddict_authorizations` | a linha morre quando |
|---|---|---|---|---|
| `client_credentials` (hoje; #182 se for por cliente; #186) | cada pedido de token | 1 access token, `subject` = `client_id`, sem autorização. **Com esta change: nenhuma (D1)** | nenhuma (F6) | access token: no `exp` (1 h) |
| authorization code + PKCE (#180) | `/connect/authorize` | 1 código de autorização (5 min) | 1 ad hoc, `valid`, `subject` = id do usuário | código: resgatado no uso (F9) ou no `exp`. Autorização: quando não tiver mais token |
| | troca do código no `/connect/token` | 1 access token; 1 refresh token se pedir `offline_access`; 1 id token se pedir `openid`; todos com `authorization_id` | — | access e id token: no `exp`. Refresh: quando resgatado na renovação, revogado, ou no `exp` (14 dias por padrão) |
| refresh (#180) | cada renovação | 1 access + 1 refresh + 1 id token novos; o refresh apresentado vira `redeemed` | — | o mesmo da linha acima |
| encerrar sessão, desativar usuário, logout (#180) | ação do usuário ou do admin | as linhas da sessão viram `revoked` | a autorização vira `revoked` | na hora da revogação |

## Goals / Non-Goals

**Goals:**

- `openiddict_tokens` e `openiddict_authorizations` com tamanho limitado, proporcional ao número de usuários ativos, e não ao tempo de operação.
- Nenhuma função já decidida perde o efeito: encerrar sessões, desativar usuário, logout, revogar segredo, desativar aplicação e a detecção de reuso de refresh token.
- Mesmo idioma de serviço em segundo plano da rotação de chaves, seguro com mais de uma instância e sem transação longa.
- Estar em `main` antes da #180 e da #186.

**Non-Goals:**

- **Habilitar fluxo novo.** Authorization code, refresh e encerramento de sessão são da #180. Aqui, refresh só aparece numa fixture de teste (D7).
- **Decidir se a #180 grava o access token do usuário.** Fica para ela (D8).
- **Revogar access token já emitido.** Nenhum validador consulta banco (D2), e esta change não muda isso.
- **Limpar `identity_keys`.** A #179 decidiu não apagar chave vencida (D7 de lá), e nada mudou.
- **Tela ou rota** sobre sessões ou tokens.

## Decisions

### D1. O access token de `client_credentials` deixa de gerar linha; o resto continua gravado

**Dá para separar por tipo, e está na fonte.** O `CreateTokenEntry` do `GenerateTokenContext` decide, token a token, se nasce linha (F1, F2). O filtro do handler de gravação lê esse valor só quando chega a vez dele (F3, F4). Então um handler ordenado entre o `AttachSecurityCredentials` e o `CreateTokenEntry` pode desligar só o caso escolhido. Não precisa de `DisableTokenStorage`, que desliga tudo, e é o que a #209 já descartou por matar a revogação da #180.

**`Identity/Storage/ClientCredentialsAccessTokenStorageHandler.cs`**, um `IOpenIddictServerHandler<GenerateTokenContext>`:

- ordem `OpenIddictServerHandlers.Protection.CreateTokenEntry.Descriptor.Order - 500`, calculada a partir do descritor do próprio OpenIddict, e não um número copiado;
- `CreateTokenEntry = false` **só quando** `TokenType == access_token` **e** `Request.IsClientCredentialsGrantType()`;
- qualquer outro tipo, ou o mesmo tipo em outro grant, segue intocado.

**Por que esse recorte:**

- **Nenhum consumidor lê essa linha.** Quem valida o access token é o `JwtBearer` do `apps/inbox` e do `apps/connectors` (#208), pelo JWKS, e o `apps/api` vai pelo mesmo caminho na #180 (herança registrada na #208). O token não traz `oi_tkn_id` (F5), então nem o próprio servidor consulta a linha se o token voltar a ele.
- **É o fluxo de maior volume** (ver "Volume esperado"), e o único em que a linha não tem uso nenhum.
- **O resto do fluxo de usuário continua gravado**, porque é o que a revogação usa (D2).

**Um efeito declarado:** um access token de `client_credentials` emitido **antes** do deploy tem linha e `oi_tkn_id`. Ele continua validando no `JwtBearer`, que não olha banco. Em produção não há nenhum (nenhum cliente cadastrado), e a linha morre no `exp` como qualquer outra.

**Guardas:** um teste de integração conta zero linhas depois de um pedido de `client_credentials`, e reprova com o registro do handler comentado (convenção 15). Um teste de unidade mostra que só o par (access token, `client_credentials`) muda, e que a ordem resolvida no `OpenIddictServerOptions.Handlers` composto fica antes do `CreateTokenEntry`.

**Alternativas descartadas:**

- **Só limpar, gravando tudo.** Funciona (a tabela fica limitada pela idade mínima), mas grava uma linha por pedido de cada aplicação para apagar 14 dias depois sem ninguém ler. Pelas contas abaixo, é a maior parte da tabela.
- **`DisableTokenStorage`.** Sem linha de refresh token, sem revogação de sessão (F8).
- **Tirar a linha depois de emitida** (um handler no fim do `ProcessSignIn` que apaga). Duas escritas por token em vez de nenhuma.

### D2. O que precisa continuar gravado, e por quê

Cada função pela linha de que depende. "Efeito preservado" quer dizer que nem a decisão D1 nem a limpeza (D3) tiram dela algo que ela usa.

| função (issue) | o que ela faz no OpenIddict | linha de que depende | D1 | limpeza (D3) |
|---|---|---|---|---|
| **Encerrar todas as sessões** (#180, Minha conta e Editar usuário) | revoga as autorizações do usuário (`IOpenIddictAuthorizationManager.RevokeBySubjectAsync`) e/ou os tokens (`IOpenIddictTokenManager.RevokeBySubjectAsync`). O próximo refresh é recusado porque a autorização não está `valid`, ou porque o refresh token não está `valid` (F8) | a autorização ad hoc e o refresh token **válidos** da sessão | não toca (é fluxo de usuário) | nunca remove token `valid`, não vencido e com autorização `valid`, nem autorização com token (F10, F11). Depois da revogação a linha fica morta e pode sair; **sem linha, o token também é recusado** (F8, `RestoreTokenEntryProperties`), então remover nunca desfaz uma revogação |
| **Desativar usuário** (#180, Editar usuário) | o mesmo do item acima, e o login passa a recusar o usuário | as mesmas | não toca | o mesmo |
| **Revogação no logout** (#180) | o endpoint de encerramento de sessão revoga a autorização da sessão (ou o refresh token) | a autorização e o refresh token da sessão; o `id_token_hint`, se a #180 usar, tem linha própria (F14) | não toca | o mesmo; o `id_token_hint` antigo é tratado em D8 |
| **Detecção de reuso de refresh token** (#180, padrão do OpenIddict) | um refresh token já resgatado, apresentado de novo depois de 30 s, revoga a cadeia inteira da autorização (F8) | a linha **`redeemed`** do refresh token antigo, até ele vencer | não toca | a linha resgatada só sai com mais que a idade mínima, e a idade mínima é ≥ duração do refresh token (D3, checada no boot). Então ela existe enquanto o token apresentado ainda estaria no prazo; depois disso o próprio `exp` recusa |
| **Revogar segredo** (#186) | o endpoint de token deixa de aceitar o segredo na autenticação do cliente | `openiddict_applications` (ou o armazenamento de segredos que a #186 decidir) | **nenhuma linha de token envolvida** | não toca em aplicação |
| **Desativar aplicação** (#186) | o endpoint de token recusa o cliente | `openiddict_applications` | o mesmo | o mesmo |

**Dois fatos que valem para a #186, independentes desta change** (registrar lá):

- **Revogar segredo ou desativar aplicação não invalida o access token já emitido.** Ele continua aceito até o `exp` (1 h hoje) em qualquer app, porque a validação é pelo JWKS (#208). Isso já seria verdade com a linha gravada: `RevokeByApplicationIdAsync` marcaria a linha, mas nenhum validador a lê. Se a "revogação imediata" da #186 precisar cortar o token em circulação, a saída é duração curta do token da aplicação ou introspecção, e é decisão da #186.
- **Aplicação externa não pode receber `gt:refresh_token`.** Sem essa permissão, `offline_access` é recusado (F7), e `client_credentials` nunca gera refresh token nem autorização. Com ela, cada pedido criaria autorização e refresh token, e o D1 não cobre isso.
- **O "último uso" da #186 não sai de `openiddict_tokens`.** Sem a linha (D1), e com a limpeza removendo o que passou de 14 dias, a tabela não serve como histórico. A #186 registra o uso por conta própria.

### D3. Limpeza: `PruneAsync` dos dois managers, a cada 1 h, com idade mínima de 14 dias

**Mecanismo:** `IOpenIddictTokenManager.PruneAsync(agora - idade mínima)` e depois `IOpenIddictAuthorizationManager.PruneAsync(agora - idade mínima)`, nessa ordem. Primeiro os tokens: a autorização só sai sem token (F11), e a ordem é a mesma do `OpenIddict.Quartz` (F13). É o critério do próprio OpenIddict (F10, F11), sem SQL nosso para manter a cada upgrade.

**Constantes em `Identity/Storage/IdentityTokenPrunePolicy.cs`** (convenção 2: nenhuma vira opção sem cenário):

| constante | valor | por quê |
|---|---|---|
| `MinimumAge` | 14 dias | **≥ duração do refresh token** (14 dias, padrão do OpenIddict, F14). Uma linha de refresh token resgatada só some quando o token já estaria vencido, e a detecção de reuso (D2) continua valendo pelo prazo inteiro do token. O código de autorização (5 min) fica coberto com folga. É também o padrão do `OpenIddict.Quartz` (F13), escolhido pelo mesmo motivo |
| `Interval` | 1 h | o mesmo do `OpenIddict.Quartz` e da verificação da rotação. Entre dois ciclos entra 1 h de linhas, e o tamanho estável fica em idade mínima + 1 h de linhas (ver "Volume esperado") |
| `AdvisoryLockKey` | `4779819235982397489` | os bytes ASCII de `BUTECOP1`, lidos como inteiro big-endian, na forma `bigint` (D5) |

**A desigualdade vai para o boot (convenção 8).** O `ValidateIdentityIssuerConfiguration` passa a reprovar se `IdentityTokenPrunePolicy.MinimumAge < RefreshTokenLifetime` do servidor composto (`ComposeWithoutKeys`, o mesmo caminho que já confere a retenção das chaves contra o `AccessTokenLifetime`). Hoje o refresh token não está habilitado e a duração é o padrão, 14 dias, então a checagem passa. **Ela existe para a #180:** se a #180 subir a duração do refresh token acima de 14 dias sem subir a idade mínima, o boot falha, em vez de a detecção de reuso parar de valer em silêncio. A checagem tem teste que a vê reprovar com a duração configurada acima do limite.

**Lotes, sem transação longa:** o `PruneAsync` já remove em lotes de 1.000, cada um num `DELETE ... LIMIT` próprio, e os lotes **não** ficam numa transação só, desde que a chamada não aconteça dentro de uma (F10). Por isso o lock fica em outra conexão (D5). No regime estável, cada ciclo remove a hora de linhas que completou 14 dias, de centenas a poucos milhares (ver "Volume esperado"): um a três lotes.

**Dois relógios, declarados.** O limite (`agora - 14 dias`) sai do `TimeProvider` do `apps/api`. O "vencido" é `expiration_date < DateTime.UtcNow` **dentro da consulta** (F12), que o Npgsql traduz para o relógio do Postgres. Em produção os dois são o mesmo relógio de sistema, com diferença desprezível diante de 14 dias. Nos testes o `FakeTimeProvider` controla o limite e a data com que o OpenIddict grava cada linha (F15), e o "vencido" segue o relógio do banco. Os testes usam margens de dias, nunca de segundos, porque o relógio da VM do Podman atrasa. A tradução para o relógio do banco é **conferida** na implementação pelo SQL emitido (`EmittedSqlCapture`, tarefa 3.2), e não suposta.

### D4. `IdentityTokenPruneService`, no idioma do `IdentityKeyRotationService`

`Identity/Storage/IdentityTokenPruneService.cs`, um `BackgroundService`:

- **intervalo injetável** por `IdentityTokenPruneSchedule` (singleton com `Interval` = `IdentityTokenPrunePolicy.Interval`, trocável nos testes como o `IdentityKeyRotationSchedule`);
- `PeriodicTimer(schedule.Interval, timeProvider)`, criado na construção, como na rotação, para o teste com `FakeTimeProvider` ser determinístico. **O primeiro ciclo é no primeiro tique, 1 h depois do start, e não no start.** Assim o boot e as fixtures continuam sem I/O no start, e não há disputa com o aquecimento do anel de chaves;
- **`try/catch` por ciclo** (convenção 4): falha de banco, ou a `AggregateException` que o `PruneAsync` lança quando um lote falha (F10), gera um `LogError` com `EventId` próprio, e o próximo tique tenta de novo. O serviço só para pelo cancelamento;
- cada ciclo abre **um escopo de DI novo** e chama `IdentityTokenPruner.RunCycleAsync`, que devolve o resultado (`Pruned(tokens, authorizations)` ou `Skipped`). O log é `Information` quando removeu algo, `Debug` quando pulou ou não havia nada. O `RunCycleAsync` é a unidade que os testes chamam direto;
- registrado em `AddIdentityIssuer`, só com o emissor ligado e o issuer válido, junto da rotação. Com o emissor desligado não há OpenIddict, e o serviço não existe.

**Por que não `OpenIddict.Quartz`:** a conta é a mesma da #179 (D7 de lá) e da issue. Ele traz o Quartz.NET (scheduler, job store, triggers) para um job só, e o `apps/api` já tem o idioma de `BackgroundService` com intervalo injetável, testado com `FakeTimeProvider`, em três serviços (`KnowledgeIndexingRequestSweepService`, `IdentityKeyRotationService` e este). E o Quartz não resolve o lock entre instâncias sem job store clusterizado, que seriam mais tabelas. O que ele traz de útil, o critério e a ordem, já está no `PruneAsync` (F10, F11, F13).

### D5. Uma instância por ciclo: `pg_try_advisory_xact_lock` numa chave própria, noutra conexão

- **Chave `bigint` `4779819235982397489`** (`BUTECOP1`), em `IdentityTokenPrunePolicy.AdvisoryLockKey`, com o mesmo comentário da chave da rotação. É **diferente** de `IdentityKeyRotationPolicy.AdvisoryLockKey` (`4779819235982396209`, `BUTECOK1`), e as duas estão na forma de 64 bits, que não colide com os locks de dois `int4` do `apps/workers` (`ConversationContextLock`), pelo mesmo motivo registrado na #179. Um teste afirma que as duas constantes diferem, e outro que a limpeza roda com o lock da rotação preso.
- **`try`, e não o lock que espera.** Na rotação, as duas instâncias **precisam** passar em fila, porque a segunda tem que ver a chave criada pela primeira. Aqui, a segunda não tem o que fazer: a primeira está removendo as mesmas linhas. Esperar só faria a segunda repetir a varredura e achar zero. Quem não consegue o lock registra `Debug` e devolve `Skipped`.
- **O lock fica numa transação curta de outra conexão, aberta só para isso.** O ciclo abre dois `AppDbContext` em escopos separados: no primeiro, `BeginTransaction` e `SELECT pg_try_advisory_xact_lock(@key)`; no segundo, os dois `PruneAsync`, cada lote num comando próprio (F10). No fim (ou numa exceção), o rollback da primeira solta o lock. Se o processo morre, a conexão fecha e o Postgres solta o lock.
- **Por que não chamar o `PruneAsync` dentro da transação do lock:** os lotes passariam a ser uma transação só, do tamanho da limpeza inteira, que é o que se quer evitar. A transação do lock não escreve nada, não segura linha e não ganha id de transação.
- **Remoção em dobro não acontece nem sem o lock**, porque `DELETE` é idempotente: o lock evita a varredura repetida e o bloqueio de linha entre dois `DELETE` concorrentes, não um defeito de dado. Os testes de concorrência afirmam as três coisas: a soma do que as instâncias removeram é igual ao que estava morto, nenhuma lança, e todas terminam dentro do tempo.

**Alternativa descartada: lock de sessão (`pg_try_advisory_lock` + `pg_advisory_unlock`).** Funciona numa conexão só, mas o lock fica preso à conexão do pool se o `unlock` não rodar, e a forma `_xact_` não tem `unlock` a esquecer. É o mesmo motivo da #179.

### D6. Índices: nenhum novo

O que as consultas usam, contra os índices que a migration `AddIdentityIssuer` criou:

| consulta | filtro | índice existente | conclusão |
|---|---|---|---|
| `PruneAsync` de tokens | `creation_date < @t` e (status, autorização, `expiration_date`), `ORDER BY Id LIMIT 1000` | nenhum em `creation_date`; a junção com a autorização usa a PK | varredura da tabela, que fica limitada (abaixo). Um índice em `creation_date` custaria uma escrita a mais **por token emitido**, para economizar numa consulta de hora em hora |
| `PruneAsync` de autorizações | `creation_date`, status/tipo, `NOT EXISTS` token | `IX_openiddict_tokens_AuthorizationId` para o `NOT EXISTS` | atendida |
| revogação por autorização (reuso, logout) | `authorization_id = @id` | `IX_openiddict_tokens_AuthorizationId` | atendida |
| revogação por usuário (#180) | `subject = @s` | `(ApplicationId, Status, Subject, Type)`, que não começa por `subject` | varredura da tabela limitada, numa ação rara (clique do admin ou do usuário). **Corrigido na implementação:** o Postgres 18 usa o índice por skip scan (ver abaixo) |

Com a tabela no tamanho estável projetado (dezenas de milhares de linhas), a varredura sequencial cabe em milissegundos. **Isso é medido, e não suposto:** a tarefa 3.6 grava 150.000 linhas mortas e vivas no Postgres de teste, roda o `EXPLAIN (ANALYZE, BUFFERS)` das quatro consultas e registra o tempo no `tasks.md`. **Se** passar de 100 ms, a decisão volta para revisão antes de seguir, com o índice e a migration. Sem migration, o `apps/workers` não é afetado.

**Correção na implementação (3.6, medido em 26–27/10/2026; decisão do dono, 27/10/2026).** A medição com 150.000 tokens confirmou que não entra índice nem migration. A rodada válida começou com `CPU_Speed_Limit = 100`, e nela as quatro consultas levaram 36 / 3,6 / 3,5 / 43 ms. As rodadas com throttling deram o mesmo plano e os mesmos buffers em até 1.588 ms. Dois pontos da tabela acima estavam errados:

- **A revogação por subject não varre a tabela.** O Postgres 18 usa o índice composto existente `(ApplicationId, Status, Subject, Type)` por skip scan (`Index Searches: 7`), mesmo sem o `subject` ser a primeira coluna, e acha as linhas em cerca de 15 ms. O resto do tempo é o `UPDATE` das linhas do usuário. Revogar pelas autorizações do usuário (`IOpenIddictAuthorizationManager.RevokeBySubjectAsync`) levou de 0,5 a 5,7 ms, contra 43 a 251 ms pelos tokens. Isso ficou registrado na #180.
- **O custo do prune de tokens vem da forma do `DELETE` do OpenIddict, e não de índice faltando.** O EF traduz o lote como `DELETE … WHERE "Id" IN (SELECT … ORDER BY "Id" LIMIT 1000)`. A subconsulta acha as 1.000 linhas pela PK em cerca de 24 ms, e o Postgres junta o resultado com a tabela de fora por `Hash Semi Join` sobre um `Seq Scan` dela inteira. Um índice em `"CreationDate"`, medido num experimento descartável, não mudou o plano.

### D7. Testes

**Unidade (`apps/api`):**

- `ClientCredentialsAccessTokenStorageHandlerTests`: para cada tipo de token (access, refresh, id, código) cruzado com cada grant (`client_credentials`, `authorization_code`, `refresh_token`), só o par (access, `client_credentials`) sai com `CreateTokenEntry = false`. E, nas opções compostas pelo builder real do servidor, o handler aparece antes do `CreateTokenEntry` no `Handlers` ordenado.
- `IdentityTokenPrunePolicyTests`: `MinimumAge` ≥ 14 dias; a chave do lock difere da da rotação.
- `IdentityTokenPruneServiceTests`: com `FakeTimeProvider`, nenhum ciclo antes do primeiro tique, um ciclo por tique, falha de um ciclo vira log e o seguinte roda (convenção 4).

**Integração (`apps/api`, Postgres real pelo Testcontainers):**

- **`IdentityTokenStorageTests`**, com o cliente de fixture (`IdentityIssuerTestClient`): um pedido de `client_credentials` deixa `openiddict_tokens` e `openiddict_authorizations` vazias, e o token continua verificável pelo JWKS. **Guarda (convenção 15):** com o registro do handler comentado, a contagem vira 1.
- **`IdentityTokenPruneTests`**, numa fixture com `FakeTimeProvider` e um cliente de fixture com **refresh habilitado só no teste**: o servidor de teste ganha `AllowRefreshTokenFlow()` e o cliente ganha `gt:refresh_token` e `offline_access`. Assim o `client_credentials` com `offline_access` faz o OpenIddict real gravar o que a #180 vai gravar: autorização ad hoc, access token e refresh token, ligados (F6, F7). Se o OpenIddict não assinar sozinho o grant de refresh com o principal do token, a fixture registra um handler de teste que assina com ele. O relógio começa 30 dias atrás e avança até o presente:
  - **remove o que venceu:** as linhas emitidas 30 dias atrás (access vencido, refresh resgatado por uma renovação, autorização sem token) saem;
  - **preserva o que ainda vale:** o refresh token emitido agora, a autorização dele, e uma sessão antiga mas válida (refresh renovado ontem) ficam. O refresh preservado continua renovando depois da limpeza;
  - **preserva o que a revogação precisa:** depois da limpeza, `RevokeBySubjectAsync` encontra a sessão viva e o refresh passa a ser recusado. Um refresh resgatado há 1 dia, apresentado de novo, revoga a cadeia (a linha resgatada não saiu). Uma sessão revogada há 15 dias sai na limpeza, e o refresh dela continua recusado sem a linha (F8);
  - **o "vencido" é o do banco:** o SQL capturado do `DELETE` compara `expiration_date` com o relógio do Postgres (D3).
- **`IdentityTokenPruneConcurrencyTests`**, no molde do `IdentityKeyRingConcurrencyTests`: quatro `RunCycleAsync` simultâneos contra o mesmo banco, cada um no próprio escopo. A soma do que removeram é igual ao que estava morto, nenhum lança, e todos terminam em menos de 30 s. Com o lock preso por fora numa transação do teste, o ciclo devolve `Skipped` e não remove nada; solto o lock, o ciclo seguinte remove. Com o lock da **rotação** preso, a limpeza roda.
- **`IdentityIssuerStartupValidationTests`** (existente) ganha o caso da desigualdade: com `RefreshTokenLifetime` de 15 dias no servidor composto, o boot falha com mensagem que cita as duas durações. **Guarda:** a checagem retirada faz o teste reprovar.

**Acordo (`tests/`):** nada novo. Os dois projetos já emitem `client_credentials` pelo `apps/api` real e validam no app vizinho (#208). Eles rodam na baseline e no fechamento: se o D1 quebrasse a emissão, eles reprovariam.

**Front-end e workers:** não mudam, e não ganham teste. A regra do schema pede teste unitário em todo app **tocado**.

### D8. O que fica decidido agora, e o que fica para a #180 e a #186

**Decidido aqui:**

- access token de `client_credentials` sem linha, para qualquer cliente: aplicação externa (#186) e, se a #182 escolher client credentials, os serviços;
- limpeza a cada 1 h, idade mínima de 14 dias, `PruneAsync` dos dois managers, lock `BUTECOP1` com `try`;
- a idade mínima ≥ duração do refresh token, conferida no boot;
- nenhum índice novo, com a medição da 3.6.

**Fica para a #180** (registrar lá):

- **Se o access token do usuário é gravado.** Hoje é, pelo D1. A linha só tem uso se o `apps/api` validar o próprio token **consultando o banco** (`OpenIddict.Validation` com validação de entrada, ou introspecção). A herança da #208 aponta para o `JwtBearer`, que não consulta. Se for assim, a #180 pode estender o D1 ao access token de usuário, com o mesmo handler, e a tabela cai a cerca de um terço (ver "Volume esperado").
- **Duração do refresh token:** acima de 14 dias, subir `IdentityTokenPrunePolicy.MinimumAge` junto, ou o boot falha (D3).
- **`id_token_hint` antigo:** a validade do id token não é checada quando ele vem como `id_token_hint` (F14), mas a linha dele é. Um id token com mais de 14 dias, cuja linha já saiu, faz o encerramento de sessão recusar o `id_token_hint`. O painel guarda o id token mais recente, que é renovado a cada refresh, ou a #180 deixa de gravar o id token pelo mesmo mecanismo do D1.
- **Lista de sessões em Minha conta / Editar usuário:** mostra só as sessões vivas (D9). Sessão encerrada some depois de 14 dias.
- **"Último acesso" do usuário** (lista de Usuários e Editar usuário): não pode vir de `openiddict_tokens`. A #180 grava o último acesso em campo próprio, atualizado no login e na renovação, sem escrever a cada requisição.

**Fica para a #186** (registrar lá): os três fatos do fim do D2. O token em circulação não é cortado pela revogação do segredo. Sem `gt:refresh_token` para aplicação externa. O "último uso" registrado por conta própria.

### D9. A lista de sessões mostra só as sessões vivas (decisão do dono, 26/10/2026)

A lista de sessões de Minha conta e de Editar usuário (#180, #183) mostra **só as sessões vivas**. Histórico de sessões encerradas não é requisito. Com isso, a limpeza remover a sessão revogada ou vencida 14 dias depois de criada não tira nada de nenhuma tela decidida. Se um dia houver pedido de auditoria de acesso ("quem entrou e quando"), ele não pode depender de `openiddict_tokens` e precisa de registro próprio.

## Volume esperado

**Premissas:** N aplicações com `client_credentials` a cada 10 min, ou 144 tokens por aplicação por dia. M usuários do painel, cada um com 1 login por dia e 8 h de uso, access token de 15 min (o "ex.: 15 min" da #180) e renovação a cada vencimento. Isso dá 4 renovações por hora, cada uma com access, refresh e id token: 4 linhas no login (código, access, refresh, id), mais 32 renovações × 3 = 96, cerca de **100 linhas de token por usuário por dia** e **1 autorização por usuário por dia**. As durações da #180 ainda não estão decididas; as fórmulas valem para outras.

**Tamanho estável com a limpeza:** uma linha sai no primeiro ciclo depois de estar morta e ter mais de 14 dias. Todo token do fluxo de usuário morre em até 14 dias (o refresh pela renovação ou pelo `exp`), então o estoque é a taxa diária × (14 dias + 1 h) ≈ **14,04 × taxa diária**. A autorização dura enquanto tiver token, cerca de 14 dias depois do último uso daquele login.

| cenário | `openiddict_tokens` | `openiddict_authorizations` |
|---|---|---|
| **sem esta change** (tudo gravado, nada removido) | (144·N + 100·M) × dias de operação, **sem teto**. Com N = 20 e M = 10: 3.880 por dia, cerca de **1,4 milhão em um ano** | M × dias: 3.650 em um ano |
| **só a limpeza** (D3 sem o D1) | 14,04 × (144·N + 100·M). N = 20, M = 10: ≈ **54.500**, das quais 40.400 de aplicação | ≈ 15·M = 150 |
| **esta change** (D1 + D3) | 14,04 × 100·M. M = 10: ≈ **14.000**; M = 50: ≈ **70.200**; **independe de N** | ≈ 15·M: 150 (M = 10), 750 (M = 50) |
| esta change e a #180 sem gravar access de usuário (D8) | 14,04 × 68·M. M = 50: ≈ 47.700 | o mesmo |

Por ciclo, no regime estável, saem cerca de 100·M / 24 linhas: 42 com M = 10, 210 com M = 50. Isso é um lote. O pior caso é o primeiro ciclo depois de o serviço ficar parado: `PruneAsync` remove até 1.000.000 por chamada, em lotes de 1.000, e o que sobrar sai no ciclo seguinte.

## Árvore de pastas proposta

```
apps/api/src/Buteco.Api/
├── Identity/
│   ├── IdentityIssuerServiceCollectionExtensions.cs   + registra o handler (D1), o pruner, o schedule e o serviço (D4)
│   ├── IdentityIssuerStartupValidation.cs             + MinimumAge ≥ RefreshTokenLifetime (D3)
│   └── Storage/                                       NOVO
│       ├── ClientCredentialsAccessTokenStorageHandler.cs   GenerateTokenContext: CreateTokenEntry = false só para access + client_credentials (D1)
│       ├── IdentityTokenPrunePolicy.cs                     MinimumAge, Interval, AdvisoryLockKey (D3, D5)
│       ├── IdentityTokenPruner.cs                          RunCycleAsync: lock noutra conexão, PruneAsync de tokens e de autorizações (D3, D5)
│       └── IdentityTokenPruneService.cs                    BackgroundService + IdentityTokenPruneSchedule (D4)
└── (nenhuma migration; Program.cs, AppDbContext e appsettings não mudam)

apps/api/tests/Buteco.Api.Tests/
├── Identity/
│   ├── ClientCredentialsAccessTokenStorageHandlerTests.cs   NOVO, unidade
│   ├── IdentityTokenPrunePolicyTests.cs                     NOVO, unidade
│   ├── IdentityTokenPruneServiceTests.cs                    NOVO, unidade (FakeTimeProvider, pruner falso)
│   ├── IdentityTokenStorageTests.cs                         NOVO, integração: zero linha no client_credentials
│   ├── IdentityTokenPruneTests.cs                           NOVO, integração: remove, preserva, revogação, relógio do banco
│   ├── IdentityTokenPruneConcurrencyTests.cs                NOVO, integração: 4 ciclos, lock preso, lock da rotação
│   ├── IdentityTokenPruneQueryPlanTests.cs                  NOVO, integração: EXPLAIN com 150.000 linhas (D6); [Trait] de medição, fora da suíte padrão
│   └── IdentityIssuerStartupValidationTests.cs              + caso da desigualdade
└── Support/
    └── IdentityIssuerTestClient.cs                          + variante com refresh (gt:refresh_token, offline_access) para a fixture da limpeza

docs/architecture.md, docs/deployment.md, 01-ARQUITETURA_E_CONVENCOES.md, 02-HISTORICO_E_STATUS.md, CHANGELOG.md
```

Nada vai para `libs/`: um consumidor só, o `apps/api`. Nenhum pacote novo.

**O que cada tarefa afeta, por app:** só o `apps/api` tem código. Os dois projetos de `tests/` entram na baseline e no fechamento, sem mudança. O `apps/workers`, o `apps/inbox`, o `apps/connectors` e o `apps/frontend` não mudam. O `apps/workers` não entra na baseline porque não há migration e o espelho dele não mapeia tabela de identidade.

## Risks / Trade-offs

- **[O handler do D1 depende da ordem de dois handlers internos do OpenIddict]** → a ordem é calculada a partir do `CreateTokenEntry.Descriptor.Order`, e não copiada. O teste de unidade confere a posição nas opções compostas, e o de integração conta linhas. Um upgrade que mude o mecanismo reprova os dois.
- **[O `PruneAsync` é do OpenIddict, e o critério pode mudar num upgrade]** → os testes de "preserva" falham se o critério passar a remover sessão viva ou linha resgatada dentro do prazo.
- **[Dois relógios no critério]** → declarados no D3, com o SQL conferido (3.2) e margens de dias nos testes.
- **[Sessão revogada some depois de 14 dias]** → sem linha, o token é recusado do mesmo jeito (F8). O que se perde é histórico, e nenhuma função decidida usa histórico (D9).
- **[Primeiro ciclo só 1 h depois do start]** → nada se acumula em 1 h que importe (tabela limitada). E evita I/O no start, que as fixtures proíbem.
- **[`PruneAsync` lança `AggregateException` se um lote falhar no meio]** → os lotes anteriores já foram confirmados (cada um é um comando), o ciclo registra o erro, e o próximo continua de onde parou (convenção 4).
- **[Varredura sequencial na revogação por usuário]** → tabela limitada e ação rara. A 3.6 mede, e acima de 100 ms a decisão volta para revisão (D6).

## Migration Plan

1. **Deploy:** só o `apps/api` muda. Sem migration, sem variável nova, sem mudança no compose. O serviço de limpeza começa 1 h depois do start, e o primeiro ciclo não encontra nada (nenhum token emitido em produção).
2. **Ordem:** indiferente. Os outros apps não mudam.
3. **Conferência pós-deploy:** `docker compose logs api | grep -i "limpeza das entradas de token"` mostra, depois de 1 h, o ciclo sem erro (nível `Debug` quando não há nada, então o que se confere é a ausência do `Error` do `EventId` do ciclo). A forma exata do `grep` fica no `docs/deployment.md`.
4. **Rollback:** a imagem anterior do `apps/api`. Sem schema novo, nada a desfazer. As linhas que a versão anterior voltar a gravar são removidas quando esta voltar.

## Open Questions

Nenhuma. A única que havia (histórico de sessões encerradas) foi resolvida pelo dono em 26/10/2026 e virou o D9.
