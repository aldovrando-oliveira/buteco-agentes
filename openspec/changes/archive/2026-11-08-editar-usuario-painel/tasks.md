Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-183.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-250.sh`. A mutação toca só o trecho nomeado, e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra:
- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes.

Uma mutação que quebra a compilação e reprova por "no tests" não vale. Ela é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/183-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-183.sh`, com o RabbitMQ de pé (`PAUSAR_RABBIT=0`, o padrão do script, porque o healthcheck TCP da #251 já está no `main`). A suíte principal é a `frontend`.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada da `frontend`, o load fica abaixo de 6.

**A baseline** está em `~/.cache/buteco-agents/183-baseline/`, em `9c49e4b`, com a árvore limpa:
- 2248 de 2248 casos aprovados, em 177 arquivos, em 204 s (`APROVADA`);
- o relatório por nome está em `nomes.txt` (2248 linhas, nenhuma reprovada). Ele é **idêntico** ao da última rodada da #250 (`250-pos-rebase-r1/`), que é o mesmo código;
- a rodada começou às 23:26 de 07/11/2026, com load 2,10 (1 min) e 5,76 (5 min) e CPU em 100. A CPU caiu até 48, e pela regra assimétrica a rodada conta;
- o RabbitMQ ficou de pé (`PAUSAR_RABBIT=0`, `healthy`, o healthcheck TCP da #251), com `postgres` e `hopeful_williams` também no `podman ps`.

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

Nenhum caso existente é removido, renomeado ou afrouxado, **exceto** dois, cujo requisito muda nesta change:
- na 6.2, "a lista não tem link para o detalhe (/users/:id chega com a #183)" (`panel-users-page`, "Ações da linha sem destino inexistente"). Os dois casos que o substituem cobrem o mesmo defeito (link para convidado) e o novo (ativo sem "Editar");
- na 6.3, `"use Usuários" fica sem link até a tela existir (#183)` (`panel-account-page`, "Cartão Dados"). O caso que o substitui afirma o link e o destino.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-183.sh`, cópia do `guarda-250.sh`, com o projeto `frontend`. Conferir com:
  - uma mutação que não altera nada: o script recusa com "defeito não aplicado";
  - uma mutação real num arquivo de `apps/frontend/src`: o arquivo volta com o mesmo sha.

  **Feito (07/11/2026):** a mutação nula recusa ("defeito não aplicado"). A real (um comentário no fim de `apps/frontend/src/utils/searchText.ts`) rodou `src/utils/searchText.test.ts` (11 de 11) e voltou com o sha igual (`b31cba75…`).
## 1. O próprio usuário pelo `sub` (apps/frontend, D2)

- [x] 1.1 (apps/frontend) `auth/sessionStore.ts` exporta o `decodeJwtPayload`, sem mudar o `Session` nem o `isSession`. `auth/sessionStore.test.ts` ganha "registro gravado antes desta change continua válido": um registro com exatamente os campos de hoje passa no `parseSession`.
  - **(guarda)** Defeito: `typeof v.sub === 'string'` exigido no `isSession`. Reprova "registro gravado antes desta change continua válido".
  **Feito:** o `decodeJwtPayload` exportado, com o `Session` e o `isSession` sem mudança.
  **Visto reprovar** (`g1.1-sub-obrigatorio`): `typeof v.sub === 'string'` no `isSession` → `registro gravado antes desta change continua válido` (`expected null to deeply equal { accessToken: 'access-1', … }`; também reprova o caso antigo `grava e lê o registro em buteco.sessao`). Voltou: `apps/frontend/src/auth/sessionStore.ts` (sha igual: `db01af07…`).
- [x] 1.2 (apps/frontend) `auth/session.ts`: `currentUserId(session)`, que devolve o `sub` do id token (string não vazia) ou `null`, e `useCurrentUserId()`, sobre o `useSession`. Mais `isSameUser(sub, id)`, que compara sem diferença de caixa. `auth/currentUserId.test.ts` cobre: o `sub` lido; id token ilegível → `null`; sem `sub` → `null`; a comparação com caixas diferentes; `null` nunca é igual.
  - **(guarda)** Defeito: a comparação com `===` sem normalizar a caixa. Reprova "id da rota em maiúsculas ainda é o próprio".

  **Feito:** `currentUserId`, `isSameUser` e `useCurrentUserId` em `auth/session.ts`; `currentUserId.test.ts` com 5 casos.
  **Visto reprovar** (`g1.2-caixa`): `sub === id` → `id da rota em maiúsculas ainda é o próprio` (`expected false to be true`). Voltou: `apps/frontend/src/auth/session.ts` (sha igual: `f89805f4…`).
## 2. A API do detalhe (apps/frontend)

- [x] 2.1 (apps/frontend) `features/users/api/usersApi.ts`: o tipo `UserDetail` (o `UserSummary` + `desativadoEm`, `convidadoPor`, `sessoes`) e as chamadas `getUser`, `updateUser`, `deactivateUser`, `activateUser`, `revokeUserSessions`, `requestPasswordReset(id, senha)` e `resetTwoFactor`, com o id por `encodeURIComponent`. `queryKeys.ts` ganha `userQueryKey(id)`, sob `['users']`. `usersApi.test.ts`: o método, o caminho e o corpo de cada uma, e o `204` do reset da verificação como `undefined`.
  **Feito:** as 7 chamadas e o `UserDetail`; `usersApi.test.ts` ganhou um `it.each` de 6 rotas, o `204` do reset e o `404` como `ApiError` (14 de 14 no arquivo).
- [x] 2.2 (apps/frontend) `test/fakeUsersApi.ts` estendido às rotas do detalhe, e `test/renderUserDetail.tsx`, que monta `/users/:id` no router de memória com uma sessão cujo id token tem o `sub` escolhido.

  **Feito:** `RAFAEL` (outro, ativo), `BRUNO` (desativado), `PROPRIO` (o `sub` da sessão, `u-aldo`), `JOANA_DETALHE` (convidada) e `RESET_LINK`. O `signInAs(sub)` grava uma sessão com o **nome e o e-mail de `RAFAEL`** e o `sub` escolhido, para o reconhecimento pelo e-mail ter caso que o distinga. O `renderUserDetail` usa `createMemoryRouter`, para o teste ler o `historyAction` do `replace`.
## 3. Rota, carga e cabeçalho (apps/frontend, D1, D3)

- [x] 3.1 (apps/frontend) `app/routes.tsx`: `users/:id` → `UserDetailPage`. `router.test.tsx`: `/users/u-1` sem sessão vai a `/entrar` com o `voltarPara`.
  **Feito:** `users/:id` ao lado de `users`. Em `router.test.tsx`, um `it` novo, "/users/:id (Editar usuário) sem sessão vai ao /entrar com o voltarPara", sem tocar no `it.each` da #250.
- [x] 3.2 (apps/frontend) `UserDetailPage`: a consulta com `retry` próprio, e os estados carregando, erro com "Tentar de novo", inexistente e o convidado com `Navigate replace` para `/users`. `UserDetailPage.test.tsx` cobre os cenários do requisito "Página Editar usuário em /users/:id".
  - **(guarda)** Defeito: a consulta sem o `retry` próprio (o padrão do painel). Reprova "id inexistente faz um GET só".
  - **(guarda)** Defeito: o ramo do convidado removido (renderiza o detalhe). Reprova "o detalhe de um convidado volta para /users".
  **Feito:** o `retry` saiu num módulo próprio, `features/users/detailRetry.ts` (o ESLint `react-refresh/only-export-components` recusa função exportada no arquivo da página). Ele recusa o `404` e, no resto, delega ao `retry` padrão do QueryClient. Assim o `retry: false` dos testes continua valendo, e o caso do `404` usa o `createAppQueryClient()` real.
  **Visto reprovar** (`g3.2a-retry-padrao`): sem o `retry` próprio → `id inexistente faz um GET só` (o "Usuário não encontrado" não apareceu em 500 ms). Voltou: `apps/frontend/src/features/users/pages/UserDetailPage.tsx` (sha igual: `76cf0b4f…`). A primeira tentativa desta mutação não casou com o texto, porque o `prettier` tinha quebrado a chamada em linhas, e o script recusou ("defeito não aplicado"). Foi refeita.
  **Visto reprovar** (`g3.2b-convidado`): sem o `Navigate` do convidado → `o detalhe de um convidado volta para /users` ("lista de Usuários" não apareceu). Voltou: `UserDetailPage.tsx` (sha igual: `76cf0b4f…`).
- [x] 3.3 (apps/frontend) O cabeçalho pelo `DetailHeader`: o selo do estado, "Você", "Desativar"/"Reativar" e o subtítulo, com os textos em `userDetailTexts.ts`. `userDetailTexts.test.ts` cobre o subtítulo (convidado por, primeiro administrador, desativado em, último acesso, sem último acesso) e o estado desconhecido neutro.
  **Feito:** o `Selo` saiu do `UsersTable` para `components/Selo.tsx` (o detalhe é o segundo consumidor), sem mudar o que ele desenha. `userDetailTexts.test.ts` com 9 casos, com agora e fuso fixos.
- [x] 3.4 (apps/frontend) O próprio: a nota, o selo "Você", e a ausência de "Desativar", "Redefinir senha", "Redefinir verificação" e "Encerrar todas as sessões", com os links para Minha conta. O reconhecimento recalcula quando o registro muda.
  - **(guarda)** Defeito: o próprio reconhecido por `session.email === user.email`. Reprova "o próprio é o do sub, mesmo com o nome e o e-mail iguais aos de outro".
  - **(guarda)** Defeito: "Desativar" mostrado para todo ativo, sem conferir o próprio. Reprova "o próprio não tem Desativar".

  **Feito:** inclusive "a troca de conta em outra aba refaz o reconhecimento", por um evento `storage`.
  **Visto reprovar** (`g3.4a-email`): `proprio = sessao?.email === query.data?.email` → `o próprio é o do sub, mesmo com o nome e o e-mail iguais aos de outro` (e mais 8 casos, que dependem do próprio). Voltou: `UserDetailPage.tsx` (sha igual: `76cf0b4f…`). A primeira forma desta mutação usava `query` antes da declaração, o que derrubou todos os casos por erro de execução; foi descartada e refeita, como manda o protocolo.
  **Visto reprovar** (`g3.4b-desativar-proprio`): "Desativar" para todo ativo → `o próprio não tem Desativar`. Voltou: `UserDetailPage.tsx` (sha igual: `76cf0b4f…`).
## 4. Dados (apps/frontend, D5)

- [x] 4.1 (apps/frontend) `UserDataCard`: os campos, "Salvar alterações" pela comparação com o lido, "Descartar", e o `useMutation` do `PUT`, com o `200` no cache do detalhe e a invalidação da lista (e de `['account']` para o próprio, sem renovação). `UserDataCard.test.tsx` cobre os cenários do requisito "Cartão Dados de Editar usuário", inclusive o `409` com `aria-invalid`, o `400` por campo e o `404` levando a inexistente.
  - **(guarda)** Defeito: "sujo" por "houve digitação" (um `useState` que vira `true` no `onChange`). Reprova "voltar ao valor lido desabilita Salvar".
  - **(guarda)** Defeito: a invalidação de `['account']` removida. Reprova "o próprio salva e Minha conta é relida".

  **Feito:** 10 casos em `UserDataCard.test.tsx`, pela página. A invalidação da lista é `exact`, para não reler o próprio detalhe que acabou de receber a resposta.
  **Visto reprovar** (`g4.1a-sujo-por-digitacao`): `sujo = rascunho !== null` → `voltar ao valor lido desabilita Salvar` (`Received element is not disabled`). Voltou: `apps/frontend/src/features/users/components/UserDataCard.tsx` (sha igual: `9477d27d…`).
  **Visto reprovar** (`g4.1b-sem-conta`): sem a invalidação de `['account']` → `o próprio salva e Minha conta é relida` (`expected "invalidateQueries" to be called with arguments: [ { queryKey: [ 'account' ] } ]`). Voltou: `UserDataCard.tsx` (sha igual: `9477d27d…`).
## 5. A página como uma ação por vez (apps/frontend, D4)

- [x] 5.1 (apps/frontend) Na `UserDetailPage`, o estado da ação aberta (`nenhuma`, `desativar`, `redefinir-senha`, `link`, `redefinir-2fa`, `encerrar-sessoes`) e os avisos de sucesso, passados aos cartões por prop. Abrir uma ação fecha a anterior. O rascunho de Dados não depende dele. Os casos "abrir outra ação tira o link" e "o rascunho de Dados sobrevive a uma ação" ficam na 7.3 e na 4.1, depois que as ações existirem.

  **Feito:** o tipo `Acao` da `UserDetailPage`. Os casos ficaram em `PasswordResetLinkPanel.test.tsx` ("abrir outra ação tira o link") e em `UserDataCard.test.tsx` ("o rascunho de Dados sobrevive a uma ação"), com os guardas da 7.3.
## 6. A lista e Minha conta (apps/frontend, D1)

- [x] 6.1 (apps/frontend) `UsersTable`: o "Editar" na linha do ativo e na do desativado, um link do router para `/users/{id}` com `aria-label` "Editar {nome}".
  **Feito:** o "Editar" (`Button` do Mantine com `component={Link}`) na célula Ações do ativo e do desativado.
- [x] 6.2 (apps/frontend) `UsersPage.test.tsx`: sai "a lista não tem link para o detalhe (/users/:id chega com a #183)", e entram "a linha do ativo e a do desativado têm Editar para /users/{id}" e "a linha do convidado não tem Editar".
  - **(guarda)** Defeito: o "Editar" só na linha do ativo. Reprova "a linha do ativo e a do desativado têm Editar para /users/{id}".
  - **(guarda)** Defeito: o "Editar" em toda linha, inclusive a do convidado. Reprova "a linha do convidado não tem Editar".
  **Feito:** o caso da #250 saiu, e os dois novos entraram no mesmo `describe`.
  **Visto reprovar** (`g6.2a-so-ativo`): "Editar" só em `estado === 'ativo'` → `a linha do ativo e a do desativado têm Editar para /users/{id}` (`Unable to find … "Editar Bruno Teixeira"`). Voltou: `apps/frontend/src/features/users/components/UsersTable.tsx` (sha igual: `618cf08d…`).
  **Visto reprovar** (`g6.2b-convidado`): "Editar" em toda linha → `a linha do convidado não tem Editar`. Voltou: `UsersTable.tsx` (sha igual: `618cf08d…`).
- [x] 6.3 (apps/frontend) `AccountDataCard`: "Usuários" vira link para `/users/{id}`, com o `id` do `GET /account`. O caso `"use Usuários" fica sem link até a tela existir (#183)` (`features/account/pages/AccountPage.test.tsx`) é substituído por "o link leva a /users/{id da conta}", como o requisito "Cartão Dados" da `panel-account-page` muda.
  - **(guarda)** Defeito: o link para `/users`. Reprova "o link leva a /users/{id da conta}".

  **Feito:** o caso da #246 saiu, e entrou "o link leva a /users/{id da conta}" (`/users/u-1`, o `id` do `ACCOUNT` do fake).
  **Visto reprovar** (`g6.3-lista`): o link para `/users` → `o link leva a /users/{id da conta}`. Voltou: `apps/frontend/src/features/account/components/AccountDataCard.tsx` (sha igual: `62c40ee3…`).
## 7. Formas de entrada, Redefinir senha e o link (apps/frontend, D6, D7, Q11)

- [x] 7.1 (apps/frontend) `CopyableLink`: a caixa do link com "Copiar link" e "Copiado ✓", extraída do `InviteLinkPanel`, que passa a usá-la. Os casos de `InviteLinkPanel.test.tsx` rodam sem mudança de asserção e mostram o mesmo de hoje.
  **Feito:** `components/CopyableLink.tsx`, com o mesmo markup que estava no `InviteLinkPanel`. `InviteLinkPanel.test.tsx` não mudou e passa (7 de 7).
- [x] 7.2 (apps/frontend) `SignInFormsCard`: a linha Senha pelo alvo, o rodapé, e o formulário "Redefinir senha" pelo `useCallerPassword` e o `CallerPasswordField`, com o `403`, o `409` (aviso e detalhe relido) e o `404`. `SignInFormsCard.test.tsx` cobre os cenários dos requisitos "Formas de entrada" e "Redefinir senha com a senha de quem chama".
  - **(guarda)** Defeito: o envio sem conferir a senha vazia (o `call` direto, sem o `enviar` do `useCallerPassword`). Reprova "senha vazia não chama a rota".
  - **(guarda)** Defeito: o `401` tratado como falha genérica, no aviso. Reprova "senha errada na redefinição fica no campo, sem deslogar".
  **Feito:** 8 casos em `SignInFormsCard.test.tsx`, inclusive o `403` e "Cancelar fecha e limpa a senha".
  **Visto reprovar** (`g7.2a-senha-vazia`): a conferência da senha vazia desligada no `useCallerPassword` (`if (false)`) → `senha vazia não chama a rota` (o pedido saiu, o fake respondeu o link, e o formulário deu lugar ao painel). Voltou: `apps/frontend/src/features/users/callerPassword.ts` (sha igual: `4192ef6a…`). A mutação foi no hook comum, porque é ele que faz a conferência para as três ações.
  **Visto reprovar** (`g7.2b-401-generico`): o `401` fora do ramo do campo → `senha errada na redefinição fica no campo, sem deslogar` (sem `aria-invalid`). Voltou: `callerPassword.ts` (sha igual: `4192ef6a…`).
- [x] 7.3 (apps/frontend) `PasswordResetLinkPanel`, com a forma do quadro (Q11) e o texto da Q5, numa chamada direta, sem `useMutation`, com o link só no estado da página e o detalhe invalidado depois do `200`. `PasswordResetLinkPanel.test.tsx` e a página cobrem os cenários do requisito "Link de redefinição mostrado uma vez", mais "abrir outra ação tira o link" (5.1).
  - **(guarda)** Defeito: o pedido por `useMutation`. Reprova "o link não fica no MutationCache".
  - **(guarda)** Defeito: o link gravado em `sessionStorage` ao chegar. Reprova "o link não vai a storage". O caso percorre o storage por `length` e `key(i)` e afirma que vê o `buteco.sessao`, para não passar contra o defeito como na #250.
  - **(guarda)** Defeito: o "Concluir" só esconde o painel, e o link continua no estado. Reprova "Concluir tira o link".
  - **(guarda)** Defeito: abrir outra ação não limpa o link. Reprova "abrir outra ação tira o link".

  **Feito:** 6 casos em `PasswordResetLinkPanel.test.tsx`, inclusive a forma do quadro sem o e-mail nem o aviso amarelo, e "08/11/2026 às 18:30" no fuso de São Paulo.
  **Visto reprovar** (`g7.3a-usemutation`): o pedido por `useMutation` → `o link não fica no MutationCache` (`expected [ Mutation{…} ] to have a length of +0 but got 1`). Voltou: `apps/frontend/src/features/users/components/SignInFormsCard.tsx` (sha igual: `d8e39cc1…`).
  **Visto reprovar** (`g7.3b-storage`): `sessionStorage.setItem('ultimo-link', …)` no `onLink` → `o link não vai a storage` (`expected 'ultimo-link=https://…' not to contain 'Hq8vN3…'`). Voltou: `UserDetailPage.tsx` (sha igual: `76cf0b4f…`).
  **Visto reprovar** (`g7.3c-concluir-esconde`): "Concluir" com um `hidden` local, e o link no DOM → `Concluir tira o link`. Voltou: `apps/frontend/src/features/users/components/PasswordResetLinkPanel.tsx` (sha igual: `d028bba3…`).
  **Visto reprovar** (`g7.3d-outra-acao`): o `abrir` que mantém a ação `link` → `abrir outra ação tira o link` (e `Concluir tira o link`). Voltou: `UserDetailPage.tsx` (sha igual: `76cf0b4f…`).
## 8. Verificação em 2 etapas (apps/frontend, D9)

- [x] 8.1 (apps/frontend) `UserTwoFactorCard`: os dois estados, a confirmação, e o `204`/`409` relendo o detalhe, com o `403` no aviso. `UserTwoFactorCard.test.tsx` cobre os cenários do requisito "Verificação em 2 etapas de outro usuário".
  - **(guarda)** Defeito: o botão sem `disabled` com `duasEtapas: false`. Reprova "com a verificação desligada, o botão fica desabilitado".
  - **(guarda)** Defeito: o `204` só trocando o selo (`setQueryData` com `duasEtapas: false`), sem reler. Reprova "depois de redefinir a verificação, as sessões vão a Nenhuma sessão ativa".

  **Feito:** 6 casos em `UserTwoFactorCard.test.tsx`, inclusive o desativado com a verificação ativa e o botão habilitado.
  **Visto reprovar** (`g8.1a-desligada-habilitado`): `disabled={confirmando}` → `com a verificação desligada, o botão fica desabilitado`. Voltou: `apps/frontend/src/features/users/components/UserTwoFactorCard.tsx` (sha igual: `2d1f3e02…`).
  **Visto reprovar** (`g8.1b-sem-releitura`): o `204` com `setQueryData(…, { duasEtapas: false })` no lugar da releitura → `depois de redefinir a verificação, as sessões vão a Nenhuma sessão ativa` (e `já redefinida em outra aba`). Voltou: `UserDetailPage.tsx` (sha igual: `76cf0b4f…`).
## 9. Sessões (apps/frontend, D10, Q12)

- [x] 9.1 (apps/frontend) `UserSessionsCard`: as contagens (plural, singular, zero), o último uso, o texto do próprio sem "incluindo esta" e sem último uso, a confirmação com o singular, e o `200` com "N sessões encerradas.", "1 sessão encerrada." ou "Nenhuma sessão estava aberta.". `UserSessionsCard.test.tsx` cobre os cenários do requisito "Sessões de outro usuário".
  - **(guarda)** Defeito: o botão mostrado também com `ativas: 0`. Reprova "sem sessões ativas, não há botão de encerrar".
  - **(guarda)** Defeito: o texto do quadro de volta ("{N} sessões ativas, incluindo esta") para o próprio. Reprova "as sessões do próprio sem a dedução".

  **Feito:** 7 casos em `UserSessionsCard.test.tsx`, inclusive a falha passageira com a confirmação aberta.
  **Visto reprovar** (`g9.1a-botao-com-zero`): `ativas >= 0` → `sem sessões ativas, não há botão de encerrar`. Voltou: `apps/frontend/src/features/users/components/UserSessionsCard.tsx` (sha igual: `8d7a42c0…`).
  **Visto reprovar** (`g9.1b-incluindo-esta`): `"{N} sessões ativas, incluindo esta"` para o próprio → `as sessões do próprio sem a dedução`. Voltou: `apps/frontend/src/features/users/userDetailTexts.ts` (sha igual: `4f8583cb…`).
## 10. Desativar e reativar (apps/frontend, D8)

- [x] 10.1 (apps/frontend) Na `UserDetailPage`: a confirmação de desativar acima dos cartões, o `200` no cache do detalhe e a lista invalidada; "Reativar" sem confirmação, com o aviso "Reativação concluída…"; o `403`/`409` no aviso e o `404` em inexistente. `UserDetailPage.test.tsx` cobre os cenários do requisito "Desativar e reativar" e "403 do servidor no aviso".
  - **(guarda)** Defeito: "Desativar" do cabeçalho chamando a rota direto, sem a confirmação. Reprova "cancelar a desativação" (a rota não pode ter sido chamada).

  **Feito:** os casos em `UserDetailPage.test.tsx` (desativar com confirmação, cancelar, `403` no aviso, `404` numa ação, reativar sem confirmação).
  **Visto reprovar** (`g10.1-sem-confirmacao`): "Desativar" do cabeçalho chamando a rota direto → `cancelar a desativação` (e os outros três casos da confirmação: a confirmação nunca abre). Voltou: `UserDetailPage.tsx` (sha igual: `76cf0b4f…`).
## 11. Suítes e conferências

- [x] 11.1 (apps/frontend) `npm run lint` e `npm run build`, sem erro. A diferença do bundle contra a baseline é medida nos dois worktrees, minificado e gzip.
  **Feito (08/11/2026):** `npm run lint` sem erro nem aviso; `npm run build` (com o `tsc -b`) passa nos dois worktrees.

  | | `9c49e4b` (baseline) | change | diferença |
  |---|---|---|---|
  | `index-*.js` minificado | 1.155,22 kB | 1.169,76 kB | **+14,54 kB** |
  | gzip (Vite) | 336,09 kB | 339,29 kB | **+3,20 kB** |

  Sem dependência nova.
  **Premissa que caiu (formatação):** um `prettier --write` em diretórios inteiros reformatou 19 arquivos que a change não tocava. O `main` tem 129 arquivos fora da configuração (`printWidth: 100`, conferido no worktree da baseline), e nada roda o `format:check` (não há CI; débito aberto na [#255](https://github.com/aldovrando-oliveira/buteco-agentes/issues/255)). Os 19 voltaram ao `HEAD` **um a um, pelo nome**, e os 7 que a change editava voltaram ao `HEAD` com só as edições reaplicadas por script (`routes.tsx`, `session.ts` e `sessionStore.test.ts` não tinham sido reformatados). Os arquivos novos ficam no formato da configuração.

- [x] 11.2 (apps/frontend) Suíte `frontend` completa, pelo `suite-183.sh`, com o relatório por nome comparado ao `nomes.txt` da baseline: nenhum caso sumiu, mudou de nome ou deixou de passar, fora o da 6.2 (que sai) e o "use Usuários sem link" da 6.3 (substituído). Os casos novos, contados por arquivo.
  **Feito (08/11/2026)**, com o RabbitMQ de pé, `healthy`, pelo healthcheck TCP da #251:
  - **r1** (`~/.cache/buteco-agents/183-frontend-r1/`): APROVADA, 2.328 de 2.328 em 185 arquivos, 169 s; começou com load 5,49 e CPU em 100, CPU mínima 60;
  - **r2** (`183-frontend-r2/`), depois da correção da 11.3: **APROVADA, 2.329 de 2.329 em 185 arquivos, 161 s**; começou com load 3,46 e CPU em 100, CPU mínima 58.

  **Por nome, contra a baseline (`183-baseline/nomes.txt`):** saem **só** os dois casos trocados:
  - `UsersPage > a lista não tem link para o detalhe (/users/:id chega com a #183)` (6.2);
  - `AccountPage > cartão Dados > "use Usuários" fica sem link até a tela existir (#183)` (6.3).

  Nenhum outro caso sumiu, mudou de nome ou deixou de passar. Entram 83:

  | arquivo | casos novos |
  |---|---|
  | `UserDetailPage` | 18 |
  | `UserDataCard` | 10 |
  | `userDetailTexts` | 9 |
  | `SignInFormsCard` | 8 |
  | `usersApi` | 8 |
  | `UserSessionsCard` | 7 |
  | `PasswordResetLinkPanel` | 7 |
  | `UserTwoFactorCard` | 6 |
  | `currentUserId` | 5 |
  | `UsersPage` | 2 |
  | `AccountPage`, `sessionStore`, `router` | 1 cada |

  Da r1 para a r2, a única diferença por nome é o caso da correção da 11.3.

- [x] 11.3 (apps/frontend + apps/api) **Conferência no navegador**, com o `apps/api` local (`TZ=America/Sao_Paulo`) e o Vite:
  - o "Editar" na lista, levando ao detalhe;
  - outro usuário: editar o nome, o `409` com o e-mail de outro, "Descartar";
  - "Redefinir senha" com a senha errada (o erro no campo, a sessão continua, uma falha no banco) e com a certa, o link colado numa janela anônima abrindo o `/redefinir-senha`, e "Concluir";
  - "Redefinir verificação" num usuário com as 2 etapas, e as sessões indo a zero;
  - "Encerrar todas as sessões" com a confirmação;
  - desativar e reativar;
  - o próprio: a nota, os links e o "Usuários" de Minha conta levando a ele;
  - um id inexistente e um convidado em `/users/:id`.

  O que não puder ser conferido na aba de automação ("Copiar link", o botão desabilitado pelo `Retry-After` no desenvolvimento, o escuro) fica registrado com o motivo e vai para a validação visual.
  **Feito (08/11/2026)**, com o `apps/api` local (`TZ=America/Sao_Paulo`, `dotnet run --launch-profile http`) e o Vite do worktree, no Chrome. Quem chama é `conta.246@buteco.local`, a conta de validação da #246, com a sessão já aberta. O helper é `~/.cache/buteco-agents/183-conferencia/val183.sh`, cópia do `val250.sh`; ele cria os alvos: Rafael (ativo, com as 2 etapas ligadas pela própria conta e sessões abertas), Bruno (desativado) e Joana (convidada). Senhas e ids em `credenciais.env` e `ids.env`, na mesma pasta.
  - **Lista:** "Editar" na linha de todo ativo e desativado, e nenhum nos convites; o clique leva a `/users/<id>`.
  - **Outro usuário (Rafael):** "Convidado por operador em 07/11/2026 · último acesso hoje, 00:01", "Ativa", "5 sessões ativas · último uso hoje, 00:01".
  - **Dados:** o nome salvo ("Alterações salvas.", o título trocado); o e-mail de outra conta → "Já existe um usuário com este e-mail." no campo, com `aria-invalid="true"`; "Descartar" voltou ao e-mail lido, com "Salvar" desabilitado.
  - **Redefinir senha:**
    - vazia: "Informe a sua senha.", sem pedido;
    - errada: "Senha inválida." no campo, com `aria-invalid`, o campo limpo, a página em `/users/<id>` e o `buteco.sessao` gravado. **No banco, essa uma falha pausou a conta** (`last_lock_minutes` de 5 para 10): a conta trazia falhas da conferência da #250, e o bloqueio do servidor é progressivo (#180). O envio que dispara a pausa responde `401`;
    - certa, com a conta pausada: o aviso amarelo "…pausada por alguns minutos." no formulário, sem deslogar;
    - certa, depois da pausa: "Link para redefinir a senha", o link e "Vale até 09/11/2026 às 00:13". Por JS, o fragmento do link não estava no `localStorage`, no `sessionStorage` (5 chaves, com o `buteco.sessao`), no endereço nem no `history.state`;
    - o link aberto numa aba nova: "Criar nova senha", com "Olá, Rafael Conferência 183 Editado" e o mesmo vencimento (sem consumir o link);
    - abrir "Desativar" com o link aberto: o link saiu do DOM; um segundo link (diferente do primeiro) saiu do DOM no "Concluir".
  - **Encerrar sessões:** "Encerrar as 5 sessões de Rafael Conferência 183 Editado? …", "5 sessões encerradas." e "Nenhuma sessão ativa" na releitura. Abrir a confirmação fechou o formulário de redefinição.
  - **Redefinir as 2 etapas** (com uma sessão nova aberta pelo helper): antes, "1 sessão ativa · último uso hoje, 00:05"; depois, "Não configurada", "Nenhuma sessão ativa", o botão desabilitado e nenhum aviso.
  - **Desativar e reativar:** a confirmação; "Desativado", "Reativar", "Reative para gerar um link" e "desativado em 08/11/2026"; depois "Reativação concluída. … pode entrar de novo com a senha que já tinha.", "Desativar" e "Redefinir senha" de volta.
  - **Desativado (Bruno):** "Desativado", "Reativar", "Reative para gerar um link", "Não configurada" com o botão desabilitado, "Nenhuma sessão ativa" sem botão.
  - **O próprio** (pelo id em maiúsculas): "Você", a nota com o link para Minha conta, "Trocar em Minha conta", "Gerencie a sua em Minha conta.", "1 sessão ativa" sem "incluindo esta", "Ver em Minha conta"; o único botão é "Salvar alterações".
  - **`404`:** um GUID inexistente mostrou "Usuário não encontrado" em **53 ms** (sem as repetições); um id que não é GUID, o mesmo.
  - **Convidado (Joana):** `/users/<id>` voltou para `/users`.
  - **Minha conta:** "Usuários" leva a `/users/<id da conta>`, que é a página do próprio.

  **Achado, corrigido na hora (convenção 9, seguir e registrar):** ao ir do detalhe de uma pessoa para o de outra, o aviso "Reativação concluída. Rafael…" continuou na página do Bruno e na do próprio. O React Router reaproveita a instância quando só o `:id` muda, e a ação aberta (inclusive um link de redefinição) passaria de uma pessoa para outra. A `UserDetailPage` passou a montar o conteúdo com `key={id}`. Caso novo: "o link não passa para a página de outro usuário" (`PasswordResetLinkPanel.test.tsx`).
  **Visto reprovar** (`g11.3-sem-key`): sem o `key` → `o link não passa para a página de outro usuário`. Voltou: `apps/frontend/src/features/users/pages/UserDetailPage.tsx` (sha igual: `dd460a16…`). No navegador, depois da correção, o aviso da reativação do Bruno não apareceu no Rafael.
  Os shas registrados nos guardas da 3.2, 3.4, 7.3, 8.1 e 10.1 (`76cf0b4f…`) são da `UserDetailPage` antes desta correção.

  **Não conferido no navegador, e por quê:**
  - **"Copiado ✓":** na aba de automação, o `navigator.clipboard` não resolve sem foco no documento (como na #250). O caso `Copiar link` da suíte cobre o valor copiado.
  - **O botão desabilitado pelo `Retry-After`:** no desenvolvimento, o cabeçalho não é legível (pedido entre origens; #246 D8), e o texto cai em "alguns minutos". O caso `conta pausada` da suíte cobre o botão com o cabeçalho legível.
  - **O escuro:** fica para a validação visual.

- [ ] 11.4 **Validação visual na homologação da M1** (convenção 14; decisão do dono, 08/11/2026, a mesma da #250). O dono valida quadro a quadro depois que todas as issues da entrega estiverem mergeadas, sem issue própria, e o PR é mergeado antes dela. Se a homologação achar algo, a correção vira trabalho da própria homologação. A validação cobre:
  - **`Usuarios` (`entrega=m1`):** o "Editar" na linha do ativo e na do desativado, e a ausência dele no convidado;
  - **`MinhaConta`:** "Usuários" como link, levando ao detalhe do próprio;
  - **`UsuarioDetalhe` (`entrega=m1`), `alvo`:** `outro`; `desativado`; `proprio`; `carregando`; `erro`; `inexistente`;
  - **`verificacao`:** `ativa` e `desligada`, em `outro` e em `desativado`;
  - **`acao`:** `nenhuma`; `email-em-uso`; `redefinir-senha`; `senha-errada`; `senha-bloqueada`; `link-gerado` (com "Copiado ✓"); `reativado`; `redefinir-2fa`; `encerrar-sessoes`; `sessoes-encerradas`; `desativar`;
  - **os estados sem quadro:**
    - "Sua senha" vazia;
    - o convidado em `/users/:id`;
    - `403`, `409`, `404`, `429`, `503` e rede nas ações;
    - o `400` de Dados;
    - "Nenhuma sessão estava aberta.";
    - os singulares: "1 sessão ativa", "Encerrar a sessão de {nome}?", "1 sessão encerrada.";
    - o estado desconhecido;
  - **as divergências, conferidas como registro e não como defeito:**
    - Q3: "último acesso hoje, 14:15";
    - Q5: "Vale até {expiraEm}";
    - Q12: as sessões do próprio sem "incluindo esta";
    - o N do `Retry-After` no bloqueio;
  - **os dois esquemas de cor.**
  **Pendente: validação visual na homologação da M1**, sem parada (decisão do dono, 08/11/2026). O checklist acima vai igual no `02` e no PR.

- [x] 11.5 Medição da convenção 18: produção, testes e duplos separados, por `git diff --numstat` contra `9c49e4b` mais as linhas dos arquivos novos, só `apps/frontend/src`, comparados à projeção do design (~885 / ~1.210 / ~70).
  **Feito (08/11/2026)**, por `git diff --numstat 9c49e4b` mais as linhas dos arquivos novos, só `apps/frontend/src`, antes da validação visual (se ela mudar código, a medição se refaz):
  - **produção:** +1.165 / −35 em 19 arquivos, contra ~885 projetados (**+32%**). Com o erro da #250 (+31%), o design projetava ~1.160;
  - **testes:** +1.168 / −12 em 13 arquivos, contra ~1.210 (**−3%**);
  - **duplos:** +118 / −1 em 2 arquivos, contra ~70 (**+69%**): as fixtures do detalhe (`RAFAEL`, `BRUNO`, `PROPRIO`, `JOANA_DETALHE`) e o `renderUserDetail` com o data router e o `signInAs`.

## 12. Documentação e registros

- [x] 12.1 (docs) `02-HISTORICO_E_STATUS.md` (a entrada da change) e `CHANGELOG.md`, antes do archive, com a validação visual "na homologação da M1" e o checklist da 11.4.
  **Feito (08/11/2026):** `CHANGELOG.md`, "Editar usuário no painel" em Added, com a validação visual na homologação da M1; `02-HISTORICO_E_STATUS.md`, a entrada `editar-usuario-painel`, com as decisões, as premissas que caíram, as medições e a validação pendente.

- [x] 12.2 (issues) Comentários de fechamento, com o ok do dono:
  - na **#183**, a entrega e a validação visual na homologação;
  - na **#184**: Editar usuário abre para todo usuário ativo na M1, e o papel no cartão Dados, "O novo papel vale…" e o "Primeiro administrador" voltam a ser revistos com ela;
  - na **#194**, a M1 com 17 de 18 itens entregues.

  O PR leva `Closes #183`, e o corpo dele traz o checklist da 11.4.
  **Feito (08/11/2026):** [comentário de entrega na #183](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6716707771), [na #184](https://github.com/aldovrando-oliveira/buteco-agentes/issues/184#issuecomment-6716707951) e [na #194](https://github.com/aldovrando-oliveira/buteco-agentes/issues/194#issuecomment-6716708180). O da #194 foi corrigido logo depois de publicado: a primeira versão punha a #245 e a #246 na homologação, e as duas já tinham sido validadas pelo dono. Também aberta, a pedido do dono, a [#255](https://github.com/aldovrando-oliveira/buteco-agentes/issues/255), sem milestone: os 130 arquivos do `apps/frontend` fora da formatação do prettier.
