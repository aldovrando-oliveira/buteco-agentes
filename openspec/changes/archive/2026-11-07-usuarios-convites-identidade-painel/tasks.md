Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-250.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-246.sh`. A mutação toca só o trecho nomeado, e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra:
- o nome do teste;
- a mensagem da reprovação;
- o "voltou: <arquivo>", com o sha igual ao de antes.

Uma mutação que quebra a compilação e reprova por "no tests" não vale. Ela é descartada e refeita.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-250.sh`, e a principal é a `frontend`.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim.
- Antes de cada rodada da `frontend`, o load fica abaixo de 6.

**A baseline** está em `~/.cache/buteco-agents/250-baseline/`, em `a852946`, com a árvore limpa:
- 2166 aprovados em 168 arquivos, 0 reprovados, em 207 s;
- o relatório por nome está em `nomes.txt` (2166 linhas);
- a rodada começou com load 5,38 e CPU em 100, e a CPU caiu até 30 (`APROVADA`).

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

Nenhum caso existente é removido, renomeado ou afrouxado. O caso que muda de arquivo na 1.1 leva o mesmo nome.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/guarda-250.sh`, cópia do `guarda-246.sh`, com o projeto `frontend`. Conferir com:
  - uma mutação que não altera nada: o script recusa com "defeito não aplicado";
  - uma mutação real num arquivo de `apps/frontend/src`: o arquivo volta com o mesmo sha.

  **Feito (07/11/2026):** a mutação nula recusa ("defeito não aplicado"). Uma mutação real (um comentário no fim de `apps/frontend/src/utils/searchText.ts`) rodou `src/utils/searchText.test.ts` (11 de 11) e voltou com o sha igual (`b31cba75…`). O caminho do arquivo é relativo à raiz do worktree, e o do teste, a `apps/frontend`.
## 1. Avisos de falha, `Retry-After` e datas num lugar comum (apps/frontend, D1, Q2)

- [x] 1.1 (apps/frontend) `features/account/accountDates.ts` vai para `utils/panelDates.ts`, e o `accountDates.test.ts` vai para `utils/panelDates.test.ts`, com os mesmos casos e os mesmos nomes.
  - Entra o `formatShortDateTime(iso, timeZone?)` (`dd/mm, HH:MM`), com um caso de virada do dia pelo fuso.
  - Os importadores de Minha conta passam a importar do lugar novo.
  - **(guarda)** Defeito: o `formatShortDateTime` montado pela data UTC. Reprova "o formato curto usa o dia e a hora do fuso".
  **Feito:** os dois arquivos movidos com `mv`, sem `git mv`, para nada ir ao índice antes do archive. O `describe('accountDates')` ficou com o mesmo nome, para os três casos manterem o nome completo. Entrou `formatShortDateTime` e o caso novo (4 de 4).
  **Visto reprovar** (`~/.cache/buteco-agents/250-guardas/g1.1-utc`): o formato curto montado por `toISOString()` → `o formato curto usa o dia e a hora do fuso` (`Expected: "10/11, 22:30"`, `Received: "11/11, 01:30"`). Voltou: `apps/frontend/src/utils/panelDates.ts` (sha igual: `2d564054…`).
- [x] 1.2 (apps/frontend) `features/account/cardFailure.ts` vai para `utils/failureNotice.ts`.
  - O `instanceof ApiError` vira `isApiFailure(error)`, que confere `status` numérico e, quando existem, `retryAfter` e `title`.
  - `utils/failureNotice.test.ts` cobre: o `423` com e sem `Retry-After`; o `429`; o `503`; o `title`; o erro de rede; e **um erro com a forma do `ApiError`, mas de outra classe**.
  - O `AccountCard` e os cartões de Minha conta passam a importar daqui.
  - **(guarda)** Defeito: o `instanceof ApiError` do `accountApi` de volta. Reprova "um erro de outra classe, com a forma de ApiError, recebe o texto do bloqueio", que receberia "Não foi possível falar com o servidor.".
  **Feito:** `isApiFailure` exige `instanceof Error`, `status` numérico, e `retryAfter` e `title` do tipo certo quando vêm. O `failureText`, o `failureNotice` e o `blockedUntil` usam ele. `failureNotice.test.ts` tem 5 casos, um deles com um objeto sem `status` numérico. Os imports de `entryNotices` e `messages` passaram a `../features/auth/…`.
  **Visto reprovar** (`g1.2-instanceof`): o `instanceof ApiError` do `accountApi` de volta no `failureText` → `um erro de outra classe, com a forma de ApiError, recebe o texto do bloqueio` (`Received: "Não foi possível falar com o servidor."`). Voltou: `apps/frontend/src/utils/failureNotice.ts` (sha igual: `64775eab…`).
- [x] 1.3 (apps/frontend) `features/account/useBlockedUntil.ts` vai para `hooks/useBlockedUntil.ts`, sem mudança de código. Os importadores de Minha conta são ajustados.
  **Feito:** movido sem mudança, e os três cartões de Minha conta passaram a importar de `hooks/useBlockedUntil`. `src/utils` e `src/features/account` rodam com 88 de 88.
- [x] 1.4 (apps/frontend) Os arquivos de teste de `features/account/` rodam e passam sem mudança de asserção. Conferir pelo relatório por nome: nenhum caso de Minha conta sumiu ou mudou de nome contra a baseline, fora os do `accountDates.test.ts`, que reaparecem com o mesmo nome em `panelDates.test.ts`.

  **Feito (07/11/2026), pelo relatório por nome das duas rodadas da 10.2:** nenhum dos 2166 casos da baseline sumiu ou mudou de nome. Os três de `accountDates.test.ts` reaparecem, com o mesmo nome completo (`accountDates > …`), em `src/utils/panelDates.test.ts`. Todos os arquivos de `features/account/` passaram nas duas rodadas, e nenhuma reprovação foi em Minha conta.
## 2. Rotas e a seção "Acesso" (apps/frontend, D1)

- [x] 2.1 (apps/frontend) `app/routes.tsx`: `users` e `identity` dentro do `ProtectedRoute` e da casca. `router.test.tsx`: `/users` e `/identity` sem sessão vão a `/entrar` com o `voltarPara`.
  **Feito (07/11/2026):** `users` e `identity` ao lado de `account`. Em `router.test.tsx`, um `it.each` novo, "%s sem sessão vai ao /entrar com o voltarPara", com os dois casos. O `voltarPara` conferido é a própria rota.
- [x] 2.2 (apps/frontend) `AppShell.tsx`: o requisito "Seção Acesso na navegação".
  - O rótulo é um `Text` no estilo do `.navsec`.
  - Os itens são `Usuários` (ícone `Users`) e `Identidade` (ícone `Shield`), no mesmo `NavLink` dos outros.
  - `AppShell.test.tsx` cobre os quatro cenários do requisito. Os casos de hoje continuam.
  - **(guarda)** Defeito: um item "Aplicações externas" na seção. Reprova "o menu não tem Aplicações externas".
  - **(guarda)** Defeito: o rótulo "Acesso" como `NavLink` para `/users`. Reprova "o rótulo Acesso não é link".

  **Feito:** `accessItems` depois de `navItems`, com o mesmo `navItem` (o `NavLink` de antes, extraído para os dois grupos usarem). O rótulo é um `Text` `xs`, `600`, em maiúsculas, `dimmed`. **Na implementação, o tom terciário do protótipo virou o `dimmed`**, pelo mesmo motivo do `SectionLabel` (o contraste da `frontend-visual-theme`). A spec e o D1 do design foram ajustados na redação, sem mudar comportamento. `AppShell.test.tsx` ganhou 4 casos, no `describe('seção Acesso')`.
  **Caso antigo estendido, sem afrouxar:** "dá um ícone decorativo a cada item, sem roubar o nome acessível" exigia **exatamente 6** ícones no menu, e a spec aprovada leva o menu a 8. A lista do caso ganhou "Usuários" e "Identidade", que passam pelas mesmas duas asserções (o ícone existe e é `aria-hidden`), e o total foi de 6 para 8, com um comentário. Nenhuma asserção saiu, então a regra de parada não se aplica (só se acrescentou cobertura para o requisito novo). 29 de 29.
  **Vistos reprovar** (`~/.cache/buteco-agents/250-guardas/`):
  - g2.2-aplicacoes (um item `/external-apps` "Aplicações externas" em `accessItems`) → `o menu não tem Aplicações externas` e também `dá um ícone decorativo a cada item…` (9 ícones). Voltou: `AppShell.tsx` (sha igual: `8761e9df…`);
  - g2.2-rotulo-link (o rótulo trocado por `<NavLink component={Link} to="/users" label="Acesso" />`) → `o rótulo Acesso não é link` (`expected <a …> to be null`). Voltou: idem, mesmo sha.
## 3. A API de Usuários (apps/frontend)

- [x] 3.1 (apps/frontend) `features/users/api/usersApi.ts`:
  - a cópia do `request<T>` e do `ApiError`, com o comentário da convenção 2, como no `accountApi.ts`;
  - `listUsers()`, `inviteUser({ nome, email, senha })`, `renewInvitation(id, senha)` e `cancelInvitation(id)`;
  - os tipos `UserSummary`, `InvitationSummary`, `InvitationCreated` e `AccessLink`.
  **Feito:** a cópia do `request<T>` e do `ApiError`, com o comentário da convenção 2 e da Q2. Entraram as quatro chamadas e os tipos, e o `queryKeys.ts` com `USERS_QUERY_KEY`.
- [x] 3.2 (apps/frontend) `usersApi.test.ts` confere:
  - o caminho, o método e o corpo de cada chamada;
  - o `ApiError` com o `status`, o `title`, o `problem.errors` do `400` e o `retryAfter` do `423`;
  - o `204` do cancelamento.

  **(guarda)** Defeito: o `retryAfter` não lido (`undefined` fixo no `ApiError`). Reprova "o 423 traz o Retry-After".

  **Feito:** 6 casos:
  - o `GET /users` com o token;
  - os corpos do convite e do novo link (com o id codificado);
  - o `204` do cancelamento, sem corpo;
  - o `400` com `errors`;
  - o `423` com o `Retry-After`.

  O duplo `features/users/test/fakeUsersApi.ts` **reexporta o `fakeAccountApi`**, que já é genérico (método e caminho, fila de respostas, o `sessionFetch` real por cima), e acrescenta só os dados dos usuários. A Q2 vale para o código de produção, e o duplo de teste não ganhou cópia.
  **Visto reprovar** (`g3.2-retry-after`): o `retryAfterSeconds(response)` trocado por `undefined` no `ApiError` → `o 423 traz o Retry-After` (`expected undefined to be 300`). Voltou: `usersApi.ts` (sha igual: `6845c727…`).
## 4. A lista de Usuários (apps/frontend, D2, D8)

- [x] 4.1 (apps/frontend) `UsersPage.tsx`, `UsersTable.tsx` e `usersTexts.ts`, pelos requisitos "Página Usuários em /users", "Colunas da lista de Usuários" e "Ações da linha sem destino inexistente":
  - o `SectionedCard` "Usuários", com "Convidar usuário";
  - o carregando e o erro com "Tentar de novo";
  - os dois cartões de texto da versão `m1`;
  - o último acesso pelo `formatRelative` (Q3);
  - o vencimento pelo `formatShortDateTime`.
  **Feito:**
  - `UsersPage.tsx`: o título, o subtítulo, o `SectionedCard` "Usuários" com a máquina de estados do cartão (D3) e os dois cartões de texto;
  - `UsersTable.tsx`: as colunas, a linha marcada (`data-marcada`) e a confirmação do cancelamento na linha;
  - `usersTexts.ts`: o selo do estado com o vencimento, o selo da verificação, os chips e os dois avisos azuis.

  Os selos de valor desconhecido usam o `outline` neutro, com `data-tom="neutral"`. O fundo dos painéis é o `--mantine-primary-color-light` (o `#f5f8fe` do quadro), que existe nos dois esquemas de cor.
