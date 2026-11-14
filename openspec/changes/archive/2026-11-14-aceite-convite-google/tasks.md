Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-274.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-189.sh`, com os projetos `api` e `rt-connectors`, e com o mesmo lock das suítes. A mutação toca só o trecho nomeado e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação e reprova por "no tests" não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/274-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-274.sh` (`api`, `rt-connectors`), uma por vez, com o lock, e com o RabbitMQ de pé.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 8.
- A intermitente da #232 (`KnowledgeBaseIndexingSummaryTests.SummaryQuery_CostDoesNotGrowWithNumberOfBases`) numa rodada com a CPU baixa é inconclusiva: repete, e o comentário vai na #232.
- Um build nunca roda junto de uma suíte nem de uma conferência.

**A baseline** está em `~/.cache/buteco-agents/274/baseline/`, em `1475d35`, com a árvore limpa: `api` 1794 + 1 ignorado (CPU mínima 44), `rt-connectors` 12/12 (CPU 100), as duas `APROVADA`, e o relatório por nome em `nomes.txt` (1807 linhas). Detalhes no design ("Baseline").

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **parar** se a medida da 4.9 (o provedor que saiu da configuração) não couber em `falha` (decisão do dono, 13/11/2026);
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Nada de commit parcial.** O trabalho fica na árvore até o dono pedir o commit.

**Sem tela.** A change não toca o `apps/frontend`; o contrato do token na aba (D5) é da #190. Se passar a precisar de tela, é parada.

**O login pelo Google, o conectar, o authorize, a troca do código e a renovação não mudam.** Nenhuma tarefa toca o `LoginAsync`, o `ConnectAsync`, o `AuthorizeEndpoint`, o `UserGrantTokenHandler` nem as rotas `/auth/session*`, além de o retorno despachar a intenção nova e gravar o cookie pendente pelo `IssuerSession.SignInAsync` que já existe. O aceite por senha muda só na origem da condição de consumo (o mesmo `WHERE`).

**Casos que saem ou mudam de asserção**, porque o requisito muda nesta change (delta specs). Nenhum sai. Todo outro caso existente fica sem remoção, renome nem afrouxamento:
- `apps/api`, **muda a lista esperada:** `RouteAuthenticationTests.TheAuthEntryPoints_AreTheKnownOnes` (l. 107–137) ganha `/auth/invitation/federated/{chave}/start`. A asserção continua exata. Requisito: `route-authentication`, "Endpoints do emissor OIDC são anônimos, classificados e conferidos no boot" e "Rotas anônimas do login federado";
- `apps/api`, **muda a lista, asserção igual:** a allowlist de rotas anônimas do `Program.cs` (l. 277–290) ganha a mesma rota. Requisito: o mesmo;
- `apps/api`, **sem mudança, conferido:** `InvitationAcceptTests.TwoConcurrentAccepts_OneWins_AndTheConsumptionIsOneConditionalUpdate` (l. 131–169) continua procurando no `WHERE` do `UPDATE` com `password_hash` os mesmos quatro fragmentos; a condição dividida (2.1) os mantém. Se algum fragmento mudar de forma, é parada (afrouxar a asserção reduz cobertura).

