Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-281.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-274.sh`, com o mesmo lock das suítes. A mutação toca só o trecho nomeado e **nunca** restaura um diretório ao `HEAD`;
2. o teste roda e reprova pela asserção esperada, e os vizinhos que não devem reprovar aprovam;
3. a árvore volta **pela cópia** feita antes da mutação, com o `sha256` conferido contra o de antes.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo> (<sha>)". Uma mutação que quebra a compilação, que reprova por "no tests" ou por outra asserção não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/281-guardas/<tarefa>/`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-281.sh` (cópia do `suite-279.sh`), **uma por vez, com o lock** (`suites.lock`; o guarda, a sonda e a conferência tomam o mesmo):
- suítes: `api`, `rt-connectors` (o round-trip compõe o host do `apps/api` com o emissor) e `frontend` (o `nginxHeaders.test.ts` lê o `nginx.conf`, que não muda), com o RabbitMQ de pé;
- fora: `inbox`, `connectors`, `workers`, `rt-inbox` e `rt-crossapp` (nenhum código deles muda, e o acordo deles não passa pelo cliente OIDC);
- vale a regra assimétrica: verde conta mesmo com a CPU caindo; reprovação só conta com a CPU em 100 do início ao fim, senão é inconclusiva e se repete. Reprovação por caso que esta change mudou não é inconclusiva: "reprovada pelo X (a → b), corrigida";
- antes de cada rodada, o load de 1 e de 5 min abaixo do limite do script, e a VM do Podman com o relógio e a memória conferidos. Nunca `dotnet build` nem `podman build` junto de uma suíte.

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **parar e voltar ao dono com a B** (design, D1) se qualquer premissa da tarefa 0 (P1, P2, P3) falhar, pelo critério de cada uma no design (D0);
- **parar** se a correção precisar de tela, de cookie novo, de rota nova ou de migration;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**Nada de commit** até o dono pedir. Um commit só, depois do archive.

**O que não muda:** o `LoginAsync`, o `ConnectAsync`, o `InvitationAsync`, o `FederatedChallenge`, os inícios, o painel e o `nginx.conf`. A rota do retorno perde só o desvio das l. 133–137 e ganha o erro do log para o catálogo vazio.

## Casos que saem ou mudam de asserção (a lista do topo)

Nenhum sai. Todo outro caso existente fica sem remoção, renome nem afrouxamento:
- `apps/api`, **muda de forma, asserção igual:** `NginxNavigationEndpointsTests.EveryTopLevelNavigationPath_HasItsOwnExactBlock_ToTheApi_WithoutTheDeviation` (l. 20–39) vira uma base com dois herdeiros, um sobre o `FederatedLoginFixture` (o nome e a classe de hoje continuam) e um sobre o host sem provedor. As duas asserções (a lista não vazia, l. 30, e os problemas vazios, l. 38) ficam nos dois. Requisito: `server-deployment`, "Nginx do stack roteia por prefixo" (design, D0, P3);
- `apps/api`, **muda o arranjo: o cliente OIDC existe sempre** (acrescentado no apply, decisão do dono de 18/11/2026): `IdentityIssuerEndpointsPassthroughTests.WithBothRoutesAnonymousAndClassified_Passes` mapeia também o `FederatedLogin.CallbackPath` como anônima `AuthEntryPoint`, como o `Program.cs`. O nome e a asserção ficam iguais. Requisito: `route-authentication`, "Passthrough sem rota derruba o boot", e `federated-providers` (o cliente sem provedor). O design não o previu ("O que o apply mudou");
- `apps/api`, **sem mudança, conferido:** `FederatedNoProviderTests.TheReturn_WithNoProviderConfigured_IsFailure_ToTheLogin_WithoutSession` (`FederatedRemovedProviderTests.cs` l. 121–129). Com o cliente registrado sem provedor, o state dele (`state-de-um-fluxo-anterior`) é forjado e para na ordem 3: continua `/entrar?recusa=falha`, sem `provedor`. Requisito: `federated-login`, "Decisão do login no retorno", cenário "O state forjado cai no login". Se mudar, é parada;
- `apps/api`, **sem mudança, conferidos:** os três da chave trocada (`FederatedRenamedProvider{Login,Connect,Invitation}Tests`), com o `invalid_request` no log;
- `apps/api`, **sem mudança, conferidos:** `RouteAuthenticationTests.TheAuthEntryPoints_AreTheKnownOnes` (nenhuma rota nova), `IdentityIssuerStartupValidationTests.RealComposition_Boots_WithTheRealRoutesAndEndpoints` e os cinco de `LogPrivacyTests`.

Cada um é listado por nome na comparação da 6.3, com o requisito. Um caso fora desta lista que precise sair, mudar ou afrouxar é parada.

## 0. Preparação e as premissas da A (antes de qualquer código da correção; design, D0)

- [x] 0.1 (scripts locais) `guarda-281.sh` (cópia do `guarda-274.sh`), `suite-281.sh` (cópia do `suite-279.sh`, com `api`, `rt-connectors` e `frontend`) e `teste-281.sh` (rodada filtrada com o lock). Conferir o guarda com uma mutação nula ("defeito não aplicado") e uma real num comentário de `apps/api/src` (o arquivo volta com o mesmo sha).
  **Feito:** `guarda-281.sh` (cópia do `guarda-274.sh`), `suite-281.sh` (cópia do `suite-279.sh`) e `teste-281.sh`; mais o `sonda-281.sh`/`sonda-281.py` e o `fake-google-281.py` (cópia do da #274).
- [x] 0.2 (repo) O worktree da baseline, `.claude/worktrees/conectar-falha-minha-conta-baseline`, destacado em `0bfa1cf`, criado por `git worktree add --detach` (sem mexer na árvore da change nem na `main`). `npm ci` no `apps/frontend` dos dois worktrees antes das suítes.
  **Feito:** `conectar-falha-minha-conta-baseline`, destacado em `0bfa1cf`; `npm ci` nos dois worktrees.
- [x] 0.3 (apps/api, a sonda) O patch descartável da A (o `AddClient` sem a condição das l. 159–165 e a rota sem o desvio das l. 133–137) em `~/.cache/buteco-agents/281/sonda/sonda.diff`, aplicado num worktree próprio da sonda (`.claude/worktrees/conectar-falha-minha-conta-sonda`, destacado em `0bfa1cf`); a imagem do `api` da sonda (`podman build` do `apps/api/Dockerfile`) e a do `origin/main`, sem suíte rodando. Nada da sonda entra no worktree da change.
  **Feito:** `281/sonda/sonda.diff` (3 arquivos, +3 / −11), num worktree próprio destacado em `0bfa1cf`; as duas imagens construídas pelo `sonda-281.sh`, sem suíte rodando.
- [x] 0.4 **P1. O caso real, de ponta a ponta** (design, D0, P1), no pod da 10.1 da #274 (`conferencia-281.sh`, com o lock), num banco descartável `buteco_agents_sonda281` (do `template0`): o conectar começa com o provedor; o `api` da sonda reinicia **sem** a seção, no mesmo banco e com a mesma `Identity:SigningKeyEncryptionKey`; a volta com o pote de cookies. Conferir: `302 /account?recusa=falha&provedor=corporativo`; a linha do state em `openiddict_tokens` com o status `redeemed`; a linha `1199`; nenhum `ID2004`, `ID2019` nem erro de decifração no log. **Parada** pelo critério da P1.
  **Feito, passou:** as três voltas no `api` reiniciado sem a seção deram `/account…`, `/entrar…` e `/convite…` com `&provedor=corporativo`, sem `buteco-sessao`, com o cookie do fluxo apagado e sem cookie novo; os state tokens de `valid=3` a `redeemed=3`; nenhum vínculo; o convidado `invited`; nenhum `ID2004`/`ID2019`/`ID2139`, erro de decifração nem exceção. O caso 2 com o provedor de volta (já usado, forjado, ausente) foi ao `/entrar?recusa=falha`.
- [x] 0.5 **P2. Sem provedor, nada muda para quem não usa o Google** (design, D0, P2), com as duas imagens (sonda e `origin/main`), sem a seção, nas mesmas condições:
  1. o boot com `HTTP_PROXY` e `HTTPS_PROXY` numa porta morta (como na 13.1 da #189): conclui, e o log sem `HttpRequestException`, sem linha do `System.Net.Http.HttpClient` e sem discovery do `OpenIddict.Client`;
  2. o `IdentityIssuerStartupValidation` sem reprovar;
  3. `GET /auth/federated/providers` → `200 {"provedores":[]}`;
  4. a lista dos endpoints: o `curl` de um conjunto fixo de caminhos (as rotas anônimas, o retorno com state forjado, `/auth/federated/callbackxyz`, um caminho inexistente sob `/auth/federated/`) com o status e o `Location`, nas duas imagens, e o `diff` vazio.
  **Parada** pelo critério da P2.
  **Feito, passou:** nas duas imagens, sem a seção e com o proxy morto: zero linhas do `HttpClient`, nenhum `HttpRequestException`, a checagem de boot passou, o `providers` vazio. A lista de 22 caminhos diferiu só no corpo do `/.well-known/jwks`; refeita com dois boots de cada imagem, só o `x5c`/`x5t` muda, a cada boot e também no `origin/main` (o certificado do boot); `kid`, `n`, `e`, `alg`, `use` e o discovery iguais. Não é mudança da A.
- [x] 0.6 **P3. O guarda da #285** (design, D0, P3): no worktree da sonda, com a A, a mutação da 3.6 da #285 na forma antiga (o `LocalUsersFixture` no lugar do `FederatedLoginFixture`) **aprova** — o registro de que a forma antiga deixou de pegar; a forma nova (a fonte da lista trocada por um `new OpenIddictClientOptions()`) reprova pela l. 30. Registrar as duas saídas. **Parada** se a forma nova não reprovar.
  **Feito, passou:** a forma antiga da 3.6 **aprovou** (`rc=0`): deixou de pegar com a A. A forma nova reprovou pela l. 30 ("o cliente OIDC do host não tem nenhuma URI de retorno") nos dois hosts. O arquivo voltou com o sha `66f7168a…`.
- [x] 0.7 Apagar a sonda: o worktree da sonda (conferido sem alteração além do patch), a imagem dela e o banco `buteco_agents_sonda281`; conferido por `git worktree list`, `podman images` e `\l`. O `sonda.diff` e as saídas ficam em `~/.cache/buteco-agents/281/sonda/`.
  **Feito:** o worktree da sonda conferido (só o `sonda.diff`) e removido; contêineres, pod, imagens `buteco281-*`, o banco `buteco_agents_sonda281` e a CA apagados; conferido por `git worktree list`, `podman ps -a`, `podman pod ps`, `podman images` e `pg_database`.
- [x] 0.8 A baseline no worktree da 0.2: `api`, `rt-connectors` e `frontend`, uma por vez, em `~/.cache/buteco-agents/281/baseline/`, com o relatório por nome (`nomes-trx.py`). E a lista dos endpoints do host sem provedor (a do G0.2b), impressa por um caso temporário no worktree da baseline, sem asserção, e apagado depois (conferido por `git status` limpo no worktree da baseline).
  **Feito:** `281/baseline/` (`0bfa1cf`, árvore limpa): `api` 1844 + 1 ignorado (CPU mín. 46), `rt-connectors` 12/12 (100), `frontend` 2820 em 207 arquivos (48), as três `APROVADA`; `nomes.txt` com 1857 linhas. A lista dos endpoints (103 linhas) em `281/baseline/lista-endpoints.txt`, pelo caso temporário, apagado; `git status` da baseline vazio depois.
- [x] 0.9 O design corrigido com o que a tarefa 0 achou (convenção 9), numa seção "O que o apply mudou".
  **Feito:** design, "O que o apply mudou".

## 1. O cliente OIDC sem provedor (apps/api; `federated-providers`, design D1)

- [x] 1.1 `IdentityIssuerServiceCollectionExtensions`: o `AddClient`, o `IdentityKeyRingClientOptionsConfigurer`, o `IdentityKeyRingClientChangeTokenSource` e as opções do cookie do fluxo registrados sempre; sem provedor, o `UseWebProviders` sem nenhum `AddGoogle`. O comentário das l. 159–161 reescrito com o porquê (o retorno de um fluxo começado antes do reinício).
  **Feito.**
- [x] 1.2 `IdentityIssuerStartupValidation` (l. 184–209): a conferência dos endpoints do cliente nos dois sentidos também sem provedor; o `clientRegistered` sai.
  **Feito.**
- [x] 1.3 Os comentários que dizem "sem provedor, o cliente nem é registrado": `FederatedMissingRegistrationHandler.cs` (l. 17–18) e o `NginxNavigationEndpointsTests.cs` (l. 16–18, na 3.1).
  **Feito.**
- [x] 1.4 Casos (`FederatedClientWithoutProviderTests`, nome de trabalho), sobre um host sem provedor com o contador de pedidos de saída (**duplo novo**: um `IHttpMessageHandlerBuilderFilter` de teste que conta todo pedido de todo `HttpClient` da fábrica):
  - `TheHostWithoutProvider_MakesNoNetworkRequest`: o boot e um pedido a `/auth/federated/providers` e um ao retorno com state forjado; o contador em `0`;
  - `TheHostWithoutProvider_ExposesTheSameEndpoints`: as rotas mapeadas (método e padrão) e os caminhos do servidor e do cliente OIDC, iguais à lista da baseline (0.8), e os do cliente só `/auth/federated/callback`.
  Requisito: `federated-providers`, "Provedores federados por configuração", cenários "Sem a seção, o boot não faz pedido de rede" e "Sem a seção, os endpoints do host são os mesmos".
  **Feito:** `Identity/FederatedClientWithoutProviderTests.cs`, com o `WithoutProviderCountingFixture` e o `OutboundRequestCounter` (duplo) e o `HostEndpointList` (duplo não previsto); os dois aprovados.
- [x] 1.5 Caso (`IdentityIssuerStartupValidationTests`): a composição real sem provedor confere os endpoints do cliente e passa; e, com um caminho do cliente fora do `ExpectedClient` num host sem provedor, reprova nomeando o caminho. Requisito: cenário "Sem a seção, a checagem de boot do cliente passa".
  **Feito:** `RealComposition_WithoutProvider_StillChecksTheClientEndpoints` (o retorno tirado do cliente reprova o boot sem provedor, nomeando o caminho); aprovado.
- [x] 1.6 **(guarda G0.2a)** "O boot sem provedor fazendo pedido de rede". **Defeito:** um `IHostedService` registrado na composição que, sem provedor, busca o discovery fixo do tipo Google (`https://accounts.google.com/.well-known/openid-configuration`) pelo `IHttpClientFactory`. Reprova: `TheHostWithoutProvider_MakesNoNetworkRequest` (`Expected 0, Actual 1`).
  **Guarda feito:** `TheHostWithoutProvider_MakesNoNetworkRequest` reprovou ("pedidos de saída sem provedor: https://accounts.google.com/.well-known/openid-configuration"); o dos endpoints aprovou; voltou `IdentityIssuerServiceCollectionExtensions.cs` (`a2c8bdcd…`).
