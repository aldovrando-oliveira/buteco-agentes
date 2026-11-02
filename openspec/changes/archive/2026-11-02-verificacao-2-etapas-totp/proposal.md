**Issue:** #218
**Entrega:** M1 · 6 de 12 (acompanhamento #194). **Depende de:** #180 (em `main` desde `ca2bd71`, PR #224). **Base:** `86abb5d` (merge do PR #228, #217). **Relacionadas:** #219 (rotas de Minha conta e de Usuários, que dependem desta), #183 (telas: o passo do código, o cadastro com QR, os cards), #220 (obrigatoriedade), #184 (papéis, M2), #186 (aplicações externas).

## Why

Desde a #180 o login é só por e-mail e senha, e a decisão da evolução é "verificação em 2 etapas obrigatória para Admin", que na M1 vale para todo usuário (#180, D4). A obrigatoriedade só pode ser ligada (#220) depois que existir onde cadastrar (#183), e a #183 só pode desenhar o que o backend serve. Esta change entrega o backend do TOTP: cadastro, verificação no login, códigos de recuperação, remoção pela própria pessoa e redefinição pelo admin, valendo para quem o ligar, sem deixar ninguém sem entrar pelo painel de hoje.

## What Changes

**`apps/api`:**

- **Segundo passo no login.** Para quem tem a verificação ligada, o `POST /auth/session` com a senha certa deixa de responder `204` e responde `200` com `{ "segundoPasso": "totp" }`. O cookie `buteco-sessao` passa a guardar o passo pendente. Uma rota nova, `POST /auth/session/second-factor`, recebe `{ "codigo" }` (do app autenticador ou de recuperação) e completa a sessão com `204`. O authorize só emite código para a sessão completa. Para quem não tem a verificação, nada muda.
- **TOTP pelo RFC 6238**, implementado sobre o BCL: HMAC-SHA1, 6 dígitos, passo de 30 s, um passo de tolerância para cada lado, e recusa do código já usado pelo último passo aceito, gravado.
- **Segredo cifrado em repouso** com AES-256-GCM e uma chave nova, `Identity:TwoFactorEncryptionKey`, distinta das outras duas e conferida no boot.
- **Cadastro em dois tempos:** gerar o segredo (devolvido uma vez, com o URI `otpauth://`) e confirmar com um código válido. Só a confirmação liga a verificação, e devolve os 10 códigos de recuperação, uma vez.
- **Códigos de recuperação:** 10, de uso único, guardados só em hash; regenerados pela pessoa com um código válido.
- **Remoção pela própria pessoa**, com um código válido.
- **"Redefinir verificação" pelo admin:** apaga o segredo e os códigos e troca o `security_stamp`.
- **Força bruta:** as falhas de código contam no mesmo bloqueio por e-mail da senha (`identity_login_throttle`), e a senha certa não zera a contagem enquanto falta o código.
- **O cadastro fica fechado em produção até a #183** (`Identity:TwoFactor:EnrollmentEnabled`, falso por padrão, verdadeiro em `Development`). A verificação no login nunca é desligada. É o que impede alguém de ligar a verificação e ficar sem entrar pelo painel de hoje, que só conhece o `204`.
- O `apps/api` ganha os prefixos `/account` e `/users`.

**Deploy:** `Identity__TwoFactorEncryptionKey` obrigatória no `docker-compose.prod.yml` e no `.env.prod.example`. O `deploy/nginx.conf` do `apps/frontend` passa a levar `/account` e `/users` ao `apps/api`.

**Fora daqui:** as telas (#183); a obrigatoriedade (#220); a lista, o detalhe e as outras ações de Usuários e de Minha conta (#219), que leem as colunas criadas aqui; o claim `amr` no token, sem consumidor na M1 (design, D10).

## Capabilities

### New Capabilities

- `two-factor-totp`: o TOTP do usuário local. Parâmetros e reuso, segredo cifrado e a chave conferida no boot, cadastro com confirmação, códigos de recuperação, remoção, redefinição pelo admin, o fechamento do cadastro até a #183, e o que cada rota responde.

### Modified Capabilities

- `issuer-user-login`: o `POST /auth/session` responde `200` com o segundo passo pendente para quem tem a verificação ligada; a rota do código completa a sessão; o authorize trata a sessão pendente como ausência de sessão.
- `local-users`: o bloqueio por e-mail conta também as falhas do código, e a senha certa com o código pendente não zera a contagem.
- `route-authentication`: a rota do código entra entre os pontos de entrada anônimos do emissor.
- `server-deployment`: os prefixos `/account` e `/users` no nginx do stack. A chave nova no compose já cai no requisito existente ("chave … de criptografia" obrigatória na interpolação), sem mudança de texto.

## Impact

- **`apps/api`:** `Users/` (entidade, colunas novas, tabela de códigos de recuperação, TOTP, cifra, rotas de cadastro e de redefinição), `Users/Login/SessionEndpoints.cs`, `Identity/Flow/AuthorizeEndpoint.cs` e `IssuerSession.cs`, `Identity/IdentityIssuerOptions.cs` e a checagem de boot, `Program.cs`, `appsettings*.json`, uma migration. `Identity/Keys/IdentityKeyMaterialCipher.cs` e `McpServers/Security/AesGcmMcpCredentialCipher.cs` passam a usar o envelope extraído (design, D5), sem mudar o formato. Sem pacote novo.
- **`apps/frontend`:** só o `deploy/nginx.conf`. Nenhum código do painel muda.
- **`apps/inbox`, `apps/connectors`, `apps/workers`, `tests/`:** código sem mudança. O `tests/CrossAppTaskStoreCompatibility.Tests` prova que o `apps/workers` continua lendo a cifra de MCP (design, D5).
- **Deploy:** a variável nova antes do `api`; `migrator`, `api`, `frontend` (nginx).
- **Docs:** `01` (Autenticação, a seção dos usuários locais), `docs/configuration.md`, `docs/deployment.md` (NTP no host, cadastro fechado), `docs/development.md` (a saída de quem cadastrar em desenvolvimento), `SECURITY.md`, `CHANGELOG`, `02`.
