**Issue:** #250

## Why

O `apps/api` já tem a lista de Usuários e as rotas de convite (#219, #181) e a leitura do emissor e das chaves (#211), mas o painel não tem tela para elas. Hoje, convidar alguém ou gerar um novo link de convite exige `curl` com um token, e não há onde ver o emissor e as chaves de assinatura.

Esta é a primeira parte do passo C da #183, dividido pelo dono em 07/11/2026 (Q1, opção A). A #183 fica com Editar usuário.

## What Changes

- **A seção "Acesso" no menu da casca**, com **Usuários** (`/users`) e **Identidade** (`/identity`). "Aplicações externas" fica fora até a M2 (#184, #185).
- **Usuários (`/users`)**, pelo quadro `Usuarios` (`entrega=m1`):
  - a lista (`GET /users`) com formas de entrada, 2 etapas, estado (com o vencimento do convite) e último acesso;
  - **convidar dentro do card** (`POST /users/invitations`), com a senha de quem chama, e o link mostrado uma vez, com "Copiar link";
  - **gerar novo link** (`…/invitation/renew`), com a senha de quem chama;
  - **cancelar convite** (`…/invitation/cancel`), com a confirmação na linha;
  - os estados de carga e de erro, e-mail em uso, senha errada, bloqueio e convite já aceito.
- **A linha da lista não mostra "Editar".** A rota `/users/:id` só chega com a #183. É uma divergência temporária do quadro `Usuarios`, que a #183 resolve.
- **Identidade (`/identity`)**, pelo quadro `Provedores` (`entrega=m1`):
  - o card do emissor e o card das chaves de assinatura (`GET /identity`), nos dois momentos do quadro;
  - as durações e as datas vindas da rota;
  - o estado de indisponível, com uma nova tentativa automática enquanto houver erro.
- **Os links de convite ficam só na memória da tela:** nada em storage, na URL, no `state` do router nem no cache do TanStack Query.
- **Os avisos de falha e as datas de Minha conta vão para um lugar comum** (`utils/` e `hooks/`), porque Usuários é o segundo consumidor real (convenção 2). O aviso passa a conferir os campos do erro, e não a classe (Q2).
- **Nenhuma mudança no `apps/api`** nem em contrato. O `401` "Senha inválida." das ações com a senha de quem chama já volta à tela pelo `sessionFetch` (#246, Q0).

## Capabilities

### New Capabilities

- `panel-users-page`: a lista de Usuários, o convite, o novo link e o cancelamento, com os estados do quadro, a senha de quem chama e o link mostrado uma vez.
- `panel-identity-page`: a página Identidade, com o emissor, as chaves de assinatura, as durações e as datas da rota, e o estado de indisponível com a nova tentativa automática.

### Modified Capabilities

- `frontend-app-shell`: a navegação ganha a seção "Acesso", com Usuários e Identidade.

## Impact

**`apps/frontend`**, o único app tocado:
- as features novas `features/users/` e `features/identity/`;
- as rotas `users` e `identity` em `app/routes.tsx`;
- a seção "Acesso" em `components/layout/AppShell.tsx`;
- `cardFailure`, `useBlockedUntil` e `accountDates` saem de `features/account/` para `utils/` e `hooks/`, e os importadores de Minha conta são ajustados, sem mudança de comportamento.

**Fora do `apps/frontend`:**
- `apps/api`, `apps/inbox`, `apps/connectors` e `apps/workers` não mudam;
- o nginx não muda: `/users` e `/identity` já caem no `apps/api` com o desvio de navegação;
- não há dependência nova nem migration;
- nas docs, o `CHANGELOG.md` e o `02` são atualizados antes do archive.