- [x] 4.2 (apps/frontend) `UsersPage.test.tsx`, com os cenários dos três requisitos e o duplo `features/users/test/fakeUsersApi.ts` (o `fetch` falso por caminho, no molde do `fakeAccountApi`):
  - **(guarda)** Defeito: o vencido calculado pelo `expiraEm` contra o relógio do navegador. Reprova "o vencido vem do servidor".
  - **(guarda)** Defeito: um estado desconhecido com o selo verde. Reprova "estado desconhecido sai neutro".
  - **(guarda)** Defeito: a lista ordenada por nome no cliente. Reprova "a ordem é a da resposta".
  - **(guarda)** Defeito: um `Link` "Editar" para `/users/{id}` nas linhas ativas. Reprova "a lista não tem link para o detalhe".
  - **(guarda)** Defeito: o último acesso em "há N dias". Reprova "usuário ativo com a verificação ligada", que espera "ontem, 18:40".

  **Feito:** `UsersPage.test.tsx` com 11 casos de lista e carga. O harness é `test/renderUsers.tsx` (QueryClient sem `retry`, router com `/inventory` para "sair da tela"), um duplo novo. **As datas esperadas saem do próprio `formatRelative` e do `formatShortDateTime`**, e não de um texto fixo: a suíte roda no fuso da máquina (como em Minha conta). O caso do último acesso também confere a forma por regex, que é o que pega "há N dias".
  **Vistos reprovar** (`250-guardas/`):
  - g4.2-vencido-cliente (o vencido por `expiraEm <= Date.now()`) → `o vencido vem do servidor`. Voltou: `usersTexts.ts` (sha igual: `27efa24f…`);
  - g4.2-desconhecido-verde (o `default` com `tom: 'green'`) → `estado desconhecido sai neutro`. Voltou: idem;
  - g4.2-ordem-cliente (a lista ordenada por `nome` na tabela) → `a ordem é a da resposta` (`expected 'Ana Souza…' to contain 'Bruno Teixeira'`). Voltou: `UsersTable.tsx` (sha igual: `089c193b…`);
  - g4.2-editar-link (um `Link` "Editar" para `/users/{id}` nas linhas que não são convite) → `a lista não tem link para o detalhe (/users/:id chega com a #183)` (`expected [ '/users/u-ana', '/users/u-bruno' ] to deeply equal []`). Voltou: idem;
  - g4.2-ha-n-dias (o último acesso como "há N dias") → `usuário ativo com a verificação ligada`. Voltou: idem.
