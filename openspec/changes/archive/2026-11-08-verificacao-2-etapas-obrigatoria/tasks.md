Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-220.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-183.sh`, com os projetos `rt-inbox` e `rt-connectors` a mais. A mutação toca só o trecho nomeado, e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação e reprova por "no tests" não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/220-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-220.sh` (`api`, `frontend`, `rt-inbox`, `rt-connectors`), com o RabbitMQ de pé (`PAUSAR_RABBIT=0`).
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada da `frontend`, o load fica abaixo de 6; das outras, abaixo de 8.
- O `rt-inbox` tem uma intermitente conhecida (`Source3`, #176): se ela aparecer, o comentário vai na #176, e não numa issue nova.

**A baseline** está em `~/.cache/buteco-agents/220-baseline/`, em `8471250`, com a árvore limpa: `apps/api` 1298/1298 + 1 ignorado, `apps/frontend` 2329/2329 em 185 arquivos, `rt-inbox` 14/14, `rt-connectors` 10/10, todas `APROVADA`, e o relatório por nome em `nomes.txt` (3652 linhas). Detalhes no design ("Baseline").

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Casos que saem ou mudam de asserção**, porque o requisito sai ou muda nesta change (delta specs). Todo outro caso existente fica sem remoção, renome nem afrouxamento:
- `apps/api`: os casos de cadastro fechado e os de `setup`, `confirm` e `remove` por access token em `TwoFactorEnrollmentTests` (`two-factor-totp`, requisitos removidos); os casos que esperam `204` do `POST /auth/session` (`issuer-user-login`, "Rota de login abre a sessão do emissor") passam a esperar `200 {segundoPasso:"cadastro"}`; o `403` de si mesmo em `TwoFactorAdminResetTests` (texto novo); o login seguinte do redefinido (`"cadastro"` em vez de `204`);
- `apps/frontend`: no `TwoFactorCard.test.tsx`, os casos de `indisponivel`, `desligada`, `senha*`, `qr*` e `remover*` (`panel-account-page`, requisitos removidos); no `accountApi.test.ts`, as rotas `setup`, `confirm` e `remove`; os casos de `sessionApi`, `EntrarPage`, `AccessLinkPage` e do fluxo do emissor que partem do `204` do `POST /auth/session` passam ao `totp` ou ao `cadastro`; os textos do `UserTwoFactorCard` e da confirmação.

Cada um é listado por nome na comparação da 10.3, com o requisito que o tirou ou mudou. Um caso fora desta lista que precise sair ou afrouxar é parada.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-220.sh`, cópia do `guarda-183.sh`, com os projetos `rt-inbox` e `rt-connectors`. Conferir com uma mutação nula (o script recusa com "defeito não aplicado") e uma real em `apps/api/src` e outra em `apps/frontend/src` (os arquivos voltam com o mesmo sha).
  **Feito (08/11/2026):** a mutação nula recusa ("defeito não aplicado"). As reais (um comentário no fim de `apps/frontend/src/utils/searchText.ts` e de `apps/api/src/Buteco.Api/Users/TwoFactor/Totp.cs`) rodaram `searchText.test.ts` (11/11) e `TotpTests` (13/13) e voltaram com o sha igual (`b31cba75…` e `cb545d8d…`).

## 1. O login e o cookie (apps/api, D1, D2, D8)

- [x] 1.1 (apps/api) `Identity/Flow/IssuerSession.cs`: o `SignInAsync` ganha o vencimento e o claim `buteco_cadastro_ate` opcionais; os chamadores de hoje não mudam. Uma constante para o claim.
  **Feito:** `EnrollmentDeadlineClaim` (`buteco_cadastro_ate`, segundos Unix), o parâmetro `enrollmentDeadline` (claim e `ExpiresUtc`) e `EnrollmentDeadline(principal)`.
- [x] 1.2 (apps/api) `Support/IdentityIssuerTestClient.cs`: o `SessionCookieAsync` passa a reconhecer `200 {segundoPasso:"cadastro"}` e a cadastrar (`setup`, o código do segredo pelo `Totp` e o `FakeTimeProvider` do host, `confirm`), devolvendo o cookie completo. Feito **antes** da 1.3, para os 39 arquivos que entram pelo login continuarem sem mudança.
  **Feito, com duas premissas que caíram (convenção 9, seguir e registrar):**
  - **o `SessionCookieAsync` não tinha acesso ao relógio** (só o `HttpClient`). Ganhou o `IServiceProvider` (o `TimeProvider` registrado, falso ou real), e os 8 arquivos que o chamam direto passaram a mandar o `factory.Services`, sem mudar asserção. As chamadas HTTP do cadastro ficaram em `TwoFactorTestClient.EnrollmentAsync` e `EnrollAsync`;
  - **o segundo login do mesmo usuário já encontra a verificação** (o primeiro cadastrou): 22 casos da r1 reprovaram com "Login falhou: 200 totp". O apoio guarda os códigos de recuperação do cadastro por e-mail (`TwoFactorTestClient.Remember`) e entra com um deles no `"totp"`. O código do app no mesmo passo seria recusado como reuso, e avançar o relógio da fixture mudaria o tempo dos testes.
- [x] 1.3 (apps/api) `Users/Login/SessionEndpoints.cs`: o ramo sem a verificação responde `200 { segundoPasso: "cadastro" }`, zera a contagem, grava o cookie pendente e **não** registra o último acesso. O `204` sai.
  - Testes em `TwoFactorRequiredLoginTests`: a resposta e o cookie; a contagem zerada; o último acesso sem mudança; o authorize com esse cookie → `302 /entrar`.
  **Feito:** `200 { segundoPasso: "cadastro" }` (`EnrollmentStep`), contagem zerada, sem último acesso. O `TimeProvider` saiu do `CreateAsync`. Os casos: `TwoFactorRequiredLoginTests.WithoutTheVerification_TheLoginEnrolls_AndOnlyTheConfirmGivesACode`, `ThePasswordWithoutTheVerification_DoesNotRecordTheAccess` e `TheEnrollmentCookie_IsNoSession_BeforeAndAfterTheSetup`.
- [x] 1.4 (apps/api) Os casos existentes que esperavam o `204` do login passam a `200 {segundoPasso:"cadastro"}` (lista do topo), cada um registrado aqui por nome.
  **Feito:** `LocalUserSeed.AssertPasswordAcceptedAsync(login, passo)` confere o `200` e o valor exato de `segundoPasso` (`cadastro` para quem nunca entrou, `totp` para quem já cadastrou). Mudaram de asserção, todos da lista do topo (`issuer-user-login`, "Rota de login abre a sessão do emissor"; `user-access-links`, os três requisitos modificados):
  - `InvitationAcceptTests`: `ALinkRewrittenOnAnActive_IsNotAcceptedAgain`, `Accepting_ClearsTheEmailLock`, `TwoConcurrentAccepts_OneWins…` (`cadastro`);
  - `PasswordResetTests`: `Generating_Returns200_With24Hours_AndTheCurrentPasswordKeepsWorking` e `Completing_ChangesThePassword_EndsEverySession_AndOpensNone` (`totp`), `Completing_ClearsTheEmailLock` (`cadastro`);
  - `AccountPasswordTests`: `WithoutTheCurrentPassword_Is401`, `ANewPasswordOutsideThePolicy_Is400_AndTheCurrentOneKeepsWorking`, `ACurrentPasswordShorterThanThePolicy_StillWorks`, `TheChange_ReplacesThePassword` (`totp`);
  - `UserEditTests.ChangingTheEmail_MovesTheLogin_AndKeepsTheSub_AndTheSessions` (`totp`); `LoginThrottleTests.Success_ZeroesTheCount` e `UserBootstrapTests.TheFirstLogin_RehashesWithArgon2id_AndTheNextLoginStillWorks` (`cadastro`); `TwoFactorAdminResetTests.ResetAnotherUser_TurnsItOff_AndTheirRenewalIsRefused` (o login do redefinido, `cadastro`);
  - **renomeados**, porque o nome dizia `204`: `SessionLoginTests.ValidLogin_Returns204_WithTheSessionCookie` → `ValidLogin_Returns200WithTheEnrollmentStep_AndThePendingSessionCookie` (também o último acesso: era gravado no login, agora `Assert.Null`, pela spec modificada); `TwoFactorLoginTests.WithoutTheVerification_TheLoginIsStill204` → `WithoutTheVerification_TheLoginAsksForTheEnrollment`.
  - **setup mudado, asserção igual:** `TwoFactorLoginTests.CookieFromBeforeTurningTheVerificationOn_IsNoSession` usa o cookie pendente (com a obrigatoriedade, é o único cookie de antes da verificação); `TwoFactorAdminResetTests.ResetSomeoneWithoutTheVerification_KeepsTheirSessions` desliga a verificação no banco depois do login (`TwoFactorTestClient.SeedDisabledAsync`, a sessão de antes da obrigatoriedade).

## 2. As rotas de cadastro (apps/api, D1, D2, D5)

- [x] 2.1 (apps/api) `Users/TwoFactor/TwoFactorEnrollment.cs`: a lógica de `setup` e `confirm` extraída do `AccountTwoFactorEndpoints` (segredo novo cifrado, URI, conferência do pendente, ligar e gerar os códigos numa transação). `User.StartTwoFactorEnrollment` recebe o início.
  **Feito:** `TwoFactorEnrollment` (`StartAsync`, `IsPendingLive`, `Match`, `CompleteAsync`), registrado como scoped. O `StartTwoFactorEnrollment` já recebia o instante; o parâmetro passou a se chamar `startedAt`, com o comentário do prazo.
- [x] 2.2 (apps/api) `POST /auth/session/enrollment/setup` e `/confirm` em `SessionEndpoints`, anônimas, `AuthEntryPoint`, `LoginRateLimiting.Marker`, com as guardas de JSON e `Origin`. O cookie aceito é o pendente de usuário ativo, stamp igual e **sem** a verificação; fora disso, `401` `ExpiredMessage`, sem contar. `423` antes de qualquer efeito.
  - `setup`: o primeiro grava `buteco_cadastro_ate` = agora + 10 min e regrava o cookie até lá; os seguintes trocam o segredo e mantêm o prazo.
  - `confirm`: certo → liga, apaga o pendente, grava o passo, 10 códigos; depois zera a contagem, registra o acesso e grava o cookie completo; errado → `401` "Código inválido.", contado; sem pendente ou vencido → `401` `ExpiredMessage`.
  - **(guarda)** Defeito: o `setup` aceita o cookie de quem já tem a verificação. Reprova o caso do cookie de quem já tem a verificação.
  - **(guarda)** Defeito: o `setup` seguinte recalcula o prazo (agora + 10). Reprova "o prazo é o do primeiro setup" (0, 8, 10,5).
  - **(guarda)** Defeito: o `setup` regrava o cookie sem `ExpiresUtc`. Reprova "o cookie regravado vale até o fim do prazo" (senha, setup no 4, confirm no 13).
  - **(guarda)** Defeito: o `setup` mantém o segredo pendente anterior quando já há um. Reprova "setup de novo troca o segredo".
  - Testes também: o cookie de A não cadastra B; o cookie em cadastro no `second-factor` → `401`; o código do `confirm` recusado no `second-factor` seguinte; `Origin` de outro site → `403`; bloqueio → `423`.
  **Feito.** Um detalhe a mais que o tasks previa: o `confirm` exige o claim do prazo no cookie, ou seja, um `setup` **daquele** cookie (`ConfirmWithoutASetupOnThisCookie_TheEntryExpired`), para um pendente de um login anterior não ser confirmado por um cookie novo.
  **Visto reprovar:**
  - `g2.2a-cookie-com-verificacao` (o `IsTwoFactorEnabled: false` retirado de `EnrollingUserAsync`) → `TheCookieOfSomeoneWithTheVerification_DoesNotEnroll` (`Assert.Equal() Failure: Values differ`). Voltou: `Users/Login/SessionEndpoints.cs` (sha igual: `ecff3624…`);
  - `g2.2b-prazo-estendido` (o `setup` seguinte abre um prazo novo) → `TheDeadline_IsTheFirstSetups` (`Values differ`). Voltou: `Users/TwoFactor/TwoFactorEnrollment.cs` (`3312575b…`);
  - `g2.2c-sem-expiresutc` → `TheRewrittenCookie_LastsUntilTheDeadline` (`Values differ`). Voltou: `Identity/Flow/IssuerSession.cs` (`84c201c7…`);
  - `g2.2d-segredo-mantido` (o pendente só gravado se vazio) → `SetupAgain_ReplacesTheSecret` (`Values differ`). Voltou: `Users/TwoFactor/TwoFactorEnrollment.cs` (`3312575b…`).
- [x] 2.3 (apps/api) `RouteAuthenticationTests`: as duas rotas classificadas como `AuthEntryPoint`, e nenhuma outra rota anônima nova.
  **Feito:** `RouteAuthenticationTests.EnrollmentRoutes_WithoutToken_StayAnonymous` (as duas, `401` "A entrada expirou…" e não o `401` vazio) e `TheAuthEntryPoints_AreTheKnownOnes` (a lista das 9 rotas `AuthEntryPoint`, as duas novas incluídas). 23/23 no arquivo.

## 3. Authorize, renovação e logout (apps/api, D3, D4, D5)

- [x] 3.1 (apps/api) `Identity/Flow/AuthorizeEndpoint.cs`: `user.IsTwoFactorEnabled && twoFactorDone`.
  - **(guarda)** Defeito: a condição de hoje (`!IsTwoFactorEnabled || twoFactorDone`). Reprova "Cookie de cadastro é tratado como ausência de sessão".
  **Visto reprovar** (`g3.1-authorize-antigo`) → `TheEnrollmentCookie_IsNoSession_BeforeAndAfterTheSetup` (`Assert.StartsWith() Failure`). Voltou: `Identity/Flow/AuthorizeEndpoint.cs` (`74588454…`).
- [x] 3.2 (apps/api) `Identity/Flow/UserGrantTokenHandler.cs`: `!user.IsTwoFactorEnabled` → `invalid_grant`, nos dois grants. `TwoFactorRequiredRenewalTests.Renewal_WithoutTwoFactor_IsInvalidGrant_ThePanelEndsTheSession`: `400`, `Content-Type` JSON, `error` = `invalid_grant`, com o comentário que aponta `apps/frontend/src/auth/renewal.ts` (o leitor do formato); e quem tem a verificação renova.
  - **(guarda)** Defeito: a condição retirada. Reprova o caso de contrato.
  **Visto reprovar** (`g3.2-renovacao-sem-condicao`) → `Renewal_WithoutTwoFactor_IsInvalidGrant_ThePanelEndsTheSession` (`Values differ`). Voltou: `Identity/Flow/UserGrantTokenHandler.cs` (`ba094dd4…`).
- [x] 3.3 (apps/api) `Identity/Flow/EndSessionEndpoint.cs`: com o cookie pendente válido de usuário sem a verificação, numa transação, apaga o cadastro pendente e troca o stamp (com a revogação da regra de `user-account`), e então apaga o cookie. `TwoFactorEnrollmentLogoutTests`: o pendente some, o stamp muda, `302 /entrar`, a cópia do cookie → `401` no `confirm`; o cookie completo de quem tem a verificação não troca o stamp.
  - **(guarda)** Defeito: a troca do stamp retirada (só apaga o pendente). Reprova "a cópia do cookie não confirma".
  **Feito:** `User.CancelTwoFactorEnrollment()` e o `CancelEnrollmentAsync` do logout, antes do `SignOutAsync`.
  **Visto reprovar** (`g3.3-logout-sem-stamp`) → `LogoutDuringTheEnrollment_EndsTheCookieOnTheServer` (`Assert.NotEqual() Failure: Strings are equal`). Voltou: `Users/User.cs` (`c6c84889…`).

## 4. Remoções (apps/api, D5)

- [x] 4.1 (apps/api) Sai `Users/TwoFactor/TwoFactorOptions.cs` e o registro dele; sai a seção `Identity:TwoFactor` de `appsettings.json` (com o comentário) e de `appsettings.Development.json`; sai o override de `Support/TestAuthentication.cs`.
  **Feito.** O `TwoFactorOptions.cs` saiu por `rm`, e não por `git rm`, para não mexer no índice.
- [x] 4.2 (apps/api) `AccountTwoFactorEndpoints`: sai `cadastroDisponivel` do `StatusResponse`, o `EnrollmentClosedMessage`, e o mapeamento de `setup`, `confirm` e `remove`. Ficam o `GET` e o `recovery-codes`. `TwoFactorEnrollmentTests`: saem os casos da lista do topo, registrados por nome; entram "não há rota de remover" e "não há cadastro por access token".
  **Feito.** `User.RemoveTwoFactor` ficou privado (só a redefinição o usa). Saíram, com os requisitos removidos de `two-factor-totp`:
  - de `TwoFactorEnrollmentTests`: `Setup_WithTheWrongPassword_Is401_AndCounts`, `Setup_ReturnsTheSecretAndTheUri`, `Confirm_WithAWrongCode_DoesNotTurnItOn`, `Confirm_WithTheRightCode_TurnsItOn_ReturnsTenCodes_AndTheNextLoginAsksForTheCode`, `Confirm_AfterTenMinutes_Is400`, `Setup_WithTheVerificationOn_Is409_AndTheSecretStays`, `Remove_WithACode_TurnsItOff_AndTheLoginIs204Again`, `Remove_WithAWrongCode_Is401_StaysOn_AndCounts`; `RegenerateOrRemove_WithoutTheVerification_Is409` virou `Regenerate_WithoutTheVerification_Is409` (a metade do remove saiu). O comportamento do cadastro está agora em `TwoFactorRequiredLoginTests`;
  - a classe `TwoFactorEnrollmentClosedTests` inteira (5 casos: `Setup_IsClosed_AndNothingIsPending`, `Confirm_IsClosed`, `Status_WithoutTheVerification_SaysTheEnrollmentIsClosed`, `TheVerification_StillAppliesAtLogin`, `TheAdminReset_StillWorks`);
  - `TheSecret_NeverComesBackInARead`: a asserção de `cadastroDisponivel: true` passou a conferir a ausência do campo;
  - entraram `ThereIsNoRemoveRoute` e `ThereIsNoEnrollmentByAccessToken`. `TwoFactorAccount` cadastra pelo login e guarda o segredo e os códigos.
- [x] 4.3 (config) `docker-compose.prod.yml`: sai `Identity__TwoFactor__EnrollmentEnabled` e o comentário; `.env.prod.example`: sai o bloco comentado.
  **Feito:** `podman compose -f docker-compose.prod.yml --env-file .env.prod.example config` sai `0`.

## 5. A redefinição e o comando de emergência (apps/api, D5, D6)

- [x] 5.1 (apps/api) `Users/TwoFactor/TwoFactorReset.cs`: a operação extraída do `UserTwoFactorResetEndpoint` (apagar, trocar o stamp, encerrar as sessões, numa transação). A rota passa a usá-lo, e o `403` de si mesmo vira "Você não pode redefinir a própria verificação. Peça a outro usuário, ou use o comando de emergência no servidor.". `TwoFactorAdminResetTests`: o texto novo e o login seguinte em `"cadastro"`.
  **Feito:** `TwoFactorReset.ResetAsync` devolve as sessões encerradas. `ResetYourself_Is403` ganhou a asserção do texto.
- [x] 5.2 (apps/api) `Users/TwoFactor/TwoFactorEmergencyCommand.cs` e o ramo de `args` no `Program.cs`, antes do `WebApplication.CreateBuilder`: um host mínimo (configuração, log, `AppDbContext`, `UserSessions`, `TwoFactorReset`), sem Kestrel nem serviços em segundo plano. Saídas `0`, `1`, `2`, `3` e `64`; a linha `Warning` com `EventId` próprio.
  - Uma exceção não tratada sai com `70`, nunca com `1` nem com outro código de resultado; a linha traz o tipo do erro, sem a string de conexão nem a senha. O comando não chama `Migrate`.
  - `TwoFactorEmergencyResetTests`, contra o Postgres do Testcontainers, pela mesma entrada do `Program.cs`: os seis códigos de saída (`0`, `1`, `2`, `3`, `64` e `70`, este com a conexão numa porta fechada) e o efeito ou a falta dele; a saída capturada com o `EventId` e sem segredo, código, hash, stamp, string de conexão ou senha; o histórico de migrations sem mudança com uma migration pendente.
  - **(guarda)** Defeito: o `catch` geral devolvendo `1`. Reprova "banco fora do ar não vira usuário inexistente".
  - **(guarda)** Defeito: o comando sem a troca do stamp. Reprova o caso do `0`.
  - **(guarda)** Defeito: o comando aceita o desativado. Reprova o caso do `2`.
  **Feito.** O teste roda o `Buteco.Api.dll` de verdade, num processo à parte (`Support/EmergencyCommandRunner.cs`), com a conexão do Testcontainers (`ApiFactoryFixture.PostgresConnectionString`). O `EventId` é o `1196`. O processo do comando só escreve o próprio log: o do host e o do EF ficam de fora, para nenhuma linha levar SQL ou endereço do banco.
  **Visto reprovar:**
  - `g5.2a-comando-sem-stamp` (a troca do stamp retirada do `User.ResetTwoFactor`, que o `TwoFactorReset` usa) → `Reset_ExitsZero_EndsTheSessions_AndTheNextLoginEnrolls` (`Strings are equal`). Voltou: `Users/User.cs` (`c6c84889…`);
  - `g5.2b-comando-aceita-desativado` → `Deactivated_ExitsTwo_AndNothingChanges` (`Values differ`). Voltou: `Users/TwoFactor/TwoFactorEmergencyCommand.cs` (`9cfb50a2…`);
  - `g5.2c-catch-devolve-1` → `TheDatabaseDown_ExitsSeventy_AndDoesNotSayTheUserDoesNotExist` (`Values differ`). Voltou: o mesmo arquivo (`9cfb50a2…`).
- [x] 5.3 (apps/api) `TwoFactorResetParityTests`: o mesmo estado inicial redefinido pela rota e pelo comando termina igual (colunas, códigos, stamp trocado, autorizações revogadas).
  - **(guarda)** Defeito: a rota com uma cópia da operação sem a revogação. Reprova a paridade.
  **Visto reprovar** (`g5.3-rota-sem-revogacao-r2`): a rota com uma cópia da operação sem o `EndAllAsync` → `TheRouteAndTheCommand_EndInTheSameState` (`Values differ`, a lista de autorizações). Voltou: `Users/TwoFactor/UserTwoFactorResetEndpoint.cs` (`fe19d903…`). A primeira tentativa (`g5.3-rota-sem-revogacao`) quebrou a compilação (`ExecuteDeleteAsync` na classe errada) e foi descartada.

## 6. Os acordos de `tests/` (D9)

- [x] 6.1 (tests) `tests/InboxOrchestratorRoundTrip.Tests/Support/IssuerUserFlow.cs` e a cópia em `tests/ApiConnectorsRoundTrip.Tests/Support/`: com `"cadastro"`, o fluxo cadastra o Admin do bootstrap (`setup`, o código pelo RFC 6238 calculado no próprio projeto, `confirm`) e segue para o authorize com o cookie completo. As duas cópias iguais, com o comentário de espelho mantido.
  **Feito, com duas premissas que caíram (convenção 9, seguir e registrar; só apoio de teste):**
  - **"um login por host" não vale:** os hosts de um mesmo acordo sobem sobre o mesmo banco (o connectors faz um `Build` por teste, e o rt-inbox um `apps/api` novo no `Source3`), e o segundo encontra o Admin já cadastrado. A chave do cadastro é a conexão do banco (`RoundTripFixture.IssuerDatabaseOf(api)`), e não o host;
  - **10 códigos não bastam:** o rt-inbox faz mais de 10 logins no mesmo banco. Com 2 ou menos, o fluxo faz um login a mais com o escopo `buteco-api` e regenera os 10 por `POST /account/two-factor/recovery-codes`.
  O `AccessTokenAsync` ganhou o primeiro parâmetro (`issuerDatabase`), e as 6 chamadas o passam. As duas cópias continuam iguais (diff vazio, fora o namespace). 14/14 e 10/10.

## 7. O painel: a etapa do cadastro (apps/frontend, D7)

- [x] 7.1 (apps/frontend) `features/auth/api/sessionApi.ts`: `createSession` devolve `{ kind: 'segundo-passo', passo: 'totp' | 'cadastro' }`; `startEnrollment()` e `confirmEnrollment(codigo)`, sem token. `sessionApi.test.ts`: os dois valores, um terceiro desconhecido (aviso genérico) e as duas chamadas novas.
  **Feito.** O `kind: 'ok'` saiu do `SessionResult`: um `204` do `POST /auth/session` cai no aviso genérico. No `sessionApi.test.ts`, "204 é sessão aberta" virou "204 não é sessão…" (da lista), e entraram o `cadastro`, o desconhecido e os testes das duas chamadas novas (19/19).
- [x] 7.2 (apps/frontend) `TwoFactorQr` e `RecoveryCodes` vão para `features/two-factor/components/`, com os testes. O `RecoveryCodes` recebe o título, o rótulo do download e o botão final. Minha conta continua mostrando os mesmos textos.
  **Feito:** `TwoFactorQr`, `RecoveryCodes`, `recoveryCodesFile` e `groupSecret` (em `secretText.ts`) foram para `features/two-factor/`, por `mv`. O `RecoveryCodes` ganhou `downloadLabel`, `doneLabel` e `badge`. O `TwoFactorQr` passou ao layout do quadro `Main` (vertical, para o cartão do login): Minha conta não o usa mais. Sem teste próprio: os casos do QR estão no `EntrarPage.test.tsx`.
- [x] 7.3 (apps/frontend) `features/auth/components/EnrollmentForm.tsx` e os textos em `features/auth/messages.ts`: a etapa do quadro `Main`, estados `cadastro`, `cadastro-qr-carregando`, `cadastro-qr-falhou`, `cadastro-codigo-vazio`, `cadastro-codigo-invalido`, `cadastro-bloqueado`, `cadastro-muitas-tentativas`, `cadastro-indisponivel`, `cadastro-falha-de-rede` e `cadastro-codigos`. As chamadas diretas, sem `useMutation`.
  - **(guarda)** Defeito: o `401` reconhecido como expirado por `startsWith` em vez da igualdade com `ENTRADA_EXPIROU`. Reprova o caso do `401` com outro texto.
  - **(guarda)** Defeito: o segredo guardado no `sessionStorage`. Reprova "nada do cadastro fica guardado".
  **Feito.** O `setup` usa um ref por tentativa, para o StrictMode do desenvolvimento (que monta duas vezes) não gravar dois segredos e mostrar o primeiro (`no StrictMode, a etapa faz um setup só`). O e-mail do `.txt` sai do rótulo do `otpauthUri`, o do servidor.
  **Visto reprovar:**
  - `g7.3a-expirou-por-prefixo` → `etapa do cadastro > outro texto de 401 não é tomado por entrada expirada`. Voltou: `features/auth/components/EnrollmentForm.tsx` (`f03c44d3…`);
  - `g7.3b-segredo-no-storage` → `nada do cadastro fica guardado: nem o segredo, nem o URI, nem os códigos`. Voltou: o mesmo arquivo (`f03c44d3…`).
- [x] 7.4 (apps/frontend) `EntrarPage.tsx`: a etapa `"cadastro"`; "Continuar" pelo `continuarEntrada()`; "Voltar" navegando a `/connect/logout`; o `state` `{ etapa: "cadastro" }` com o aviso `cadastro-apos-nova-senha`. `EntrarPage.test.tsx` e `loginFlow.integration.test.tsx`: os cenários de `operator-login-ui` e de `panel-issuer-session`.
  - **(guarda)** Defeito: "Voltar" só troca a etapa. Reprova "Voltar encerra o cadastro no servidor".
  - O `state` `{ etapa: "cadastro" }` lido uma vez e limpo com `navigate(…, { replace: true })`, mantendo a query. Os outros dois `state` ficam como estão (observação do design).
  - **(guarda)** Defeito: o `state` não é limpo. Reprova "Vindo do link, recarregar volta a e-mail e senha".
  - O cenário "Continuar depois de 5 minutos" em `loginFlow.integration.test.tsx` (o authorize de mentira responde `302 /entrar`), e no `apps/api` um caso em `TwoFactorRequiredLoginTests`: depois do `confirm`, 6 min no `FakeTimeProvider`, o authorize → `302`, e o login com a senha e um código do app, ou um de recuperação mostrado, completa.
  **Feito:** 22 casos novos em `etapa do cadastro` e 2 em `vindo das páginas do link`. Os três "204 …" viraram "sessão completa …" pelo segundo passo, e entraram "o POST /auth/session com 204, que não existe mais, cai no aviso genérico" e "um segundo passo desconhecido…". No `loginFlow.integration.test.tsx`, "da rota protegida até a lista de agentes…" passa pelo cadastro, e entrou "Continuar depois de 5 minutos…" (a volta do authorize é uma árvore nova, como a página nova do navegador). O caso do `apps/api` é o `TwoFactorRequiredLoginTests.ContinueAfterFiveMinutes_TheAuthorizeSendsToLogin_AndTheNewAppAndCodesWork`.
  **Visto reprovar:**
  - `g7.4a-voltar-so-troca-a-etapa` → `Voltar encerra o cadastro no servidor, pelo /connect/logout sem hint`. Voltou: `features/auth/pages/EntrarPage.tsx` (`1c080571…`);
  - `g7.4b-state-nao-limpo` → `com { etapa: "cadastro" }, o state é limpo, e recarregar volta a e-mail e senha mantendo a query`. Voltou: o mesmo arquivo (`1c080571…`).
- [x] 7.5 (apps/frontend) `AccessLinkPage.tsx`: o `"cadastro"` navega ao `/entrar` com `{ etapa: "cadastro" }`. `AccessLinkPage.test.tsx`: "Aceite seguido do cadastro", com o `state` sem a senha.
  **Feito.** Os imports de `authRuntime` e `continueEntry` saíram. "entra com o e-mail da consulta e segue o fluxo do emissor para a tela inicial" virou "…e chega ao /entrar na etapa do cadastro, sem a senha no state"; "o token nunca vai numa URL" e "o sucesso apaga o token" esperam a etapa em vez do authorize (da lista). 53/53.

## 8. O painel: Minha conta e Editar usuário (apps/frontend, D7)

- [x] 8.1 (apps/frontend) `features/account/api/accountApi.ts`: saem `cadastroDisponivel`, `setupTwoFactor`, `confirmTwoFactor` e `removeTwoFactor`; `test/fakeAccountApi.ts` acompanha.
  **Feito:** saíram as três rotas do `accountApi.test.ts` (da lista).
- [x] 8.2 (apps/frontend) `TwoFactorCard.tsx`: só `ativa` (com "Trocou de celular ou perdeu o app?…"), `poucos-codigos`, `regenerar*`, `codigos` e `nao-configurada`. `AccountPage.tsx`: o subtítulo novo. Os casos que saem, da lista do topo, registrados por nome.
  **Feito.** Saíram do `TwoFactorCard.test.tsx` (da lista, `panel-account-page`): `cadastro fechado…`, `cadastro aberto, desligada…`, os 5 de `ligar a verificação`, os 3 de `QR e segredo` (agora no `EntrarPage.test.tsx`), `código errado ao remover…`, `remover desliga…`, `servidor indisponível no cadastro…`, `senha bloqueada no início do cadastro…`, `cadastro fechado no meio do caminho…`. Mudaram: `ativa com o cadastro fechado continua gerenciável` → `ativa: "Gerar novos códigos" e o texto de quem trocou de celular, sem "Remover verificação"`; `nada no storage, no passo do QR e no dos códigos` → `nada no storage, no passo dos códigos`; os de códigos passaram pelo caminho da regeneração; o `describe` "gerar novos códigos e remover" virou "gerar novos códigos". Entraram `sem a verificação…`, `servidor indisponível ao gerar códigos…`, `bloqueio ao gerar códigos…` e `verificação redefinida no meio do caminho…`. 54/54 em `features/account`.
  **Premissa que caiu:** o subtítulo da página muda o caso `AccountPage > os quatro cartões…`, que a lista do topo não previa. É troca de texto pela spec modificada (`panel-account-page`, "Página Minha conta em /account"), sem tirar nem afrouxar asserção: segui e registro.
- [x] 8.3 (apps/frontend) `UserTwoFactorCard.tsx` e `userDetailTexts.ts`: os textos do `UsuarioDetalhe` (`desligada`, `ativa`, a confirmação, o próprio). `UserTwoFactorCard.test.tsx` e o caso do `403` de si mesmo com o texto novo.
  **Feito:** 120/120 em `features/users`. Os textos mudaram também em `UserDetailPage.test.tsx` (o do próprio).

## 9. Documentação de deploy e desenvolvimento (D6, Migration Plan)

- [x] 9.1 (docs) `docs/deployment.md`: sai a seção "Minha conta e o cadastro das 2 etapas (#246)" e a linha da tabela de variáveis; entram o comando de emergência (quem usa, o comando, as saídas, onde fica o registro) e as notas da #220 (antes, deploy, conferir, cada usuário, voltar atrás), com o aviso à equipe para entrar logo depois da virada.
  **Feito:** a seção "Verificação em 2 etapas obrigatória e o comando de emergência (#220)" no lugar da da #246, que ficou como "Minha conta (#246) (histórico)" só com o que não é o cadastro (a página e a troca de senha). As referências da #245 e da #218 à opção passaram ao passado, com link para a seção nova.
- [x] 9.2 (docs) `docs/configuration.md` (as duas linhas da opção), `docs/development.md` (o cadastro em desenvolvimento passa pelo login; a saída de quem perder o app local é o comando), `docs/architecture.md` (o diagrama do login com o `"cadastro"`), `SECURITY.md` (a verificação obrigatória e o comando).
  **Feito.** O `development.md` ganhou o fluxo por `curl` com o segundo passo (o `204` do passo 1 não existe mais) e o comando por `dotnet run -- two-factor-reset`.

## 10. Suítes e conferências

- [x] 10.1 (apps/api + apps/frontend) `dotnet build` sem aviso novo; `npm run lint` e `npm run build`, com a diferença do bundle contra a baseline (minificado e gzip).
  **Feito (08/11/2026):** `dotnet build` com os mesmos 18 avisos da baseline (nenhum novo); `npm run lint` sem erro nem aviso; `npm run build` (com o `tsc -b`) passa nos dois worktrees.

  | | `8471250` (baseline) | change | diferença |
  |---|---|---|---|
  | `index-*.js` minificado | 1.169.768 B | 1.171.720 B | **+1.952 B** |
  | gzip | 336.013 B | 336.383 B | **+370 B** |
- [x] 10.2 Suítes completas pelo `suite-220.sh`: `api`, `frontend`, `rt-inbox` e `rt-connectors`, cada uma com o veredito e a CPU mínima.
  **Feito (08/11/2026), com a máquina em limite térmico** (`CPU_Speed_Limit` em 23 durante horas, com load baixo):
  - `apps/api`: **r1 INCONCLUSIVA** (273 reprovações de infraestrutura, 45 min, CPU 23: cancelamentos, timeouts, falha transitória do Npgsql, o anel do emissor sem carregar); **r2 APROVADA**, 1318/1318 + 1 ignorado, 27 min 36 s, CPU mínima 23 (`220-fechamento-r2/`);
  - `apps/frontend`: r1, r2 e r3 **INCONCLUSIVAS** (30, 31 e 28 reprovações por timeout em formulários que a change não toca, CPU 23 o tempo todo; os arquivos que reprovaram passam isolados nos dois worktrees); **r4 APROVADA**, 2351/2351 em 185 arquivos, CPU mínima 23, rodada pelo `suite-220-4workers.sh` (cópia do `suite-220.sh` com o Vitest em 4 workers, para a suíte esquentar menos a máquina; os mesmos testes) (`220-fechamento-r4-4workers/`);
  - `rt-inbox`: **r1 APROVADA**, 14/14, CPU mínima 23; `rt-connectors`: **r1 APROVADA**, 10/10, CPU mínima 23 (`220-fechamento-r1/`). **Nessas, o RabbitMQ do compose não estava de pé** (o `podman ps` mostra só o Postgres). A pedido do dono, ele foi subido e os dois rodaram de novo: `rt-connectors` **r5 APROVADA**, 10/10, CPU mínima 100 (`220-fechamento-r5-rabbit/`); `rt-inbox` r5 inconclusiva (o `Source3` da #176, timeout com CPU 23) e **r6 APROVADA**, 14/14, CPU mínima 25 (`220-fechamento-r6-rabbit/`), com o broker `running healthy` antes e depois de cada rodada (`rabbitmq.txt`).
  - A r4 do frontend com 4 workers foi aceita pelo dono como a rodada de fechamento (08/11/2026).
- [x] 10.3 Comparação por nome contra o `220-baseline/nomes.txt`: nenhum caso sumiu, mudou de nome ou deixou de passar fora da lista do topo, cada saída com o requisito; os casos novos contados por arquivo.
  **Feito**, contra `220-baseline/nomes.txt` (saídas em `~/.cache/buteco-agents/220-comparacao/`):
  - **`apps/api`** (1299 → 1319): saem 16, todos da lista do topo e registrados na 1.4 e na 4.2; entram 36 (`TwoFactorRequiredLoginTests` 14, `TwoFactorEmergencyResetTests` 9, `RouteAuthenticationTests` 3, `TwoFactorEnrollmentTests` 3, `TwoFactorEnrollmentLogoutTests` 2, `TwoFactorRequiredRenewalTests` 2, `SessionLoginTests` 1, `TwoFactorLoginTests` 1, `TwoFactorResetParityTests` 1); nenhum deixa de passar;
  - **`apps/frontend`** (2329 → 2351): saem 28, todos da lista do topo (`TwoFactorCard` 19, `accountApi` 3, `sessionApi` 1, `AccessLinkPage` 2, `EntrarPage` 3), registrados na 7.x e na 8.x; entram 50 (`EntrarPage` 28, `sessionApi` 11, `TwoFactorCard` 8, `AccessLinkPage` 2, `loginFlow.integration` 1); nenhum deixa de passar;
  - **`rt-inbox` e `rt-connectors`**: os mesmos nomes da baseline, todos passando.
- [x] 10.4 (apps/api + apps/frontend) **Conferência no navegador**, com o `apps/api` local (`TZ=America/Sao_Paulo`) e o Vite: um usuário sem a verificação entra (senha, QR lido por um app autenticador real, código, códigos, painel); "Tentar de novo" com o `setup` derrubado; "Voltar" no meio (o cookie apagado e o stamp trocado no banco); o prazo vencido; o convite novo chegando à etapa; Minha conta e Editar usuário com os textos novos; nenhum aviso de CSP no console com o QR no `/entrar`, servido pelo `vite preview` com o cabeçalho do `nginx.conf` (arquivo temporário fora do repositório). O que não puder ser conferido na aba de automação fica registrado com o motivo e vai para a validação visual.
  **Feito (08/11/2026)**, com o `apps/api` local (`TZ=America/Sao_Paulo`, `dotnet run --launch-profile http`) e o Vite do worktree, no Chrome, com contas do banco de desenvolvimento:
  - **cadastro de ponta a ponta** (`operador@buteco.local`, sem a verificação): senha → "Cadastre a verificação em 2 etapas", o QR e o segredo de 32 caracteres; nada no `sessionStorage`, `localStorage` nem `history.state`; o SVG sem `<image>`, `<foreignObject>` ou `<script>`; o código errado mostra "Código inválido. Confira se o relógio do celular está certo." com `aria-invalid="true"`; o código certo mostra "Verificação ligada", os 10 códigos (nenhum no storage), "Copiar todos", "Baixar .txt" e "Continuar", sem o selo "Ativa"; "Continuar" leva ao painel (`/inventory`);
  - **"Voltar" no meio:** a tela volta a e-mail e senha, e no banco o carimbo muda (`273A7C0F` → `420E36E4`) e o pendente some;
  - **Minha conta:** o subtítulo novo, "Ativa desde 08/11/2026", "10 códigos de recuperação restantes.", "Gerar novos códigos", o texto de quem trocou de celular, sem "Remover verificação";
  - **Editar usuário:** sem a verificação (`rafael.183`), "A pessoa cadastra a verificação no próximo login."; com ela (`validacao.2fa`), o texto novo e a confirmação que termina em "No próximo login, ela cadastra o app autenticador de novo." (fechada em "Cancelar", sem redefinir); o próprio, "Para trocar de celular, peça a outro usuário que redefina a sua verificação.", sem botão;
  - **o convite novo** (`joana.183`, link renovado pela rota do admin): criar a senha leva ao `/entrar` na etapa do cadastro com "Senha salva. Falta só cadastrar o app autenticador para entrar.", o `state` já limpo; recarregar volta a e-mail e senha;
  - **a CSP:** o `dist` servido pelo `vite preview` na porta 5173 (a origem que o `POST /auth/session` aceita), com o cabeçalho do `nginx.conf`, por um script fora do repositório (`~/.claude/jobs/…/tmp/csp/preview.mjs`): a etapa do cadastro renderiza o QR, e o console não tem nenhuma mensagem de CSP.

  **O código do app** foi calculado do segredo na própria página (WebCrypto, RFC 6238), no lugar de um app autenticador num celular: a aba de automação não lê QR. A leitura do QR por um app real fica para a validação visual.

  **Não conferido no navegador, e por quê** (vão para a validação visual):
  - **"Tentar de novo" com o `setup` derrubado:** a etapa chama o `setup` sozinha ao abrir, e derrubar só ele exigiria parar o `apps/api` entre a senha e o `setup`; os casos `falha ao gerar o QR…` e `"Tentar de novo" espera o Retry-After…` cobrem;
  - **o prazo de 10 minutos vencido:** exige esperar 10 minutos de relógio; `TheDeadline_IsTheFirstSetups` e `prazo vencido volta a e-mail e senha…` cobrem;
  - **o escuro** e **"Copiar"/"Copiar todos"** (o `navigator.clipboard` não resolve na aba de automação sem foco, como na #183).

  **O que a conferência deixou no banco de desenvolvimento:** a `joana.183` passou de convidada a ativa, com uma senha de conferência em `~/.cache/buteco-agents/220-dev/conferencia-credenciais.txt` (modo 600); o `operador@buteco.local` cadastrou e foi redefinido em seguida pelo comando de emergência (`dotnet run -- two-factor-reset`, o caminho do `development.md`), e o próximo login dele pede o cadastro.
- [x] 10.5 (apps/api) O comando de emergência pela imagem real: `podman build` do `apps/api` e `podman run … two-factor-reset --email …` contra o Postgres do compose de desenvolvimento, com as saídas `0` e `1`.
  **Feito (08/11/2026):** `podman build -f apps/api/Dockerfile .` e `podman run --rm --network buteco-agents_default … buteco-api:220-emergencia two-factor-reset --email …`, contra um banco descartável (`buteco_220_cmd`) no Postgres do compose de desenvolvimento, copiado do `buteco_agents` por `pg_dump` (o `CREATE DATABASE … TEMPLATE` recusou por divergência de collation do banco de dev, que não foi tocado):
  - **saída `0`** (`validacao.2fa@buteco.local`): a linha `Warning` `[1196]` "…redefinida para o usuário 01a1ae8f… ; 1 sessões encerradas…"; no banco, carimbo `A85813D6` → `467DF61B`, verificação e segredo apagados, 10 códigos → 0, 1 autorização válida → 0; o carimbo novo não aparece na saída;
  - **saída `1`** (`ninguem-220@exemplo.test`): "nenhum usuário com o e-mail…";
  - **saída `70`** (conexão na porta 1, com uma senha de teste): "falha (System.InvalidOperationException, causa System.Net.Sockets.SocketException); nada mudou", sem `Host=`, `Port=1` nem a senha na saída.
  O banco descartável e a imagem foram apagados. Saídas em `~/.cache/buteco-agents/220-dev/10.5/`.
- [x] 10.6 Medição da convenção 18: produção, testes e duplos separados, por `git diff --numstat` contra `8471250` mais as linhas dos arquivos novos, comparados à projeção do design (~+720 / −500 e ~+1.450 / −530), depois da validação visual se ela mudar código.
  **Feito (08/11/2026)**, por `git diff --numstat 8471250` mais as linhas dos arquivos novos, só código (sem `openspec/`, `.md` e migrations):
  - **produção:** +1.136 / −685 em 35 arquivos, contra ~+720 / −500 projetados (**+58 %** nas linhas novas). ~140 linhas de cada lado são a mudança de pasta do QR, dos códigos e do `.txt` (que a projeção contou como ~+20/−20);
  - **testes:** +1.492 / −518 em 32 arquivos; **duplos:** +525 / −29 em 11 arquivos (os dois `IssuerUserFlow` +165 cada, `TwoFactorTestClient` +80, `EmergencyCommandRunner` +42). Somados, +2.017 / −547, contra ~+1.450 / −530 (**+39 %**), na faixa do erro medido na convenção 18 (+57 % na #180; +70 % nas linhas de teste);
  - **config:** +4 / −19 em 2 arquivos (o compose e o `.env.prod.example`).
  A medição se refaz se a validação visual mudar código.

## 11. Validação visual

- [ ] 11.1 **Validação visual na homologação da M1**, sem parada (convenção 14; decisão do dono, 08/11/2026). O PR é mergeado antes; o dono valida quadro a quadro depois que todas as issues da entrega estiverem em `main`, e o que a homologação achar vira trabalho dela. O checklist vai igual no `02` e no PR:
  - **`Main` (`estado`):**
    - `cadastro`;
    - `cadastro-qr-carregando` e `cadastro-qr-falhou` (com "Tentar de novo"; o campo desabilitado sem QR);
    - `cadastro-codigo-vazio` e `cadastro-codigo-invalido`;
    - `cadastro-bloqueado` (com o N do `Retry-After`), `cadastro-muitas-tentativas`, `cadastro-indisponivel` e `cadastro-falha-de-rede`;
    - `cadastro-codigos` (com "Baixar .txt" e "Continuar");
    - "Continuar" depois de 5 minutos no `cadastro-codigos` (cai no `/entrar`, e a pessoa entra com a senha e o código do app);
    - "Voltar" levando a `inicio`;
    - `cadastro-apos-nova-senha`, vindo do convite e da redefinição de senha;
    - `mfa-expirou` vindo da etapa do cadastro (os 10 minutos);
    - a falha do `setup` mostrando o `cadastro-qr-falhou`, e os avisos de `429`, `503` e rede só no `confirm` (a leitura do D7);
  - **`MinhaConta` (`verificacao`):** `ativa` (sem "Remover verificação", com o texto de quem trocou de celular), `poucos-codigos`, `regenerar`, `regenerar-codigo-invalido`, `regenerar-bloqueado`, `codigos` e `nao-configurada`; o subtítulo novo;
  - **`UsuarioDetalhe` (`entrega=m1`):** `verificacao=desligada` e `ativa`, em `outro` e em `desativado`; `acao=redefinir-2fa`; `alvo=proprio`; o `403` de `redefinir-2fa-proprio` (no layout de outro, com o reconhecimento falhando);
  - **os dois esquemas de cor**, com o QR em fundo branco no escuro.

## 12. Documentação e registros

- [x] 12.1 (docs) `01-ARQUITETURA_E_CONVENCOES.md` (se alguma convenção mudar: a obrigatoriedade e o comando no binário), `02-HISTORICO_E_STATUS.md` (a entrada da change, com as decisões, as premissas que caíram, as medições e o checklist da 11.1) e `CHANGELOG.md`, **antes do archive**.
  **Feito (08/11/2026):** `01-ARQUITETURA_E_CONVENCOES.md` (a descrição das 2 etapas: obrigatória, o cadastro no login, o comando de emergência; nenhuma convenção numerada mudou), `02-HISTORICO_E_STATUS.md` (a entrada da change, com as decisões, as premissas que caíram, as rodadas, a medição explicada por área, o banco de desenvolvimento e o checklist da 11.1 inteiro) e `CHANGELOG.md` (Added, Changed e Removed), antes do archive.
- [x] 12.2 (issues) Comentários de fechamento, com o ok do dono: na **#220**, a entrega e a validação visual na homologação; na **#194**, a M1 com 18 de 18; na **#184**, que a obrigatoriedade vale para todos e o "Opcional para Operador" volta com os papéis. O PR leva `Closes #220`, e o corpo traz o checklist da 11.1.
  **Feito em parte, por decisão do dono (08/11/2026):** o comentário da #220 vai com o PR. Os textos da #194 (o desenvolvimento da M1 terminou, 18 de 18; a issue fica aberta até a homologação) e da #184 foram escritos e entregues ao dono no relatório, sem publicar: ele publica, ou pede, depois do merge.
