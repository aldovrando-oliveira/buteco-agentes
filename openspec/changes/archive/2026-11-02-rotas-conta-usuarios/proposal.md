**Issue:** #219

## Why

Depois da #180 e da #218, o usuário existe, entra e liga as 2 etapas, mas nenhuma rota o gere: não dá para trocar a senha, ver e encerrar sessões, editar, desativar ou reativar um usuário. As telas Minha conta, Usuários e Editar usuário (#183) não têm de onde ler nem onde escrever, e a #183 depende desta issue.

## What Changes

- **Minha conta**, no grupo `/account` (que já existe, #218), para o usuário real do token (`CurrentUser`):
  - ler os próprios dados;
  - trocar a senha, pedindo a atual, com a política de 12 a 128 caracteres;
  - listar as sessões vivas, com a sessão atual marcada;
  - encerrar uma sessão;
  - encerrar todas as outras.
- **Usuários**, no grupo `/users` (que já existe, #218), com o acesso do operador da M1 (todo usuário real; o HMAC de operador recebe `403`):
  - listar, com estado, formas de entrada, 2 etapas e último acesso;
  - detalhe, com a contagem das sessões vivas e a última atividade;
  - editar nome e e-mail, com `409` na colisão do e-mail normalizado e sem mudar o `sub`;
  - desativar e reativar;
  - encerrar todas as sessões de outro usuário.
- **Sessão = autorização ad hoc `valid` com refresh vivo.** Encerrar e desativar revogam pelas autorizações (#180 D3.10). O último uso sai das linhas de token que já existem, sem coluna nova.
- **A sessão atual vem do `oi_au_id` do access token.** O principal do `apps/api` hoje descarta esse claim, e passa a guardá-lo.
- **Toda troca de `security_stamp` passa a revogar as autorizações da pessoa** (trocar a senha, desativar e, por correção, a redefinição das 2 etapas da #218), para a lista nunca mostrar uma sessão que já morreu pelo stamp.
- **Desativar é atômico** com a regra de não desativar a si mesmo, sob um lock de transação.
- A redefinição das 2 etapas pelo admin continua sendo da #218 (`POST /users/{id}/two-factor/reset`), e não é reimplementada.

## Capabilities

### New Capabilities

- `user-account`: Minha conta: os próprios dados, a troca de senha, a lista de sessões vivas, encerrar uma e encerrar as outras.
- `user-management`: Usuários: lista, detalhe, edição de nome e e-mail, desativação e reativação, e o encerramento das sessões de outro usuário.

### Modified Capabilities

- `two-factor-totp`: a redefinição pelo admin passa a revogar também as autorizações da pessoa, além de trocar o stamp.
- `issuer-user-login`: a renovação passa a levar ao id token e ao refresh novo o nome e o e-mail vigentes (P6 do design).

## Impact

- **`apps/api`:** rotas novas em `Users/` (Minha conta e Usuários), a consulta e a revogação de sessões sobre as tabelas do OpenIddict, o `oi_au_id` no principal (`IssuerTokenAuthentication`), a política de senha, a revogação junto da troca do stamp e da redefinição das 2 etapas. Sem migration, sem pacote novo, sem variável de configuração.
- **Contrato HTTP novo** para a #183. Nenhuma rota existente muda de forma.
- **`apps/frontend`, `apps/inbox`, `apps/connectors`, `apps/workers`:** sem mudança de código. O `deploy/nginx.conf` já roteia `account|users` (#218).
- **Documentação:** `01` (Autenticação), `02`, `CHANGELOG.md`, `docs/architecture.md`.
