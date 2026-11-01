## Context

**Estado atual (`ca2bd71`, merge do PR #224, 31/10/2026).**

- **O backend do fluxo existe** (#180):
  - `POST /auth/session` grava o cookie `buteco-sessao` (5 min, `HttpOnly`, `SameSite=Strict`) e responde `204`;
  - `/connect/authorize` e `/connect/logout` estão em passthrough;
  - `/connect/token` faz `authorization_code` com PKCE obrigatório e `refresh_token` rotativo e deslizante de 14 dias;
  - access token de 15 min, id token de 20 min, código de 5 min;
  - o cliente público `buteco-painel` tem `redirect_uri` `{PanelBaseUrl}/entrar/retorno` e `post_logout_redirect_uri` `{PanelBaseUrl}/entrar`.
- **Os três apps aceitam o token do emissor:** o `apps/api` desde a #180, pelas chaves do anel; o `apps/inbox` e o `apps/connectors` desde a #208, pelo JWKS.
- **O painel não usa nada disso:**
  - `LoginPage` manda `POST /auth/login` (usuário e senha) e guarda o HMAC em `sessionStorage` (`src/auth/token.ts`, chave `buteco.operatorToken`);
  - `ProtectedRoute` só confere se há token, e manda para `/login`;
  - 6 das 7 cópias do `request<T>` limpam o token e fazem `window.location.href = '/login'` no `401`. A do `apps/connectors` não desloga (`connectorsApi.ts` l. 90–94);
  - a casca não tem "Sair";
  - o `QueryClient` é o padrão do TanStack Query (`app/queryClient.ts`), com 3 novas tentativas em toda consulta e nenhuma em mutação;
  - o `vite.config.ts` não tem proxy, e os `request<T>` chamam `VITE_API_BASE_URL`, `VITE_INBOX_BASE_URL` e `VITE_CONNECTORS_BASE_URL` com CORS.
- **Superfície de XSS hoje:** nenhum `dangerouslySetInnerHTML` nem `innerHTML` em `src/`. O único Markdown é o `ReactMarkdown` das instruções do agente (`AgentOverviewTab.tsx` l. 55), sem `rehype-raw`. O `index.html` tem um script inline (a cor do tema do Mantine). O `nginx.conf` não manda CSP nem outro cabeçalho de segurança.

**Heranças**, cada uma com decisão abaixo:

| herança | de onde | decisão |
|---|---|---|
| o SPA só segue `retorno` que comece por `/connect/authorize?`; o resto vai para a tela inicial | #217, comentário de 31/10 | D6 |
| `400 invalid_request` no authorize logo depois de um deploy, antes da primeira sincronização do catálogo, é passageiro | #217, comentário de 31/10; #180 D3.13 | D1, D6 |
| access token vale até 15 min depois do logout, nos três apps; aceito na M1 | #217, comentário de 31/10; #180 D3.10 | D7 |
| `503` com `Retry-After` é passageiro; só `401` encerra a sessão | #208 D4; #180 D3.12 | D4, D5 |
| desenvolvimento pelo proxy do Vite, e não CORS | #180 D3.4 e comentário de 24/10 | D10 |
| guardar o id token mais recente, renovado a cada refresh, para o `id_token_hint` | #180 D3.9; #209 D8 | D2, D7 |
| abrir aba nova sem token pede login; muda se a #217 guardar o token de outro jeito | #180 D1 | D2 |
| `Auth:OperatorPasswordHash` só para o bootstrap; orientação do `docs/deployment.md` | #180 D2 | D9 |

**O protótipo** é o canvas "Protótipo de autenticação federada" (`claude.ai/artifact/H7dPBZafiSkdKd1hqesPZ6`), relido em 31/10/2026 pela ferramenta Artifact:

- **"Login — sem provedor federado"** (`LoginSemProvedor.dc.html`): cartão único com o lockup, "E-mail" (`autocomplete="username"`, `voce@empresa.com`), "Senha" com o botão "Esqueceu a senha?", "Entrar", e abaixo do cartão "Ainda não tem acesso? Peça um convite ao administrador do painel."
- **"Login — com provedor federado"** (`Main.dc.html`), estados que valem sem provedor:
  - `inicio`: o formulário;
  - `bloqueado`: aviso amarelo "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por 5 minutos.";
  - `sessao-expirada`: aviso azul (`role="status"`) "Sua sessão expirou. Entre novamente para continuar de onde parou.";
  - "Esqueceu a senha?" abre "Peça ao administrador para redefinir sua senha. Você vai criar uma nova no próximo acesso."
- **Fora desta change:** `mfa` (#218, #220), `redirecionando` e `negado` (provedor federado, M3).

### Achados da fonte do OpenIddict 7.7.2

Lidos na tag `7.7.2` (`950cb13`, o mesmo commit da #180), em 31/10/2026. Os caminhos são relativos a `src/`. Os fatos G1 a G5 da #180 continuam valendo. Os novos são:

| # | fato | onde |
|---|---|---|
| H1 | **Tolerância de reuso do refresh: 30 s por padrão.** "the period of time rolling refresh tokens marked as redeemed can still be used to make concurrent refresh token requests". O `apps/api` não muda o padrão (`IdentityIssuerServiceCollectionExtensions.cs` l. 90–91 só fixa as durações). | `OpenIddict.Server/OpenIddictServerOptions.cs` l. 298–302 |
| H2 | **Dentro da tolerância, o refresh resgatado passa** e gera um par novo. O `IsReusableAsync` compara agora com a data do resgate mais a tolerância. | `OpenIddictServerHandlers.Protection.cs` l. 1245–1247 e 1356–1373 |
| H3 | **Fora da tolerância, o reuso revoga todos os tokens da autorização** (`RevokeByAuthorizationIdAsync`), inclusive o refresh mais novo, e responde `invalid_token`. **A autorização em si não é revogada** ("to allow the legitimate client to start a new flow"). Log `6228`: "{Count} tokens associated with the authorization '{Identifier}' were revoked to prevent a potential token replay attack." | `OpenIddictServerHandlers.Protection.cs` l. 1238–1297; `OpenIddictResources.resx`, `ID6228` |
| H4 | No `/connect/token`, `invalid_token` vira `invalid_grant`, com `400`. | `OpenIddictServerHandlers.Exchange.cs` l. 2188–2197 |
| H5 | **O resgate do refresh é marcado cedo e ignora concorrência:** o `RedeemTokenEntry` roda logo depois do `ValidateSignInDemand`, para marcar mesmo se o pedido falhar depois, e no `refresh_token` ignora a falha do `TryRedeemAsync` ("may be caused by concurrent requests"). A data do resgate é gravada só na primeira vez. **O código de autorização, ao contrário, recusa o segundo resgate.** | `OpenIddictServerHandlers.cs` l. 2992–3100; `OpenIddict.Core/Managers/OpenIddictTokenManager.cs` l. 908–935 |
| H6 | Refresh de autorização não `valid` (revogada no logout, D3.10 da #180) é recusado: `ID2022`, "The authorization associated with the refresh token is no longer valid." | `OpenIddictServerHandlers.Protection.cs` l. 1378–1430 |
| H7 | **Erro do authorize que não pode voltar ao `redirect_uri`** (cliente desconhecido, por exemplo) é escrito como `text/plain` com `error:…` e `error_description:…`, a menos que o app ligue a integração com status code pages, que este não liga. | `OpenIddict.Server.AspNetCore/OpenIddictServerAspNetCoreHandlers.cs` l. 1261–1377 |
| H8 | **A resposta do authorize leva `iss`** (RFC 9207), pelo `AttachIssuer`. | `OpenIddictServerHandlers.Authentication.cs` l. 73 e 2220 |

**Já garantido por teste do `apps/api`:** `RefreshRotationTests.ReuseAfterTheLeeway_RevokesTheChain` (H3) avança o relógio 31 s e confere que o refresh antigo **e** o novo dão `400`. Não há teste do lado de dentro da tolerância (H2).

**Medido em 31/10/2026, no ambiente de teste do `apps/frontend`** (Vitest 4.1.10, `jsdom` 29.1.1, Node 26.3.1, sonda num diretório à parte, sem tocar nos worktrees):

| recurso | no `jsdom` |
|---|---|
| `crypto.subtle.digest('SHA-256', …)` | existe, devolve 32 bytes |
| `crypto.getRandomValues` | existe |
| `navigator.locks` (Web Locks) | **não existe** |
| `BroadcastChannel` | existe |
| `StorageEvent` | existe |

**Consequência:** a coordenação entre abas recebe o gerenciador de lock por parâmetro, e o teste passa um gerenciador falso com exclusão mútua real (D12).

**No navegador:** Web Crypto (`subtle`) e Web Locks existem só em contexto seguro, ou seja, HTTPS ou `localhost`. Os dois valem em produção, atrás do TLS de fora, e no `npm run dev`. O cookie `buteco-sessao` já exige `Secure` fora de `Development` (#180 D1), então o painel já não funcionava por HTTP puro fora de `localhost`. A diferença é que agora a falha aparece no próprio painel, e não no cookie (D3).

## Goals / Non-Goals

**Goals:**

- O painel entra pelo emissor, e acessa uma rota do `apps/api`, uma do `apps/inbox` (canais) e a sincronização do `apps/connectors` com o token do emissor. É o critério de aceite herdado da #180.
- Uma sessão de painel sobrevive a recarregar a página e a várias abas, sem que a rotação do refresh a derrube.
- Indisponibilidade passageira (`503` com `Retry-After`, o `400` do catálogo logo depois do deploy) nunca desloga.
- O `POST /auth/login` e o operador da variável deixam de existir. O HMAC fica só validado, para a #182 e a #184.
- Decisões a–j da issue tomadas. As que eram do dono (P1 a P9) foram decididas em 01/11/2026, todas como recomendadas ("Decisões do dono").

**Non-Goals:**

- **2 etapas** no login (#218, #220).
- **Telas definitivas** do login e da casca (#183). O "Sair" desta change é mínimo.
- **Recusar o HMAC `operator`** (#184) e **remover o HMAC** (#182).
- **Revogar o access token já emitido** depois do logout (aceito na M1; para aplicações externas, #186).
- **"Esqueceu a senha?"** e o rodapé do convite: não há redefinição nem convite antes da #181 (D6).
- **Refresh token em cookie `HttpOnly`** por um backend do painel (D2, descartada para a M1).

## Decisions

### D1. O fluxo, e por onde ele começa

```
 sem tokens            /entrar (SPA)                    apps/api                         SPA
 ──────────            ─────────────                    ────────                         ───
 ProtectedRoute ──▶ formulário ──POST /auth/session──▶ 204 + cookie buteco-sessao
                     │                                   (401/403/423/429/503 → aviso)
                     │ beginLogin(): verifier, state,
                     │ voltarPara → sessionStorage
                     └──navega──▶ /connect/authorize?client_id=buteco-painel&…
                                    │ cookie válido → SignIn
                                    └──302──▶ /entrar/retorno?code&state&iss ──▶ confere state
                                                                                   POST /connect/token
                                                                                   grava tokens (D2)
                                                                                   navega a voltarPara
```

**O SPA começa pelo `/entrar`, e só navega ao authorize depois do `204`.** O #180 D1 descreve o caminho inverso: o SPA navega ao authorize, que manda para `/entrar?retorno=…` quando não há cookie. Os dois funcionam com o backend de hoje. A diferença está no que o SPA consegue ver:

| | (A) começa no authorize (#180 D1) | (B) começa no `/entrar` |
|---|---|---|
| primeiro contato com o `apps/api` | **navegação**: o SPA não vê a resposta | **`fetch`**: o SPA vê o status e mostra o aviso |
| `503` do gate do anel (logo depois do start) | página em branco do `apps/api`, sem o painel | aviso "O painel está iniciando", com o `Retry-After` (D6) |
| `400` do catálogo ainda não sincronizado (H7) | `text/plain` "error:invalid_request" | só se o catálogo sincronizar depois do `204`. O gate já abriu, e o catálogo roda no mesmo primeiro ciclo (#180 D3.13) |
| aba nova com o cookie ainda vivo (até 5 min depois do login) | código direto, sem formulário | formulário de novo. Com o D2, a aba nova já tem token e nem chega aqui |
| `retorno` | todo login passa por ele | só quando o authorize devolve para `/entrar` (cookie vencido, stamp trocado, link direto) |

**Decisão: (B)** (P1, dono, 01/11/2026). O `retorno` continua tratado como a issue pede (D6), porque o authorize ainda devolve para `/entrar` nos casos da última linha.

**O `400` do catálogo que sobra em (B).** O SPA não tem como ver a resposta de uma navegação (H7). As saídas:

- **aceitar e documentar** (decidida, P1): a janela é o primeiro ciclo do aquecimento, de segundos, e só pega quem entra nesse instante. Recarregar resolve. Fica no `docs/deployment.md`;
- **status code pages no `apps/api`**, só para o authorize, redirecionando o erro local para `/entrar?erro=indisponivel`: é backend novo, e liga uma integração global do OpenIddict (H7) para uma janela de segundos;
- **sondar antes de navegar** (`fetch` do authorize sem `code_challenge`, com `redirect: 'manual'`): distingue cliente desconhecido (`400`) de conhecido (erro devolvido ao `redirect_uri`) pela ordem dos validadores do OpenIddict. **Descartada:** depende de detalhe interno da biblioteca, e uma sonda com cookie vivo emitiria código.

### D2. (a) Onde ficam os tokens

**As opções, pelo que custam:**

| | memória | `sessionStorage` (hoje) | `localStorage` | cookie `HttpOnly` (backend do painel) |
|---|---|---|---|---|
| recarregar a página | **perde a sessão**: login de novo (o cookie de 5 min só ajuda logo depois do login) | mantém | mantém | mantém |
| aba nova | login de novo | login de novo, e **uma autorização nova por aba** | usa a mesma sessão | usa a mesma sessão |
| "duplicar aba" do navegador | — | **copia o `sessionStorage`**: duas abas com o mesmo refresh e sem coordenação. A primeira que renovar deixa a outra com um refresh resgatado, e o uso dele depois de 30 s revoga a sessão das duas (H3) | coordenado (D3) | coordenado pelo servidor |
| fechar o navegador | perde | perde | **mantém**, até 14 dias sem uso | conforme o cookie |
| sessões em Minha conta (#219) | uma por recarga | uma por aba, todas vivas por até 14 dias | uma por navegador | uma por navegador |
| XSS lê o refresh token | sim, com esforço: interceptando o `fetch` do `/connect/token` | sim, direto | sim, direto | **não** |
| refresh em repouso no disco | não | não | **sim**, no perfil do navegador | sim, mas ilegível para script |
| logout em uma aba encerra as outras | não há outras | **não**: cada aba tem sua autorização | sim, pelo D7 | sim |
| custo de implementação | baixo | baixo | coordenação (D3) | backend novo no `apps/api`, fora do escopo (convenção 1) |

**XSS, sem ilusão:** em todas as opções sem backend, um script injetado no painel **usa** a sessão enquanto a página está aberta: chama as APIs com o token, ou intercepta a próxima renovação. A diferença entre memória e storage é o **roubo do refresh para uso fora do navegador**, por até 14 dias deslizantes. Só o cookie `HttpOnly` fecha isso, e é a recomendação do rascunho "OAuth 2.0 for Browser-Based Applications" (IETF), padrão "backend for frontend". Ele exige que o `apps/api` guarde o refresh e faça a troca pelo SPA: é outra change, de backend, e cabe na M2 se o risco pedir.

**Decisão: `localStorage`** (P2, dono, 01/11/2026), num registro só:

```
buteco.sessao = { accessToken, accessExpiresAt, refreshToken, idToken, nome, email }
```

- **Por que:** é a única opção sem backend em que a sessão é do navegador, como o operador espera de um painel, e não da aba. Recarregar não desloga, aba nova não pede login, e o logout encerra todas. A lista de sessões da #219 fica legível: uma por navegador, e não uma por aba que alguém abriu e fechou.
- **O que ela custa, e é aceito com ela:** o refresh fica legível por script e em repouso no perfil do navegador por até 14 dias sem uso. O `sessionStorage` de hoje também é legível por script. O que muda é o repouso.
- **O que ela exige:**
  1. **CSP e `Referrer-Policy: no-referrer` no `nginx.conf`**, no `server` e repetidas no `location = /index.html` (a armadilha do `add_header`, já registrada no próprio arquivo). Todo caminho que entrega o shell reentra nesse bloco (o `rewrite` de navegação e o `try_files`), então os dois níveis bastam:
     ```
     default-src 'self'; script-src 'self' 'sha256-<hash do script do tema>'; style-src 'self' 'unsafe-inline';
     img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none';
     base-uri 'none'; frame-ancestors 'none'; form-action 'self'
     ```
     - `style-src 'unsafe-inline'`, porque o Mantine escreve `style=` em componentes;
     - o hash do script inline do `index.html` sai do build e é conferido por teste;
     - `connect-src 'self'` vale porque, em produção, as três bases são caminho relativo (`Dockerfile`, `VITE_*_BASE_URL=""`).
     - **`Referrer-Policy: no-referrer`**: o `/entrar/retorno` chega com `code` e `state` na URL. A troca de URL do D6 tira os dois do histórico da aba, e o cabeçalho garante que nenhum pedido feito antes dela (o próprio `/connect/token`, um asset) os leve no `Referer`;
     - os dois com `always`, para valerem também nas respostas de erro;
     - **Conferência no navegador** no fechamento: o painel inteiro sem violação no console (D10), no stack local, que não passa pela borda;
     - **A borda de produção, medida em 01/11/2026** (`217-verificacao/10.3-borda.txt`, `GET /` e `GET /entrar` com cabeçalhos de navegador): a **única** injeção do Cloudflare é o beacon do Web Analytics, um `<script type="module">` de `https://static.cloudflareinsights.com` com SRI. Nenhum Rocket Loader, ofuscação de e-mail, outro script, `<style>` inline ou atributo de evento. Pelo código do beacon, com `version` no `data-cf-beacon` e sem `send.to`, ele envia a `/cdn-cgi/rum` na própria origem, e não a `cloudflareinsights.com`. A borda não manda nenhum cabeçalho de segurança. **Decisão do dono (01/11/2026): o painel não usa o Web Analytics, e o beacon não é liberado.** A CSP fica sem nenhum host do Cloudflare; a CSP bloqueia o beacon, e a injeção automática deve ser desligada na zona (`docs/deployment.md`). Se um dia o Web Analytics for desejado, libera-se só o `script-src https://static.cloudflareinsights.com`, e o `connect-src https://cloudflareinsights.com` só se o `data-cf-beacon` vier sem `version`. A falta de HSTS na borda virou issue própria (convenção 23, link no `tasks.md`, 10.3);
     - entra nesta change (P3, dono, 01/11/2026).
  2. **Nenhum HTML vindo de dado**: manter `dangerouslySetInnerHTML` fora e o `ReactMarkdown` sem `rehype-raw`. Uma regra de lint torna isso verificável sem dependência nova: o `eslint-plugin-react` não está no projeto (`package.json`, 01/11/2026), então a regra é a `no-restricted-syntax` do próprio ESLint, com o seletor `JSXAttribute[name.name='dangerouslySetInnerHTML']`, e a `no-restricted-imports` para `rehype-raw`.
  3. **Coordenação da renovação** (D3), sem a qual o `localStorage` derruba a sessão.
  4. **Limpeza no logout e no fim de sessão** (D5, D7): o registro sai por inteiro, e as outras abas reagem.
- **O que fica no `sessionStorage`**, por aba: o login em andamento (`verifier`, `state`, `voltarPara`, `criadoEm`). Ele não é sessão, e não deve ser visto por outra aba (D6).
- **`nome` e `email`** saem do payload do id token, decodificado sem conferir assinatura. Ele chegou pelo `/connect/token`, por TLS, do próprio emissor (OIDC Core 3.1.3.7, item 6). Nenhuma decisão de acesso usa esses campos: eles são só exibidos.

**Descartadas:** memória (recarregar desloga, e um painel recarrega muito); `sessionStorage` (o caso da aba duplicada derruba a sessão sem coordenação, e com coordenação as abas continuam com autorizações separadas); cookie `HttpOnly` (backend novo, acima).

### D3. (b) Várias abas: a renovação coordenada

**O problema, pela fonte:** duas abas que renovam com o mesmo refresh dentro de 30 s recebem pares novos, e a sessão segue (H2). Uma aba que apresenta um refresh já resgatado há mais de 30 s **revoga todos os tokens da autorização**, inclusive o refresh mais novo da outra aba (H3). Com o `localStorage`, isso acontece quando uma aba guarda o refresh em memória e o usa depois de outra ter renovado.

**A regra: o refresh nunca sai do registro para uma variável que viva mais que a renovação.** A renovação roda sob um lock exclusivo do navegador e relê o registro dentro dele:

```
renew(tokenQueFalhou?):
  navigator.locks.request('buteco.sessao.renovar', async () => {
    s = lerRegistro()
    se s ausente                          → fim de sessão (outra aba saiu)
    se s.accessToken ≠ tokenQueFalhou
       e s.accessExpiresAt > agora + 60 s → outra aba já renovou: usa s
    r = POST /connect/token (grant_type=refresh_token, refresh_token=s.refreshToken, client_id),
        com AbortSignal.timeout(10 s) por tentativa
    200          → grava o registro inteiro ANTES de soltar o lock
    400 invalid_grant → apaga o registro: fim de sessão (D5)
    503          → espera o Retry-After e repete (abaixo)
    falha de rede → repete (abaixo)
  })
```

- **Web Locks**, e não um lock feito à mão em `localStorage`: é atômico entre abas da mesma origem, é solto quando a aba fecha ou trava, e existe em todos os navegadores atuais, em contexto seguro (Context).
- **Aviso às outras abas pelo evento `storage`**, e não pelo `BroadcastChannel`. O registro é a fonte, e o evento dispara em toda outra aba da origem quando ele muda ou sai. Não precisa de um segundo canal que poderia divergir do registro. O `BroadcastChannel` ficaria para mensagens que não são estado, e não há nenhuma.
- **Repetição dentro da tolerância.** Um `503` do gate não chegou ao OpenIddict, porque o `UseIdentityKeyRingGate` roda antes do `UseAuthentication` (`Program.cs` l. 199–200), e o refresh não foi resgatado. Uma falha de rede pode ter chegado, e o refresh pode estar resgatado (H5).
  - Nos dois casos, o refresh só é apresentado de novo **até 25 s depois da primeira tentativa**, abaixo dos 30 s (H1).
  - Passado esse prazo, a renovação desiste **sem apagar o registro**. A sessão não acabou: a próxima chamada tenta de novo.
  - **Risco declarado:** se a resposta perdida resgatou o refresh, essa próxima tentativa, depois de 30 s, revoga a cadeia (H3). O usuário vê "Sua sessão expirou" uma vez. Ver "Risks".
- **Todo escritor do registro escreve sob este mesmo lock** (decisão de 01/11/2026, revisão do dono). São quatro: a gravação depois da troca do código (`/entrar/retorno`), a renovação, o logout (D7) e o fim de sessão (D5: `invalid_grant` na renovação ou segundo `401`). Leitura não pede lock.
  - **A corrida que isso fecha:** a aba A está renovando, com o pedido ao `/connect/token` em voo; a aba B clica em "Sair" e apaga o registro; a resposta de A chega e grava o par novo. A sessão "volta" em todas as abas, com uma autorização que o logout de B acabou de revogar no servidor (H6), e o painel só descobre na renovação seguinte, por `invalid_grant`.
  - **Por que o lock, e não a gravação condicional** ("a renovação só grava se o registro ainda existe com o mesmo `refreshToken` que ela leu"): a condicional é ler e depois escrever no `localStorage`, e o `localStorage` não tem operação atômica entre abas. O "storage mutex" da especificação do HTML nunca foi implementado pelos navegadores, e abas em processos diferentes intercalam a leitura de uma com a escrita da outra. A janela ficaria pequena, mas a corrida seria a mesma, só mais estreita. O Web Lock é exclusão mútua de verdade, e já existe para a renovação: o logout e o fim de sessão só passam a pedi-lo, sem um segundo mecanismo.
  - **E o logout sob o lock lê o `idToken` depois da renovação em voo**, ou seja, o mais novo, que é o que o `id_token_hint` pede (#180 D3.9).
  - **Custo:** o "Sair" espera a renovação em voo terminar. No caso comum, menos de 1 s. No pior caso, o prazo da renovação: nenhuma tentativa começa depois de 25 s, e cada uma tem `AbortSignal.timeout(10 s)`, então o lock é solto em até 35 s. O item mostra "Saindo…" enquanto espera.
  - **Dentro do lock, o fim de sessão apaga só o registro que falhou:** compara o `refreshToken` (fim por `invalid_grant`) ou o `accessToken` (segundo `401`) com o que a aba usou. Se outra aba já gravou outra sessão (um login novo em outra aba), ela fica. Não é a gravação condicional descartada acima: a comparação roda sob o lock, e serve para não apagar o trabalho de outra aba, e não para substituir a exclusão. O logout apaga sempre.
- **Sem `navigator.locks`** (HTTP fora de `localhost`): o painel mostra no `/entrar` "O painel precisa ser aberto por HTTPS." e não começa o fluxo. O `crypto.subtle` do PKCE falta no mesmo caso, então não há modo degradado que valha a pena.
- **Garantia do lado do servidor:** um teste novo no `apps/api` fixa o H2, com o mesmo refresh reapresentado 10 s depois do primeiro resgate e as duas respostas `200` (em sequência: ver a 2.1 do `tasks.md`), ao lado do `ReuseAfterTheLeeway_RevokesTheChain` que já fixa o H3. Se uma atualização do OpenIddict mudar o padrão de 30 s, os dois reprovam. **Alternativa:** fixar `SetRefreshTokenReuseLeeway(30 s)` no `apps/api`. É uma linha, mas é configuração nova só para repetir o padrão. O teste já protege, e a constante dos 25 s do SPA cita o H1.

### D4. (c) Quando renovar, e o `503`

**Renovação preguiçosa, antes do pedido:** antes de cada chamada, se o access token vence em menos de 60 s, renova (D3). E, uma vez, diante de `401` (D5). **Sem timer.**

- **Por que sem timer:** aba em segundo plano tem timers estrangulados pelo navegador, então um timer não garante nada, e o caminho do `401` precisaria existir de qualquer jeito. E uma aba esquecida aberta não renova sozinha: a expiração deslizante de 14 dias (#180 D3.7) conta do último uso real, e não de uma aba parada. As consultas com polling condicional (convenção 20) renovam quando chamam, como qualquer outra.
- **Os 60 s** cobrem a tolerância de relógio de 30 s dos três apps (#208 D4) e um pedido lento.

**O `503` convive assim:**

| onde | o que o painel faz | sessão |
|---|---|---|
| `/connect/token` na renovação | espera o `Retry-After` e repete, dentro dos 25 s (D3) | mantida |
| `/connect/token` na troca do código | repete uma vez depois do `Retry-After`. O código é de resgate único (H5): se a primeira resposta se perdeu, a repetição dá `invalid_grant`, e o `/entrar/retorno` pede para entrar de novo | — |
| `POST /auth/session` | aviso no `/entrar`, botão de novo depois do `Retry-After` (D6). Nada é repetido sozinho, porque é a senha da pessoa | — |
| rotas dos três apps, em **consulta** | o `request<T>` lança o `ApiError` com o `retryAfter` lido do cabeçalho. O `QueryClient` repete `503` até 3 vezes, esperando o `Retry-After` (de 1 a 30 s), e depois mostra o erro da tela | mantida |
| rotas dos três apps, em **mutação** | não repete sozinho. As telas já tratam o `503` de contenção do `apps/api` ("Nada foi excluído; repita", `deleteKnowledgeBaseMessages.ts`) | mantida |

- **Os `503` de hoje não são só do gate:** o `apps/api` também responde `503` com `Retry-After: 1` na contenção da exclusão de base e da escrita de documento (`KnowledgeBaseEndpoints.cs` l. 194–200, `KnowledgeDocumentEndpoints.cs` l. 236–242), e o `apps/connectors` responde `503` quando o provedor ou o `apps/api` estão fora (`ConnectorEndpoints.cs` l. 93, `SyncEndpoints.cs` l. 38–48). Nenhum deles fez nada antes de responder. Por isso a regra é por status, e não por origem: **nenhum `503` desloga**.
- **Mudança de comportamento declarada:** hoje toda consulta repete 3 vezes por padrão, com espera exponencial e sem ler o `Retry-After`. A regra nova muda isso só para o `503`. Os outros erros continuam no padrão do TanStack Query, e as consultas que já declaram `retry` próprio (`useAgentInsights.ts` l. 48–51, `useConnectors.ts` l. 21 e 35) não mudam. Decidida pelo dono (P4, 01/11/2026), porque mexe no padrão de todas as telas.

### D5. `401` e fim de sessão

**Toda cópia do `request<T>` faz o mesmo, pelo módulo de sessão (D13):**

1. anexa o access token, renovado antes se preciso (D4);
2. no `401`, renova uma vez, com o token que falhou (D3), e repete o pedido uma vez;
3. **`401` de novo, ou renovação com `invalid_grant`, encerra a sessão:** sob o lock da renovação (D3), apaga o registro se ele ainda é o que falhou, e navega a `/entrar?motivo=sessao-expirada`, guardando a rota atual como `voltarPara`.
4. **Emissor indisponível na renovação depois do `401`:** quando a renovação desiste por indisponibilidade (`503` ou rede até o fim da janela de 25 s, D3), a chamada devolve à tela o `401` recebido, sem repetir e **sem** encerrar a sessão: indisponibilidade nunca desloga (D4). **Decidido na implementação (3.7, 01/11/2026), aceito pelo dono.**
5. **`invalid_grant` na renovação encerra a sessão em qualquer dos três apps, inclusive no `apps/connectors`:** a renovação já apagou o registro sob o lock (D3), então não há sessão para manter, e ficar na tela deixaria o painel sem registro e sem aviso. A exceção do `apps/connectors` abaixo vale só para o segundo `401`. **Decidido na implementação (3.7, 01/11/2026), aceito pelo dono.**

**Por que renovar antes de desistir:** um `401` não é sempre sessão acabada. Um `kid` desconhecido no `apps/inbox` ou no `apps/connectors` responde `401` e dispara a busca das chaves, e a requisição seguinte valida (#208 D4). O access token também pode ter vencido entre a conferência dos 60 s e a chegada do pedido.

**`apps/connectors`: continua sem encerrar a sessão** (P5, dono, 01/11/2026). O motivo de hoje (`connectorsApi.ts` l. 90–94) era a chave HMAC divergente entre processos. Com o token do emissor, o equivalente é uma configuração errada do `apps/connectors` (`aud`, issuer), que responderia `401` a todo token. Deslogar a cada abertura do seletor seria o mesmo laço sem saída. Então ele renova e repete, como os outros, e no segundo `401` lança o erro da tela sem apagar o registro. Token vencido de verdade é pego pela próxima chamada ao `apps/api`.

### D6. (d) `/entrar` e `/entrar/retorno`

**`/entrar`**, o cartão do protótipo sem provedor:

| resposta do `POST /auth/session` | o que aparece | de onde vem o texto |
|---|---|---|
| `204` | segue o fluxo (D1) | — |
| `400` | "Informe e-mail e senha." | validação do formulário antes de enviar; o `title` do servidor se escapar |
| `401` | "E-mail ou senha inválidos." | o `title` do servidor (`SessionEndpoints.InvalidCredentialsMessage`), igual ao de hoje |
| `403` | o `title` do servidor. É "Acesso desativado. Fale com o administrador." (desativado, depois da senha certa) ou "Origem não permitida." (configuração) | `SessionEndpoints.cs` l. 31; `SessionRequestGuards.cs` l. 35. Os dois são texto para a pessoa, então o SPA não precisa distinguir |
| `423` | estado `bloqueado`: "Muitas tentativas sem sucesso. Por segurança, o acesso desta conta fica pausado por **N minutos**." | o protótipo diz "5 minutos" fixo; o servidor dobra até 60 min (`local-users`, bloqueio progressivo). **Contraria o protótipo** (convenção 17): N sai do `Retry-After`, arredondado para cima |
| `429` | "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." | o `Retry-After` é 1 s (`LoginRateLimiting.cs` l. 26); o botão volta depois dele |
| `503` | "O painel está iniciando. Tente de novo em alguns segundos." | o botão volta depois do `Retry-After` |
| falha de rede | "Não foi possível falar com o servidor." | — |

- **`?motivo=sessao-expirada`** mostra o aviso azul do protótipo, "Sua sessão expirou. Entre novamente para continuar de onde parou.". **"Continuar de onde parou" é cumprido:** o `voltarPara` guardado no fim de sessão (D5) segue no login em andamento, e o `/entrar/retorno` navega para ele. Ele só é aceito como caminho relativo do próprio painel: começa por `/`, não por `//`, sem `\`, e não é `/entrar…`. Senão, é `/`.
- **"Esqueceu a senha?" e "Ainda não tem acesso? Peça um convite…" ficam de fora.** Os dois prometem o que só existe com a #181 (redefinição e convite) e a #183. **Contraria o protótipo**, com registro.
- **Divergência do corpo da issue (convenção 9):** o corpo diz "as telas definitivas (2 etapas, bloqueio, sessão expirada) são da #183"; o pedido do dono de 31/10/2026 traz para cá os estados `inicio`, `bloqueado` e `sessao-expirada`. Fica o pedido do dono, e o corpo é atualizado.

**`retorno` (validação):** `/entrar?retorno=…` vem do `apps/api` (#180 D1). Depois do `204`:

- **segue** quando o valor, decodificado uma vez (a leitura da query), começa por `/connect/authorize?`. **A navegação usa exatamente essa string**, sem decodificar de novo e sem recompor a URL;
- **só o prefixo** (decisão do dono, 01/11/2026, na parada da 3.6): um caminho que começa por `/connect/authorize?` não muda de origem nem de caminho ao ser resolvido contra a origem do painel, então a conferência por `new URL` (mesma origem e caminho exato) que este item previa era redundante e saiu. Ela não tinha defeito que a fizesse falta: retirada, nenhum caso da tabela reprovava (`217-verificacao/guardas/3.6-antes-da-decisao/`). A proteção contra afrouxar o prefixo é a tabela de testes (D12);
- qualquer outro valor (absoluto, `//…`, `/\…`, outro caminho) termina em `/`, a tela inicial, como o comentário da issue pede: o `/entrar` começa um fluxo novo com `beginLogin('/')`, e, com o cookie recém-gravado, o authorize devolve o código sem formulário. A tela inicial não é um laço de volta ao `/entrar`. **Corrigido na implementação (4.1, 01/11/2026):** antes, este item dizia que o `ProtectedRoute` começaria o fluxo novo, mas ele manda para o `/entrar`, o que pediria a senha de novo. Sem `retorno`, o fluxo é o normal (D1), com o `voltarPara`;
- o `state` de um `retorno` que o SPA não iniciou (link montado por terceiro) não bate no `/entrar/retorno`, e o código é descartado. Quem montou o link não recebe o código, que vai ao `redirect_uri` registrado.

**`state` e PKCE, gerados e conferidos no SPA:**

- `code_verifier`: 32 bytes de `crypto.getRandomValues`, em base64url (43 caracteres, dentro de 43–128, RFC 7636 4.1);
- `code_challenge`: base64url do SHA-256 do verifier, `code_challenge_method=S256`;
- `state`: 32 bytes aleatórios, em base64url;
- o login em andamento vai ao `sessionStorage` da aba (`buteco.login`), com `criadoEm`. Um `beginLogin` novo sobrescreve o anterior, então há no máximo um por aba.

**`/entrar/retorno`**, na ordem:

0. **lê `code`, `state`, `error` e `error_description` da URL uma vez, e troca a URL** por `/entrar/retorno` sem query, com `navigate('/entrar/retorno', { replace: true })` (revisão do dono, 01/11/2026), antes de qualquer pedido:
   - **por quê:** o código não fica na entrada do histórico da aba (voltar, "reabrir aba fechada", a URL copiada da barra) nem no `Referer` de um pedido seguinte, que o `Referrer-Policy: no-referrer` do D2 também corta;
   - **o que sobra, declarado:** a navegação original, o `302` do authorize, já entrou no histórico global do navegador com a query. O código ali é de uso único, vence em 5 min e só troca com o `code_verifier` da aba (PKCE), então não serve a quem o ler depois;
   - o processamento usa os valores capturados, e não a URL, que já não os tem. Ele roda uma vez por montagem, por uma `ref`: no `StrictMode` de desenvolvimento o efeito roda duas vezes, e o login em andamento é de uso único (passo 1);
   - vale para toda saída, inclusive `error` e `state` errado;
1. lê e **apaga** o login em andamento: é de uso único;
2. **sem login em andamento**, ou `state` diferente, ou mais de 10 min (código de 5 min + cookie de 5 min) → "Não foi possível concluir a entrada. Entre de novo.", com o botão "Entrar". Nada é trocado;
3. **com `error`**, e `state` conferido:
   - `login_required`: só vem com `prompt=none`, que o SPA nunca manda. Vai para `/entrar`, sem aviso de erro;
   - `invalid_request`, `access_denied`, `invalid_scope`, `unauthorized_client`, `server_error`, `temporarily_unavailable`, ou qualquer outro: "Não foi possível concluir a entrada.", com "Tentar de novo" (`beginLogin`). O `error_description` vai ao console, e não à tela: ele é inglês e técnico;
   - o `400` sem redirecionamento (H7) nunca chega aqui (D1);
4. **com `code`** → `POST /connect/token` (`grant_type=authorization_code`, `code`, `redirect_uri`, `client_id`, `code_verifier`):
   - `200` → grava o registro (D2), sob o lock da renovação (D3), e navega a `voltarPara`;
   - `invalid_grant` (código vencido ou já usado, ou usuário desativado entre o authorize e a troca, #180) → `/entrar?motivo=falha-na-entrada&voltarPara=…`, sem gravar sessão. O `/entrar` mostra o aviso genérico, "Não foi possível concluir a entrada. Entre de novo.", em vermelho e com `role="alert"`, e o `voltarPara` segue no próximo login. **Decidido na implementação (4.2, 01/11/2026), aceito pelo dono:** o `/entrar` não tinha aviso para esse caso, e o motivo na URL é o mesmo mecanismo do `sessao-expirada`;
   - `invalid_client` (catálogo ainda não sincronizado) ou `503` → "O painel está iniciando…", com "Tentar de novo".
- **O `iss` da resposta (H8) não é conferido.** Ele defende contra confusão entre emissores (RFC 9207), e o painel só conhece um. Conferir exigiria o SPA saber o issuer, que em desenvolvimento é outra origem (`http://localhost:5017`) e viria do discovery, só para isso. **Gatilho:** um segundo emissor no painel (federado, M3).
- **Sem `nonce`.** O PKCE já amarra o código à aba que o pediu (OAuth 2.0 Security BCP, RFC 9700, 2.1.1), e o id token não decide acesso (D2).

### D7. (e) Encerrar sessão

"Sair", na casca:

1. pede o lock da renovação (D3). Se outra aba estiver renovando, espera ela gravar, e o item mostra "Saindo…";
2. dentro do lock, lê o `idToken` do registro, que é o mais novo;
3. **apaga o registro**, ainda dentro do lock, e solta. O evento `storage` leva as outras abas a `/entrar` (D3);
4. navega a `/connect/logout?id_token_hint=<idToken>&post_logout_redirect_uri=<origin>/entrar`.

- **O `apps/api` revoga a autorização do hint e apaga o cookie** (#180 D3.9). O refresh que outra aba tivesse em voo é recusado (H6).
- **Sem `idToken`** (registro corrompido): navega sem hint. O `apps/api` apaga o cookie e manda para `/entrar` (`EndSessionEndpoint.cs` l. 56–59), mas a autorização fica viva até vencer. É um caso de defeito, e não de uso.
- **Hint vencido:** o id token dura 20 min, e o do registro é o da última renovação. A validade do hint não é conferida no logout, só a assinatura, que dura 15 dias na retenção (#180 D3.7 e D3.9).
- **O access token já emitido vale até 15 min** nos três apps (aceito na M1). O painel já o apagou, então isso só importa para quem o copiou.
- **"Sair" mínimo:** um item no rodapé da navegação da casca, com o nome do usuário do id token. **Muda um requisito da `frontend-app-shell`**, que proibia nome e e-mail no rodapé "enquanto o backend não fornecer essa identidade": o id token agora fornece, e o requisito passa a exigir que o nome venha dele (convenção 13). A posição e a forma definitivas eram da #183 (P6, dono, 01/11/2026).
- **O rodapé do protótipo** (validação visual da 11.5, dono, 01/11/2026). O protótipo de autenticação foi atualizado com o botão de tema (quadro "Editar usuário", `UsuarioDetalhe.dc.html`, e as outras telas com barra lateral), e o rodapé passa a segui-lo:
  - uma linha só, com a borda superior que separa da navegação;
  - avatar de 30 px, decorativo, com as iniciais do nome (primeira letra da primeira e da última palavra; uma letra com uma palavra; sem nome, a do e-mail). Fundo e texto vêm das variáveis `--buteco-avatar-bg` e `--buteco-avatar-ink` do tema: o tom 1 e o tom 9 do `butecoBlue` no claro, como o protótipo (`#eef3fb` e `#1a3f78`), e **o par invertido no escuro**, que o protótipo não desenha (convenção 16: o papel troca de ponta). A escolha do escuro é desta implementação, e vai à validação do dono;
  - no meio, o nome (peso 500, com reticências) e, abaixo, o e-mail em 11 px e `dimmed` (`#686d74`, o mesmo do protótipo). Sem nome, o e-mail sobe e a segunda linha some;
  - à direita, o tema e o "Sair", os dois só de ícone, em 32 px com ícone de 16 px. O tema mantém o rótulo do `frontend-scaffold` ("Mudar para tema escuro"/"…claro"), e não o "Usar tema escuro" do protótipo. O "Sair" usa o `loading` do `ActionIcon`: desabilitado, com o loader no lugar do ícone e `aria-label` "Saindo…";
  - a borda continua a `--mantine-color-default-border` da casca, a mesma do topo da barra; o protótipo usa `#f1f1f1`;
  - **a #184 troca a segunda linha pelo papel**, como no protótipo, e a #183 só revisa o rodapé (comentários nas duas issues).

### D8. (f) A remoção do `POST /auth/login` e do caminho HMAC do operador

**O que sai do `apps/api`:**

- `Auth/Endpoints/AuthEndpoints.cs` (a rota inteira), `Auth/Requests/LoginRequest.cs`, `Auth/Responses/LoginResponse.cs`;
- `OperatorCredentialOptions`: o `OperatorUsername` não tem mais leitor, e o `OperatorPasswordHash` já está no `UserBootstrapOptions`. A classe sai, e o `UserBootstrapOptions.SectionName` passa a ser `"Auth"` literal;
- `TokenSigningOptions.OperatorTokenLifetime`: só o login lia (convenção 25);
- `Program.cs`: o `Configure<OperatorCredentialOptions>`, o `MapAuthEndpoints()` e `"/auth/login"` da allowlist;
- `Auth:OperatorUsername` dos `appsettings*.json`, do `docker-compose.prod.yml`, do `.env.prod.example` e do `.env.example`.

**O que fica:**

- `OperatorPasswordHasher`, que o `BootstrapLegacyPasswordVerifier` usa (`BootstrapLegacyPasswordVerifier.cs` l. 28);
- `ITokenService`, a validação HMAC e o `ServiceTokenDelegatingHandler`, para os tokens de serviço;
- o subject `operator` e o `ServiceScopeAuthorizationHandler`, que mapeiam também o token do painel (#180 D3.3).

**O que sai do `apps/frontend`:** `features/auth/api/authApi.ts` (`login`), `useAuth.ts`, `types/auth.ts` (`LoginInput`, `LoginResult`), `src/auth/token.ts`, e o `LoginPage`, que vira o `EntrarPage`. A rota `/login` fica como `<Navigate to="/entrar" replace />`, para links e favoritos.

**Os tokens HMAC de operador já emitidos:**

- duram até 30 min (`OperatorTokenLifetime` padrão) depois do último login antes do deploy;
- **continuam aceitos** nos três apps, porque a validação HMAC não muda. Recusar o `operator` HMAC é da #184, e remover o HMAC é da #182;
- **depois do deploy, nenhum é emitido**: a única forma de ter um é assinar com `Auth:TokenSigningKey`, que é o risco já registrado da #117;
- **painel aberto na versão antiga:** ele continua com o HMAC até o primeiro `401`, depois do qual navega a `/login`. O `index.html` é `no-store`, então essa navegação já carrega o painel novo, que manda para `/entrar`.

**Rota inexistente sem token responde `401`, e é intencional.** O `FallbackPolicy` do `apps/api` (`Program.cs` l. 131–134, `RequireAuthenticatedUser` mais `ServiceScopeRequirement`) vale também para o pedido que não casa com nenhuma rota: sem token, o `POST /auth/login` responde `401`, como `GET /rota-que-nao-existe` e o próprio `GET /agents`. Com token válido, a rota inexistente responde `404`. Isso não revela a quem chega sem token quais rotas existem, e fica assim. Medido em 01/11/2026 (`217-verificacao/7.3-sonda.txt`); a premissa anterior deste D8, de um `404` sem token, era falsa (convenção 9). O `401` sem token sai do handler HMAC, que recebe todo pedido sem `Authorization` (`IssuerTokenAuthentication.SelectScheme`), com o corpo vazio e **sem** `WWW-Authenticate` (`217-verificacao/7.3-sonda-cabecalhos.txt`).

**A ausência da rota se prova com o token de usuário do emissor**, obtido pelo fluxo do `IdentityIssuerUserFlow` (sessão, authorize com PKCE e troca), e **não** com o HMAC de operador: a #184 vai recusar o subject `operator` no HMAC, e o caso passaria a responder `401` por esse motivo, deixando de provar a ausência. Com o token do emissor, a rota antiga responderia `200` à credencial do bootstrap, e a ausente responde exatamente `404`.

**Rotas anônimas e checagem de boot:** a allowlist do `ValidateRouteAuthenticationClassification` fica com `/health`, `/auth/session`, `/connect/authorize`, `/connect/logout` e `/agents/{id:guid}/.well-known/agent-card.json`. O `AnonymousRouteReason.AuthEntryPoint` continua, usado pelo `/auth/session` e pelo authorize. O `RouteAuthenticationStartupFailureTests` troca `/auth/login` por uma rota inventada, para continuar provando a direção "allowlist desatualizada".

**Testes do `apps/api` e de `tests/`:**

| teste | o que acontece |
|---|---|
| `AuthLoginTests` (4 casos) | sai |
| `IdentityIssuerCoexistenceTests.HmacLogin_StillIssuesTheOperatorToken_AndItIsAcceptedOnAgents` | sai. O HMAC continua aceito, e isso já é provado pelo `TestAuthentication.AttachOperatorToken` em toda fixture |
| `RouteAuthenticationTests` (o caso do `/auth/login` anônimo) | troca para o `/auth/session` |
| `KnowledgeDocumentCatalogTests` (l. 405–408) | o token vem do `ITokenService`, como nas outras fixtures |
| `InboxOrchestratorRoundTrip.Tests` (`RoundTripFixture.cs` l. 395) | **os dois usos se separam** (decisão do dono, 01/11/2026, na parada da 8.1). Os 9 chamadores que usam o token no `apps/api` recebem o access token de usuário do emissor pelo `IssuerUserFlow` (`UserAccessTokenAsync`, escopo `openid buteco-api`). O caso que prova a validação do HMAC no `apps/inbox` sem rede recebe um HMAC de operador assinado pelo `ITokenService` do `apps/api` (`OperatorHmacToken`), e não de rota, e é renomeado de `OperatorToken_IssuedByApi_IsAcceptedByInboxWithoutNetworkCallToApi` para `OperatorHmacToken_SignedWithTheSharedKey_IsAcceptedByInboxWithoutNetworkCallToApi`. Trocá-lo pelo token do emissor o faria depender do JWKS buscado no `apps/api` e tiraria a única prova de ponta a ponta do HMAC no `apps/inbox`, que continua até a #182. A #184 ajusta o caso quando recusar o subject `operator`; ele sai com o HMAC na #182. Saem o `OperatorUsername` e o `Auth:OperatorUsername` da fixture |
| novo: `GoneLoginTests` (2 casos) | sem `Authorization`, o `POST /auth/login` com a credencial do bootstrap responde `401`, como qualquer rota inexistente; com o access token de usuário do emissor, responde exatamente `404`, onde a rota antiga responderia `200` |
| novo: renovação concorrente (D3) | fixa o H2 |

### D9. (g) O bootstrap depois da #217

**O que o `docs/deployment.md` diz hoje** (l. 419–422): "Depois da #217: entre uma vez, troque a senha do Admin pela tela de Minha conta (#219), e então `AUTH_OPERATOR_PASSWORD_HASH` e `AUTH_BOOTSTRAP_ADMIN_EMAIL` podem sair do `.env.prod`."

**O que está errado nisso, lido no código:**

- **A #219 não tem tela.** Ela entrega as rotas, e a tela de Minha conta é da #183. Entre a #217 e a #183, não há como trocar a senha pelo painel;
- **e não precisa trocar para tirar a variável.** O hash da variável só é lido quando não há nenhum usuário (`local-users`, "Primeiro Admin criado pelo bootstrap"). Depois do primeiro login, a linha do Admin já está em Argon2id, e a variável não é mais lida.

**O que a variável ainda custa:** ela guarda o PBKDF2 de 100.000 iterações **da senha atual do Admin**, seis vezes abaixo do recomendado para o próprio PBKDF2 (#180, achados). Enquanto a senha não muda, cópia do `.env.prod` ou de backup é material de ataque offline contra ela.

**Decisão** (P7, dono, 01/11/2026):

1. **A variável continua, só para o bootstrap**, com o mesmo nome. **Sai o `:?` do compose**, porque ela deixa de ser necessária depois do primeiro boot. Sem ela, numa instalação nova, o bootstrap já avisa "Nenhum usuário; defina Auth:BootstrapAdminEmail e Auth:OperatorPasswordHash…";
2. **O `docs/deployment.md` passa a orientar**: depois do primeiro login do Admin, que refaz o hash, tire `AUTH_OPERATOR_PASSWORD_HASH` e `AUTH_BOOTSTRAP_ADMIN_EMAIL` do `.env.prod` e dos backups dele. Troque a senha do Admin quando a tela de Minha conta existir (#183), porque cópias antigas do `.env.prod` guardam o hash fraco dela;
3. **Fora desta change, aguardando decisão do dono: #225** (aberta em 01/11/2026, sem milestone). Renomear para `AUTH_BOOTSTRAP_ADMIN_PASSWORD_HASH`, que diz o que ela é hoje; ou trocar o PBKDF2 por um hash Argon2id gerado offline. Os dois mudam a instalação nova, e nenhum é implementado aqui.

### D10. (h) Desenvolvimento: o proxy do Vite

```ts
server: {
  proxy: {
    '/auth':       { target: 'http://localhost:5017' },
    '/connect':    { target: 'http://localhost:5017' },
    '/.well-known':{ target: 'http://localhost:5017' },
  },
}
```

- **Sem `changeOrigin`.** O `apps/api` não lê o `Host`: o issuer vem da `PublicUrl:BaseUrl`, e o `retorno` só do caminho e da query (#180 D1). O cabeçalho `Origin` o proxy não muda em nenhum caso. Ele chega como `http://localhost:5173`, que é o `Identity:PanelBaseUrl` de `appsettings.Development.json`, aceito pelo `SessionRequestGuards`.
- **O cookie passa pelo proxy:** o `Set-Cookie` do `apps/api` chega ao navegador como resposta de `localhost:5173`. Sem `Domain`, ele fica no host `localhost`, e cookie não distingue porta. Sem `Secure` em `Development` (#180 D1). O `SameSite=Strict` não barra nada, porque toda navegação do fluxo é dentro de `localhost:5173`:
  - o authorize via proxy;
  - o `302` a `/entrar/retorno` em `http://localhost:5173` (`PanelBaseUrl`);
  - o logout via proxy.
- **O `Location: /entrar?retorno=…` é relativo**, e o navegador o resolve contra `localhost:5173`.
- **Os `request<T>` não mudam de endereço em desenvolvimento.** Eles continuam em `VITE_*_BASE_URL`, com CORS e `Authorization`, sem cookie. Só o fluxo do emissor vai pelo proxy, que é o que a #180 D3.4 decidiu.
- **O SPA não lê o discovery** (D6: sem `iss`, endpoints relativos). O `/.well-known` entra no proxy pela decisão da #180 D3.4, para a origem do painel servir o mesmo que o nginx serve em produção.
- **Conferência no fechamento**, pelo navegador real (Claude in Chrome), com os passos fixados pelo dono em 01/11/2026. O `npm run dev` não serve os cabeçalhos do nginx, então a conferência roda contra o stack local (D11), e o proxy do Vite é conferido à parte, pelo login no `npm run dev` e o `Set-Cookie` com `HttpOnly` e `SameSite=Strict` no painel de rede:
  1. login, e a URL sem `code` depois do retorno;
  2. recarga, sem pedir login;
  3. segunda aba, sem pedir login;
  4. aba duplicada pelo navegador, e as duas continuam depois de uma renovação (o access token vencido à mão, para não esperar 15 min);
  5. logout numa aba, e a outra indo a `/entrar`;
  6. a CSP sem violação no console, em todas as telas do painel.

### D11. (i) nginx de produção

- **`/entrar` e `/entrar/retorno` já caem no SPA:** nenhum bloco de API casa com `/entrar`, e o `location /` faz `try_files $uri /index.html`. Nada muda nos blocos.
- **Mudam só os cabeçalhos** (D2): a CSP e o `Referrer-Policy: no-referrer`, no `server` **e** repetidos no `location = /index.html`.
- **Conferência por curl** no stack local, no fechamento, com a saída no `tasks.md`, na forma da D3.5 da #180 (com o `resolver` do Podman, #212):

```
curl -si -H 'Sec-Fetch-Mode: navigate' http://localhost:<porta>/entrar
  → 200, shell do SPA, Cache-Control: no-store, Content-Security-Policy (a do D2), Referrer-Policy: no-referrer
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/entrar/retorno?code=x&state=y'
  → 200, shell do SPA, os mesmos três cabeçalhos
curl -si -H 'Sec-Fetch-Mode: navigate' http://localhost:<porta>/agents
  → 200, shell do SPA pelo rewrite de navegação, os mesmos três cabeçalhos
curl -si -X POST http://localhost:<porta>/auth/login -H 'Content-Type: application/json' -d '{}'
  → 401 do apps/api, corpo vazio e sem Content-Type: text/html (rota inexistente sem token, D8);
    o Content-Length: 0 não é afirmado, porque pelo nginx o tamanho pode chegar de outro jeito
curl -si -H 'Sec-Fetch-Mode: navigate' 'http://localhost:<porta>/connect/authorize?client_id=buteco-painel&…'
  → 302 Location: /entrar?retorno=… (a D3.5 da #180, repetida)
```

### D12. (j) Testes

**O que existe:** 138 arquivos e 1839 casos no `apps/frontend` (baseline, abaixo). Os que tocam o login:

- `LoginPage.test.tsx`;
- `ProtectedRoute.test.tsx`;
- `router.test.tsx` (o `401` leva a `/login`);
- o caso "em 401, limpa o token e redireciona para /login" de `agentsApi`, `knowledgeBasesApi`, `knowledgeIndexApi` e dos outros clientes;
- o "401 NÃO limpa o token" de `connectorsApi.test.ts` e `KnowledgeBaseCreatePage.sync.test.tsx`;
- 19 arquivos importam o `src/auth/token.ts`.

**O que entra, com o guarda de cada um** (convenção 15: cada guarda é visto reprovar contra o defeito reintroduzido, com a saída no `tasks.md`):

| teste | tipo | defeito reintroduzido para ver reprovar |
|---|---|---|
| PKCE: o challenge é o base64url do SHA-256 do verifier (vetor do apêndice B da RFC 7636) e o verifier tem 43 caracteres do alfabeto | unidade | challenge com `+/=`, ou o verifier no lugar do challenge |
| `state`: aleatório, conferido, de uso único, com prazo de 10 min | unidade | conferência retirada; login em andamento não apagado |
| `retorno`: aceita só o que começa por `/connect/authorize?`; recusa ausente, absoluto, `//`, `/\`, outro caminho (`/connect/authorizex?`), sem query e codificado duas vezes; e devolve exatamente a string conferida | unidade, tabela | o prefixo sem o `?`; decodificar antes de conferir; devolver o valor decodificado de novo. (O defeito previsto antes, "`startsWith` sem a conferência da origem", não existe: o prefixo já fixa origem e caminho, e a conferência saiu, D6) |
| `voltarPara`: só caminho relativo do painel | unidade | aceitar `//host` |
| **duas renovações simultâneas não derrubam a sessão** | integração do módulo, com duas "abas" (duas instâncias sobre o mesmo storage), um gerenciador de lock falso **com exclusão real** e um `/connect/token` falso **com rotação estrita**: o refresh resgatado de novo responde `invalid_grant` e revoga a cadeia | o lock retirado: a segunda aba apresenta o refresh velho e a sessão cai |
| a renovação relê o registro dentro do lock e reaproveita a de outra aba | integração do módulo | ler fora do lock |
| a repetição do refresh para em 25 s | unidade, relógio falso | sem prazo |
| `503` na renovação não apaga o registro; `invalid_grant` apaga | unidade | `503` tratado como fim |
| `401` renova e repete uma vez; o segundo `401` encerra a sessão com `voltarPara` (api e inbox) e não encerra no `apps/connectors` | unidade, por cópia do `request<T>` | encerrar no primeiro `401`; encerrar no `apps/connectors` |
| `QueryClient`: `503` repete com o `Retry-After`; mutação não repete | unidade | repetir mutação |
| evento `storage` com o registro apagado leva a aba a `/entrar` | integração | sem o ouvinte |
| logout: apaga antes de navegar, com `id_token_hint` e `post_logout_redirect_uri` | unidade | navegar sem apagar |
| **logout durante uma renovação:** a aba A está com o pedido ao `/connect/token` em voo (resposta segura por uma promessa do teste), a aba B sai, a resposta de A chega, e o registro termina **ausente**. E o hint de B é o `idToken` novo de A | integração do módulo, duas "abas", o lock falso com exclusão real | o logout sem o lock: A grava depois de B apagar, e o registro termina presente |
| fim de sessão sob o lock apaga só o registro que falhou | unidade | apagar sem comparar: o login novo de outra aba some |
| `/entrar`: estados `401`, `403`, `423` com N minutos do `Retry-After`, `429`, `503`, `sessao-expirada`, `falha-na-entrada` | componente | N fixo em 5; o aviso do `falha-na-entrada` tirado |
| `/entrar/retorno`: `state` errado não troca código; `error` mostra o aviso; `invalid_grant` volta ao `/entrar` | componente | trocar antes de conferir |
| `/entrar/retorno`: a URL fica sem query, por `REPLACE`, **antes** do `POST /connect/token`, e a troca usa o código capturado; vale também para `error` e `state` errado | componente, `createMemoryRouter` (`location.search` e `historyAction`) e o `fetch` falso, que confere a URL no instante do pedido | tirar o `navigate`; navegar com `push` |
| **fluxo inteiro**: rota protegida → `/entrar` → `204` → authorize (navegação simulada) → `/entrar/retorno` → token → rota protegida carrega, com o `Authorization` do emissor | integração com `createMemoryRouter` e `fetch` falso | — (é a costura, e os guardas acima cobrem as partes) |
| CSP: o hash no `nginx.conf` é o do script do `index.html`, e a CSP e o `Referrer-Policy: no-referrer` estão no `server` e no `location = /index.html` | unidade, sobre o texto do `nginx.conf` | mudar o script sem o hash; tirar o `Referrer-Policy` do `location = /index.html` |
| `apps/api`: o mesmo refresh reapresentado 10 s depois do resgate dá `200` (H2). **Em sequência, e não simultâneo:** o caso previsto antes, com `Task.WhenAll`, não testava o H2. A tolerância só é consultada quando o refresh já está marcado como resgatado (`OpenIddictServerHandlers.Protection.cs` l. 1245–1247, o `HasStatusAsync(token, Statuses.Redeemed)` que chama o `IsReusableAsync`; e l. 1366–1369, a comparação da data do resgate mais a tolerância com o relógio), e dois pedidos de fato simultâneos passam os dois pela validação antes do resgate. Medido na 2.1: aquele caso aprovou também com a tolerância zerada (correção aceita pelo dono em 01/11/2026) | integração | `SetRefreshTokenReuseLeeway(TimeSpan.Zero)` |
| `apps/api`: `POST /auth/login` sem token → `401`, como qualquer rota inexistente (D8); com o access token de usuário do emissor → exatamente `404` | integração | rota de volta: o caso com token reprova pelo `200` |

**O que a stack não permite:** o `jsdom` não tem Web Locks (medido acima) nem várias abas reais. A coordenação é testada pelo módulo com dependências injetadas, e a prova com abas reais é a conferência no navegador do D10. O fluxo contra o `apps/api` de verdade é o critério de aceite, conferido à mão no fechamento.

### D13. A forma do módulo de sessão, e a convenção 7

A convenção 7 diz que cada feature mantém o próprio `request<T>`, e que a preocupação transversal (token) entra "como módulo fino que cada `request<T>` importa, não como cliente comum".

**Proposta:** `src/auth/` exporta `sessionFetch(input, init, { endsSessionOn401 })`. É o `fetch` com o token anexado, a renovação antes do pedido, o `401` com uma nova tentativa e o fim de sessão. Cada `request<T>` troca o `fetch` por ele, e continua dono da URL base, do `ApiError`, da leitura do corpo e do `204`.

- **Por que não um cliente comum:** ele não monta URL, não lê corpo, não cria erro. A diferença de hoje é que o módulo passa a fazer mais do que ler o storage, porque renovar com repetição e lock não cabe em cada cópia.
- **Alternativa:** exportar só as peças (`getAccessToken()`, `renewAfter401(token)`, `endSession()`) e repetir o laço "renova, repete uma vez" nas 7 cópias. São ~15 linhas por cópia, e a regra mais delicada da change em 7 lugares.
- **O `01` registra a leitura** na convenção 7: módulo transversal que envolve o `fetch` sem conhecer o contrato de nenhuma API.

## Árvore de pastas proposta

```
apps/frontend/
├── vite.config.ts                         # + server.proxy (D10)
├── .env.example                           # comentário do proxy
├── deploy/nginx.conf                      # + CSP e Referrer-Policy no server e no /index.html (D2)
├── src/
│   ├── auth/
│   │   ├── token.ts                       # SAI
│   │   ├── pkce.ts                        # verifier, challenge, state (D6)
│   │   ├── pkce.test.ts
│   │   ├── sessionStore.ts                # registro buteco.sessao em localStorage; evento storage (D2, D3)
│   │   ├── sessionStore.test.ts
│   │   ├── renewal.ts                     # renovação sob lock, repetição em 25 s (D3)
│   │   ├── renewal.test.ts                # inclui as duas renovações simultâneas
│   │   ├── loginFlow.ts                   # beginLogin, login em andamento, retorno, voltarPara, troca do código (D1, D6)
│   │   ├── loginFlow.test.ts
│   │   ├── sessionFetch.ts                # fetch com token, 401 com nova tentativa, fim de sessão (D5, D13)
│   │   ├── sessionFetch.test.ts
│   │   ├── logout.ts                      # D7
│   │   ├── logout.test.ts
│   │   └── transient.ts                   # Retry-After e a política de 503 do QueryClient (D4)
│   ├── app/
│   │   ├── ProtectedRoute.tsx             # registro, ou /entrar com voltarPara
│   │   ├── ProtectedRoute.test.tsx
│   │   ├── routes.tsx                     # /entrar, /entrar/retorno, /login → /entrar
│   │   ├── router.test.tsx
│   │   ├── queryClient.ts                 # retry e retryDelay do 503 (D4)
│   │   └── queryClient.test.ts
│   ├── components/layout/AppShell.tsx     # rodapé do protótipo: avatar, nome e e-mail, tema e Sair (D7)
│   └── features/
│       ├── auth/
│       │   ├── api/sessionApi.ts          # POST /auth/session (sai authApi.ts, useAuth.ts, types/auth.ts)
│       │   ├── components/EntrarForm.tsx  # era LoginForm
│       │   ├── pages/EntrarPage.tsx       # era LoginPage
│       │   ├── pages/EntrarPage.test.tsx
│       │   ├── pages/EntrarRetornoPage.tsx
│       │   └── pages/EntrarRetornoPage.test.tsx
│       ├── agents/api/agentsApi.ts        # sessionFetch (e os testes de cada cópia)
│       ├── insights/api/insightsApi.ts
│       ├── knowledge-bases/api/knowledgeBasesApi.ts
│       ├── knowledge-bases/api/connectorsApi.ts   # endsSessionOn401: false
│       ├── mcp-servers/api/mcpServersApi.ts
│       ├── channels/api/channelsApi.ts
│       └── sessions/api/sessionsApi.ts

apps/api/
├── src/Buteco.Api/
│   ├── Auth/Endpoints/AuthEndpoints.cs    # SAI
│   ├── Auth/Requests/LoginRequest.cs      # SAI
│   ├── Auth/Responses/LoginResponse.cs    # SAI
│   ├── Auth/OperatorCredentialOptions.cs  # SAI
│   ├── Auth/TokenSigningOptions.cs        # − OperatorTokenLifetime
│   ├── Users/Bootstrap/UserBootstrapOptions.cs   # SectionName "Auth"
│   ├── Program.cs                         # − Configure, − MapAuthEndpoints, − "/auth/login"
│   ├── appsettings.json, appsettings.Development.json   # − OperatorUsername
└── tests/Buteco.Api.Tests/
    ├── AuthLoginTests.cs                  # SAI
    ├── Identity/IdentityIssuerCoexistenceTests.cs   # − o caso do login HMAC
    ├── Identity/RefreshRotationTests.cs   # + renovação concorrente (H2)
    ├── RouteAuthenticationTests.cs, RouteAuthenticationStartupFailureTests.cs
    ├── Knowledge/KnowledgeDocumentCatalogTests.cs
    └── Support/TestAuthentication.cs, Support/ApiFactoryFixture.cs   # − OperatorUsername

tests/InboxOrchestratorRoundTrip.Tests/Support/RoundTripFixture.cs   # token pelo IssuerUserFlow

docker-compose.prod.yml, .env.prod.example, .env.example   # − AUTH_OPERATOR_USERNAME; hash sem :? (D9)
```

Nada em `libs/`: a sessão é do painel, e a remoção é do `apps/api`.

## Tamanho e divisão

**Âncoras medidas** (convenção 18), só código, sem `openspec/` nem `.md`, por `git diff --numstat`:

| PR | issue | produção | testes |
|---|---|---|---|
| #215 | #208 | 1.286 linhas, 26 arquivos | 2.459 linhas, 29 arquivos |
| #224 | #180 (recortada) | **+2.036 / −141**, 45 arquivos (`3bb8236..051e944`, sem migrations) | **+2.984 / −237**, 44 arquivos |

A #180 projetou 1.300 / 2.400 e entregou 2.036 / 2.984: **+57 % na produção e +24 % nos testes.** A mesma projeção dava 500 / 500 para a #217.

**Projeção por componente**, sobre este design. Ela é rascunho e erra para baixo por construção (convenção 18), porque a verificação no navegador ainda não rodou. A régua de comentário da convenção 18 vale aqui: D3 e D6 carregam razões citadas (H1–H5, os 25 s, a origem do `retorno`), e cada uma custa ~26 linhas de comentário.

| componente | produção | testes |
|---|---|---|
| `src/auth/`: PKCE, registro, renovação sob lock, fluxo de login, `sessionFetch`, logout, `503` | ~650 | ~900 |
| `/entrar`, `/entrar/retorno`, `ProtectedRoute`, rotas, "Sair" | ~350 | ~450 |
| as 7 cópias do `request<T>` e os 19 arquivos que importam o `token.ts` | ~120 | ~350 (os casos de `401` reescritos) |
| `queryClient`, `vite.config.ts`, CSP | ~60 | ~60 |
| **`apps/frontend`** | **~1.180** | **~1.760** |
| `apps/api`: remoção | +~20 / −~200 | +~120 / −~250 |
| `tests/`: fixture do acordo | ~20 | — |
| **total** | **~1.220 escritas, −~200** | **~1.880 escritas, −~250** |

**Conclusão: cabe numa change, abaixo da #180 e na ordem da #208.** Não proponho divisão.

- **Remoção no `apps/api` junto:** ela é quase toda subtração, e separá-la deixaria um PR intermediário com o `POST /auth/login` sem consumidor nenhum, que é exatamente o que a convenção 25 manda tirar junto.
- **"Tela não leva backend" (convenção 1):** o `/entrar` é tela, e a remoção é backend. A exceção já foi aceita na divisão da #180 ("o cliente OIDC do painel não é tela, mas também não é backend"), e o backend que entra aqui não serve à tela: só tira o que ela deixa sem uso.
- **Divisão possível, registrada e não adotada:** (1) `src/auth/` e as 7 cópias, ainda com o `LoginPage` de hoje no `POST /auth/login`; (2) `/entrar`, `/entrar/retorno`, logout e a remoção. Ela não é recomendada: (1) teria a renovação e o lock sem nenhum token que os use, e seria testável só por dublês.

## Risks / Trade-offs

- **[Refresh legível por script e em repouso no navegador por até 14 dias]** → CSP e `Referrer-Policy` (D2), nenhum HTML vindo de dado, por lint (`no-restricted-syntax` e `no-restricted-imports`). O backend do painel com cookie `HttpOnly` fica registrado como saída, se o risco pedir.
- **[Resposta perdida na renovação, e a nova tentativa depois de 30 s revoga a cadeia (H3)]** → repetição só dentro dos 25 s (D3). Fora disso, o usuário vê "Sua sessão expirou" uma vez, e o log `6228` do `apps/api` registra a revogação.
- **[Aba congelada pelo navegador segura o lock]** → o Web Lock é solto quando a aba fecha ou o processo morre. Uma aba só suspensa, com a requisição em voo, segura o lock até a resposta ou o timeout do `fetch`. A renovação tem prazo próprio de 25 s, e depois dele solta.
- **[O `400` do catálogo logo depois do deploy aparece como texto puro]** → janela de segundos, recarregar resolve, documentado (D1). Alternativas registradas.
- **[O "Sair" espera a renovação em voo]** → no máximo 35 s, com "Saindo…" (D3). É o preço de o logout nunca perder para a renovação.
- **[O código fica no histórico global do navegador]** → a entrada da aba é trocada (D6), e o código restante é de uso único, de 5 min e preso ao PKCE.
- **[A regra do `503` muda a repetição de todas as consultas]** → só para `503`, e as telas com `retry` próprio não mudam (D4).
- **[HTTP puro fora de `localhost`]** → o painel avisa no `/entrar` (D3). O cookie `Secure` já exigia HTTPS desde a #180.
- **[Painel aberto na versão antiga depois do deploy]** → continua com o HMAC até 30 min. No primeiro `401`, a navegação a `/login` carrega o painel novo (D8).

## Migration Plan

1. **Deploy:** `api` e `frontend` juntos, depois do `migrator` (sem migration nesta change). O `inbox` e o `connectors` não mudam.
2. **`.env.prod`:** `AUTH_OPERATOR_USERNAME` deixa de ser lida, e pode sair. `AUTH_OPERATOR_PASSWORD_HASH` deixa de ser obrigatória no compose (D9).
3. **Conferir:**
   - os `curl` do D11;
   - o critério de aceite: entrar no painel e abrir a lista de agentes (`apps/api`), a de canais (`apps/inbox`) e uma sincronização (`apps/connectors`), com o token do emissor.
4. **Depois do primeiro login do Admin:** tirar o hash e o e-mail do bootstrap do `.env.prod` (D9).
5. **Rollback:** a imagem anterior do `api` e do `frontend`. Quem entrou pelo emissor fica com o registro no `localStorage`, que o painel antigo não lê. Ele pede `/login`, que volta a existir no `api` antigo com `AUTH_OPERATOR_USERNAME`. Por isso o rollback precisa da variável de volta no `.env.prod`, e o `docs/deployment.md` diz isso.

## Decisões do dono (01/11/2026)

Na revisão do proposal e do design, o dono aceitou **P1 a P9 como recomendadas**:

| | decisão | onde |
|---|---|---|
| P1 | o SPA começa pelo `/entrar` e só navega ao authorize depois do `204`; o `400` do catálogo logo depois do deploy fica aceito e documentado | D1 |
| P2 | tokens no `localStorage`, no registro `buteco.sessao`, com a renovação coordenada | D2, D3 |
| P3 | a CSP entra nesta change, e com ela o `Referrer-Policy: no-referrer` (revisão) | D2, D11 |
| P4 | o `QueryClient` repete `503` só em consulta, até 3 vezes, esperando o `Retry-After` | D4 |
| P5 | o `401` do `apps/connectors` continua sem encerrar a sessão, depois de renovar e repetir | D5 |
| P6 | "Sair" mínimo no rodapé da casca, com o nome do id token, até a #183 | D7 |
| P7 | `AUTH_OPERATOR_PASSWORD_HASH` só para o bootstrap, sem `:?`; nome e formato na #225 | D9 |
| P8 | "Esqueceu a senha?" e o rodapé do convite fora, até a #181 e a #183 | D6 |
| P9 | `sessionFetch` único, com a leitura registrada na convenção 7 | D13 |

- **Isso inclui os estados `bloqueado` e `sessao-expirada` nesta change**, e o corpo da #217 foi atualizado em 01/11/2026 (convenção 9).
- **Ajustes da mesma revisão**, já incorporados:
  - todo escritor do registro sob o lock da renovação, com o teste do logout durante uma renovação (D3, D7, D12);
  - a troca da URL do `/entrar/retorno` por `replace`, e o `Referrer-Policy: no-referrer` (D2, D6, D11, D12);
  - os passos da conferência no navegador (D10).

## Registros (convenção 23)

- **#189, comentário de 01/11/2026:** o painel não confere o `iss` da resposta do authorize (D6), e conferir passa a ser obrigatório quando houver o segundo emissor.
- **#225, aberta em 01/11/2026, sem milestone, aguardando decisão do dono:** renomear `AUTH_OPERATOR_PASSWORD_HASH` para dizer que é só do bootstrap, ou trocar por um hash Argon2id gerado offline (D9, item 3), com o PBKDF2 de 100.000 iterações e o risco das cópias do `.env.prod`.

## Open Questions

Nenhuma.

## Baseline

No worktree limpo `../buteco-agentes-217-baseline`, em `ca2bd71`. O protocolo é o da #180: `DOCKER_HOST` do Podman, `TESTCONTAINERS_RYUK_DISABLED`, `DOTNET_SYSTEM_NET_DISABLEIPV6` e `TZ=America/Sao_Paulo`. **Antes de cada suíte**, o script (`~/.cache/buteco-agents/bin/suite-217.sh`) esperou `CPU_Speed_Limit = 100` e um load abaixo do limite (6 no frontend, 8 nas outras), acertou o relógio da VM e registrou o `podman ps`. As saídas completas ficam em `~/.cache/buteco-agents/217-baseline/`.

| suíte | resultado | duração | estado |
|---|---|---|---|
| `apps/frontend` (`npm test`) | **1839/1839**, 138 arquivos | 151 s | início: load 5,61, CPU 100, `podman ps` vazio. Fim: load 25,75, **CPU 48** |
| `apps/api` | **1009/1009 + 1 ignorado** | 217 s (3m15s de teste) | início: load 6,19, CPU 100, `podman ps` vazio. Fim: load 13,21, **CPU 48** |
| `tests/InboxOrchestratorRoundTrip.Tests` | **12/12** | 51 s | início: load 7,25, CPU 100. Fim: CPU 55 |
| `tests/ApiConnectorsRoundTrip.Tests` | **8/8** | 22 s | início: load 4,41, CPU 100. Fim: CPU 83 |

- **Iguais por número aos fechamentos da #180** (1009 + 1, 12, 8). A comparação por nome fica para o fechamento, contra os `.trx` desta pasta.
- **A CPU caiu durante as quatro suítes**, sempre depois de começar em 100. Os resultados não mudam por isso, mas as durações não servem de referência de desempenho.
- **A sonda do `jsdom` (Context) rodou durante a suíte do `apps/api`**, por alguns segundos, fora dos dois worktrees.
- `apps/inbox` e `apps/connectors` não foram medidos: nenhum teste deles usa o `POST /auth/login` (`git grep` em 31/10/2026). O `apps/connectors` não muda. **O `apps/inbox` muda só num comentário** (`ServiceTokenDelegatingHandler.cs` l. 17, que citava o `Auth:OperatorTokenLifetime` como se existisse; achado da 7.4, decisão do dono de 01/11/2026). Nenhum teste lê comentário, então a suíte dele fica fora da medição, e no fechamento basta o build do `apps/inbox` sem avisos novos.