## 5. A senha de quem chama e o convite (apps/frontend, D3, D4)

- [x] 5.1 (apps/frontend) `CallerPasswordField.tsx`: um `PasswordField` com `autocomplete="current-password"`, a ajuda por prop e o erro do campo.
  **Feito:** um `PasswordField` com `autocomplete="current-password"`, a ajuda por prop e o erro. O envio fica num hook à parte, `features/users/callerPassword.ts` (`useCallerPassword`), fora do arquivo do componente por causa do `react-refresh/only-export-components`. Ele faz a validação local da senha vazia, o `401` no campo (a senha apagada), e o `423`, o `429`, o `503` e a rede no aviso, com o `useBlockedUntil`. É um arquivo a mais do que o D7 lista, e entra na árvore do fechamento.
- [x] 5.2 (apps/frontend) O cartão como máquina de estados (D3), com um painel por vez e "Convidar usuário" desabilitado com um painel aberto. `InviteForm.tsx`, pelos requisitos "Convidar usuário" e "Senha de quem chama":
  - a validação local da senha vazia;
  - o `400` e o `409` nos campos;
  - o `401` no campo de senha;
  - o `423` com o `lockedText` e o `useBlockedUntil`;
  - o `failureNotice` para `429`, `503` e rede;
  - a linha nova marcada pelo `usuario.id` enquanto o painel está aberto (Q8).
  **Feito:** o `Painel` do `UsersPage` (`nenhum`, `convidar`, `renovar`, `link`) mais o `cancelando` da linha. "Convidar usuário" fica desabilitado fora de `nenhum` e com uma confirmação aberta. `InviteForm.tsx` trata o `409` no E-mail e o `400` pelos `errors` (`nome`, `email`) por meio do `outro` do hook. As ações da linha **não** ficam desabilitadas com um painel aberto, porque é por elas que se abre outro painel (cenário "Abrir outro painel descarta o link").
- [x] 5.3 (apps/frontend) `InviteForm.test.tsx`, com os cenários de "Convidar usuário", "Senha de quem chama" e "Um painel por vez no cartão Usuários". O caso do `401` usa o `sessionFetch` real (o `createSessionFetch` com o runtime falso de `test/authFakes.ts`). Ele confere que a rota foi chamada uma vez, que nenhuma renovação aconteceu e que o registro da sessão continua gravado.
  - **(guarda)** Defeito: a senha vazia enviada (sem a validação local). Reprova "senha vazia não é enviada".
  - **(guarda)** Defeito: o desvio do `401` com problem+json tirado do `sessionFetch`. Reprova "senha errada fica no campo, sem deslogar".
  - **(guarda)** Defeito: o `423` sem desabilitar o botão. Reprova "conta pausada".
  - **(guarda)** Defeito: o `409` mostrado no aviso do formulário, e não no campo E-mail. Reprova "e-mail em uso".
  - **(guarda)** Defeito: a lista relida e reordenada com a linha nova no topo. Reprova "a linha nova fica na ordem do servidor".

  **Feito:** `InviteForm.test.tsx`, 9 casos. O `401` passa pelo `sessionFetch` real (o `fakeUsersApi` fica por baixo dele). O caso confere que:
  - a rota foi chamada uma vez;
  - nenhum pedido foi a `/connect/`;
  - o `sessionStore.read()` continua gravado;
  - o `assign` nunca foi chamado;
  - o formulário continua aberto, com o campo vazio.

  **Vistos reprovar** (`250-guardas/`):
  - g5.3-vazia-enviada (a checagem da senha vazia desligada) → `senha vazia não é enviada`. Voltou: `callerPassword.ts` (sha igual: `4192ef6a…`);
  - g5.3-sessionfetch (`isRouteRefusal` sempre `false` no `auth/sessionFetch.ts`) → `senha errada fica no campo, sem deslogar` (o `401` renovou e saiu para o `/entrar`, e "Senha inválida." não apareceu). Voltou: `auth/sessionFetch.ts` (sha igual: `d61acd47…`);
  - g5.3-423-botao (o `setBloqueadoAte` tirado) → `conta pausada` (`Received element is not disabled`). Voltou: `callerPassword.ts` (mesmo sha);
  - g5.3-409-no-aviso (o ramo do `409` desligado, e o texto no aviso geral) → `e-mail em uso` (o `aria-invalid` ausente). Voltou: `InviteForm.tsx` (sha igual: `1089a919…`). **Na mesma rodada, `senha vazia não é enviada` caiu por `Test timed out in 15000ms`**, com o CPU_Speed_Limit em 23 e load 7,3. Esse caso não depende do trecho mudado, e a reprovação que conta é a do caso guardado, pela asserção dele;
  - g5.3-linha-no-topo (a linha marcada ordenada para o topo na tabela) → `a linha nova fica na ordem do servidor` (`[ 'Marcos Lima', 'Joana', 'Paulo' ]`). Voltou: `UsersTable.tsx` (sha igual: `089c193b…`).
