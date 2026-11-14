## Context

A #279 é a **190b** da M3 (#196): as contas conectadas no painel. A #190 (a 190a, PR #280) entregou a entrada: o
login pelo Google, as recusas no login e o aceite de convite com provedor. Esta change leva o que a Q1 do design da
`telas-login-google` tirou dela, com as decisões Q3, Q8, Q9, Q10 e Q11 daquele design e os D4 a D7 que o corpo da
#279 resume. O backend está no `main` (`9ec078c`). A 190b é só tela.

**Fontes lidas** (14/11/2026, `9ec078c`):
- o corpo da #279 e o design arquivado `openspec/changes/archive/2026-11-14-telas-login-google/` (as Q da
  tabela "Decisões do dono") e a entrada da #190 no `02`;
- o código do `apps/api` (os contratos do D1, com arquivo e linha) e do `apps/frontend` (o D2 ao D8);
- os quadros do canvas `H7dPBZafiSkdKd1hqesPZ6` pela ferramenta Artifact, na **versão 82**, id de referência
  `1794678488-1a7d` (Q0, decisões do dono de 14/11/2026; a 81 mudou só o `UsuarioDetalhe/desconectar-409`, com o
  `409` na linha; a 82, no fechamento, só o texto da verificação do próprio no `UsuarioDetalhe` da M3). O texto cita **quadro e estado**; o id é só referência,
  porque muda a cada atualização automática do tipo de artifact sem mudar conteúdo. **Regra:** uma leitura que vier
  noutro id vale se os quatro quadros desta change tiverem o mesmo conteúdo. Na `1a7d`, sha256: `MinhaConta`
  `0bd3212e…`, `UsuarioDetalhe` `0ad23cfe…`, `Usuarios` `f6afbf3e…`, `Provedores` `cb8bba30…` (na `98cb`, a 81, o
  `UsuarioDetalhe` era `3ec43b89…`, e os outros três iguais). A `0ef4` (a primeira
  leitura) foi a atualização automática da 77; a 80 mudou só o `MinhaConta` (a confirmação do desconectar e o `409`
  na linha do Google; a linha Senha com "—"; o `sessao-expirada` com o texto da Q3; o `desconectado` da Q4).

Caminhos do backend a partir de `apps/api/src/Buteco.Api/`; do painel, a partir de `apps/frontend/src/`.

### O código do painel em `9ec078c`

- **A confirmação de quem chama** é um hook só, `features/users/callerPassword.ts` (`useCallerPassword`, l. 12–57),
  e um campo só, `features/users/components/CallerPasswordField.tsx` ("Sua senha", `current-password`). O hook
  recusa o campo vazio com "Informe a sua senha." (`usersTexts.ts` l. 8), põe o `401` no campo pelo `title` (l.
  37–39) e o resto no aviso (`failureNotice`, com o `423` e o `Retry-After`). Cada formulário passa uma função
  `(senha: string) => Promise<T>`, e as funções de API mandam `{ senha }`.
- **Minha conta** (`features/account/pages/AccountPage.tsx`) tem os cards Dados, Senha, Verificação em 2 etapas e
  Sessões, cada um com a própria consulta. `GET /account` é a chave `ACCOUNT_QUERY_KEY` (`features/account/queryKeys.ts`
  l. 3), lida por três cards; o tipo `Account` (`features/account/api/accountApi.ts` l. 75–84) **não tem**
  `formasDeEntrada` nem `contasConectadas`, que o backend já serve.
- **Editar usuário**: o `SignInFormsCard` (`features/users/components/SignInFormsCard.tsx`) mostra só a linha
  Senha (l. 67–75) e o rodapé "Por enquanto, a senha é a única forma de entrada." (l. 104). O `UserDetail`
  (`features/users/api/usersApi.ts` l. 145–149) não tem `contasConectadas`.
- **Usuários**: o chip de uma forma que não é `senha` sai cru e em contorno (`usersTexts.ts` l. 55–59;
  `UsersTable.tsx` l. 93), então o `google` aparece como "google", neutro.
- **Identidade** (`features/identity/pages/IdentityPage.tsx`) tem o emissor e as chaves de uma consulta só, com o
  polling condicional no erro (`retryWhileFailing.ts`).
- **A lista anônima dos provedores** que a #190 fez, `listLoginProviders` (`features/auth/api/federatedApi.ts` l.
  34) e `useLoginProviders` (`features/auth/federatedHooks.ts` l. 17), e o mapa de rótulos `KNOWN_PROVIDER_TYPES`
  (`federatedApi.ts` l. 21, `{ google: 'Google' }`), servem a Minha conta sem mudança.
- **A sessão**: o `sessionFetch` (`auth/sessionFetch.ts`) renova uma vez no `401` sem corpo e no `403`; a renovação
  que recebe `invalid_grant` encerra a sessão (`auth/renewal.ts` l. 75–78) e leva a
  `/entrar?motivo=sessao-expirada` (`sessionFetch.ts` l. 57–60).

## Goals / Non-Goals

**Goals:** o D2 ao D7, sobre as rotas que o backend já serve; testes do Vitest por tela e por estado; a validação
visual na homologação.

**Non-Goals:** backend (dado que falte é achado, convenção 1); o Admin conectar a conta de outra pessoa (o Admin só
desconecta); editar provedor ou adicionar provedor (a tela é só leitura); o estado do JWKS do provedor (Q11); o SSO
corporativo e as aplicações federadas (M4).

## Decisions

### D1. Os contratos, conferidos no backend

**`GET /account`** (todos os papéis, `Auth/RoleRoutes.cs:100`): `ProfileResponse(id, nome, email, papel, criadoEm,
ultimoAcesso, duasEtapas, formasDeEntrada, contasConectadas)` (`Users/Account/AccountEndpoints.cs:33-35`, montado
em :54-57). **`GET /users/{id}`** (Admin, `RoleRoutes.cs:84`): o `UserDetail` com os mesmos dois campos, o
`contasConectadas` por último (`Users/Management/UserResponses.cs:77-91`; `UserEndpoints.cs:224-229`). **`GET
/users`** (Admin) traz `formasDeEntrada` e não traz `contasConectadas` (`UserResponses.cs:17-27`).

- `formasDeEntrada` (`Users/Federated/FederatedSignInForms.cs:23-37`): `"senha"` primeiro, **só** com
  `PasswordHash` (a constante em `UserResponses.cs:35`); depois o **tipo** (não a chave) de cada vínculo com o
  issuer ainda configurado, distinto, em ordem ordinal. O vínculo fora da configuração não entra. O convidado tem
  `[]`. **É o único sinal de que a pessoa tem senha**: não há `temSenha`, e a data da senha não é servida.
- `contasConectadas` (`FederatedSignInForms.cs:8`, montado em :39-45), em ordem de `conectadaEm` e `id`:

  | campo | tipo | de onde |
  |---|---|---|
  | `chave` | `string?` | a chave do provedor configurado com o issuer do vínculo; **nula quando o issuer saiu da configuração** |
  | `tipo` | `string` | o tipo do provedor; fora da configuração, `"google"` pelo issuer do Google, senão `"desconhecido"` (:17) |
  | `email` | `string?` | o e-mail do provedor na conexão ou no último uso; nulo se o provedor não mandou |
  | `conectadaEm` | `DateTimeOffset` | |
  | `ultimoUso` | `DateTimeOffset?` | |
  | `disponivel` | `bool` | `provedor is not null`: **`chave` nula ⇔ `disponivel` falso** |

**`POST /account/federated/{chave}/connect`** (todos os papéis, `RoleRoutes.cs:107`;
`Users/Federated/FederatedAccountEndpoints.cs:60-98`), corpo `ConnectRequest(Senha?, Codigo?)` (:44), nesta ordem:

| resposta | quando | `title` |
|---|---|---|
| `403` | quem chama não é um usuário ativo (:71-74) | "Esta rota exige o login de um usuário ativo." (`Users/CurrentUser.cs:17`) |
| `404` | a chave não está configurada, **antes** da confirmação (:77-80) | "Provedor não configurado." (`Identity/Federated/FederatedLoginEndpoints.cs:37`) |
| `423` + `Retry-After` | o e-mail de quem chama pausado (:82) | "Acesso pausado por tentativas seguidas. Tente de novo mais tarde." (`Users/Login/LoginThrottleResponses.cs:12-21`) |
| `401` | com senha: a senha errada ou ausente; o `codigo` é ignorado (`Users/AccessLinks/CallerPassword.cs:45,54`) | "Senha inválida." (:29) |
| `401` | sem senha: o código errado, ausente ou repetido; a `senha` é ignorada (:66) | "Código inválido." (:31) |
| `500` | o segredo do app ilegível (:62) | "Não foi possível conferir o código do app autenticador. Fale com o administrador." (:33-34) |
| `503` | a descoberta do provedor sem resposta em 10 s (`Identity/Federated/FederatedChallenge.cs:26-29`) | "Não foi possível falar com o provedor. Tente de novo em instantes." (:18) |
| `200 { url }` | e o cookie do fluxo (`FederatedChallenge.cs:46-48`) | — |

