# panel-access-link-pages Specification

## Purpose
Diz o que as páginas públicas `/convite` e `/redefinir-senha` do painel fazem com o link de uso único da #181 (#245): o token sai do endereço antes de qualquer pedido e fica só na aba, a consulta e seus estados (carregando, indisponível, link inválido), o formulário da senha nova e a entrada logo depois, inclusive no passo do código para quem tem as 2 etapas.
## Requirements
### Requirement: Páginas públicas do link de uso único
`apps/frontend` SHALL prover as páginas `/convite` e `/redefinir-senha`, abertas sem sessão, ao lado de `/entrar`. Os caminhos SHALL ser os que o `apps/api` monta no link (`AccessLinkUrls`, `user-access-links`). Uma sessão aberta na aba SHALL NOT impedir nem mudar o uso das páginas.

| página | consulta | conclusão | quadro |
|---|---|---|---|
| `/convite` | `POST /auth/invitation` | `POST /auth/invitation/accept` | `AceitarConviteSemProvedor` |
| `/redefinir-senha` | `POST /auth/password-reset` | `POST /auth/password-reset/complete` | `RedefinirSenha` |

As quatro chamadas SHALL ser `POST` com corpo JSON, sem token, e SHALL NOT levar o token do link na URL.

#### Scenario: O convite abre sem sessão
- **WHEN** alguém sem sessão abre `/convite#abc`
- **THEN** a página do convite aparece, sem ir ao `/entrar`

#### Scenario: A redefinição abre sem sessão
- **WHEN** alguém sem sessão abre `/redefinir-senha#abc`
- **THEN** a página de redefinição aparece, sem ir ao `/entrar`

### Requirement: O token sai do endereço antes de qualquer pedido e fica só na aba
Na primeira renderização, a página SHALL ler o token do fragmento (`location.hash`), gravá-lo no `sessionStorage` sob `buteco.link.convite` (ou `buteco.link.redefinir-senha`) e trocar o endereço por `/convite` (ou `/redefinir-senha`) com `history.replaceState`, **antes do primeiro pedido**. Sem token no fragmento, o token SHALL ser o da chave. O token SHALL ir só no corpo dos pedidos.

O token SHALL ser apagado da chave:
- no `204` da conclusão (aceite ou redefinição), antes do login;
- em qualquer `404` (link inválido);
- ao sair da página, quando ela desmonta;
- **no aceite pelo provedor**: o `/entrar` com `convite=aceito` apaga a chave `buteco.link.convite` antes de abrir o cadastro (`panel-federated-login`);
- **na recusa que volta ao convite**: a página lê o motivo, faz a consulta do convite (e, no `outro-email`, a leitura do aviso) e apaga a chave; o token que o formulário de senha ainda precisa fica só no estado da página.

O clique do provedor SHALL gravar a chave com o token do estado antes da navegação ao provedor (`location.assign` depois do `200` do início), sempre, e a navegação SHALL NOT apagá-la: a volta de uma recusa reaproveita o token, também numa segunda tentativa depois de uma recusa que já apagou a chave. O token SHALL NOT ir para a URL (nem query, nem fragmento recolocado), para log, para mensagem de erro, para o `localStorage` nem para cookie.

Recarregar a página SHALL reaproveitar o token da chave. Sem token no fragmento nem na chave, a página SHALL mostrar o estado de link inválido sem fazer pedido. O `StrictMode` do React SHALL NOT perder o token.

#### Scenario: O endereço troca antes do primeiro pedido
- **WHEN** `/convite#k7Qp2vX9` é aberto
- **THEN** no instante do primeiro `fetch` o endereço já é `/convite`, sem fragmento, e o corpo do pedido é `{ "token": "k7Qp2vX9" }`

#### Scenario: O token nunca vai numa URL
- **WHEN** a página faz a consulta e a conclusão
- **THEN** nenhuma URL de pedido contém o token

#### Scenario: Recarregar reaproveita o token
- **WHEN** a página do convite é recarregada depois de o endereço virar `/convite`
- **THEN** a consulta sai de novo com o mesmo token, e o formulário aparece

#### Scenario: Sem token não há pedido
- **WHEN** `/redefinir-senha` é aberto sem fragmento e sem a chave na aba
- **THEN** a página mostra "Este link não vale mais" e nenhum pedido sai