## 6. O link uma vez, só na memória (apps/frontend, D5)

- [x] 6.1 (apps/frontend) `InviteLinkPanel.tsx`: o requisito "Link de convite mostrado uma vez e só na memória da tela".
  - O `CopyButton` copia o `link` como veio.
  - A data sai do `expiraEm` (`dd/mm/aaaa às HH:MM`).
  - As chamadas de convidar e de gerar novo link são diretas, sem `useMutation`. O resultado vai para o estado do cartão, e a lista é relida pelo `invalidateQueries`.
  **Feito:** `InviteLinkPanel.tsx` (`role="status"`):
  - o título e o e-mail;
  - o link num `Code` com "Copiar link" (o `CopyButton` copia o `link` como veio, e mostra "Copiado ✓");
  - "Vale até {expiraEm}", pelo `formatLinkExpiry` da #245, reusado de `features/auth/linkExpiry.ts`;
  - o aviso amarelo e "Concluir".

  O link vem só por prop do estado `painel` do `UsersPage`. Convidar e gerar novo link são chamadas diretas, e a lista é relida pelo `invalidateQueries`.
- [x] 6.2 (apps/frontend) `InviteLinkPanel.test.tsx`, com os cinco cenários do requisito e um duplo do `navigator.clipboard`:
  - **(guarda)** Defeito: o convite por `useMutation`. Reprova "nenhum cache guarda o link".
  - **(guarda)** Defeito: a lista preenchida pelo `setQueryData` com a resposta do convite. Reprova "nenhum cache guarda o link".
  - **(guarda)** Defeito: o link gravado no `sessionStorage` "para não se perder". Reprova "nada em storage nem na URL".
  - **(guarda)** Defeito: o link guardado num `useRef` do nível da página, que sobrevive ao "Concluir". Reprova "Concluir apaga o link".
  - **(guarda)** Defeito: o link copiado sem o fragmento (`link.split('#')[0]`). Reprova "Copiar link".

  **Feito:** `InviteLinkPanel.test.tsx`, 7 casos: os cinco do requisito, "mostra o link, a validade e o aviso…" e "abrir outro painel descarta o link". **Duplo:** o `userEvent.setup()` já instala a área de transferência falsa do user-event, e o caso lê com `navigator.clipboard.readText()`. Um stub próprio do `navigator.clipboard` seria substituído pelo do user-event.
  **Correção de guarda no lugar errado (convenção 15, segunda forma; só acrescenta asserção):** a primeira versão de "nada em storage nem na URL" lia o storage com `Object.keys`. O `MemoryStorage` da suíte (`test/setup.ts`) não expõe as chaves como propriedades, a lista saía vazia, e o caso **passou** contra o defeito (`g6.2-sessionstorage`, `rc=0`). O caso passou a percorrer `length` e `key(i)`, e a afirmar que enxerga o `buteco.sessao`, prova de que a varredura vê o storage.
  **Vistos reprovar** (`250-guardas/`):
  - g6.2-usemutation (o convite por `useMutation`/`mutateAsync`) → `nenhum cache guarda o link` (o `MutationCache` com o `data` do link). Voltou: `InviteForm.tsx` (sha igual: `1089a919…`);
  - g6.2-setquerydata (`setQueryData([...USERS_QUERY_KEY, 'convite'], result)`) → `nenhum cache guarda o link`. Voltou: `UsersPage.tsx` (sha igual: `7c431598…`);
  - g6.2-sessionstorage-2 (`sessionStorage.setItem('buteco.convite', result.link)`), refeito depois da correção → `nada em storage nem na URL` (`expected 'buteco.convite=https://…' not to contain …`). Voltou: idem;
  - g6.2-concluir ("Concluir" que só limpa o aviso, sem descartar o link) → `Concluir apaga o link`. Voltou: idem;
  - g6.2-copia-sem-fragmento (`value={link.split('#')[0]}`) → `Copiar link` (`Received: "https://buteco.exemplo.test/convite"`). Voltou: `InviteLinkPanel.tsx` (sha igual: `1489b2a0…`).

  **O defeito do `useRef` do tasks.md foi trocado** pelo "Concluir que não descarta": o `useRef` precisaria de uma estrutura que o código não tem, e o defeito real que o caso pega é o link que sobrevive ao "Concluir".
## 7. Gerar novo link (apps/frontend, D3, Q7)

- [x] 7.1 (apps/frontend) `RenewInviteForm.tsx`, pelo requisito "Gerar novo link de convite":
  - o `CallerPasswordField` com a ajuda "Para gerar um link, confirme que é você.";
  - o `200` vai ao `InviteLinkPanel`;
  - o `409` fecha o formulário, relê a lista e mostra o aviso azul com o texto ajustado;
  - o `404` relê a lista, sem aviso.
  **Feito:** `RenewInviteForm.tsx` com o `useCallerPassword`. O `409` chama `onGone('aceito')` e o `404`, `onGone('sumiu')`. O `UsersPage` fecha o painel, relê a lista e, no `aceito`, mostra o `conviteAceitoAoRenovar`. O `200` abre o painel do link com "Novo link gerado para {nome}" e o e-mail da linha.
- [x] 7.2 (apps/frontend) `RenewInviteForm.test.tsx`, com os cenários do requisito e os de "Senha de quem chama" na rota do novo link (vazio, `401`, `423`):
  - **(guarda)** Defeito: o `409` tratado como falha genérica, no aviso do formulário. Reprova "convite aceito no meio".
  - **(guarda)** Defeito: o `404` sem reler a lista. Reprova "convite cancelado no meio".

  **Feito:** `RenewInviteForm.test.tsx`, 7 casos: os três do requisito, o formulário com o texto do quadro, e a senha vazia, o `401` (sem deslogar, `assign` nunca chamado) e o `423` na rota do novo link.
  **Vistos reprovar** (`250-guardas/`):
  - g7.2-409-generico (o ramo do `409` desligado, e o aviso de falha no formulário) → `convite aceito no meio`. Voltou: `RenewInviteForm.tsx` (sha igual: `3b15e1b9…`);
  - g7.2-404-sem-reler (o `reler` só no `aceito`) → `convite cancelado no meio` (`expected [ { method: 'GET', … } ] to have a length of 2 but got 1`). Voltou: `UsersPage.tsx` (sha igual: `7c431598…`).
## 8. Cancelar convite (apps/frontend, D3)

