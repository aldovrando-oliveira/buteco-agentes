## Context

A #190 é a **190a** da M3 (#196): a entrada pelo Google no painel (o login, as recusas no login e o aceite de
convite com provedor). As contas (Minha conta, a confirmação com código, Editar usuário, Usuários e Identidade)
saíram para a **#279** (Q1, decisão do dono de 14/11/2026). O backend está no `main` (`0f44c10`): a #189, a #274
e as correções #276 e #277. A 190a é só tela.

**Fontes lidas** (13–14/11/2026, `0f44c10`):
- o corpo e os comentários da #190, inclusive o contrato do token do convite que a #274 deixou
  ([comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/190#issuecomment-6869086154));
- os designs arquivados `2026-11-13-provedores-federados-google` e `2026-11-14-aceite-convite-google`;
- as specs vivas `federated-providers`, `federated-login`, `panel-issuer-session`, `operator-login-ui` e
  `panel-access-link-pages` (e, para a #279, `user-account`, `user-management`, `panel-roles`,
  `frontend-app-shell`);
- os quadros do canvas `H7dPBZafiSkdKd1hqesPZ6` pela ferramenta Artifact: a primeira leitura na versão
  `1794615840-7c9e`; **o h) refeito na versão `1794626941-64ac`** (a "versão 77" do dono; a 75 era `1794625429-e7ff`), nos quadros `Main`
  (`entrega=m3`), `LoginRecusado` e `AceitarConvite`.

### O código do painel em `0f44c10`

- **`/entrar`** (`features/auth/pages/EntrarPage.tsx`): as etapas `credenciais`, `codigo` e `cadastro`; a etapa
  inicial sai do `state` do router (as páginas do link), não da query. Depois do `204`, o `continueEntry`
  (`continueEntry.ts`) segue o `retorno` conferido (`/connect/authorize?…`) ou começa um fluxo novo. O "Voltar"
  do cadastro chama o `/connect/logout`. Os avisos verdes de "Senha salva…" (`messages.ts` l. 22–30) são o molde
  dos avisos de volta do Google.
- **`/convite`** (`features/auth/pages/AccessLinkPage.tsx`): o token sai do fragmento e **já fica no
  `sessionStorage`, na chave `buteco.link.convite`** (`linkToken.ts` l. 12–15, #245 D4; spec
  `panel-access-link-pages`), apagado no `204`, no `404` e ao desmontar. Uma navegação de página inteira
  (`location.assign`) não roda a limpeza do React: o token já sobrevive à ida ao Google e à volta.
- **O proxy do Vite** leva `/auth` e `/connect` ao `apps/api` (`vite.config.ts` l. 38–40): a volta do Google a
  `http://localhost:5173/auth/federated/callback`, uma navegação de topo, passa pelo mesmo proxy.

## Goals / Non-Goals

**Goals:** o D1, o D2 e o D3, sobre as rotas que o backend já serve; testes do Vitest por tela e por estado; a
validação visual na homologação.

**Non-Goals:** backend (dado que falte é achado, convenção 1); **as contas, na #279** (Minha conta "Formas de
entrada", a confirmação com o código para quem não tem senha, Editar usuário, Usuários, Identidade); o SSO
corporativo e as aplicações federadas (M4); o `papel` na consulta do convite (Q9 da #274).

## Decisions

### D1. (a) O login

**Os botões.** O `/entrar` lê `GET /auth/federated/providers` (anônima, `{ provedores: [{ chave, tipo }] }`) na
montagem. Com a lista vazia, nada muda. Com provedores: abaixo do formulário de senha, o separador "ou entre
com" e um botão por provedor, "Entrar com Google" (Q6). O rótulo sai de um mapa fixo por `tipo`; **um tipo
desconhecido não ganha botão** (convenção 13). **A lista falhando** (rede, resposta que não é `200`, ou um `200` fora do formato: sem
`provedores`, `provedores` que não é lista, itens sem `chave` ou `tipo` em texto) esconde o bloco, sem aviso e sem
quebrar a página: a senha continua valendo (Q5, convenção 13).

**O início.** O clique chama `POST /auth/federated/{chave}/start` com `{ retorno }`, o `retorno` que o authorize
mandou ao `/entrar` (já conferido pelo prefixo `/connect/authorize?`; sem ele, `null`):

| resposta | a tela |
|---|---|
| `200 { url }` | "Redirecionando para o Google…" no cartão, e a navegação ao `url` na hora, sem "Cancelar" (Q7) |
| `503` | aviso vermelho "Não foi possível falar com o provedor. Tente de novo em instantes." |
| `404` com o `title` **exatamente** "Provedor não configurado." (`FederatedLoginEndpoints.NotConfiguredMessage`) | aviso vermelho "Provedor não configurado."; a lista é relida, e o botão some com ela; a senha continua |
| qualquer outro `404` (sem corpo, outro `title`, o `404` do nginx) | aviso vermelho "Não foi possível entrar com o Google. Tente de novo."; a lista **não** é relida |
| `429` | aviso amarelo "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." |
| rede | aviso vermelho "Não foi possível falar com o servidor." |
| outro (`403`, `415`, `5xx`) | aviso vermelho "Não foi possível entrar com o Google. Tente de novo." |

**A volta.** O retorno responde `302 /entrar?federado=<chave>&segundoPasso=totp|cadastro[&convite=aceito][&retorno=…]`
com o `buteco-sessao` pendente. O `/entrar` passa a ler a etapa **também da query**:

| query | a etapa | o aviso verde acima dela |
|---|---|---|
| `segundoPasso=totp`, sem `convite` | o passo do código | "O Google confirmou a sua conta. Falta o código do app autenticador para entrar." |
| `segundoPasso=cadastro`, sem `convite` | o cadastro | "O Google confirmou a sua conta. Falta só cadastrar o app autenticador para entrar." |
| `segundoPasso=cadastro&convite=aceito` | o cadastro | **só** "Convite aceito. Falta cadastrar o app autenticador para entrar." (Q12; não soma com o anterior) |

Com `federado` e um `segundoPasso` fora de `totp` e `cadastro`, a página abre o e-mail e senha, sem aviso, e limpa a
query do mesmo jeito (convenção 13). Daí em diante, tudo é o de hoje: o `second-factor`, o `setup`/`confirm`, o
`continueEntry` com o `retorno`.
**Depois de ler**, a página troca o endereço por `replace`, sem `federado`, `segundoPasso` e `convite`, e **com o
`retorno`** (Q4): recarregar volta ao e-mail e senha, como o `state` do cadastro já é limpo ao ser lido (#220).
Com `convite=aceito`, a página **apaga a chave `buteco.link.convite` antes de abrir o cadastro** (D3).

**Descartado:** o painel chamar o Google direto (o D1 da #189 já fixou o Google atrás do emissor); um "Cancelar"
no redirecionamento.

### D2. (b) As recusas no login

O retorno recusado responde `302 /entrar?recusa=<motivo>&provedor=<chave>` (a `falha` do aceite com o state
ilegível vem sem `provedor`), sem sessão. O `/entrar` mostra o aviso acima do formulário e troca o rótulo do botão
nos motivos marcados:

| motivo | cor | título | texto | botão |
|---|---|---|---|---|
| `sem-acesso` | vermelho | "Conta sem acesso" | "O Google confirmou `<e-mail>`, mas essa conta não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador." | "Entrar com outra conta Google" |
| `sem-acesso`, **sem o e-mail** | vermelho | "Conta sem acesso" | "Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador." | "Entrar com outra conta Google" |
| `convite-pendente` | azul | "Você tem um convite pendente" | "Abra o link do convite que você recebeu para ativar o acesso. Se o link expirou, peça um novo ao administrador." | "Entrar com Google" |
| `nao-conectada` | amarelo | "Esta conta Google ainda não está conectada" | "Entre com seu e-mail e senha e conecte o Google em Minha conta → Formas de entrada. Depois disso, o botão do Google passa a funcionar." | "Entrar com Google" |
| `desativado` | vermelho | "Acesso desativado" | "Seu acesso ao Buteco Agentes foi desativado. Fale com o administrador do painel." | "Entrar com Google" |
| `email-nao-verificado` | amarelo | "E-mail não confirmado" | "O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou com e-mail e senha." | "Entrar com outra conta Google" |
| `dominio-nao-permitido` | vermelho | "Domínio não permitido" | "Esta conta Google não é do domínio permitido neste painel." | "Entrar com outra conta Google" |
| `falha` e **todo motivo desconhecido** (convenção 13) | vermelho | — (sem título) | "Não foi possível entrar com o Google. Tente de novo." | "Entrar com Google" |

**O e-mail confirmado.** Só o `sem-acesso` o põe no texto. Ele vem de **uma** chamada a
`POST /auth/federated/refusal` (JSON, mesma origem; o cookie `Strict` vai porque o `fetch` parte do documento do
painel), feita com a recusa `sem-acesso` na query: `200 { motivo, provedor, email }` → a variante com o e-mail;
`404` (o cookie já lido numa recarga, ou depois de 120 s), qualquer outra resposta ou falha de rede → a variante sem o
e-mail. **O e-mail só vale para a recusa certa:** o `motivo` da resposta igual ao da query e, quando a query trouxe
`provedor`, o `provedor` também; fora disso, a variante sem o e-mail. O backend grava o `buteco-recusa` em três
motivos do login (`sem-acesso`, `nao-conectada` e `convite-pendente`) e no `outro-email` do convite, e cada página lê
só um: sem a conferência, o cookie de
uma recusa que ficou sem ser lida emprestaria o e-mail a outra, dentro dos 120 s (corrigido no apply). **A chamada é
única por montagem**, com uma guarda contra o efeito duplo do `StrictMode`: a segunda chamada responderia `404` e
apagaria o e-mail. Os outros motivos não ganham variante (o texto não leva e-mail), e por isso não chamam a rota.

**Depois de ler**, o endereço perde `recusa` e `provedor` por `replace`, e mantém o `retorno` (Q4).

**Descartado:** uma página por recusa (o corpo já descartou); deixar a recusa na URL (a recarga repetiria o aviso,
e um link compartilhado o levaria a outra pessoa); chamar a rota do aviso nos motivos que não usam o e-mail.

### D3. (c) O aceite de convite com provedor

**Os botões e o "ou crie uma senha".** Com o convite válido (a consulta de hoje) e provedores configurados (a
mesma lista do D1), a página mostra o cabeçalho de hoje, sem o papel ("Olá, `<nome>`", "`<quem convidou>`
convidou você para o **Buteco Agentes**."), e, acima do formulário de senha: o título "Entre com uma conta
Google", o botão "Continuar com Google", a nota "A conta precisa ter o e-mail `<e-mail do convite>`." e o
separador "ou crie uma senha". Sem provedor, a página é a de hoje (o quadro `AceitarConviteSemProvedor`).

**O início.** `POST /auth/invitation/federated/{chave}/start` com `{ token }`:

| resposta | a tela |
|---|---|
| `200 { url }` | "Redirecionando para o Google…" no lugar dos botões e do formulário, e a navegação |
| `404` com o `title` **exatamente** o do link inválido (`AccessLinkPublicEndpoints.InvalidLinkMessage`) | o cartão de link inválido de hoje, e a chave apagada |
| `404` com o `title` **exatamente** "Provedor não configurado." | aviso vermelho "Provedor não configurado."; a lista relida esconde o bloco; o formulário continua |
| qualquer outro `404` (sem corpo, outro `title`, o `404` do nginx) | aviso vermelho "Não foi possível entrar com o Google. Tente de novo."; a chave **não** é apagada, e a lista **não** é relida |
| `503` | aviso vermelho "Não foi possível falar com o provedor. Tente de novo em instantes." |
| `429` | aviso amarelo "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." |
| rede e outro | como no D1 |

**O token na aba** (o contrato da #274 com a chave da Q2):
- **onde:** `sessionStorage`, chave **`buteco.link.convite`**, a que a página já usa;
- **gravado:** ao abrir o link (`useLinkToken`, como hoje) **e, sempre, no clique do provedor**, com o token do estado
  da página, antes do `location.assign` (decisão do dono, 14/11/2026): sem isso, uma segunda tentativa depois de uma
  recusa (que já apagou a chave) iria ao Google sem a chave, e uma segunda recusa cairia na volta sem token;
- **apagado no aceite:** o `/entrar` com `convite=aceito` apaga a chave **antes** de abrir o cadastro (D1);
- **apagado na recusa:** a volta a `/convite?recusa=…` lê o motivo, faz a consulta do convite (e, no
  `outro-email`, a leitura do aviso) e **apaga a chave**; o token que o formulário de senha e uma nova tentativa
  ainda precisam fica no estado da página. **Recarregar depois disso** (sem fragmento, sem a chave e sem `recusa`,
  que a limpeza tirou) mostra o estado de link inválido de hoje, "Este link não vale mais", sem pedido (decisão do
  dono, 14/11/2026; ver Riscos);
- **nunca:** na URL (nem query, nem fragmento recolocado), em log, em mensagem de erro, no `localStorage` ou em
  cookie.

**As recusas que voltam ao convite** (`/convite?recusa=<motivo>&provedor=<chave>`), acima dos botões, com o
convite e o formulário valendo:

| motivo | cor | título | texto |
|---|---|---|---|
| `outro-email` | vermelho | "E-mail diferente do convite" | "O Google confirmou `<e-mail do provedor>`, mas este convite é para `<e-mail do convite>`. Entre com a conta certa ou crie uma senha abaixo." |
| `email-nao-verificado` | amarelo | "E-mail não confirmado" | "O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou crie uma senha abaixo." |
| `em-uso` | vermelho | "Conta Google já conectada" | "Esta conta Google já está conectada a outro usuário do Buteco Agentes. Entre com outra conta ou crie uma senha abaixo." |
| `dominio-nao-permitido` | vermelho | "Domínio não permitido" | "Esta conta Google não é do domínio permitido neste painel." |
| `falha` e todo motivo desconhecido | vermelho | — | "Não foi possível entrar com o Google. Tente de novo." |
| `convite-invalido` | — | o cartão de link inválido de hoje ("Este link não vale mais"); a consulta também responde `404` |

**O e-mail do provedor no `outro-email`** vem da mesma rota de uso único do D2 (`POST /auth/federated/refusal`),
chamada uma vez, pelo mesmo hook do D2, com a mesma conferência: o e-mail só vale com o `motivo` `outro-email` e,
quando a query trouxe, o mesmo `provedor`; com outra recusa, a variante sem o e-mail. No convite o hook só liga depois da consulta, quando a fase vira
`formulario`, nunca na montagem, e não religa (a fase continua `formulario` depois de uma resposta recusada do
início): o efeito duplo do `StrictMode` não o alcança, e a guarda de uso único protege contra uma mudança futura que
o faça religar, não contra a montagem (corrigido no apply). Sem o e-mail (o `404`), o aviso sai como o da volta sem
token, abaixo.

**Um novo clique no provedor esconde o aviso da recusa**, como no `/entrar`: a resposta do início que não é o `200`
mostra só o seu aviso, e o aviso da recusa não volta; o `404` do link inválido segue para o cartão de hoje
(decisão do dono, 14/11/2026, no apply).

**A volta sem o token na aba** (outra aba, armazenamento limpo): a página não sabe o convite. Mostra o título
"Convite", o aviso do motivo (no `outro-email`, "E-mail diferente do convite" / "O Google confirmou outro e-mail,
diferente do e-mail deste convite."; nos outros, o da tabela) e "Abra o link do convite que você recebeu de
novo.", **sem formulário e sem pedido**.

Depois de ler, o endereço perde `recusa` e `provedor` por `replace` (Q4).

**Descartado:** uma chave nova `buteco.convite` ao lado da que já existe (Q2); um estado "Conta ativada" em
`/convite` (o aceite vai ao `/entrar`).

### D4. As contas: na #279

Minha conta ("Formas de entrada", conectar e desconectar), a confirmação com o código do app para quem não tem
senha, Editar usuário (o desconectar pelo Admin), Usuários (o "Google") e Identidade (os provedores) saíram para
a **#279** (Q1), com as decisões Q3, Q8, Q9, Q10 e Q11 deste design e a lista dos quadros que faltam.

### D5. (h) O que os quadros desenham, contra o backend (versão `1794626941-64ac`)

No formato do item f da #220. "serve" = o backend serve e a tela usa. Os itens da primeira leitura que a
versão 75 corrigiu estão marcados como **corrigido**; os estados que a versão 77 acrescentou, como **novo (77)**.

**`Main` (`entrega=m3`):**

| item | estado | o que o quadro mostra | o backend / a M3 |
|---|---|---|---|
| a | `inicio` | "ou entre com" e só "Entrar com Google", sem subtítulo | serve; **corrigido** (o subtítulo e o "SSO Empresa" saíram; o SSO só em `entrega=m4`) |
| b | `redirecionando` | "Redirecionando para o Google…", sem "Cancelar" nem texto de volta | serve; **corrigido** |
| c | `recusa-sem-acesso` | "Conta sem acesso", o e-mail, "Entrar com outra conta Google" | serve (`sem-acesso` e o `buteco-recusa`) |
| d | `recusa-sem-acesso-sem-email` | "Conta sem acesso", "Essa conta Google não tem acesso…" | serve (o `404` da rota do aviso) |
| e | `recusa-convite-pendente`, `recusa-nao-conectada`, `recusa-desativado`, `recusa-email-nao-verificado`, `recusa-dominio-nao-permitido` | os títulos, os textos, as cores e o botão da tabela do D2 | serve |
| f | `recusa-falha`, `recusa-desconhecida` | sem título, "Não foi possível entrar com o Google. Tente de novo." | serve |
| g | `google-indisponivel` | aviso vermelho "Não foi possível falar com o provedor…" | serve (`503`) |
| h | `google-nao-configurado` | aviso vermelho "Provedor não configurado.", sem o bloco dos provedores | serve (`404`) |
| i | `google-muitas-tentativas` | aviso amarelo "Muitas tentativas ao mesmo tempo…" | serve (`429`) |
| i2 | `google-falha-de-rede` | aviso vermelho "Não foi possível falar com o servidor." | serve (a rede); **novo (77)** |
| i3 | `google-outra-resposta` | aviso vermelho "Não foi possível entrar com o Google. Tente de novo." | serve (o resto, inclusive o `404` sem o `title` conhecido); **novo (77)** |
| j | `mfa-vindo-do-google`, `cadastro-vindo-do-google`, `cadastro-convite-aceito` | os três avisos verdes do D1 | serve (a query da volta) |
| k | `inicio`, rodapé | "Ainda não tem acesso? Peça um convite ao administrador do painel." | já na tela |

**`LoginRecusado`:** os oito cartões (1, 1b, 2 a 7), sem o "Acesso Acme" (**corrigido**), batem com a tabela do D2
e com o `Main`. Serve.

**`AceitarConvite` (`entrega=m3`):**

| item | estado | o que o quadro mostra | o backend / a M3 |
|---|---|---|---|
| a | `escolha` | o cabeçalho sem o papel | serve; **corrigido** |
| b | `escolha` | "Entre com uma conta Google", "Continuar com Google", a nota do e-mail, "ou crie uma senha" | serve; **corrigido** (o SSO só em `entrega=m4`) |
| c | `redirecionando` | "Redirecionando para o Google…" | serve; **corrigido** |
| d | `provedor-indisponivel` | aviso vermelho do `503` | serve |
| e | `provedor-nao-configurado` | aviso vermelho "Provedor não configurado.", sem o bloco, com o formulário | serve |
| e2 | `provedor-muitas-tentativas` | aviso amarelo "Muitas tentativas ao mesmo tempo…" | serve (`429`); **novo (77)** |
| e3 | `provedor-falha-de-rede` | aviso vermelho "Não foi possível falar com o servidor." | serve (a rede); **novo (77)** |
| e4 | `provedor-outra-resposta` | aviso vermelho "Não foi possível entrar com o Google. Tente de novo." | serve (o resto, inclusive o `404` sem o `title` conhecido); **novo (77)** |
| f | `recusa-outro-email`, `recusa-email-nao-verificado`, `recusa-em-uso`, `recusa-dominio-nao-permitido`, `recusa-falha` | os avisos da tabela do D3, com o formulário | serve |
| g | `recusa-convite-invalido` | o cartão "Este link não vale mais" de hoje | serve |
| h | `recusa-sem-token-outro-email`, `recusa-sem-token-falha` | "Convite", o aviso e "Abra o link do convite que você recebeu de novo.", sem formulário | serve (o painel, sem pedido) |

### D6. (i) Validação visual

A regra da M1 e da M2 (decisão do dono, 08/11/2026): o checklist vai no `tasks.md`, no `02` e no PR; a validação
é na homologação da M3, quadro a quadro contra a versão `1794626941-64ac`, **sem prender o PR**. A conferência
com o Google real fica para a homologação, depois do merge, com o roteiro da #189 (13.2 a 13.8) e da #274 (10.2 a
10.5), **agora pelo painel**, e não bloqueia o PR. Achado de homologação vira issue nova ligada à entrega
(convenção 23).

### D7. (j) Tamanho (convenção 18)

**Âncoras** (só `apps/frontend/src`): a **#185**, produção +1.157 / −324 (52 arquivos), testes +1.353, duplos
+77, 140 casos; a **#188**, produção **+2.708** (projetada ~1.620, **+67 %**), testes +1.945, duplos +284, 137
casos. **A régua da #188:** linhas já formatadas pelo Prettier, e cada tabela com ação e confirmação na linha
contada à parte (a 190a não tem nenhuma). Unidade: linha; casos à parte, unidade **caso** (cada `it` e cada
linha de `it.each`).

| componente | produção | testes | casos | duplo |
|---|---|---|---|---|
| a API federada do painel: a lista, os dois inícios, a rota do aviso, os tipos | +90 | +130 | 10 | — |
| o login: os botões, o tipo desconhecido, a lista falhando e fora do formato, o início e as sete respostas (os dois `404` pelo `title`), o redirecionamento (D1) | +220 | +310 | 21 | — |
| a volta: a etapa pela query, o `segundoPasso` desconhecido, os três avisos, a limpeza com o `retorno`, o token apagado no aceite (D1) | +125 | +225 | 13 | — |
| as recusas no login: as oito variantes, o desconhecido, a leitura única com a guarda, a falha de rede na leitura, a limpeza (D2) | +180 | +340 | 23 | — |
| o convite com provedor: os botões, a lista fora do formato, o início e as respostas (os três `404` pelo `title`), a chave regravada no clique, o redirecionamento (D3) | +175 | +300 | 18 | — |
| as recusas no convite: as seis, o e-mail do provedor, o token apagado, a volta sem token, a recarga, a segunda tentativa (D3) | +175 | +320 | 18 | — |
| **o duplo**: as rotas federadas no duplo de `fetch` dos testes do `auth` (a lista, os inícios, o aviso de uso único), e o `respond()` do `EntrarPage.test.tsx` com a lista à parte | — | — | — | **+120** |
| **total** | **~+965** | **~+1.625** | **~103** | **+120** |

Com o erro da #188 na produção (+67 %), até ~1.610; nos testes (−10 %), ~1.460 a 1.625. **Cabe numa change**:
abaixo da #188 e perto da #185, sem tabela com confirmação na linha. Rascunho: a projeção conta antes da
verificação.

### D8. (k) Premissas falsas (convenção 9)

Nenhum dado que a tela da 190a precise falta no backend: os três quadros da versão 75 só mostram o que o D5 marca
como "serve". Registros:

1. **O corpo, "As quatro recusas na tela de login"**: são sete motivos no login (#189, D7), mais a variante sem
   e-mail e o motivo desconhecido, e seis na volta ao convite (#274, D3). Registro.
2. **O corpo, "Telas do protótipo: Minha conta — formas de entrada" e "Identidade — provedores federados"**: foram
   para a #279. Registro.
3. **O comentário da #274 na #190, "chave `buteco.convite`, ao lado de `buteco.login` e `buteco.sessao`" e
   "gravado no clique do botão do provedor"**: o painel já guarda o token do convite em `buteco.link.convite`
   (`apps/frontend/src/features/auth/linkToken.ts` l. 12–15, #245 D4), gravado ao abrir o link; e o
   `buteco.sessao` não fica na aba, fica no `localStorage` (`apps/frontend/src/auth/sessionStore.ts` l. 2–9; spec
   `panel-issuer-session`, "A sessão do navegador fica num registro só no localStorage"). **Decidido pelo dono
   (Q2, 14/11/2026):** a chave que já existe, com o resto do contrato (apagada no aceite e na recusa; nunca na
   URL, em log, no `localStorage` ou em cookie). Só o painel lê a chave.
4. **O comentário, "o `outro-email` … depois do `POST /auth/federated/refusal` e da consulta `POST
   /auth/invitation`"**: a consulta é a que a página já faz na montagem; a ordem é a consulta e depois a leitura
   do aviso. Registro.
5. **A primeira leitura do quadro `Main` tinha a prop `federados`**: a versão 75 trocou por `entrega` (`m2`, `m3`,
   `m4`). Registro.

## Risks / Trade-offs

- **[O `StrictMode` lê o aviso duas vezes]** → a segunda chamada a `POST /auth/federated/refusal` responde `404` e
  apagaria o e-mail; a leitura é única por montagem, com guarda, e o teste roda no `StrictMode` (guardas 4.4 e 6.7 do `tasks.md`).
- **[O cookie `Strict` na volta]** → o `fetch` do `/entrar` e do `/convite` é same-site e leva o `buteco-recusa`
  e o `buteco-sessao`; a conferência em Chrome, Firefox e Safari é da homologação (D6), como na #189.
- **[Recarregar depois de uma recusa no convite]** → limitação conhecida, aceita pelo dono (14/11/2026): a recusa
  apaga a chave e a limpeza tira a `recusa` da URL, e a recarga mostra "Este link não vale mais", sem pedido, como
  hoje sem fragmento e sem chave. O link do convite continua valendo: a pessoa o abre de novo. É o contrato da
  #274, a favor de não deixar o token na aba.
- **[Uma segunda recusa seguida]** → coberta: o clique do provedor regrava a chave com o token do estado (D3).
- **[O Admin que entra só pelo Google]** → fica sem a confirmação com o código até a #279.

## Baseline

No worktree limpo `../buteco-agentes-190-baseline`, em `0f44c10`, pelo `~/.cache/buteco-agents/bin/suite-190.sh`
(cópia do `suite-188.sh` com o cabeçalho da #190: o lock, o `MAXWORKERS`, a espera por CPU 100 e load abaixo de
6, a amostragem e o veredito assimétrico), com o `rabbitmq_1` de pé (o healthcheck TCP). Saídas em
`~/.cache/buteco-agents/190/baseline/`.

| suíte | resultado | duração | estado (início → fim) | CPU mínima |
|---|---|---|---|---|
| `apps/frontend` | **2634/2634**, 199 arquivos | 176 s | load 2,87, CPU 100 → load 24,39, CPU 69 | 58 |

- **APROVADA** na primeira rodada, com o `MAXWORKERS` padrão (regra assimétrica da #181).
- `nomes.txt` em `~/.cache/buteco-agents/190/baseline/`: 2634 linhas, por arquivo e nome.

**Por que cada suíte:** `apps/frontend` (tudo mora lá). **Fora:** `apps/api` (a #189 e a #274 servem as rotas;
a #190 não muda rota nem resposta: dado que falte é achado, convenção 1, e aí a suíte do `apps/api` entra);
`apps/inbox` e `apps/connectors` (código intocado); `tests/InboxOrchestratorRoundTrip.Tests` e
`tests/ApiConnectorsRoundTrip.Tests` (não passam pela tela, e o login do emissor e as rotas não mudam);
`tests/CrossAppTaskStoreCompatibility.Tests` (banco entre api e workers); `apps/workers` (sem rota HTTP).

## Decisões do dono (14/11/2026)

As doze aceitas como recomendadas; as da #279 ficam registradas lá.

| | pergunta | decisão | onde |
|---|---|---|---|
| Q1 | Uma change ou duas | duas: a #190 é a 190a (D1 a D3); as contas são a **#279** | D4 |
| Q2 | A chave do token do convite | `buteco.link.convite`, com o contrato do D3 | D3, D8.3 |
| Q3 | A confirmação com o código para quem não tem senha | na #279 | #279 |
| Q4 | Os parâmetros da volta na URL | apagados depois de lidos, por `replace`; o `retorno` fica | D1, D2, D3 |
| Q5 | A lista dos provedores falhando | o bloco some, sem aviso | D1 |
| Q6 | O rótulo dos botões | "Entrar com Google", "Continuar com Google", "Entrar com outra conta Google"; sem subtítulo | D1, D2, D3 |
| Q7 | O redirecionamento | sem "Cancelar" | D1, D3 |
| Q8 | Desconectar a última forma | na #279 | #279 |
| Q9 | O vínculo fora da configuração | na #279 | #279 |
| Q10 | A pessoa sem senha em Minha conta | na #279 | #279 |
| Q11 | As colunas da Identidade | na #279 | #279 |
| Q12 | O aviso do convite aceito | no `/entrar`, acima do cadastro, sozinho | D1 |

**Os textos** (convenção 17, decisão do dono de 14/11/2026): os títulos "E-mail não confirmado", "Domínio não
permitido" e "Conta Google já conectada"; a `falha` sem título; a variante sem e-mail do `sem-acesso`; os avisos
verdes da volta do Google; o `429` em amarelo; o `404` com o aviso e a lista relida; "Entre com uma conta Google"
no convite; o cabeçalho sem o papel; a volta sem token do `outro-email`. O resto, como no D2 e no D3.

## Open Questions

Nenhuma.

## O que o apply mudou

Divergências achadas no apply (convenção 9), nenhuma de comportamento ou contrato:

1. **Duas consultas do link sob o `StrictMode`.** O efeito duplo de montagem faz a consulta duas vezes (o código
   de hoje, da #245, não tem guarda nela). A lista exata do caso do `StrictMode` (aprovada pelo dono) conta as
   duas, depois a lista dos provedores; nenhuma URL leva o token, e a chave continua gravada.
2. **A ordem dos pedidos no `/entrar` depois do aceite:** o `GET /auth/session/enrollment/setup` (efeito do
   filho, o cadastro) vem antes do `GET /auth/federated/providers` (efeito da página). As listas exatas seguem a
   ordem medida.
3. **Os hooks num arquivo próprio:** `useLoginProviders` e `useRefusalEmail` foram para `federatedHooks.ts`,
   porque a regra `react-refresh/only-export-components` do lint reprova hook e componente no mesmo arquivo
   (`components/FederatedParts.tsx` ficou só com componentes). A lista dos provedores usa o `fetch` direto: o `features/auth` não tem helper de
   `GET` (os outros pedidos anônimos são todos `POST`, pelo `postJson` do `sessionApi.ts`).
4. **O Prettier só nos arquivos que já estavam formatados no `main`** (a regra da #185): um `prettier --write`
   inicial em `AccessLinkPage.tsx`, `EntrarPage.tsx`, `linkToken.ts` e `EnrollmentForm.tsx`, que não estavam,
   foi desfeito (os arquivos voltaram ao `main` e só as edições da change foram reaplicadas). Os hunks com e sem
   `-w` são os mesmos, exceto no `AccessLinkPage.tsx`, em que o bloco do `NewPasswordForm` ganhou um nível de
   indentação por estar agora dentro da condicional do redirecionamento (mudança de estrutura).
5. **Os casos com a fila do `respondCode` no `EntrarPage.test.tsx`** recebem a lista dos provedores à parte, fora
   da fila, e os que conferem a lista exata de pedidos contam o `GET /auth/federated/providers` (lista do topo).
6. **O `/redefinir-senha` sem a lista:** no `AccessLinkPage.test.tsx`, a lista esperada por página
   (`LISTA`) é vazia na redefinição, que não pede provedores.
7. **A introdução do convite com provedor** (`introComProvedor`) sai sem "Crie sua senha para entrar.", como no
   quadro `AceitarConvite/escolha`; o convite sem provedor mantém a introdução de hoje.
8. **Casos listados que não mudaram:** os que só olham a navegação pelo `authRuntime.assign` passam sem
   mudança, porque o pedido da lista não entra na conta deles; foram conferidos e ficam como no `main`.
9. **O literal do `404` da consulta no `AccessLinkPage.test.tsx`** (o `notFound`, da #245) fica como no `main`:
   é a resposta da consulta, não um dos `404` do início, e trocá-lo mexeria num helper fora da lista do topo. Os
   dois títulos do início vêm só de `START_404_TITLES`, e o `federatedApi.test.ts` os confere com o texto do
   backend (o único lugar em que o texto se repete, de propósito).
10. **O stub do `ConviteFederated.test.tsx`** casa a fila pelo caminho sem a query, para que a mutação do 5.6 (a)
    (`?token=` no início) chegue à rota e reprove pela asserção da URL, não por uma rota sem resposta.
11. **A premissa do D3 sobre a guarda do aviso no convite era falsa.** O D3 dizia "a mesma guarda do D2" contra o
    efeito duplo do `StrictMode`. No convite, o hook só liga quando a fase vira `formulario`, depois da consulta,
    nunca na montagem, e não religa, porque a fase continua `formulario` depois de uma resposta recusada do início.
    A mutação 6.7 (a guarda retirada) passou nos 30 casos e numa sonda do clique com `503` depois da recusa (uma
    chamada à rota do aviso nos dois): é **mutante equivalente**. A guarda do hook, que o login e o convite
    compartilham, continua provada pela 4.4; o caso do `StrictMode` no convite fica como asserção sem guarda; o
    comentário no `AccessLinkPage`, onde o hook liga, diz por que a guarda continua lá.
12. **Os dois avisos juntos depois de um novo clique no convite.** A sonda da 6.7 mostrou que, depois da recusa
    `outro-email`, o clique com `503` deixava na tela o aviso da recusa e o do `503`: o convite não tinha o
    `recusaVisivel` do `/entrar`, e a spec não dizia o que acontecia. Decisão do dono (14/11/2026): igual ao
    `/entrar`, o clique esconde o aviso da recusa, e a resposta do início mostra só o seu. Regra e dois cenários
    novos na spec `panel-access-link-pages`, sete casos novos no `ConviteFederated.test.tsx` e a guarda 6.8.
13. **O e-mail da recusa precisa ser o da recusa certa** (revisão do dono, 14/11/2026). O `readRefusalEmail()` usava o
    `email` da resposta sem olhar o `motivo` nem o `provedor`. Como o backend grava o `buteco-recusa` em mais de um
    motivo, um cookie gravado e não lido (um `nao-conectada`, por exemplo) emprestaria, dentro dos 120 s, o e-mail ao
    aviso do `sem-acesso` no `/entrar` ou do `outro-email` no `/convite`. Agora o `readRefusalEmail()` devolve
    `{ motivo, provedor, email }`, o `useRefusalEmail` recebe a recusa esperada (o motivo e o provedor da query), e o
    e-mail só vale com o motivo igual e, quando a query trouxe, o provedor igual. Regra e cenários nas duas specs,
    casos novos e a guarda 4.8.