- [x] 1.7 **(guarda G0.2b)** "O boot sem provedor expondo rota nova". **Defeito:** `SetPostLogoutRedirectionEndpointUris("auth/federated/logout-callback")` no `AddClient`, acrescentado também ao `IdentityIssuerEndpoints.ExpectedClient` e mapeado como rota anônima `AuthEntryPoint` (para a checagem de boot e o `TheAuthEntryPoints_AreTheKnownOnes` não pegarem antes: o guarda é a lista). Reprova: `TheHostWithoutProvider_ExposesTheSameEndpoints`, nomeando `/auth/federated/logout-callback`.
  **Guarda feito:** `TheHostWithoutProvider_ExposesTheSameEndpoints` reprovou ("A mais: GET /auth/federated/logout-callback"), com o boot de pé; voltaram os quatro arquivos com o sha igual (`a2c8bdcd…`, `e4adf7a5…`, `7dece5b9…`, `e506b380…`).

## 2. O retorno sem provedor e com a intenção desconhecida (apps/api; `federated-login`, design D1, D2, D3)

- [x] 2.1 `FederatedLoginEndpoints.CallbackAsync`: sai o desvio das l. 132–137; com o catálogo vazio, o `LogFailed` leva a chave do state (ou `(desconhecido)`) e `sem_provedor_configurado`; o resto das l. 139–166 não muda. O comentário do `FailureRefusal` (l. 169) com a tabela dos três destinos e a intenção desconhecida no `/entrar`.
  **Feito.**