- [x] 8.1 (apps/frontend) A confirmação na linha (`UsersTable.tsx`), pelo requisito "Cancelar convite com confirmação na linha":
  - o `204` relê a lista;
  - o `409` relê a lista e mostra o aviso azul do quadro;
  - o `404` relê a lista, sem aviso;
  - `429`, `503` e rede mostram o aviso na linha, com a confirmação aberta.
  **Feito:** na confirmação da linha (`UsersTable.tsx`), o `confirmarCancelamento` do `UsersPage`:
  - o `204` relê a lista;
  - o `409` mostra o aviso azul e relê;
  - o `404` relê sem aviso;
  - o resto deixa o `failureNotice` na linha, com a confirmação aberta.

  A chamada é direta, sem `useMutation`, como as outras duas.
- [x] 8.2 (apps/frontend) Os cenários do requisito em `UsersPage.test.tsx`:
  - **(guarda)** Defeito: "Cancelar convite" chamando a rota no primeiro clique. Reprova "o primeiro clique só pede confirmação".
  - **(guarda)** Defeito: o `409` sem reler a lista. Reprova "convite já aceito".

  **Feito:** 6 casos em `UsersPage.test.tsx`, no `describe('cancelar convite')`: os três cenários do requisito, mais "Voltar desfaz a confirmação sem chamar a rota", "o 404 relê a lista sem aviso" e "falha passageira fica na linha, com a confirmação aberta".
  **Vistos reprovar** (`250-guardas/`):
  - g8.2-sem-confirmacao ("Cancelar convite" chamando direto o `onConfirmCancel`) → `o primeiro clique só pede confirmação` (`expected [ { method: 'POST', … } ] to have a length of +0 but got 1`), e também `Voltar desfaz…`, `convite cancelado`, `convite já aceito` e `o 404…`. Voltou: `UsersTable.tsx` (sha igual: `089c193b…`);
  - g8.2-409-sem-reler (um `return` depois do aviso do `409`) → `convite já aceito` (`expected [ { method: 'GET', … } ] to have a length of 2 but got 1`). Voltou: `UsersPage.tsx` (sha igual: `7c431598…`).
## 9. Identidade (apps/frontend, D6, Q9, Q10)

- [x] 9.1 (apps/frontend) `features/identity/api/identityApi.ts`: a cópia do `request<T>` e do `ApiError`, o `getIdentity()` e os tipos. `identityTexts.ts`: `formatDuration(segundos)` e `nextStepText(chave)`. `identityTexts.test.ts`, com os cenários de "Formato das durações" e as três frases do Próximo passo:
  - **(guarda)** Defeito: o `formatDuration` arredondando para a maior unidade (`Math.round`), em vez de exigir divisão exata. Reprova "sem unidade arredondada".
  **Feito:**
  - `identityApi.ts`: a cópia do `request<T>` e do `ApiError`, o `getIdentity()`, os tipos e o `IDENTITY_QUERY_KEY`;
  - `identityTexts.ts`: o `formatDuration` (divisão exata: dia, h, min, s), o `keyBadge` (desconhecido neutro) e o `nextStepText`;
  - `identityTexts.test.ts`: 5 casos, com os valores de hoje do emissor e as três frases no fuso de São Paulo.

  **Visto reprovar** (`250-guardas/g9.1-arredonda`): as horas por `Math.round` sempre que o valor passa de 1 h → `sem unidade arredondada` (`expected '2 h' to be '90 min'`). Voltou: `identityTexts.ts` (sha igual: `9619b9f4…`).
- [x] 9.2 (apps/frontend) `IdentityPage.tsx`, `IssuerCard.tsx` e `SigningKeysCard.tsx`, pelos requisitos "Página Identidade em /identity", "Cartão Emissor do Buteco" e "Cartão Chaves de assinatura". O texto da validade é "{duração} para usuários e módulos" (Q10).
  **Feito:**
  - `IssuerCard.tsx`: o `SectionedCard` com o selo "Operando", uma `dl` de seis linhas com os endereços monoespaçados, e "… para usuários e módulos" (Q10);
  - `SigningKeysCard.tsx`: a faixa com a duração da troca, o texto do quadro com as três durações da rota, e a tabela na ordem da resposta;
  - `IdentityPage.tsx`: o título e o subtítulo `m1`, o `Skeleton` do carregando, os dois cartões no `200`, e o cartão "Indisponível" (`role="alert"`) em erro, com "Tentar de novo".
- [x] 9.3 (apps/frontend) `IdentityPage.test.tsx`, com os cenários dos três requisitos:
  - **(guarda)** Defeito: "90 dias" escrito à mão na faixa das chaves. Reprova "a troca segue a rota" (30 dias).
  - **(guarda)** Defeito: as chaves ordenadas por estado na tela. Reprova "a ordem é a da resposta".
  - **(guarda)** Defeito: um estado de chave desconhecido com o selo de "Em uso". Reprova "estado desconhecido".
  - **(guarda)** Defeito: a validade com o texto do quadro `completa` ("para usuários, aplicações e módulos"). Reprova "o emissor com os valores de hoje".
  **Feito:** `IdentityPage.test.tsx`, 15 casos: os cenários dos três requisitos, mais "carregando", "Tentar de novo chama na hora" e os dois de polling da 9.4. As datas esperadas saem do `formatDate`, pelo mesmo motivo da 4.2 (o fuso da máquina).
  **Vistos reprovar** (`250-guardas/`):
  - g9.3-90-dias-a-mao ("Troca automática a cada 90 dias" escrito no JSX) → `a troca segue a rota`. Voltou: `SigningKeysCard.tsx` (sha igual: `a4fac445…`);
  - g9.3-ordena (a chave em uso puxada para o topo) → `a ordem é a da resposta`. Voltou: idem;
  - g9.3-desconhecido-verde (o `default` do `keyBadge` com `tom: 'green'`) → `estado desconhecido`. Voltou: `identityTexts.ts` (sha igual: `9619b9f4…`);
  - g9.3-validade-completa (o texto do quadro `completa`) → `o emissor com os valores de hoje` e `a validade segue a rota`. Voltou: `IssuerCard.tsx` (sha igual: `5e5c0b9c…`).
