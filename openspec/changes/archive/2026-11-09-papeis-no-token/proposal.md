**Issue:** #184 (M2 · 1 de 5; depende da #180, da #208 e da #217, todas em `main`). Acompanhamento: #195.

## Why

Na M1 todo usuário real tem o poder de um Admin (#180 D4): gere usuários, convites, identidade, agentes e conectores. Não dá para deixar alguém só olhar, nem tirar de quem opera os agentes o poder de gerir acesso. A #184 traz três papéis, **Admin**, **Operador** e **Leitor**, gravados no usuário, levados no token do emissor e checados por rota nos três apps que recebem o token do painel (`apps/api`, `apps/inbox`, `apps/connectors`), com checagem de boot no molde da convenção 8.

## What Changes

**`apps/api`**
- **Coluna `users.role`** (`admin`, `operator`, `reader`), com migration que dá `admin` a todo usuário existente, o do bootstrap inclusive. O bootstrap cria o primeiro Admin com `admin`.
- **O claim `role` no access token e no id token**, posto no authorize e relido do banco em toda renovação (`UserGrantTokenHandler`), como o nome e o e-mail.
- **Tabela de papéis por rota** (`RoleRoutes`), ao lado da tabela de subjects de serviço que já existe. O subject `operator` deixa de "passar em tudo": o token do painel passa só nas rotas cujo papel ele traz. Rota autenticada que não está em nenhuma das duas tabelas derruba o boot, e entrada de tabela sem rota também.
- **Rotas de Admin relêem o papel do banco** (`/users/*`, convite, redefinição de senha e de 2 etapas, `/identity`): elas já carregam o usuário em toda requisição (`CurrentUser`), e o rebaixamento vale ali na requisição seguinte.
- **Nova rota `PUT /users/{id}/role`**, só do Admin, sob o `pg_advisory_xact_lock` `BUTECOD1`, com a contagem de Admins ativos dentro do lock. A desativação passa a reler, dentro do mesmo lock, que quem chama ainda é Admin ativo.
- **O convite leva o papel**, obrigatório (`POST /users/invitations`), e a lista, o detalhe e `GET /account` devolvem o `papel` (`admin`, `operador`, `leitor`).
- **Promover a Admin pede a senha de quem chama**; rebaixar a si mesmo é permitido enquanto houver outro Admin ativo.
- **As rotas só de serviço** (`POST /agents/{id}/a2a`, `/sync/knowledge-bases/*`) deixam de aceitar o token do painel.
- **BREAKING:** token do painel sem o claim `role` recebe `401` nos três apps (o painel renova e repete); token do painel com papel fora da rota recebe `403`.

**`apps/inbox` e `apps/connectors`**
- O principal leva o `role` do token. A política única por subject (`operator`) vira a mesma tabela de papéis por rota, com a mesma checagem de boot nos dois sentidos. No `apps/connectors`, as três rotas do `operator` passam a Admin e Operador; o Leitor recebe `403`. A rota de serviço (`service:api`) não muda.

**`tests/`**: os dois acordos que passam pelo login (`IssuerUserFlow`) passam a afirmar o papel do Admin do bootstrap; entra o critério de aceite (a): token do emissor com papel Leitor recebe `403` numa rota de pastas do `apps/connectors`.

**Documentação:** `01-ARQUITETURA_E_CONVENCOES.md` ("Autenticação"), `docs/deployment.md` (notas de deploy e o rebaixamento por API até a #185), `CHANGELOG.md`, `02-HISTORICO_E_STATUS.md`.

**Fora:** qualquer tela (#185); o card "Aplicações com acesso" (#187); a verificação em 2 etapas por papel (continua obrigatória para todos, decisão do dono, Q2); `PUT /account` (Q7, sem consumidor); o comando `password-reset-link` (#257, change própria logo depois desta).

**Deploy:** a #184 entra no `main` e vai para produção junto com a #185, nunca sozinha (decisão do dono, Q9).

## Capabilities

### New Capabilities

- `route-roles`: a matriz de papéis por rota nos três apps, a checagem de boot e as respostas `401`/`403`.

### Modified Capabilities

- `local-users`: o papel do usuário, a migration e o bootstrap.
- `issuer-user-login`: o claim `role` no authorize e na renovação.
- `user-management`: rotas só do Admin, `PUT /users/{id}/role`, o último Admin.
- `user-access-links`: convite com papel; convite, novo link, cancelamento e redefinição só do Admin.
- `user-account`: `GET /account` devolve o papel; `/account/*` para todo papel.
- `identity-issuer-overview`: `GET /identity` só do Admin.
- `two-factor-totp`: a redefinição de outro usuário só do Admin; a verificação continua obrigatória para todo papel.
- `route-authentication`, `connectors-api`, `issuer-token-validation`: o subject `operator` deixa de bastar; o principal leva o `role`; o `401` sem corpo vale para o token sem papel.

## Impact

- **Contrato:** o subject `operator` deixa de dar acesso a toda rota; `POST /agents/{id}/a2a` e `/sync/knowledge-bases/*` ficam só dos serviços (design, Q8f). Token do painel sem papel recebe `401` sem corpo `problem+json`. `papel` obrigatório no convite.
- **Prazos:** o rebaixamento vale na requisição seguinte nas rotas de Admin, e em até 15 min 30 s nas outras e no `apps/inbox` e `apps/connectors`; a promoção chega na renovação seguinte.
- **Banco:** uma coluna em `users`, com `DEFAULT 'reader'` só no SQL (design, D4 e j).
- **Sessões em produção:** os access tokens emitidos antes do deploy não têm papel: o primeiro pedido de cada um recebe `401`, o painel renova e o token novo já sai com o papel. Ninguém precisa entrar de novo.
- **Suítes:** `apps/api`, `apps/inbox`, `apps/connectors`, `tests/InboxOrchestratorRoundTrip.Tests` e `tests/ApiConnectorsRoundTrip.Tests`.