#### Scenario: O sucesso apaga o token
- **WHEN** a conclusão responde `204`
- **THEN** a chave da página não existe mais no `sessionStorage`

#### Scenario: O link inválido apaga o token
- **WHEN** a consulta responde `404`
- **THEN** a chave da página não existe mais no `sessionStorage`

#### Scenario: Sair da página apaga o token
- **WHEN** a pessoa sai da página do convite por "Ir para o login" ou por outra navegação do painel
- **THEN** a chave `buteco.link.convite` não existe mais no `sessionStorage`

#### Scenario: O StrictMode não perde o token
- **WHEN** a página do convite monta sob o `StrictMode` com `#k7Qp2vX9` no endereço
- **THEN** a consulta leva `k7Qp2vX9`, e a chave continua gravada enquanto a página está aberta

#### Scenario: A ida ao provedor não apaga o token
- **WHEN** a pessoa clica em "Continuar com Google" no convite, e o início responde `200 { url }`
- **THEN** no instante da navegação ao `url`, a chave `buteco.link.convite` ainda está no `sessionStorage`

#### Scenario: Segunda tentativa depois de uma recusa
- **WHEN** depois de `/convite?recusa=outro-email&provedor=corporativo` (a chave já apagada, o token no estado), a pessoa clica em "Continuar com Google", e o início responde `200 { url }`
- **THEN** no instante da navegação ao `url`, a chave `buteco.link.convite` existe no `sessionStorage` com o mesmo token

#### Scenario: A recusa apaga o token
- **WHEN** o `/convite?recusa=em-uso&provedor=corporativo` abre com o token na chave, e a consulta responde `200`
- **THEN** o aviso e o formulário aparecem, e a chave `buteco.link.convite` não existe mais no `sessionStorage`

#### Scenario: O token não vai para a URL nem para o log
- **WHEN** a página faz a consulta, o início pelo provedor e a volta de uma recusa, com o `console` capturado
- **THEN** nenhuma URL de pedido nem endereço da página contém o token, nenhuma linha do `console` o contém, e o `localStorage` não o contém

### Requirement: Consulta, carregando, indisponível e link inválido
Enquanto a consulta não responde, a página SHALL mostrar "Conferindo o link…" com o indicador de carregamento, com `role="status"`, no lugar do formulário. A resposta da consulta SHALL levar a um destes estados:

| resposta | estado |
|---|---|
| `200` | o formulário (requisito seguinte) |
| `404` | link inválido |
| `503` ou falha de rede | indisponível: "Não foi possível abrir o link agora", "O painel está indisponível ou sem conexão. O link continua valendo: tente de novo em alguns segundos." e o botão "Tentar de novo", que repete a consulta, com `role="alert"` |

O estado de link inválido SHALL mostrar o título "Este link não vale mais", o texto da página e o botão "Ir para o login", que leva a `/entrar`. O texto SHALL ser o do quadro de cada página, e não o `title` do servidor:

| página | texto |
|---|---|
| `/convite` | "O convite pode ter vencido, já ter sido usado ou ter sido cancelado. Peça um novo link a quem convidou você." |
| `/redefinir-senha` | "O link pode ter vencido, já ter sido usado, ou outro link pode ter sido gerado depois dele. Peça um novo a um administrador do painel." |

O `404` da conclusão SHALL levar ao mesmo estado de link inválido.

#### Scenario: Carregando enquanto a consulta não responde
- **WHEN** a consulta ainda não respondeu
- **THEN** a página mostra "Conferindo o link…" num elemento com `role="status"`, e nenhum campo de senha aparece

#### Scenario: Link inválido no convite
- **WHEN** `POST /auth/invitation` responde `404`
- **THEN** a página mostra "Este link não vale mais", "O convite pode ter vencido, já ter sido usado ou ter sido cancelado. Peça um novo link a quem convidou você." e "Ir para o login", e não mostra o `title` do servidor

#### Scenario: Link inválido na redefinição
- **WHEN** `POST /auth/password-reset` responde `404`
- **THEN** a página mostra "Este link não vale mais" e "O link pode ter vencido, já ter sido usado, ou outro link pode ter sido gerado depois dele. Peça um novo a um administrador do painel."

