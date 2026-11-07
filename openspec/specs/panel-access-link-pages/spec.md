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
- ao sair da página, quando ela desmonta.

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
| `204` | segue o fluxo do emissor para a tela inicial (`/`) |
| `200` com `{ "segundoPasso": "totp" }` | navega a `/entrar` com o `state` `{ etapa: "codigo" }`, e o `/entrar` abre no passo do código com o aviso "Senha salva…" (`operator-login-ui`) |
| qualquer outra | navega a `/entrar` com o `state` `{ email, status }`, e o `/entrar` abre com o e-mail preenchido e o aviso daquele status (`operator-login-ui`) |

A senha SHALL ficar só na memória da página, entre a conclusão e o login, e SHALL NOT ir ao `state` do router nem a armazenamento nenhum.

#### Scenario: Aceite seguido do login
- **WHEN** o aceite responde `204` e o `POST /auth/session` com `marcos.lima@empresa.com` e a senha digitada responde `204`
- **THEN** o painel navega ao authorize, com `/` como `voltarPara`

#### Scenario: Redefinição de quem tem as 2 etapas chega ao passo do código
- **WHEN** a redefinição responde `204` e o `POST /auth/session` responde `200` com `{ "segundoPasso": "totp" }`
- **THEN** a URL passa a `/entrar`, a tela mostra o passo do código com "Senha salva. A sua conta usa verificação em 2 etapas: digite o código para entrar.", e o `state` do router não contém a senha

#### Scenario: Login recusado depois do aceite chega ao /entrar com o e-mail
- **WHEN** o aceite responde `204` e o `POST /auth/session` responde `423` com `Retry-After: 300`
- **THEN** a URL passa a `/entrar`, o campo "E-mail" vem com o e-mail da consulta, e a tela mostra o aviso `bloqueado` com "5 minutos"

