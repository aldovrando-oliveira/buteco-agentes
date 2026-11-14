Todas as tarefas rodam no **`apps/frontend`**, salvo as marcadas (scripts locais, documentação, GitHub).

Um guarda marcado **(guarda)** só conta depois de ter reprovado contra o defeito que ele existe para pegar (convenção 15). Para cada um:

1. o `~/.cache/buteco-agents/bin/guarda-279.sh` reintroduz o defeito nomeado. Ele é cópia do `guarda-190.sh` (o ramo `frontend`), com o mesmo lock das suítes. A mutação toca só o trecho nomeado e nunca restaura um diretório ao `HEAD`;
2. o teste roda e reprova;
3. a árvore volta pela cópia feita antes da mutação, com o sha conferido.

A tarefa registra o nome do teste, a mensagem da reprovação e o "voltou: <arquivo>", com o sha igual ao de antes. Uma mutação que quebra a compilação (o `tsc` do Vitest) e reprova por erro de tipo não vale: é descartada e refeita. As saídas ficam em `~/.cache/buteco-agents/279-guardas/<tarefa>`.

**As suítes** rodam pelo `~/.cache/buteco-agents/bin/suite-279.sh` (`frontend`), uma por vez, com o lock.
- Vale a regra assimétrica: uma rodada verde conta mesmo com a CPU caindo, e uma reprovação só conta com a CPU em 100 do início ao fim; com queda, é inconclusiva e se repete.
- Antes de cada rodada, o load de 1 e de 5 min fica abaixo de 6. Sob limite térmico, a variante `MAXWORKERS=4` (#220), registrada no `-estado.txt`.
- Um caso que **esta change modificou** e que reprova conta como "reprovada pelo <caso> (a → b), corrigida", mesmo com a CPU baixa, e não como inconclusiva.
- Uma intermitente nova do painel vira issue (convenção 23).
- Um build nunca roda junto de uma suíte.

**A baseline** é do `main` em `9ec078c`, no worktree limpo `../buteco-agentes-279-baseline`, com as saídas em `~/.cache/buteco-agents/279/baseline/` (tarefa 0.3).

**Parada** (convenção 9):
- **parar** quando uma premissa falsa mudar comportamento ou contrato, ou quando a correção reduzir cobertura;
- **parar** quando a tela precisar de dado que o backend não serve (convenção 1);
- **parar** quando um caso fora da lista do topo precisar sair, mudar de nome ou afrouxar;
- **seguir e registrar** quando a correção só acrescentar teste ou cenário para pôr um guarda no lugar certo, ou só tocar comentário ou documentação.

**O Prettier** só nos arquivos que já estavam formatados no `main` (a regra da #185): a lista sai da 0.4, e um arquivo fora dela recebe só as edições da change.

**O protótipo**: quadro e estado da **versão 82** (id de referência `1794678488-1a7d`; a 81 era `1794673056-98cb`). Uma leitura noutro id vale se os quatro quadros desta change (`MinhaConta`, `UsuarioDetalhe`, `Usuarios`, `Provedores`) tiverem o mesmo conteúdo.

**Nada de commit** até o archive; um commit só, depois dele, e o PR com `Closes #279` (convenção 24).

**Só a #279.** Nada no `apps/api`; a volta do conectar que cai no `/entrar` é a **#281**, que não bloqueia esta change.

## A lista do topo

Os casos existentes que mudam, e por quê (design, D10). Nenhum sai, nenhum muda de nome, nenhuma asserção afrouxa. Todo outro caso existente fica sem remoção, renome nem afrouxamento.

**Mudam de asserção (os 7 textos da M3):**
- `features/account/pages/AccountPage.test.tsx`, `mostra o título, o subtítulo e os quatro cartões, na ordem`: o subtítulo da M3. A lista dos cartões fica igual, porque o duplo padrão não tem provedor nem conta e o cartão "Formas de entrada" não aparece;
- `features/identity/pages/IdentityPage.test.tsx`, `os dois cartões, sem provedores federados`: o subtítulo da M3; a ausência de "Provedores federados" vira o cartão no estado vazio ("Nenhum provedor federado configurado. O login fica só com e-mail e senha."). O nome fica;
- `features/users/pages/UsersPage.test.tsx`, `título, subtítulo e os dois cartões de texto`: o subtítulo e o `cardSenha` da M3;
- `features/users/components/SignInFormsCard.test.tsx`, `a linha Senha de outro usuário`: o rodapé da M3, e `queryByText('Conectada em')` ausente passa a presente;
- `features/users/components/UserDataCard.test.tsx`, `Salvar desabilitado sem mudança`: a ajuda do e-mail da M3;
- `features/users/components/UserTwoFactorCard.test.tsx`, `verificação ativa` e `com a verificação desligada, o botão fica desabilitado`: um texto só da M3 para os dois estados.

**A lista exata de pedidos ou "nenhum pedido": nenhum caso entra** (conferido em `9ec078c`, design D10). Nos arquivos dos seis formulários e das páginas que os montam, as asserções de pedidos são todas filtradas por rota, método ou prefixo (`/connect`, `PUT`, `GET` de `/users`, `/identity`, `calls('<rota>')`), e o `GET /account` novo não entra em nenhum filtro.

**O arranjo, sem mudança de asserção:** o duplo é um só, o `fakeAccountApi` (o `fakeUsersApi` e o `fakeApplicationsApi` o reexportam), uma **fila por rota** em que a última resposta vale para os pedidos seguintes. Ele passa a ter três rotas por padrão, como já tem o `POST /connect/token`: `GET /account` com uma conta **com senha** e sem contas conectadas; `GET /auth/federated/providers` e `GET /identity/federated-providers` com a **lista vazia**. O caso que declara a rota sobrepõe o padrão. Os casos que passam a ler essas respostas (todos os dos arquivos do `features/users`, do `features/applications`, do `features/identity` e do `features/account` que montam um formulário com a confirmação, o cartão "Formas de entrada" ou o cartão dos provedores) ficam com as mesmas asserções. **A lista vazia é de propósito:** o `IdentityPage.test.tsx` conta as linhas da página inteira (`linhas()`, l. 87) e procura "Tentar de novo" por papel (l. 221–229); um provedor ou uma rota sem resposta mudaria esses casos.

**Mudam de arranjo, sem mudar asserção (achados no apply pelo `tsc`; design, "O que o apply mudou", 1):** as funções de API recebem a confirmação em vez da senha em texto, e a chamada passa de `'senha'` a `{ senha: 'senha' }`, com o mesmo corpo esperado:
- `features/applications/api/applicationsApi.test.ts`: a linha `secrets` do `it.each` de caminhos e corpos, e `o 401 da senha chega com o title, sem renovar a sessão`;
- `features/users/api/usersApi.test.ts`: as linhas `POST /users/u%201/invitation/renew` (de `%s leva o corpo do contrato`) e `POST /users/u%201/password-reset` (de `%s leva o método, o caminho e o corpo do contrato`).

**A conferir no apply, sem mudança prevista:** `UserDataCard.test.tsx`, o caso do rebaixamento de si mesmo (l. 373–374, nenhum `GET` de `/users` depois do `PUT`): ele invalida o `ACCOUNT_QUERY_KEY`, e o `GET /account` que vier não entra no filtro.

Cada caso é listado por nome na comparação da 13.3, com o requisito que o mudou.

## 0. Preparação

- [x] 0.1 (scripts locais) `~/.cache/buteco-agents/bin/suite-279.sh`, cópia do `suite-190.sh` com o cabeçalho da #279, e `~/.cache/buteco-agents/bin/guarda-279.sh`, cópia do `guarda-190.sh`, com o lock das suítes. Conferir o guarda com uma mutação nula (recusa com "defeito não aplicado") e uma real em `apps/frontend/src` (o arquivo volta com o mesmo sha).
- [x] 0.2 (git) O worktree limpo `../buteco-agentes-279-baseline` em `9ec078c`, destacado.
- [x] 0.3 **A baseline**: a suíte `frontend` no worktree da 0.2, pelo `suite-279.sh`, com as saídas e o `nomes.txt` (arquivo e nome) em `~/.cache/buteco-agents/279/baseline/`. Registrar no design ("Baseline") o resultado, a duração, o estado (início → fim) e a CPU mínima.
- [x] 0.4 A lista dos arquivos que a change toca e que estão formatados no `main` (`prettier --check` no worktree da 0.2), registrada no design.
- [x] 0.5 Reler o `callerPassword.ts`, o `CallerPasswordField.tsx`, os seis formulários, o `AccountPage`, o `SignInFormsCard`, o `UserTwoFactorCard`, o `IdentityPage`, o `useLoginProviders` e o `fakeAccountApi`, e registrar no design qualquer diferença do D2 ao D7 (convenção 6).
- [x] 0.6 (protótipo) Ler os quatro quadros e conferir o conteúdo contra os sha256 da versão 81 do design; outro id com o mesmo conteúdo vale, e é registrado.

## 1. A API (`panel-federated-accounts`, `panel-identity-page`; D1)

- [x] 1.1 `features/account/api/accountApi.ts`: `Account` com `formasDeEntrada` e `contasConectadas` (`ConnectedAccount`: `chave`, `tipo`, `email`, `conectadaEm`, `ultimoUso`, `disponivel`); `connectFederated(chave, confirmacao)` (`POST /account/federated/{chave}/connect`, `200 { url }`) e `disconnectOwnFederated(chave)` (`DELETE /account/federated/{chave}`, sem corpo).
- [x] 1.2 `features/users/api/usersApi.ts`: `UserDetail.contasConectadas`; `disconnectUserFederated(id, chave)` (`DELETE /users/{id}/federated/{chave}`, sem corpo); `inviteUser`, `renewInvitation`, `requestPasswordReset` e `changeUserRole` recebem a confirmação (`{ senha } | { codigo }`) e a espalham no corpo.
- [x] 1.3 `features/applications/api/applicationsApi.ts`: `createApplication` e `generateSecret` com a confirmação.
- [x] 1.4 `features/identity/api/identityApi.ts`: `getFederatedProviders()` (`GET /identity/federated-providers`) e o tipo (`chave`, `tipo`, `nome`, `issuer`, `usadoPara`, `clientId`, `uriDeRetorno`, `vinculos`), com a chave de consulta própria `['identity', 'federated-providers']`.
- [x] 1.5 Casos (`accountApi.test.ts`, `usersApi`, `identityApi`): o caminho, o método e o corpo exato de cada pedido; os `DELETE` sem corpo; o corpo com `codigo` sem `senha`; o `401` com problema que não renova (o critério do `sessionFetch`).
- [x] 1.6 O duplo: as três rotas por padrão no `fakeAccountApi` (a lista do topo); `formasDeEntrada` e `contasConectadas` no `ACCOUNT`; `contasConectadas: []` nos `UserDetail` do `fakeUsersApi` (o `tsc` enumera os que faltarem); as contas conectadas de exemplo (disponível e fora da configuração). Contado como duplo.

## 2. A confirmação com o código (`panel-users-page`, "Senha de quem chama"; D2)

- [x] 2.1 `features/users/callerPassword.ts`: o `useCallerPassword` lê o `ACCOUNT_QUERY_KEY` e expõe o `modo` (`'codigo'` só com a conta lida sem `"senha"`; `'senha'` pendente ou em erro); o `enviar` passa à função a confirmação `{ senha }` ou `{ codigo }`, só o campo do modo; o vazio do modo ("Informe a sua senha." ou "Informe o código."); no modo `'senha'`, o `401` "Código inválido." relê a conta. O valor sai no sucesso, no `401` e ao desmontar, como hoje.
- [x] 2.2 `features/users/components/CallerPasswordField.tsx`: o campo por `modo` (o rótulo, o `type`, o `inputmode`, o `autocomplete`, a ajuda do código no modo código), com o rótulo, a ajuda e o erro de `MinhaConta/confirmar-codigo`.
- [x] 2.3 Os seis formulários passam o `modo` ao campo e a confirmação à API: `InviteForm.tsx`, `RenewInviteForm.tsx`, `SignInFormsCard.tsx` (`ResetPasswordForm`), `UserDataCard.tsx` (com a frase da promoção no modo código e o `motivo()` pelo `title`), `ApplicationCreatePage.tsx` e `ApplicationCredentialTab.tsx`.
- [x] 2.4 Casos (`callerPassword.test.tsx`): o modo pela conta (com senha, sem senha, pendente, em erro); o campo no modo código (rótulo, `autocomplete="one-time-code"`, a ajuda); o vazio do código; o `401` "Código inválido." no campo; e, **para cada um dos seis formulários**, o envio no modo código com o corpo exato (o campo `codigo`, sem `senha`).
- [x] 2.5 Caso: com `GET /account` em erro, o `401` "Código inválido." relê a conta, e o campo passa ao código.
- [x] 2.6 **(guarda)** O `401` "Código inválido." no modo senha que não relê a conta, e o campo que não troca. Mutação: tirar a releitura do 2.1. Reprova o caso da 2.5.
- [x] 2.9 **(guarda, correção do dono no fechamento)** A troca de modo chega limpa: o `useCallerPassword` limpa o erro e o valor quando o modo muda; o caso `A troca para o código chega limpa` (o campo do código vazio, sem `aria-invalid`, sem "Código inválido.", com a ajuda do código); o caso da 2.5 sem a asserção do erro. Mutação: tirar a limpeza do erro na troca. Reprova `A troca para o código chega limpa`. A 2.6 rodada de novo contra o código corrigido.
- [x] 2.7 **(guarda)** O modo código mandando `{ senha }` num dos seis formulários. Mutação: o `enviar` monta `{ senha: valor }` no modo código. Reprova o caso de envio do convite da 2.4 (e os outros cinco).
- [x] 2.8 **(guarda)** O modo código mandando os dois campos. Mutação: o `enviar` monta `{ senha: valor, codigo: valor }`. Reprova os casos de corpo exato da 2.4.

## 3. Minha conta, o cartão "Formas de entrada" (`panel-federated-accounts`, "Formas de entrada em Minha conta"; D3)

- [x] 3.1 `features/account/components/AccountSignInFormsCard.tsx`: as consultas (a conta e o `useLoginProviders`); quando o cartão aparece; as quatro linhas; o rótulo pelo mapa de tipos; o tipo desconhecido cru e sem conectar; a contagem da última forma (a senha e as contas `disponivel`); "Sem ação" e o selo no vínculo fora; o rodapé. No `AccountPage`, entre "Dados" e "Senha".
- [x] 3.2 Casos (`AccountSignInFormsCard.test.tsx`): um por cenário do requisito (a senha e o Google; o provedor sem conta; sem provedor e sem conta; a recusa sem conta e sem lista que mantém o cartão; o desconectar da única conta que mantém o cartão com o aviso; a lista falhando; o vínculo fora; uma forma só; o vínculo indisponível que não conta; o tipo desconhecido).
- [x] 3.3 **(guarda)** O vínculo indisponível contando como forma. Mutação: a contagem soma todos os itens de `contasConectadas`. Reprova `O vínculo indisponível não conta como forma`.
- [x] 3.4 **(guarda)** "Desconectar" habilitado com uma forma só. Mutação: o botão ignora a contagem. Reprova `Com uma forma só, Desconectar fica desabilitado`.
- [x] 3.5 **(guarda)** A regra de visibilidade do cartão ignorando o aviso. Mutação: a condição de aparecer só olha a lista e as contas. Reprova `A recusa sem conta e sem lista mantém o cartão` e `Desconectar a única conta mantém o cartão com o aviso`.

## 4. Conectar (`panel-federated-accounts`, "Conectar a conta do provedor"; D3)

- [x] 4.1 `features/account/components/ConnectProviderForm.tsx`: o formulário no cartão, o texto do quadro, o campo do D2 (a ajuda "Para conectar, confirme que é você." no modo senha), "Continuar com Google" e "Cancelar"; o `connectFederated`; "Redirecionando para o Google…" e o `authRuntime.assign(url)`; o `404` pelo `title` exato (com a lista relida) e o outro `404`; o `503`; o `423` pelo `useCallerPassword`; a rede e o resto pelo `failureNotice`.
- [x] 4.2 Casos: um por cenário do requisito, inclusive o corpo exato com a senha e com o código.
- [x] 4.3 **(guarda)** O conectar no modo código mandando `{ senha }`. Mutação: o `ConnectProviderForm` chama `connectFederated(chave, { senha: valor })`. Reprova `O código vai como codigo`.

## 5. A volta do conectar (`panel-federated-accounts`, "A volta do conectar em /account"; D4)

- [x] 5.1 `features/account/federatedReturn.ts` e `accountFederatedTexts.ts`: a leitura de `conectado`, `recusa` e `provedor` na montagem; o aviso por motivo; todo motivo desconhecido como `falha`; o `navigate` por `replace` para `/account` sem os três, com o `state` da navegação; nenhum `POST /auth/federated/refusal`.
- [x] 5.2 Casos: um por cenário do requisito (o conectado; as cinco recusas; a falha sem provedor; o desconhecido; os parâmetros que saem com o `state`; a recarga; sem a rota do aviso).
- [x] 5.3 **(guarda)** Os parâmetros da volta lidos e não limpos. Mutação: tirar o `navigate` de `replace`. Reprova `Os parâmetros saem e o state fica`.
- [x] 5.4 **(guarda)** Os parâmetros limpos sem preservar o `state`. Mutação: o `replace` sem o `state`. Reprova o mesmo caso, pela asserção do `state`. A asserção do endereço continua verde.
- [x] 5.5 **(guarda)** O motivo desconhecido na volta, sem aviso. Mutação: o motivo fora da tabela devolve nenhum aviso. Reprova `Motivo desconhecido sai como falha`.

## 6. Desconectar a própria conta, a pessoa sem senha e o subtítulo (`panel-federated-accounts`, `panel-account-page`; D3)

- [x] 6.1 O desconectar no `AccountSignInFormsCard`: a confirmação na célula de Ações, com a linha em destaque; o `disconnectOwnFederated`; o `204` com "Conta Google desconectada." e a conta relida; o `409` na mesma linha, com "Fechar"; o `404` pelo `title`; nenhuma renovação nem logout.
- [x] 6.2 `AccountPasswordCard.tsx`: a pessoa sem senha (Q10), com o cartão de hoje enquanto a conta está pendente ou em erro. `AccountPage.tsx`: o subtítulo da M3.
- [x] 6.3 Casos: um por cenário de "Desconectar a própria conta do provedor"; `A pessoa sem senha` (`AccountPasswordCard.test.tsx`); `Com provedor, os cinco cartões` (`AccountPage.test.tsx`).
- [x] 6.4 **(guarda)** O cartão Senha da pessoa sem senha com "Trocar senha". Mutação: o cartão ignora `formasDeEntrada`. Reprova `A pessoa sem senha`.

## 7. Editar usuário (`panel-user-detail-page`, `panel-federated-accounts`; D5)

- [x] 7.1 `SignInFormsCard.tsx`: as colunas novas; a linha Senha por alvo (com senha, sem senha com "Gerar link de senha", o próprio, o desativado); as contas conectadas com as regras do cartão de Minha conta; "Desconectar em Minha conta" no próprio; "Sem ação" no vínculo fora; o rodapé da M3. O formulário da redefinição com o título e o texto da pessoa sem senha.
- [x] 7.2 O desconectar pelo Admin: a confirmação na célula de Ações; o `disconnectUserFederated`; o `204` com o aviso, o detalhe relido e a lista de Usuários invalidada; o `409` na célula de Ações da linha, com "Fechar", como em Minha conta; os dois `404` pelo `title`; o resto pelo `failureNotice`.
- [x] 7.3 `UserDataCard.tsx`: a ajuda do e-mail da M3. `UserTwoFactorCard.tsx`: o texto da M3 nos dois estados.
- [x] 7.4 Casos: um por cenário novo de "Formas de entrada" e "Redefinir senha com a senha de quem chama" (`SignInFormsCard.test.tsx`), de "Desconectar pelo Admin em Editar usuário", e `A ajuda do e-mail da M3` e `A promoção com o código` (`UserDataCard.test.tsx`).
- [x] 7.5 **(guarda)** O Admin vendo "Desconectar" na própria linha em vez do link para Minha conta. Mutação: a linha da conta ignora o `proprio`. Reprova `O próprio desconecta em Minha conta`.

## 8. A sessão de quem o Admin desconectou (`panel-federated-accounts`; D6)

- [x] 8.1 Caso, **sem guarda** (decisão do dono): com a sessão de teste gravada, a renovação responde `400` `invalid_grant`, e o painel apaga o registro e vai a `/entrar?motivo=sessao-expirada` (o arranjo de `auth/renewal.test.ts`). Nenhuma mudança no código da sessão.

## 9. Usuários (`panel-users-page`; D7)

- [x] 9.1 `usersTexts.ts`: o chip "Google", conhecido, no tom da "Senha"; outro tipo cru, em contorno. `UsersPage.tsx`: o subtítulo e os dois cartões da M3.
- [x] 9.2 Casos: `O chip Google no tom da Senha` e `Outro tipo de provedor continua neutro`.

## 10. Identidade (`panel-identity-page`; D7)

- [x] 10.1 `features/identity/components/FederatedProvidersCard.tsx`: a consulta própria; a tabela (Provedor com o `nome` e a `chave`, Usado para, Issuer, ID do cliente, URI de retorno, Vínculos); "1 usuário" e "`<n>` usuários"; os estados carregando, vazio e erro com "Tentar de novo", sem polling; o `403` pela regra da página; o rodapé; nada de "Adicionar provedor", "Editar" ou "Chaves". No `IdentityPage`, abaixo das chaves, fora do ramo do `GET /identity`, e o subtítulo da M3.
- [x] 10.2 Casos (`FederatedProvidersCard.test.tsx` e `IdentityPage.test.tsx`): um por cenário de "Cartão Provedores federados".
- [x] 10.3 **(guarda)** A falha da consulta dos provedores derrubando o cartão do emissor. Mutação: o `IdentityPage` mostra o aviso de erro da página quando qualquer das duas consultas falha. Reprova `O erro dos provedores não derruba o emissor`.
- [x] 10.4 **(guarda)** "Vínculos" com o plural errado no 1. Mutação: sempre "`<n>` usuários". Reprova `Um vínculo no singular`.

## 11. A lista do topo

- [x] 11.1 Os 7 casos de texto, com as asserções novas, e nenhum outro.
- [x] 11.2 A conferência do caso do rebaixamento de si mesmo no `UserDataCard.test.tsx` (l. 373–374): passa sem mudança; se não passar, parada.

## 12. Lint, build e tamanho

- [x] 12.1 `npm run lint` sem aviso novo; `npm run build` com o mesmo aviso de chunk da baseline (o tamanho registrado), sem suíte rodando junto.
- [x] 12.2 O diff com e sem `-w` nos arquivos fora da lista da 0.4: só as edições da change.
- [x] 12.3 A convenção 18, medida (só `apps/frontend/src`, sem `openspec/` e `.md`): produção, testes sem duplos, duplos e casos, contra o D10, com a causa de cada desvio.

## 13. Fechamento

- [x] 13.1 A suíte `frontend` pelo `suite-279.sh`, com o veredito assimétrico, as saídas em `~/.cache/buteco-agents/279/fechamento/`.
- [x] 13.2 Os guardas pelo `guarda-279.sh`, com o "voltou" e o sha de cada arquivo (`~/.cache/buteco-agents/279-guardas/`).
- [x] 13.3 A comparação por arquivo e nome contra a baseline (o JSON do Vitest): nenhum caso some nem muda de nome; os casos da lista do topo listados por nome, com o requisito que os mudou; cada caso novo ligado ao requisito (o `describe` com o nome dele).
- [x] 13.4 O design com "O que o apply mudou" (convenção 9), se houver.
- [x] 13.5 (docs) A entrada da change no `02-HISTORICO_E_STATUS.md` e no `CHANGELOG.md`, e o que o `01-ARQUITETURA_E_CONVENCOES.md` precisar (a seção de Autenticação, as contas conectadas no painel), **antes** do archive.
- [x] 13.6 (openspec) O archive com o sync das specs (`panel-federated-accounts` nova; `panel-account-page`, `panel-users-page`, `panel-user-detail-page`, `panel-external-applications` e `panel-identity-page` modificadas).
- [ ] 13.7 (git, com a autorização do dono na mensagem) Um commit só, depois do archive; o push; o PR com `Closes #279`, o checklist da 14 e a #281 citada; a #279 em In review.

## 14. Homologação da M3 (não prende o PR)

A validação é quadro e estado contra a **versão 82** (id de referência `1794678488-1a7d`), depois do merge. Achado vira issue nova ligada à #196 (convenção 23); a change não é reaberta.

**Validação visual:**
- [ ] 14.1 **`MinhaConta` (`entrega=m3`), prop `formas`:** `lista`; `sem-conta`; `confirmar-senha`; `confirmar-senha-errada`; `confirmar-codigo`; `confirmar-codigo-errado`; `provedor-indisponivel`; `provedor-nao-configurado`; `redirecionando`; `conectado`; `recusa-em-uso`; `recusa-ja-conectado`; `recusa-sessao-expirada`; `recusa-dominio-nao-permitido`; `recusa-falha`; `confirmar-desconectar`; `desconectar-409`; `desconectado`; `fora-da-configuracao`; `sem-senha`.
- [ ] 14.2 **`UsuarioDetalhe` (`entrega=m3`), prop `formas`:** `senha-e-google`; `so-senha`; `so-google`; `fora-da-configuracao`; `confirmar-desconectar`; `desconectar-409`; `desconectado`; e `alvo=proprio` (a verificação com o texto de hoje, decidido no fechamento) e `alvo=desativado`.
- [ ] 14.3 **`Usuarios` (`entrega=m3`):** o chip "Google" no tom da "Senha"; o subtítulo e os dois cartões.
- [ ] 14.4 **`Provedores` (`entrega=m3`), prop `federados`:** `lista`; `vazio`; `carregando`; `erro`.
- [ ] 14.5 **Os seis formulários no modo código** (convite, novo link, redefinição, promoção a Admin, criar aplicação, gerar segredo), com um Admin sem senha, contra `MinhaConta/confirmar-codigo` e `confirmar-codigo-errado` (o rótulo, a ajuda, o erro).

**Conferência com o Google real** (o roteiro da #189, agora pelo painel):
- [ ] 14.6 Conectar com a senha e com o código (um Admin que entrou pelo convite com o Google); a volta com o aviso verde; o login seguinte pelo botão do `/entrar`.
- [ ] 14.7 As recusas que se montam: `em-uso` (a conta Google de outro usuário); `ja-conectado` (uma segunda conta Google); `sessao-expirada` (trocar a senha noutra aba antes de voltar do Google); `falha` (cancelar no Google); `dominio-nao-permitido`, com um `HostedDomain` configurado.
- [ ] 14.8 Desconectar a própria conta (a sessão continua); o Admin desconecta outra pessoa com o painel dela aberto noutro navegador (a aba dela cai em `/entrar?motivo=sessao-expirada` na renovação, em até 15 min); o `409` da última forma.