#### Scenario: Link que vence entre a consulta e a conclusão
- **WHEN** a consulta respondeu `200` e a conclusão responde `404`
- **THEN** a página mostra o estado de link inválido

#### Scenario: Indisponível e tentar de novo
- **WHEN** a consulta responde `503`, e depois "Tentar de novo" é acionado e a consulta responde `200`
- **THEN** a página mostra primeiro "Não foi possível abrir o link agora" com `role="alert"`, e depois o formulário

#### Scenario: Sem conexão na consulta
- **WHEN** o `fetch` da consulta falha por rede
- **THEN** a página mostra o estado indisponível

### Requirement: O formulário da senha nova
Com a consulta em `200`, a página SHALL mostrar:

- no convite, "Olá, {nome}" e "{convidadoPor} convidou você para o Buteco Agentes. Crie sua senha para entrar.";
- na redefinição, "Criar nova senha" e "Olá, {nome}. Crie uma nova senha para entrar no Buteco Agentes.", e o aviso "Ao salvar, você sai de todos os navegadores em que estava conectado. Se a sua conta usa verificação em 2 etapas, o código continua sendo pedido.";
- o campo "E-mail" com o `email` da consulta, `readonly` e com `autocomplete="username"`;
- "Senha" (convite) ou "Nova senha" (redefinição) e "Repita a senha", os dois com `autocomplete="new-password"` e a ajuda "De 12 a 128 caracteres." no primeiro;
- o botão "Criar senha e entrar" (convite) ou "Salvar senha e entrar" (redefinição);
- abaixo do cartão, "Este convite vale até {expiraEm} e só pode ser usado uma vez." (ou "Este link vale até {expiraEm} e só pode ser usado uma vez."), com a data local no formato `dd/mm/aaaa às hh:mm`.

Antes de enviar, a página SHALL conferir:
- a senha com menos de 12 ou mais de 128 caracteres, contados pelo `length`, sem `trim`: "A senha precisa ter de 12 a 128 caracteres." no primeiro campo, com `aria-invalid="true"`, sem enviar;
- a repetição diferente da senha: "As senhas não conferem." no segundo campo, com `aria-invalid="true"`, sem enviar.

A conclusão SHALL enviar `{ token, senha }`, e a resposta SHALL levar a:

| resposta | o que a página faz |
|---|---|
| `204` | apaga o token e entra (requisito seguinte) |
| `400` | o `title` do servidor no primeiro campo, com `aria-invalid="true"` |
| `404` | link inválido |
| `429` | "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." sobre o formulário, com `role="alert"`; o botão fica desabilitado até passar o `Retry-After` |
| `503` ou falha de rede | "Não foi possível salvar agora. O que você digitou continua aqui: tente de novo em alguns segundos." sobre o formulário, com `role="alert"`, mantendo o que foi digitado; depois de um `503` com `Retry-After`, o botão fica desabilitado até ele passar |

A página SHALL NOT repetir o envio sozinha.

#### Scenario: O convite mostra quem convidou e o e-mail travado
- **WHEN** `POST /auth/invitation` responde `200` com `{ "nome": "Marcos Lima", "email": "marcos.lima@empresa.com", "convidadoPor": "Aldovrando Oliveira", "expiraEm": "2026-10-26T00:29:00Z" }`, num navegador em `America/Sao_Paulo`
- **THEN** a página mostra "Olá, Marcos Lima", "Aldovrando Oliveira convidou você para o Buteco Agentes. Crie sua senha para entrar.", o e-mail `readonly`, e "Este convite vale até 25/10/2026 às 21:29 e só pode ser usado uma vez."

#### Scenario: A redefinição avisa que encerra as sessões e mantém as 2 etapas
- **WHEN** `POST /auth/password-reset` responde `200`
- **THEN** a página mostra "Criar nova senha", "Ao salvar, você sai de todos os navegadores em que estava conectado. Se a sua conta usa verificação em 2 etapas, o código continua sendo pedido." e "Salvar senha e entrar"

#### Scenario: Senha curta não envia
- **WHEN** a senha tem 11 caracteres
- **THEN** nenhum pedido sai, e o primeiro campo mostra "A senha precisa ter de 12 a 128 caracteres." com `aria-invalid="true"`