- [x] 2.2 O host sem provedor no mesmo banco e com o mesmo anel do `FederatedLoginFixture` (**duplo novo**, `NoProviderSameDatabase`, nome de trabalho): a forma (um `WithWebHostBuilder` do fixture com provedor, ou um segundo `WebApplicationFactory` com a cadeia de conexão e a `Identity:SigningKeyEncryptionKey` dele) é a que a 0.4 provou. Contado como duplo.
  **Feito:** `Support/NoProviderSameDatabase.cs` (`NoProviderSameDatabaseHost`, `RestartedWithoutProviderFixture`) e o gancho `SharedDatabaseConnectionString` no `ApiFactoryFixture` (não previsto).
- [x] 2.3 Casos do caso 1 (em `FederatedRemovedProviderTests.cs`, ao lado dos da #276), o início e a autorização no host com provedor, a volta no host sem provedor, com o cookie do fluxo:
  - `TheConnect_WithNoProviderConfigured_IsFailure_BackToTheAccount`: `302 /account?recusa=falha&provedor=corporativo`, sem `buteco-sessao`, sem vínculo, o cookie do fluxo apagado, nenhum cookie novo, o state `redeemed`, o `1199` com `corporativo` e `sem_provedor_configurado`;
  - `TheLogin_WithNoProviderConfigured_IsFailure_ToTheLogin_WithTheProvider`: `302 /entrar?recusa=falha&provedor=corporativo`, sem sessão;
  - `TheAccept_WithNoProviderConfigured_IsFailure_BackToTheInvitation_StillPending`: `302 /convite?recusa=falha&provedor=corporativo`, sem sessão, o convite pendente.
  Requisitos: `federated-login`, "Decisão do login no retorno" (os três cenários "Nenhum provedor configurado no retorno…"), "Conectar o provedor em Minha conta" ("A falha do conectar sem provedor volta a Minha conta") e "As recusas do aceite…" ("O provedor saiu da configuração no meio").
  **Feito:** os três em `FederatedNoProviderAfterRestartTests`, aprovados; o status do state lido pelo `IOpenIddictTokenManager` (a coluna guarda o reference id ofuscado).
- [x] 2.4 Casos do caso 2 (`FederatedUnreadableStateTests`, nome de trabalho), todos de um conectar começado, com o cookie do fluxo dele:
  - `TheReturn_WithoutState_IsFailure_ToTheLogin`;
  - `TheReturn_WithAForgedState_IsFailure_ToTheLogin`;
  - `TheReturn_WithAnExpiredConnectState_IsFailure_ToTheLogin`: o `Clock` do fixture avançado 11 min entre a autorização e a volta;
  - `TheReturn_WithAReusedConnectState_IsFailure_ToTheLogin`: o conectar termina com `/account?conectado=corporativo`, e a mesma URL de retorno, outra vez;
  cada um `302 /entrar?recusa=falha`, sem `provedor`, sem `buteco-sessao`, sem vínculo novo. Requisito: os quatro cenários "O state … cai no login".
  **Feito:** `FederatedUnreadableStateTests` (3) e `FederatedExpiredStateTests` (1), aprovados.
- [x] 2.5 Caso do destino fixo: `TheFailureTarget_IsAlwaysOneOfTheFixed`: o retorno do conectar sem provedor com `&retorno=https://outro.example/` e com `&retorno=//outro.example` na query; o `Location` é exatamente `/account?recusa=falha&provedor=corporativo`, começa por `/` e não por `//`. Requisito: cenário "O destino da falha não sai do pedido".
  **Feito:** `TheFailureTarget_IsAlwaysOneOfTheFixed`, um Theory com os dois valores, aprovado.
- [x] 2.6 **(guarda G1)** "O caso 1 voltando ao `/entrar`, pela rota". **Defeito:** o desvio `if (catalog.Providers.Count == 0) { LogFailed(…); return LoginRefusal(FederatedLogin.Refusals.Failure, providerKey: null); }` de volta antes do `AuthenticateAsync`. Reprova: `TheConnect_WithNoProviderConfigured_IsFailure_BackToTheAccount` (`Expected /account?recusa=falha&provedor=corporativo`, `Actual /entrar?recusa=falha`), e o do aceite; o `FederatedNoProviderTests` de hoje aprova.
  **Guarda feito:** os 5 do caso 1 reprovaram (`Actual "/entrar?recusa=falha"`); o `FederatedNoProviderTests` aprovou; voltou `FederatedLoginEndpoints.cs` (`7dece5b9…`).
- [x] 2.7 **(guarda G1b)** "O caso 1 voltando ao `/entrar`, pelo registro". **Defeito:** o `if (federated.Providers.Count == 0) return services;` de volta antes do `AddClient`, com a rota da change (sem o desvio). Reprova: `TheConnect_WithNoProviderConfigured_IsFailure_BackToTheAccount` (`500`: o esquema do cliente não existe) e o caso da 1.5 (o cliente sem provedor não confere).
  **Guarda feito, por outro caminho:** a checagem de boot (agora incondicional) derrubou o host sem provedor antes do `500` previsto ("'/auth/federated/callback' está na lista esperada do cliente e não corresponde…"), no caso do conectar e no `RealComposition_Boots_WithTheRealRoutesAndEndpoints`; voltou `IdentityIssuerServiceCollectionExtensions.cs` (`a2c8bdcd…`).
- [x] 2.8 **(guarda G3)** "A intenção desconhecida ou forjada indo ao `/account`". **Defeito:** o ramo padrão do `FailureRefusal` trocado de `LoginRefusal` para `AccountRefusal`. Reprovam: os quatro da 2.4 e o `FederatedNoProviderTests` (`Expected /entrar?recusa=falha`, `Actual /account?recusa=falha`); os da 2.3 aprovam.
  **Guarda feito:** os quatro do caso 2 e o `FederatedNoProviderTests` reprovaram (`Actual "/account?recusa=falha"`); o login sem provedor também, porque divide o ramo padrão com a intenção desconhecida; o conectar e o aceite aprovaram; voltou `7dece5b9…`.
- [x] 2.9 **(guarda G4)** "Um destino fora dos fixos". **Defeito:** o `AccountRefusal` passando a usar o `retorno` da query do pedido como destino, quando há. Reprova: `TheFailureTarget_IsAlwaysOneOfTheFixed`, com o `Location` `https://outro.example/`.
  **Guarda feito:** reprovou com o `Location` `https://outro.example/` e `//outro.example`; voltou `7dece5b9…`.

## 3. O guarda da #285 com o cliente sempre registrado (apps/api; `server-deployment`, design D0, P3)

- [x] 3.1 `NginxNavigationEndpointsTests`: uma base abstrata com o caso e as duas asserções, e dois herdeiros, sobre o `FederatedLoginFixture` (a classe e o nome de hoje) e sobre um host sem provedor; o comentário das l. 11–19 com os dois hosts.
  **Feito:** `NginxNavigationEndpointsTestsBase` e os herdeiros `NginxNavigationEndpointsTests` e `NginxNavigationEndpointsWithoutProviderTests`.
- [x] 3.2 **(guarda G0.3a)** "A lista do código vazia" (a 3.6 da #285, na forma nova). **Defeito:** a fonte da lista trocada por um `new OpenIddictClientOptions()` não composto, no lugar do `IOptionsMonitor` do host. Reprovam os dois herdeiros, pela asserção da l. 30.
  **Guarda feito:** os dois herdeiros reprovaram pela l. 30; voltou `NginxNavigationEndpointsTests.cs` (`bf85184c…`).
- [x] 3.3 **(guarda G0.3b)** "O retorno sem bloco exato" (o defeito da #285). **Defeito:** o `location = /auth/federated/callback` tirado do `apps/frontend/deploy/nginx.conf`. Reprovam os dois herdeiros, nomeando `/auth/federated/callback`.
  **Guarda feito:** os dois herdeiros reprovaram nomeando `/auth/federated/callback`; voltou `apps/frontend/deploy/nginx.conf` (`8a452d98…`).

## 4. Conferência local, com o duplo do Google (design, D5)

Sem suíte rodando: o `conferencia-281.sh` toma o mesmo lock.

- [x] 4.1 O roteiro da D5 com a imagem do `api` do worktree da change (`podman build` sem suíte junto): o banco `buteco_agents_conf281` do `template0`, migrado pelo worktree; o pod com o `fake-google-281.py`; o caso 1 no conectar, no login e no aceite, com o reinício sem a seção entre a autorização e a volta; o caso 2 (o já usado, o forjado e o sem `state`); o log sem o e-mail, o `sub`, o `code` e o `state`. Saídas em `~/.cache/buteco-agents/281/conferencia/`.
  **Feito (18/11/2026), 28/28 conferências OK** (`~/.cache/buteco-agents/bin/conferencia-281.sh`, cópia do `sonda-281.sh` sem a P2; saídas em `281/conferencia/`): a imagem do worktree da change; as três voltas no `api` reiniciado sem a seção deram `/account…`, `/entrar…` e `/convite…` com `&provedor=corporativo`, sem `buteco-sessao`, com o cookie do fluxo apagado e sem cookie novo; os state tokens de `valid=3` a `redeemed=3`; nenhum vínculo; o convidado `invited`; as três linhas `1199` com `corporativo` e `sem_provedor_configurado`; nenhum `ID2004`/`ID2019`/`ID2139` nem exceção. O caso 2, com o provedor de volta: o já usado, o forjado e o sem `state` no `/entrar?recusa=falha`. O log: 13 valores procurados (e-mails, `sub`, `code`, `state`, o token do convite), nenhum encontrado.
- [x] 4.2 A limpeza conferida: `DROP DATABASE buteco_agents_conf281`; o pod, o `api` e o duplo parados; a imagem da conferência e a CA apagadas; conferido por `\l`, `podman ps -a` e `podman images`.
  **Feito:** contêineres, pod, a imagem `buteco281-conf-api`, o banco `buteco_agents_conf281` e a CA apagados; conferido em `281/conferencia/limpeza.txt` (todos 0, e a CA fora da Keychain e da VM).

## 5. Documentação (antes do archive, dentro do PR)

- [x] 5.1 `CHANGELOG.md`: a correção da #281 (o conectar sem provedor volta a Minha conta; a intenção desconhecida continua no login).
  **Feito.**
- [x] 5.2 `02-HISTORICO_E_STATUS.md`: a entrada da change (a tarefa 0 com as três premissas e o que fez cada uma passar; a baseline e o fechamento; os guardas com a reprovação vista; a conferência local; a medição da convenção 18 contra a projeção do design, D7, com os duplos à parte), e a linha da fila atualizada.
  **Feito.**
- [x] 5.3 `docs/deployment.md`: a frase da l. 467 ("A falha do conectar que ainda volta ao `/entrar`…") reescrita com o comportamento novo; e, na seção da #189, que sem provedor o cliente OIDC fica registrado sem registração, sem pedido de rede nem rota nova (a promessa da l. 343 continua verdadeira).
  **Feito:** a l. 343, a nota da #279 e a seção nova "A falha do conectar sem provedor volta a Minha conta (#281)".

## 6. Fechamento

- [x] 6.1 `dotnet build` sem aviso novo (comparado à baseline); `openspec validate conectar-falha-minha-conta --strict`.
  **Feito:** 8 avisos distintos na baseline e na change (`apps/api/Api.sln` e `tests/ApiConnectorsRoundTrip.Tests`, `--no-incremental`), nenhum novo; `openspec validate --strict` válido.
- [x] 6.2 As suítes da change pelo `suite-281.sh`, uma por vez, em `~/.cache/buteco-agents/281/fechamento/`: `api`, `rt-connectors` e `frontend`.
  **Feito:** primeira rodada (`281/fechamento/`): `api` 1856 + 1 ignorado e **1 reprovação** (`WithBothRoutesAnonymousAndClassified_Passes`, o caso que a change mudou: parada, ajuste aprovado pelo dono), `rt-connectors` 12/12 APROVADA, `frontend` 2820 em 207 APROVADA. Refeita a `api` (`281/fechamento-2/`): 1857 + 1 ignorado, 8 min 17 s, CPU mín. 46, **APROVADA**. Depois da primeira rodada, só um arquivo de teste do `apps/api` mudou.
- [x] 6.3 A comparação por nome contra a baseline (`nomes-trx.py` e o `frontend.json`): nenhum caso some nem muda fora da lista do topo, e cada caso novo fica ligado ao requisito.
  **Feito:** `281/fechamento-2/nomes.txt` (`api` refeita e `rt-connectors`) contra `281/baseline/nomes.txt`: nenhum saiu nem mudou de resultado; entraram 13, todos aprovados (`FederatedNoProviderAfterRestartTests` 5, `FederatedUnreadableStateTests` 3, `FederatedExpiredStateTests` 1, `FederatedClientWithoutProviderTests` 2, `RealComposition_WithoutProvider_StillChecksTheClientEndpoints` 1, `NginxNavigationEndpointsWithoutProviderTests` 1). `frontend`: os mesmos 2820 nomes.
- [x] 6.4 A medição da convenção 18: produção e testes por arquivo, lógica e comentário, os duplos à parte, contra a tabela do design (D7).
  **Feito:** `281/medicao-conv18.txt`, sem passar pelo índice (`git diff` nos modificados, `diff` contra `/dev/null` nos novos): produção +35 / −23 (5 de lógica, 30 de comentário); casos +459 / −4 (103 são a lista da baseline); duplos +200 / −3; 13 casos.
- [x] 6.5 O design com "O que o apply mudou" completo (convenção 9).
  **Feito.**
- [x] 6.6 (openspec) O portão: a entrada no `02` e no `CHANGELOG` existe; o archive com o sync de `federated-login`, `federated-providers` e `server-deployment`.
  **Feito:** a entrada no `02` e no `CHANGELOG` conferidas antes; o archive com o sync das três specs.
- [x] 6.7 (git) **Só com o pedido do dono:** um commit só, depois do archive; o push; o PR com `Closes #281`, citando a #189, a #276 e a #279, com o checklist da homologação (D6); a #281 em `In review`.
  **Autorizado pelo dono em 18/11/2026** ("um commit só, o push e o PR fechando a #281"): o commit depois do archive, o push e o PR com `Closes #281`; a #281 em `In review`. O número do PR vai no comentário da issue.

## 7. Homologação, depois do merge (fora do PR; convenção 23)

**Depois do merge, no servidor de homologação, com a confirmação do dono sobre a máquina** (decisão do dono, 18/11/2026). Não é tarefa da change: vai como checklist no PR e o resultado vai como comentário na #281.

- [x] Antes de tudo, o dono confirma que o servidor de homologação **não** é a máquina do piloto (design, D6); se for, a reprodução não acontece ali.
- [x] Na janela que o dono combinar: o roteiro da D6 (o conectar parado na escolha de conta, o provedor tirado e o `api` reiniciado, a volta, as conferências e a restauração conferida).
  **Feito (19/11/2026), a homologação passou:** a reprodução do caso 1 no servidor de homologação voltou a `/account?recusa=falha`, com a linha `1199` e nenhum vínculo gravado. Registrado no `02`, "A homologação" da `conectar-falha-minha-conta`.
