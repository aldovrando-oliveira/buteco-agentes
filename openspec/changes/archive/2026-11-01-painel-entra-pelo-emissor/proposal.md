**Issue:** #217
**Entrega:** M1 · 5 de 12 (acompanhamento #194). **Depende de:** #180 (em `main` desde `ca2bd71`, PR #224). **Relacionadas:** #182 e #184 (que esperam esta), #183 (telas definitivas), #218 e #220 (2 etapas, fora daqui), #219 (rotas de Minha conta), #186, #221, #222.

## Why

Desde a #180, o `apps/api` autentica uma pessoa por e-mail e senha e emite o token dela pelo fluxo authorization code + PKCE, mas ninguém usa esse fluxo: o painel continua no `POST /auth/login`, com o operador da variável de ambiente e o token HMAC de 30 minutos. Enquanto o painel não troca de login, a #182 não pode remover o HMAC e a #184 não pode recusar o HMAC `operator`. Esta change troca o login do painel e tira do `apps/api` o caminho que fica sem consumidor.

## What Changes

**`apps/frontend` (cliente OIDC do painel):**

- **Início do fluxo:** o painel gera `code_verifier` (PKCE S256) e `state`, guarda os dois para a aba, e navega ao `/connect/authorize` com `client_id=buteco-painel` e os escopos `openid offline_access buteco-api buteco-inbox buteco-connectors`.
- **`/entrar`:** o formulário de hoje, com "E-mail" no lugar de "Usuário", chamando `POST /auth/session`. Mostra os estados do protótipo que valem sem provedor federado (`inicio`, `bloqueado`, `sessao-expirada`) e as respostas `401`, `403`, `423` com `Retry-After`, `429` e `503`. Depois do `204`, segue o `retorno` só quando ele começa por `/connect/authorize?`; o passo das 2 etapas não entra (#218, #220).
- **`/entrar/retorno`:** tira `code` e `state` da URL por `replace` antes de qualquer pedido, confere o `state`, troca o código no `/connect/token` com o `code_verifier`, e trata o retorno com `error`. O `iss` não é conferido enquanto houver um emissor só (registrado na #189).
- **Tokens no navegador e várias abas:** o access, o refresh e o id token num registro só no `localStorage`, e todo escritor dele (troca do código, renovação, logout, fim de sessão) sob um Web Lock, porque o refresh é rotativo e o reuso fora da janela de 30 s revoga a sessão inteira (design, D2 e D3).
- **Renovação** pelo refresh token antes de o access token (15 min) vencer, e uma vez diante de `401`.
- **`503` com `Retry-After` é indisponibilidade passageira** nos três apps: não desloga. Só `401` depois da renovação encerra a sessão.
- **Encerrar sessão** pelo `/connect/logout` com o id token mais recente como `id_token_hint`, apagando os tokens locais sob o mesmo lock da renovação e avisando as outras abas. Entra um "Sair" mínimo na casca, que hoje não tem nenhum, com o nome do id token.
- **As 7 cópias do `request<T>`** passam a anexar o access token do emissor pelo módulo de sessão.
- **Proxy do Vite** para `/auth`, `/connect` e `/.well-known` (design da #180, D3.4).
- `/login` continua existindo só como redirecionamento para `/entrar`, para links salvos.

**`apps/api` (remoção do que fica sem consumidor):**

- **BREAKING:** saem o `POST /auth/login`, `Auth:OperatorUsername`, `Auth:OperatorTokenLifetime`, o `LoginRequest`/`LoginResponse` e a entrada `/auth/login` da allowlist de rotas anônimas.
- `Auth:OperatorPasswordHash` passa a servir só ao bootstrap do primeiro Admin, e deixa de ser obrigatória no compose (D9, decisão do dono de 01/11/2026). Nome e formato da variável ficam para a #225.
- **O HMAC continua validado** nos três apps, para os tokens de serviço e para os tokens de operador ainda em voo (até 30 min depois do deploy). Recusar o HMAC `operator` é da #184, e remover o HMAC é da #182.

**Deploy:** `docker-compose.prod.yml` e `.env.prod.example` sem `AUTH_OPERATOR_USERNAME`, e o hash do bootstrap sem `:?`; o `nginx.conf` ganha a CSP e o `Referrer-Policy: no-referrer` em todo caminho que entrega o shell (as rotas `/entrar` e `/entrar/retorno` já caem no SPA, e a conferência por curl entra no fechamento).

## Capabilities

### New Capabilities

- `panel-issuer-session`: a sessão do painel pelo emissor. Início do fluxo com PKCE e `state`, validação do retorno e troca do código, guarda dos tokens, renovação coordenada entre abas, `503` passageiro, `401` como fim de sessão, encerramento pelo `/connect/logout` e aviso às outras abas.

### Modified Capabilities

- `operator-login-ui`: a tela de login vira `/entrar`, com e-mail e senha pelo `POST /auth/session` e os estados do protótipo sem provedor; "rotas exigem sessão ativa" e "`401` encerra a sessão" passam a valer sobre a sessão do emissor, depois da renovação.
- `operator-authentication`: sai o requisito "Login do operador único"; o do token HMAC de operador passa a dizer que ele só é validado (nenhum é emitido) até a #182 ou a #184.
- `route-authentication`: a allowlist de rotas anônimas perde o `/auth/login`, e os cenários que obtinham o HMAC pelo login passam a obtê-lo do `ITokenService`.
- `oidc-issuer`: o requisito da convivência com o login HMAC perde o `POST /auth/login` e fica só com a validação HMAC.
- `issuer-user-login`: o requisito do refresh rotativo passa a fixar a tolerância de 30 s para renovações concorrentes e a revogação da cadeia depois dela, de que o painel depende (D3).
- `frontend-app-shell`: o rodapé passa a mostrar o nome do id token e o "Sair", no lugar da proibição de mostrar identidade.
- `knowledge-base-sync-source-ui`: o `401` do `apps/connectors` continua sem encerrar a sessão, agora depois de uma renovação e de uma nova tentativa (D5).
- `server-deployment`: o hash do bootstrap deixa de ser obrigatório no compose, e o `nginx.conf` ganha a CSP e o `Referrer-Policy`.

`local-users` não muda: o requisito do bootstrap já diz que `Auth:OperatorPasswordHash` só é lida quando não há nenhum usuário. O que muda é o doc e o compose (D9).

## Impact

- **`apps/frontend`:** `src/auth/` (o módulo de sessão substitui o `token.ts`), `features/auth` (`/entrar`, `/entrar/retorno`), `app/ProtectedRoute.tsx`, `app/routes.tsx`, `app/queryClient.ts`, a casca (`Sair`, `components/layout/AppShell.tsx`), as 7 cópias do `request<T>` e os 19 arquivos que importam o `token.ts`, `vite.config.ts`, `.env.example`, `deploy/nginx.conf`. Sem dependência nova.
- **`apps/api`:** `Auth/Endpoints/AuthEndpoints.cs`, `Auth/Requests`, `Auth/Responses`, `Auth/OperatorCredentialOptions.cs`, `Auth/TokenSigningOptions.cs`, `Program.cs`, `appsettings*.json`, e os testes que usam o `POST /auth/login` (`AuthLoginTests`, `IdentityIssuerCoexistenceTests`, `RouteAuthenticationTests`, `RouteAuthenticationStartupFailureTests`, `KnowledgeDocumentCatalogTests`).
- **`tests/InboxOrchestratorRoundTrip.Tests`:** a fixture deixa de logar pelo `POST /auth/login`.
- **`apps/inbox` e `apps/connectors`:** código sem mudança.
- **Deploy:** `docker-compose.prod.yml`, `.env.prod.example`, `.env.example`. Ordem: `api` e `frontend` juntos; o painel aberto numa versão antiga recarrega para a nova no próximo `401`.
- **Docs:** `01` (Autenticação, convenção 7), `docs/deployment.md` ("Primeiro Admin", revisto pela D9), `docs/configuration.md`, `docs/development.md`, `SECURITY.md`, `CHANGELOG`, `02`.