- [x] 9.4 (apps/frontend) `retryWhileFailing.ts`: a função pura do intervalo (requisito "Identidade indisponível e nova tentativa automática"), com o `Retry-After` preso entre `MIN_RETRY_AFTER_SECONDS` e `MAX_RETRY_AFTER_SECONDS`. Ela é usada como `refetchInterval` do `useQuery` da página, e o cartão de erro tem "Tentar de novo". O guarda é **pareado** (convenção 20, quinta forma da 15):
  - `retryWhileFailing.test.ts`: a função resolvida contra a consulta real do cache, em erro (`503` com e sem `retryAfter`, rede) e com dado;
  - `IdentityPage.test.tsx`: o comportamental com timers falsos, "tenta de novo sozinha e para no 200".

  **(guarda)** Defeito: o intervalo constante de 30 s, também com dado. Reprova "sem erro, sem intervalo" e o comportamental ("nenhuma chamada nova acontece com o passar do tempo").
  **(guarda)** Defeito: o `Retry-After` ignorado (30 s fixo no `503`). Reprova "usa o Retry-After do 503".

  **Feito:** `retryWhileFailing.ts`, função pura exportada e usada como `refetchInterval` da consulta. O `retry` continua o do QueryClient do painel. O guarda é pareado:
  - **determinística**, em `retryWhileFailing.test.ts`: 4 casos contra a consulta **real** do cache (`QueryClient.fetchQuery` e `getQueryCache().find`): o `503` com `Retry-After` 5 (5 000), preso em 30 e 1 s, o `503` sem o cabeçalho e a rede (30 000), e com dado (`false`);
  - **comportamental**, em `IdentityPage.test.tsx`, com timers falsos: "tenta de novo sozinha e para no 200" (a rede, depois 30 s e o `200`, depois 120 s sem chamada nova) e "sair da página para o polling".

  **Vistos reprovar** (`250-guardas/`):
  - g9.4-constante (sem a condição do erro: 30 s sempre) → `sem erro, sem intervalo` (`expected 30000 to be false`) e `tenta de novo sozinha e para no 200` (`expected […] to have a length of 2 but got 6`): as duas metades do par. Voltou: `retryWhileFailing.ts` (sha igual: `a090caca…`);
  - g9.4-ignora-retry-after (o ramo do `503` desligado) → `usa o Retry-After do 503` (`expected 30000 to be 5000`) e `o Retry-After fica entre 1 e 30 s`. Voltou: idem.
## 10. Suítes e conferências

- [x] 10.1 (apps/frontend) `npm run lint` e `npm run build`, sem erro. A diferença do bundle contra a baseline é medida nos dois worktrees, minificado e gzip.
  **Feito (07/11/2026):** `npm run lint` sem erro nem aviso. `npm run build` (com o `tsc -b`) passa nos dois worktrees; o aviso de chunk acima de 500 kB já existia.
  **Bundle:**

  | | `a852946` (baseline) | change | diferença |
  |---|---|---|---|
  | `index-*.js` minificado | 1.135,49 kB | 1.155,22 kB | **+19,73 kB** |
  | gzip (Vite) | 330,63 kB | 336,09 kB | **+5,46 kB** |

  Sem dependência nova: é só o código das duas features.
- [x] 10.2 (apps/frontend) Suíte `frontend` completa, pelo `suite-250.sh`, com o relatório por nome comparado ao `nomes.txt` da baseline:
  - nenhum caso da baseline sumiu, mudou de nome ou deixou de passar. Os do `accountDates.test.ts` reaparecem, com o mesmo nome, no `panelDates.test.ts`;
  - os novos estão listados por arquivo;
  - o veredito segue a regra assimétrica.
  **Em aberto (07/11/2026): duas rodadas, as duas INCONCLUSIVAS** pela regra assimétrica.
  - **Rodada 1** (`~/.cache/buteco-agents/250-frontend-20261107/`): começou com CPU 100 e load 4,06; levou 616 s; 2209 de 2248, com 39 reprovações em 9 arquivos, 23 delas `timed out`, todas fora da change (agentes, canais, conhecimento, MCP, inventário). A CPU caiu até 23.
  - **Rodada 2** (`250-frontend-20261107-r2/`): começou com CPU 100 e load 3,12; levou 606 s; 2216 de 2248, com 32 reprovações em 7 arquivos, 18 delas `timed out`. Fora da change, exceto uma: `InviteLinkPanel > nenhum cache guarda o link`, por timeout (15,6 s contra 15 s), e não por asserção. A CPU caiu até 23.
  - **Por nome, nas duas rodadas:** nenhum caso da baseline sumiu ou mudou de nome; 82 casos novos (`novos.txt`): `UsersPage` 17, `IdentityPage` 15, `InviteForm` 9, `InviteLinkPanel` 7, `RenewInviteForm` 7, `usersApi` 6, `failureNotice` 5, `identityTexts` 5, `AppShell` 4, `retryWhileFailing` 4, `router` 2, `panelDates` 1.
  - **A causa da carga, achada entre as rodadas:** a VM do Podman a ~450% de CPU com a suíte parada. Dentro dela, um `beam.smp` recém-criado a cada poucos segundos. É o healthcheck do `buteco-agents_rabbitmq_1` (`rabbitmq-diagnostics -q ping`, a cada 5 s, timeout de 5 s), que sobe uma VM Erlang inteira por execução. Com a máquina carregada, ele estoura o timeout (`podman healthcheck run` → "exceeded timeout of 5s"), o contêiner fica `unhealthy`, e as execuções se sobrepõem. A baseline, às 12:59, levou 207 s. Nada no stack do compose foi mexido: é decisão do dono.
  **Feito (07/11/2026), com o RabbitMQ pausado durante a suíte (decisão do dono):**
  - **o `suite-250.sh`** passou a fazer `podman pause buteco-agents_rabbitmq_1` antes da espera por CPU e load, e `unpause` no fim, também em falha ou interrupção (`trap`), com as duas linhas no `-estado.txt`. Conferido com uma interrupção de verdade: o unpause veio pelo `trap`, e o RabbitMQ voltou a `running`. O zsh só trata o sinal quando o vitest sai, o que ficou registrado no cabeçalho do script;
  - **as rodadas, alternadas** pelo `~/.cache/buteco-agents/bin/alterna-250.sh`, em `~/.cache/buteco-agents/250-alterna-20261107-1815/`:

    | rodada | worktree | resultado | duração | início | CPU mínima | veredito |
    |---|---|---|---|---|---|---|
    | `base-1` | baseline `a852946`, 0 alterados | 2166/2166 em 168 arquivos | 147 s | 18:19:22, CPU 100 | 32 | **APROVADA** |
    | `change-1` | change, 21 alterados | 2248/2248 em 177 arquivos | 160 s | 18:28:52, CPU 100 | 39 | **APROVADA** |

  - **por nome** (`base-1/nomes.txt` contra `change-1/nomes.txt`): nenhum caso da baseline sumiu, mudou de nome ou deixou de passar. Os três de `accountDates.test.ts` estão, com o mesmo nome completo, em `src/utils/panelDates.test.ts`. O `nomes.txt` da `base-1` é **idêntico** ao da baseline das 12:59. Os 82 novos estão em `250-alterna-20261107-1815/novos.txt`:

    | arquivo | casos |
    |---|---|
    | `UsersPage` | 17 |
    | `IdentityPage` | 15 |
    | `InviteForm` | 9 |
    | `InviteLinkPanel` | 7 |
    | `RenewInviteForm` | 7 |
    | `usersApi` | 6 |
    | `failureNotice` | 5 |
    | `identityTexts` | 5 |
    | `AppShell` | 4 |
    | `retryWhileFailing` | 4 |
    | `router` | 2 |
    | `panelDates` | 1 |

  - **o achado vira a #251** (convenção 23): o healthcheck do RabbitMQ, igual nos dois composes, custa ~1 núcleo ocioso e acumula sob carga.
  **Depois do rebase sobre o `main` com a #251 (08/11/2026, commit `5ddcc0e` sobre `497105a`):** `~/.cache/buteco-agents/250-pos-rebase-r1/`, com o `suite-250.sh` em `PAUSAR_RABBIT=0` e o broker **de pé**, `healthy`, com o healthcheck TCP da #251 (conferido no `-estado.txt`).
  - **APROVADA na primeira rodada:** 2.248 de 2.248 em 177 arquivos, em 139 s. Começou com load 3,18 e CPU em 100, e a CPU mínima foi 58.
  - **Por nome:** o `nomes.txt` é **idêntico** ao da rodada válida anterior (`250-alterna-20261107-1815/change-1/`). Nenhum caso sumiu, mudou de nome ou deixou de passar.
  - O lint ficou sem erro, e o build gerou o mesmo `index-X6VWxKH7.js` de antes do rebase.