#### Scenario: Senha longa demais não envia
- **WHEN** a senha tem 129 caracteres
- **THEN** nenhum pedido sai, e o primeiro campo mostra "A senha precisa ter de 12 a 128 caracteres."

#### Scenario: Espaços contam como caracteres
- **WHEN** a senha é "  abcdefghij" (12 caracteres, dois deles espaços) e a repetição é igual
- **THEN** a conclusão sai com essa senha, sem os espaços cortados

#### Scenario: Senhas diferentes não enviam
- **WHEN** a repetição é diferente da senha
- **THEN** nenhum pedido sai, e o segundo campo mostra "As senhas não conferem." com `aria-invalid="true"`

#### Scenario: A política do servidor aparece no campo
- **WHEN** a conclusão responde `400` com o `title` "A senha precisa ter de 12 a 128 caracteres."
- **THEN** o primeiro campo mostra esse texto com `aria-invalid="true"`

#### Scenario: Muitas tentativas na conclusão
- **WHEN** a conclusão responde `429` com `Retry-After: 1`
- **THEN** a página mostra "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." com `role="alert"`, o botão fica desabilitado por 1 segundo, e os campos mantêm o que foi digitado

#### Scenario: Painel indisponível na conclusão mantém o formulário
- **WHEN** a conclusão responde `503`
- **THEN** a página mostra "Não foi possível salvar agora. O que você digitou continua aqui: tente de novo em alguns segundos." sobre o formulário, com `role="alert"`, e os campos mantêm o que foi digitado

#### Scenario: Sem conexão na conclusão mantém o formulário
- **WHEN** o `fetch` da conclusão falha por rede
- **THEN** a página mostra "Não foi possível salvar agora. O que você digitou continua aqui: tente de novo em alguns segundos." sobre o formulário, e os campos mantêm o que foi digitado

### Requirement: Depois de criar a senha, a pessoa entra
No `204` da conclusão, a página SHALL chamar `POST /auth/session` com o `email` da consulta e a senha digitada, e:

| resposta do login | o que a página faz |
|---|---|
| `200` com `{ "segundoPasso": "cadastro" }` | navega a `/entrar` com o `state` `{ etapa: "cadastro" }`, e o `/entrar` abre na etapa do cadastro com o aviso "Senha salva. Falta só cadastrar o app autenticador para entrar." (`operator-login-ui`) |
| `200` com `{ "segundoPasso": "totp" }` | navega a `/entrar` com o `state` `{ etapa: "codigo" }`, e o `/entrar` abre no passo do código com o aviso "Senha salva…" (`operator-login-ui`) |
| qualquer outra | navega a `/entrar` com o `state` `{ email, status }`, e o `/entrar` abre com o e-mail preenchido e o aviso daquele status (`operator-login-ui`) |

A senha SHALL ficar só na memória da página, entre a conclusão e o login, e SHALL NOT ir ao `state` do router nem a armazenamento nenhum.

#### Scenario: Aceite seguido do cadastro
- **WHEN** o aceite responde `204` e o `POST /auth/session` com `marcos.lima@empresa.com` e a senha digitada responde `200` com `{ "segundoPasso": "cadastro" }`
- **THEN** a URL passa a `/entrar`, a tela mostra a etapa do cadastro com "Senha salva. Falta só cadastrar o app autenticador para entrar.", e o `state` do router não contém a senha

#### Scenario: Redefinição de quem tem as 2 etapas chega ao passo do código
- **WHEN** a redefinição responde `204` e o `POST /auth/session` responde `200` com `{ "segundoPasso": "totp" }`
- **THEN** a URL passa a `/entrar`, a tela mostra o passo do código com "Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.", e o `state` do router não contém a senha

#### Scenario: Login recusado depois do aceite chega ao /entrar com o e-mail
- **WHEN** o aceite responde `204` e o `POST /auth/session` responde `423` com `Retry-After: 300`
- **THEN** a URL passa a `/entrar`, o campo "E-mail" vem com o e-mail da consulta, e a tela mostra o aviso `bloqueado` com "5 minutos"