Cada um é listado por nome na comparação da 9.3, com o requisito que o mudou. Um caso fora desta lista que precise sair ou afrouxar é parada.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-274.sh`, cópia do `guarda-189.sh` sem o `rt-inbox`, com o lock das suítes. Conferir com uma mutação nula (recusa com "defeito não aplicado") e uma real em `apps/api/src` (o arquivo volta com o mesmo sha).
  **Feito:** `guarda-274.sh` = `guarda-189.sh` sem o `rt-inbox` (diff: o cabeçalho, o uso e a linha do `case`). Nula: "defeito não aplicado"; real (comentário em `Identity/IdentityTokenLifetimes.cs`): `AdvisoryLockKeysTests` 6/6, voltou com o sha `1c893d96…`. Mais o `teste-274.sh` (rodada filtrada com o mesmo lock).
- [x] 0.2 Reler no fonte do OpenIddict 7.7.2 (`~/.cache/buteco-agents/182-fonte/openiddict-core`) e registrar no design qualquer diferença (convenção 6):
  - o que o `AuthenticateAsync` do retorno entrega quando a registração do state token não existe mais (as propriedades vêm? o erro é qual?), para a 4.9;
  - como decifrar, num teste, o payload do state token gravado em `openiddict_tokens` com as credenciais de cifra do cliente, para a 1.4;
  - se o WebIntegration do Google lê o discovery ou fixa os endpoints, para a conferência local (10.1).
  **Feito, com uma diferença que virou parada (4.9):** (a) a registração ausente **não** é rejeição: o `ResolveClientRegistrationFromStateToken` chama o `GetClientRegistrationByIdAsync`, que **lança** (`OpenIddictClientService.cs` l. 153–155); (b) as propriedades do Buteco ficam no claim `oi_hst_props` do state token, e o payload (JWE) decifra com o `JsonWebTokenHandler.DecryptToken` e as `EncryptionCredentials` do cliente (`StateTokenReader`, apoio de teste); (c) o WebIntegration do Google fixa só o issuer (`OpenIddictClientWebIntegrationProviders.xml` l. 1114–1126) e lê o discovery: o duplo local serve os endpoints que quiser sob `accounts.google.com`. Para a 10.1: o .NET no macOS valida a cadeia pelo Security framework e não lê `SSL_CERT_FILE`; no Linux, lê. O `apps/api` da conferência roda no contêiner da imagem, com a CA só no `SSL_CERT_FILE` do processo (aceito pelo dono, 13/11/2026).

## 1. O início do aceite (apps/api; `federated-login`, `user-access-links`, `route-authentication`, D1)

- [x] 1.1 `POST /auth/invitation/federated/{chave}/start` (`FederatedInvitationEndpoints`, nome de trabalho, ao lado do `FederatedLoginEndpoints`): as barreiras do login, o limite global (`LoginRateLimiting.Marker`), `AuthEntryPoint`; a chave (`NotConfigured`); o convite pelo `FindAsync` do aceite por senha, que passa a ser compartilhado (sem mudar a consulta); o `FederatedChallenge.BeginAsync` com `buteco_intencao` = `convite`, `buteco_usuario` e `buteco_link` (o SHA-256 em base64url). A constante `FederatedLogin.Intents.Invitation` e a `FederatedLogin.Properties.LinkHash`.
  **Feito:** `Identity/Federated/FederatedInvitationEndpoints.cs` (parcial do `FederatedLoginEndpoints`, para usar o `ProviderIdentity` e os apoios do retorno), `FederatedLogin.InvitationStartPath`, `Intents.Invitation`, `Properties.LinkHash`, `Refusals.OtherEmail` e `InvalidInvitation`, `InvitationPagePath`. O `FindAsync`, o `ValidLink` e o `InvalidLink` do `AccessLinkPublicEndpoints` passaram de `private` a `internal`, sem mudar a consulta.
- [x] 1.2 A rota na allowlist do `Program.cs` e na lista do `TheAuthEntryPoints_AreTheKnownOnes` (lista do topo).
  **Feito.** As duas listas ganharam `/auth/invitation/federated/{chave}/start`; o `TheAuthEntryPoints_AreTheKnownOnes` aprovado com a asserção exata.
- [x] 1.3 Casos (`FederatedInvitationStartTests`): o `200` com o `url` e o cookie do fluxo, sem o token nem o e-mail no `url`; o convite inválido nos quatro casos (vencido, cancelado, aceito, link de redefinição) com o corpo igual ao da consulta e sem cookie, e o `FakeGoogle` sem pedido; a chave desconhecida; a `Origin` de outro site; o provedor fora do alcance (`503`, a regra da #189); o acesso sem `Authorization`.
  **Feito:** `FederatedInvitationStartTests` (9 casos: o `200`, o state, a `Theory` dos quatro inválidos, a resposta única, a chave, a origem) e `FederatedProviderUnreachableTests.TheInvitationStart_WithTheProviderDown_IsA503ProblemJson_WithoutCookie_AndTheInvitationUnchanged`; todos aprovados.
- [x] 1.4 **(guarda)** "O state token leva o hash, e não o link": o payload do state token em `openiddict_tokens`, decifrado pelo apoio da 0.2, traz a intenção, o id e o hash, e não contém o token. **Defeito:** o `buteco_link` com o token, e o retorno calculando o hash dele (o fluxo continua funcionando; só o guarda pega).
  **Guarda feito:** `FederatedInvitationStartTests.TheStateToken_CarriesTheHash_NotTheLink` reprovou ("Assert.Contains() Failure: Sub-string not found": o hash não está no state); no mesmo filtro, `Accepting_OpensThePendingSession…` **aprovou** com o defeito, o que confirma que só o guarda o pega; voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 1.5 **(guarda)** "Resposta única também no início": o início e a consulta com o mesmo token vencido têm o mesmo status e o mesmo corpo. **Defeito:** o início respondendo `410` com "Convite vencido." para o vencido.
  **Guarda feito:** `Start_AndLookup_WithTheSameExpiredToken_AnswerTheSame` reprovou ("Expected: NotFound / Actual: Gone"); voltou `d52e136f…`.

## 2. A condição de consumo dividida (apps/api; `user-access-links`, D2, D4)

- [x] 2.1 `InvitationConsumption` (nome de trabalho): o `WHERE` do consumo (o id, o hash, o propósito, o vencimento, o estado convidado) num método só, usado pelo aceite por senha (sem mudança de efeito, fora de transação) e pelo do Google (mais o e-mail normalizado, Q5).
  **Feito:** `Users/AccessLinks/InvitationConsumption.cs` (`Pending`), usado pelo aceite por senha (o mesmo `ExecuteUpdateAsync`, fora de transação) e pelo do provedor (mais o e-mail normalizado).
- [x] 2.2 O `TwoConcurrentAccepts_OneWins_AndTheConsumptionIsOneConditionalUpdate` aprovado sem mudança (lista do topo); a suíte do aceite por senha (`InvitationAcceptTests`, `AccessLinkPublicRoutesTests`) aprovada antes de seguir.
  **Feito:** `Users.AccessLinks` inteiro mais o `FederatedProviderUnreachableTests`, 88/88, com o `TwoConcurrentAccepts_OneWins_AndTheConsumptionIsOneConditionalUpdate` sem mudança (os quatro fragmentos do `WHERE` continuam lá).
- [x] 2.3 Um apoio de teste para parar o retorno entre a leitura (o 2 do D2) e o consumo (o 6), no molde da barreira dos "Dois aceites concorrentes" da #181: um serviço registrado só no host de teste, sem nada no caminho de produção além de um ponto de chamada que não faz nada por padrão. Contado como duplo.
  **Feito:** `Identity/Federated/FederatedInvitationPause.cs` (singleton, `BeforeConsumeAsync` que não faz nada) e, nos testes, o `FederatedInvitationFixture.ControllablePause` (arma uma ação uma vez, para um convidado).

## 3. O retorno com a intenção `convite` (apps/api; `federated-login`, `issuer-user-login`, D2)

- [x] 3.1 O `CallbackAsync` despacha a intenção `convite` para um `InvitationAsync`, sem tocar o `LoginAsync` nem o `ConnectAsync`. A ordem do D2: o domínio, o convite (com o hash do state token), o verificado, o e-mail, o par, o consumo e o vínculo na transação (zero linhas e `23505` com rollback), o bloqueio zerado, o log `1203`, a sessão pendente com o usuário relido, `302 /entrar?federado=<chave>&segundoPasso=cadastro&convite=aceito`.
  **Feito:** o `CallbackAsync` despacha por `switch` (login, conectar, convite) e a `falha` volta pela intenção (`FailureRefusal`); o `LoginAsync` e o `ConnectAsync` não mudaram.
- [x] 3.2 O `GoogleFlow` ganha o início do aceite (`StartInvitationAsync`) e o retorno com a intenção, sobre o `FakeGoogle` que existe. Contado como duplo (extensão).
  **Feito:** `GoogleFlow.StartInvitationAsync` e `AcceptInvitationAsync`; `InvitationSeed.InvitedAsync` ganhou o papel (opcional, padrão `Admin` como antes).
- [x] 3.3 Casos (`FederatedInvitationAcceptTests`): o aceite (o `302`, o cookie pendente, o usuário ativo sem senha com o papel do convite, o vínculo, o link inválido depois, `formasDeEntrada` `["google"]`); o cookie do aceite não emite código no authorize; o passo é o `cadastro` e o `second-factor` responde `401`; o aceite zera o bloqueio (o `setup` responde `200`, e não `423`); o login por senha do aceito responde `401` genérico; o aceite por senha depois do aceite pelo Google é o `404` único.
  **Feito:** `FederatedInvitationAcceptTests`, os quatro casos do aceite, aprovados.
- [x] 3.4 De ponta a ponta (`FederatedInvitationEndToEndTests`), sem semeador além do Admin: o Admin convida um Operador; o início, o `FakeGoogle`, o retorno; o `setup` e o `confirm` com o cookie do retorno; o authorize, o `/connect/token`; o access token com o `iss`, o `aud`, o `client_id`, o `sub` e o `role` `operator`.
  **Feito:** `FederatedInvitationEndToEndTests.InviteByTheRoute_AcceptByTheProvider_EnrollAndGetTheButecoToken`, aprovado (`role` `operator`, `client_id` `buteco-painel`, o `sub` do convidado).

## 4. As recusas (apps/api; `federated-login`, D3)

- [x] 4.1 As seis recusas para `/convite?recusa=<motivo>&provedor=<chave>`, o `buteco-recusa` só no `outro-email` (com o e-mail do provedor), e a `falha` pela intenção quando ela se lê; senão, o `/entrar?recusa=falha` de hoje.
  **Feito** (`InvitationRefusal`, `RefuseInvitation`, os logs 1202 e 1203).
- [x] 4.2 Casos: cada motivo, com o usuário ainda convidado e sem vínculo (menos `convite-invalido`, em que o convite já não valia); o `buteco-recusa` do `outro-email` lido de `/convite` (o e-mail do provedor, e não o do convite) e o `404` na segunda leitura; o convite que deixou de valer no meio, nos quatro casos (vencido, cancelado, aceito por senha, link novo, com o novo ainda aceito); o domínio; o `access_denied`; a URL da recusa sem o token, o hash e os e-mails.
  **Feito:** os casos das recusas em `FederatedInvitationAcceptTests` (com a `Theory` dos quatro mortos no meio) e o `FederatedInvitationHostedDomainTests`; 18/18 com os do aceite.
- [x] 4.3 **(guarda)** "O convite morto vem antes do e-mail": o convite cancelado no meio com outro e-mail responde `convite-invalido`, e não `outro-email`. **Defeito:** o e-mail (o 4) conferido antes do convite (o 2).
  **Guarda feito:** `ADeadInvitation_ComesBeforeTheEmail` reprovou ("Expected: \"convite-invalido\" / Actual: \"outro-email\""); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 4.4 **(guarda)** "E-mail não verificado não aceita": o e-mail do convite com `email_verified` falso responde `email-nao-verificado`, e o usuário continua convidado. **Defeito:** a conferência do verificado (o 3) retirada.
  **Guarda feito:** `AnUnverifiedEmail_IsRefused_AndTheInvitationStaysPending` reprovou ("Assert.StartsWith() Failure: String start does not match / String: \"/entrar?federado=corporativo&segundoPasso\"··· / Expected start: \"/convite?\"": com o defeito, o convite foi aceito); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 4.5 **(guarda)** "Conta do provedor de outro usuário": o par já vinculado a outro usuário responde `em-uso`, e nenhum vínculo é gravado nem muda. **Defeito:** a conferência do par (o 5) retirada e o vínculo gravado por upsert pelo par (o vínculo do outro passa ao convidado).
  **Guarda feito:** `AProviderAccountOfAnotherUser_IsInUse_AndNoLinkChanges` reprovou (a mesma mensagem da 4.4: o par do outro passou ao convidado, e o convite foi aceito); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 4.6 **(guarda)** "Nenhuma recusa do aceite grava sessão" (os seis motivos, e o authorize seguinte sem código). **Defeito:** o `SignInAsync` antes da decisão.
  **Guarda feito:** `NoRefusal_OfTheAccept_WritesASession` reprovou ("Assert.Null() Failure: Value is not null / Actual: \"buteco-sessao=CfDJ8…\""); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 4.7 **(guarda)** "O log do aceite não leva e-mail nem sub" (o aceite e as seis recusas, com o log capturado; cada desfecho com o `EventId` 1202, 1203 ou 1199). **Defeito:** (a) o e-mail do provedor no template do 1202; (b) o `sub` no template do 1203.
  **Guarda feito, as duas mutações:** (a) o e-mail do provedor na linha do 1202 e (b) o `sub` na linha do 1203: nas duas, `TheAcceptLog_CarriesNeitherEmailNorSubject` reprovou ("Assert.DoesNotContain() Failure: Filter matched in collection"); voltou `d52e136f…` nas duas.
- [x] 4.8 **(guarda)** "A recusa volta ao convite": `outro-email` com o `Location` em `/convite`. **Defeito:** o `LoginRefusal` (o `/entrar`) no lugar do da recusa do aceite.
  **Guarda feito:** `TheRefusal_GoesBackToTheInvitation_NotToTheLogin` reprovou ("Expected: \"/convite?recusa=outro-email&provedor=corp\"··· / Actual: \"/entrar?recusa=outro-email&provedor=corpo\"···"); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 4.9 **(medida)** "O provedor saiu da configuração no meio": o início com o provedor `corporativo`, um host novo sem ele (o mesmo banco e o mesmo anel), o retorno. Medir o que o `AuthenticateAsync` entrega e para onde a resposta vai, registrar no design (D3) e afirmar a medida no caso. **Se a medida não couber em `falha`** (exceção, `500`, página do OpenIddict, sessão), **parar** e devolver ao dono o que veio.
  **Medido (13/11/2026), PARADA, decidida pelo dono:** a chave trocada (a registração do state tirada das opções do cliente, outra presente) dava **`500`** (`System.InvalidOperationException: The client registration corresponding to the specified identifier cannot be found in the client options.`, do `ResolveClientRegistrationFromStateToken`, dentro do middleware de autenticação), no aceite e no login da #189; nenhum provedor configurado dava `302 /entrar?recusa=falha`. **Decisão do dono (13/11/2026):** corrigir aqui, para as três intenções (D9), com a issue [#276](https://github.com/aldovrando-oliveira/buteco-agentes/issues/276) para o defeito da #189. **Medida depois da correção: as propriedades do state chegam à rota depois da rejeição**: login `/entrar?recusa=falha&provedor=corporativo`, conectar `/account?recusa=falha&provedor=corporativo`, aceite `/convite?recusa=falha&provedor=corporativo` (o convite pendente); nenhum com `buteco-sessao`; o log `1199` com `invalid_request`. A spec `federated-login` ganhou o MODIFIED de "Decisão do login no retorno" com os seis cenários (as duas formas, para as três intenções). Os casos: `FederatedRemovedProviderTests.cs` (`FederatedRenamedProviderLoginTests`, `…ConnectTests`, `…InvitationTests`, `FederatedNoProviderTests`), 4/4; os andaimes da medida saíram.
- [x] 4.10 `FederatedMissingRegistrationHandler` (D9): antes do `ResolveClientRegistrationFromStateToken`, com os mesmos filtros, a registração do state que não está nas opções vira `context.Reject(invalid_request)`; registrado no cliente ao lado do `FederatedRequestUriHandler`.
  **(guarda)** "A chave trocada no meio é `falha`, e não `500`", nas três intenções. **Defeito:** o handler retirado do registro.
  **Guarda feito:** `FederatedRenamedProviderLoginTests.TheLogin_WithTheKeyRenamedInTheMiddle_IsFailure_WithoutSession`, `FederatedRenamedProviderConnectTests.TheConnect_WithTheKeyRenamedInTheMiddle_IsFailure_WithoutLink` e `FederatedRenamedProviderInvitationTests.TheAccept_WithTheKeyRenamedInTheMiddle_IsFailure_BackToTheInvitation_StillPending` reprovaram os três ("Assert.Equal() Failure: Values differ / Expected: Found / Actual: InternalServerError"); voltou `apps/api/src/Buteco.Api/Identity/IdentityIssuerServiceCollectionExtensions.cs` (sha igual ao de antes: `5b4d6467f722a1809c78e45418eaf49e9053a3b4`).

## 5. A concorrência (apps/api; `federated-login`, D4)

- [x] 5.1 Casos (`FederatedInvitationConcurrencyTests`), com a barreira da 2.3: senha × Google, com a senha vencendo e com o Google vencendo (cada um determinístico pela barreira, mais a rodada solta de 20 como a da #181); Google × Google, um vínculo só; o vínculo que falha desfaz o consumo (outro usuário ganha o par durante a barreira: `em-uso`, o convidado continua convidado, com o mesmo link); o e-mail editado no meio (`convite-invalido`, sem vínculo); o link novo gerado no meio (`convite-invalido`, o novo aceito).
  **Feito:** `FederatedInvitationConcurrencyTests`, 7/7: a senha vence (pela pausa), o Google vence, a rodada solta de 20, Google × Google (o retorno B inteiro dentro da pausa do A), o vínculo que falha desfaz o consumo, o e-mail editado no meio, o link novo no meio.
- [x] 5.2 **(guarda)** "O vínculo que falha desfaz o consumo". **Defeito:** o `UPDATE` do Google fora da transação (o convidado fica ativo, sem senha e sem vínculo).
  **Guarda feito:** `TheLinkThatFails_UndoesTheConsumption` reprovou ("Expected: Invited / Actual: Active": o convidado ativo, sem senha e sem vínculo); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 5.3 **(guarda)** "Senha e provedor ao mesmo tempo, a senha vence: nenhum vínculo". **Defeito:** o `INSERT` do vínculo antes do `UPDATE`, sem desfazer quando o `UPDATE` volta zero linhas (o vínculo órfão).
  **Guarda feito:** `PasswordAndProvider_ThePasswordWins_NoLink` reprovou ("Assert.Empty() Failure: Collection was not empty": o vínculo órfão); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 5.4 **(guarda)** "O link novo gerado no meio não é consumido pelo antigo". **Defeito:** a condição do Google sem o hash.
  **Guarda feito:** `ANewLinkInTheMiddle_IsNotConsumedByTheOldOne` reprovou ("Expected: \"/convite?recusa=convite-invalido&provedor\"··· / Actual: \"/entrar?federado=corporativo&segundoPasso\"···"); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).
- [x] 5.5 **(guarda)** "O e-mail editado no meio não é aceito". **Defeito:** o `UPDATE` do Google sem a condição do e-mail normalizado.
  **Guarda feito:** `TheEmailEditedInTheMiddle_IsNotAccepted` reprovou (a mesma mensagem da 5.4: o aceite com o e-mail antigo passou); voltou `Identity/Federated/FederatedInvitationEndpoints.cs` (`d52e136f…`).

## 6. Acordos

- [x] 6.1 (`tests/ApiConnectorsRoundTrip.Tests`) Sem mudança de código: o `RoleRoundTripTests` aceita o convite por senha pelo `IssuerUserFlow.InviteAndAcceptAsync`, agora pela condição dividida. Roda no fechamento.
  **Feito** no fechamento (9.2): `rt-connectors` 12/12, sem mudança de código.

## 7. Docs

- [x] 7.1 `docs/configuration.md`, na seção dos provedores federados: o aceite de convite pelo provedor (o e-mail verificado igual ao do convite, a pessoa sem senha, o cadastro das 2 etapas logo depois); sem senha, a primeira vem do link de redefinição que um Admin gera.
  **Feito:** o parágrafo do aceite na seção dos provedores federados.
- [x] 7.2 `docs/deployment.md`, na seção "Comando de emergência da senha: `password-reset-link` (#257)": o comando só serve Admin ativo; um Operador ou um Leitor que só tem Google e perde o acesso a ele ganha senha pelo link que um Admin gera no painel (`POST /users/{id}/password-reset`). Na seção dos provedores federados (#189), o "Conferir" do D7.
  **Feito:** na seção do comando, o item "quem só tem Google"; na dos provedores, "Aceite de convite pelo Google (#274)", com o #276, o conferir e a volta atrás.
- [x] 7.3 `01-ARQUITETURA_E_CONVENCOES.md`, seção "Autenticação": o aceite pelo provedor, a ordem, a intenção `convite` no state token com o hash, a condição de consumo dividida, os `EventId` 1202 e 1203, a rota nova.
  **Feito:** o item da registração ausente (#276) e o bloco "Aceite de convite pelo Google".
- [x] 7.4 No archive: o `Purpose` da spec viva `federated-login` perde "o aceite de convite pelo provedor é da #274" e passa a citar o aceite (o archive não reescreve o `Purpose`).
  **Feito** no archive: o `Purpose` da spec viva `federated-login` cita o aceite de convite pelo provedor (#274), a registração ausente (#276) e o log da biblioteca (#277).
- [x] 7.5 Antes do archive: `02-HISTORICO_E_STATUS.md` (a entrada da change e a fila da M3 com a #190) e `CHANGELOG.md`, com a medição da convenção 18 contra a projeção do design, em colunas separadas para produção, testes, duplos e casos.
  **Feito:** `02-HISTORICO_E_STATUS.md` ("`aceite-convite-google`", com a medição e o checklist da homologação; a fila da M3 com "fecha #276 e #277") e `CHANGELOG.md` (Added: o aceite; Fixed: a #276 e a #277).

## 11. O log da biblioteca OIDC (apps/api; `federated-login`, `issuer-user-login`, D11; issue #277)

- [x] 11.1 A issue de bug [#277](https://github.com/aldovrando-oliveira/buteco-agentes/issues/277) (bug, tipo: bug, app: api, M3), no quadro em In progress.
- [x] 11.2 O `IdentityIssuerLogCapture` com as categorias `Buteco.Api.*` e `OpenIddict.*` (servidor e cliente), guardando a categoria, sem filtro de nível próprio. Nenhum caso existente mudou: todos filtram por `EventId`.
- [x] 11.3 **(medida)** Os fluxos completos com a captura em `Trace` (fixture temporário, apagado depois): o login por senha com o código, o cadastro das 2 etapas, a renovação, o logout, o login pelo Google, o aceite pelo Google, o conectar e o `client_credentials` de serviço e de aplicação. **Medido** (a tabela está no D11): `OpenIddictClientDispatcher[6195]` em **Information** com o `sub` e o e-mail do Google; `OpenIddictServerDispatcher[6075]` em **Information** com o `code_verifier`; em **Trace**, a 6001 e a 6013 (e-mail, nome e os tokens) e a 6015 (o código do authorize). **Nada em Warning ou acima**: o filtro de nível basta, e cobre o servidor e o cliente.
- [x] 11.4 `"OpenIddict": "Warning"` no `Logging:LogLevel` do `appsettings.json` (todos os ambientes), com o comentário; `LogPrivacyTests` (5 casos, um por fluxo, com o host real).
  **(guarda)** "Nenhuma linha de nenhuma categoria leva dado pessoal nem segredo". **Defeito:** o filtro `OpenIddict` retirado do `appsettings.json`.
  **Guarda feito:** reprovaram `FederatedLoginTests.NoLogLine_CarriesTheEmailOrTheSub` (o da #189), `FederatedInvitationAcceptTests.TheAcceptLog_CarriesNeitherEmailNorSubject` (o 4.7), `LogPrivacyTests.PasswordLogin_Enrollment_Code_Renewal_AndLogout_LogNoPersonalDataNorSecret` (a 6075), `…GoogleLogin_…`, `…GoogleInvitationAccept_…` e `…Connect_…` (a 6195), com "linhas de log com dado pessoal ou segredo: OpenIddict.Client.OpenIddictClientDispatcher[6195] Information: sub do Google…"; o `ServiceAndApplicationClientCredentials_LogNoSecret` aprovou (nada em Information); voltou `apps/api/src/Buteco.Api/appsettings.json` (sha igual ao de antes: `a160a69d058ec2b5b31915598beb05912a99d990`).
- [x] 11.5 Docs: `docs/configuration.md` e `docs/deployment.md` avisam que baixar o OpenIddict para `Information` expõe dado pessoal.

## 8. Lista dos guardas (conferência)

| tarefa | caso | defeito |
|---|---|---|
| 1.4 | o state token leva o hash, e não o link | o `buteco_link` com o token (e o retorno calculando o hash) |
| 1.5 | resposta única também no início | o início com `410` próprio para o vencido |
| 4.3 | o convite morto vem antes do e-mail | o e-mail conferido antes do convite |
| 4.4 | e-mail não verificado não aceita | a conferência do verificado retirada |
| 4.5 | conta do provedor de outro usuário | a conferência do par retirada e o vínculo por upsert pelo par |
| 4.6 | nenhuma recusa do aceite grava sessão | o `SignInAsync` antes da decisão |
| 4.7 | o log do aceite não leva e-mail nem sub | (a) o e-mail no 1202; (b) o `sub` no 1203 |
| 4.8 | a recusa volta ao convite | a recusa do login (o `/entrar`) no lugar |
| 4.10 | a chave trocada no meio é `falha`, e não `500` (login, conectar, aceite) | o `FederatedMissingRegistrationHandler` retirado do registro |
| 11.4 | nenhuma linha de nenhuma categoria leva dado pessoal nem segredo (o da #189, o 4.7 e os quatro fluxos de `LogPrivacyTests`) | o filtro `OpenIddict` retirado do `appsettings.json` |
| 5.2 | o vínculo que falha desfaz o consumo | o `UPDATE` fora da transação |
| 5.3 | a senha vence: nenhum vínculo | o `INSERT` antes do `UPDATE`, sem desfazer |
| 5.4 | o link novo no meio não é consumido | a condição sem o hash |
| 5.5 | o e-mail editado no meio não é aceito | o `UPDATE` sem o e-mail normalizado |

São 14 guardas e 15 mutações. Os pedidos na revisão (13/11/2026): a ordem (4.3), os quatro da concorrência (5.2 a 5.5), o e-mail não verificado (4.4), o par de outro usuário (4.5), a sessão numa recusa (4.6), o e-mail ou o `sub` no log (4.7) e o link no state token (1.4). O 1.5 e o 4.8 são desta change; o 4.10 veio da parada da 4.9, e o 11.4 da parada da 10.1 (decisões do dono, 13/11/2026).

## 9. Fechamento

- [x] 9.1 `dotnet build` sem aviso novo (comparado à baseline); `openspec validate aceite-convite-google --strict`.
  **Feito:** 15 avisos distintos na baseline e na change (`apps/api/Api.sln` e `tests/ApiConnectorsRoundTrip.Tests`, `--no-incremental`), nenhum novo; os dois `xUnit2012` que os casos novos davam foram corrigidos antes. `openspec validate --strict`: válido.
- [x] 9.2 Suítes da change pelo `suite-274.sh`, uma por vez, em `~/.cache/buteco-agents/274/fechamento/`, sem contêiner do Testcontainers antes de cada suíte e com o `rabbitmq_1` no `podman ps` do início de cada uma.
  **Feito** (sem contêiner do Testcontainers antes, `postgres_1` e `rabbitmq_1` no `podman ps` do início): `api` **APROVADA**, 1834 + 1 ignorado, 443 s, CPU mínima 48 (load 6,06 → 8,52); `rt-connectors` **APROVADA**, 12/12, 40 s, CPU 100. Primeira rodada das duas; a #232 não apareceu. **Depois da #277** (`274/fechamento-log/`): `api` **APROVADA**, 1839 + 1 ignorado, 394 s, CPU mínima 60; `rt-connectors` **APROVADA**, 12/12, CPU 100; contra a rodada anterior, nenhum caso saiu nem mudou, e entraram os 5 de `LogPrivacyTests`. Avisos: 15 distintos, os mesmos da baseline.
- [x] 9.3 Comparação por nome contra a baseline (`nomes-trx.py`, GUID normalizado, multiconjunto). Nenhum caso some nem muda fora da lista do topo, e cada caso novo fica ligado ao requisito.
  **Feito:** `274/fechamento/nomes.txt` (1847): nenhum caso da baseline saiu nem mudou de resultado; os da lista do topo (`TheAuthEntryPoints_AreTheKnownOnes`, `TwoConcurrentAccepts_OneWins_AndTheConsumptionIsOneConditionalUpdate`) com o mesmo nome e aprovados. Entraram 40, todos aprovados: `FederatedInvitationAcceptTests` 17 (`federated-login`, decisão e recusas do aceite; `issuer-user-login`; `local-users`), `FederatedInvitationStartTests` 9 (início; `user-access-links`, resposta única), `FederatedInvitationConcurrencyTests` 7 (a disputa), `FederatedInvitationEndToEndTests` 1, `FederatedInvitationHostedDomainTests` 1, `FederatedProviderUnreachableTests` 1 (o `503`), `FederatedRenamedProvider{Login,Connect,Invitation}Tests` 3 e `FederatedNoProviderTests` 1 ("Decisão do login no retorno", #276).
- [x] 9.4 O design corrigido com toda divergência achada no apply (convenção 9), numa seção "O que o apply mudou".
  **Feito:** D9 (a registração ausente) e D10 ("O que o apply mudou", APPLY-1 a APPLY-7).

## 10. Conferências

**Com o duplo, nos testes (feitas no apply):** o início, o aceite de ponta a ponta até o token, as seis recusas, a concorrência, o state token com o hash, os logs. O duplo não prova nada sobre o navegador nem sobre o Google de verdade.

- [x] 10.1 **Local, com o duplo do Google e o `apps/api` num banco descartável, apagado no fim** (`~/.cache/buteco-agents/bin/conferencia-274.sh`, com o lock das suítes; saídas em `~/.cache/buteco-agents/274/conferencia/`):
  1. o banco `buteco_agents_conf274` no `postgres_1` do compose, criado a partir do `template0` e migrado pelo worktree; nenhum outro banco tocado;
  2. o duplo do Google fora do processo: um servidor HTTPS local (`~/.cache/buteco-agents/bin/fake-google-274.py`, com uma CA própria só da conferência) que serve o discovery com o issuer `https://accounts.google.com/`, o JWKS, a autorização (devolve o código ao retorno com a conta escolhida pelo roteiro), o token (confere o `code_verifier` e assina o id token com o `nonce`) e o userinfo. O `apps/api` chega a ele pelo nome `accounts.google.com` e confia na CA da conferência só nesse processo. O caminho exato (contêiner da imagem do `api` com a CA montada, ou o processo local) sai da 0.2;
  3. o roteiro por `curl`, com a `Origin` do painel e um pote de cookies por navegador simulado: o Admin convida (`POST /users/invitations`); o início do aceite; a autorização no duplo; o retorno; o `setup` e o `confirm` com o TOTP calculado do segredo; o authorize e o `/connect/token`. Conferir: o `302` com `convite=aceito`, o `buteco-sessao` pendente, o usuário ativo sem senha, o vínculo, o token com o `role` do convite;
  4. as recusas: outro e-mail (`/convite?recusa=outro-email`, o `POST /auth/federated/refusal` com o e-mail do duplo, a segunda leitura `404`), e-mail não verificado, o convite cancelado entre o início e o retorno, `error=access_denied`; em cada uma, sem `buteco-sessao`, e o convite pendente (menos o cancelado);
  5. o log do `apps/api` sem o e-mail, o `sub` e o token (procurados pelo texto na saída capturada);
  6. no fim: o banco apagado (`DROP DATABASE`), o processo e o duplo parados, a CA da conferência apagada; conferido por `\l` e `podman ps`. Resultado no `02`.
  **Feito (13/11/2026), com um achado que é parada** (`~/.cache/buteco-agents/bin/conferencia-274.sh` e `conferencia-274.py`, `fake-google-274.py`; saídas em `~/.cache/buteco-agents/274/conferencia/`). O caminho: o `apps/api` na **imagem do worktree** (`podman build` do `apps/api/Dockerfile`), num pod com o duplo (`python:3.13-slim`), `accounts.google.com` → `127.0.0.1` dentro do pod; a CA da conferência só no `SSL_CERT_FILE` do processo do `api` e no `--cacert` do curl do roteiro. O banco `buteco_agents_conf274`, do `template0`, migrado pelo `dotnet ef` do worktree (27 migrations); o Admin do bootstrap, com a senha pelo `password-reset-link` da mesma imagem. A primeira rodada parou no Admin (o roteiro consultou o banco antes de o bootstrap, que roda depois do start, criá-lo); a espera limitada entrou no script, e a segunda rodada foi inteira.
  - **o fluxo: 29/29 conferências OK:** o aceite (`/entrar?federado=corporativo&segundoPasso=cadastro&convite=aceito`, o pendente, ativo com `["google"]` e sem senha, o link morto, o cadastro com o cookie do retorno, o token com o `sub` do convidado e `role` `operator`); outro e-mail (`/convite?recusa=outro-email`, o `buteco-recusa` com o e-mail do duplo, a segunda leitura `404` vazio, o convite pendente); não verificado; o convite cancelado no meio (`convite-invalido`); `access_denied` (`/convite?recusa=falha`); o início com link cancelado e com chave desconhecida;
  - **o log do `api`: REPROVOU**, 8 dos 17 valores procurados encontrados (os e-mails e os `sub` das quatro contas que chegaram ao userinfo). **Nenhum vem das linhas do Buteco** (1198 a 1203): todos vêm de `OpenIddict.Client.OpenIddictClientDispatcher[6195]`, em `Information`, "The userinfo response returned by https://accounts.google.com/userinfo was successfully extracted: { \"sub\": …, \"email\": … }". O `appsettings.json` tem `Default` `Information` sem filtro para o OpenIddict, e o `appsettings.Production.json` não muda isso: **o login pelo Google da #189 já grava o `sub` e o e-mail no log de produção**, e o aceite também. Os guardas de log (o da #189 e o 4.7) não o viam: o `IdentityIssuerLogCapture` só captura `Buteco.Api.Identity.*` e `Buteco.Api.Users.*` (conferido por um caso temporário, apagado: a captura recebeu só a linha 1203). **Parada** (muda o que vai ao log de produção), devolvida ao dono;
  - **a limpeza** (`limpeza.txt`, também em falha, pelo `trap`): os dois contêineres, o pod e a imagem `localhost/buteco274-conf-api` apagados (contagem 0 no `podman ps -a`, `pod ps` e `images`); o banco apagado (`DROP DATABASE … WITH (FORCE)`, 0 em `pg_database`); o diretório da CA apagado. **Nenhum certificado da conferência em armazenamento nenhum**, conferido pelo nome da CA e pela impressão SHA-256 dela: Keychain `login`, `System` e `SystemRootCertificates` do macOS (`security find-certificate -a -c` e `-Z`), `/etc/ssl` e o OpenSSL do Homebrew no macOS, o armazenamento do .NET do usuário (`~/.dotnet/corefx`), e na VM do Podman o `trust list` e `/etc/pki/ca-trust` e `/etc/ssl` (`podman machine ssh`): zero em todos. As imagens base (`dotnet/sdk`, `dotnet/aspnet`, `python:3.13-slim`) já estavam no Podman antes e ficaram.

