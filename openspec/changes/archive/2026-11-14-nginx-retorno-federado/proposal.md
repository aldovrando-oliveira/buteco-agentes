**Issue:** #285 (defeito de entrega da M3, achado na homologação; envolvida: #189, o retorno do cliente OIDC, que não é
reaberta, convenção 23). Acompanhamento: #196.

## Why

Na homologação da M3, depois da autorização no Google, o navegador volta a
`GET /auth/federated/callback?state=…&iss=…&code=…` e mostra "Unexpected Application Error! 404 Not Found", do React
Router. O bloco de regex que leva `/auth` ao `apps/api` em `apps/frontend/deploy/nginx.conf` tem o desvio de navegação
(`Sec-Fetch-Mode: navigate` → `/index.html`), e o retorno do Google é uma navegação de topo: o nginx serve o shell do
SPA em vez de encaminhar ao `apps/api`. O `/connect/authorize` e o `/connect/logout` escapam porque têm `location =`
próprio, sem o desvio; o retorno do cliente OIDC não tem.

O login, o aceite de convite e o conectar pelo Google passam pelo mesmo retorno, e nenhum dos três volta. O `code`
fica na URL do SPA, mas vale uma vez, depende do PKCE e do cookie do fluxo, e não vaza pelo `Referer`
(`no-referrer`).

Por que passou: as conferências 13.1 da #189 e 10.1 da #274 usaram `curl`, sem `Sec-Fetch-Mode`; o
`NginxRouteCoverageTests` da #268 confere que o prefixo `auth` chega ao `apps/api`, e não que um caminho aberto por
navegação de topo escapa do desvio; e no desenvolvimento o proxy do Vite leva `/auth` inteiro ao `apps/api`, sem
desvio nenhum.

## What Changes

**`apps/frontend/deploy/nginx.conf`:** um `location = /auth/federated/callback`, no molde do `/connect/authorize`: o
`proxy_pass` pelo `$api_upstream` e os mesmos `proxy_set_header`, **sem** o desvio de navegação, com o comentário do
porquê. O resto de `/auth` continua no bloco de regex, com o desvio.

**O guarda que faltou** (`apps/api`): um teste que lê o `nginx.conf` e reprova quando um caminho que o navegador abre
por navegação de topo no `apps/api`, tirado do código (o `FederatedLogin.CallbackPath` e os
`RedirectionEndpointUris` e `PostLogoutRedirectionEndpointUris` do cliente OIDC composto, num host com um provedor
configurado), não tiver um `location =` próprio, encaminhando ao `apps/api`, sem o desvio. Ele reprova contra o
`nginx.conf` de hoje. A verificação mora no leitor de `tests/Shared/NginxRouting.cs`, e o
`NginxRouteCoverageTests` do `apps/api` ganha o bloco novo na lista das exceções nomeadas, com o motivo.

**O inventário:** nenhuma outra rota do `apps/api` (nem do `apps/inbox` ou do `apps/connectors`) é aberta por
navegação de topo sob um prefixo com o desvio. A lista do que fica fora, com o motivo, está no design (D2); os
destinos do SPA para onde o `apps/api` redireciona (`/entrar`, `/account?conectado=…`, `/convite?recusa=…`)
continuam no SPA.

**A conferência com o nginx de verdade:** a imagem do frontend, com o `apps/api` local, e
`curl -H 'Sec-Fetch-Mode: navigate'` em `/auth/federated/callback?state=x&code=y` chegando ao `apps/api`
(`302 /entrar?recusa=falha`, pelo state forjado), contra a imagem de antes respondendo `200` com o HTML do SPA.

**Spec `server-deployment`:** o requisito "Nginx do stack roteia por prefixo" ganha a regra dos caminhos de navegação
de topo do `apps/api` (bloco exato, sem o desvio, conferido por teste a partir do código), o retorno do cliente OIDC
nas exceções nomeadas, e os cenários.

**Documentação:** `docs/deployment.md`, na seção da #189, a conferência com navegação (`curl` com
`Sec-Fetch-Mode: navigate`) ao lado das conferências por `curl`, e a nota de deploy da #285; `CHANGELOG.md` e
`02-HISTORICO_E_STATUS.md`.

**Fora:** nenhum código de produção dos apps muda; as rotas, as respostas e as telas ficam como estão. A #281 (a falha
do conectar que volta ao `/entrar`) continua à parte.

## Capabilities

### New Capabilities

Nenhuma.

### Modified Capabilities

- `server-deployment`: o requisito "Nginx do stack roteia por prefixo, sem expor porta pública diretamente", com os
  caminhos de navegação de topo do `apps/api` e os cenários do retorno do cliente OIDC.

## Impact

- **Arquivos:** `apps/frontend/deploy/nginx.conf`; `tests/Shared/NginxRouting.cs`; no `apps/api`,
  `NginxRouteCoverageTests.cs`, `NginxRoutingReaderTests.cs` e um teste novo, `NginxNavigationEndpointsTests.cs`.
- **Deploy:** só a imagem do `frontend`. Pode ir sozinha: o `apps/api` já serve o retorno.
- **Suítes:** `api` (os testes novos), `inbox` e `connectors` (os testes de cobertura leem o mesmo `nginx.conf`) e
  `frontend` (o `nginxHeaders.test.ts` também).