### Requirement: O convite com o provedor
Com o convite válido (a consulta respondeu `200`) e provedores configurados (`GET /auth/federated/providers`, os tipos conhecidos, como no `/entrar`), a página do convite SHALL mostrar o cabeçalho de hoje, sem o papel ("Olá, `<nome>`", "`<quem convidou>` convidou você para o **Buteco Agentes**."), e, acima do formulário da senha nova: o título "Entre com uma conta Google", o botão "Continuar com Google", a nota "A conta precisa ter o e-mail `<e-mail do convite>`." e o separador "ou crie uma senha". Sem provedor, ou com a lista falhando (falha de rede, resposta que não é `200`, ou um `200` fora do formato: sem `provedores`, com `provedores` que não é lista, ou com itens sem `chave` ou `tipo` em texto), a página SHALL ser a de hoje, sem aviso e sem quebrar.

O clique SHALL chamar `POST /auth/invitation/federated/{chave}/start` com `{ token }`, o token do estado da página, e SHALL **gravar a chave `buteco.link.convite` com esse token antes de navegar ao `url`, sempre**, também depois de uma recusa que já a apagou. Durante o pedido e depois do `200`, a página SHALL mostrar "Redirecionando para o Google…" no lugar dos botões e do formulário, sem "Cancelar"; com o `200 { url }`, SHALL navegar ao `url`. As outras respostas:

| resposta | a página |
|---|---|
| `404` com o `title` exatamente igual ao do link inválido ("Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.") | o cartão "Este link não vale mais" de hoje, e a chave apagada |
| `404` com o `title` exatamente "Provedor não configurado." | aviso vermelho "Provedor não configurado."; a lista relida esconde o bloco; o formulário continua |
| qualquer outro `404` (sem corpo, com outro `title`, o do proxy) | aviso vermelho "Não foi possível entrar com o Google. Tente de novo."; a chave SHALL NOT ser apagada, e a lista SHALL NOT ser relida |
| `503` | aviso vermelho "Não foi possível falar com o provedor. Tente de novo em instantes." |
| `429` | aviso amarelo "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." |
| falha de rede | aviso vermelho "Não foi possível falar com o servidor." |
| outra | aviso vermelho "Não foi possível entrar com o Google. Tente de novo." |

#### Scenario: A escolha com o Google
- **WHEN** `/convite#k7Qp2vX9` abre, a consulta responde `200` com o nome "Marcos" e o e-mail `marcos.lima@empresa.com`, e a lista dos provedores tem o Google
- **THEN** a página mostra "Olá, Marcos", o cabeçalho sem o papel, "Entre com uma conta Google", "Continuar com Google", "A conta precisa ter o e-mail marcos.lima@empresa.com.", "ou crie uma senha" e o formulário

#### Scenario: Sem provedor, a página de hoje
- **WHEN** a lista dos provedores responde vazia, ou falha
- **THEN** a página não tem "Entre com uma conta Google" nem o separador, e mostra o formulário da senha nova de hoje, sem aviso

#### Scenario: Redirecionando para o Google
- **WHEN** a pessoa clica em "Continuar com Google", e o início responde `200 { url }`
- **THEN** o corpo do pedido é `{ "token": "k7Qp2vX9" }`, a página mostra "Redirecionando para o Google…" sem "Cancelar", e o painel navega ao `url`

#### Scenario: O provedor indisponível
- **WHEN** o início responde `503`
- **THEN** a página volta à escolha com o aviso vermelho "Não foi possível falar com o provedor. Tente de novo em instantes."

#### Scenario: O provedor não configurado
- **WHEN** o início responde `404` "Provedor não configurado.", e a lista relida responde vazia
- **THEN** a página mostra o aviso vermelho "Provedor não configurado.", sem "Entre com uma conta Google", e o formulário da senha continua

#### Scenario: Muitas tentativas no início do convite
- **WHEN** o início responde `429`
- **THEN** a página volta à escolha com o aviso amarelo "Muitas tentativas ao mesmo tempo. Tente de novo em instantes."

#### Scenario: 404 sem o title conhecido no convite
- **WHEN** o início responde `404` sem corpo, ou com o `title` "Not Found"
- **THEN** a página volta à escolha com o aviso vermelho "Não foi possível entrar com o Google. Tente de novo.", a chave `buteco.link.convite` continua no `sessionStorage`, e a lista não é relida

