## Context

A #281 é o bug do `apps/api` achado no design da #279 (Q10): duas falhas do conectar voltam ao `/entrar`, e não a
`/account`. Ela saiu da M3 para o backlog em 17/11/2026 e voltou no mesmo dia, por decisão do dono, para ser
corrigida antes da M4.

**Fontes lidas** (17/11/2026, `origin/main` em `0bfa1cf`, o merge do PR #290):
- o corpo da #281; o design e as tasks arquivados da `provedores-federados-google` (#189, D4, o cookie do fluxo) e
  da `aceite-convite-google` (#274, D3, D9, a 0.2 e a 10.1);
- as specs vivas `federated-login` e `federated-providers`;
- no `apps/api`: `Identity/Federated/FederatedLoginEndpoints.cs`, `FederatedLogin.cs`, `FederatedChallenge.cs`,
  `FederatedMissingRegistrationHandler.cs`, `FederatedRequestUriHandler.cs`,
  `Identity/IdentityIssuerServiceCollectionExtensions.cs`, `Identity/IdentityIssuerStartupValidation.cs`,
  `Identity/Keys/IdentityKeyRingClientOptionsConfigurer.cs`;
- nos testes do `apps/api`: `Identity/Federated/FederatedRemovedProviderTests.cs`, `Support/GoogleFlow.cs`,
  `Support/FederatedInvitationSupport.cs` (o `StateTokenReader`), `Support/ApiFactoryFixture.cs`;
- no `apps/frontend`: `features/account/federatedReturn.ts` e `accountFederatedTexts.ts`;
- o fonte do OpenIddict **7.7.2** (a versão do `Directory.Packages.props`), em
  `~/.cache/buteco-agents/182-fonte/openiddict-core` (tag `7.7.2`): `OpenIddict.Client/OpenIddictClientHandlers.cs`,
  `OpenIddictClientHandlers.Protection.cs`, `OpenIddict.Client.AspNetCore/OpenIddictClientAspNetCoreHandler.cs` e
  `OpenIddictClientAspNetCoreHandlers.cs`.

Caminhos do backend a partir de `apps/api/src/Buteco.Api/`; os do OpenIddict, a partir de `src/` do fonte.

### O caminho do retorno no cliente OIDC

A ordem dos handlers da autenticação (`OpenIddictClientHandlers.cs` l. 40–49) decide o que a rota recebe:

| ordem | handler | se recusa aqui, a rota recebe |
|---|---|---|
| 1 | `ResolveValidatedStateToken` (l. 598): o state é o `state` da query (l. 615–621) | — |
| 2 | `ValidateRequiredStateToken` (l. 630): sem `state`, `missing_token` (l. 652) | **`NoResult`**, sem `Properties` (`OpenIddictClientAspNetCoreHandler.cs` l. 154–157) |
| 3 | `ValidateStateToken` (l. 666): o reference id procurado no banco (`…Protection.cs` l. 237 e 275; desconhecido: `ID2004`, l. 289–292), o JWE decifrado e a assinatura, o vencimento (l. 663; vencido: `ID2019`, l. 686–689), a entrada (l. 823; já resgatada: `ID2139`, l. 855–863) | falha **sem** as propriedades do Buteco |
| 4 | `ResolveHostAuthenticationPropertiesFromStateToken` (l. 737–770): o claim `oi_hst_props` vira `context.Properties` | — |
| 5 | `RedeemStateTokenEntry` (l. 812), `ValidateRequestForgeryProtection` (l. 919, o cookie do fluxo), `ValidateEndpointUri` | falha **com** as propriedades |
| 6 | `FederatedMissingRegistrationHandler` (ordem `ResolveClientRegistrationFromStateToken` − 500) | falha **com** as propriedades |

O state que o Buteco manda ao Google é um **reference token**: o `GenerateLoginStateToken` grava com
`IsReferenceToken = !DisableTokenStorage` (`OpenIddictClientHandlers.cs` l. 5807), o payload fica em
`openiddict_tokens` e o `state` da URL é só o reference id, 256 bytes aleatórios em base64url
(`…Protection.cs` l. 1183–1195). O payload é um JWE cifrado e assinado pelas chaves do anel
(`IdentityKeyRingClientOptionsConfigurer.cs` l. 30–39).

Daí as duas falhas da #281: o caso 1 nem chega à ordem 1 (D1); o caso 2 é a recusa nas ordens 2 e 3 (D2). Toda
recusa da ordem 5 em diante já volta pela intenção: é o que a #276 mediu (`FederatedRemovedProviderTests.cs`,
`TheConnect_WithTheKeyRenamedInTheMiddle_IsFailure_WithoutLink`, `/account?recusa=falha&provedor=corporativo`).

## Goals / Non-Goals

**Goals:** a falha do conectar sem provedor configurado volta a `/account?recusa=falha&provedor=<chave>`; a regra
da intenção desconhecida fica escrita na spec; os guardas pegam cada defeito pelo nome.

**Non-Goals:** mudar o painel (D7); um cookie, uma rota ou uma URI de retorno nova; mudar o login, o aceite ou o
conectar que dão certo; mexer em qualquer outro app.

## Decisions

### D0. As premissas da A, provadas antes de qualquer código da correção (tarefa 0; condição do dono, 17/11/2026)

A A (D1) só vale se as três premissas abaixo se provarem, nesta ordem. A prova usa uma **sonda**: um patch
descartável com o mínimo da A (o `AddClient` sem a condição e a rota sem o desvio das l. 133–137), aplicado num
worktree próprio da sonda, nascido do `origin/main` e removido no fim. O patch fica em
`~/.cache/buteco-agents/281/sonda/sonda.diff`, e nada dele entra no worktree da change: o código da correção é escrito
depois, com os casos.

**P1. O caso real, de ponta a ponta, no pod da 10.1 da #274.** A imagem do `api` da sonda, com o duplo do Google e o
banco descartável da D5:
1. o conectar começa **com** o provedor (`200 { url }` e o cookie do fluxo), e a autorização no duplo devolve o
   `code` e o `state`;
2. o `api` para e sobe **sem** a seção do provedor, no mesmo banco e com a mesma chave do anel
   (`Identity:SigningKeyEncryptionKey`, `Identity/Keys/IdentityKeyMaterialCipher.cs` l. 27–30);
3. a volta, com o pote de cookies.

Prova: o `302 /account?recusa=falha&provedor=corporativo` (a rota só o monta com o `buteco_intencao` = `conectar` e o
`buteco_provedor` lidos das propriedades restauradas); a linha do state em `openiddict_tokens` com o status
`redeemed` (o JWE decifrou, a assinatura conferiu, e o OpenIddict passou da ordem 3 à 5); a linha `1199`. **As
credenciais de cifra do cliente não dependem do provedor:** saem do `IdentityKeyRingClientOptionsConfigurer`, que lê
só o `IdentityKeyRing.Snapshot` (`Identity/Keys/IdentityKeyRingClientOptionsConfigurer.cs` l. 23–40) e passa a ser
registrado antes da antiga condição; a P1 prova isso no reinício, e não só no papel.

**Para (e volta ao dono com a B) se:** o OpenIddict 7.7.2 recusar o cliente sem registração no boot ou no retorno; a
volta responder `/entrar`, `500` ou outro destino; a linha do state não ficar `redeemed`; ou o log mostrar `ID2004`,
`ID2019` ou erro de decifração (o state não decifrou depois do reinício).

**P2. Sem provedor, nada muda para quem não usa o Google** (a promessa do `docs/deployment.md` l. 343: "sem provedor
configurado, **nada muda**: o login fica só com senha, como antes"). Na mesma imagem da sonda, sem a seção, e contra a
imagem do `origin/main` (`0bfa1cf`) nas mesmas condições:
1. **o boot sem nenhum pedido de rede:** o `api` com `HTTP_PROXY` e `HTTPS_PROXY` apontando para uma porta morta,
   como na 13.1 da #189, e o log do boot sem `HttpRequestException`, sem linha do `System.Net.Http.HttpClient` e sem
   linha do `OpenIddict.Client` falando de discovery; o boot conclui (a linha de pronto do host);
2. **o `IdentityIssuerStartupValidation` sem reprovar:** os caminhos do cliente (`RedirectionEndpointUris`) são
   exatamente o `ExpectedClient` (`/auth/federated/callback`), e a checagem passa a conferi-los também sem provedor;
3. **`GET /auth/federated/providers` vazio:** `200 {"provedores":[]}`, igual ao de antes;
4. **nenhuma rota nova respondendo:** a lista dos endpoints do host (os `RouteEndpoint` do `EndpointDataSource`, com
   método e padrão, mais os caminhos do servidor e do cliente OIDC) tirada pelo `curl` de um conjunto fixo de
   caminhos e, nos testes, por um caso que grava a lista; **comparada antes (imagem do `origin/main`) e depois
   (imagem da sonda)**. A única diferença aceita é nenhuma: o `/auth/federated/callback` já é rota mapeada sem
   provedor (`MapFederatedLoginEndpoints`, l. 47–55), e a resposta dele a um state forjado continua
   `302 /entrar?recusa=falha`.

**Para se:** o boot sem a seção fizer qualquer pedido de rede, reprovar na checagem, o `providers` vier diferente de
vazio, ou a lista de endpoints mudar. Cada um muda o que a instalação sem Google vê: é critério de parada pela
premissa falsa, e volta ao dono com a B.

**P3. O guarda da #285 continua reprovando contra o defeito dele.** Hoje o `NginxNavigationEndpointsTests`
(`apps/api/tests/Buteco.Api.Tests/NginxNavigationEndpointsTests.cs` l. 20–39) roda sobre o `FederatedLoginFixture`,
e a mutação da 3.6 da #285 ("a lista do código vazia") troca o fixture pelo `LocalUsersFixture`: sem provedor, o
cliente não existia, a lista vinha vazia, e o `Assert.True(clientPaths.Count > 0, …)` (l. 30) reprovava. **Com a A,
essa mutação deixa de reprovar:** o host sem provedor também tem o cliente, com o retorno na lista. O guarda não pode
ficar verde por isso. O ajuste, sem afrouxar:
- **o caso passa a rodar nos dois hosts** (com provedor e sem nenhum), porque o retorno agora é navegação de topo
  real nos dois: o teste vira uma base com dois herdeiros, um por fixture, e a asserção da lista não vazia fica nos
  dois;
- **a mutação da 3.6 muda de forma, e não de alvo:** o defeito continua sendo "a lista do código vazia", introduzido
  agora na **fonte** da lista (o teste lendo um `new OpenIddictClientOptions()` não composto, no lugar do
  `IOptionsMonitor` do host). Reprova pela l. 30 nos dois herdeiros;
- **a mutação do defeito da #285** (o `location = /auth/federated/callback` tirado do `nginx.conf`, a 2.2 dela)
  continua reprovando, agora nos dois herdeiros, nomeando o caminho;
- o comentário do teste (l. 16–18, "sem provedor, o cliente nem é registrado") e a frase da spec
  `server-deployment` ("sobre um host com um provedor configurado") passam a dizer os dois hosts.

**Para se:** alguma das duas mutações aprovar em algum dos dois herdeiros.

### D1. O caso 1: o state é lido pelo próprio cliente OIDC, registrado também sem provedor

**O `AuthenticateAsync` não consegue ler o state sem provedor.** O `AddClient` só roda com provedor configurado
(`IdentityIssuerServiceCollectionExtensions.cs` l. 159–165, o `return services` antes da l. 170), e com ele saem o
esquema `OpenIddictClientAspNetCoreDefaults.AuthenticationScheme`, o handler e as credenciais do cliente. Sem o
esquema, o `AuthenticationService.AuthenticateAsync` do ASP.NET Core lança `InvalidOperationException` ("No
authentication handler is registered for the scheme…"): é por isso que a rota recusa antes (o comentário da
`FederatedLoginEndpoints.cs` l. 132). Não há leitura parcial: o handler não existe.

**O que consegue ler o state sem o cliente:**
- **a linha do state em `openiddict_tokens`**: o core do OpenIddict é registrado sempre, com o EF
  (`IdentityIssuerServiceCollectionExtensions.cs` l. 86–93), e o `IOpenIddictTokenManager.FindByReferenceIdAsync`
  acha a linha pelo `state` da query (o que o `ValidateReferenceTokenIdentifier` faz, `…Protection.cs` l. 275);
- **o payload, decifrado pelas chaves do anel**: o `IdentityKeyRing` existe sem provedor (l. 61–68 do mesmo
  arquivo), e o `JsonWebTokenHandler.DecryptToken` com as chaves de cifra devolve o JWS com o `oi_hst_props`. Já se
  faz isso nos testes: `StateTokenReader.DecryptedPayloadOfNewAsync`
  (`tests/Buteco.Api.Tests/Support/FederatedInvitationSupport.cs` l. 70–90), com as `EncryptionCredentials` do
  cliente; sem cliente, viriam do `IdentityKeyRing.Snapshot`.

**As alternativas:**

| | o que faz | custo | risco |
|---|---|---|---|
| **A (escolhida)** | registrar o cliente **sempre**; sem provedor, com zero registrações. O retorno passa pelas ordens 1 a 5, e o `FederatedMissingRegistrationHandler` recusa por falta da registração (ordem 6), como na chave trocada. A rota perde as l. 133–137 | ~+15 / −15 em produção; a checagem de boot do cliente (`IdentityIssuerStartupValidation.cs` l. 188) deixa de depender do catálogo | a instalação sem provedor ganha os handlers do cliente no pipeline (Risks); a validação das opções do cliente pede as credenciais do anel (ID0357/ID0358) também sem provedor, o que o `IdentityKeyRingGate` já cobre para o servidor |
| B | manter o cliente condicional e, sem provedor, ler o state na rota: a linha pelo reference id, o JWE pelas chaves do anel, a assinatura, o tipo, o vencimento e o estado da entrada | ~+80 em produção: um leitor que repete os handlers 3 e 4 do OpenIddict | acoplado a detalhe interno do OpenIddict (o `oi_hst_props`, o tipo do state, o formato do payload); um erro na conferência deixa um state forjado escolher o destino; sobe de versão com o OpenIddict |
| C | gravar, no início do conectar, a intenção ao lado do reference id (tabela nova) | migration e entidade novas | uma gravação nova por início, e um segundo registro da mesma coisa que o state já guarda |

**Por que A:** o caso 1 vira o caso da chave trocada, que já é coberto, medido e homologado; a intenção sai do
mesmo lugar e pelo mesmo código que o resto do retorno; o OpenIddict confere o state, e o Buteco não reimplementa
criptografia. A escolha depende das três premissas da D0, provadas antes de qualquer código da correção.

**O que muda na rota:** some o desvio das l. 133–137. O log `1199` continua distinguindo o caso: com o catálogo
vazio, `LogFailed(logger, <chave do state>, "sem_provedor_configurado")`, e não o `invalid_request` do
`FederatedMissingRegistrationHandler` (que fica para a chave trocada). O resto da l. 146–152 não muda: a intenção e a
chave do state, o `FailureRefusal`.

**O resultado por intenção, sem provedor:**

| intenção do state | hoje | com a D1 |
|---|---|---|
| `conectar` | `/entrar?recusa=falha` | **`/account?recusa=falha&provedor=<chave>`** |
| `login` | `/entrar?recusa=falha` | `/entrar?recusa=falha&provedor=<chave>` (o `provedor` passa a vir, como na chave trocada) |
| `convite` | `/entrar?recusa=falha` | **`/convite?recusa=falha&provedor=<chave>`** (Q1, decisão do dono, 17/11/2026) |
| desconhecida (state ausente, forjado, vencido, já usado) | `/entrar?recusa=falha` | `/entrar?recusa=falha` (D2) |

### D2. O caso 2: de onde viria a intenção quando o state não a traz

**Quando acontece** (a tabela do Context): o state **ausente** (ordem 2), **desconhecido ou adulterado** (ordem 3,
`ID2004`), **vencido**, mais de 10 min entre o início e a volta (ordem 3, `ID2019`), ou **já resgatado**, o voltar do
navegador ou a mesma URL de retorno aberta duas vezes (ordem 3, `ID2139`). Nos quatro, o OpenIddict recusa **antes**
do handler 4, e a rota recebe a intenção nula.

**O cookie do fluxo (`buteco-fluxo-externo.<nonce>`):**
- **quem grava:** o **OpenIddict**, no `GenerateLoginCorrelationCookie`
  (`OpenIddictClientAspNetCoreHandlers.cs` l. 742–824). O Buteco só troca o nome, o `SameSite`, o `Path` e o
  `Secure` do `CookieBuilder` (`IdentityIssuerServiceCollectionExtensions.cs` l. 215–222);
- **o que leva:** o nome tem o nonce do state (l. 800–803); o valor é `0x01` + o tamanho + o valor
  anti-falsificação, em base64url (l. 805–821). **Nenhuma** propriedade do Buteco, nenhuma intenção;
- **protegido?** **Não.** É só correlação: o próprio OpenIddict diz que "the correlation cookie is not protected
  against tampering" (l. 447–450), e a adulteração só se pega porque o valor é comparado com o que está **dentro** do
  state (ordem 5). Sem o state legível, o valor do cookie não prova nada;
- **o callback o recebe nos casos?** **Caso 1:** sim (`Lax`, `Path=/auth/federated/callback`, a navegação de topo
  vinda do Google), e é com ele que a ordem 5 da D1 passa. **Caso 2:** depende do sub-caso, e falta justo nos dois que
  acontecem de verdade: **vencido**, o cookie vence com o state (`options.Expires ??= <vencimento do state>`, l. 797),
  e o navegador não o manda; **já resgatado**, a primeira volta o apagou (l. 467, e o `DeleteFlowCookies` da rota,
  `FederatedLoginEndpoints.cs` l. 318–324). Só chega no state **ausente ou forjado** dentro dos 10 min, e aí também não
  traz a intenção.

**Conclusão do cookie:** ele não serve como está, e não há como fazê-lo servir sem trocar o handler do OpenIddict
(que o próprio comentário das l. 767–772 diz ser o caminho de quem quer outra estratégia).

**As alternativas:**

| | de onde vem a intenção | custo | risco |
|---|---|---|---|
| **N (escolhida, Q2): não corrigir o caso 2** | de lugar nenhum: a intenção desconhecida vai ao `/entrar?recusa=falha`, como hoje, agora escrito na spec como regra | zero em produção; um cenário e o guarda da D4 (G3) | a pessoa que conectava e voltou depois de 10 min, ou pelo voltar do navegador, lê o aviso do login logada no painel. Raro, e sem dano: nenhuma gravação, e o painel continua logado |
| L: ler a linha do state recusado | o `state` da query → a linha em `openiddict_tokens` → o payload decifrado pelo anel → o `oi_hst_props`, **mesmo vencido ou resgatado** (a linha fica até a limpeza, `IdentityTokenPruner`) | o leitor da alternativa B da D1 (~+80), mais a decisão de ler um token que o OpenIddict recusou | o mesmo acoplamento da B; e ler de propósito um token vencido ou já usado contraria a conferência que acabou de recusá-lo. Forjado continua caindo no `/entrar` (a linha não existe) |
| K: um cookie do Buteco com a intenção, gravado no início | um cookie novo (`buteco-intencao`, protegido pelo Data Protection, `Lax`, no caminho do retorno) | um cookie novo no início do conectar, a spec do cookie, a limpeza dele | para cobrir o vencido, ele teria de viver mais que o state, e um conectar abandonado mandaria a `/account` a falha de um **login** seguinte na mesma janela; cobre o já resgatado só se não for apagado na primeira volta. Contraria o "sem cookie novo" da D3 |
| U: uma URI de retorno por intenção (`/auth/federated/callback/conectar`) | o caminho do retorno | uma segunda URI registrada em cada cliente no console do Google, o `ExpectedClient`, o `nginx.conf` (`location =` novo, a regra da #285), o `docs/deployment.md` | quem instala precisa mexer no Google; uma URI esquecida quebra o conectar inteiro (`redirect_uri_mismatch`), e não só a falha |
| S: o `buteco-sessao` | a sessão do emissor | — | **não chega**: é `Strict`, e a volta do Google é navegação de topo de outro site (o mesmo §5.6.7.1 que a D4 da #189 cita). Descartada |

**Por que N:** o caso 2 é a falha que o cliente OIDC não reconhece; ali o `/entrar` é a resposta certa para o state
ausente ou forjado, e os dois sub-casos reais (vencido e já resgatado) não têm fonte confiável que não custe um leitor
próprio do state (L), um cookie novo (K) ou mexer no Google (U). **Decisão do dono, 17/11/2026 (Q2):** a N, com a
regra escrita na spec e **um cenário para cada sub-caso**: ausente, forjado, vencido e já usado.

### D3. A segurança

**O sinal** é a propriedade `buteco_intencao` do state token, e só ela. O state token é gravado pelo Buteco, cifrado
e assinado pelo anel, de uso único (ordem 3), e o `state` da URL é um reference id de 256 bytes aleatórios: quem não
tem a linha no banco não fabrica um. Com a D1 (A), quem confere é o OpenIddict, e o Buteco não lê nada que ele não
tenha conferido.

- **Os destinos fixos.** A falha `falha` do retorno SHALL ir a um destes três, e a nenhum outro (Q1, decisão do
  dono, 17/11/2026):

  | intenção | destino |
  |---|---|
  | `login` ou desconhecida | `/entrar?recusa=falha` |
  | `conectar` | `/account?recusa=falha` |
  | `convite` | `/convite?recusa=falha` |

  com `&provedor=<chave>` quando a chave é conhecida (vem do state). A intenção desconhecida não tem chave: o state
  não foi lido.
- **Sem redirecionamento aberto.** O `FailureRefusal` escolhe o destino da tabela; os três são **caminhos fixos e
  relativos** (`AuthorizeEndpoint.LoginPath`, `/account`, `FederatedLogin.InvitationPagePath`). Nada do pedido entra
  no caminho; a única parte variável é a chave, que sai do state e vai **escapada** (`Uri.EscapeDataString`, o
  `ProviderQuery` da l. 314–315), depois de `&provedor=`. O `buteco_retorno` do state não é usado em nenhuma falha.
- **Sem gravar vínculo, sessão ou cookie novo.** O `FailureRefusal` não grava nada; o `DeleteFlowCookies` (l. 130)
  e o `ResolveRequestForgeryProtection` (l. 467) apagam o cookie do fluxo, como hoje. A única escrita é a do próprio
  OpenIddict: o resgate do state (ordem 5), que já acontece na chave trocada e que fecha o reuso.
- **Sem dado pessoal no log.** A linha `1199` leva a chave do provedor e o código do erro (`sem_provedor_configurado`
  ou o `error` do OpenIddict), nunca o e-mail, o `sub`, o `state` nem o código. Na recusa da ordem 6 o cliente não
  chegou ao token nem ao userinfo: não há e-mail nem `sub` em memória para vazar (o achado de log da 10.1 da #274
  era do userinfo, que aqui não roda).
- **Forjado ou ausente cai no `/entrar`.** O forjado para na ordem 3 (`ID2004`), e o ausente na 2 (`NoResult`): a
  intenção é nula, e o ramo padrão é o login. Com a D2 (N), esse ramo fica escrito como regra.

**O que um atacante ganharia forjando o sinal:** nada. Ele não consegue (o sinal está dentro do state cifrado e
assinado, e o reference id não se adivinha). E se conseguisse, escolheria entre os três destinos fixos, páginas do
próprio painel que mostram um aviso de falha, sem gravar nada e sem mudar a sessão. Ele já pode mandar a vítima a
qualquer um deles com um link direto, porque o painel lê a `recusa` da URL (`features/account/federatedReturn.ts`
l. 18–24, e o mesmo no `/entrar` e no `/convite`): o sinal não dá capacidade nenhuma que o link não dê.

### D4. Os guardas, cada um com o defeito nomeado

Cada guarda: copiar o arquivo, introduzir o defeito, rodar o filtro com o lock, ver o caso reprovar pela asserção
esperada (e os vizinhos que **não** devem reprovar aprovarem), **restaurar pela cópia** e conferir o `sha256` contra
o de antes. Nunca restaurar diretório inteiro ao HEAD.

| # | guarda | defeito introduzido | caso que reprova, e como |
|---|---|---|---|
| G1 | o caso 1 voltando ao `/entrar`, pela rota | o desvio `if (catalog.Providers.Count == 0) return LoginRefusal(…)` de volta antes do `AuthenticateAsync` | `TheConnect_WithNoProviderConfigured_IsFailure_BackToTheAccount`: `Expected /account?recusa=falha&provedor=corporativo, Actual /entrar?recusa=falha` |
| G1b | o caso 1 voltando ao `/entrar`, pelo registro | o `if (federated.Providers.Count == 0) return services;` de volta antes do `AddClient` (sem o desvio da rota) | o mesmo caso, com `500` (o esquema não existe), e o caso da checagem de boot sem provedor |
| G2 | o caso 2 voltando ao `/entrar` | **não existe**: com a N (Q2), o `/entrar` é o comportamento certo, e quem o prende é o G3 | — |
| G3 | a intenção desconhecida ou forjada indo ao `/account` | o ramo padrão do `FailureRefusal` trocado para `AccountRefusal` | os quatro casos do caso 2 do conectar (`…_WithoutState_…`, `…_WithAForgedState_…`, `…_WithAReusedConnectState_…`, `…_WithAnExpiredConnectState_…`) e o `FederatedNoProviderTests` de hoje (o forjado sem provedor): `Expected /entrar?recusa=falha, Actual /account?recusa=falha` |
| G4 | um destino fora dos fixos | a falha do conectar seguindo um valor do pedido: o `AccountRefusal` usando o `retorno` da query como destino quando há | `TheFailureTarget_IsAlwaysOneOfTheFixed`: o retorno do conectar sem provedor com `&retorno=https://outro.example/` e com `&retorno=//outro.example` responde exatamente um destino da tabela da D3, relativo (começa por `/` e não por `//`) |
| G0.2a | o boot sem provedor fazendo pedido de rede | um aquecimento do discovery no boot: um `IHostedService` que, sem provedor, busca o discovery fixo do tipo Google (`https://accounts.google.com/.well-known/openid-configuration`) | `TheHostWithoutProvider_MakesNoNetworkRequest`: o contador de pedidos de saída do host sem provedor (um `IHttpMessageHandlerBuilderFilter` de teste em todo `HttpClient` da fábrica) espera `0` e recebe `1` |
| G0.2b | o boot sem provedor expondo rota nova | um endpoint novo do cliente: `SetPostLogoutRedirectionEndpointUris("auth/federated/logout-callback")` no `AddClient`, acrescentado também ao `IdentityIssuerEndpoints.ExpectedClient` e mapeado como rota anônima (para a checagem de boot não pegar antes: o guarda é a lista, e não a checagem) | `TheHostWithoutProvider_ExposesTheSameEndpoints`: a lista dos endpoints do host sem provedor difere da lista gravada na baseline (o caminho novo nomeado na mensagem) |
| G0.3a | a lista do guarda da #285 vazia | a fonte da lista do `NginxNavigationEndpointsTests` trocada por um `new OpenIddictClientOptions()` não composto (a forma nova da mutação 3.6 da #285, D0, P3) | os dois herdeiros reprovam pela asserção da lista não vazia |
| G0.3b | o defeito da #285 | o `location = /auth/federated/callback` tirado do `apps/frontend/deploy/nginx.conf` | os dois herdeiros reprovam, nomeando `/auth/federated/callback` |

**O caso 1 nos testes** precisa de dois hosts no **mesmo banco** e com o **mesmo anel**: o host com o provedor faz
o início e a autorização no `FakeGoogle`; o host **sem** provedor recebe a volta. É o que a homologação faz com o
reinício. Duplo novo: um `NoProviderSameDatabaseFixture` (nome de trabalho), derivado do `FederatedLoginFixture`
pela cadeia de conexão e pela chave do anel, sem a seção `Identity:FederatedProviders`. A forma exata (um
`WithWebHostBuilder` do fixture com provedor ou um segundo `WebApplicationFactory`) sai da tarefa 0.

**A lista dos endpoints do G0.2b** é gravada no teste a partir da baseline (o `origin/main`, na 0.4): o caso,
escrito primeiro no worktree da baseline sem asserção, imprime a lista; ela entra no teste da change como valor
esperado. Assim "antes e depois" é a mesma lista, comparada por código.

**Casos novos** (nomes de trabalho): o conectar, o login e o aceite sem provedor (três); o state ausente, o
forjado, o resgatado e o vencido do conectar (quatro, G3); o destino fixo (um, G4); a checagem de boot sem provedor
(um); o boot sem pedido de rede e os endpoints iguais (dois, G0.2); o `NginxNavigationEndpointsTests` no host sem
provedor (um herdeiro novo). O `TheReturn_WithNoProviderConfigured_IsFailure_ToTheLogin_WithoutSession` de hoje
**fica**: é o state forjado sem provedor, e continua indo ao `/entrar`. O vencido se monta pelo relógio do fixture: o
OpenIddict usa o `TimeProvider` do contêiner (`OpenIddictClientOptions.cs` l. 218–220; o vencimento conferido por ele,
`…Protection.cs` l. 684), e o `LocalUsersFixture` já registra o `FakeTimeProvider` (`Clock`, l. 21 e 32–33): avançar
11 min entre a autorização e a volta.

### D5. A conferência local, com o duplo do Google (no molde da 10.1 da #274)

`~/.cache/buteco-agents/bin/conferencia-281.sh`, com o lock das suítes; saídas em
`~/.cache/buteco-agents/281/conferencia/`.

1. **O banco descartável:** `buteco_agents_conf281` no `postgres_1` do compose, criado do `template0` (o `template1`
   tem a colação divergente, registro da #285), migrado pelo `dotnet ef` do worktree; nenhum outro banco tocado.
2. **O duplo e o `api`:** o pod da 10.1 da #274, sem mudança: a imagem do `api` do worktree e o
   `fake-google-274.py` (copiado para `fake-google-281.py`), `accounts.google.com` → `127.0.0.1` dentro do pod, a CA
   da conferência só no `SSL_CERT_FILE` do processo do `api` e no `--cacert` do `curl`.
3. **O caso 1, por `curl` com o pote de cookies** (`Origin` do painel): o Admin do bootstrap entra com senha; o
   `POST /account/federated/corporativo/connect` (`200 { url }` e o `buteco-fluxo-externo.<nonce>` no pote); a
   autorização no duplo devolve o `code` e o `state`. **Antes de voltar:** o `api` é parado e sobe de novo **sem** a
   seção do provedor, no mesmo banco. A volta com o pote. Conferir: `302 /account?recusa=falha&provedor=corporativo`;
   nenhum `Set-Cookie` de `buteco-sessao`; o `buteco-fluxo-externo.<nonce>` apagado; nenhuma linha nova em
   `user_federated_identities`; a linha do state com o status `redeemed`; a linha `1199` com `corporativo` e
   `sem_provedor_configurado`.
4. **O mesmo para o login** (`/entrar?recusa=falha&provedor=corporativo`) e para o aceite
   (`/convite?recusa=falha&provedor=corporativo`, com o convite pendente depois).
5. **O caso 2, por `curl`:** com o provedor de volta, a mesma URL de retorno do passo 3 aberta outra vez (o state já
   resgatado), uma com o `state` trocado por um valor aleatório e uma sem `state`: as três
   `302 /entrar?recusa=falha` (D2, N), sem sessão e sem vínculo. O vencido fica só nos testes (o relógio do fixture):
   na conferência, pediria esperar 10 min sem provar nada que o resgatado não prove (a mesma recusa na ordem 3).
6. **O log** do `api` sem o e-mail, o `sub`, o `code` e o `state` das contas do roteiro, procurados pelo texto na
   saída capturada.
7. **A limpeza conferida:** `DROP DATABASE buteco_agents_conf281`; o pod, o `api` e o duplo parados; a imagem da
   conferência e a CA apagadas; conferido por `\l`, `podman ps -a` e `podman images`. Resultado no `02`.

### D6. A homologação, depois do merge

Pela convenção 23, a homologação é depois do merge e não segura o archive nem o PR. **Ela mexe na configuração do
servidor e derruba o login pelo Google enquanto o provedor está fora** (alguns minutos; o login por senha continua).
**Decisão do dono, 17/11/2026 (Q3):** a reprodução é **no servidor de homologação**, numa janela que o dono combina,
e **nunca no de produção** (o piloto em `agente.butecandoespetobar.com.br`).

**O que o repositório diz sobre as duas máquinas:** não diz se são a mesma. O `docs/deployment.md` l. 347 fala em
"servidores de produção e de homologação", no plural; a preparação da #189 (13, passo 3) põe o
`federated-providers.env` "no servidor de homologação"; e o registro da homologação da M3 no `02` diz só "no
servidor". Nenhum endereço da homologação está registrado. **Antes do passo 2, o dono confirma que o servidor não é o
do piloto**; se for o mesmo, a reprodução não acontece ali e volta ao dono.

1. Com a imagem nova do `api` no servidor e o Google configurado, entrar no painel com senha (uma conta de teste
   **sem** Google conectado) e, em Minha conta, começar o conectar: o navegador vai à escolha de conta do Google.
   **Parar ali**, sem escolher a conta.
2. No servidor, tirar o provedor da configuração (comentar as variáveis
   `Identity__FederatedProviders__<chave>__*` do arquivo de ambiente do `api`) e reiniciar só o `api`
   (`docker compose -f docker-compose.prod.yml up -d api`). Conferir no log do boot que ele subiu.
3. Voltar ao navegador, dentro dos 10 min do início, e escolher a conta.
4. **Conferir:** o navegador em `/account`, com "Não foi possível conectar a conta Google. Tente de novo." (o aviso
   da #279); a pessoa continua logada; a conta sem Google conectado; no log do `api`, a linha `1199` com a chave e
   `sem_provedor_configurado`, sem o e-mail nem o `sub` da conta de teste.
5. **Restaurar:** as variáveis de volta, o `api` reiniciado, e um conectar inteiro dando certo
   (`/account?conectado=<chave>`), depois desconectado. Conferir que o login pelo Google voltou para todos.

O caso 2 não precisa da homologação: com a N, ele não muda.

### D7. O tamanho (convenção 18) e as suítes

**Só o `apps/api`.** O painel não muda: o `returnNotice` lê só a `recusa` (`features/account/federatedReturn.ts`
l. 18–24) e o texto da `falha` é fixo (`accountFederatedTexts.ts` l. 24, `FALHA_AO_CONECTAR`), sem depender da lista
de provedores, que sem provedor vem vazia. O `/account?recusa=falha&provedor=<chave>` é o que a #279 já trata.

**Âncora:** a #276, dentro da `aceite-convite-google` (o `FederatedMissingRegistrationHandler` e os quatro casos do
`FederatedRemovedProviderTests`), a mais parecida: um defeito do retorno, os casos com o duplo, sem tela.

| componente | produção | testes | casos |
|---|---|---|---|
| o `AddClient` sem a condição, e o comentário do porquê | ~+10 / −6 | — | — |
| a rota: o desvio fora, o log do catálogo vazio | ~+6 / −6 | — | — |
| a checagem de boot do cliente | ~+3 / −3 | — | — |
| comentários que citam "o cliente só existe com provedor" | ~+4 / −4 | — | — |
| o host sem provedor no mesmo banco (**duplo novo**) | — | ~+40 | — |
| o contador de pedidos de saída (**duplo novo**) | — | ~+25 | — |
| o caso 1 (conectar, login, aceite) | — | ~+75 | 3 |
| o caso 2 (ausente, forjado, resgatado, vencido) | — | ~+80 | 4 |
| o destino fixo | — | ~+30 | 1 |
| a checagem de boot sem provedor | — | ~+15 | 1 |
| o boot sem rede e os endpoints iguais (G0.2), com a lista da baseline | — | ~+55 | 2 |
| o `NginxNavigationEndpointsTests` em dois herdeiros | — | ~+15 / −5 | 1 |
| **total** | **~+23 / −19** | **~+335 / −5** (dos quais ~65 em duplo) | **12** |

Projeção feita no design, **antes** do apply e da prova da tarefa 0: pela convenção 18, é rascunho. Os duplos vão
à parte (a régua da `projecao-conv18-conta-duplos`: o erro grande de linhas vinha deles). A produção é quase toda
comentário de mecanismo (o porquê do cliente sem registração), ~1 : 2 entre lógica e comentário; o teste é quase todo
arranjo, ~0,5 : 1. A sonda da D0 não conta: é descartada.

**Suítes**, uma por vez, com o lock, baseline no `origin/main` (`0bfa1cf`) num worktree próprio: `api`,
`rt-connectors` (o round-trip compõe o host do `apps/api` com o emissor) e `frontend` (o `nginxHeaders.test.ts` lê o
`nginx.conf`, que não muda: roda para provar que nada mudou ali). Os casos que mudam de resultado esperado:
**nenhum** dos de hoje. O `FederatedNoProviderTests` continua `/entrar` (é o state forjado); o
`NginxNavigationEndpointsTests` muda de forma (uma base com dois herdeiros) e mantém o nome do caso no herdeiro com
provedor; a spec muda o cenário "Nenhum provedor configurado no retorno do conectar", que não tinha caso próprio.

## Árvore de pastas proposta

```
apps/api/
├── src/Buteco.Api/Identity/
│   ├── IdentityIssuerServiceCollectionExtensions.cs   (o AddClient sem a condição)
│   ├── IdentityIssuerStartupValidation.cs             (a checagem do cliente sem o catálogo)
│   └── Federated/
│       ├── FederatedLoginEndpoints.cs                 (o desvio fora, o log)
│       └── FederatedMissingRegistrationHandler.cs     (só o comentário)
└── tests/Buteco.Api.Tests/
    ├── NginxNavigationEndpointsTests.cs               (uma base, dois herdeiros)
    ├── Support/
    │   ├── NoProviderSameDatabase.cs                  (novo: o host sem provedor no mesmo banco)
    │   └── OutboundRequestCounter.cs                  (novo: o contador de pedidos de saída)
    └── Identity/
        ├── Federated/
        │   ├── FederatedRemovedProviderTests.cs       (o caso 1, ao lado dos da #276)
        │   └── FederatedUnreadableStateTests.cs       (novo: o caso 2 e o destino fixo)
        ├── FederatedClientWithoutProviderTests.cs     (novo: o boot sem rede e os endpoints iguais)
        └── IdentityIssuerStartupValidationTests.cs    (a checagem sem provedor)
openspec/specs/federated-login/spec.md                 (delta)
openspec/specs/federated-providers/spec.md             (delta)
openspec/specs/server-deployment/spec.md               (delta: o guarda da #285 nos dois hosts)
```

Nomes de arquivo de teste são de trabalho.

Nada em `libs/`.

## O que o apply mudou (convenção 9)

Nenhuma decisão mudou: a A passou nas três premissas, e a N, a tabela dos destinos e os guardas ficaram como
aprovados. Registrado:

- **A tarefa 0 (sonda, 18/11/2026; `~/.cache/buteco-agents/281/sonda/`).**
  - **P1 passou.** Os três fluxos começaram com o provedor; o `api` da sonda reiniciou sem a seção, no mesmo banco;
    as três voltas deram `/account…`, `/entrar…` e `/convite…` com `&provedor=corporativo`, sem `buteco-sessao`,
    com o cookie do fluxo apagado e sem cookie novo. Os state tokens foram de `valid=3` a `redeemed=3`, nenhum
    vínculo, o convidado `invited`, e nenhum `ID2004`/`ID2019`/`ID2139` nem erro de decifração no log. A linha `1199`
    da sonda dizia `invalid_request` (a sonda não mudou o log); a correção passou a dizer `sem_provedor_configurado`.
  - **P2 passou, com uma diferença explicada.** Nas duas imagens, sem a seção e com o proxy morto: zero linhas do
    `HttpClient`, nenhum `HttpRequestException`, a checagem de boot passou, o `providers` vazio. A lista de 22
    caminhos diferiu só no corpo do `/.well-known/jwks`. Refeita com dois boots de cada imagem: só o `x5c` e o `x5t`
    mudam, **a cada boot, também na imagem do `origin/main`** (o certificado gerado no boot a partir da mesma chave);
    `kid`, `n`, `e`, `alg` e `use` iguais nos quatro, e o discovery idêntico. Não é mudança da A.
  - **P3 passou.** Com a A, a mutação antiga da 3.6 da #285 (o `LocalUsersFixture` no lugar do
    `FederatedLoginFixture`) **aprovou: deixou de pegar**, como previsto. A forma nova (a fonte da lista vazia)
    reprovou pela l. 30 nos dois hosts.
- **Um caso existente não previsto mudou de arranjo (erro do design).** A D7 dizia que nenhum caso de hoje mudaria
  de resultado. O `IdentityIssuerEndpointsPassthroughTests.WithBothRoutesAnonymousAndClassified_Passes` monta um
  `WebApplication` mínimo com o registro real do emissor e só o authorize e o logout mapeados; com o cliente OIDC
  registrado sempre, a checagem passou a cobrar a rota do retorno, e o caso reprovou na primeira suíte de fechamento
  ("'/auth/federated/callback' é endpoint do cliente OIDC em passthrough e não tem rota mapeada…"). Foi parada, e o
  dono aprovou (18/11/2026) mapear também o `FederatedLogin.CallbackPath` como anônima `AuthEntryPoint`, como o
  `Program.cs`. O nome e a asserção ficaram iguais; o caso entrou na lista do topo como "muda o arranjo: o cliente
  OIDC existe sempre". Faltou ao design procurar, entre os testes, os que montam o registro do emissor sem o
  `Program.cs`.
- **G1b reprovou por outro caminho.** A tasks previa o `500` do esquema ausente. Com a checagem de boot do cliente
  agora incondicional (1.2), o defeito derruba o boot do host sem provedor antes de qualquer pedido: "'/auth/federated/callback'
  está na lista esperada do cliente e não corresponde a nenhum endpoint configurado no cliente OIDC", no caso do
  conectar sem provedor e no `RealComposition_Boots_WithTheRealRoutesAndEndpoints`. A causa é o defeito nomeado; a
  defesa é mais cedo que a prevista.
- **G3 reprovou também o login sem provedor.** A tasks previa os casos da 2.3 aprovando. O login e a intenção
  desconhecida dividem o ramo padrão do `FailureRefusal` (a tabela da D3 manda os dois ao `/entrar`), e o defeito
  mandou os dois ao `/account`. O conectar e o aceite aprovaram.
- **Os duplos.** Além dos dois previstos (o host sem provedor no mesmo banco e o contador de pedidos de saída), o
  apply precisou do `HostEndpointList` (a lista dos endpoints, a mesma na baseline e na change) e de um gancho no
  `ApiFactoryFixture` (`SharedDatabaseConnectionString`, o banco de outro host, sem subir nem apagar contêiner).
- **O reference id do state** fica gravado ofuscado (`OpenIddictTokenManager.ObfuscateReferenceIdAsync`): os casos
  leem o status da linha pelo gerente do OpenIddict, e não pela coluna.

## Risks / Trade-offs

- **[A instalação sem provedor ganha o cliente OIDC no pipeline]** → o `OpenIddictClientAspNetCoreHandler` roda em
  todo pedido para inferir o endpoint, como já roda em toda instalação com Google; o `FederatedRequestUriHandler`
  também. Sem registração, nenhum início é possível (o `start` responde `404` pelo catálogo, antes do desafio), e o
  login por senha não passa pelo cliente. A suíte inteira roda com a maioria dos hosts sem provedor: é ela que mede,
  junto da P2 da D0 e dos dois casos do G0.2.
- **[O cliente sem registração depende do anel no boot]** → as opções do cliente pedem as credenciais (ID0357 e
  ID0358) com o endpoint de retorno ligado; o `IdentityKeyRingClientOptionsConfigurer` as entrega do snapshot, e o
  `IdentityKeyRingGate` segura os pedidos até a primeira carga, como para o servidor. A P1 da D0 prova no reinício.
- **[O guarda da #285 perde a mutação dele]** → ajustado na P3 da D0, sem afrouxar: dois herdeiros e a mutação na
  fonte da lista.
- **[A spec viva diz o contrário]** → o cenário "Nenhum provedor configurado no retorno do conectar" exige hoje o
  `/entrar` "sem ler o state". A delta o substitui; não há caso de teste que o afirme para o conectar.
- **[O caso 2 continua no `/entrar`]** (com a N) → aceito e escrito; a pessoa logada que conectava lê o aviso do
  login. Sem dano.

## Migration Plan

Só a imagem do `api`, sem configuração nova e sem migration. Rollback: a imagem anterior.

## As perguntas, decididas pelo dono (17/11/2026)

- **Q1. O aceite de convite sem provedor:** vai ao `/convite?recusa=falha&provedor=<chave>`, como na chave trocada.
  A D3 passa a falar em destinos fixos e lista os três (`/entrar`, `/account`, `/convite`). Descartada: manter o
  aceite no `/entrar`, que exigiria um ramo só para conservar o defeito.
- **Q2. O caso 2:** a N. A intenção desconhecida vai ao `/entrar?recusa=falha`, escrito como regra, com um cenário
  para cada sub-caso (ausente, forjado, vencido e já usado). Descartadas: L, K, U (D2).
- **Q3. A homologação do caso 1:** no servidor de homologação, numa janela que o dono combina, nunca no de produção
  (D6). A A ficou condicionada às três premissas da D0.

## Open Questions

- **O servidor de homologação é a máquina do piloto?** O repositório não diz (D6). Não bloqueia o apply nem o PR;
  bloqueia só o passo 2 da D6, que espera a confirmação do dono.
