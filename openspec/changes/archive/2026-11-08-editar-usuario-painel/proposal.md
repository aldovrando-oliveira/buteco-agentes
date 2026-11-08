**Issue:** #183

## Why

O `apps/api` já serve o detalhe e as ações de um usuário (#219, #181, #218): editar nome e e-mail, redefinir senha, redefinir a verificação em 2 etapas, encerrar sessões, desativar e reativar. O painel não tem tela para nada disso. Hoje, desativar alguém ou gerar um link de redefinição de senha exige `curl` com um token, e a lista de Usuários (#250) não tem para onde levar.

É a segunda parte do passo C, dividido pelo dono em 07/11/2026 (Q1, opção A). A #250 entregou a lista, os convites, a Identidade e a seção "Acesso". Esta change fecha a tela de Usuários.

## What Changes

- **Editar usuário (`/users/:id`)**, pelo quadro `UsuarioDetalhe` (`entrega=m1`), com todos os valores de `alvo`, `verificacao` e `acao`:
  - **a carga:** carregando, erro com "Tentar de novo" e usuário inexistente (`404`). O convidado volta para `/users` (Q4);
  - **o cabeçalho:** volta para Usuários, o nome, o selo do estado, o selo "Você", e o subtítulo com a origem e o último acesso ou a data da desativação;
  - **Dados:** nome e e-mail editáveis (`PUT /users/{id}`), com "Salvar alterações", "Descartar", o `409` de e-mail em uso e o `400` nos campos;
  - **Formas de entrada:** só a linha Senha, com **"Redefinir senha"** (`POST …/password-reset`). Ela pede a senha de quem chama, e o link aparece uma vez, com "Copiar link" e "Vale até {expiraEm}" (Q5);
  - **Verificação em 2 etapas:** o estado e **"Redefinir verificação"** (`POST …/two-factor/reset`), com confirmação no cartão;
  - **Sessões:** a contagem e o último uso, com **"Encerrar todas as sessões"** (`POST …/sessions/revoke`) e confirmação no cartão;
  - **Desativar**, com confirmação acima dos cartões, e **Reativar**, sem confirmação, como no quadro (`POST …/deactivate`, `…/activate`).
- **O próprio usuário**, reconhecido pelo `sub` do id token contra o `:id` da rota. Ele vê a nota "Este é você…" e os links para Minha conta no lugar de Desativar, Redefinir senha, Redefinir verificação e Encerrar sessões. Nome e e-mail continuam editáveis. O servidor continua sendo a guarda real: os `403` dele aparecem no aviso.
- **A senha de quem chama na redefinição:** vazia não é enviada, o `401` fica no campo sem deslogar, e o `423` vai no aviso com o botão bloqueado. É o mesmo caminho da #250.
- **O link de redefinição só fica na memória da tela:** nada em storage, URL, `state` do router ou cache do TanStack Query.
- **O "Editar" na linha da lista de Usuários**, para o ativo e o desativado. Resolve a divergência temporária da #250, e o guarda "a lista não tem link para o detalhe" é trocado pelo caso do "Editar".
- **Em Minha conta, "Usuários" vira link** para o detalhe do próprio usuário (`/users/{id}`), como no quadro `MinhaConta`.
- **Nenhuma mudança no `apps/api`** nem em contrato. Todas as rotas existem, e o quadro não mostra nenhum campo que a rota não traga (a lista do que é inferido está no design).

## Capabilities

### New Capabilities

- `panel-user-detail-page`: a página Editar usuário (`/users/:id`), com a carga, o cabeçalho, o reconhecimento do próprio usuário, os dados, a redefinição de senha com a senha de quem chama e o link mostrado uma vez, a redefinição da verificação, o encerramento das sessões, e desativar e reativar.

### Modified Capabilities

- `panel-users-page`: o requisito "Ações da linha sem destino inexistente" passa a exigir o "Editar" na linha do ativo e na do desativado, levando a `/users/{id}`. A descrição da capacidade deixa de dizer que Editar usuário chega com a #183.
- `panel-account-page`: no requisito "Cartão Dados", "Usuários" vira link para o detalhe do próprio usuário. O cenário "use Usuários sem link" é substituído.

## Impact

**`apps/frontend`**, o único app tocado:
- `features/users/`: a página `UserDetailPage`, os cartões do detalhe, as rotas do detalhe no `usersApi.ts` e o "Editar" no `UsersTable`;
- `app/routes.tsx`: a rota `users/:id`;
- `auth/`: `currentUserId(session)`, que lê o `sub` do id token. O decodificador do payload, que já existe em `sessionStore.ts`, passa a ser exportado. O formato do registro da sessão não muda;
- `features/account/components/AccountDataCard.tsx`: o link.

**Fora do `apps/frontend`:**
- `apps/api`, `apps/inbox`, `apps/connectors` e `apps/workers` não mudam;
- o nginx não muda: `/users/<id>` já cai no `apps/api` com o desvio de navegação, como `/users`;
- não há dependência nova nem migration;
- nas docs, o `CHANGELOG.md` e o `02` são atualizados antes do archive.
