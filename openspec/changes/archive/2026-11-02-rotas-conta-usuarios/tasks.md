**Base:** `origin/main` em `80f6c9a` (merge do PR #229, #218), 01/11/2026.
- **Worktree da change:** `../buteco-agentes-219`, branch `feat/219-rotas-conta-usuarios`.
- **Baseline:** worktree limpo separado, `../buteco-agentes-219-baseline`, no mesmo commit, sem edição nenhuma.

**Saídas completas:** `.log` inteiro, `.trx` ou relatório JSON do Vitest, e estado da máquina colado ao número, nunca filtrados (convenção 19), em:
- `~/.cache/buteco-agents/219-baseline/`;
- `~/.cache/buteco-agents/219-verificacao/` (e `guardas/` dentro dela);
- `~/.cache/buteco-agents/219-fechamento/`.

**Variáveis em toda rodada .NET:** `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1` e `TZ=America/Sao_Paulo`.

**Antes de cada suíte**, e não só da primeira, o script `~/.cache/buteco-agents/bin/suite-219.sh` confere:
- `CPU_Speed_Limit = 100`;
- loads de 1 e de 5 min abaixo de 6 no `apps/frontend` e de 8 nas outras;
- relógio da VM acertado;
- nenhum contêiner em `podman ps`.

**O que muda nesta change:** só o `apps/api` tem código:
- `Users/` (entidade, `Account/`, `Management/`, `Sessions/`, `Passwords/PasswordPolicy.cs`, `UserAdministration.cs`, `TwoFactor/UserTwoFactorResetEndpoint.cs`);
- `Identity/Validation/IssuerTokenAuthentication.cs` e `Identity/Flow/UserGrantTokenHandler.cs`;
- `Program.cs`.

Também muda a documentação. Sem migration, sem pacote, sem configuração. O código do `apps/frontend`, do `apps/inbox`, do `apps/connectors`, do `apps/workers` e de `tests/` não muda.

**Convenção 15 em todo guarda:** escrever o teste, reintroduzir o defeito nomeado na tarefa, ver reprovar **no componente que a correção toca**, guardar a saída em `219-verificacao/guardas/<tarefa>-reprova.log`, voltar a árvore e ver aprovar em `<tarefa>-aprova.log`. A tarefa só é marcada com as duas saídas registradas nela. **A volta reintroduz só o defeito nomeado**, nunca um diretório inteiro ao `HEAD`.

**As duas corridas** (6.5 e 6.7) seguem a quinta forma da convenção 15: o guarda comportamental (`Task.WhenAll`) pode passar por sorte com o defeito presente. Por isso, cada uma tem **também** uma asserção determinística sobre o SQL emitido na requisição real, capturado pelo `EmittedSqlCapture` (`Support/`). Essa asserção tem de reprovar em toda execução com o defeito. As duas asserções são verificadas **rodando a classe inteira três vezes** com o defeito, e não o caso isolado.

**Commit:** nenhum commit parcial, nem só da documentação ou dos artefatos do OpenSpec. O único commit da change acontece depois do sync e do archive (9.6), com tudo junto. Push e PR só com o ok do dono naquela mensagem.

**Paradas obrigatórias:**
- **grupo 2:** qualquer verificação que reprovar;
- **em qualquer grupo:** premissa do design que se mostrar falsa (convenção 9) **e** mudar comportamento, contrato, escopo de teste ou decisão do design. Achado que só toca comentário ou documentação é resolvido, registrado na tarefa com o motivo e listado no fechamento, sem parar (critério do dono, 01/11/2026).

Nos dois casos de parada, a decisão volta ao dono antes de seguir.

## 1. Baseline, antes de tocar código

- [x] 1.1 (`apps/api`, `apps/frontend`, `tests/InboxOrchestratorRoundTrip.Tests`, `tests/ApiConnectorsRoundTrip.Tests`) Baseline no worktree limpo em `80f6c9a`.

      **Resultado, 01/11/2026, worktree `../buteco-agentes-219-baseline`, `git status` vazio.** O script esperou `CPU_Speed_Limit = 100` e os dois loads antes de cada suíte:

      | suíte | resultado | duração | início (load 1 / 5 min) → fim |
      |---|---|---|---|
      | `apps/api` | **1102/1102 + 1 ignorado** | 298 s | 5,16 / 3,86, CPU 100 → CPU 48 |
      | `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 53 s | 1,72 / 7,78, CPU 100 → 100 |
      | `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 24 s | 3,17 / 7,22, CPU 100 → 100 |
      | `apps/frontend` | **1978/1978**, 154 arquivos | 139 s | 2,74 / 5,70, CPU 100 → CPU 55 |

      - **Por nome:** os três `.trx` são idênticos aos do fechamento da #218 (`compare-trx.py`: 0 sem par, 0 novos).
      - **A primeira rodada do frontend saiu com `127`** (`vitest: command not found`), porque o worktree novo não tinha `node_modules`. Depois do `npm ci`, rodou inteira; a tentativa está em `219-baseline/frontend-1a/`.
      - As durações do `apps/api` e do `apps/frontend` não servem de referência, porque a CPU caiu durante as duas.
      - O `CrossAppTaskStoreCompatibility.Tests` não entra, porque a cifra não muda.

## 2. Verificação antes de qualquer código (parada obrigatória se reprovar)

Cada verificação usa um teste temporário em `apps/api/tests`, apagado depois, com a saída em `219-verificacao/2.N.log`.

- [x] 2.1 (`apps/api`, H1–H3) **O `oi_au_id` no access token real e estável na renovação.** Pelo fluxo real (`IdentityIssuerUserFlow.SignInAsync`):
      - decodificar o payload do access token e conferir o `oi_au_id`, igual ao id da linha de `openiddict_authorizations` do login;
      - renovar, e conferir o mesmo `oi_au_id` no access token novo.

      Se o claim faltar ou mudar na renovação, a sessão atual (D4) não tem fonte: parar.

      **Resultado, 01/11/2026: confirmado.** Teste temporário `Verificacao219Tests` (cópia em `219-verificacao/2.x-teste-temporario.cs`), apagado depois. A rodada final é `219-verificacao/2.1-2.4.log` (CPU 100, load 6,05 / 5,38 no início), com as quatro aprovadas.
      - o access token do login traz `"oi_au_id":"f83c0e25-…"`, igual ao `Id` da única linha de `openiddict_authorizations` do usuário (`valid`, `ad-hoc`);
      - o access token da renovação traz o mesmo `oi_au_id`;
      - o payload traz também `oi_prst` (`buteco-painel`), que o `MapPrincipalByClient` também descarta hoje e que esta change não usa.
- [x] 2.2 (`apps/api`, H6) **A revogação entra na transação do endpoint.** Num escopo, abrir a transação do `AppDbContext`, trocar o stamp de um usuário, rodar o `ExecuteUpdateAsync` das autorizações dele para `revoked` e **desfazer**. Conferir, noutra conexão, que a autorização continua `valid` e o stamp continua o antigo. Repetir com o `RevokeBySubjectAsync` do manager, para registrar que o store também entra (H6). Se a revogação sobreviver ao rollback, o D2 não é atômico: parar.

      **Registrar aqui, lado a lado** (pedido do dono, 01/11/2026), para a divergência ficar visível numa atualização da biblioteca:
      - o SQL do store, lido na fonte da 7.7.2 com arquivo e linha (`RevokeBySubjectAsync`, `OpenIddictEntityFrameworkCoreAuthorizationStore.cs` l. 840–853) e o SQL que o EF emite para ele;
      - o SQL do nosso `UPDATE` (`EndAllAsync` e `EndOthersAsync`), capturado pelo `EmittedSqlCapture`.

      O comentário de `UserSessions` cita o mesmo arquivo e a mesma linha.

      **Resultado, 01/11/2026: confirmado.** Na transação do `AppDbContext`: `Deactivate` gravado, o nosso `UPDATE` (1 linha) e o `RevokeBySubjectAsync` do manager (1 linha), e depois o rollback. Noutro escopo, a autorização continua `valid` e o stamp é o de antes. Os dois comandos entram na transação.

      **Os dois SQL, lado a lado:**

      | | fonte | SQL emitido pelo EF (Npgsql) |
      |---|---|---|
      | store do OpenIddict 7.7.2 | `RevokeBySubjectAsync`, `src/OpenIddict.EntityFrameworkCore/Stores/OpenIddictEntityFrameworkCoreAuthorizationStore.cs` l. 840–853: `from authorization in context.Set<TAuthorization>() where authorization.Subject == subject select authorization` + `ExecuteUpdateAsync(… SetProperty(authorization => authorization.Status, Statuses.Revoked))`, no ramo `SUPPORTS_BULK_DBSET_OPERATIONS` e sem `DisableBulkOperations` | `UPDATE openiddict_authorizations AS o SET "Status" = @p WHERE o."Subject" = @subject` |
      | o nosso (`UserSessions`) | `Set<OpenIddictEntityFrameworkCoreAuthorization>()`, `Subject == @usuário && Status == valid` (e `Id != @atual` no "as outras") + `ExecuteUpdateAsync(… Status = revoked)` | `UPDATE openiddict_authorizations AS o SET "Status" = @p WHERE o."Subject" = @s AND o."Status" = 'valid' AND o."Id" <> @atual` |

      A divergência é só a condição `"Status" = 'valid'` (e o `Id <> @atual`). Ela existe pela contagem de `encerradas` (design, D2). Numa atualização do OpenIddict, reler as linhas acima: se o store passar a fazer mais que pôr `revoked` (revogar os tokens junto, por exemplo), o nosso comando precisa acompanhar.
- [x] 2.3 (`apps/api`, H7) **Claim trocado no `UserGrantTokenHandler` sai no id token da renovação.** Com uma troca temporária de `name` no handler, renovar e decodificar o id token novo; renovar de novo com o refresh novo e conferir o mesmo nome. Registrar a ordem do handler nas opções compostas, que tem de ser posterior ao `AttachPrincipal` (`int.MinValue + 100_000`). Se o id token sair com o nome antigo, o P6 (A) não funciona como desenhado: parar.

      **Resultado, 01/11/2026: confirmado, com uma condição de implementação.** Sonda registrada no fixture como handler de `HandleTokenRequestContext`, sem tocar no código de produção.
      - **Primeira rodada (`2.x-b.log`):** com só `SetClaim(name, "Nome Trocado")`, o id token da renovação saiu **sem `name` nenhum**. A renovação sem a sonda trazia `"name":"Pessoa de Teste"`.
      - **A causa:** o `SetClaim` cria um claim novo, e o destino (`id_token`) mora no claim antigo. Sem destino, o `PrepareIdentityTokenPrincipal` o exclui (`OpenIddictServerHandlers.cs` l. 4648–4653).
      - **Com o destino regravado** (`FindFirst(name).SetDestinations(IdentityToken)`): o id token da renovação traz `"name":"Nome Trocado"`, a renovação seguinte, com o refresh novo, também, e o access token não traz `name`.
      - **Ordem:** `ClientCredentialsTokenHandler`, `UserGrantTokenHandler` e a sonda têm ordem 0, depois do `AttachPrincipal` interno (`int.MinValue + 100_000`, `OpenIddictServerHandlers.Exchange.cs` l. 2103).

      A 7.1 regrava o destino, e o guarda dela cobre a perda (um caso afirma o `name` presente). Não muda comportamento nem contrato; não é parada.
- [x] 2.4 (`apps/api`, D6, D7) **O `EmittedSqlCapture` vê o que as asserções do SQL precisam.**
      - Numa requisição real, ele captura o `SELECT pg_advisory_xact_lock(…)` emitido por `ExecuteSqlInterpolatedAsync` antes do `UPDATE` de `users`, na ordem.
      - Ele captura o `ORDER BY` de uma consulta de `users`.

      Se não capturar, a quinta forma da convenção 15 não tem artefato: parar e propor o meio de captura.

      **Resultado, 01/11/2026: confirmado.** Numa transação, o `EmittedSqlCapture` registrou, nesta ordem:
      1. `SELECT pg_advisory_xact_lock(@p0)`, parametrizado (`DbType = Int64`): a asserção procura o texto do comando, e não o valor;
      2. o `SELECT` de `users`;
      3. `UPDATE users SET deactivated_at = …, security_stamp = …, status = … WHERE id = @p3`;
      4. `ORDER BY u.name, u.id`.

      **A tabela `users` é mapeada em minúsculas** (`u.id`), e o `AssertOrderByEndsWithTieBreak` tem por padrão `"Id"` entre aspas. A primeira rodada (`2.x.log`) reprovou por isso, e não por falta de captura. A 6.2 passa `"u.id"`.
- [x] 2.5 (`apps/api`, D7) **Chaves de lock.** Reconferir por `grep` do prefixo `4779819235982` em `apps/` e `tests/` que só existem as quatro chaves do design (`BUTECOC1`, `BUTECOK1`, `BUTECOP1`, `BUTECOU1`), e que `BUTECOD1` (`4779819235982394417`) está livre. Colar a saída aqui.


      **Resultado, 01/11/2026** (`219-verificacao/2.5-chaves.txt`), quatro linhas:
      ```
      apps/api/src/Buteco.Api/Identity/Keys/IdentityKeyRotationPolicy.cs:52:    public const long AdvisoryLockKey = 4779819235982396209;
      apps/api/src/Buteco.Api/Identity/Clients/IdentityClientCatalog.cs:43:    public const long AdvisoryLockKey = 4779819235982394161;
      apps/api/src/Buteco.Api/Identity/Storage/IdentityTokenPrunePolicy.cs:32:    public const long AdvisoryLockKey = 4779819235982397489;
      apps/api/src/Buteco.Api/Users/Bootstrap/UserBootstrapper.cs:55:    public const long AdvisoryLockKey = 4779819235982398769;
      ```
      `BUTECOD1` (`4779819235982394417`) está livre.
## 3. Peças de base (`apps/api`)

- [x] 3.1 `Users/Passwords/PasswordPolicy.cs` (12 a 128 pelo `string.Length`, sem `Trim`, sem composição, com a mensagem do `400`) e `PasswordPolicyTests` ("Política de senha"): 11 e 129 recusados; 12 e 128 aceitos, com espaços; espaço nas pontas conta. **Guarda:** limite superior com `<` no lugar de `<=` → o caso dos 128 reprova.

      **Feito, 01/11/2026.** `PasswordPolicyTests`: 7 casos. **Guarda:** `and <= MaximumLength` → `and < MaximumLength` reprova `TheLimitsAreAccepted(length: 128)`, e só ele (`guardas/3.1-reprova.log`); de volta, aprova (`3.1-aprova.log`).

- [x] 3.2 `IssuerTokenAuthentication.MapPrincipalByClient` copia o `oi_au_id` (constante pública ao lado de `SubjectClaim`), e `IssuerPrincipalMappingTests`:
      - o principal de um token do emissor tem `oi_au_id` e `sub`;
      - `NameIdentifier` continua pela tabela de clientes;
      - um token de cliente fora da tabela continua sem `NameIdentifier`.

      **Guarda:** cópia do `oi_au_id` retirada → o primeiro caso reprova.

      **Feito, 01/11/2026.** O mapeamento saiu do evento para `MapPrincipal(ClaimsPrincipal?)`, público como o `SelectScheme`, para o teste de unidade (o projeto não tem `InternalsVisibleTo`). `AuthorizationIdClaim = "oi_au_id"`. O `oi_prst`, que o payload também traz (2.1), continua descartado; um caso afirma isso. **Guarda:** a linha que copia o `oi_au_id` trocada por `_ = authorizationId;` reprova `KeepsTheAuthorizationId_NextToTheSub` (`guardas/3.2-reprova.log`); de volta, aprova.

- [x] 3.3 `User.cs`:
      - `ChangePassword(hash)`, que grava o hash, tira o marcador de legado e troca o stamp;
      - `Activate()`, que limpa `deactivated_at` e não troca o stamp;
      - `UpdateProfile(nome, email)`, que recalcula o normalizado e não troca o stamp.

      O comentário do topo deixa de citar "#219" como futuro e passa a descrever o estado atual.

      **Feito, 01/11/2026.** Os três métodos, com o comentário do topo e os de `Deactivate` sem "#219" como futuro. `UpdateProfile` aplica o `Trim` no nome e no e-mail; a validação (tamanho, formato) fica no endpoint (6.1). Coberto pelos testes dos grupos 5 e 6.

- [x] 3.4 `Users/Sessions/UserSessions.cs` (D2, D4), registrado em `UserServiceCollectionExtensions`:
      - `ListLiveAsync(userId)`: autorização ad hoc `valid` do subject, com `EXISTS` de refresh `valid` e não vencido, `criadaEm` e `ultimoUso` (máximo `creation_date` dos tokens), ordem por `criadaEm` decrescente e id;
      - `CountLiveAsync(userId)`: contagem e última atividade, para o detalhe;
      - `EndOneAsync(userId, authorizationId)`, que confere o subject e só revoga a sessão viva;
      - `EndOthersAsync(userId, currentId)` e `EndAllAsync(userId)`: o `ExecuteUpdateAsync` de `subject` e `status = valid`, com e sem `id <> @atual`, devolvendo a contagem.

      O relógio é o `TimeProvider`. Os testes do comportamento são os dos grupos 5 e 6.

      **Feito, 01/11/2026,** com duas diferenças de nome e forma em relação ao texto da tarefa, sem mudar comportamento (convenção 9, registradas aqui):

      - `CountLiveAsync` virou `SummarizeAsync`, porque devolve a contagem **e** a última atividade (`Summary(Ativas, UltimaAtividade)`);
      - `EndOneAsync` não usa `FindByIdAsync` + `TryRevokeAsync`, como o D4 descrevia. É um `UPDATE` só, com o filtro de sessão viva do usuário e o `Id`: a conferência do dono fica no próprio `WHERE`, e o resultado é `true` só com 1 linha afetada. É o mesmo comando das outras duas revogações, sem ler e depois escrever.

      O comentário da classe cita o SQL do store com arquivo e linha (2.2).

- [x] 3.5 `Users/UserAdministration.cs`: `AdvisoryLockKey = 4779819235982394417` (`BUTECOD1`), com o comentário no formato das outras chaves (bytes ASCII lidos como inteiro big-endian, `bigint`, o motivo e a lista das siglas em uso). `AdvisoryLockKeysTests` (unidade): as cinco constantes (`IdentityClientCatalog`, `IdentityKeyRotationPolicy`, `IdentityTokenPrunePolicy`, `UserBootstrapper`, `UserAdministration`) são distintas, e cada uma é a leitura big-endian da sua sigla. **Guarda:** `UserAdministration.AdvisoryLockKey` igual ao de `UserBootstrapper` (`BUTECOU1`, a colisão da primeira redação) → o teste reprova.

      **Feito, 01/11/2026.** `UserAdministration.AdvisoryLockKey` e `LockAsync`, que pega o lock na transação aberta. `AdvisoryLockKeysTests`: 5 + 1 casos. **Guarda:** a chave trocada para `4779819235982398769` (`BUTECOU1`, a do bootstrap) reprova `TheKeysAreDistinct` e `EachKeyIsItsAcronymInBigEndian("BUTECOD1", …)` (`guardas/3.5-reprova.log`); de volta, aprova.

## 4. Toda troca do stamp revoga (`apps/api`, D2, `two-factor-totp`)

- [x] 4.1 `UserTwoFactorResetEndpoint` chama `UserSessions.EndAllAsync` dentro da transação que já existe. Com `409`, nada é revogado.

      **Feito, 01/11/2026.** `UserSessions` injetado; `EndAllAsync` depois do `ExecuteDelete` dos códigos, antes do `Commit`. O comentário da classe passa a dizer por que revoga, e perde o "a lista e o detalhe são da #219".

- [x] 4.2 `TwoFactorAdminResetTests`, estendido com os cenários modificados de "Redefinição da verificação pelo admin":
      - **"A redefinição encerra as sessões na lista":** alvo com duas sessões vivas, redefinido → `GET /users/{id}` com `sessoes.ativas` 0, e as duas autorizações `revoked`;
      - **"Redefinir quem não tem a verificação":** `409`, o stamp igual, e as sessões continuam vivas.

      **Guarda (pedido do dono, 01/11/2026):** a revogação retirada da redefinição → o primeiro caso reprova. A saída fica em `guardas/4.2-reprova.log`.

      **Feito, 01/11/2026.** Casos novos `ResetAnotherUser_EndsTheirSessionsInTheList` e `ResetSomeoneWithoutTheVerification_KeepsTheirSessions`. Os quatro de antes não mudaram. **Divergência da tarefa (convenção 9):** o caso da lista lê as sessões pelo `UserSessions.ListLiveAsync`, o mesmo componente que o detalhe usa, e o estado das autorizações direto na tabela (`Support/UserSessionsSeed`), porque o `GET /users/{id}` só nasce na 6.1. A asserção pelo detalhe HTTP entra na 6.3, que cobre o mesmo cenário pela rota. **Guarda (pedido do dono):** com a linha do `EndAllAsync` retirada da redefinição, `ResetAnotherUser_EndsTheirSessionsInTheList` reprova, e só ele (`guardas/4.2-reprova.log`); de volta, a classe aprova (`4.2-aprova.log`).

## 5. Minha conta (`apps/api`, D1, D3, D4, `user-account`)

- [x] 5.1 `Users/Account/AccountEndpoints.cs`:
      - `GET /account`;
      - `POST /account/password`: `CurrentUser`; bloqueio antes de tudo (`423`); senha atual pelo `PasswordHasher`, contando falha; política; `ChangePassword` e `EndAllAsync` numa transação.

      `Program.cs` mapeia o grupo.

      **Feito, 01/11/2026.** `Users/Account/AccountEndpoints.cs`, com o grupo mapeado no `Program.cs`. **Extração junto (convenção 2):** o `LockedAsync` privado do `AccountTwoFactorEndpoints` (o `423` com `Retry-After`) passou a `Users/Login/LoginThrottleResponses.LockedAsync`. A troca de senha é o segundo arquivo a precisar dele; uma cópia seria o terceiro ponto com o mesmo texto do `423`. As suítes das 2 etapas que o usam passam sem mudança (`TwoFactorEnrollmentTests`, `TwoFactorThrottleTests`, `TwoFactorCallerTests`, em `5.x-primeira.log`).

- [x] 5.2 `Users/Account/AccountSessionEndpoints.cs`:
      - `GET /account/sessions`, com `atual` pelo `oi_au_id` do principal;
      - `POST /account/sessions/{id}/revoke`;
      - `POST /account/sessions/revoke-others`, com `400` sem `oi_au_id`.

      **Feito, 01/11/2026.** `Users/Account/AccountSessionEndpoints.cs`. O `{id}` da rota é texto, e não `guid`, porque o id da autorização do OpenIddict é `string`.

- [x] 5.3 `AccountProfileTests` ("Os próprios dados"): `200` com os dados de quem chama e `duasEtapas` pela coluna.

      **Feito, 01/11/2026.** `AccountProfileTests`: 1 caso, com outra pessoa existindo, para afirmar que os dados são os de quem chama.

- [x] 5.4 `AccountPasswordTests` ("Trocar a própria senha" e "Toda troca do carimbo"):
      - sem a atual, ou com a errada → `401`, contada;
      - cinco falhas e a sexta com a certa → `423`, sem trocar;
      - nova fora da política → `400`;
      - certo → `204`; login com a antiga → `401`, com a nova → `204`;
      - duas sessões vivas, troca por uma: a renovação das duas → `invalid_grant`, e a lista, por um login novo, só com a sessão nova;
      - o acerto não zera a contagem: com quatro falhas antes, uma falha de login depois do `204` bloqueia.
      - **senha atual mais curta que a política** (semente com uma senha de 8 caracteres): o login → `204`, e a troca com ela como `senhaAtual` → `204` ("Senha atual mais curta que a política continua valendo"; pedido do dono, 01/11/2026).

      **Guardas:**
      - política aplicada também à `senhaAtual` → o caso da senha curta reprova;
      - troca do stamp retirada → a renovação passa, e o caso reprova;
      - revogação retirada → a lista mostra as duas sessões, e o caso reprova;
      - `RecordFailureAsync` retirado → o caso das cinco falhas reprova.

      **Feito, 01/11/2026.** `AccountPasswordTests`: 8 casos. **Premissa do guarda corrigida (convenção 15, segunda forma: guarda no lugar errado).** A tarefa dizia "troca do stamp retirada → a renovação passa". Com a revogação presente, a renovação cai mesmo sem o stamp, porque a autorização já saiu revogada: aquele guarda passaria verde com o defeito. O efeito **próprio** do stamp é recusar o cookie `buteco-sessao` em voo no authorize. Daí o caso `TheChange_RefusesASessionCookieInFlight`, com cenário novo na spec ("Cookie de sessão em voo recusado depois da troca"). Não muda comportamento nem contrato; acrescenta um caso para o guarda acordado valer.

      **Guardas,** cada um com a saída em `guardas/`:

      | | defeito | reprovou |
      |---|---|---|
      | 5.4a | política aplicada também à `senhaAtual` | `ACurrentPasswordShorterThanThePolicy_StillWorks` |
      | 5.4b | `SecurityStamp = NewSecurityStamp()` retirado de `ChangePassword` | `TheChange_RefusesASessionCookieInFlight` |
      | 5.4c | `EndAllAsync` retirado da troca | `TheChange_EndsEverySession_IncludingTheCurrentOne` (a lista mostra as sessões) |
      | 5.4d | `RecordFailureAsync` retirado | `FiveWrongCurrentPasswords_LockTheEmail_AndNothingIsChecked` e `TheSuccess_DoesNotResetTheLockCount` |

      De volta, a classe aprova em cada um (`-aprova.log`).

- [x] 5.5 `AccountSessionsTests` ("Lista das sessões vivas", "Encerrar uma", "Encerrar todas as outras"):
      - dois logins → duas sessões, só a do token `atual`;
      - `ultimoUso` muda depois de uma renovação (`FakeTimeProvider`), e `criadaEm` não;
      - código obtido e não trocado → não aparece;
      - 14 dias sem renovar → não aparece, sem rodar a limpeza;
      - encerrar a não atual → `204`, some, a renovação dela → `invalid_grant`, e a atual renova;
      - a de outra pessoa → `404`, e a do outro continua viva;
      - encerrar a atual → `204`, e a renovação seguinte → `invalid_grant`;
      - três sessões e "as outras" → `encerradas` 2, só a atual fica e renova;
      - a sessão de outro usuário não é tocada.

      **Guardas:**
      - `EXISTS` do refresh vivo retirado → o caso do código não trocado reprova;
      - conferência do `subject` retirada do `EndOneAsync` → o caso da sessão de outra pessoa reprova;
      - `id <> @atual` retirado → a atual cai, e o caso das três sessões reprova;
      - o `oi_au_id` (3.2) retirado → nenhuma sai `atual`, e o primeiro caso reprova.

      **Feito, 01/11/2026.** `AccountSessionsTests`: 9 casos. O caso dos 14 dias não avança o relógio: o `JwtBearer` usa o `TimeProvider` do host, e o access token de quem chama venceria junto. Em vez disso, o teste põe no passado a `expiration_date` dos tokens da sessão parada, que é o que a consulta lê.

      **Guardas:**

      | | defeito | reprovou |
      |---|---|---|
      | 5.5a | `EXISTS` do refresh vivo retirado de `Live` | `AnAbandonedLogin_IsNotASession` e `ASessionPastItsRefresh_IsNotASession_BeforeThePrune` |
      | 5.5b | `EndOneAsync` sem o filtro do usuário | `SomeoneElsesSession_Is404_AndStaysAlive` |
      | 5.5c | `Id <> @atual` retirado de `EndOthersAsync` | `EndTheOthers_KeepsOnlyTheCurrentOne` |
      | 5.5d | `oi_au_id` fora do principal | `TwoLogins_TwoSessions_OnlyTheTokensOneIsCurrent`, e os dois de "as outras" (`400` sem a sessão atual) |

## 6. Usuários (`apps/api`, D1, D5–D8, `user-management`)

- [x] 6.1 `Users/Management/UserResponses.cs` (`UserSummary`, `UserDetail`) e `Users/Management/UserEndpoints.cs`:
      - `GET /users`;
      - `GET /users/{id}`;
      - `PUT /users/{id}`, com pré-checagem do normalizado e `catch` do `23505` no índice de `normalized_email`, no molde do `IsFolderUniqueViolation`;
      - `deactivate` (sob o lock da 3.5, relendo quem chama e o alvo dentro dele; recusa de si mesmo; idempotente sem gravar);
      - `activate` (idempotente);
      - `sessions/revoke`.

      Todos pelo `CurrentUser`. `Program.cs` mapeia o grupo; a rota da #218 continua no mesmo prefixo.

      **Feito, 01/11/2026.** `Users/Management/UserResponses.cs` e `UserEndpoints.cs`, com o grupo mapeado no `Program.cs`. A validação do `PUT` é `ValidationProblem` com as chaves `nome` e `email`, como em `McpServers`. O e-mail é conferido por `MailAddress.TryCreate` **e** por `parsed.Address == email`, porque o `TryCreate` aceita `Fulano <x@y>`. O índice do `catch` é `IX_users_normalized_email` (migration `AddLocalUsers`). A desativação devolve `403` do `CurrentUser` quando a releitura dentro do lock encontra quem chama já desativado.

- [x] 6.2 `UserListTests` ("Lista de usuários"):
      - ativo com a verificação e desativado, com `estado`, `duasEtapas` e `formasDeEntrada` `["senha"]`;
      - dois de mesmo nome em ordem crescente de `id`;
      - **asserção do SQL:** o `ORDER BY` emitido termina no `id` (`EmittedSqlCapture`).

      **Guarda:** `ThenBy` do id retirado → a asserção do SQL reprova em três execuções da classe.

      **Feito, 01/11/2026.** `UserListTests`: 2 casos. **Guarda:** `ThenBy` do id retirado, três execuções da classe, as três reprovaram. **Achado no caminho (`guardas/6.2-primeira/`):** na primeira rodada as três reprovações vieram da asserção comportamental, que vinha antes e encerrava o caso, e a do SQL nunca chegou a rodar. Com a do SQL antes (comentário no teste), as três reprovam por "ORDER BY tem um único critério" (`guardas/6.2-reprova-{1,2,3}.log`). A tabela `users` é mapeada em minúsculas, e o helper recebe `"u.id"` (2.4).

- [x] 6.3 `UserDetailTests` ("Detalhe de usuário"): duas sessões, uma renovada depois → `ativas` 2 e `ultimaAtividade` igual à renovação; sem sessão → `ativas` 0 e nulo; inexistente → `404`. **Guarda:** contagem sem o `EXISTS` do refresh vivo → um login abandonado conta, e o caso reprova.

      **Feito, 01/11/2026.** `UserDetailTests`: 5 casos, entre eles **`TheTwoFactorReset_EndsTheSessionsShownInTheDetail`**, que é o cenário modificado de `two-factor-totp` pela rota (a 4.2 o cobria pelo componente). **Guarda:** `SummarizeAsync` sobre `Open` em vez de `Live` (sem o `EXISTS` do refresh vivo) reprova `AnAbandonedLogin_DoesNotCount` (`guardas/6.3-reprova.log`).

- [x] 6.4 `UserEditTests` ("Editar nome e e-mail"):
      - e-mail trocado → `200` com o mesmo `id`; login pelo novo `204`, pelo antigo `401`; a sessão de antes renova;
      - o e-mail de outro noutra caixa → `409`, nada muda nos dois;
      - a própria caixa trocada → `200`;
      - nome vazio depois do `Trim`, e-mail inválido → `400`;
      - inexistente → `404`.

      **Guardas:**
      - conferência por `email` no lugar de `normalized_email` → o caso da outra caixa reprova (`500` pelo índice no lugar de `409`);
      - troca do stamp introduzida na edição → a sessão de antes deixa de renovar, e o caso reprova.

      **Feito, 01/11/2026.** `UserEditTests`: 8 casos nesta tarefa (4 deles na teoria dos campos inválidos). **Premissa do guarda corrigida (convenção 15, segunda forma).** A tarefa dizia "conferência por `email` no lugar de `normalized_email` → `500` pelo índice". Mas o índice e o `catch` cobrem sozinhos o mesmo caso: com a pré-checagem errada, a escrita bate no índice e o `catch` devolve `409`, então o guarda passaria verde. A pré-checagem só poupa uma exceção. O que o caso da outra caixa protege é a **normalização**.

      **Guardas:**

      | | defeito | reprovou |
      |---|---|---|
      | 6.4a | `EmailNormalization.Normalize` sem `ToLowerInvariant()` | `SomeoneElsesEmail_InAnotherCase_Is409` |
      | 6.4b | `UpdateProfile` trocando o stamp | `ChangingTheEmail_MovesTheLogin_AndKeepsTheSub_AndTheSessions` (a sessão de antes deixa de renovar) |

- [x] 6.5 `UserEditTests`, a corrida ("Duas edições concorrentes para o mesmo e-mail"): dois usuários editados ao mesmo tempo para o mesmo e-mail livre (`Task.WhenAll`, 20 rodadas) → sempre um `200` e um `409`, nunca `500`.

      **Asserção determinística:** a pré-checagem é contornada por um alvo que já passou dela (o teste grava o e-mail na outra linha entre a leitura e o `SaveChanges`, por um `DbCommandInterceptor` de teste que espera um sinal), e a resposta é `409`. O `EmittedSqlCapture` mostra o `UPDATE` recusado pelo índice.

      **Guarda:** `catch` do `23505` retirado → o caso determinístico responde `500` e reprova em três execuções da classe. O comportamental é registrado com quantas das 20 rodadas reproduziram a corrida com o defeito.

      **Feito, 01/11/2026.**

      - **Comportamental** (`TwoConcurrentEdits_…`, 20 rodadas): sempre um `200` e um `409`. **Em 20 das 20 rodadas o `409` veio do índice** (o `UPDATE` recusado aparece no SQL capturado): as duas edições passam juntas pela pré-checagem.
      - **Determinístico** (`AWriteBetweenTheCheckAndTheUpdate_Is409_NotA500`): um `SaveChangesInterceptor` de teste grava o e-mail disputado no rival por outra conexão, depois da pré-checagem do alvo. A resposta é `409`, e o SQL capturado traz o `UPDATE users SET` em "Failed executing DbCommand". O interceptor dispara uma vez (`Fired == 1`).

      **Guarda:** `catch` do `23505` neutralizado. Três execuções do caso, as três reprovaram com "Expected: Conflict / Actual: InternalServerError" (`guardas/6.5-reprova-{1,2,3}.log`).

- [x] 6.6 `UserDeactivationRoutesTests` ("Desativar outro usuário"):
      - outro com sessões → `200` `desativado`; a renovação dele → `invalid_grant`; login com a senha certa → `403`; detalhe com 0 sessões;
      - a si mesmo → `403`, continua ativo;
      - de novo → `200`, o stamp igual;
      - inexistente → `404`.

      **Guardas:**
      - revogação retirada → o detalhe mostra sessões, e o caso reprova;
      - recusa de si mesmo retirada → o caso de si mesmo reprova;
      - idempotência retirada (desativar de novo grava) → o caso do stamp igual reprova.

      **Feito, 01/11/2026.** `UserDeactivationRoutesTests`: 4 casos nesta tarefa.

      **Guardas:**

      | | defeito | reprovou |
      |---|---|---|
      | 6.6a | `EndAllAsync` retirado | `DeactivateSomeoneElse_EndsTheirSessions` |
      | 6.6b | recusa de si mesmo neutralizada | `DeactivateYourself_Is403` |
      | 6.6c | idempotência retirada | `DeactivateAgain_Is200_AndTheStampStays` |

- [x] 6.7 `UserDeactivationRoutesTests`, a corrida ("Dois únicos ativos desativando um ao outro"): só A e B ativos, `POST /users/B/deactivate` por A e `POST /users/A/deactivate` por B ao mesmo tempo (20 rodadas, cada uma com os dois reativados por semente) → sempre um `200` e um `403`, e um ativo no fim.

      **Asserção determinística:** no SQL capturado de uma desativação, `SELECT pg_advisory_xact_lock(4779819235982394417)` vem antes da releitura de quem chama, que vem antes do `UPDATE` de `users`, tudo na mesma transação.

      **Guardas:**
      - lock retirado → a asserção do SQL reprova em três execuções da classe;
      - releitura de quem chama retirada de dentro do lock (lock mantido, conferência só antes) → a asserção da ordem reprova.

      O comportamental é registrado com quantas das 20 rodadas reproduziram a corrida com o lock retirado.

      **Feito, 01/11/2026.**

      - **Comportamental** (20 rodadas, dois usuários novos por rodada, cada um desativando o outro): sempre um `200` e um `403`, e um dos dois ativo. **Em 20 das 20 rodadas o `403` veio da releitura dentro do lock**, com o título do `CurrentUser`: a segunda passou pela primeira conferência e esperou o lock.
      - **Determinístico** (`ADeactivation_TakesTheLock_ThenRereadsTheCaller_ThenWrites`): no SQL capturado, `pg_advisory_xact_lock` vem antes do `SELECT EXISTS … FROM users`, que vem antes do `UPDATE users SET`.

      **Guardas,** cada um em três execuções, as três reprovaram:

      - 6.7a, lock retirado: "a desativação não pediu o pg_advisory_xact_lock";
      - 6.7b, releitura retirada: "a releitura de quem chama não veio depois do lock (lock 1, releitura -1)".

- [x] 6.8 `UserActivationTests` ("Reativar usuário"): desativado com a verificação, reativado → `200`; o refresh de antes → `invalid_grant`; login com a mesma senha → `200 {segundoPasso}`, e a sessão nova renova; já ativo → `200` sem gravar; inexistente → `404`. **Guarda:** reativação que devolve um stamp guardado (o de antes da desativação) → o refresh de antes renova, e o caso reprova.

      **Feito, 01/11/2026.** `UserActivationTests`: 4 casos. **Premissa do guarda corrigida (convenção 15, segunda forma).** "Reativação que devolve um stamp guardado" não é um defeito possível: não há stamp guardado a devolver. E o refresh de antes cai pela autorização revogada na desativação, com ou sem stamp, como na 5.4. A comparação do stamp depois de desativar e reativar já é guardada pelo `UserDeactivationTests` da #180. **Guarda:** `Activate` sem `Status = Active` reprova `Reactivate_LoginComesBack_TheOldSessionsDoNot` e `AfterReactivation_ANewSessionRenews` (`guardas/6.8-reprova.log`).

- [x] 6.9 `UserSessionsAdminTests` ("Encerrar todas as sessões de outro usuário"): duas sessões vivas do outro → `200` `encerradas` 2, as duas renovações → `invalid_grant`, e o login seguinte abre uma sessão que renova; uma terceira autorização já revogada antes não entra na contagem; inexistente → `404`. **Guarda:** `status = valid` retirado do `EndAllAsync` → a contagem dá 3, e o caso reprova.

      **Feito, 01/11/2026.** `UserSessionsAdminTests`: 2 casos. A autorização já revogada é encerrada pela própria pessoa (`/account/sessions/{id}/revoke`) antes do encerramento pelo outro. **Guarda:** `Status = valid` retirado de `Open` reprova `EndSomeoneElsesSessions` (`guardas/6.9-reprova.log`).

- [x] 6.10 `UserRoutesCallerTests` (os dois "Rotas … atendem só o usuário real do token"):
      - HMAC `operator` → `403` nas 11 rotas, com o alvo intacto (nome, e-mail, stamp, estado e sessão viva conferidos depois);
      - usuário desativado com access token válido → `403` em `GET /account` e em `GET /users`.

      **Guarda:** `CurrentUser` retirado de `PUT /users/{id}` → o caso do HMAC numa rota de escrita reprova.

      **Feito, 01/11/2026.** `UserRoutesCallerTests`: 2 casos, as 11 rotas no primeiro. **Guarda:** `CurrentUser` retirado de `PUT /users/{id}` reprova `OperatorHmac_Is403_OnEveryRoute_AndNothingChanges` (`guardas/6.10-reprova.log`).

## 7. Nome e e-mail na renovação (`apps/api`, D5, P6, `issuer-user-login`)

- [x] 7.1 `UserGrantTokenHandler`: no grant `refresh_token` aceito, troca no principal os claims `name` e `email` pelos do usuário relido, mantendo os destinos (só o id token). O comentário do handler registra o porquê (H7).

      **Feito, 01/11/2026.** `RefreshProfileClaims` no grant `refresh_token` aceito, antes do registro do último acesso, com o destino regravado (2.3). O comentário da classe registra o porquê, com as linhas da fonte.

- [x] 7.2 `UserGrantClaimsRefreshTests` ("A renovação leva o nome e o e-mail vigentes"):
      - nome editado → o id token da renovação traz o nome novo, e a renovação seguinte, com o refresh novo, também;
      - e-mail editado → o id token traz o e-mail novo;
      - o access token não traz `name` nem `email`.

      **Guarda:** troca dos claims retirada → o primeiro caso reprova.

      **Feito, 01/11/2026.** `UserGrantClaimsRefreshTests`: 3 casos. O primeiro confere também o nome antigo no id token do login, para o caso não passar por um id token sem `name` nenhum.

      **Guardas:**

      | | defeito | reprovou |
      |---|---|---|
      | 7.2 | troca dos claims retirada | `AnEditedName_ReachesTheNextRenewal_AndTheOneAfter` e `AnEditedEmail_ReachesTheNextRenewal` |
      | 7.2b | só os `SetDestinations` retirados (a perda do destino da 2.3) | os mesmos dois |

      De volta, aprovam também o `RefreshRotationTests` e o `UserLoginFlowTests` (`guardas/7.2-aprova.log`).

## 8. Documentação (antes do archive)

- [x] 8.1 `01-ARQUITETURA_E_CONVENCOES.md` (Autenticação): as rotas de `/account` e `/users`; sessão = autorização viva com refresh vivo; "toda troca do stamp revoga, na mesma transação"; o `oi_au_id` no principal; o lock `BUTECOD1` e a tabela das cinco chaves; a política de senha.

      **Feito, 02/11/2026.** Bullet novo "Minha conta e Usuários (#219)" na seção Autenticação, com a tabela das cinco chaves em texto; a linha da redefinição das 2 etapas passa a dizer que revoga as sessões.

- [x] 8.2 `docs/architecture.md`: o mesmo, no nível do documento.

      **Feito, 02/11/2026.** Subseção "Minha conta e Usuários (`apps/api`, #219)", com o diagrama da lista e do "encerrar as outras"; a linha do `apps/api` na tabela dos apps; o `security_stamp` em "Usuários locais e login". **Também `docs/deployment.md`**, que a tarefa não listava: seção "Minha conta e Usuários (#219)", com o que muda para quem está logado, a conferência por `curl` e o rollback (as notas de deploy pedidas pelo dono).

- [x] 8.3 `02-HISTORICO_E_STATUS.md` e `CHANGELOG.md`, escritos antes do sync e do archive, com o que a implementação mediu:
      - as rodadas das corridas;
      - a medição do tamanho contra a projeção (convenção 18, só código, sem migrations);
      - as divergências do design (convenção 9);
      - a correção da #218.

      **Feito, 02/11/2026,** antes do sync e do archive: a seção `rotas-conta-usuarios` no fim do `02` (decisões, o que entrou, as medições, as divergências, o stack e o pós-merge) e a entrada da #219 no `CHANGELOG`, depois da das 2 etapas.

## 9. Fechamento

- [x] 9.1 As quatro suítes da baseline no worktree da change, pelo mesmo script e protocolo, com `CPU_Speed_Limit = 100` e os dois loads conferidos antes de cada uma: `apps/api`, `apps/frontend` (com o JSON), `tests/InboxOrchestratorRoundTrip.Tests` e `tests/ApiConnectorsRoundTrip.Tests`. Comparação **por nome** contra o `.trx` e o JSON da baseline (`compare-trx.py`): os testes de antes, todos com o mesmo resultado, e os novos listados um a um. `git diff` vazio em `tests/` e em `apps/frontend/`.

      **Resultado, 02/11/2026** (`219-fechamento/`), cada suíte com CPU 100 e os dois loads abaixo do limite no início:

      | suíte | resultado | início (load 1 / 5 min) | por nome contra a baseline |
      |---|---|---|---|
      | `apps/api` (3ª rodada, a que vale) | **1173 + 1 ignorado** | 4,84 / 6,60 | 0 sem par, **71 novos** (`api-novos.txt`), 0 com resultado mudado |
      | `apps/frontend` | **1978/1978**, 154 arquivos | 2,08 / 5,77 | 0 sem par, 0 novos, pelo caminho relativo do arquivo (o absoluto muda de worktree) |
      | `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 3,69 / 7,76 | igual |
      | `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 6,13 / 7,85 | igual |

      - **As rodadas anteriores do `apps/api`** (`api-1a/`, `api-1b/`) foram verdes também: 1172 + 1, igual à baseline por nome. A 1ª foi refeita pela troca do interceptor (9.2); a 2ª, pela correção do piso (9.3), que trouxe o 71º caso.
      - **Os de `tests/` rodaram de novo** depois das duas correções (`rt-1a/` guarda a primeira).
      - **`git diff` vazio em `tests/` e em `apps/frontend/`.**

- [x] 9.2 Build `--no-incremental` do `apps/api` e do projeto de testes com o mesmo conjunto de avisos da baseline.

      **Resultado, 02/11/2026** (`9.2-*`). **Primeira comparação: dois `EF1001` novos**, em `UserEditTests.cs`, pelo `NpgsqlOptionsExtension` (API interna), copiado do `KnowledgeDocumentCatalogTests.Concurrency`. O interceptor passou a gravar o rival pelo próprio `DbContext`, com `ExecuteSqlInterpolatedAsync` em autocommit antes da transação do `SaveChanges`, e entra por `ConfigureDbContext`, sem reconstruir as opções. O guarda 6.5 foi refeito com a classe inteira, três de três (`guardas/6.5-reprova-{1,2,3}.log`; a primeira versão em `6.5-primeira/`).

      **Final, sobre o código final:** o mesmo conjunto da baseline por código e contagem (`4 CS8602`, `4 CS8604`, `8 EF1001`, `12 EF1002`, `8 NU1903`) e por arquivo e mensagem. Os `NU1903` (`SSH.NET` 2025.1.0, via Testcontainers) são os da #146.

- [x] 9.3 Stack local (`docker-compose.prod.yml`, como na 13.3 da #218), com a saída colada aqui:
      - login do bootstrap pelo painel;
      - `GET /account/sessions` pelo `curl` com o token, a sessão marcada `atual`;
      - `GET /users` → `200`;
      - `GET /users` sem token → `401` do `apps/api`, e não `200` com HTML.

      **Resultado, 02/11/2026** (`219-fechamento/stack/`): `podman compose -p stack219 -f docker-compose.prod.yml --env-file stack.env up -d --build`, com `PUBLIC_DOMAIN=http://localhost:18219` e os segredos descartáveis da #218. O bootstrap criou o Admin. O contorno do `resolver` da #212 foi aplicado **só dentro do contêiner** do `frontend`, e de novo depois de recriar o `api`. Um segundo usuário foi semeado por SQL, com o hash do Admin, porque o convite é da #181. O fluxo foi conduzido por um roteiro sem seguir redirecionamentos, pelo nginx do stack. O painel não tem as telas (#183), então "ser deslogado" é a renovação do painel recebendo `invalid_grant`. Stack derrubado com `down -v`.

      ```
      GET /users, /account, /account/sessions sem token       → 401, content-type vazio, 0 bytes
      POST /users/{id}/deactivate sem token                  → 401, 0 bytes
      GET /usersxyz (sem separador)                          → 200 text/html (o painel)
      duas sessões do Admin, lista pelo token de B           → 200 [B atual:true, A atual:false]
      POST /account/sessions/{A}/revoke por B                → 204; renovação de A → invalid_grant (ID2022); lista → só B
      POST /account/sessions/revoke-others por C             → {"encerradas":1}; renovação de B → invalid_grant; a de C → par novo
      POST /users/{eu}/deactivate                            → 403 "Você não pode desativar o próprio usuário."
      POST /users/{outro}/deactivate                         → 200 desativado, sessoes.ativas 0; a sessão dele → invalid_grant
      login do outro, senha certa                            → 403 "Acesso desativado. Fale com o administrador."
      POST /users/{outro}/activate                           → 200 ativo; a sessão de antes → invalid_grant; login → 204
      POST /account/password (nova curta)                    → 400 "A senha precisa ter de 12 a 128 caracteres."
      POST /account/password                                 → 204; a sessão que trocou → invalid_grant
      login com a senha antiga / a nova                      → 401 / 204; a lista dele → só a sessão nova, atual
      ```

      **Achado: o último uso saía antes da criação** (`"criadaEm":"…03:56:57.77"`, `"ultimoUso":"…03:56:57"`). A data do token sai do `iat`, em segundos, e a da autorização tem a fração. **Correção:** o último uso tem piso na criação (`UserSessions.LastUse`), com o caso `TheLastUse_IsNeverBeforeTheCreation` visto reprovar antes da correção (`guardas/9.3-piso-reprova.log`). O `TheLastUse_FollowsTheRenewal` ganhou 1 s de tolerância pelo mesmo truncamento, e o guarda 6.3 foi refeito sobre o `SummarizeAsync` novo. Com a imagem refeita (`--force-recreate`), a sessão saiu com `ultimoUso` 04:02:09 ≥ `criadaEm` 04:02:08.69 (`9.3-piso.txt`). Não muda contrato: é um valor que o contrato já dizia (último uso ≥ criação), corrigido.

- [x] 9.4 Medição do tamanho (convenção 18) contra a projeção do design, colada aqui e no `02`.

      **Resultado, 02/11/2026** (só código, sem `openspec/` nem `.md`, arquivos novos contados por linha): produção **+765 / −31, 15 arquivos**; testes **+1.365, 17 arquivos**. Contra a projeção do design (~980 / ~1.550): **−22 % / −12 %**, para cima pela primeira vez na série da M1. Registrado no `02`.

- [x] 9.5 `openspec validate rotas-conta-usuarios --strict`; `/opsx:sync` e `/opsx:archive` dentro do worktree, com o `02` e o `CHANGELOG` já escritos (8.3); `openspec validate --all --strict` depois. Sem validação visual: nenhuma tela muda.

      **Feito, 02/11/2026** (`219-fechamento/9.5-*.log`). `openspec validate rotas-conta-usuarios --strict` válido; `openspec archive rotas-conta-usuarios --yes`, que aplicou as specs (o sync): `issuer-user-login` +1, `two-factor-totp` ~1, `user-account` +8 e `user-management` +7, total +16 / ~1. As 4 tarefas abertas eram esta e as três de commit, PR e limpeza, que vêm depois por desenho. O `Purpose` das duas specs novas, que o CLI gera como "TBD", foi escrito à mão. `openspec validate --all --strict`: 84 de 84. Sem validação visual: nenhuma tela muda.

- [ ] 9.6 O único commit da change, depois do sync e do archive, com tudo junto (código, testes, docs e OpenSpec), só com o pedido do dono naquela mensagem e sem atribuição.
- [ ] 9.7 Push e PR com `Closes #219`, só com o ok do dono naquela mensagem; mover a #219 para `In review`.
- [ ] 9.8 Depois do merge: remover os dois worktrees, um de cada vez, e a branch local com `git branch -d`.