O código de recuperação **não vale** (`Users/TwoFactor/TwoFactorVerifier.cs:47-48,86`, `allowRecoveryCode: false`),
e o passo TOTP é de uso único (:79-83). Toda recusa conta no bloqueio do e-mail (`CallerPassword.cs:53,65`). **Não
há `409` nem `429` nesta rota** (ver D8.1).

**A volta** (`GET /auth/federated/callback`, `FederatedLoginEndpoints.cs:244-297`): `302 /account?conectado=<chave>`
(:296) ou `302 /account?recusa=<motivo>[&provedor=<chave>]` (:311-315; o `provedor` sai quando a chave é nula). Os
motivos do conectar, na ordem em que o backend confere:

| motivo | a condição no código |
|---|---|
| `falha` | o retorno recusado pelo cliente OIDC (state, correlação, PKCE, nonce, assinatura, `aud`, `iss`, **o state de 10 min vencido**, ou a pessoa cancelou no Google); a chave do state fora da configuração; sem `sub` (:146-158, :170-175). Pode vir **sem** `provedor` |
| `dominio-nao-permitido` | o `hd` diferente do `HostedDomain` configurado, só quando há um (:253-257, :358-359) |
| `sessao-expirada` | `user is not { IsActive: true } \|\| user.SecurityStamp != state.buteco_stamp` (:262): o usuário sumiu, foi desativado, ou **o stamp mudou** entre o início e a volta (troca de senha, redefinição da verificação, desconectar pelo Admin, desativação). Não olha a sessão do painel |
| `em-uso` | o par (issuer, sub) ligado a outro usuário (:269-275), também na corrida do índice único (:288-293) |
| `ja-conectado` | o mesmo par já ligado a este usuário, ou este usuário já tem um vínculo com o issuer (outra conta Google) (:277-281) |

`email-nao-verificado`, `desativado`, `convite-pendente`, `nao-conectada` e `sem-acesso` são só do login
(:178-241). **O conectar não grava o `buteco-recusa`** (`refusal.Write` só no login, :239; o `ConnectAsync` nem
recebe o cookie, :244-251). Dois casos levam ao `/entrar` em vez do `/account`: nenhum provedor configurado
(`/entrar?recusa=falha`, :133-137) e o state ilegível (:174) (ver D8.2).

**`DELETE /account/federated/{chave}`** (todos os papéis, `RoleRoutes.cs:108`) e **`DELETE
/users/{id}/federated/{chave}`** (Admin pelo papel gravado, `RoleRoutes.cs:91`, `CurrentUser.cs:37-38`)
(`FederatedAccountEndpoints.cs:100-195`), **sem corpo e sem senha** (:21):

| resposta | quando | `title` |
|---|---|---|
| `204` | desconectado (:194) | — |
| `403` | o Admin aponta para si mesmo pela rota de Usuários (:132-135) | "Para desconectar a sua própria conta, use Minha conta." (:40) |
| `403` | quem chama não é ativo (ou, na de Usuários, não é Admin) (:109-112) | "Esta rota exige o login de um usuário ativo." |
| `404` | a chave fora da configuração, ou a conta não conectada a esse provedor (:151-154, :170-173, :221-222) | "Esta conta não está conectada a esse provedor." (:42) |
| `404` | o usuário não existe (:162-165) | "Usuário não encontrado." |
| `409` | sem outra forma: a senha mais os outros vínculos com provedor configurado, com a linha do usuário travada (:159-161, :175-178) | "A última forma de entrada não pode ser removida." (:38) |

O alvo desativado **não é recusado** (só a existência, :162). **Só o desconectar pelo Admin** troca o stamp e
encerra as sessões, na mesma transação (`user.RenewSecurityStamp()` e `sessions.EndAllAsync`, :181-189); o
desconectar de si mesmo não mexe em nada disso (D6).

**`GET /identity/federated-providers`** (Admin pelo papel gravado, `RoleRoutes.cs:97`, :200-203;
`FederatedAccountEndpoints.cs:197-219`): `{ provedores: [{ chave, tipo, nome, issuer, usadoPara, clientId,
uriDeRetorno, vinculos }] }` (:46-49), na ordem da configuração (:210). `usadoPara` é sempre `["login"]` (:215);
`vinculos` conta usuários distintos por issuer (:205-208). **O campo é `uriDeRetorno`.** Nada de JWKS.

**A confirmação nas seis rotas** passa pelo mesmo `CallerPassword.RejectAsync`, com o `423` antes, e **todas
aceitam `senha` e `codigo`** desde a #189:

| formulário | rota | corpo | o painel hoje |
|---|---|---|---|
| convite | `POST /users/invitations` | `{ nome, email, papel, senha, codigo }` (`Users/AccessLinks/InvitationEndpoints.cs:39`) | `inviteUser` (`usersApi.ts`) |
| novo link | `POST /users/{id}/invitation/renew` | `{ senha, codigo }` (:41) | `renewInvitation` |
| redefinição | `POST /users/{id}/password-reset` | `{ senha, codigo }` (`PasswordResetEndpoints.cs:38,44`) | `requestPasswordReset` |
| promoção a Admin | `PUT /users/{id}/role` | `{ papel, senha, codigo }`, a confirmação só para `admin` (`Users/Management/UserRoleEndpoint.cs:33,76-87`) | `changeUserRole` |
| criar aplicação | `POST /applications` | `{ nome, descricao, senha, codigo }` (`ExternalApplications/ExternalApplicationEndpoints.cs:44`) | `createApplication` (`applicationsApi.ts` l. 127) |
| gerar segredo | `POST /applications/{id}/secrets` | `{ senha, codigo }` (:48) | `generateSecret` (l. 144) |

A redefinição de quem não tem senha é servida: o `password-reset` recusa só o convidado e o desativado
(`PasswordResetEndpoints.cs:75-76,97`), e o aceite do link grava o `PasswordHash`
(`AccessLinkPublicEndpoints.cs:202`).

### D2. A confirmação com o código (Q3)

**O modo vem de `GET /account`.** O `useCallerPassword` passa a ler a consulta `ACCOUNT_QUERY_KEY` (a mesma dos
cards de Minha conta; o cache serve as seis telas) e expõe `modo: 'senha' | 'codigo'`: `'codigo'` **só** quando a
resposta chegou e `formasDeEntrada` não tem `"senha"`. Com a consulta pendente ou em erro, o modo é `'senha'`, o de
hoje (Q2). O `401` "Código inválido." no modo `'senha'` (a consulta falhou e a pessoa não tem senha) relê a conta, e
o campo vira o do código, **vazio e sem erro**, com a ajuda do código (decisão do dono no fechamento): o "Código
inválido." era da senha que a pessoa digitou.

**O campo.** O `CallerPasswordField` recebe o `modo`:

| | `senha` (hoje) | `codigo` |
|---|---|---|
| rótulo | "Sua senha" | "Código do app autenticador" |
| entrada | `type=password`, `autocomplete=current-password` | `type=text`, `inputmode=numeric`, `autocomplete=one-time-code` |
| ajuda | a de cada formulário, como hoje | "Você entra só pelo Google: confirme com o código de 6 dígitos do app. O código de recuperação não vale aqui." (Q6) |
| vazio | "Informe a sua senha." | "Informe o código." (Q6) |
| `401` | o `title`: "Senha inválida." | o `title`: "Código inválido." |

**O envio.** A função que o formulário passa recebe a confirmação, não a senha: `(c: { senha } | { codigo }) =>
Promise<T>`, e as seis funções de API espalham `c` no corpo. No modo `'senha'` o corpo é o de hoje, byte a byte;
no `'codigo'`, `{ codigo }` no lugar de `{ senha }`. O valor só existe no estado do hook e sai no sucesso, no `401`
e ao desmontar, como a senha hoje.

**Os seis formulários e o que muda em cada um:**