#### Scenario: A lista fora do formato no convite
- **WHEN** a lista responde `200` com `{}`, com `{ "provedores": "google" }` ou com `{ "provedores": [{ "chave": 1 }] }`
- **THEN** a página é a de hoje, sem "Entre com uma conta Google", sem aviso, e o formulário da senha nova funciona

#### Scenario: O link deixou de valer no início
- **WHEN** o início responde `404` com o corpo do link inválido
- **THEN** a página mostra o cartão "Este link não vale mais", e a chave `buteco.link.convite` não existe mais no `sessionStorage`

### Requirement: As recusas que voltam ao convite
Com `recusa` na query e o token na chave, a página SHALL fazer a consulta do convite e mostrar, acima de "Entre com uma conta Google", o aviso do motivo, com o formulário valendo:

| motivo | cor | título | texto |
|---|---|---|---|
| `outro-email` | vermelho | "E-mail diferente do convite" | "O Google confirmou `<e-mail do provedor>`, mas este convite é para `<e-mail do convite>`. Entre com a conta certa ou crie uma senha abaixo." |
| `email-nao-verificado` | amarelo | "E-mail não confirmado" | "O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou crie uma senha abaixo." |
| `em-uso` | vermelho | "Conta Google já conectada" | "Esta conta Google já está conectada a outro usuário do Buteco Agentes. Entre com outra conta ou crie uma senha abaixo." |
| `dominio-nao-permitido` | vermelho | "Domínio não permitido" | "Esta conta Google não é do domínio permitido neste painel." |
| `falha` e todo motivo desconhecido | vermelho | sem título | "Não foi possível entrar com o Google. Tente de novo." |

No `convite-invalido`, a consulta responde `404`, e a página SHALL mostrar o cartão "Este link não vale mais" de hoje. No `outro-email`, o e-mail do provedor SHALL vir de **uma** chamada a `POST /auth/federated/refusal`, com a mesma guarda do `StrictMode` do `/entrar`; sem ele (o `404`), o aviso SHALL sair como o da volta sem o token. O e-mail SHALL ser usado só quando a recusa gravada é a da query (o `motivo` `outro-email` e, quando a query trouxe `provedor`, o mesmo `provedor`); com outra recusa, o aviso SHALL sair na variante do `outro-email` sem o e-mail do provedor.

Um novo clique no provedor SHALL esconder o aviso da recusa; a resposta do início SHALL mostrar só o seu aviso. Vale para toda resposta que não seja o `200` (o `503`, o `429`, a falha de rede, o `404` do provedor, o `404` sem `title` conhecido e qualquer outra) e para todo motivo, com ou sem o e-mail do provedor; o aviso da recusa SHALL NOT voltar, e a rota do aviso SHALL NOT ser chamada de novo. O `404` do link que deixou de valer continua indo ao cartão "Este link não vale mais".

**Sem o token na chave**, a página SHALL NOT fazer pedido e SHALL mostrar o título "Convite", o aviso do motivo (no `outro-email`, "E-mail diferente do convite" / "O Google confirmou outro e-mail, diferente do e-mail deste convite."; nos outros, o da tabela) e "Abra o link do convite que você recebeu de novo.", sem formulário.

Depois de ler, a página SHALL trocar o endereço por `history.replace`, sem `recusa` e `provedor`. Recarregar depois disso (sem fragmento, sem a chave e sem `recusa`) SHALL mostrar o estado de link inválido de hoje, "Este link não vale mais", sem pedido.

#### Scenario: Outro e-mail
- **WHEN** `/convite?recusa=outro-email&provedor=corporativo` abre com o token na chave, a consulta responde `200` com o e-mail `marcos.lima@empresa.com`, e a rota do aviso responde `200` com o e-mail `marcos.pessoal@gmail.com`
- **THEN** a página mostra o aviso vermelho "E-mail diferente do convite" / "O Google confirmou marcos.pessoal@gmail.com, mas este convite é para marcos.lima@empresa.com. Entre com a conta certa ou crie uma senha abaixo.", os botões e o formulário, e o endereço passa a ser `/convite`

#### Scenario: E-mail não confirmado no convite
- **WHEN** `/convite?recusa=email-nao-verificado&provedor=corporativo` abre com o token na chave
- **THEN** a página mostra o aviso amarelo "E-mail não confirmado" / "O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou crie uma senha abaixo." e o formulário