- [x] 10.3 (apps/frontend + apps/api) **Conferência no navegador**, com o `apps/api` local (`TZ=America/Sao_Paulo`) e o Vite:
  - a seção "Acesso" e os dois itens ativos;
  - a lista com um ativo, um desativado, um convite pendente e um vencido;
  - convidar com a senha errada: o erro fica no campo, a sessão continua, e o banco conta **uma** falha;
  - convidar com a senha certa, "Copiar link", e o link colado numa janela anônima, que abre o `/convite`;
  - "Concluir", e o link não volta;
  - gerar novo link, e o link anterior dá "Este link não vale mais";
  - cancelar convite, e o `409` com o convite aceito em outra janela;
  - o bloqueio depois de tentativas seguidas, com o texto e o botão;
  - Identidade com o `api` de pé, e com o `api` parado: "Indisponível", a nova tentativa sozinha e a volta quando o `api` sobe;
  - nos dois esquemas de cor.
  **Feito (07/11/2026)**, com o `apps/api` local (`TZ=America/Sao_Paulo`, `dotnet run --launch-profile http`) e o Vite do worktree, no Chrome. Quem chama é `conta.246@buteco.local`, a conta de validação da #246, que já estava com a sessão aberta; a senha está em `~/.cache/buteco-agents/250-conferencia/credenciais.env`. O helper é `250-conferencia/val250.sh`, cópia do `val246.sh`.
  - **Menu:** a seção "Acesso" com Usuários e Identidade, e o item ativo em cada página.
  - **Lista:** ativos com "Senha", "Ativa" ou "Não configurada", "hoje, 16:49" e `dd/mm/aaaa, HH:MM`; convites pendentes com "vence em dd/mm, HH:MM" e as duas ações; nenhum "Editar".
  - **Senha vazia:** "Informe a sua senha." no campo, e a contagem de falhas no banco continuou sem linha (nenhum pedido saiu).
  - **Senha errada:** "Senha inválida." no campo, e a página continuou em `/users` com o `buteco.sessao` gravado. O banco contou **uma** falha (`consecutive_failures = 1`) para um envio, sinal de que o `sessionFetch` não repetiu o pedido.
  - **Senha certa:** "Convite criado para Pessoa Validação 250", o link e "Vale até 10/11/2026 às 16:51". A linha nova apareceu na ordem do servidor, marcada. Por JS: o token do link não estava no `localStorage`, no `sessionStorage`, no endereço nem no `history.state`.
  - **"Concluir":** o painel fechou, a marcação saiu, e o token não estava mais no `innerHTML`.
  - **Gerar novo link** ("Pessoa Convidada 225333", senha certa): "Novo link gerado…", e a linha passou a "vence em 10/11, 16:53" (a lista relida).
  - **Sair da tela com o link aberto:** de `/users` para Inventário e de volta. Nenhum painel aberto, e o token fora do DOM.
  - **Cancelar convite (`204`):** a confirmação na linha, e a "Pessoa Convidada 225407" saiu da lista.
  - **Convite já aceito (`409`):** o convite da "Pessoa Validação 250" foi aceito pela rota pública (`POST /auth/invitation/accept`, `204`), como em outra janela. "Cancelar convite" na tela mostrou o aviso azul "O convite de Pessoa Validação 250 já foi aceito, então não há o que cancelar. A lista foi atualizada.", e a linha passou a "Ativo", com "Senha".
  - **Identidade com o `api` no ar:** o emissor "Operando", "15 min para usuários e módulos", "14 dias", "90 dias", e **uma** chave, "Em uso" (o momento que o quadro não desenha).
  - **Identidade com o `api` parado:** depois das tentativas do QueryClient, o cartão "Indisponível" com o texto do quadro e "Tentar de novo", sem deslogar.
  - **Identidade com o `api` de volta (16:57:08):** sem nenhum clique, a página voltou aos dois cartões dentro do intervalo de 30 s. Depois do `200`, nenhum pedido a `/identity` em 40 s (`read_network_requests`).
  - **Bloqueio:** com mais 4 senhas erradas (5 falhas no banco, uma por envio), o envio seguinte mostrou o aviso amarelo "…a sua conta fica pausada por alguns minutos.", com `last_lock_minutes = 5` no banco.

  **Não conferido no navegador, e por quê:**
  - **"Copiar link":** na aba controlada por automação, o `navigator.clipboard.writeText` não resolve (o documento não tem foco), e o rótulo não chega a "Copiado ✓". O caso `Copiar link` da suíte cobre o valor copiado, com o guarda do fragmento (g6.2). Fica para a validação visual.
  - **O botão desabilitado no bloqueio:** no desenvolvimento, o `Retry-After` não é legível (pedido cross-origin, sem `Access-Control-Expose-Headers`; #246 D8), então não há até quando desabilitar, como em Minha conta. O caso `conta pausada` da suíte cobre o botão com o cabeçalho legível, que é o caso de produção (mesma origem).
  - **Em escuro:** não feito. Fica para a validação visual (10.4).
  - **O `409` do "Gerar novo link":** não houve um aceite entre abrir o formulário e enviar. Fica coberto pelo `convite aceito no meio` (g7.2).
- [ ] 10.4 **Validação visual quadro a quadro com o dono** (convenção 14):
  - contra o `Usuarios` (`entrega=m1`), em todos os valores de `carga` e `convite`;
  - contra o `Provedores` (`entrega=m1`), nos dois valores de `carga` e de `momento`;
  - contra a barra lateral com a seção "Acesso".

  Os estados sem quadro e as divergências registradas (o "Editar" ausente, Q3, Q8 e Q10) são conferidos como tais. **O archive só depois desta validação.**
  **Validação visual na homologação da M1** (decisão do dono, 08/11/2026). O dono valida quadro a quadro depois que todas as issues da entrega estiverem mergeadas, sem issue própria; o PR #252 sai do rascunho e é mergeado antes dela. Se a homologação achar algo, a correção vira trabalho da própria homologação.
  Antes disso (07/11/2026), a 10.4 estava como "pendente: validação visual do dono", com o PR em rascunho e "não mergear antes da validação do dono".
  A validação cobre:
  - **`Usuarios` (`entrega=m1`), `carga`:** `ok`; `carregando` ("Carregando os usuários…"); `erro` ("Não foi possível carregar os usuários agora." e "Tentar de novo");
  - **`Usuarios`, `convite`:** `lista`; `formulario`; `email-em-uso`; `senha-errada`; `senha-bloqueada`; `convite-aceito` (o aviso azul); `link-gerado` (com a linha marcada); `renovar`; `link-renovado`; `cancelar` (a confirmação na linha);
  - **`Provedores` (`entrega=m1`):** `carga=ok`, `carga=erro`, `momento=depois-da-troca` e `momento=antes-da-troca`;
  - **a barra lateral:** o rótulo "Acesso", com Usuários e Identidade, e o item ativo;
  - **os estados sem quadro:**
    - "Sua senha" vazia ("Informe a sua senha.");
    - o `401` e o `423` em "Gerar novo link";
    - o aviso azul do `409` em "Gerar novo link";
    - o `404` do novo link e do cancelamento (lista relida, sem aviso);
    - `429`, `503` e rede no formulário e na linha do cancelamento;
    - o carregando da Identidade;
    - só a chave em uso;
    - "Copiado ✓";
  - **as divergências, conferidas como registro e não como defeito:**
    - a linha sem "Editar" (temporária, #183);
    - Q3: o último acesso em "hoje, 14:15";
    - Q8: a linha nova na ordem do servidor, marcada;
    - Q10: "15 min para usuários e módulos";
    - o rótulo "Acesso" no tom `dimmed`;
  - **os dois esquemas de cor.**
- [x] 10.5 Medição da convenção 18 **depois** da validação visual: produção, testes e duplos separados, por `git diff --numstat` contra `a852946`, só código, comparados à projeção do design.

  **Feito (07/11/2026)**, por `git diff --numstat a852946` mais as linhas dos arquivos novos, só `apps/frontend/src`, **antes** da validação visual (decisão do dono: o fechamento não espera por ela; se a validação mudar código, a medição se refaz). Os quatro arquivos movidos foram medidos à parte, pelo diff real contra o original:
  - **produção:** +1.291 / −34 em 23 arquivos, contra ~1.010 projetados (**+28%**). Os movidos somam +36 / −6 (o `isApiFailure` e o `formatShortDateTime`);
  - **testes:** +1.222 / −1 em 11 arquivos, contra ~1.180 (**+4%**). O movido soma +6 / −1;
  - **duplos:** +92 em 2 arquivos (`fakeUsersApi`, `renderUsers`), contra ~120 (**−23%**). O `fakeUsersApi` reexporta o `fakeAccountApi`.

  A produção passou na máquina de estados do cartão de Usuários (`UsersPage`, 172 linhas) e no hook `callerPassword.ts`, que não estava projetado. Fica abaixo da âncora (+1.516 na #246), como a Q1 previa.
## 11. Documentação e registros

- [x] 11.1 (docs) `02-HISTORICO_E_STATUS.md` (a entrada da change) e `CHANGELOG.md`, antes do archive.
  **Feito (07/11/2026):**
  - `CHANGELOG.md`: "Usuários e Identidade no painel" em Added, com a validação visual pendente (em 08/11/2026: "na homologação da M1");
  - `02-HISTORICO_E_STATUS.md`: a entrada `usuarios-convites-identidade-painel`, com as decisões, as premissas que caíram, as medições e a validação pendente.
- [x] 11.2 (issues) Comentários de entrega, com o ok do dono:
  - na **#183**, o que ela encontra pronto:
    - `failureNotice`, `useBlockedUntil` e `panelDates` no lugar comum;
    - o `CallerPasswordField` e o `InviteLinkPanel`;
    - a coluna Ações sem "Editar", com o guarda "a lista não tem link para o detalhe", que ela troca pelo caso do "Editar";
  - na **#184**: Usuários e Identidade abrem para todo usuário ativo na M1.

  O PR leva `Closes #250`.
  **Feito (07/11/2026):** [comentário de entrega na #183](https://github.com/aldovrando-oliveira/buteco-agentes/issues/183#issuecomment-6713401861). O [comentário na #184](https://github.com/aldovrando-oliveira/buteco-agentes/issues/184#issuecomment-6713632615) (Usuários e Identidade abertos a todo usuário ativo na M1, e o que a #184 restringe) saiu logo depois, a pedido do dono: o pedido do fechamento citou só a #183 por omissão. Também publicados no fechamento, a pedido do dono:
  - na [#226](https://github.com/aldovrando-oliveira/buteco-agentes/issues/226#issuecomment-6713392834) e na [#176](https://github.com/aldovrando-oliveira/buteco-agentes/issues/176#issuecomment-6713393019), o healthcheck do RabbitMQ como causa nova desde 05/11, com a tabela das 174 rodadas;
  - na [#251](https://github.com/aldovrando-oliveira/buteco-agentes/issues/251#issuecomment-6713394225), o custo em produção e a garantia do `service_healthy`.
