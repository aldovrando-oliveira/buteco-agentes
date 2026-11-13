## Context

A #189 abre a M3 (#196, "Autenticação com Google"). Pela Q10 (decisão do dono, 13/11/2026), esta change é
a **189a**; o aceite de convite pelo Google é a **#274**, e a #190 (as telas) vem depois das duas. Depende
da #181 (convites) e da #184 (papéis), as duas no `main`.

**Fontes lidas** (12/11/2026, `3de31dd`, merge do PR #273):
- o corpo e os comentários da #189, inclusive o registro da #180 (cookie do fluxo externo) e o da #217
  (`iss`);
- as specs vivas `issuer-user-login`, `oidc-issuer`, `local-users`, `issuer-token-storage`,
  `identity-issuer-overview`, `user-access-links`, `user-account`, `user-management`,
  `external-applications`, `two-factor-totp`, `route-roles`, `route-authentication`,
  `panel-account-page`, `panel-users-page`, `panel-user-detail-page`, `panel-identity-page`;
- o `01`, seção "Autenticação" (l. 525–987);
- o fonte do OpenIddict 7.7.2 (`~/.cache/buteco-agents/182-fonte/openiddict-core`, tag `7.7.2`);
- o draft-ietf-httpbis-rfc6265bis-22 (01/12/2025), §5.2, §5.6.7.1 e §5.7;
- o protótipo, canvas `H7dPBZafiSkdKd1hqesPZ6`, **versão `1794522896-00ec`**, pela ferramenta Artifact.

### O código em `3de31dd`

- **O painel conhece um emissor só.** `apps/frontend/src/auth/oauth.ts` l. 7–15: o cliente
  `buteco-painel`, os endpoints relativos e o `redirect_uri` `<origem>/entrar/retorno`; "O painel não lê
  o discovery" (l. 4). O `beginLogin` (`loginFlow.ts` l. 50–70) monta o authorize do Buteco.
- **A sessão do emissor** é o `buteco-sessao` (`Identity/Flow/IssuerSession.cs` l. 37–39 e 97–123):
  `HttpOnly`, `SameSite=Strict`, 5 min, com o id, o stamp e, completo, `buteco_2fa=ok`.
- **O authorize só emite código para o cookie completo de quem tem a verificação ligada**
  (`AuthorizeEndpoint.cs` l. 89–104), e **a troca do código e toda renovação recusam quem não a tem**
  (`UserGrantTokenHandler.cs` l. 78). Esta change não toca nenhuma das duas (Q1).
- **O segundo passo e o cadastro leem o usuário só do cookie** (`SessionEndpoints.cs` l. 199–209 e
  334–345): servem igual ao cookie pendente gravado pelo retorno do Google.
- **`CallerPassword`** (`Users/AccessLinks/CallerPassword.cs` l. 18–29) confere a senha gravada e recusa
  sempre quem não tem senha. É chamado em seis lugares: `InvitationEndpoints.cs` l. 96 e 156,
  `PasswordResetEndpoints.cs` l. 79, `UserRoleEndpoint.cs` l. 79, `ExternalApplicationEndpoints.cs`
  l. 153 e 348.
- **O `TwoFactorVerifier`** (`Users/TwoFactor/TwoFactorVerifier.cs` l. 39–75) aceita no mesmo campo o
  código do app (TOTP, com o `UPDATE` condicional do último passo) e o de recuperação.
- **A checagem de boot do emissor** (`Identity/IdentityIssuerStartupValidation.cs`) lança
  `InvalidOperationException` com a mensagem que diz o que fazer, sem banco; os endpoints do OpenIddict
  têm a lista esperada própria (`IdentityIssuerEndpoints.Expected`), conferida nos dois sentidos.
- **As rotas anônimas de entrada** são conferidas por nome no `RouteAuthenticationTests`
  (`TheAuthEntryPoints_AreTheKnownOnes`, l. 106–131) e pela allowlist do `Program.cs` l. 269–278.
- **O cliente do OpenIddict 7.7.2:**
  - o cookie de correlação é `OpenIddict.Client.State.<nonce>`, `SameSite=None` por padrão
    (`OpenIddictClientAspNetCoreOptions.cs` l. 77–84), e leva **só** o valor anti-falsificação
    (`OpenIddictClientAspNetCoreHandlers.cs` l. 790–825); o resto vai no **state token**, cifrado e
    assinado com as credenciais do cliente, 15 min por padrão (`OpenIddictClientOptions.cs` l. 78);
  - o state token é **de uso único** pela entrada em `openiddict_tokens` (`RedeemStateTokenEntry`,
    `OpenIddictClientHandlers.cs` l. 812), salvo `DisableTokenStorage`;
  - o modo de resposta padrão é `query`, "compatível com `SameSite=Lax`" (`…AspNetCoreHandlers.cs`
    l. 676–680);
  - o issuer do id token é conferido contra o do discovery, com e sem a barra final
    (`OpenIddictClientHandlers.Protection.cs` l. 138–168);
  - **para o Google, a conferência do parâmetro `iss` da resposta (RFC 9207) é desligada** quando o
    discovery não anuncia o suporte (`OpenIddictClientWebIntegrationHandlers.cs` l. 94–104,
    openiddict-core#2428); o provedor fixa o issuer `https://accounts.google.com/`
    (`OpenIddictClientWebIntegrationProviders.xml` l. 1114–1116).

## Goals / Non-Goals

**Goals:** o login pelo Google atrás do emissor, com o código do Buteco; a seção de configuração e a
checagem de boot; o vínculo (`issuer`, `sub`), conectar e desconectar; as recusas sem sessão; o
`CallerPassword` com o código para quem não tem senha; as rotas dos provedores; testes sem o Google real.

**Non-Goals:** o aceite de convite pelo Google (#274); telas (#190); SSO corporativo, OIDC genérico,
aplicações federadas e token exchange (M4); cadastro de provedores pelo portal; entrada automática;
definir a primeira senha sem link do Admin; envio de e-mail.

## Decisions

### D1. (a) A arquitetura: o Google atrás do emissor do Buteco

O `apps/api` é cliente OIDC do Google; o painel continua falando só com o emissor do Buteco. O login:

1. o painel, no `/entrar?retorno=<authorize>`, chama `POST /auth/federated/{chave}/start` (JSON, `Origin`,
   as barreiras do login, no limite global) com `{ "retorno": "<authorize>" }`;
2. o `apps/api` monta o desafio do `OpenIddict.Client` para a registração da chave, grava o cookie do
   fluxo (D4) e responde `200 { "url": "https://accounts.google.com/…" }`; o painel navega para a URL;
3. o Google volta a `GET /auth/federated/callback?code=…&state=…` (o redirection endpoint do cliente, em
   passthrough, anônimo, `AuthEntryPoint`); o cliente confere o state token, a correlação, o PKCE, o nonce,
   a assinatura e o issuer do id token, e entrega à rota o principal do Google;
4. a rota decide (D7). Se o Buteco deixa entrar, grava o `buteco-sessao` **pendente** e responde
   `302 /entrar?federado=<chave>&segundoPasso=totp|cadastro[&retorno=<authorize>]`; senão,
   `302 /entrar?recusa=<motivo>&provedor=<chave>`, sem cookie de sessão;
5. o painel, na própria origem, segue o caminho de hoje (D5).

**Descartado: o painel falar com o Google.** Discovery do Google no SPA, `client_id` do Google num
cliente público e um endpoint novo para trocar o id token do Google por um token do Buteco.

**Biblioteca: `OpenIddict.Client` + `WebIntegration` 7.7.2**, e não o `AddOpenIdConnect`: mesma família e
versão do servidor; state token de uso único; as credenciais do cliente saem do mesmo anel do emissor (um
`IConfigureOptions<OpenIddictClientOptions>` no molde do `IdentityKeyRingOptionsConfigurer`, com o mesmo
`IOptionsChangeTokenSource`), sem chave nova; e o WebIntegration já trata o desvio do `iss` do Google. A
registração usa `ProviderName` = a chave neutra de quem instala.

**O comentário da #217 é premissa falsa com esta arquitetura** (D14).

**Onde o `iss` do Google é conferido, no `apps/api`:**
- **o `iss` do id token**, sempre, contra o issuer do discovery (`OpenIddictClientHandlers.Protection.cs`
  l. 138–168). Um id token com `iss` `accounts.google.com`, sem esquema, é **recusado** (`falha`): lado
  seguro, e item da conferência manual;
- **o parâmetro `iss` da resposta** fica desligado pelo WebIntegration enquanto o Google não anunciar o
  suporte (l. 94–104). Na M3 não é lacuna: só existe um provedor por issuer (D3, regra 6), e o state token
  amarra a resposta à registração que a pediu. Na M4 volta a importar;
- **a chave do vínculo** é o issuer **fixo do tipo**, nunca o `iss` lido do token.

### D2. (b) O formato de `Identity:FederatedProviders`

```json
"Identity": {
  "FederatedProviders": {
    "corporativo": {
      "Type": "google",
      "ClientId": "1234567890-abc.apps.googleusercontent.com",
      "HostedDomain": "empresa.com"
    }
  }
}
```

| campo | | regra |
|---|---|---|
| a chave | obrigatória | `^[a-z][a-z0-9-]{1,31}$`; vai nas rotas e no motivo de recusa |
| `Type` | obrigatório | só `google` na M3 |
| `ClientId` | obrigatório | não vazio, sem espaço; não é segredo |
| `ClientSecret` | obrigatório | **só** por `Identity__FederatedProviders__<chave>__ClientSecret` |
| `HostedDomain` | opcional (Q8) | o claim `hd` do id token tem de ser igual, em todo retorno; filtro, nunca entrada automática |

**Fixo pelo tipo:** issuer `https://accounts.google.com/`; nome "Google" e o botão pela marca; escopos
`openid email profile`; `prompt=select_account`; `response_mode=query`; uso "login de usuários".
**Desligar** é tirar a entrada e reiniciar; o vínculo fica gravado e para de valer (D6).

### D3. (c) A checagem de boot

`ValidateFederatedProvidersConfiguration`, depois do `Build()`, sem banco e sem rede. Seção ausente ou
vazia é válida.

| # | inconsistência | mensagem |
|---|---|---|
| 1 | chave fora do formato | `Identity:FederatedProviders:<chave>: a chave precisa ter de 2 a 32 caracteres, minúsculas, dígitos e hífen, começando por letra (por exemplo corporativo).` |
| 2 | `Type` ausente | `Identity:FederatedProviders:<chave>:Type não configurado: o tipo é obrigatório (na M3, só google).` |
| 3 | `Type` desconhecido | `Identity:FederatedProviders:<chave>:Type '<valor>' desconhecido: os tipos aceitos são google.` |
| 4 | `ClientId` ausente ou em branco | `Identity:FederatedProviders:<chave>:ClientId não configurado: use o ID do cliente OAuth criado no provedor.` |
| 5 | `ClientSecret` ausente ou em branco | `Identity:FederatedProviders:<chave>:ClientSecret não configurado: defina a variável Identity__FederatedProviders__<chave>__ClientSecret, nunca no arquivo de configuração.` |
| 6 | dois provedores com o mesmo issuer | `Identity:FederatedProviders: '<a>' e '<b>' usam o mesmo issuer (<issuer>): o vínculo é pelo par (issuer, sub), e dois provedores com o mesmo issuer o tornariam ambíguo.` |
| 7 | `ClientSecret` vindo de arquivo JSON | `Identity:FederatedProviders:<chave>:ClientSecret veio de um arquivo de configuração: o segredo só é aceito por variável de ambiente.` |
| 8 | campo desconhecido | `Identity:FederatedProviders:<chave>:<campo> desconhecido: os campos do tipo google são Type, ClientId, ClientSecret e HostedDomain.` |
| 9 | `HostedDomain` fora do formato | `Identity:FederatedProviders:<chave>:HostedDomain '<valor>' inválido: use só o domínio, sem @ (por exemplo empresa.com).` |
| 10 | `ClientSecret` igual a uma chave ou segredo do `apps/api` | `Identity:FederatedProviders:<chave>:ClientSecret igual a <nome>: o segredo do provedor precisa ser distinto das chaves e dos segredos do apps/api.` (APPLY-2) |

A 7 percorre os provedores do `IConfigurationRoot` e reprova quando o provedor que fornece o valor
efetivo é um `JsonConfigurationProvider`. A 8 pega erro de digitação (`ClientID`), que o binder ignoraria.

**A convenção 8 nos endpoints do cliente:** o redirection endpoint `/auth/federated/callback` entra na
lista esperada (`IdentityIssuerEndpoints.Expected`), com passthrough e o motivo `AuthEntryPoint`,
conferido nos dois sentidos contra as URIs configuradas no **cliente**, como o authorize e o logout são
contra as do servidor.

### D4. (d) O cookie do fluxo externo

| | |
|---|---|
| **nome** | `buteco-fluxo-externo.<nonce>` (o `CookieBuilder` do cliente com o nome trocado; um por fluxo) |
| **SameSite** | **`Lax`** |
| **outros** | `HttpOnly`; `Secure` fora de `Development`; `Path=/auth/federated/callback`; sem `Domain` |
| **vida** | **10 min**, a do state token (`SetStateTokenLifetime`) |
| **carrega** | **só** o valor anti-falsificação, que o state token também leva |
| **apagado** | no retorno, aceito ou recusado; se a pessoa não volta, vence |

**O state token** (cifrado e assinado pelo anel, uso único pela entrada em `openiddict_tokens`) carrega o
nonce, o `code_verifier`, a registração, o vencimento e as propriedades do Buteco: a **intenção**
(`login`, `conectar`), o `retorno` (já conferido pelo prefixo `/connect/authorize?`) e, no conectar, o id e
o stamp de quem conecta.

**Por que `Lax`:** o retorno é uma navegação de topo iniciada em `accounts.google.com`. Com `Strict` o
navegador não manda o cookie (§5.6.7.1: "Same-site cookies in "Strict" enforcement mode will not be sent
along with top-level navigations which are triggered from a cross-site document context"), e o cliente
recusa por falta da correlação (`ID2129`). `None` só seria preciso com `form_post`, fixado fora.

**Por que não abre CSRF:** o cookie só vai ao caminho do retorno e só em `GET` de topo; o retorno exige um
state token do Buteco cujo valor bata com o do cookie; trocar o código ou o state falha no PKCE e no nonce;
o início é `POST` JSON com `Origin` conferida, e outro site não começa um fluxo no navegador da vítima.

### D5. O login pelo Google pede o código do Buteco (Q1, decisão do dono, 13/11/2026)

O retorno grava o `buteco-sessao` **pendente** (`IssuerSession.SignInAsync(…, twoFactorDone: false)`) e
manda a `segundoPasso=totp` (com a verificação ligada) ou `segundoPasso=cadastro` (sem ela). Daí em
diante, as rotas de hoje (`/auth/session/second-factor`, `/auth/session/enrollment/*`). O authorize, a
troca do código, a renovação e o "obrigatória para todos" do `01` não mudam. Quem só tem Google cadastra
a verificação no primeiro login. O último acesso continua gravado no segundo passo e no `confirm`.

**Como o `buteco-sessao` (Strict) atravessa a cadeia cross-site** (vai para a spec `issuer-user-login`, e a
conferência manual em Chrome, Firefox e Safari o confirma, D11):

1. **gravado na resposta do retorno.** O `GET /auth/federated/callback` é uma navegação de topo, e o
   draft-ietf-httpbis-rfc6265bis-22 §5.7, passo 18.3, deixa gravar cookie de qualquer `SameSite` nela:
   "If the cookie was received from a request which is navigating a top-level traversable … skip the
   remaining substeps and continue processing the cookie", com a nota "Top-level navigations can create
   a cookie with any `SameSite` value". O que o §5.7 proíbe é gravar `Lax`/`Strict` em resposta a
   subrecurso cross-site (§4.1.2.7), que não é o caso;
2. **não vai no carregamento do `/entrar`** que o `302` provoca: esse pedido ainda é da navegação que
   começou no Google, e o `Strict` não acompanha (§5.6.7.1). Não importa: o `/entrar` é o `index.html`,
   que não lê sessão;
3. **vai no `POST /auth/session/second-factor`** (e no `setup`/`confirm`), porque o pedido parte do
   `fetch` do documento do painel, cujo "site for cookies" é a origem do painel (§5.2.1: "For a document
   displayed in a top-level traversable, the document's "site for cookies" is the top-level origin"), e a
   URL é da mesma origem: o pedido é same-site (§5.2), e o `Strict` vai;
4. **vai no authorize** depois do código, porque a navegação ao `retorno` é iniciada pelo próprio
   documento do painel (`location.assign`), same-site, como no login por senha de hoje.

Por isso o retorno **nunca** manda direto ao `/connect/authorize`: o `302` ao authorize ainda seria da
cadeia cross-site, e o `Strict` não iria.

### D6. (e) O vínculo pelo par (`issuer`, `sub`)

**A tabela `user_federated_identities`:** `id` (uuid, PK), `user_id` (FK `users`, `ON DELETE CASCADE`),
`issuer`, `subject`, `email` (o último e-mail confirmado; só exibição), `connected_at`, `last_used_at`.
**`UNIQUE (issuer, subject)`** e **`UNIQUE (user_id, issuer)`**; a colisão em corrida vira a recusa
`em-uso` pelo `23505`.

**O e-mail muda no Google:** nada muda no vínculo; o login seguinte acha pelo par e regrava `email` e
`last_used_at`. O e-mail do usuário no Buteco não é tocado.

**Vínculo de provedor fora da configuração** (a chave saiu, ou nenhuma chave tem aquele issuer): fica
gravado, aparece nas formas de entrada com `disponivel: false`, não entra e não conta para a última forma.

**Conectar** (`POST /account/federated/{chave}/connect`, access token, todo papel), **com a confirmação de
quem chama (Q3)**: `senha` para quem tem senha, `codigo` do app para quem não tem (D8). Resposta
`200 { url }`, como o `start`. O retorno grava o vínculo para o id do state token se o usuário continuar
ativo e com o mesmo stamp, e responde `302 /account?conectado=<chave>`, ou `302 /account?recusa=<motivo>&provedor=<chave>`
com `em-uso` (a conta Google já é de outro usuário), `ja-conectado` (a pessoa já tem vínculo nesse issuer),
`sessao-expirada` (desativada ou stamp trocado no meio), `dominio-nao-permitido` ou `falha`. O e-mail do
Google não precisa ser o do Buteco: quem está autenticada prova as duas contas.

**Desconectar:** `DELETE /account/federated/{chave}` (a própria pessoa) e `DELETE /users/{id}/federated/{chave}`
(o Admin, pelo papel gravado; a si mesmo, `403` "Para desconectar a sua própria conta, use Minha conta.").
Sem vínculo, `404`. **A última forma de entrada não sai:** senha gravada mais vínculos de provedor
disponível; ficar com zero → `409` "A última forma de entrada não pode ser removida.", sob
`SELECT … FOR UPDATE` na linha do usuário. Não pede senha.

**O stamp e as sessões (Q2):**

| caso | stamp | sessões |
|---|---|---|
| conectar | não troca | ficam |
| desconectar pela própria pessoa | não troca | ficam |
| desconectar pelo Admin | **troca** | **revogadas** na mesma transação (`UserSessions`) |
| e-mail mudou no Google | não troca | ficam |

### D7. (f) As recusas

Todas: **nenhum `buteco-sessao`**, o cookie do fluxo apagado, `302 /entrar?recusa=<motivo>&provedor=<chave>`
e uma linha `Warning` `FederatedLoginRefused` (EventId 1198) com a chave, o motivo e o id do usuário
quando há um. **Nunca** e-mail, `sub`, código ou token no log.

**A ordem**, a primeira que casa decide:

| # | motivo | condição | texto de referência (protótipo, a #190 ajusta) |
|---|---|---|---|
| 1 | `dominio-nao-permitido` | `HostedDomain` configurado e `hd` diferente ou ausente | "Esta conta Google não é do domínio permitido neste painel." |
| 2 | `desativado` | vínculo pelo par com usuário desativado | "Acesso desativado" / "Seu acesso ao Buteco Agentes foi desativado. Fale com o administrador do painel." |
| — | (entra) | vínculo pelo par com usuário ativo | — |
| 3 | `email-nao-verificado` | sem vínculo, `email_verified` diferente de `true` | "O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou com e-mail e senha." |
| 4 | `convite-pendente` | sem vínculo, convidado com o e-mail verificado | "Você tem um convite pendente" / "Abra o link do convite que você recebeu…" |
| 5 | `nao-conectada` | sem vínculo, usuário ativo com o e-mail verificado | "Esta conta Google ainda não está conectada" / "Entre com seu e-mail e senha e conecte o Google em Minha conta…" |
| 6 | `sem-acesso` | sem vínculo, nenhum usuário ativo ou convidado com o e-mail (o desativado sem vínculo também cai aqui: o "Acesso desativado" é só para quem já estava vinculado) | "Conta sem acesso" / "O Google confirmou `<e-mail>`, mas essa conta não tem acesso ao Buteco Agentes…" |
| 7 | `falha` | o cliente recusou a resposta (correlação, state vencido ou reusado, PKCE, nonce, assinatura, `iss`, `aud`), a pessoa cancelou no Google, ou a registração do state não existe mais | "Não foi possível entrar com o Google. Tente de novo." — o `error` do OpenIddict vai ao log `FederatedLoginFailed` (1199), sem o `error_description` do provedor |

O `start` com chave fora da configuração responde `404` "Provedor não configurado." O bloqueio por e-mail
não conta recusa federada; o limite global também não.

**O e-mail confirmado (Q6):** as recusas 4, 5 e 6 gravam o cookie **`buteco-recusa`**:

| | |
|---|---|
| **conteúdo** | `{ motivo, provedor, email }`, protegido pelo Data Protection (propósito `Buteco.FederatedRefusal`) |
| **atributos** | `HttpOnly`; `SameSite=Strict`; `Secure` fora de `Development`; `Path=/auth/federated/refusal`; sem `Domain` |
| **vida** | 2 min (`Max-Age=120`) |
| **gravado** | na resposta do retorno, junto do `302` (a mesma regra do §5.7, passo 18.3) |

O painel o lê por **`POST /auth/federated/refusal`** (anônima, `AuthEntryPoint`, as barreiras do login:
JSON e `Origin`), chamado do `/entrar` por `fetch`, same-site:
- cookie válido e no prazo → `200 { "motivo", "provedor", "email" }`, e a resposta apaga o cookie (uso único);
- sem cookie, vencido, adulterado ou já lido → `404` **vazio**, igual nos quatro casos, e apaga o que houver.
  A rota não revela nada sem o cookie, nem se houve recusa.

É `POST` porque apaga: um `GET` com efeito seria disparado por pré-carregamento. O motivo também vai na
URL (não é dado pessoal); o e-mail, nunca.

### D8. O `CallerPassword` com o código para quem não tem senha (Q1)

As seis chamadas (convite, novo link, redefinição, promoção a Admin, criar aplicação, gerar segredo) e o
conectar (D6) passam por uma confirmação só:

- **quem tem senha** confirma com `senha`, como hoje. Um `codigo` sem `senha` responde `401`
  "Senha inválida." e conta no bloqueio: o código **não** substitui a senha de quem a tem;
- **quem não tem senha** confirma com `codigo`, **só o código do app autenticador** (o TOTP, com o
  `UPDATE` condicional do último passo, que impede o reuso). Ausente, errado, já usado ou com o formato
  de código de recuperação → `401` "Código inválido.", contado no bloqueio. Segredo que não decifra →
  `500` "Não foi possível conferir o código do app autenticador. Fale com o administrador.", com o log
  1194 que já existe;
- **o `423` e a contagem** são os de hoje: o bloqueio do e-mail de quem chama vem antes e não confere
  nada; o acerto não zera.

**Só o código do app, sem o de recuperação** (recomendação): o código de recuperação é o estoque de
emergência para o celular perdido, e gastá-lo para criar uma aplicação ou promover alguém o consome no
uso errado, sem aviso. Quem perdeu o celular tem o caminho da redefinição das 2 etapas por outro Admin ou
o comando de emergência. Alternativa descartada: aceitar os dois (o de recuperação viraria senha de uso
único para ações administrativas).

**Na 189a, nenhuma rota de produção cria usuário ativo sem senha**: o caminho é o aceite pelo Google
(#274). A regra entra aqui porque o vínculo e a última forma de entrada já a pressupõem; os testes
montam o usuário sem senha direto no banco.

### D9. (i) As rotas dos provedores e as formas de entrada

`GET /identity/federated-providers`, só Admin (RoleRoutes; o papel gravado pela `CurrentUser`), sem
segredo nem `HostedDomain`:

```json
{ "provedores": [
  { "chave": "corporativo", "tipo": "google", "nome": "Google", "issuer": "https://accounts.google.com/",
    "usadoPara": ["login"], "clientId": "1234…apps.googleusercontent.com",
    "uriDeRetorno": "https://agentes.exemplo.com/auth/federated/callback", "vinculos": 1 } ] }
```

`GET /auth/federated/providers`, anônima: `{ "provedores": [{ "chave", "tipo" }] }`, na ordem da chave.

**As formas de entrada.** O `formasDeEntrada` **já existe** em `GET /users` e `GET /users/{id}`, como
lista de strings (`["senha"]`, vazia sem senha; spec `user-management`, "Lista de usuários"), e o painel o
lê. **O formato não muda**: a lista ganha `"google"` quando há vínculo com um provedor disponível do tipo,
depois de `"senha"`, sem repetir. Os detalhes vão num campo novo, `contasConectadas`, em `GET /users/{id}`
e em `GET /account`: `[{ "chave", "tipo", "email", "conectadaEm", "ultimoUso" | null, "disponivel" }]`,
inclusive o vínculo de provedor fora da configuração (`disponivel: false`). `GET /account` ganha também o
`formasDeEntrada`. A senha não tem data (não há coluna, e uma nova só teria valor a partir do deploy).
Descartado: trocar `formasDeEntrada` por objetos, que mudaria o contrato que o painel já lê.

### D10. (j) Os quadros

Na revisão do design (13/11/2026), o dono desenha a entrega `m3` (Q11) antes do design da #190, e tira os
textos "a verificação é feita pelo próprio Google". **Esta change não depende dos quadros**: o contrato
das recusas (os motivos e os campos) está nas specs. A lista por quadro e por estado foi entregue ao dono
na revisão do primeiro design (12/11/2026), e é dela que ele corrige os quadros.

### D11. (k) Testes sem o Google real

**O duplo:** um `HttpMessageHandler` falso, posto como handler primário do `HttpClient` do
`OpenIddict.Client.SystemNetHttp` no `WebApplicationFactory`, que responde por `https://accounts.google.com/`:
o discovery, o JWKS (uma chave RSA gerada no teste), o token (confere o `code_verifier` contra o
`code_challenge` do pedido e devolve um id token assinado com o `nonce` do pedido) e o userinfo. Um
`GoogleFlow` de apoio lê o `url` do `start`, tira o `state`, o `nonce` e o `code_challenge`, e chama o
retorno com o código e os cookies, no molde do `IssuerUserFlow`. Um construtor do id token permite cada
desvio: `iss` errado ou sem esquema, `aud` de outro cliente, `nonce` errado, assinatura de outra chave,
`email_verified` falso, `hd` diferente, `exp` vencido, `error=access_denied`.

**Sem cobertura, para a conferência manual com o cliente OAuth real** (o dono cria; nenhum segredo no
repositório):
- o Google aceitar o pedido (escopos, `prompt=select_account`, `response_mode=query`) e a URI registrada;
- o formato real do id token: o `iss` com esquema, o `email_verified`, o `hd`;
- o parâmetro `iss` na resposta, e o WebIntegration tratando-o;
- em **Chrome, Firefox e Safari**: o cookie `Lax` indo no retorno; o `buteco-sessao` e o `buteco-recusa`
  `Strict` gravados na resposta do retorno; os dois **não** indo no carregamento do `/entrar`; os dois
  indo no `fetch` do painel (D5, passos 1 a 3);
- uma conta com e-mail não Gmail não verificado (recusa 3).

**O que o dono cria:** no Google Cloud Console, a tela de consentimento OAuth e um cliente "Aplicativo da
Web" com as URIs `https://<PUBLIC_DOMAIN>/auth/federated/callback` e
`http://localhost:5173/auth/federated/callback`, sem origem JavaScript; o `ClientId` na configuração e o
segredo em `~/.config/buteco-agents/.env` (local) e no `federated-providers.env` (servidor).

### D12. (l) Tamanho (convenção 18)

**Âncoras** (só código, sem migrations, `openspec/` e `.md`): PR #263 (#186) **+1.298 / −33** em produção
e **+1.369 / −14** em testes; PR #230 (#219) **+765 / −31** e **+1.365**. Unidade: linha; casos de teste
contados à parte no fechamento.

| componente | produção | testes | duplo |
|---|---|---|---|
| seção, opções e checagem de boot (D2, D3) | +180 | +260 | o construtor de host com log (existe) |
| cliente: registração, credenciais do anel, cookie, endpoint na convenção 8 (D1, D4) | +220 | +150 | — |
| `start`, retorno, decisão, recusas, `buteco-recusa` e a rota dele, logs (D1, D5, D7) | +420 | +700 | — |
| vínculo: entidade, store, unicidade (D6) | +120 | +120 | — |
| conectar, desconectar, última forma, stamp (D6) | +200 | +350 | — |
| `CallerPassword` com código nas seis rotas (D8) | +60 | +150 | — |
| rotas dos provedores, `formasDeEntrada`, RoleRoutes (D9) | +110 | +150 | — |
| **o provedor falso** (D11) | — | — | **+380** (novo) |
| **total** | **~+1.310** | **~+1.880** | **+380** |

Com o erro medido (+57 % na #180; +70 % nas linhas de teste), ~2.000 / ~3.800 com o duplo: acima da #186.
A divisão da Q10 já tirou o aceite do convite; o resto não se separa sem deixar o vínculo sem quem o
use. Rascunho: a projeção conta antes da verificação (convenção 18).

### D13. (m) Notas de deploy

- **Migration:** `AddUserFederatedIdentities` (a tabela e os dois índices únicos), aditiva.
- **O arquivo do provedor (Q5):** `env_file` opcional só no `api` do `docker-compose.prod.yml`:
  `env_file: [{ path: ./federated-providers.env, required: false }]`, fora do repositório (permissão 600),
  com `Identity__FederatedProviders__<chave>__Type`, `__ClientId`, `__HostedDomain` e `__ClientSecret`. Sem
  o arquivo, nenhum provedor e nada muda. O repositório leva um `federated-providers.env.example` sem
  segredo, e o `.env.prod.example` ganha só o comentário que aponta para ele.
  - **Versão mínima: Docker Compose 2.24.0**, que introduziu o `required` no `env_file` (documentação do
    Docker, "Ways to set environment variables with Compose"). Numa versão anterior, a forma longa
    **reprova no parse do arquivo**, sem subir nada: falha ruidosa, não silenciosa. Conferido localmente
    no `podman-compose` 1.6.0 (sem o arquivo, nenhuma variável; com ele, a variável chega). **As versões
    da produção e da homologação não são visíveis daqui**: o `docs/deployment.md` passa a exigir
    `docker compose version` ≥ 2.24.0 no primeiro passo.
  - **Alternativa, se alguma das duas for anterior:** a forma curta `env_file: ./federated-providers.env`
    com o arquivo **sempre presente** (vazio quando não há provedor), criado no primeiro deploy
    (`install -m 600 /dev/null federated-providers.env`). Vale em todo Compose v2; o custo é o `up`
    reprovar se alguém apagar o arquivo.
- **URI a registrar no Google:** `https://<PUBLIC_DOMAIN>/auth/federated/callback`. O nginx já leva `/auth`
  ao `apps/api` (`apps/frontend/deploy/nginx.conf` l. 97); o `NginxRouteCoverageTests` confere.
- **Conferir:** o `GET /auth/federated/providers` lista a chave; o login de uma conta conectada pede o
  código; uma conta sem convite cai em `sem-acesso`; `/identity/federated-providers` sem segredo; o
  `POST /auth/federated/refusal` sem cookie responde `404`.
- **Voltar atrás:** a imagem anterior do `api`; a tabela fica, ignorada, e os vínculos voltam a valer ao
  subir de novo. Na 189a ninguém fica só com Google (a #274 é que cria essa pessoa).

### D14. (n) Premissas falsas (convenção 9)

1. **O comentário da #217** ("Quando houver o segundo emissor (esta issue), conferir passa a ser
   obrigatório: o login em andamento guarda o issuer esperado do provedor escolhido"): falsa com o D1. O
   `redirect_uri` `/entrar/retorno` (`oauth.ts` l. 13–15) só recebe respostas do authorize do Buteco, o
   `beginLogin` (`loginFlow.ts` l. 59–69) só monta pedidos a ele, e a resposta do Google vai ao `apps/api`.
   Mix-up precisa de dois servidores no mesmo `redirect_uri`. Registro, sem parada.
2. **O corpo, "Telas do protótipo: Minha conta (conectar/desconectar)"**: o quadro `MinhaConta` não tem a
   seção (D10). Registro; o dono desenha.
3. **O registro da #180** está incompleto: além de não acompanhar o retorno, o `buteco-sessao` também não
   iria num `302` do retorno ao authorize (D5). Registro.
4. **"Nenhum nome de fornecedor em chaves, variáveis ou exemplos"** contra o valor `google` do tipo: a regra
   vale para a chave, as variáveis e os exemplos. Registro.
5. **O `01`, l. 771, "obrigatória para todo usuário"** contra o protótipo: resolvida pela Q1, sem mudança de
   contrato.
6. **O `CallerPassword`** pressupõe senha: resolvida pela Q1 (D8), sem mudança para quem tem senha.
7. **A spec `route-authentication`, cenário "Rotas anônimas do app são as conhecidas"**, lista cinco
   rotas, e o `Program.cs` l. 269–278 tem nove (faltam o convite e a redefinição da #181, e o cadastro e o
   segundo passo, que nem estão na allowlist por serem conferidos pelo `RouteAuthenticationTests` l.
   106–131). Só texto de spec: corrigido no delta desta change, com as rotas novas. Registro.
8. **A spec `route-authentication`, cenário "Abrir a sessão do emissor é acessível sem token"**, espera
   `204` do `POST /auth/session`, que desde a #220
   responde `200 { segundoPasso }` (`SessionEndpoints.cs` l. 159–172). Só texto de spec: **corrigido nesta
   change** (decisão do dono, 13/11/2026), com o requisito "Rotas classificadas como anônimas permanecem
   acessíveis sem token" em MODIFIED no delta de `route-authentication`; só esse cenário muda.

### D15. O que o apply mudou (convenção 9, 13/11/2026)

Nenhuma mudança de contrato com o que o painel já lê. O APPLY-7 e o APPLY-11 foram decididos pelo dono em 13/11/2026.

| | o que caiu | o que foi feito |
|---|---|---|
| APPLY-1 | o `OpenIddict.Client` 7.7.2 puxa o `Microsoft.Extensions.Configuration.Binder` 10.0.11, e a restauração dos testes do `apps/api` reprovava por downgrade (NU1605) | o `Microsoft.Extensions.Configuration` central foi de 10.0.10 a 10.0.11 (só dois projetos de teste o referenciam) |
| APPLY-2 | a regra 10 do D3 dizia "a `DistinctKeysMessage` que já existe", que é específica da chave das 2 etapas | mensagem própria, citando a chave igual, sem o valor |
| APPLY-3 | com o cliente OIDC sempre registrado, o `IdentityIssuerEndpointsPassthroughTests.WithBothRoutesAnonymousAndClassified_Passes` (fora da lista do topo) reprovava | o cliente é registrado **só com provedor configurado**; sem provedor, nada dele entra no caminho das requisições, e a conferência dos endpoints do cliente só vale com ele |
| APPLY-4 | a `uriDeRetorno` era `<PublicUrl:BaseUrl>/auth/federated/callback` | é a base do painel (`Identity:PanelBaseUrl`, ou a url pública), como os retornos do `buteco-painel`: em desenvolvimento o navegador está no 5173; spec `federated-providers` corrigida |
| APPLY-5 | o cliente compara a `redirect_uri` com a URI da requisição montada pelo esquema e pelo host (ID2138, `OpenIddictClientHandlers.cs` l. 1036–1056; `OpenIddictClientAspNetCoreHandlers.ResolveRequestUri` l. 116–117), e atrás do proxy o `apps/api` vê HTTP, sem `ForwardedHeaders` | o `FederatedRequestUriHandler` (logo depois do `ResolveRequestUri`) põe a base do painel no `BaseUri` e no `RequestUri`, com o caminho e a query da requisição: o mesmo princípio do servidor, cujos endereços saem do issuer. A comparação do caminho e da query continua |
| APPLY-6 | o "já lido → `404`" do `buteco-recusa` não valia contra o mesmo valor reenviado em 120 s | um id de uso único, marcado no `IMemoryCache` pelo prazo do cookie |
| APPLY-7 | o cenário "Duas desconexões ao mesmo tempo" pede um usuário sem senha com **duas** formas de entrada; com só o tipo Google e um provedor por issuer (regra 6), quem não tem senha tem no máximo **uma** | **aceito pelo dono (13/11/2026):** o cenário virou "A conferência trava a linha do usuário" (o `SELECT … FOR UPDATE` antes do `DELETE`, no molde do `BUTECOD1` da #184), e o guarda 7.5 é sobre ele. A trava fica como defesa para a M4. Nenhum caso existente saiu ou afrouxou |
| APPLY-8 | sem `EnableErrorPassthrough` no cliente, toda falha do retorno (sem cookie, state reusado ou vencido, `access_denied`) virava a página `400` do OpenIddict, e não o `302` com `falha` da spec | `EnableErrorPassthrough()`; o cliente só tem o endpoint do retorno |
| APPLY-9 | a spec dizia que o pedido leva `response_mode=query`; o OpenIddict só anexa o modo quando não é o padrão, e `query` é o padrão do `code` | spec reescrita: o modo é `query`, sem precisar repetir, nunca `form_post` |
| APPLY-10 | — | registro: o state token fica em `openiddict_tokens` com o tipo `urn:openiddict:params:oauth:token-type:state_token` |
| APPLY-11 | a spec não dizia o que o `start` responde com o Google fora do alcance. Medido na conferência local (13.1): o desafio busca o discovery antes de responder, e sem rede o `POST /auth/federated/{chave}/start` devolvia `400` em texto do OpenIddict (`server_error`, ID2170) depois de ~15 s das tentativas do cliente | **aceito pelo dono (13/11/2026):** o início (e o do conectar) busca o discovery antes do desafio, com prazo explícito de **10 s** (`FederatedLogin.ProviderTimeout`: cabe numa espera de clique, e o discovery do Google responde em menos de 1 s); fora do alcance, `503` com `problem+json` e "Não foi possível falar com o provedor. Tente de novo em instantes.", log 1199. **A busca passa pelo cache** (conferido no fonte e por teste, 13/11/2026): `registration.ConfigurationManager` é o `ConfigurationManager<OpenIddictConfiguration>` do IdentityModel, criado uma vez por registração (`OpenIddictClientConfiguration.cs` l. 122–127, renovação automática de 12 h), e o desafio lê o mesmo objeto (`OpenIddictClientHandlers.cs` l. 520–522); dois inícios seguidos fazem um só pedido de discovery (`FederatedDiscoveryCacheTests`; guarda: um `ConfigurationManager` novo a cada início reprova, 1 → 3). **O retorno não muda:** com o provedor fora do alcance na troca do código ou nas chaves, o cliente recusa dentro do `AuthenticateAsync`, e a rota responde `302 /entrar?recusa=falha` (conferido por `TheReturn_WithTheProviderDown_IsFailure_WithoutSession`). Spec `federated-login` com três cenários novos |

## Risks / Trade-offs

- **[Decisão do corpo: mudança exige reinício]** → um retorno em voo no reinício cai em `falha`; as chaves
  do Data Protection do contêiner também se perdem, e o `buteco-recusa` em voo vira `404`.
- **[Um navegador recusar gravar o `Strict` na resposta do retorno]** → o §5.7 permite, e a conferência
  manual confirma. Se algum recusar, a saída é um passo de entrega: o retorno grava só um `Lax` curto de
  uso único, e o painel troca por `buteco-sessao` num `POST` same-site. Fica fora até a conferência mandar.
- **[O Google muda o `iss` do id token ou passa a anunciar o RFC 9207]** → o primeiro recusa todo login
  (lado seguro, com log); o segundo liga a conferência sozinho.
- **[State token gera linha em `openiddict_tokens`]** → uma por início, sem autorização (fora da lista de
  sessões); a limpeza da #209 as remove 14 dias depois de mortas. O `start` está no limite global.
- **[Admin só desconecta]** (decisão do corpo) → o Admin não liga a conta de outro.

## Baseline

No worktree limpo `../buteco-agentes-189-baseline`, em `3de31dd`, pelo
`~/.cache/buteco-agents/bin/suite-189.sh` (cópia do `suite-187.sh` com o cabeçalho da #189; o lock, a
espera por CPU 100 e load, a amostragem e o veredito assimétrico), com o `buteco-agents_postgres_1` e o
`buteco-agents_rabbitmq_1` de pé no `podman ps` do início. Saídas em `~/.cache/buteco-agents/189/`.

| suíte | resultado | duração | estado (início → fim) | CPU mínima |
|---|---|---|---|---|
| `apps/api` | **1696/1696 + 1 ignorado** (rodada 2) | 339 s | load 6,07, CPU 100 → load 9,22, CPU 100 | 60 |
| `tests/InboxOrchestratorRoundTrip.Tests` | **16/16** (rodada 2) | 57 s | load 3,36, CPU 100 → load 6,07, CPU 100 | 100 |
| `tests/ApiConnectorsRoundTrip.Tests` | **12/12** | 42 s | load 4,71 → CPU 100 | 100 |

- **As três APROVADAS** (regra assimétrica da #181).
- **Rodada 1** (`baseline/`), repetida:
  - `apps/api` **INCONCLUSIVA**: 1 reprovação com a CPU em 48,
    `KnowledgeBaseIndexingSummaryTests.SummaryQuery_CostDoesNotGrowWithNumberOfBases` (`Expected: 3,
    Actual: 2`: uma consulta de fundo entrou na primeira captura de SQL);
  - `tests/InboxOrchestratorRoundTrip.Tests` **REPROVADA (válida)**, CPU em 100:
    `OrphanDispatchRoundTripTests.Question2_WorkerFailurePath_SendsPush_AndDispatchIsResolved`,
    `TimeoutException`. Passou na rodada 2. Comentada na #176 como mais um caso da mesma classe
    ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/176#issuecomment-6842575884)).
    No fechamento, reprovar nos dois worktrees não bloqueia; só no da change, para.
- As duas rodadas têm os mesmos nomes. `nomes.txt` em `~/.cache/buteco-agents/189/baseline-final/`: 1725
  linhas (1697 do `apps/api`, com 1 GUID normalizado; 16 e 12 dos acordos).

**Por que cada suíte:** `apps/api` (tudo mora lá, inclusive o boot, a RoleRoutes e o
`NginxRouteCoverageTests`); os dois acordos de `tests/` (sobem o `apps/api` real e passam pelo login do
emissor, `IssuerUserFlow`, que a #189 estende no mesmo cookie; o segundo confere o boot do host real e a
matriz por papel). **Fora:** `apps/inbox` e `apps/connectors` (o token que validam não muda),
`CrossAppTaskStoreCompatibility.Tests` (banco entre api e workers, sem login), `apps/workers` (sem rota
autenticada), `apps/frontend` (as telas são da #190).

## Decisões do dono (13/11/2026)

| | pergunta | decisão | onde |
|---|---|---|---|
| Q1 | As 2 etapas no login pelo Google | o Google também pede o código do Buteco; o `CallerPassword` aceita o código de quem não tem senha (recomendado aqui: só o do app) | D5, D8 |
| Q2 | Desconectar pelo Admin troca o stamp? | sim, e revoga as sessões | D6 |
| Q3 | Conectar pede confirmação? | sim, a senha ou o código | D6, D8 |
| Q4 | Aceite com e-mail não verificado | recusar (vai para a #274) | #274 |
| Q5 | Como o provedor chega ao contêiner | `env_file` opcional só do `api`, Compose ≥ 2.24.0, com a alternativa | D13 |
| Q6 | O e-mail no aviso de recusa | cookie `buteco-recusa` lido por `POST /auth/federated/refusal`, uso único | D7 |
| Q7 | O aceite pelo Google abre o login? | sim (vai para a #274) | #274 |
| Q8 | `HostedDomain` | opcional, filtro | D2, D7 |
| Q9 | `clientId` e `uriDeRetorno` na rota dos provedores | sim | D9 |
| Q10 | Uma change ou duas | duas: esta e a #274 | Context |
| Q11 | Entrega `m3` nos quadros | sim, o dono desenha | D10 |

## Registros (convenção 23)

- **#274** ([issue](https://github.com/aldovrando-oliveira/buteco-agentes/issues/274)), aberta em
  13/11/2026: o aceite de convite pelo Google, M3 · 2 de 3, `blocked-by` #189, em `Ready`; a #190 passou a
  3 de 3, com `blocked-by` #274; a lista da #196 ficou #189, #274, #190.
- **#176** ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/176#issuecomment-6842575884)):
  a intermitente nova do `rt-inbox`.
- **No fechamento:** na #217, o registro do D14.1; na #189, o D14.

## Open Questions

Nenhuma.
