**Issue:** #180
**Entrega:** M1 · 4 de 12 (acompanhamento #194). **Depende de:** #179, #208, #209 (todas em `main`). **Relacionadas:** #217, #218, #219, #220 (saídas da divisão), #181, #182, #183, #184, #186, #189, #221.

> **Divisão aceita pelo dono em 28/10/2026.** A #180 original foi dividida ("Tamanho e divisão", no `design.md`). Esta change é a #180 recortada. Saíram: o painel entrar pelo emissor (#217), o TOTP (#218), Minha conta e Usuários (#219) e a obrigatoriedade das 2 etapas (#220).

## Why

O acesso ao painel é de um operador único, com o usuário e o hash PBKDF2 em variável de ambiente. Não há tabela de usuários, nem como tirar o acesso de uma pessoa só, nem segundo fator. A sessão é um token HMAC de 30 minutos, sem renovação nem revogação. O emissor OIDC existe desde a #179, e o `apps/inbox` e o `apps/connectors` aceitam o token dele desde a #208. Falta o emissor autenticar uma **pessoa**. É o que esta change faz no `apps/api`.

## What Changes

O que fica na #180 recortada:

- **Usuário no `apps/api`:** tabela `users`, com ID imutável e o e-mail como identificador de login, único depois de normalizado. Guarda nome, estado (ativo ou desativado) e último acesso.
- **Hash de senha Argon2id** (OWASP, m = 19 MiB, t = 2, p = 1), com `NSec.Cryptography` 26.4.0 sobre libsodium 1.0.22. O PBKDF2 de hoje só é aceito uma vez, como hash inicial do usuário criado no bootstrap, e é trocado por Argon2id no primeiro login.
- **Bootstrap do primeiro Admin:** sem nenhum usuário, o `apps/api` cria o primeiro com `Auth:BootstrapAdminEmail` (nova) e o `Auth:OperatorPasswordHash` existente. Com usuários, a variável é ignorada e o log avisa.
- **Login com e-mail e senha no próprio `apps/api`**, numa rota JSON (`POST /auth/session`):
  - abre a sessão do emissor, um cookie `HttpOnly` de vida curta;
  - confere o `Origin` contra CSRF;
  - bloqueia progressivamente por e-mail, exista ou não o usuário;
  - limita globalmente as verificações de senha.
- **Fluxo OIDC do painel:**
  - authorization code com PKCE obrigatório;
  - `/connect/authorize` em modo passthrough, que manda para a rota de login do SPA quando não há sessão;
  - refresh token rotativo, de 14 dias;
  - access token de 15 minutos;
  - encerramento de sessão em `/connect/logout`, que revoga a autorização da sessão.
- **Cliente público `buteco-painel` e três escopos** (`buteco-api`, `buteco-inbox`, `buteco-connectors`), sincronizados pelo `apps/api` em segundo plano depois do start, a partir da url pública.
- **O `apps/api` aceita o próprio token** com `aud` `buteco-api`, pelo `JwtBearer`, com as chaves do anel em memória. Não há chamada HTTP. O HMAC continua aceito, e o esquema é escolhido pela forma do token, como na #208.
- **`PublicUrl:BaseUrl` passa a ser obrigatória:** sem ela, o boot falha. **BREAKING** para quem roda o `apps/api` sem url pública. Em produção ela já é exigida pelo compose.
- **O access token de todo fluxo e o id token deixam de gravar linha**, numa extensão do D1 da #209. O código de autorização e o refresh token continuam gravados.
- **"Último acesso"** em campo próprio, atualizado no login e na renovação.
- **nginx:** blocos exatos para `/connect/authorize` e `/connect/logout`, fora do desvio de navegação.

**O que não muda nesta change:**
- `POST /auth/login`, o token HMAC do operador e o painel, que continuam como hoje até a issue nova #217;
- o `apps/inbox` e o `apps/connectors`, cujo código não muda.

## Capabilities

### New Capabilities

- `local-users`: o usuário local, que cobre identificação por e-mail, hash de senha, bootstrap do primeiro Admin, estado, último acesso, bloqueio progressivo e limite de tentativas.
- `issuer-user-login`: a sessão do emissor e o fluxo authorization code + PKCE do painel. Cobre a rota de login, o cookie, o CSRF, o authorize em passthrough, a renovação, o encerramento de sessão e o cliente `buteco-painel`.

### Modified Capabilities

- `oidc-issuer`:
  - habilita authorization code, refresh e encerramento de sessão;
  - o boot falha sem url pública;
  - o cliente do painel e os três escopos passam a ser sincronizados pelo próprio `apps/api`;
  - o token do emissor passa a ser credencial das rotas do `apps/api`.
- `issuer-token-storage`: o access token de qualquer fluxo e o id token deixam de gerar linha. Antes, só o access token de `client_credentials`.
- `route-authentication`:
  - os endpoints em passthrough do emissor também são rotas mapeadas, e a checagem de boot passa a conferir isso;
  - o `apps/api` aceita o token do emissor pela forma.
- `issuer-token-validation`: o teste de acordo passa a usar o token de usuário do fluxo real, e não um cliente confidencial de teste.
- `identity-signing-keys`: a retenção da chave aposentada passa de 24 horas a 15 dias, para cobrir o refresh de 14 dias, que o OpenIddict valida com as mesmas chaves. A checagem de boot passa a usar a maior duração, access ou refresh.
- `agent-catalog`: sai o cenário "Sem URL pública configurada, os endereços ficam ausentes", que deixa de ser alcançável.

## Impact

- **`apps/api`:**
  - pasta nova `Users/` e subpastas novas em `Identity/`;
  - duas migrations: `users` e `identity_login_throttle`, e a de dados que estende a retenção das chaves já gravadas;
  - pacote novo `NSec.Cryptography`;
  - `Program.cs`, a configuração do emissor e as checagens de boot mudam;
  - testes de unidade e de integração.
- **`apps/frontend`:** só o `deploy/nginx.conf`. O código do painel não muda aqui, e isso fica com a #217.
- **`tests/`:** os dois projetos de acordo trocam o cliente confidencial de teste pelo fluxo real de usuário.
- **`apps/inbox`, `apps/connectors` e `apps/workers`:** sem mudança de código.
- **Deploy:**
  - `AUTH_BOOTSTRAP_ADMIN_EMAIL` nova no `.env.prod`;
  - `AUTH_OPERATOR_PASSWORD_HASH` passa a servir também ao bootstrap;
  - rebuild do `api`, do `migrator` e do `frontend`, este último por causa do nginx.
- **Docs:** `01` (Autenticação), `docs/architecture.md`, `docs/configuration.md`, `docs/deployment.md`, `docs/development.md` (bootstrap), `02` e `CHANGELOG`.