**Manual, com o cliente OAuth real: na homologação da M3, com o roteiro da #189** (13.2 a 13.8 do `tasks.md` arquivado de `2026-11-13-provedores-federados-google`; a mesma preparação, o mesmo cliente OAuth e a mesma chave `corporativo`). Nenhum segredo no repositório. Precisa de duas contas Google de teste: a **A**, com o e-mail do convite, e a **B**, com outro e-mail (as duas listadas na tela de consentimento em modo de teste).

- [ ] 10.2 **O e-mail certo.** O Admin convida o e-mail da conta A com o papel `operador`. Numa janela anônima, `POST /auth/invitation/federated/corporativo/start` com o token do link (pelo painel da #190, ou pelo `curl` com `-H 'Content-Type: application/json' -H 'Origin: https://<PUBLIC_DOMAIN>' -d '{"token":"<token>"}'`, abrindo o `url` no navegador). Escolher a conta A. Conferir: o retorno em `/entrar?federado=corporativo&segundoPasso=cadastro&convite=aceito`; o `buteco-sessao` gravado na resposta do retorno (as ferramentas do navegador, como na 13.5 da #189); o cadastro das 2 etapas; o painel aberto como Operador; em Usuários, a pessoa ativa com a forma de entrada Google e sem senha; o log do `api` com o 1203 e sem o e-mail; o login seguinte pelo Google pedindo o código do Buteco.
- [ ] 10.3 **Outro e-mail.** Um convite novo para um e-mail que não é o da conta B; o início, escolher a conta B. Conferir: o retorno em `/convite?recusa=outro-email&provedor=corporativo`; o `buteco-recusa` gravado (`Strict`, `Path=/auth/federated/refusal`, `Max-Age=120`); o `POST /auth/federated/refusal` feito de `/convite` com o e-mail da conta B, e a segunda chamada `404`; nenhum `buteco-sessao`; o convite ainda pendente em Usuários; o link aceito em seguida por senha (`POST /auth/invitation/accept`), e o login por senha pedindo o cadastro.
- [ ] 10.4 **Cancelar no Google.** Um convite novo; o início; "Cancelar" na tela do Google: o retorno em `/convite?recusa=falha&provedor=corporativo`, sem sessão, e o convite ainda pendente; o log com o 1199 e `access_denied`.
- [ ] 10.5 **O convite cancelado no meio.** Um convite novo; o início; antes de escolher a conta, o Admin cancela o convite; escolher a conta A (com o e-mail do convite): o retorno em `/convite?recusa=convite-invalido&provedor=corporativo`, sem sessão e sem vínculo.

Se a 10.2 falhar porque o navegador não grava o `buteco-sessao` na resposta do retorno, vale a parada da #189 (13.5 dela): a saída muda o contrato.
