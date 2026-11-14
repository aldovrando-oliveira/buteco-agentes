## Context

A #274 é a segunda das três da M3 (#196): a #189 (provedores, login pelo Google, vínculo) foi mergeada em
`1475d35` (PR #275); a #190 (as telas) vem depois desta. O corpo da #274 traz as decisões já tomadas, que
não se reabrem: o e-mail **verificado** e normalizado igual ao do convite; o consumo pelo mesmo `UPDATE`
único do aceite por senha; o início com o token no corpo de um `POST`, e o state token com o id do
convidado e o **hash** do link, nunca o link; o e-mail confirmado do aviso pelo cookie `buteco-recusa`; a
pessoa sem senha; o aceite já abrindo o login (Q7 da #189); a recusa do e-mail não verificado (Q4).

**Fontes lidas** (13/11/2026, `1475d35`):
- o corpo da #274;
- o design arquivado `2026-11-13-provedores-federados-google` (D1 a D15, as Q1 a Q11);
- as specs vivas `federated-login`, `user-access-links`, `issuer-user-login`, `local-users`;
- o quadro `AceitarConvite` do canvas `H7dPBZafiSkdKd1hqesPZ6`, pela ferramenta Artifact. A listagem dos
  arquivos deu a versão `1794522896-00ec` (a que a #189 leu); a leitura devolveu os arquivos da
  `1794615840-7c9e`. A lista do E é desta última;
- o fonte do OpenIddict 7.7.2 (`~/.cache/buteco-agents/182-fonte/openiddict-core`, tag `7.7.2`).

### O código em `1475d35`

- **O aceite por senha** (`Users/AccessLinks/AccessLinkPublicEndpoints.cs` l. 83–139): as barreiras, a
  política, a busca do link (`FindAsync`, l. 226–243: o hash, o propósito `invitation`, o vencimento e o
  estado convidado) antes do Argon2, e um `ExecuteUpdateAsync` só (l. 116–129), condicionado ao id, ao
  hash, ao propósito, ao vencimento e ao estado, que grava a senha, ativa e apaga o link. Zero linhas é o
  `404` único. Fora de transação. Depois, zera o bloqueio do e-mail (l. 137).
- **O retorno federado** (`Identity/Federated/FederatedLoginEndpoints.cs` l. 119–167) apaga os cookies do
  fluxo, chama o `AuthenticateAsync` do cliente, lê a intenção das propriedades do state token e despacha
  para `LoginAsync` (l. 170–233) ou `ConnectAsync` (l. 236–289). **Uma intenção desconhecida cai no
  login** (l. 164–166: tudo que não é `conectar`).
- **O início** (`FederatedChallenge.BeginAsync`, `FederatedChallenge.cs` l. 20–49) já faz o discovery com
  prazo (o `503`) e o desafio com as propriedades que receber. Serve igual ao aceite.
- **O state token é de referência, e o payload fica no banco.** Com o armazenamento de tokens ligado, o
  cliente gera o state token com `IsReferenceToken` e `PersistTokenPayload`
  (`OpenIddictClientHandlers.cs` l. 5804–5812): o `state` da URL é só a referência, e o token cifrado
  fica em `openiddict_tokens`, onde a limpeza da #209 o tira 14 dias depois de morto. **É por isso que
  ele leva o hash e nunca o link**: o link, mesmo cifrado, seria uma credencial guardada em repouso por
  semanas.
- **O `buteco-recusa`** (`FederatedRefusalCookie.cs` l. 18–31) grava `{ motivo, provedor, email }` com
  qualquer motivo, e a rota que o lê (`POST /auth/federated/refusal`) não depende de onde a pessoa está:
  o cookie vai pelo caminho da requisição, e o `fetch` sai de `/convite` como sai de `/entrar`.
- **O painel sem `retorno`** (`apps/frontend/src/auth/loginFlow.ts` l. 33–48): depois do `204` do segundo
  passo, sem um authorize a seguir, vai à tela inicial, que começa um fluxo novo com o cookie completo. O
  aceite não precisa de `retorno`.
- **O convidado nunca tem a verificação:** ele não entra (`local-users`, l. 23), e o cadastro só aceita o
  cookie pendente de um usuário ativo (`issuer-user-login`, "Cadastro da verificação no login"). O passo
  depois do aceite é sempre o `cadastro`.

## Goals / Non-Goals

**Goals:** o aceite do convite pelo provedor configurado, do link até a sessão pendente; as recusas sem
sessão, de volta ao convite; a concorrência com o aceite por senha; testes sem o Google real, sobre o
`FakeGoogle` e o `GoogleFlow` da #189.

**Non-Goals:** telas (#190); definir a primeira senha sem o link do Admin; outro tipo de provedor (M4); o
`papel` na consulta do convite (Q9); mudar o login pelo Google, o conectar ou o aceite por senha além da
condição de consumo dividida.

## Decisions

### D1. (a) O início do fluxo

**A rota: `POST /auth/invitation/federated/{chave}/start`**, anônima (`AuthEntryPoint`), com as barreiras
do login (só JSON, `Origin` conferida) e no limite global, como o início do login. Corpo `{ token }`.

1. as barreiras; o corpo;
2. a chave fora da configuração → `404` "Provedor não configurado.", sem cookie (o `NotConfigured` da #189);
3. **o convite**, pelo `FindAsync` do aceite por senha (o hash, o propósito `invitation`, o vencimento e o
   estado convidado). Inválido → o **mesmo `404` único** dos links ("Este link não vale mais…"), sem
   cookie e sem ir ao Google;
4. o discovery com o prazo de 10 s (`503`, a regra da #189);
5. o desafio, com as propriedades `buteco_intencao` = `convite`, `buteco_usuario` = o id do convidado e
   `buteco_link` = o hash do link em base64url; sem `retorno`;
6. `200 { url }` com o `Set-Cookie` do fluxo, como o início do login.

A chave antes do convite porque não custa banco e não revela nada (a lista dos provedores é pública em
`GET /auth/federated/providers`).

**O que o state token leva, e por quê:** o id (para reler o convidado no retorno) e o hash (para conferir
que o link é o mesmo, e não um substituto gerado no meio). O hash, e não o link, porque o payload do state
token fica gravado (Context). Com o hash não se aceita o convite por senha: o aceite por senha calcula o
hash do token recebido.

**Descartado: o `{ convite }` no corpo do `POST /auth/federated/{chave}/start` do login.** Mistura duas
rotas com regras diferentes (o `retorno` do login, que aqui não existe; o `404` do link, que lá não
existe), e o `RouteAuthenticationTests` e a spec passariam a descrever uma rota com dois contratos. A rota
própria fica ao lado do `/auth/invitation/accept` e sob o `/auth` que o nginx já leva ao `apps/api`.

### D2. (b) O retorno: a ordem das conferências

Com a intenção `convite` no state token, o retorno vai a um `InvitationAsync`, ao lado do `LoginAsync` e
do `ConnectAsync`. A primeira que casa decide:

| # | conferência | se falha |
|---|---|---|
| 0 | o cliente OIDC (state, correlação, PKCE, nonce, assinatura, `aud`, `iss`), o provedor ainda na configuração, o `sub` presente | `falha` |
| 1 | o `HostedDomain`, quando configurado (Q8 da #189: filtro de todo retorno) | `dominio-nao-permitido` |
| 2 | **o convite ainda pendente e com o mesmo hash**: o usuário do state token existe, está convidado, e o link gravado tem o hash do state token, o propósito `invitation` e não venceu | `convite-invalido` |
| 3 | `email_verified` igual a `true` | `email-nao-verificado` |
| 4 | o e-mail do provedor, normalizado, igual ao `NormalizedEmail` do convidado lido no 2 | `outro-email` |
| 5 | o par (`issuer`, `sub`) sem vínculo | `em-uso` |
| 6 | **o consumo**, numa transação: o `UPDATE` com a condição do aceite por senha (o id, o hash, o propósito, o vencimento, o estado convidado; mais o e-mail normalizado, Q5) que ativa e apaga o link, **sem gravar senha**; zero linhas → rollback e `convite-invalido`. Depois, o `INSERT` do vínculo; `23505` → rollback e `em-uso`. Commit | `convite-invalido` ou `em-uso` |
| 7 | depois do commit: zera o bloqueio do e-mail (Q6), o log `1203`, a sessão pendente (`IssuerSession.SignInAsync(…, twoFactorDone: false)` com o usuário relido) e `302 /entrar?federado=<chave>&segundoPasso=cadastro&convite=aceito` (Q7) | — |

**O convite (2) antes do e-mail (3, 4)** (recomendação, Q4): com o convite morto, "entre com a conta
certa ou crie uma senha" é conselho que não serve (a tela ofereceria o formulário de um link que não vale
mais); e a leitura é a mesma que o 4 precisa para comparar o e-mail. **O verificado (3) antes do igual
(4)**: um e-mail que o provedor não confirmou não diz nada, nem igual nem diferente. **O e-mail (4) antes
do par (5)**: com o e-mail errado, a pessoa troca de conta de qualquer jeito; o `em-uso` é o caso raro.
**Descartado:** a ordem da lista do pedido (o e-mail, o par, o convite), que daria `outro-email` a um
convite cancelado.

**A releitura do 2 não dispensa a condição do 6.** O 2 escolhe o motivo; o 6 decide. Entre os dois, um
aceite por senha, um link novo, o cancelamento ou o vencimento fazem o `UPDATE` voltar zero linhas.

**A condição do consumo é uma só para os dois aceites.** O `UPDATE` do aceite por senha (l. 116–129)
passa a montar o `WHERE` pelo mesmo método que o do Google (`InvitationConsumption`, nome de trabalho);
o `SET` difere só na senha. O aceite por senha continua fora de transação (não grava mais nada); o do
Google precisa dela por causa do `INSERT`.

**O passo é sempre o `cadastro`** (Context: o convidado nunca tem a verificação). O código calcula o passo
como o login (`IsTwoFactorEnabled`), e o teste afirma o `cadastro`.

### D3. (c) As recusas

Todas: **nenhum `buteco-sessao`**, os cookies do fluxo apagados (já é assim no começo do retorno),
`302 /convite?recusa=<motivo>&provedor=<chave>` (Q1) e uma linha `Warning` `FederatedInvitationRefused`
(EventId **1202**, Q8) com a chave, o motivo e o id do convidado do state token. **Nunca** e-mail, `sub`,
código, state, token nem hash do link no log.

| motivo | quando | resposta | o convite |
|---|---|---|---|
| `outro-email` | o 4 | o `302` **e o `buteco-recusa`** com `{ motivo: "outro-email", provedor, email }` (o e-mail **confirmado pelo provedor**; o do convite o painel já tem pela consulta) | continua pendente |
| `email-nao-verificado` | o 3 | o `302`, sem `buteco-recusa` (o e-mail não confirmado não é mostrado, como no login) | continua pendente; aceita por senha |
| `convite-invalido` | o 2 ou o `UPDATE` do 6 com zero linhas: vencido, cancelado (o usuário não existe mais), usado (por senha ou por outro retorno), substituído (o hash do link mudou) | o `302`; **um motivo só para os quatro**, como o `404` único dos links | já não valia; o substituto, se há, continua valendo |
| `em-uso` | o 5, ou o `23505` do `INSERT` | o `302`; o log leva o id do convidado, e não o do dono do vínculo | continua pendente (o `UPDATE` voltou no rollback) |
| `dominio-nao-permitido` | o 1 | o `302` | continua pendente |
| `falha` | o 0: o provedor saiu da configuração (reinício no meio), a pessoa cancelou no Google (`error=access_denied`), state vencido ou reusado, correlação, PKCE, nonce, assinatura, `iss`, `aud`, provedor fora do alcance na troca do código | o `302` para `/convite?recusa=falha&provedor=<chave>` **quando a intenção se lê** do state token; quando não se lê (state ilegível, ou nenhum provedor configurado, em que nem o esquema do cliente existe), `/entrar?recusa=falha`, como hoje: não há como saber que era um convite. Log `1199` com o `error` do cliente, como no login | continua pendente |

**A pessoa cancelou no Google** é `falha`, e não um motivo próprio: é o que o login já faz (D7 da #189), e
a tela mostra "Não foi possível entrar com o Google. Tente de novo." com o convite intacto.

**O provedor saiu da configuração** no meio. Na M3 há um provedor por issuer (regra 6 do boot), e são duas
formas, medidas no apply (13/11/2026, tasks.md 4.9):
- **nenhum provedor configurado** (o único tirado e o `apps/api` reiniciado): o cliente OIDC nem é registrado
  (APPLY-3 da #189), a rota responde `302 /entrar?recusa=falha` sem ler o state. Cabe em `falha`, para as três
  intenções;
- **a chave trocada** (o state token nomeia uma registração que não existe mais, e outra existe): **o OpenIddict
  respondia `500`**, no login da #189 como no aceite. A correção é o D9; com ela, a rejeição chega à rota **com
  as propriedades do state** (medido), e a `falha` volta para onde a intenção começou: `/entrar` no login,
  `/account` no conectar, **`/convite`** no aceite, sempre com `&provedor=<a chave antiga>`.

A parada desta medida (decisão do dono, 13/11/2026) aconteceu: o `500` foi devolvido ao dono, que decidiu
corrigir nesta change, para as três intenções (D9), com a issue #276 para o defeito da #189.

**Do lado do início**, sem `302` e sem state token: o `404` único do link, o `404` do provedor, o `503`,
o `403`/`415` das barreiras e o `429` do limite.

### D4. (d) A concorrência

**Senha × Google, o mesmo convite ao mesmo tempo.** Os dois `UPDATE` têm a mesma condição sobre a mesma
linha de `users`. No `READ COMMITTED` do Postgres, o segundo espera a trava de linha do primeiro e, depois
do commit dele, **reavalia o `WHERE`** sobre a versão nova (o estado já é `active`, o hash é `NULL`): zero
linhas. Quem chegou primeiro vence:
- **a senha vence:** o Google volta zero linhas, rollback, `convite-invalido`; nenhum vínculo é gravado;
- **o Google vence:** a senha volta zero linhas e responde o `404` único; o usuário fica sem senha, com o
  vínculo.

**Google × Google** (duas abas, ou duas contas): cada aba tem o seu state token, de uso único. A segunda
espera a trava, volta zero linhas e recusa com `convite-invalido`, antes do `INSERT`. Com a mesma conta
Google nas duas, a segunda nem chega ao `UPDATE`: o 2 já vê o convidado ativo.

**Sem vínculo órfão:** o `INSERT` só roda depois de um `UPDATE` de uma linha, na mesma transação; se o
`INSERT` falha (`23505`, ou qualquer erro), o rollback desfaz também o `UPDATE`, e o convite continua
pendente. Não há vínculo sem convite consumido, nem convite consumido pelo Google sem vínculo.

**Os guardas** (cada um reprova contra o defeito antes de contar; decisão do dono, 13/11/2026):
1. o `UPDATE` do Google fora da transação (o `23505` deixa o convidado ativo sem forma de entrada);
2. o `INSERT` antes do `UPDATE` (o vínculo órfão quando a senha vence);
3. a condição do Google sem o hash (um link substituído no meio é consumido);
4. o `UPDATE` sem a condição do e-mail normalizado (Q5: o e-mail editado entre a leitura e o consumo
   passa).

Os dois aceites simultâneos rodam com uma barreira entre a leitura e o `UPDATE`, como o "Dois aceites
concorrentes" da #181.

### D5. (e) O que a #190 precisa desta change

**As rotas e as respostas** que a tela de aceitar convite usa:

| rota | já existe | respostas |
|---|---|---|
| `POST /auth/invitation` `{ token }` | sim (#181) | `200 { nome, email, convidadoPor, expiraEm }`; `404` único |
| `GET /auth/federated/providers` | sim (#189) | `200 { provedores: [{ chave, tipo }] }` (os botões) |
| `POST /auth/invitation/federated/{chave}/start` `{ token }` | **nova** | `200 { url }` com o cookie do fluxo; `404` único do link; `404` "Provedor não configurado."; `503` "Não foi possível falar com o provedor…"; `403`/`415`; `429` |
| `GET /auth/federated/callback` | sim (#189) | aceito: `302 /entrar?federado=<chave>&segundoPasso=cadastro&convite=aceito` com a sessão pendente; recusa: `302 /convite?recusa=<motivo>&provedor=<chave>` |
| `POST /auth/federated/refusal` | sim (#189) | `200 { motivo: "outro-email", provedor, email }` uma vez; `404` vazio |
| `POST /auth/invitation/accept` | sim (#181) | o aceite por senha, sem mudança |
| `/auth/session/enrollment/setup` e `/confirm` | sim (#220) | o cadastro das 2 etapas depois do aceite |

**Os motivos que a tela mostra:** `outro-email`, `email-nao-verificado`, `convite-invalido`, `em-uso`,
`dominio-nao-permitido`, `falha`.

**O token na volta ao convite (Q1):** o retorno não tem o link (só o hash), e a URL de volta não o leva.
Para a tela de `email-diferente` mostrar o convite e o formulário de senha, **o painel guarda o token no
armazenamento da aba antes de navegar ao Google** (o mesmo `tabStorage` do login em andamento) e o lê ao
voltar a `/convite?recusa=…`. Sem ele (outra aba, armazenamento limpo), a tela pede para abrir o link do
convite de novo.

**O contrato do token na aba, que a #190 recebe** (decisão do dono, 13/11/2026; é do painel, e não desta
change, e foi comentado na #190, [comentário](https://github.com/aldovrando-oliveira/buteco-agentes/issues/190#issuecomment-6869086154)):

| | |
|---|---|
| **onde** | `sessionStorage` (o `tabStorage` do `AuthRuntime`), na chave **`buteco.convite`**, ao lado de `buteco.login` e `buteco.sessao`; o valor é só o token |
| **gravado** | no clique do botão do provedor, depois do `200 { url }` do início e antes de navegar ao `url` |
| **apagado no aceite** | ao carregar o `/entrar?…&convite=aceito`, **antes** de abrir o cadastro das 2 etapas |
| **apagado na recusa** | em toda volta a `/convite?recusa=…`, **depois** de a tela ler o motivo (e, no `outro-email`, depois do `POST /auth/federated/refusal` e da consulta do convite). O token que o formulário de senha ainda precisa fica só na memória da página |
| **substituído** | ao abrir um link de convite (`/convite#<token>`), que grava o token novo por cima |
| **nunca** | na URL (nem query, nem fragmento recolocado), em log, em mensagem de erro, no `localStorage` ou em cookie |

**Quadro `AceitarConvite`** (versão `1794615840-7c9e`), no formato do item f da #220:

| item | estado | o que o quadro mostra | o backend |
|---|---|---|---|
| a | `escolha` | "Olá, Marcos" | serve: `nome` da consulta |
| b | `escolha` | "Aldovrando Oliveira convidou você para o Buteco Agentes como **Operador**." | **não cumpre o papel**: a consulta traz `convidadoPor` e não o `papel` (Q9) |
| c | `escolha` | "Continuar com Google" | serve: `GET /auth/federated/providers` e o início novo |
| d | `escolha` | "Continuar com SSO Empresa · Keycloak · sso.empresa.com" | **não cumpre**: só o tipo `google` na M3; SSO corporativo é da M4 |
| e | `escolha` | "Entre com a conta da empresa" | texto do quadro; com o Google, a conta pode não ser "da empresa" (sem `HostedDomain`) |
| f | `escolha` | "A conta precisa ter o e-mail marcos.lima@empresa.com." | serve: `email` da consulta; é a regra do D2, 4 |
| g | `escolha` | "ou crie uma senha", o formulário, "Criar senha e entrar" | serve: o aceite por senha |
| h | rodapé | "Este convite vale até 25/10/2026 às 21:29 e só pode ser usado uma vez." | serve: `expiraEm` |
| i | `redirecionando` | "Redirecionando para Google…" | serve: o `200 { url }` |
| j | `redirecionando` | "Entre com a conta de marcos.lima@empresa.com. Depois você volta para cá." | **parcial**: só as recusas voltam ao convite; o aceite vai ao cadastro das 2 etapas no `/entrar` (Q7). O e-mail não vai ao Google como `login_hint` (Q10) |
| k | `redirecionando` | "Cancelar" | só o painel, antes de navegar; cancelar **no Google** é `falha` |
| l | `email-diferente` | "E-mail diferente do convite" / "O Google confirmou marcos.pessoal@gmail.com, mas este convite é para marcos.lima@empresa.com. Entre com a conta certa ou crie uma senha abaixo." | serve: o e-mail do Google pelo `buteco-recusa` (`outro-email`); o do convite pela consulta, **com o token guardado pelo painel** (Q1); o convite continua pendente, e o formulário vale |
| m | `concluido` | "Conta ativada" | **parcial**: não há estado concluído em `/convite`; o retorno vai ao `/entrar` com `convite=aceito` (Q7), e o aviso cabe lá, antes do cadastro |
| n | `concluido` | "Daqui em diante você entra com **Google**." | serve: o vínculo; `formasDeEntrada` `["google"]` |
| o | `concluido` | "A verificação em duas etapas é feita pelo próprio Google." | **não cumpre**: o Buteco pede o próprio código (Q1 da #189); o dono já tira o texto na `m3` (D10 da #189) |
| p | `concluido` | "Quer também poder entrar com senha? Defina uma em Minha conta → Formas de entrada, quando quiser." | **não cumpre**: nenhuma rota define a primeira senha sem o link do Admin (non-goal da #189 e daqui); e o `password-reset-link` só serve Admin (D8, registro 3) |
| q | `concluido` | "Ir para o painel" | **não cumpre como está**: o passo seguinte é o cadastro das 2 etapas, e só depois o painel |

**O que o backend serve e o quadro não desenha:** `email-nao-verificado`; `convite-invalido` no retorno
(no meio do fluxo, diferente do `link-invalido` da abertura, que o `AceitarConviteSemProvedor` já tem);
`em-uso`; `dominio-nao-permitido`; `falha` (inclui o cancelamento no Google e o provedor que saiu); o `503`
e o `429` do início; o `404` "Provedor não configurado." do início; e, no quadro `Main`, um `cadastro`
depois do aceite (como o `cadastro-apos-nova-senha` da #220).

### D6. (f) Tamanho (convenção 18)

**Âncora: a #189** (PR #275, `3de31dd..1475d35`, só código, sem migrations, `openspec/` e `.md`):
**+1.603 / −49** em produção e **+2.112 / −3** em testes, dos quais **445** de duplo novo (`FakeGoogle`
259, `GoogleFlow` 186); **78** casos novos. A projeção dela foi +1.310 / +1.880 / +380: produção +22 %,
testes sem duplo −11 %, duplo +17 %. Unidade: linha; casos contados à parte, unidade **caso** (`[Fact]`,
e cada linha de `[Theory]` conta uma).

| componente | produção | testes | casos | duplo |
|---|---|---|---|---|
| o início (D1): rota, convite, propriedades, as respostas do início | +70 | +150 | 7 | — |
| o retorno `convite` (D2): a ordem, a transação, a sessão, o `302` | +110 | +220 | 4 | — |
| as recusas (D3), o `buteco-recusa` do `outro-email`, os logs | +45 | +260 | 11 | — |
| a condição de consumo dividida e a concorrência (D2, D4) | +30 / −15 | +130 | 4 | — |
| a rota nas listas (`RouteAuthenticationTests`, allowlist do `Program.cs`) | +5 | +5 | 0 (caso existente muda) | — |
| **o duplo**: o `GoogleFlow` ganha o início do convite e o retorno com a intenção | — | — | — | **+50** (extensão) |
| **o duplo**: a barreira entre a leitura e o consumo (tasks 2.3), e o apoio que decifra o state token (1.4) | +10 (o ponto de chamada) | — | — | **+80** (novo, pequeno) |
| **total** | **~+270 / −15** | **~+765** | **~26** | **+130** |

Com o erro medido da âncora: produção **+270 a +330**, duplo **+130 a +160**, testes **+690 a +850**. **Cabe numa change**: um
sexto da #189 em produção, sem pacote, sem migration, e com duplo pequeno sobre o `FakeGoogle` que existe. Os casos subiram de ~26 para ~40 no `tasks.md` (os guardas e a concorrência pedidos na revisão), +200 a +300 linhas de teste sobre a faixa. Rascunho: a projeção conta antes da
verificação.

### D7. (g) Notas de deploy

- **Migration:** nenhuma. A `user_federated_identities` (#189) e as colunas do link (#181) já existem.
- **Configuração:** nenhuma nova; o aceite usa o provedor da `Identity:FederatedProviders`.
- **Conferir:** com um convite pendente, o início responde `200 { url }`; com um link vencido, o `404`
  único; o aceite real pelo Google (na conferência manual com o cliente OAuth da #189) cai no cadastro das
  2 etapas; outro e-mail volta a `/convite?recusa=outro-email`.
- **Voltar atrás:** a imagem anterior do `api` (a da #189). Quem aceitou pelo Google continua ativo, sem
  senha, com o vínculo, e **entra** pelo login pelo Google da #189 (o par decide). Um retorno em voo com a
  intenção `convite` cai, na imagem anterior, no `LoginAsync` (intenção desconhecida, l. 164–166): sem
  vínculo e convidado, a recusa é `convite-pendente`, sem sessão e com o convite intacto. Nada a desfazer
  no banco.

### D8. (h) Premissas falsas (convenção 9)

Nenhuma muda comportamento ou contrato. Registros:

1. **"O consumo do link é o mesmo `UPDATE` único do aceite por senha"**: o `UPDATE` de hoje
   (`AccessLinkPublicEndpoints.cs` l. 116–129) grava a senha e roda fora de transação; o do Google não
   grava senha e precisa da transação do `INSERT`. O que se divide é a **condição** (o `WHERE`), e o `SET`
   difere na senha (D2). Registro.
2. **"O retorno grava a sessão pendente e cai no passo do código ou no cadastro"**: o convidado nunca tem
   a verificação (`local-users`, l. 23; o cadastro só aceita usuário ativo; `User.cs` l. 97), e o passo é
   sempre o `cadastro`. Registro.
3. **"Sem senha, a pessoa ganha uma pelo link de redefinição do Admin ou pelo `password-reset-link`
   (#257)"**: o comando só serve **Admin ativo** (`PasswordResetLinkCommand.cs` l. 19 e l. 44, a saída
   `4`; spec `user-access-links`, "Comando de emergência"). Um Operador ou um Leitor que aceitou pelo
   Google e perde o acesso à conta Google só ganha senha pelo link que um Admin gera no painel
   (`POST /users/{id}/password-reset`), que grava a primeira senha (`local-users`, "Usuário ativo sem
   senha"). Registro (decisão do dono, 13/11/2026): vai para o `docs/deployment.md`, na seção do comando
   ("Comando de emergência da senha: `password-reset-link` (#257)"), e para o `docs/configuration.md`.
4. **"Telas do protótipo: … com o aviso de outro e-mail e os estados de e-mail não verificado e de convite
   que deixou de valer"**: o quadro `AceitarConvite` tem só `escolha`, `redirecionando`, `email-diferente`
   e `concluido`; os outros dois estados não estão desenhados (D5). Registro; o dono desenha na `m3`.
5. **"E-mail diferente: o aceite volta ao convite com o aviso"**: o backend não tem como voltar ao
   `/convite#<token>`, porque o state token leva só o hash (decisão do corpo). Volta a
   `/convite?recusa=outro-email&provedor=<chave>`, e o convite só aparece se o painel guardou o token
   (Q1). Registro; o contrato da volta é a Q1.

### D9. A registração ausente vira rejeição, e não exceção (issue #276; decisão do dono, 13/11/2026)

**O defeito, medido:** com a chave do provedor trocada entre o início e o retorno, o retorno respondia `500`
nas três intenções. A exceção (`System.InvalidOperationException: The client registration corresponding to the
specified identifier cannot be found in the client options.`) sai do OpenIddict 7.7.2 **dentro do middleware de
autenticação**, antes da rota:

```csharp
// OpenIddictClientHandlers.cs, ResolveClientRegistrationFromStateToken (l. 1089–1130)
// Note: if the static registration cannot be found in the options, this may indicate
// the client was removed after the authorization dance started and thus, can no longer
// be used to authenticate users. In this case, throw an exception to abort the flow.
context.Registration ??= await _service.GetClientRegistrationByIdAsync(context.RegistrationId, context.CancellationToken);

// OpenIddictClientService.cs, GetClientRegistrationByIdAsync (l. 153–155)
return new(options.CurrentValue.Registrations.Find(registration => string.Equals(
    registration.RegistrationId, identifier, StringComparison.Ordinal)) ??
    throw new InvalidOperationException(SR.GetResourceString(SR.ID0410)));
```

**A ordem dos handlers da autenticação** (`OpenIddictClientHandlers.cs` l. 39–49, e as ordens em l. 606–1104):

```csharp
EvaluateValidatedUpfrontTokens.Descriptor,
ResolveValidatedStateToken.Descriptor,
ValidateRequiredStateToken.Descriptor,
ValidateStateToken.Descriptor,                       // o state token conferido (assinatura, cifra, prazo)
...
RedeemStateTokenEntry.Descriptor,                    // o state resgatado: uso único, antes de tudo abaixo
...
ValidateRequestForgeryProtection.Descriptor,         // a correlação com o cookie do fluxo
ValidateEndpointUri.Descriptor,                      // a redirect_uri (o FederatedRequestUriHandler, APPLY-5 da #189)
ResolveClientRegistrationFromStateToken.Descriptor,  // .SetOrder(ValidateEndpointUri.Descriptor.Order + 1_000)
```

**A correção:** o `FederatedMissingRegistrationHandler`, um `IOpenIddictClientHandler<ProcessAuthenticationContext>`
com os mesmos filtros do `ResolveClientRegistrationFromStateToken` (`RequireStateTokenPrincipal`,
`RequireStateTokenValidated`) e a ordem `ResolveClientRegistrationFromStateToken.Descriptor.Order - 500` (entre
ele e o `ValidateEndpointUri`). Lê o `Claims.Private.RegistrationId` do state token; se nenhuma registração das
opções tem esse id, chama `context.Reject(error: invalid_request, description: …)`. Com o `EnableErrorPassthrough`
(APPLY-8 da #189), a rejeição chega ao `AuthenticateAsync` da rota como falha, e a rota responde a recusa `falha`
da intenção (o log `1199` com `invalid_request`). O state já foi resgatado antes: repetir o retorno continua `falha`.

**Medido depois da correção:** as propriedades do state chegam à rota (a rejeição vem depois da validação do
state token, e o `AuthenticateAsync` devolve as propriedades do principal do state). Login:
`302 /entrar?recusa=falha&provedor=corporativo`; conectar: `302 /account?recusa=falha&provedor=corporativo`;
aceite: `302 /convite?recusa=falha&provedor=corporativo`, com o convite pendente. Nenhum com `buteco-sessao`.

**Guarda** (tasks.md 4.10): o handler retirado devolve o `500`, e os três casos reprovam
(`Expected: Found / Actual: InternalServerError`).

**Descartado:** pegar a exceção num middleware antes do de autenticação (o state já resgatado e sem as
propriedades, a intenção perdida, e uma exceção genérica do OpenIddict tratada como recusa); aceitar o `500` e
mudar a spec (reduz contrato).

### D11. O log da biblioteca OIDC (issue #277; decisão do dono, 13/11/2026)

**O defeito, medido:** a conferência local (tasks.md 10.1) achou o `sub` e o e-mail do Google no log do `api`. A
medida com a captura alargada (`IdentityIssuerLogCapture` com `Buteco.Api.*` e `OpenIddict.*`; um fixture
temporário pôs a captura em `Trace` para ver todo nível, e saiu depois da medida), percorrendo o login por senha
com o código, o cadastro das 2 etapas, a renovação, o logout, o login pelo Google, o aceite pelo Google, o
conectar e o `client_credentials` de serviço e de aplicação externa:

| categoria | EventId | nível | o que leva | fluxos |
|---|---|---|---|---|
| `OpenIddict.Client.OpenIddictClientDispatcher` | 6195 ("The userinfo response returned by {Uri} was successfully extracted: {Response}.") | **Information** | o `sub` e o e-mail do Google (e o e-mail do convidado, no aceite) | login pelo Google, aceite, conectar |
| `OpenIddict.Server.OpenIddictServerDispatcher` | 6075 ("The token request was successfully extracted: {Request}.") | **Information** | o `code_verifier` do PKCE (o `code`, o `refresh_token` e o `client_secret` a biblioteca mascara) | todo authorize com troca do código |
| `OpenIddict.Client.OpenIddictClientDispatcher` | 6001 (o token validado e as claims) | Trace | o `sub` e o e-mail do Google | login pelo Google, aceite, conectar |
| `OpenIddict.Server.OpenIddictServerDispatcher` | 6001 e 6013 (o token validado; o JWT criado, com o payload e as claims) | Trace | o e-mail e o nome do usuário, o access token, o refresh token e o id token (e o token do `client_credentials`) | todos |
| `OpenIddict.Server.OpenIddictServerDispatcher` | 6015 (a referência ligada à entrada do token) | Trace | o código do authorize | todo authorize |

**Nada em `Warning` ou acima**: o filtro de nível basta. As linhas do Buteco (1198 a 1203 e as do emissor) não
levam nada. **O servidor também vaza**, e por isso o filtro é o `OpenIddict` inteiro, e não só o cliente.

**A correção:** `"OpenIddict": "Warning"` no `Logging:LogLevel` do `appsettings.json`, que vale para todos os
ambientes, com o comentário do porquê. O `appsettings.Production.json` e o `appsettings.Development.json` não o
sobrescrevem.

**O Development não mantém `Information`** (recomendação): os testes do `apps/api` rodam no ambiente
`Development` com o `appsettings.json`, e são eles que afirmam a ausência; um `Information` no Development
desligaria o guarda. Para depurar o OpenIddict localmente, `Logging__LogLevel__OpenIddict=Information` na
variável de ambiente, de propósito e sabendo que o log passa a levar esses dados (o comentário do
`appsettings.json` diz isso). Descartado: o `Information` no `appsettings.Development.json`, com comentário.

**Por que o guarda da #189 não pegou:** a captura só aceitava `Buteco.Api.Identity.*` e `Buteco.Api.Users.*`.
Alargada, o caso da #189 (`NoLogLine_CarriesTheEmailOrTheSub`) e o 4.7 passaram a ver as linhas da biblioteca.

**Os guardas** (`LogPrivacyTests`, um caso por fluxo, com o host real): nenhuma linha de nenhuma categoria
capturada leva os valores procurados. Sem o filtro (tasks.md 11.4), reprovam o da #189, o 4.7, o login por senha
(a 6075), o login pelo Google, o aceite e o conectar (a 6195); o `client_credentials` passa sem o filtro, porque
nada dele vaza em `Information`.

### D10. O que o apply mudou (convenção 9, 13/11/2026)

Nenhuma mudança de contrato com o que o painel lê, além da decidida pelo dono no APPLY-1.

| | o que caiu | o que foi feito |
|---|---|---|
| APPLY-1 | a premissa do D3 de que o provedor que sai da configuração cai em `falha`: com a chave trocada, o OpenIddict **lançava** (`500`), no aceite e no login da #189 (tasks.md 4.9) | **parada; decisão do dono (13/11/2026):** corrigido nesta change para as três intenções (D9), com a issue [#276](https://github.com/aldovrando-oliveira/buteco-agentes/issues/276); spec `federated-login`, "Decisão do login no retorno" em MODIFIED, com os seis cenários |
| APPLY-2 | — | **medido:** depois da rejeição, as propriedades do state chegam à rota; a `falha` do aceite volta a `/convite`, a do conectar a `/account`, a do login a `/entrar`, todas com o `provedor` |
| APPLY-3 | o início e o retorno do aceite como classe própria não alcançavam o `ProviderIdentity`, os apoios de recusa e o `Item`, privados do `FederatedLoginEndpoints` | o aceite é um arquivo parcial da mesma classe (`FederatedInvitationEndpoints.cs`) |
| APPLY-4 | o `FindAsync`, o `ValidLink` e o `InvalidLink` do aceite por senha eram privados | `internal`, para o início do aceite usar a mesma busca e a mesma resposta única; a consulta não mudou |
| APPLY-5 | — | registro: o guarda 1.4 reprova pela primeira asserção (o hash ausente do state), e o aceite feliz **aprova** com o mesmo defeito, o que confirma que só o guarda o pega |
| APPLY-6 | o `InvitationSeed` criava todo convidado como Admin | ganhou o papel, opcional, com o mesmo padrão; só arranjo de teste |
| APPLY-7 | a conferência local com a CA só no processo: o .NET no macOS não lê `SSL_CERT_FILE` | o `apps/api` da conferência roda no contêiner da imagem (Linux), com a CA no `SSL_CERT_FILE` do processo e o duplo no mesmo pod (aceito pelo dono, 13/11/2026) |
| APPLY-8 | a premissa de que nenhum log do fluxo leva o e-mail nem o `sub`: a conferência local achou o `OpenIddictClientDispatcher[6195]`, em `Information`, com o userinfo; os guardas não viam, porque a captura só aceitava as categorias do Buteco | **parada; decisão do dono (13/11/2026):** corrigido nesta change (D11), com a issue [#277](https://github.com/aldovrando-oliveira/buteco-agentes/issues/277). A medida achou também o servidor (a 6075, com o `code_verifier`); o filtro é o `OpenIddict` inteiro em `Warning`. Specs `federated-login` ("Logs do login federado sem dado pessoal" em MODIFIED) e `issuer-user-login` (requisito novo de log do emissor) |

## Risks / Trade-offs

- **[O token na aba]** → o token do convite fica no armazenamento da aba durante a ida ao Google (Q1):
  sessão da aba, mesma origem, apagado no aceite e em toda recusa (o contrato do D5). É o mesmo lugar do
  `code_verifier` do login. Se a aba for outra, a tela pede o link de novo; o convite não se perde.
- **[Intenção ilegível vai ao `/entrar`]** → a `falha` sem state legível perde o contexto do convite; a
  tela de login mostra a falha, e o link continua valendo.
- **[O e-mail editado no meio]** → o Admin pode editar o e-mail do convidado entre o início e o retorno;
  sem a condição do e-mail no `UPDATE` (Q5), um aceite com o e-mail antigo passaria na janela entre o 4 e
  o 6.
- **[Um reinício no meio]** → o retorno em voo cai em `falha` (a registração pode sumir; as chaves do Data
  Protection do contêiner também), e o convite continua pendente.

## Baseline

No worktree limpo `../buteco-agentes-274-baseline`, em `1475d35`, pelo
`~/.cache/buteco-agents/bin/suite-274.sh` (cópia do `suite-189.sh` com o cabeçalho da #274: o lock, a
espera por CPU 100 e load, a amostragem e o veredito assimétrico). Saídas em `~/.cache/buteco-agents/274/`.

Com o `buteco-agents_postgres_1` e o `buteco-agents_rabbitmq_1` de pé no `podman ps` do início.

| suíte | resultado | duração | estado (início → fim) | CPU mínima |
|---|---|---|---|---|
| `apps/api` | **1794/1794 + 1 ignorado** | 482 s | load 4,38, CPU 100 → load 20,25, CPU 100 | 44 |
| `tests/ApiConnectorsRoundTrip.Tests` | **12/12** | 41 s | load 1,18, CPU 100 → load 2,54, CPU 100 | 100 |

- **As duas APROVADAS** na primeira rodada (regra assimétrica da #181): a queda a 44 no `apps/api` não
  invalida rodada verde.
- `nomes.txt` em `~/.cache/buteco-agents/274/baseline/`: 1807 linhas (1795 do `apps/api`, com 1 GUID
  normalizado; 12 do acordo). O `apps/api` passou de 1697 (baseline da #189) a 1795: os +98 são da #189.

**Por que cada suíte:** `apps/api` (tudo mora lá: o aceite por senha, o retorno federado, o vínculo, as
recusas, a sessão pendente); `tests/ApiConnectorsRoundTrip.Tests` (o `RoleRoundTripTests` aceita um
convite por senha pelo `IssuerUserFlow.InviteAndAcceptAsync`, `POST /auth/invitation/accept`, cuja
condição de consumo esta change divide com o aceite pelo Google). **Fora:**
`tests/InboxOrchestratorRoundTrip.Tests` (o `IssuerUserFlow` dele tem o `InviteAndAcceptAsync`, mas nenhum
teste o chama, conferido em `1475d35`; e nenhum acordo de `tests/` passa pelo retorno do Google);
`CrossAppTaskStoreCompatibility.Tests` (banco entre api e workers, sem login); `apps/inbox` e
`apps/connectors` (o token que validam não muda); `apps/workers` (sem usuário nem rota); `apps/frontend`
(as telas são da #190; se a change passar a ter tela, ela para).

## Decisões do dono (13/11/2026)

As dez aceitas como recomendadas, com os acréscimos da última coluna.

| | pergunta | decisão | descartado | acréscimo |
|---|---|---|---|---|
| Q1 | Para onde voltam as recusas do aceite, e como a tela recupera o convite | `302 /convite?recusa=<motivo>&provedor=<chave>`; o painel guarda o token no armazenamento da aba antes de ir ao Google | `/entrar?recusa=…` (perde o convite e o formulário de senha); o token no state token (proibido pelo corpo) | o token sai da aba no aceite e em toda recusa; o contrato (D5) é da #190, comentado lá |
| Q2 | Os nomes dos motivos | os novos `outro-email` e `convite-invalido`; os da #189 `email-nao-verificado`, `em-uso`, `dominio-nao-permitido` e `falha` com o mesmo sentido | prefixar todos com `convite-` (duplica motivos que já são contrato) | — |
| Q3 | A rota do início | `POST /auth/invitation/federated/{chave}/start` com `{ token }` | `{ convite }` no início do login (uma rota com dois contratos) | — |
| Q4 | A ordem do retorno | o convite antes do e-mail (D2) | a ordem da lista do pedido (dá `outro-email` a um convite morto) | guarda: o e-mail conferido antes do convite reprova "convite morto com outro e-mail responde `convite-invalido`, e não `outro-email`" |
| Q5 | O `UPDATE` do Google condicionado também ao e-mail normalizado | sim: a regra do e-mail igual vale no instante do consumo | só a condição do aceite por senha (a janela do e-mail editado) | guarda próprio: o `UPDATE` sem a condição do e-mail normalizado (o quarto da concorrência, D4) |
| Q6 | O aceite pelo Google zera o bloqueio do e-mail | sim, como o aceite por senha: sem isso, um bloqueio de tentativas anteriores dá `423` no cadastro que vem logo depois | não zerar | — |
| Q7 | O aceite marca a volta ao `/entrar` | `&convite=aceito`, para a #190 mostrar "Conta ativada" antes do cadastro | sem marca (o `/entrar` não distingue o aceite do login) | — |
| Q8 | Os eventos de log | próprios: `1202` `FederatedInvitationRefused` (`Warning`) e `1203` `FederatedInvitationAccepted` (`Information`, o id e a chave) | reusar o `1198` do login (mistura aceite e login na mesma contagem) | — |
| Q9 | O `papel` na consulta do convite, que o quadro mostra | fora desta change: o quadro `m3` tira "como Operador", como o `AceitarConviteSemProvedor`, que não o mostra | acrescentar `papel` ao `200` do `POST /auth/invitation` aqui (contrato de rota existente, fora do escopo do corpo) | o dono tira "como Operador" do quadro na entrega `m3` |
| Q10 | O e-mail do convite como `login_hint` no pedido ao Google | não: o pedido fica o mesmo do login (D2 da #189), e o e-mail do convidado não vai ao provedor antes de a pessoa escolher a conta | mandar o `login_hint` | — |


## Open Questions

Nenhuma.