#### Scenario: Conta Google já conectada
- **WHEN** `/convite?recusa=em-uso&provedor=corporativo` abre com o token na chave
- **THEN** a página mostra o aviso vermelho "Conta Google já conectada" / "Esta conta Google já está conectada a outro usuário do Buteco Agentes. Entre com outra conta ou crie uma senha abaixo." e o formulário

#### Scenario: Domínio não permitido no convite
- **WHEN** `/convite?recusa=dominio-nao-permitido&provedor=corporativo` abre com o token na chave
- **THEN** a página mostra o aviso vermelho "Domínio não permitido" / "Esta conta Google não é do domínio permitido neste painel." e o formulário

#### Scenario: Falha no convite
- **WHEN** `/convite?recusa=falha&provedor=corporativo` abre com o token na chave, ou com um motivo desconhecido
- **THEN** a página mostra o aviso vermelho sem título "Não foi possível entrar com o Google. Tente de novo." e o formulário

#### Scenario: Convite que deixou de valer
- **WHEN** `/convite?recusa=convite-invalido&provedor=corporativo` abre com o token na chave, e a consulta responde `404`
- **THEN** a página mostra o cartão "Este link não vale mais" de hoje, e a chave não existe mais no `sessionStorage`

#### Scenario: Outro e-mail sem o token
- **WHEN** `/convite?recusa=outro-email&provedor=corporativo` abre sem a chave `buteco.link.convite` na aba
- **THEN** nenhum pedido sai, e a página mostra o título "Convite", o aviso vermelho "E-mail diferente do convite" / "O Google confirmou outro e-mail, diferente do e-mail deste convite." e "Abra o link do convite que você recebeu de novo.", sem formulário

#### Scenario: Falha sem o token
- **WHEN** `/convite?recusa=falha&provedor=corporativo` abre sem a chave na aba
- **THEN** nenhum pedido sai, e a página mostra o título "Convite", o aviso vermelho sem título "Não foi possível entrar com o Google. Tente de novo." e "Abra o link do convite que você recebeu de novo.", sem formulário

#### Scenario: Recarregar depois da recusa
- **WHEN** a página da recusa `em-uso` é recarregada, depois de o endereço virar `/convite` e de a chave ser apagada
- **THEN** nenhum pedido sai, e a página mostra "Este link não vale mais"

#### Scenario: Uma leitura só do aviso no convite
- **WHEN** `/convite?recusa=outro-email&provedor=corporativo` monta sob o `StrictMode` com o token na chave
- **THEN** a rota do aviso recebe uma chamada só, e o aviso leva o e-mail do provedor

#### Scenario: O aviso de outra recusa não empresta o e-mail do provedor
- **WHEN** `/convite?recusa=outro-email&provedor=corporativo` abre com o token na chave, a consulta responde `200`, e a rota do aviso responde `200` com outro motivo (`em-uso`) ou outro provedor, e o e-mail `marcos.pessoal@gmail.com`
- **THEN** a página mostra o aviso "E-mail diferente do convite" / "O Google confirmou outro e-mail, diferente do e-mail deste convite.", sem "marcos.pessoal@gmail.com"; a rota do aviso recebeu uma chamada só; o formulário continua

#### Scenario: O novo clique troca o aviso da recusa pelo do início
- **WHEN** depois de `/convite?recusa=outro-email&provedor=corporativo`, com o aviso "E-mail diferente do convite" na tela, o clique em "Continuar com Google" recebe `503`
- **THEN** a página mostra só "Não foi possível falar com o provedor. Tente de novo em instantes.", sem "E-mail diferente do convite"; a rota do aviso recebeu uma chamada só; o formulário continua

#### Scenario: Toda resposta recusada do início troca o aviso da recusa
- **WHEN** a página da recusa (o `outro-email` sem o e-mail, um motivo desconhecido ou o `em-uso`) recebe, no clique do provedor, o `503`, o `429`, a falha de rede, o `404` do provedor, o `404` sem `title` conhecido ou outra resposta
- **THEN** a página mostra só o aviso dessa resposta, a rota do aviso não é chamada de novo, e o formulário continua