| formulário | arquivo | o que muda |
|---|---|---|
| convite | `features/users/components/InviteForm.tsx` (l. 35, 114) | o `modo` no campo; `inviteUser({ …, ...c })` |
| novo link | `features/users/components/RenewInviteForm.tsx` (l. 20, 62) | o `modo`; `renewInvitation(id, c)` |
| redefinição | `features/users/components/SignInFormsCard.tsx` (`ResetPasswordForm`, l. 117, 163) | o `modo`; `requestPasswordReset(id, c)`; os textos da pessoa sem senha (D5) |
| promoção a Admin | `features/users/components/UserDataCard.tsx` (l. 88, 309; a frase de l. 302–303) | o `modo`; `changeUserRole(id, { papel, ...c })`; a frase "Para promover, confirme com a sua senha." ganha a variante com o código (Q6); o `motivo()` (l. 80–83) usa o `title` do `401`, sem o texto fixo |
| criar aplicação | `features/applications/pages/ApplicationCreatePage.tsx` (l. 46, 126) | o `modo`; `createApplication({ …, ...c })` |
| gerar segredo | `features/applications/components/ApplicationCredentialTab.tsx` (l. 352, 409) | o `modo`; `generateSecret(id, c)` |
| conectar (novo) | o card de Minha conta (D3) | o mesmo hook, `connectFederated(chave, c)` |

**Os casos que existem.** Nenhum caso dos seis formulários, nem das páginas que os montam, confere a lista exata
de pedidos ou "nenhum pedido" no total: a varredura (D10, "A lista do topo") achou só asserções **filtradas** por
rota, método ou prefixo, que o `GET /account` novo não alcança. O modo `'senha'` é o padrão, e o corpo nele não muda.
O que muda é o **duplo**: o `fakeAccountApi` (`features/account/test/fakeAccountApi.ts` l. 57–61), que todos esses
arquivos usam (o `fakeUsersApi` e o `fakeApplicationsApi` são o mesmo, reexportado), já põe o `POST /connect/token`
por padrão e passa a pôr também o `GET /account` com uma conta **com senha**. Sem isso, os arquivos pediriam uma rota
sem resposta (o duplo lança, l. 86), a consulta cairia em erro e o modo continuaria `'senha'`: os casos passariam, mas
pelo caminho de erro. Os casos novos do modo `'codigo'` declaram a conta sem senha.

**Descartado:** um campo de tipo no token (o id token não diz se a pessoa tem senha, e mudá-lo é backend); esconder
o formulário até a conta chegar (um pedido a mais travaria os seis formulários; Q2); aceitar o código de
recuperação (o backend recusa).

### D3. Minha conta, "Formas de entrada" (Q8, Q9, Q10)

**As consultas.** O card lê `GET /account` (`formasDeEntrada`, `contasConectadas`) e a lista anônima `GET
/auth/federated/providers` pelo `useLoginProviders` da #190 (a rota do Admin, `/identity/federated-providers`, recusa
os outros papéis). A lista falhando ou fora do formato esconde só as linhas "Conectar", como no login (Q5 da #190).

**Quando o card aparece** (decisão do spec, ver "Decisões do dono"): com a conta lida **e** pelo menos um provedor na
lista ou uma conta em `contasConectadas` (inclusive fora da configuração). Sem provedor e sem conta, o painel não
tem forma de entrada além da senha, e o rodapé, que fala da conta Google, afirmaria o que a instalação não tem
(convenção 13); o card Senha já diz como a pessoa entra. Com `GET /account` em erro, o card não aparece: o card
Dados já mostra o erro da mesma consulta. **O aviso segura o card** (decisão do dono, na aprovação das specs): com um
aviso da volta ou do desconectar na tela, o card aparece mesmo sem provedor e sem conta, com o aviso e só a linha
Senha, até a página desmontar. Sem isso, a recusa sem conta e com a lista falhando, ou o desconectar da única conta
com a lista vazia, levariam o aviso junto com o card.

**As linhas**, nesta ordem, na tabela Forma · Identificação · Conectada em · Último uso · Ações:

| linha | quando | Identificação | Conectada em · Último uso | Ações |
|---|---|---|---|---|
| Senha | `formasDeEntrada` tem `"senha"` | o e-mail, em mono | "—" · "—" (a data da senha não é servida) | "—" (Q7) |
| a conta conectada | cada item de `contasConectadas` com `disponivel` | o `email` (ou "—"), em mono | as datas, no formato curto do painel | "Desconectar", ou, sendo a última forma, o botão desabilitado com "É a sua única forma de entrada." (Q8) |
| o vínculo fora da configuração | cada item com `disponivel` falso | o `email` e o selo "Provedor fora da configuração" | as datas | "Sem ação" (Q9: a rota exige uma chave configurada) |
| o provedor sem conta | cada provedor da lista cuja `chave` não está em `contasConectadas` | "Não conectada" | "—" · "—" | "Conectar Google" (azul) |

O rótulo da Forma sai do `KNOWN_PROVIDER_TYPES` (`google` → "Google"); um tipo desconhecido sai cru, neutro, sem
ação de conectar (convenção 13). **A última forma** se calcula como no backend: `"senha"` conta 1, e cada conta
`disponivel`, 1; com o total 1, o botão daquela conta fica desabilitado. O `409` continua tratado (Q8): outra aba
pode ter tirado a outra forma.

**Conectar.** "Conectar Google" abre, **dentro do card** (sem modal), o formulário "Conectar a sua conta Google",
com o texto do quadro, "Você vai ao Google, escolhe a conta e volta para cá. Depois disso, pode entrar no
painel também com o Google; a verificação em 2 etapas continua a mesma." (Q5), o campo do D2 (com senha, "Sua senha" e a ajuda "Para conectar, confirme que é você.";
sem senha, o código, com a ajuda do D2) e "Continuar com Google" (azul) e "Cancelar". `POST
/account/federated/{chave}/connect`:

| resposta | a tela |
|---|---|
| `200 { url }` | "Redirecionando para o Google…" no lugar do formulário, e o `authRuntime.assign(url)` |
| `401` | o `title` no campo: "Senha inválida." ou "Código inválido." |
| `404` com o `title` exatamente "Provedor não configurado." | aviso vermelho "Provedor não configurado." no formulário; a lista é relida, e a linha some |
| outro `404` | aviso vermelho "Não foi possível conectar a conta Google. Tente de novo." |
| `503` | aviso vermelho "Não foi possível falar com o provedor. Tente de novo em instantes." |
| `423` | o aviso de conta pausada de hoje (`failureNotice`), com o botão desabilitado até o `Retry-After` |
| `500`, `403` | o aviso pelo `failureNotice` (o `title` do backend) |
| rede | "Não foi possível falar com o servidor." |

**Desconectar.** "Desconectar" pede a confirmação **na célula de Ações da linha** (a linha em vermelho claro):
"Desconectar o Google (`<e-mail>`)? Você deixa de entrar com essa conta.", com "Desconectar" (vermelho) e
"Cancelar" (o quadro `MinhaConta/confirmar-desconectar` da versão 80, sem o "; a senha continua valendo." da lista
de textos do dono: a frase seria falsa para quem não tem senha, pela mesma razão da Q4; registro, convenção 17).
`DELETE /account/federated/{chave}`:

| resposta | a tela |
|---|---|
| `204` | aviso verde "Conta Google desconectada." (Q4), e a conta relida |
| `409` | na célula de Ações da mesma linha: "A última forma de entrada não pode ser removida.", com "Fechar"; a conta relida |
| `404` | aviso "Esta conta não está conectada a esse provedor." (o `title`); a conta relida |
| outro, rede | o aviso pelo `failureNotice` |

**O rodapé do card:** "Cada forma de entrada leva ao mesmo usuário do Buteco. A conta Google só entra se estiver
conectada aqui ou se você aceitou o convite com ela. A última forma de entrada não pode ser removida."

**A pessoa sem senha (Q10).** Sem `"senha"` em `formasDeEntrada`, o card Senha perde o "Trocar senha" e o
formulário (que sempre responderia `401`) e mostra "Você entra só pelo Google. Para ter também uma senha, peça a um
administrador o link para criar uma." Com a consulta pendente ou em erro, o card é o de hoje.

**O subtítulo** da página vira o da M3: "Os seus dados, as formas de entrada, a sua senha, os códigos de
recuperação e os navegadores em que você está conectado." (Q5).

### D4. A volta do conectar no `/account`

O `/account` lê, na montagem, `conectado`, `recusa` e `provedor` da query:

| query | cor | título | texto |
|---|---|---|---|
| `conectado=<chave>` | verde | — | "Conta Google conectada. Da próxima vez, você também pode entrar com o Google." |
| `recusa=em-uso` | vermelho | "Conta Google já conectada" | "Esta conta Google já está conectada a outro usuário do Buteco Agentes. Escolha outra conta." |
| `recusa=ja-conectado` | amarelo | "Você já tem uma conta Google conectada" | "Para trocar de conta, desconecte a atual e conecte a outra." |
| `recusa=sessao-expirada` | amarelo | "A conexão expirou" | "A sua conta mudou enquanto você estava no Google: a senha, a verificação ou o acesso. Tente conectar de novo." (Q3) |
| `recusa=dominio-nao-permitido` | vermelho | "Domínio não permitido" | "Esta conta Google não é do domínio permitido neste painel." |
| `recusa=falha` e **todo motivo desconhecido** (convenção 13) | vermelho | — | "Não foi possível conectar a conta Google. Tente de novo." |

O aviso fica no topo do card "Formas de entrada". **Depois de ler**, a página troca o endereço por `replace`, sem os
três parâmetros (e sem perder o `state` da navegação, que leva o aviso do rebaixamento): recarregar não repete o
aviso, e um link copiado não o leva a outra pessoa (Q4 da #190). O `provedor` não entra no texto, então não é
conferido contra a lista.

**A rota de uso único do e-mail não entra.** Nenhum texto do conectar leva o e-mail, e o backend **nem grava** o
`buteco-recusa` no conectar (D1). Chamar `POST /auth/federated/refusal` aqui só gastaria o cookie de uma recusa de
login que ainda estivesse por ler. O `/account` não a chama.

**Descartado:** deixar a recusa na URL; ler a recusa pela rota do aviso.

### D5. Editar usuário

O `SignInFormsCard` ganha as colunas "Conectada em" e "Último uso" e uma linha por conta de
`contasConectadas`:

| linha | alvo | Ações |
|---|---|---|
| Senha, com senha | outro, ativo | "Redefinir senha", como hoje |
| Senha, **sem** senha | outro, ativo | Identificação "Sem senha"; o botão "Gerar link de senha", com o título "Gerar link para `<nome>` criar uma senha" e o texto "Hoje a pessoa entra só pelo Google. Com o link, ela cria uma senha e passa a ter as duas formas de entrada." |
| Senha | desativado | "Reative para gerar um link", como hoje |
| Senha | o próprio, com senha | "Trocar em Minha conta", como hoje; sem senha, nenhuma ação |
| a conta conectada | outro (ativo ou desativado) | "Desconectar"; sendo a última forma, o botão desabilitado com "É a única forma de entrada de `<nome>`." |
| a conta conectada | o próprio | o link "Desconectar em Minha conta" (o backend recusa o próprio com `403`) |
| o vínculo fora da configuração | qualquer | o selo "Provedor fora da configuração" e "Sem ação" |

A redefinição usa a confirmação do D2 (o Admin sem senha confirma com o código).

**Desconectar pelo Admin**, com a confirmação **na célula de Ações da linha**: "Desconectar o Google de `<nome>`? A
pessoa sai de todos os navegadores.", "Desconectar" (vermelho) e "Cancelar". `DELETE /users/{id}/federated/{chave}`:

| resposta | a tela |
|---|---|
| `204` | aviso verde "Google desconectado. `<nome>` saiu de todos os navegadores."; o detalhe relido (as sessões vão a "Nenhuma sessão ativa") e a lista de Usuários invalidada |
| `409` | na célula de Ações da mesma linha: "A última forma de entrada não pode ser removida.", com "Fechar"; o detalhe relido (o quadro `UsuarioDetalhe/desconectar-409` da versão 81) |
| `404` "Usuário não encontrado." | o estado "inexistente" de hoje (`onGone`) |
| `404` "Esta conta não está conectada a esse provedor." | o aviso com o `title`; o detalhe relido |
| `403` | o aviso pelo `failureNotice`, como os outros `403` das ações de Editar usuário |

**Os textos da M3:** o rodapé "Contas de provedores são conectadas pela própria pessoa em Minha conta. Aqui o
administrador só desconecta. A última forma de entrada não pode ser removida."; a verificação de outro usuário
(`UserTwoFactorCard.tsx` l. 39–40), **ativa ou não**, "Obrigatória para todos os papéis, inclusive para quem entra
pelo Google: o Buteco pede o próprio código."; a ajuda do e-mail, "É o login com senha. Trocar não afeta as contas
conectadas." (Q5).

### D6. A sessão de quem é desconectado

- **Quem desconecta o próprio Google** continua na sessão: o `DELETE /account/federated/{chave}` não troca o stamp
  nem encerra sessão (`FederatedAccountEndpoints.cs:181-190`, só com `byAdmin`). A tela relê a conta e mostra o
  aviso; o próximo login pelo Google dessa conta cai numa recusa do login da #190 (a conta não está mais ligada).
- **A pessoa desconectada pelo Admin**: o backend troca o stamp e revoga todas as autorizações dela
  (`FederatedAccountEndpoints.cs:181-189`; `Users/Sessions/UserSessions.cs:83-88`). O access token dela é um JWT de
  15 min sem conferência do stamp a cada pedido (`Identity/Validation/IssuerTokenAuthentication.cs:55-76`,
  `Identity/IdentityTokenLifetimes.cs:23`), e ela continua ativa (o `IsActive` do `CurrentUser.cs:26-27` segue
  verdadeiro). Então a aba aberta funciona **até a próxima renovação**, e a renovação recebe `400 invalid_grant`
  (`Identity/Flow/UserGrantTokenHandler.cs:60-84`, a conferência em :78).
- **Como no rebaixamento (#184).** O rebaixamento não troca o stamp: a renovação seguinte traz o papel gravado
  (`UserGrantTokenHandler.cs:86,96-100`) e a tela se redesenha, e antes disso a primeira rota de Admin responde `403`
  e força a renovação (`sessionFetch.ts` l. 80–87). Aqui vale a mesma regra: **o painel não empurra nem consulta
  nada; o efeito chega na próxima renovação, pelo `sessionFetch` que já existe.** A diferença é o resultado da
  renovação: no rebaixamento, `ok` com o papel novo; aqui, `ended`, que apaga o registro e leva a
  `/entrar?motivo=sessao-expirada` (`renewal.ts` l. 75–78; `sessionFetch.ts` l. 57–60, 96–100), o mesmo caminho da
  redefinição da verificação e do "Encerrar todas as sessões" de hoje. Nada muda no código da sessão; o caso novo
  fixa esse caminho com a resposta `invalid_grant` do duplo (o mesmo arranjo de `auth/renewal.test.ts`).
- **"Saiu de todos os navegadores"** é verdade para as sessões (revogadas na hora), não para um access token já
  emitido, que vale até 15 min. É a mesma frase e a mesma semântica da redefinição da verificação de hoje; fica como
  está (registro, ver Riscos).

### D7. Usuários e Identidade (Q11)

**Usuários.** O `signInForms` (`usersTexts.ts` l. 55–59) passa a conhecer os tipos do `KNOWN_PROVIDER_TYPES`:
`google` → "Google", `conhecido: true`, no tom da "Senha"; outro tipo continua cru e neutro. Os cards de baixo com o
texto do quadro `m3`: "Ninguém entra sem ter sido convidado aqui, mesmo autenticado pelo Google. Não existe entrada
automática por domínio." e "Esqueceu a senha: um Admin gera um link de redefinição em Editar usuário; quem entra só
pelo Google recebe o mesmo link para criar uma. A verificação em 2 etapas é obrigatória para todos os papéis,
inclusive para quem entra pelo Google: o Buteco pede o próprio código." (sem "feita no próprio provedor"). O
subtítulo, "Quem pode entrar no painel. Todo usuário existe no Buteco; os provedores federados são formas adicionais
de provar quem ele é." (Q5).

**Identidade.** Um card "Provedores federados", abaixo das chaves, com "Definidos na configuração do servidor" no
cabeçalho, de `GET /identity/federated-providers` numa **consulta própria** (`['identity', 'federated-providers']`):
o erro de uma não derruba a outra.

| coluna | de onde |
|---|---|
| Provedor | o `nome` e, abaixo, a `chave` em mono (Q9) |
| Usado para | `"login"` → o selo "Login de usuários"; outro valor cru, neutro |
| Issuer · ID do cliente · URI de retorno | `issuer`, `clientId`, `uriDeRetorno`, em mono |
| Vínculos | "1 usuário" no 1; "`<n>` usuários" no resto, inclusive o 0 (Q8) |

Os estados: vazio, "Nenhum provedor federado configurado. O login fica só com e-mail e senha."; carregando,
"Carregando os provedores…"; erro, "Não foi possível carregar os provedores federados agora." com "Tentar de novo",
**sem polling** (o texto não promete "o painel tenta de novo sozinho", ao contrário do card do emissor; convenção 20).
O rodapé: "Quem entra pelo Google precisa estar ligado a um usuário do Buteco, pelo aceite de um convite ou
conectando a conta em Minha conta. O Google confirma a identidade; o Buteco emite a sessão e pede o próprio código
da verificação em 2 etapas. Vínculos é o número de usuários com uma conta conectada a este provedor." Sem
"Adicionar provedor", sem "Editar" e sem "Chaves". O subtítulo da página, o do quadro `m3` (Q5).

### D8. O que os quadros mostram, contra o que o backend serve

"serve" = o backend serve e a tela usa. **Não cumpre** = o quadro afirma o que o sistema não sabe ou não faz.

Quadros e estados da versão 82 (id de referência `1794678488-1a7d`). **Resolvido (80)**, **resolvido (81)** e **resolvido (82)** marcam o
que cada versão corrigiu depois da primeira leitura.

**`MinhaConta` (`entrega=m3`, prop `formas`):**

| item | estado | o que o quadro mostra | o backend / a M3 |
|---|---|---|---|
| a | `lista` | Senha (e-mail, "—", "—", "—") e Google (e-mail, 14/08/2026, "hoje, 09:02", "Desconectar") | serve (`contasConectadas`); **resolvido (80)**: o "Trocar senha" saiu da linha (Q7) |
| b | `sem-conta` | Google "Não conectada" com "Conectar Google" azul | serve (a lista anônima menos `contasConectadas`) |
| c | `confirmar-senha`, `confirmar-senha-errada` | "Conectar a sua conta Google", o texto do ir e voltar, "Sua senha", "Para conectar, confirme que é você.", "Senha inválida." | serve (`401` "Senha inválida.") |
| d | `confirmar-codigo`, `confirmar-codigo-errado` | sem a linha Senha; o vínculo fora e o Google livre; "Código do app autenticador", a ajuda, "Código inválido." | serve (`401` "Código inválido."; sem senha, o backend confere o código) |
| e | `provedor-indisponivel` | aviso vermelho do `503` no formulário | serve |
| f | `provedor-nao-configurado` | aviso vermelho "Provedor não configurado." no formulário | serve (`404` antes da confirmação) |
| g | `redirecionando` | "Redirecionando para o Google…" | serve (`200 { url }`) |
| h | `conectado` | o aviso verde | serve (`?conectado=`) |
| i | `recusa-em-uso`, `recusa-ja-conectado`, `recusa-dominio-nao-permitido`, `recusa-falha` | os avisos, as cores e os títulos do D4 | serve |
| j | `recusa-sessao-expirada` | "A conexão expirou" / "A sua conta mudou enquanto você estava no Google: a senha, a verificação ou o acesso. Tente conectar de novo." | serve (o stamp: senha, verificação, desconectar pelo Admin, desativação); **resolvido (80)** (Q3) |
| k | `confirmar-desconectar` | a confirmação na célula de Ações da linha do Google, "Desconectar o Google (`<e-mail>`)? Você deixa de entrar com essa conta." | serve; **resolvido (80)**: a divergência da faixa acima da tabela; o texto perdeu "; a senha continua valendo." (registro, D3) |
| l | `desconectar-409` | "A última forma de entrada não pode ser removida." com "Fechar", na mesma linha | serve (`409`); **resolvido (80)** |
| m | `desconectado` | o aviso verde "Conta Google desconectada." | serve (`204`); **resolvido (80)** (Q4) |
| n | `fora-da-configuracao` | o e-mail antigo, o selo, as datas e "Sem ação" | serve (`disponivel` falso, `chave` nula; o `DELETE` responderia `404`) |
| o | `sem-senha` | só o Google, o botão desabilitado com "É a sua única forma de entrada."; o card Senha com o texto da Q10, sem "Trocar senha" | serve (`formasDeEntrada` sem `"senha"`) |
| p | todos | o subtítulo da M3 e o rodapé do card | serve (Q5 para o subtítulo) |
| q | — | o card sem provedor configurado e sem conta conectada | **não desenhado**; o card não aparece (D3) |

**`UsuarioDetalhe` (`entrega=m3`, prop `formas`, com `alvo=outro`, `proprio` e `desativado`):**

| item | estado | o que o quadro mostra | o backend / a M3 |
|---|---|---|---|
| a | `senha-e-google` | Senha ("—", "—", "Redefinir senha") e Google (e-mail, "14/08/2026", "ontem", "Desconectar") | serve; sem a data da senha (**corrigido** desde a lista da #190) |
| b | `so-senha` | só a Senha | serve |
| c | `so-google` | "Sem senha", "Gerar link de senha"; o Google com o botão desabilitado e "É a única forma de entrada de `<nome>`." | serve (`password-reset` para o ativo sem senha) |
| d | `so-google`, o formulário | "Gerar link para `<nome>` criar uma senha", o texto da pessoa sem senha, "Sua senha" | serve; o campo do código (o Admin sem senha) é **não desenhado; referência: `MinhaConta/confirmar-codigo`** |
| e | `fora-da-configuracao` | o selo e "Sem ação" | serve |
| f | `confirmar-desconectar` | a confirmação na célula de Ações, "…A pessoa sai de todos os navegadores." | serve (o stamp e as sessões); a frase vale para as sessões, não para o access token de até 15 min (D6, Riscos) |
| g | `desconectar-409` | "A última forma de entrada não pode ser removida." com "Fechar", na célula de Ações da linha Google | serve; **resolvido (81)**: estava numa faixa abaixo da tabela |
| h | `desconectado` | "Google desconectado. `<nome>` saiu de todos os navegadores." | serve; como o f |
| i | `alvo=proprio` | "Trocar em Minha conta" e "Desconectar em Minha conta"; com `so-google`, nenhuma ação na Senha; a verificação com "Para trocar de celular, peça a outro Admin que redefina a sua verificação." | serve (o `403` do próprio); **resolvido (82)**: o texto da verificação era "Gerencie a sua em Minha conta." |
| j | `alvo=desativado` | "Reative para gerar um link"; o Google com "Desconectar" | serve (o backend não recusa o desativado) |
| k | todos | o rodapé, a verificação da M3, a ajuda do e-mail | serve (Q5 para a ajuda do e-mail) |
| l | promoção | "Para promover, confirme com a sua senha." e "Sua senha" | serve; a variante com o código é **não desenhada; referência: `MinhaConta/confirmar-codigo`** (Q6) |

**`Usuarios` (`entrega=m3`):**

| item | o que o quadro mostra | o backend / a M3 |
|---|---|---|
| a | os chips "Senha" e "Google", no mesmo tom | serve (`formasDeEntrada` com o tipo) |
| b | Carla Menezes só com "Google" e a verificação "Ativa" | serve (sem o "No provedor" e sem o "SSO Empresa", **corrigido**) |
| c | `cardConvite` e `cardSenha` da M3 | serve (sem "feita no próprio provedor") |
| d | o subtítulo da M3 | serve (Q5) |
| e | o convite com "Sua senha" | serve; o campo do código é **não desenhado; referência: `MinhaConta/confirmar-codigo`** |

**`Provedores` (`entrega=m3`, prop `federados`):**

| item | estado | o que o quadro mostra | o backend / a M3 |
|---|---|---|---|
| a | `lista` | Google e `corporativo` (a chave), "Login de usuários", o issuer, o ID do cliente, a URI de retorno, "3 usuários" | serve (`nome`, `chave`, `usadoPara`, `issuer`, `clientId`, `uriDeRetorno`, `vinculos`); sem "Chaves" (**corrigido**, Q11) |
| b | `vazio` | "Nenhum provedor federado configurado…" | serve (`provedores: []`) |
| c | `carregando` | "Carregando os provedores…" | o painel |
| d | `erro` | o texto e "Tentar de novo" | o painel; sem polling (D7) |
| e | todos | "Definidos na configuração do servidor" e o rodapé | serve |
| f | todos | o subtítulo com "aplicações" e "Provedores federados são opcionais" | serve (Q5) |

**Os seis formulários no modo código** (convite, novo link, redefinição, promoção a Admin, criar aplicação, gerar
segredo): **não desenhados; referência: `MinhaConta/confirmar-codigo`** e `confirmar-codigo-errado` (o rótulo
"Código do app autenticador", a ajuda e o erro "Código inválido."; decisão do dono, Q5 e Q6).

### D9. Validação visual

A regra da M1 e da M2: o checklist vai no `tasks.md`, no `02` e no PR; a validação é na homologação da M3, quadro a
quadro e estado contra a versão 82 (id de referência `1794678488-1a7d`), **sem prender o PR**. A conferência com o Google real (conectar,
`ja-conectado`, `em-uso`, desconectar, o Admin desconectando com a outra pessoa numa aba aberta) fica para a
homologação, depois do merge, com o roteiro da #189. Achado de homologação vira issue nova (convenção 23).

### D10. Tamanho (convenção 18)

**Âncoras** (só `apps/frontend/src`, sem `openspec/` e `.md`): a **#190**, produção **+742** (projetada ~965,
−23 %), testes **+1.516** (−7 %), duplos +37 (−69 %), 99 casos (−4 %), **sem** tabela com confirmação na linha; a
**#188**, produção **+2.708** (projetada ~1.620, **+67 %**), testes +1.945, duplos +284, 137 casos, com tabelas de
ação e confirmação na linha. Régua da #188: linhas já formatadas pelo Prettier, cada tabela com ação e confirmação
na linha contada à parte (esta tem **duas**: Minha conta e Editar usuário). Unidade: linha; casos à parte, unidade
**caso** (cada `it` e cada linha de `it.each`).

| componente | produção | testes | casos | duplo |
|---|---|---|---|---|
| a API: os tipos de `Account` e `UserDetail`, o conectar, os dois `DELETE`, os provedores do Admin, a confirmação nas seis funções (D1) | +110 | +160 | 14 | — |
| a confirmação com o código: o `modo`, o campo, o envio, a releitura no `401` "Código inválido.", os seis formulários (D2) | +120 | +340 | 24 | — |
| **a tabela de Minha conta**: as quatro linhas, a última forma, o fora da configuração, quando o card aparece (com o aviso que o segura), o conectar e as respostas, o redirecionamento (D3) | +285 | +470 | 29 | — |
| a volta: os seis avisos, o desconhecido, a limpeza com o `state` (D4) | +90 | +200 | 12 | — |
| o desconectar de si mesmo: a confirmação na linha, `204`, `409`, `404`; a pessoa sem senha no card Senha; o subtítulo (D3) | +110 | +220 | 12 | — |
| **a tabela de Editar usuário**: as linhas por alvo, "Gerar link de senha", o desconectar pelo Admin e as respostas, os textos da M3 (D5) | +210 | +340 | 20 | — |
| a sessão de quem é desconectado: o `invalid_grant` na renovação (D6) | — | +40 | 1 | — |
| Usuários: o chip e os textos; Identidade: o card e os três estados (D7) | +130 | +230 | 15 | — |
| **o duplo**: três rotas por padrão no `fakeAccountApi` (`GET /account` com senha; `GET /auth/federated/providers` e `GET /identity/federated-providers` com a lista vazia); `formasDeEntrada` e `contasConectadas` no `ACCOUNT` e nos `UserDetail` do `fakeUsersApi`; as contas conectadas de exemplo | — | — | — | **+100** |
| **total** | **~+1.055** | **~+2.000** | **~127** | **+100** |

Com o erro da #190 (−23 %), ~810; com o da #188 (+67 %), até ~1.750, e é a #188 que se parece com esta pelas duas
tabelas com confirmação na linha. **Cabe numa change**: abaixo da #188 na produção, perto dela nos testes (a régua
da #188 diz que o teste segue a tabela). Os testes modificados ficam à parte, na lista do topo (7 casos, abaixo,
~+25 / −20 linhas). Atualizada com as decisões do dono (14/11/2026); continua rascunho, porque conta antes da
verificação.

**A lista do topo** (os casos existentes que mudam de asserção ou de arranjo, e por quê), conferida caso a caso no
`9ec078c`:

| arquivo | caso | o que muda | por quê |
|---|---|---|---|
| `features/account/pages/AccountPage.test.tsx` | "Os quatro cartões, na ordem" (o do subtítulo, l. 28–36) | o subtítulo | o subtítulo da M3 (Q5). A lista dos cartões **não** muda: o duplo padrão não tem provedor nem conta, e o card não aparece (D3) |
| `features/identity/pages/IdentityPage.test.tsx` | "os dois cartões, sem provedores federados" (l. 102–114) | o subtítulo; e a ausência de "Provedores federados" vira o card no estado vazio | o subtítulo da M3 (Q5) e o card do D7. O nome fica: o duplo padrão é a lista vazia |
| `features/users/pages/UsersPage.test.tsx` | o do cabeçalho e dos cards (l. 24–38) | o subtítulo e o `cardSenha` | os textos da M3 (Q5) |
| `features/users/components/SignInFormsCard.test.tsx` | "a linha Senha de outro usuário" (l. 36–45) | o rodapé; e `queryByText('Conectada em')` ausente vira presente | o rodapé da M3 e as colunas novas (D5) |
| `features/users/components/UserDataCard.test.tsx` | "Salvar desabilitado sem mudança" (l. 36–44) | a ajuda do e-mail | a ajuda da M3 (Q5) |
| `features/users/components/UserTwoFactorCard.test.tsx` | "verificação ativa" (l. 27–35) | o texto | um texto só da M3 para os dois estados (D5) |
| `features/users/components/UserTwoFactorCard.test.tsx` | "com a verificação desligada, o botão fica desabilitado" (l. 37–44) | o texto | idem |

São **7 casos**, todos de texto da M3. **Os casos que conferem a lista exata de pedidos ou "nenhum pedido":
nenhum entra.** A varredura dos arquivos dos seis formulários e das páginas que os montam (`InviteForm`,
`RenewInviteForm`, `SignInFormsCard`, `UserDataCard`, `ApplicationCreatePage`, `ApplicationCredentialTab`,
`UsersPage`, `UserDetailPage` e `ApplicationDetailPage`, `.test.tsx`) achou só asserções **filtradas**, que o
`GET /account` não alcança:

| arquivo | asserção | por que não muda |
|---|---|---|
| `InviteForm.test.tsx` l. 185; `SignInFormsCard.test.tsx` l. 82; `UserDataCard.test.tsx` l. 92 | `requests.filter(path começa com '/connect')` vazio | o `GET /account` não é `/connect` |
| `UserDataCard.test.tsx` l. 173 | os pedidos `PUT` | o `GET /account` não é `PUT` |
| `UserDataCard.test.tsx` l. 373–374 | depois do `PUT`, nenhum `GET` de `/users` | o caminho é `/account`. **Atenção no apply:** é o caso do rebaixamento de si mesmo, que invalida o `ACCOUNT_QUERY_KEY`; o `GET /account` que se seguir não entra no filtro |
| `ApplicationCreatePage.test.tsx` l. 52 | nenhum pedido a `/identity` | o caminho é `/account` |
| `calls('<rota>')` com `toHaveLength` (vários) | contam uma rota só | outra rota |

**O duplo** é um só nos três grupos: o `fakeAccountApi` (o `fakeUsersApi` e o `fakeApplicationsApi` o reexportam,
`features/users/test/fakeUsersApi.ts` l. 5, `features/applications/test/fixtures.ts` l. 15), uma **fila por rota**
em que a última resposta vale para os pedidos seguintes (l. 9–10, 87). As três rotas por padrão entram como a do
`POST /connect/token` (l. 61): o caso que declara a rota sobrepõe o padrão. **Por que o padrão dos provedores é a
lista vazia:** o `IdentityPage.test.tsx` conta as linhas da página inteira (`linhas()`, l. 87, em cinco casos) e
procura "Tentar de novo" por papel (l. 221–229); um provedor por padrão somaria uma linha, e a rota sem resposta
somaria um segundo "Tentar de novo". Com a lista vazia, o card mostra só o texto do vazio, e nenhum desses casos muda.

Caso fora desta lista que precise sair, mudar de nome ou afrouxar é parada.

### D11. Premissas falsas (convenção 9)

1. **O pedido, "o 429" do conectar:** a rota não tem limitador (o `LoginRateLimiting.Marker` só está nas rotas
   `/auth/*`, `Users/Login/LoginRateLimiting.cs:66`; `FederatedLoginEndpoints.cs:49,53`). O que existe é o `423` do
   bloqueio por e-mail (D1). A tela trata o `423` e não tem ramo de `429`. Registro.
2. **O corpo da #279, "`503` e `404` com aviso"** no conectar: há também o `401` de cada modo, o `423`, o `403` e o
   `500` do segredo ilegível (D1). Registro.
3. **O quadro `MinhaConta/recusa-sessao-expirada`, "A sua sessão mudou":** o motivo sai pela conta (o stamp), não
   pela sessão (D1). Q3.
4. **A Q3 da #190, "o código do app no conectar e nos seis formulários":** o backend já aceita o `codigo` nas sete
   rotas e decide pelo `PasswordHash` de quem chama, ignorando o outro campo (D1); o painel só precisa saber o modo.
   Registro.
5. **O corpo da #279, "o vínculo de provedor fora da configuração (`disponivel` falso, `chave` nula)":** confirmado
   (D1); e o `DELETE` com outra chave responderia `404` "Esta conta não está conectada a esse provedor.", não
   "Provedor não configurado.". Registro.
6. **O pedido do dono, "o `GET /account` novo entra nos casos que conferem a lista exata de pedidos, como o `GET`
   dos provedores entrou na #190":** nos arquivos dos seis formulários não há caso assim; as asserções de pedidos
   são filtradas (D10, "A lista do topo"). Registro, sem mudança de comportamento nem de cobertura.
7. **A lista de textos do dono, a confirmação do desconectar em Minha conta "…; a senha continua valendo.":** o
   quadro `MinhaConta/confirmar-desconectar` da versão 80 não tem a segunda oração, e a tela segue o quadro (D3).
   Registro (convenção 17).

## Árvore de pastas

```
apps/frontend/src/
├── features/account/
│   ├── api/accountApi.ts                      (M) Account com formasDeEntrada e contasConectadas; connectFederated, disconnectOwnFederated
│   ├── components/
│   │   ├── AccountSignInFormsCard.tsx         (N) a tabela, o conectar e o desconectar (D3)
│   │   ├── AccountSignInFormsCard.test.tsx    (N)
│   │   ├── ConnectProviderForm.tsx            (N) o formulário no card, com o useCallerPassword (D2, D3)
│   │   ├── AccountPasswordCard.tsx            (M) a pessoa sem senha (Q10)
│   │   └── AccountPasswordCard.test.tsx       (M) casos novos
│   ├── federatedReturn.ts                     (N) a leitura e a limpeza da volta (D4)
│   ├── federatedReturn.test.tsx               (N)
│   ├── accountFederatedTexts.ts               (N) os avisos da volta e do desconectar
│   ├── pages/AccountPage.tsx                  (M) o card novo, o subtítulo
│   ├── pages/AccountPage.test.tsx             (M) lista do topo
│   └── test/fakeAccountApi.ts                 (M) duplo: GET /account e GET /auth/federated/providers por padrão
├── features/users/
│   ├── callerPassword.ts                      (M) o modo e a confirmação (D2)
│   ├── callerPassword.test.tsx                (N) o modo pela conta, a releitura, os seis formulários no modo código
│   ├── usersTexts.ts                          (M) o chip Google
│   ├── api/usersApi.ts                        (M) UserDetail.contasConectadas; disconnectUserFederated; a confirmação
│   ├── components/
│   │   ├── CallerPasswordField.tsx            (M) o campo por modo
│   │   ├── SignInFormsCard.tsx                (M) as linhas, o desconectar pelo Admin (D5)
│   │   ├── SignInFormsCard.test.tsx           (M) lista do topo + casos novos
│   │   ├── InviteForm.tsx, RenewInviteForm.tsx, UserDataCard.tsx, UserTwoFactorCard.tsx   (M)
│   │   └── UserDataCard.test.tsx, UserTwoFactorCard.test.tsx                             (M) lista do topo
│   ├── pages/UsersPage.tsx, UsersPage.test.tsx (M) os textos da M3
│   └── test/fakeUsersApi.ts                   (M) duplo: contasConectadas nos UserDetail
├── features/applications/
│   ├── api/applicationsApi.ts                 (M) a confirmação em createApplication e generateSecret
│   ├── pages/ApplicationCreatePage.tsx        (M) o modo
│   └── components/ApplicationCredentialTab.tsx (M) o modo
└── features/identity/
    ├── api/identityApi.ts                     (M) getFederatedProviders e o tipo
    ├── components/FederatedProvidersCard.tsx  (N) o card e os três estados (D7)
    ├── components/FederatedProvidersCard.test.tsx (N)
    └── pages/IdentityPage.tsx, IdentityPage.test.tsx (M) o card, o subtítulo
```

Nada em `libs/`: tudo é do `apps/frontend`.

## Risks / Trade-offs

- **[A aba aberta de quem o Admin desconectou funciona até 15 min]** → é o access token já emitido; as sessões caem
  na hora. A frase "saiu de todos os navegadores" é a da redefinição da verificação de hoje, com a mesma semântica.
  Verificável: o caso do D6 (a renovação com `invalid_grant` leva a `/entrar?motivo=sessao-expirada`).
- **[O modo errado com `GET /account` em erro]** → o Admin sem senha vê "Sua senha"; o `401` "Código inválido."
  relê a conta e troca o campo (D2). Verificável: caso próprio.
- **[O `fakeAccountApi` com `GET /account` por padrão]** → um caso que hoje dependa da rota sem resposta mudaria em
  silêncio. Verificável: a comparação por arquivo e nome contra a baseline, e a busca por `GET /account` nos testes
  antes de mudar o duplo (hoje só `AccountPage.test.tsx` e `accountApi.test.ts` a declaram, e os dois a sobrepõem).
- **[A volta que cai no `/entrar`]** (nenhum provedor configurado, ou o state ilegível) → a pessoa logada vê o aviso
  de falha do login. É o backend (D1); a tela não corrige. Issue **#281** (Q10); sem cenário nesta change, porque a
  tela não tem como distinguir.
- **[Duas contas do mesmo tipo]** → dois provedores do tipo `google` configurados dão duas linhas "Google"; o `tipo`
  repetido em `formasDeEntrada` sai uma vez só no chip de Usuários (o backend deduplica). Registro.

## Baseline

No worktree limpo `../buteco-agentes-279-baseline`, em `9ec078c`, pelo `~/.cache/buteco-agents/bin/suite-279.sh`
(cópia do `suite-190.sh`, só o cabeçalho muda), com o `rabbitmq_1` de pé (o healthcheck TCP). Saídas em
`~/.cache/buteco-agents/279/baseline/`.

| suíte | resultado | duração | estado (início → fim) | CPU mínima |
|---|---|---|---|---|
| `apps/frontend` | **2733/2733**, 202 arquivos | 264 s | load 4,99, CPU 100 → load 32,88, CPU 46 | 41 |

- **APROVADA** na primeira rodada, com o `MAXWORKERS` padrão (regra assimétrica da #181).
- `nomes.txt` em `~/.cache/buteco-agents/279/baseline/`: 2733 linhas, por arquivo e nome.
- **Os arquivos formatados pelo Prettier no `main`** (0.4), entre os que a change toca: `SignInFormsCard.tsx`,
  `UserDataCard.tsx`, `UserTwoFactorCard.tsx`, `UserDetailPage.tsx`, `applicationsApi.ts`, `ApplicationCreatePage.tsx`
  e `ApplicationCredentialTab.tsx`. Os outros recebem só as edições da change; os novos saem formatados.

**Por que só essa suíte:** tudo mora no `apps/frontend`; as rotas do `apps/api` não mudam (dado que falte é achado,
convenção 1, e aí a suíte do `apps/api` entra).

## Decisões do dono (14/11/2026)

O design foi aprovado com as recomendações das Q1 a Q10.

| | pergunta | decisão | onde |
|---|---|---|---|
| Q0 | A versão do protótipo | a **versão 82**, id de referência `1794678488-1a7d` (a 80 era `1794672097-0d9e`, a 81 `1794673056-98cb`); citar quadro e estado; outro id vale com o mesmo conteúdo | Context, D8, D9 |
| Q1 | Uma change ou duas | uma | D10 |
| Q2 | O modo com a conta pendente ou em erro | `'senha'`, e a releitura no `401` "Código inválido." | D2 |
| Q3 | O texto do `sessao-expirada` | "A sua conta mudou enquanto você estava no Google: a senha, a verificação ou o acesso. Tente conectar de novo." | D4 |
| Q4 | O `desconectado` | "Conta Google desconectada." para todos (o quadro 80) | D3 |
| Q5 | Os textos só do quadro | como no quadro `m3` | D3, D5, D7 |
| Q6 | O código nos seis formulários | a ajuda do conectar nos seis, "Informe o código.", e a promoção com o código; o campo segue `MinhaConta/confirmar-codigo` | D2, D8 |
| Q7 | "Trocar senha" na linha Senha | só no card Senha; a linha com "—" (o quadro 80) | D3 |
| Q8 | Vínculos | "1 usuário"; "`<n>` usuários" no resto, inclusive o 0 | D7 |
| Q9 | O nome do provedor | o `nome` e a `chave` em mono | D7 |
| Q10 | A volta do conectar no `/entrar` | **#281** (bug, `app: api`, M3, ligada à #196, sem bloquear a #279) | D1, Riscos |

**Na aprovação das specs:** aceito o card que só aparece com provedor ou conta conectada, **e o aviso da volta ou do
desconectar o segura** (D3); o `409` do desconectar pelo Admin na linha, como em Minha conta (D5, versão 81).

**Na aprovação do fechamento:** aceitos os 4 casos de API com o arranjo novo (na lista do topo), a linha reindentada
do `callerPassword.ts` e os itens de "O que o apply mudou", com duas decisões:
- **a troca de modo chega limpa** (`panel-users-page`, "Senha de quem chama"): depois da releitura, o campo do código
  aparece vazio e sem erro, com a ajuda do código (D2); cláusula, o cenário "A troca para o código chega limpa" e o
  guarda 2.9;
- **a verificação do próprio** em Editar usuário fica com o texto de hoje ("Para trocar de celular, peça a outro Admin
  que redefina a sua verificação."): o quadro estava errado e foi corrigido na versão 82, só no `UsuarioDetalhe`. A
  pendência saiu da 14.2.

## Open Questions

Nenhuma.

## O que o apply mudou

Divergências achadas no apply (convenção 9), nenhuma de comportamento pedido ou de contrato:

1. **Quatro casos de API fora da lista do topo mudaram de arranjo**, sem mudar asserção: as funções passaram a
   receber a confirmação (`{ senha }` ou `{ codigo }`) em vez da senha em texto, e o `tsc` acusou as chamadas. Os
   casos: em `applicationsApi.test.ts`, a linha `secrets` do `it.each` de caminhos e corpos e `o 401 da senha chega
   com o title, sem renovar a sessão`; em `usersApi.test.ts`, as linhas `POST /users/u%201/invitation/renew` (de
   `%s leva o corpo do contrato`) e `POST /users/u%201/password-reset` (de `%s leva o método, o caminho e o corpo do
   contrato`). A chamada passou de `'senha'` a `{ senha: 'senha' }`, e o corpo esperado é o mesmo. Nenhum saiu,
   mudou de nome ou afrouxou: não é parada. Entram na lista do topo como arranjo.
2. **O formulário do conectar aberto também segura o cartão.** O `404` "Provedor não configurado." relê a lista; com
   a lista vazia e sem conta, o cartão sumiria com o aviso, que está no formulário. É a mesma classe da correção do
   dono (o aviso que some com o cartão). Cláusula na spec `panel-federated-accounts`.
3. **O desconectar pelo Admin entra na regra "Uma ação aberta por vez"** de Editar usuário: a confirmação e o `409`
   são ações da página, como as outras; abrir outra fecha o desconectar. Requisito modificado e um cenário na spec
   `panel-user-detail-page`.
4. **As datas das contas:** "Conectada em" só com a data (`formatDate`, o "14/08/2026" do quadro), e "Último uso" no
   formato relativo de Minha conta (`formatRelative`, o "hoje, 09:02").
5. **Os casos do cartão dos provedores** ficaram no `IdentityPage.test.tsx`, que já tem o arranjo da página (o
   `identity()` e o `renderIdentity()`), e não num `FederatedProvidersCard.test.tsx` novo.
6. **Cenários cobertos noutro arquivo, sem caso duplicado:** "A redefinição com o código", "A promoção com o
   código", "Criar com o código" e "Gerar com o código" estão no `callerPassword.test.tsx` (os seis formulários num
   lugar só, os casos dos guardas 2.7 e 2.8); "A ajuda do e-mail da M3" é o caso da lista do topo (reforço, sem `it`
   novo).
7. **Os nomes reais dos casos da lista do topo:** `mostra o título, o subtítulo e os quatro cartões, na ordem`
   (`AccountPage.test.tsx`) e `título, subtítulo e os dois cartões de texto` (`UsersPage.test.tsx`).
8. **O texto da verificação do próprio** em Editar usuário ("Para trocar de celular, peça a outro Admin…") ficou o
   de hoje: o D5 cobre só a verificação de outro usuário. O quadro da 81 dizia "Gerencie a sua em Minha conta.";
   **decidido no fechamento**: fica o texto de hoje, e a versão 82 corrigiu o quadro (`UsuarioDetalhe`, `alvo=proprio`).
9. **Uma linha de indentação no `callerPassword.ts`**, arquivo não formatado no `main`: o `} else if (!outro?.(error)) {`
   do `catch` estava com 8 espaços, e saiu com 6 no bloco que a change reescreveu (o envio com a confirmação). É a
   única diferença entre o diff com e sem `-w` nos arquivos fora da lista da 0.4. Não foi desfeita, para não mudar o
   sha que os guardas 2.6 a 2.8 conferiram.
10. **A troca de modo chega limpa** (decisão do dono no fechamento): o `useCallerPassword` guarda o modo visto e, quando
   ele muda (a releitura depois do `401` "Código inválido."), limpa o erro e o valor, na renderização, sem efeito. O
   caso `O código inválido no modo senha relê a conta` (2.6) perdeu a asserção do "Código inválido." no campo, que a
   decisão inverte, e continua conferindo a releitura e o campo do código; a asserção nova é o caso `A troca para o
   código chega limpa` (2.9). O caso é desta change, não da baseline.

## O fechamento

| suíte | baseline `9ec078c` (`279/baseline/`) | fechamento (`279/fechamento-2/`) | veredito |
|---|---|---|---|
| `apps/frontend` | 2733/2733, 202 arquivos, 264 s, CPU mín. 41 | **2818/2818**, 207 arquivos, 217 s, CPU mín. 34 | APROVADA |

- Uma rodada antes das correções do dono no fechamento (`279/fechamento/`): 2817/2817, 207 arquivos, 188 s, CPU mín.
  39, APROVADA.
- **Comparação por arquivo e nome** (`279/fechamento-2/nomes.txt`): nenhum caso da baseline saiu nem mudou de nome;
  os 7 da lista do topo e os 4 de arranjo aprovados; o rebaixamento de si mesmo (`UserDataCard.test.tsx`) sem
  mudança. Entraram 85: `AccountSignInFormsCard.test.tsx` 25, `callerPassword.test.tsx` 14,
  `federatedReturn.test.tsx` 11, `SignInFormsCard.test.tsx` 11, `IdentityPage.test.tsx` 9, `accountApi.test.ts` 5,
  `usersApi.test.ts` 4, `UsersPage.test.tsx` 2, e 1 em `AccountPasswordCard.test.tsx`, `AccountPage.test.tsx`,
  `federatedDisconnectSession.test.tsx` e `identityApi.test.ts`. Contra a primeira rodada, só entrou `A troca para o
  código chega limpa`. Duas linhas do `it.each` `POST /account/federated/google/connect leva só o campo da
  confirmação` têm o mesmo título (a senha e o código): por nome distinto são 84.
- **Os 15 guardas** (`279-guardas/`), cada um reprovando pelo caso nomeado e o arquivo voltando com o sha igual; a
  2.6 rodada de novo contra o código corrigido (`2.6b`: reprovam `O código inválido no modo senha relê a conta` e `A
  troca para o código chega limpa`), e a 2.9 (`A troca para o código chega limpa`).
- **Lint** sem aviso; **build** com o mesmo aviso de chunk da baseline (`index` de 1.229,16 kB para 1.244,06 kB).
- **Convenção 18** (só `apps/frontend/src`):

  | | projetado (D10) | medido | diferença |
  |---|---|---|---|
  | produção | ~+1.055 | +1.158 / −83 (26 arquivos; 946 de lógica, 152 de comentário, 60 em branco) | +10 % |
  | testes (sem duplos) | ~+2.000 | +1.397 / −19 (15 arquivos) | −30 % |
  | duplos | +100 | +79 / −1 (3 arquivos) | −21 % |
  | casos | ~127 | 85 | −33 % |

  O custo por caso acertou (~16 linhas); o erro foi a contagem de casos por componente (Identidade 15 → 10, API
  14 → 10).
