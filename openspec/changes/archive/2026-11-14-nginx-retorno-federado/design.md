## Context

A #285 é o defeito de entrega da #189 achado na homologação da M3: o retorno do Google cai no SPA. A change segue o
molde da `nginx-prefixo-applications` (#268, arquivada em `2026-11-10`): o guarda antes da correção, a correção
mínima no `nginx.conf`, e a conferência com a imagem de verdade contra a de antes.

**Fontes lidas** (14/11/2026, `main` em `f503aca`, o merge do PR #284):
- o corpo da #285 e o design, as tasks e a spec da `nginx-prefixo-applications`;
- `apps/frontend/deploy/nginx.conf`, `apps/frontend/vite.config.ts` e `apps/frontend/src/app/nginxHeaders.test.ts`;
- `tests/Shared/NginxRouting.cs` e, no `apps/api`, `NginxRouteCoverageTests.cs`, `NginxRoutingReaderTests.cs` e
  `Support/GoogleFlow.cs` (o `FederatedLoginFixture`);
- no `apps/api`: `FederatedLogin.cs`, `FederatedLoginEndpoints.cs`, `FederatedInvitationEndpoints.cs`,
  `IdentityIssuerServiceCollectionExtensions.cs`, `IdentityIssuerStartupValidation.cs`, `IdentityIssuerEndpoints.cs`,
  `Clients/IdentityClients.cs`, `Flow/AuthorizeEndpoint.cs`, `Flow/EndSessionEndpoint.cs` e o `Program.cs`.

Caminhos do backend a partir de `apps/api/src/Buteco.Api/`.

### O caminho até o defeito

1. O cliente OIDC dos provedores federados registra o retorno em
   `IdentityIssuerServiceCollectionExtensions.cs` l. 178 (`SetRedirectionEndpointUris(Relative(FederatedLogin.CallbackPath))`),
   com `FederatedLogin.CallbackPath = "/auth/federated/callback"` (`Identity/Federated/FederatedLogin.cs` l. 11). A URI
   registrada no Google é `https://<PUBLIC_DOMAIN>/auth/federated/callback` (`docs/deployment.md`, a seção da #189).
2. O Google devolve o navegador com um `302` para essa URI: uma navegação de topo, `Sec-Fetch-Mode: navigate`.
3. No `nginx.conf`, `/auth/federated/callback` casa com o bloco de regex do `apps/api` (l. 97), que começa pelo desvio
   de navegação (l. 98–100): o `rewrite ^ /index.html last` serve o shell do SPA, e o React Router, sem rota para o
   caminho, mostra o 404 dele.
4. O `/connect/authorize` e o `/connect/logout`, as outras navegações de topo ao `apps/api`, têm `location =` próprio
   sem o desvio (l. 138–152), e o `location =` vence a regex.

**Por que passou:**
- as conferências 13.1 da #189 e 10.1 da #274 foram por `curl`, que não manda `Sec-Fetch-Mode`, e o pedido ia ao
  `apps/api` direto (a 10.1, com o `apps/api` no contêiner da imagem, sem o nginx do frontend no caminho);
- o `NginxRouteCoverageTests` (#268) confere os prefixos, os blocos exatos nomeados e o desvio em todo bloco de regex;
  ele exige o desvio, e nada nele pergunta se um caminho servido é aberto por navegação de topo;
- no desenvolvimento, o proxy do Vite leva `/auth` inteiro ao `apps/api` (`vite.config.ts` l. 39), sem desvio: o
  retorno funciona no `localhost:5173`, e o defeito só existe atrás do nginx do stack.

**O risco do `code` na URL do SPA**, enquanto o defeito vale: o `code` vale uma vez; trocá-lo exige o `code_verifier`
do PKCE, que está no state token, guardado no servidor, e o cookie do fluxo (`buteco-fluxo-externo.<nonce>`,
`HttpOnly`, só no caminho do retorno); e o `Referrer-Policy: no-referrer` do `server` e do `location = /index.html`
impede que os pedidos do SPA o levem no `Referer`.

## Goals / Non-Goals

**Goals:**
- o retorno do Google chega ao `apps/api` numa navegação real, e o login, o aceite e o conectar pelo Google voltam;
- um guarda que reprova quando um caminho de navegação de topo do `apps/api`, tirado do código, não tem bloco exato
  sem o desvio, e que reprova contra o `nginx.conf` de hoje;
- o inventário dos caminhos que o navegador abre no `apps/api` por navegação de topo, com o que fica fora;
- a conferência com navegação na documentação de deploy, ao lado das conferências por `curl`.

**Non-Goals:**
- mudar rota, resposta ou tela de qualquer app;
- a #281 (a falha do conectar que volta ao `/entrar` em vez de `/account`);
- o proxy do Vite: no desenvolvimento ele não tem desvio, e o retorno funciona;
- um teste de ponta dentro das suítes: elas não constroem imagem (D4).

## Decisions

### D1. A correção: um `location = /auth/federated/callback`, no molde do `/connect/authorize`

O bloco novo fica logo depois do `location = /connect/logout`, no grupo dos blocos exatos do emissor, com o corpo do
`/connect/authorize`: `set $api_upstream api:8080`, `proxy_pass http://$api_upstream` e os três `proxy_set_header`
(`Host`, `X-Real-IP`, `X-Forwarded-For`), **sem** o `if ($http_sec_fetch_mode = navigate)`. O comentário diz:
- que o caminho é o retorno do cliente OIDC dos provedores federados (`FederatedLogin.CallbackPath`, #189), a URI
  registrada no Google;
- que o provedor devolve o navegador numa navegação de topo, e que pelo bloco de `/auth` ele cairia no desvio e
  receberia o shell do SPA (#285);
- que o `location =` vence a regex de `/auth`, e que o resto de `/auth` (fetch do painel) continua no desvio;
- que o guarda é o `NginxNavigationEndpointsTests` do `apps/api`.

O `location =` casa só o caminho: a query (`state`, `code`, `iss`) não entra na comparação. O bloco não declara
`add_header`, e por isso herda a CSP e o `Referrer-Policy: no-referrer` do `server`, que vão também no `302` do
`apps/api`.

O comentário do topo do arquivo, na lista das exceções, passa de "os quatro `location =` do emissor OIDC" para os
cinco blocos exatos do `apps/api`, com o retorno do cliente OIDC.

**Alternativas descartadas:**
- um bloco de regex `^/auth/federated/` sem o desvio: abriria à navegação de topo também o `start`, o `providers` e
  o `refusal`, e qualquer rota nova sob o prefixo;
- tirar `auth` do bloco com o desvio: `/auth` não é rota de página hoje, mas a spec exige o desvio em todo bloco de
  prefixo de API, e o bloco exato é a forma que o arquivo já usa para as navegações de topo.

### D2. O inventário: o que o navegador abre no `apps/api` por navegação de topo

Procurado no código: as URIs do cliente OIDC (`RedirectionEndpointUris` e `PostLogoutRedirectionEndpointUris`), os
endpoints do emissor em passthrough, as respostas de redirecionamento (`Results.Redirect`) e os `MapGet` dos três
apps.

| caminho | de onde vem | no nginx | nesta change |
|---|---|---|---|
| `/auth/federated/callback` | `FederatedLogin.CallbackPath`; o único `RedirectionEndpointUris` do cliente OIDC (`IdentityIssuerServiceCollectionExtensions.cs` l. 178); o Google devolve o navegador para ele | o bloco de `/auth`, **com** o desvio | **o defeito**: ganha `location =` (D1) |
| `/connect/authorize` | endpoint do emissor em passthrough (`IdentityIssuerEndpoints.Expected`); o painel navega até ele | `location =`, sem o desvio | fica |
| `/connect/logout` | endpoint do emissor em passthrough; o painel navega até ele no sair | `location =`, sem o desvio | fica |
| `PostLogoutRedirectionEndpointUris` do cliente OIDC | nenhum: o cliente configura só o retorno (l. 178) | — | nada a fazer; o guarda (D3) cobre um que venha a existir |

**Fica fora, com o motivo:**
- **os destinos do SPA para onde o `apps/api` redireciona** depois do retorno, do authorize e do logout: `/entrar?…`
  (`AuthorizeEndpoint.LoginPath`; `FederatedLoginEndpoints.cs` l. 219 e 309; `FederatedInvitationEndpoints.cs`
  l. 182; `EndSessionEndpoint.cs` l. 70; `AuthorizeEndpoint.cs` l. 67), `/convite?recusa=…`
  (`FederatedInvitationEndpoints.cs` l. 209) e `/account?conectado=…` ou `?recusa=…`
  (`FederatedLoginEndpoints.cs` l. 296 e 312). São páginas do painel. O `/entrar` e o `/convite` não são prefixo de
  API e caem no fallback do SPA; o `/account` **é** prefixo de API, e a navegação de topo nele tem de continuar no
  desvio, para o SPA ler a query;
- **os retornos do cliente `buteco-painel`** no emissor (`Clients/IdentityClients.cs` l. 36–37): `/entrar/retorno` e
  `/entrar`, páginas do SPA;
- **os outros `MapGet` do `apps/api`**: JSON lido por `fetch` do painel ou por máquina. O agent card
  (`/agents/{id}/.well-known/agent-card.json`) é lido por cliente A2A, sem `Sec-Fetch-Mode`; aberto no navegador,
  mostra o SPA, como hoje, sem dano;
- **`/auth/federated/start`, `/auth/federated/providers`, `/auth/federated/refusal` e
  `/auth/invitation/federated/{chave}/start`**: `POST` ou `GET` por `fetch` do painel, e continuam no desvio;
- **o `apps/inbox` e o `apps/connectors`**: nenhuma rota aberta por navegação de topo; os webhooks são `POST` de
  máquina, e o único `callback` nos fontes é o modelo do webhook do Telegram (`TelegramWebhookModels.cs`), dado e
  não rota.

### D3. O guarda: os caminhos de navegação do código, cada um com `location =` sem o desvio

**Onde:** um teste novo no `apps/api`, `NginxNavigationEndpointsTests`, sobre o host com um provedor Google
configurado (o `FederatedLoginFixture` de `Support/GoogleFlow.cs`). Sem provedor, o cliente OIDC nem é registrado, e a
lista do código sairia vazia; com ele, o teste lê a composição real.

**A lista, tirada do código:**
- `FederatedLogin.CallbackPath`;
- os caminhos de `RedirectionEndpointUris` e de `PostLogoutRedirectionEndpointUris` das `OpenIddictClientOptions`
  compostas pelo host (`IOptionsMonitor<OpenIddictClientOptions>`);
- os endpoints do emissor em passthrough (`IdentityIssuerEndpoints.Expected` com `Passthrough: true`: o
  `/connect/authorize` e o `/connect/logout`), que o painel abre por navegação de topo (Q1).

O teste exige que a lista do cliente não saia vazia: um host sem o cliente registrado não deixa o guarda verde por não
ter o que conferir. A `ValidateIdentityIssuerEndpoints` já amarra, no boot, os caminhos do cliente à
`IdentityIssuerEndpoints.ExpectedClient`; o teste lê as opções, e não essa lista, para conferir o que o cliente de
fato usa.

**A conferência**, num método novo do leitor, `NginxRouting.FindNavigationProblems(paths, locations, upstream)`: para
cada caminho, um `location =` com o mesmo padrão, encaminhando ao `upstream`, sem o desvio. Senão, uma linha que nomeia
o caminho e diz o que falta ("cai no shell do SPA numa navegação de topo"): sem bloco exato, bloco exato de outro
upstream, ou bloco exato com o desvio. Todas as linhas numa falha só, como no teste de cobertura.

**O `NginxRouteCoverageTests` do `apps/api`** ganha o `/auth/federated/callback` na lista `ExactBlocks`, com o
motivo ("o retorno do cliente OIDC: o provedor devolve o navegador numa navegação de topo, sem o desvio; o
`location =` vence a regex de `auth`"). A lista é escrita à parte, a partir da spec (convenção 15, sexta forma), e
continua reprovando bloco exato fora dela. Sem a entrada, o bloco novo reprovaria o teste de cobertura.

**Os casos do leitor** (`NginxRoutingReaderTests`, sobre um texto forjado, como os da #268): o caminho com o bloco
exato certo não dá problema; sem bloco exato, com o desvio e de outro upstream, cada um dá a linha que nomeia o
caminho.

**A ordem** (o D6 da #268): o teste novo entra antes da correção e reprova contra o `nginx.conf` de hoje, nomeando
`/auth/federated/callback`, e só ele (o **defeito real**). Só depois entra o bloco.

### D4. A conferência com o nginx de verdade

No molde do D5 da #268, por script, com as saídas guardadas:
- **as imagens:** `podman build` do `apps/frontend/Dockerfile` na árvore da change e na da baseline (`f503aca`), e
  `nginx -t` dentro das duas;
- **o `apps/api` local** (`dotnet run`, `TZ=America/Sao_Paulo`, `DOTNET_SYSTEM_NET_DISABLEIPV6=1`, um banco
  descartável), **com um provedor Google de credenciais falsas** configurado (`Identity__FederatedProviders__…`):
  o state forjado não decifra, a falha passa à rota pelo passthrough dos erros, e a intenção ilegível manda ao login.
  Nenhum pedido sai para o Google no retorno com o state inválido; se sair, fica registrado. A imagem do frontend e
  um contêiner `api` que encaminha a porta `8080` ao `apps/api` do host, numa rede do Podman; o `resolver` trocado só
  dentro do contêiner do frontend (o contorno da #212), registrado;
- **os pedidos à imagem da change:**
  - `GET /auth/federated/callback?state=x&code=y` com `Sec-Fetch-Mode: navigate` → `302`,
    `Location: /entrar?recusa=falha`, do `apps/api`, com `Referrer-Policy: no-referrer`;
  - o mesmo sem `Sec-Fetch-Mode` → o mesmo `302`;
  - `GET /auth/federated/providers` com `Sec-Fetch-Mode: navigate` → `200`, `text/html`, `Cache-Control: no-store`
    (o resto de `/auth` continua no desvio), e sem o cabeçalho → o JSON do `apps/api`;
  - `GET /account?conectado=x` com `Sec-Fetch-Mode: navigate` → o `index.html` (o destino do SPA continua no SPA);
  - `GET /auth/federated/callbackxyz` com `Sec-Fetch-Mode: navigate` → o `index.html` (o bloco exato não casa por
    prefixo);
- **o contraste:** a imagem da baseline responde ao primeiro pedido `200` com `text/html`, o defeito da homologação.

**Por que conferência, e não teste na suíte:** nenhuma suíte constrói imagem nem sobe o nginx, e acrescentar isso a uma
suíte é uma change de infraestrutura de teste, maior que esta. O guarda que fica no repositório é o do D3; a
conferência prova uma vez que o nginx real se comporta como o leitor entende.

**Alternativa, se o provedor falso exigir rede no retorno:** o `apps/api` sem provedor responde ao mesmo pedido
`302 /entrar?recusa=falha` sem ler o state (`FederatedLoginEndpoints.cs` l. 133–137; o caso
`TheReturn_WithNoProviderConfigured_IsFailure_ToTheLogin_WithoutSession`). Prova o mesmo para o nginx (o pedido chega
ao `apps/api`) e fica registrada como a variante usada.

### D5. A documentação

- **`docs/deployment.md`, na seção da #189, em "Conferir":** a conferência com navegação, ao lado das conferências por
  `curl`:
  `curl -s -o /dev/null -w '%{http_code} %{redirect_url}\n' -H 'Sec-Fetch-Mode: navigate' 'https://<PUBLIC_DOMAIN>/auth/federated/callback?state=x&code=y'`
  responde `302` para `/entrar?recusa=falha`; `200` é o shell do SPA, o defeito da #285. Com a frase de por que o
  `curl` sem o cabeçalho não prova o caminho do navegador.
- **A nota de deploy da #285:** só a imagem do `frontend`; pode ir sozinha; a conferência é a de cima; voltar atrás é
  a imagem anterior, com o defeito de volta.
- **`CHANGELOG.md` e `02-HISTORICO_E_STATUS.md`**, antes do archive e dentro do PR.

### D6. Tamanho (convenção 18)

Âncora: a #268, a mais parecida (uma linha de `nginx.conf` e o guarda), com o leitor inteiro novo. Esta reaproveita o
leitor.

| componente | produção | testes | casos |
|---|---|---|---|
| `nginx.conf`: o bloco e o comentário, e a lista do topo | ~+18 | — | — |
| `NginxRouting.FindNavigationProblems` | — | ~+30 | — |
| `NginxNavigationEndpointsTests` | — | ~+45 | 1 |
| os casos do leitor | — | ~+40 | 4 |
| a entrada nova do `ExactBlocks` | — | +2 | — |
| **total** | **~+18** | **~+117** | **5** |

Sem duplo novo: o `FederatedLoginFixture` já existe.

## O que o apply mudou (convenção 9)

Nada de comportamento nem de contrato. Registrado:
- **Q1 no texto:** a lista do guarda (D3), o requisito e o cenário do guarda na spec passaram a citar os endpoints do
  emissor em passthrough (o `/connect/authorize` e o `/connect/logout`). Na 2.2, os dois aprovaram, e só o retorno
  reprovou.
- **A baseline (0.3):** a primeira rodada da `frontend` saiu com `exit 127` em 1 s (`vitest: command not found`): o
  worktree da baseline não tinha `node_modules`, e a saída era relativa. Depois do `npm ci` nos dois worktrees, a
  rodada foi refeita com o caminho absoluto. As três suítes .NET não mudaram, e os `.trx` foram movidos do
  `TestResults/` do projeto para a saída.
- **A conferência (4.2):** o `CREATE DATABASE` do banco descartável reprovou, porque o `template1` do Postgres de
  desenvolvimento tem a versão de colação divergente. O banco passou a ser criado do `template0`, e as imagens foram
  refeitas, porque a limpeza da primeira tentativa já as tinha apagado. Com o provedor falso, o retorno não pediu
  rede (0 pedidos do `HttpClient` no log), e a variante sem provedor não foi usada.
- **Convenção 18 (6.2):** produção +21 / −1 no `nginx.conf` (7 linhas de configuração e 14 de comentário), contra
  ~+18; testes +149 (o leitor +31, os casos do leitor +72, o teste novo +43, o `ExactBlocks` +3), contra ~+117
  (+27 %), quase tudo nos textos forjados dos casos do leitor; 5 casos, como projetado.

## Risks / Trade-offs

- **[Um caminho de navegação de topo que não venha do cliente OIDC]** (uma rota nova do `apps/api` aberta por link,
  por exemplo) → o guarda não o vê: a lista sai do cliente OIDC e do `CallbackPath`. Hoje não há outro (D2). O
  authorize e o logout entram pela lista do emissor (Q1).
- **[O host com provedor custa um contêiner a mais na suíte do `apps/api`]** → a classe nova tem o próprio
  `IClassFixture<FederatedLoginFixture>`, como as outras classes do login federado. **Alternativa:** o caso numa classe
  que já usa o fixture (`FederatedProviderRoutesTests`, por exemplo), sem custo, à custa de misturar o nginx com as
  rotas do login. Recomendo a classe própria.
- **[O `code` exposto ao SPA enquanto a imagem nova não sobe]** → contido pelo PKCE, pelo uso único e pelo
  `no-referrer` (Context). Nada a fazer além do deploy.

## Migration Plan

Só a imagem do `frontend`, a qualquer momento: o `apps/api` já serve o retorno. Depois do deploy, a conferência com
navegação do D5 e o roteiro da homologação da M3 a partir de onde parou. Voltar atrás: a imagem anterior, com o
defeito de volta.

## Open Questions

Nenhuma. As duas do proposal foram decididas pelo dono na aprovação (14/11/2026), como recomendado:
- **Q1, sim:** a lista do guarda soma os endpoints de `IdentityIssuerEndpoints.Expected` com `Passthrough: true` (o
  `/connect/authorize` e o `/connect/logout`), e a regra fica uma só para o emissor e para o cliente. Os dois já têm
  bloco exato e não podem reprovar na 2.2.
- **Q2, o provedor de credenciais falsas** na conferência (D4), com a variante sem provedor só se o retorno com o
  state inválido pedir rede.
- **A classe de teste própria** (Riscos), com o próprio `IClassFixture<FederatedLoginFixture>`.
