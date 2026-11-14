# panel-federated-login Specification

## Purpose
O login do painel pelo provedor federado (o Google, #189): os botões no `/entrar` pela rota anônima dos provedores, o início com o `retorno` conferido, a volta pela query (o passo do código ou o cadastro, com o aviso verde), os avisos de cada recusa e a leitura única do e-mail confirmado. Change `telas-login-google` (#190).
## Requirements
### Requirement: Os botões dos provedores no /entrar
O `/entrar` SHALL ler `GET /auth/federated/providers` na montagem. Com a lista vazia, a tela SHALL ser a de hoje, sem o separador. Com provedores, SHALL mostrar, abaixo do formulário de senha, o separador "ou entre com" e um botão por provedor de tipo conhecido, com o rótulo do tipo: para `google`, "Entrar com Google", sem subtítulo. Um provedor de tipo desconhecido SHALL NOT ganhar botão. Com a lista falhando (falha de rede, resposta que não é `200`, ou um `200` fora do formato: sem `provedores`, com `provedores` que não é lista, ou com itens sem `chave` ou `tipo` em texto), o separador e os botões SHALL NOT aparecer, nenhum aviso SHALL ser mostrado, e a página SHALL NOT quebrar; o login por senha SHALL continuar.

#### Scenario: Com o Google configurado
- **WHEN** o `/entrar` abre e a lista responde `{ "provedores": [{ "chave": "corporativo", "tipo": "google" }] }`
- **THEN** abaixo do botão "Entrar" aparecem o separador "ou entre com" e o botão "Entrar com Google", sem subtítulo

#### Scenario: Sem provedor configurado
- **WHEN** a lista responde `{ "provedores": [] }`
- **THEN** a tela não tem o separador "ou entre com" nem botão de provedor

#### Scenario: Tipo desconhecido não ganha botão
- **WHEN** a lista responde com um provedor `google` e outro de `tipo` `keycloak`
- **THEN** só o botão "Entrar com Google" aparece

#### Scenario: A lista falhando esconde o bloco sem aviso
- **WHEN** a lista responde `500`, ou o pedido falha por rede
- **THEN** a tela não tem o separador nem botão de provedor, não mostra aviso, e o login por senha continua

#### Scenario: A lista fora do formato
- **WHEN** a lista responde `200` com `{}`, com `{ "provedores": "google" }` ou com `{ "provedores": [{ "chave": 1 }] }`
- **THEN** a tela não tem o separador nem botão de provedor, não mostra aviso, não quebra, e o login por senha continua

### Requirement: O início do login pelo provedor
O clique no botão do provedor SHALL chamar `POST /auth/federated/{chave}/start` com `{ retorno }`, em que `retorno` é o parâmetro `retorno` do `/entrar` quando ele começa por `/connect/authorize?`, e `null` nos outros casos. Durante o pedido e depois do `200`, o cartão SHALL mostrar "Redirecionando para o Google…", sem "Cancelar", no lugar do formulário. Com o `200 { url }`, o painel SHALL navegar ao `url`. As outras respostas SHALL voltar ao formulário com o aviso:

| resposta | aviso |
|---|---|
| `503` | vermelho, "Não foi possível falar com o provedor. Tente de novo em instantes." |
| `404` com o `title` exatamente "Provedor não configurado." | vermelho, "Provedor não configurado."; a lista SHALL ser relida, e o botão SHALL sumir com ela |
| qualquer outro `404` (sem corpo, com outro `title`, o do proxy) | vermelho, "Não foi possível entrar com o Google. Tente de novo."; a lista SHALL NOT ser relida |
| `429` | amarelo, "Muitas tentativas ao mesmo tempo. Tente de novo em instantes." |
| falha de rede | vermelho, "Não foi possível falar com o servidor." |
| outra | vermelho, "Não foi possível entrar com o Google. Tente de novo." |

#### Scenario: Redirecionando para o Google
- **WHEN** a pessoa clica em "Entrar com Google" no `/entrar?retorno=%2Fconnect%2Fauthorize%3Fclient_id%3Dbuteco-painel%26…`, e o início responde `200 { "url": "https://accounts.google.com/o/oauth2/v2/auth?…" }`
- **THEN** o corpo do pedido é `{ "retorno": "/connect/authorize?client_id=buteco-painel&…" }`, o cartão mostra "Redirecionando para o Google…" sem "Cancelar", e o painel navega ao `url`

#### Scenario: Sem retorno do authorize
- **WHEN** o `/entrar` abre sem `retorno`, ou com um `retorno` que não começa por `/connect/authorize?`, e a pessoa clica em "Entrar com Google"
- **THEN** o corpo do pedido é `{ "retorno": null }`

#### Scenario: O Google fora do alcance
- **WHEN** o início responde `503`
- **THEN** o formulário volta com o aviso vermelho "Não foi possível falar com o provedor. Tente de novo em instantes."

#### Scenario: O provedor saiu da configuração
- **WHEN** o início responde `404` "Provedor não configurado.", e a lista relida responde vazia
- **THEN** o formulário volta com o aviso vermelho "Provedor não configurado.", sem o separador nem o botão, e o login por senha continua

#### Scenario: 404 sem o title conhecido no login
- **WHEN** o início responde `404` sem corpo, ou com o `title` "Not Found"
- **THEN** o formulário volta com o aviso vermelho "Não foi possível entrar com o Google. Tente de novo.", a lista não é relida, e o botão continua

#### Scenario: Muitas tentativas no início
- **WHEN** o início responde `429`
- **THEN** o formulário volta com o aviso amarelo "Muitas tentativas ao mesmo tempo. Tente de novo em instantes."

#### Scenario: Falha de rede no início
- **WHEN** o pedido do início falha por rede
- **THEN** o formulário volta com o aviso vermelho "Não foi possível falar com o servidor."

### Requirement: A volta do provedor no /entrar
O `/entrar` SHALL ler a etapa também da query: `segundoPasso=totp` SHALL abrir o passo do código, e `segundoPasso=cadastro` SHALL abrir o cadastro das 2 etapas, os dois de hoje, com o aviso verde acima deles:

| query | aviso |
|---|---|
| `federado` e `segundoPasso=totp`, sem `convite` | "O Google confirmou a sua conta. Falta o código do app autenticador para entrar." |
| `federado` e `segundoPasso=cadastro`, sem `convite` | "O Google confirmou a sua conta. Falta só cadastrar o app autenticador para entrar." |
| `federado`, `segundoPasso=cadastro` e `convite=aceito` | só "Convite aceito. Falta cadastrar o app autenticador para entrar." |

Com `federado` e um `segundoPasso` diferente de `totp` e de `cadastro`, a página SHALL abrir o e-mail e senha, sem aviso (convenção 13). Com `convite=aceito`, a página SHALL apagar a chave `buteco.link.convite` do `sessionStorage` **antes** de abrir o cadastro. Depois de ler, a página SHALL trocar o endereço por `history.replace`, sem `federado`, `segundoPasso` e `convite`, mantendo o `retorno`. Daí em diante, o segundo passo, o cadastro e o `continueEntry` com o `retorno` SHALL ser os de hoje.

#### Scenario: Volta do Google para o código
- **WHEN** o `/entrar?federado=corporativo&segundoPasso=totp&retorno=%2Fconnect%2Fauthorize%3F…` abre
- **THEN** a tela abre o passo do código com o aviso verde "O Google confirmou a sua conta. Falta o código do app autenticador para entrar.", e o endereço passa a ser `/entrar?retorno=%2Fconnect%2Fauthorize%3F…`

#### Scenario: Volta do Google para o cadastro
- **WHEN** o `/entrar?federado=corporativo&segundoPasso=cadastro` abre
- **THEN** a tela abre o cadastro das 2 etapas com o aviso verde "O Google confirmou a sua conta. Falta só cadastrar o app autenticador para entrar.", e o endereço passa a ser `/entrar`

#### Scenario: Convite aceito pelo Google
- **WHEN** o `/entrar?federado=corporativo&segundoPasso=cadastro&convite=aceito` abre, com `buteco.link.convite` na aba
- **THEN** a chave `buteco.link.convite` não existe mais no `sessionStorage` quando o cadastro aparece, o aviso verde é só "Convite aceito. Falta cadastrar o app autenticador para entrar.", e o endereço passa a ser `/entrar`

#### Scenario: segundoPasso desconhecido
- **WHEN** o `/entrar?federado=corporativo&segundoPasso=xyz` abre
- **THEN** a tela abre o e-mail e senha, sem aviso, e o endereço passa a ser `/entrar`, sem `federado` e `segundoPasso`

#### Scenario: Recarregar depois de ler volta ao e-mail e senha
- **WHEN** a página do passo do código vindo do Google é recarregada, depois de o endereço perder `federado` e `segundoPasso`
- **THEN** a tela abre no e-mail e senha, sem o aviso verde

#### Scenario: O código completa a entrada com o retorno
- **WHEN** depois da volta para o código, o segundo passo responde `204`
- **THEN** o painel navega ao `retorno` conferido, como no login por senha

### Requirement: As recusas no /entrar
Com `recusa` na query, o `/entrar` SHALL mostrar o aviso do motivo acima do formulário de senha, e SHALL trocar o rótulo do botão do provedor nos motivos marcados:

| motivo | cor | título | texto | botão |
|---|---|---|---|---|
| `sem-acesso`, com o e-mail | vermelho | "Conta sem acesso" | "O Google confirmou `<e-mail>`, mas essa conta não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador." | "Entrar com outra conta Google" |
| `sem-acesso`, sem o e-mail | vermelho | "Conta sem acesso" | "Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador." | "Entrar com outra conta Google" |
| `convite-pendente` | azul | "Você tem um convite pendente" | "Abra o link do convite que você recebeu para ativar o acesso. Se o link expirou, peça um novo ao administrador." | "Entrar com Google" |
| `nao-conectada` | amarelo | "Esta conta Google ainda não está conectada" | "Entre com seu e-mail e senha e conecte o Google em Minha conta → Formas de entrada. Depois disso, o botão do Google passa a funcionar." | "Entrar com Google" |
| `desativado` | vermelho | "Acesso desativado" | "Seu acesso ao Buteco Agentes foi desativado. Fale com o administrador do painel." | "Entrar com Google" |
| `email-nao-verificado` | amarelo | "E-mail não confirmado" | "O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou com e-mail e senha." | "Entrar com outra conta Google" |
| `dominio-nao-permitido` | vermelho | "Domínio não permitido" | "Esta conta Google não é do domínio permitido neste painel." | "Entrar com outra conta Google" |
| `falha` e todo motivo desconhecido | vermelho | sem título | "Não foi possível entrar com o Google. Tente de novo." | "Entrar com Google" |

Depois de ler, a página SHALL trocar o endereço por `history.replace`, sem `recusa` e `provedor`, mantendo o `retorno`. Nenhuma recusa SHALL abrir o passo do código nem o cadastro.

#### Scenario: Conta sem acesso, com o e-mail
- **WHEN** o `/entrar?recusa=sem-acesso&provedor=corporativo` abre, e a rota do aviso responde `200 { "motivo": "sem-acesso", "provedor": "corporativo", "email": "maria@empresa.com" }`
- **THEN** a tela mostra o aviso vermelho "Conta sem acesso" / "O Google confirmou maria@empresa.com, mas essa conta não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador.", o botão "Entrar com outra conta Google", e o endereço passa a ser `/entrar`

#### Scenario: Conta sem acesso, sem o e-mail
- **WHEN** o `/entrar?recusa=sem-acesso&provedor=corporativo` abre, e a rota do aviso responde `404`
- **THEN** a tela mostra o aviso vermelho "Conta sem acesso" / "Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador." e o botão "Entrar com outra conta Google"

#### Scenario: Convite pendente
- **WHEN** o `/entrar?recusa=convite-pendente&provedor=corporativo` abre
- **THEN** a tela mostra o aviso azul "Você tem um convite pendente" / "Abra o link do convite que você recebeu para ativar o acesso. Se o link expirou, peça um novo ao administrador." e o botão "Entrar com Google"

#### Scenario: Conta Google não conectada
- **WHEN** o `/entrar?recusa=nao-conectada&provedor=corporativo` abre
- **THEN** a tela mostra o aviso amarelo "Esta conta Google ainda não está conectada" / "Entre com seu e-mail e senha e conecte o Google em Minha conta → Formas de entrada. Depois disso, o botão do Google passa a funcionar." e o botão "Entrar com Google"

#### Scenario: Acesso desativado
- **WHEN** o `/entrar?recusa=desativado&provedor=corporativo` abre
- **THEN** a tela mostra o aviso vermelho "Acesso desativado" / "Seu acesso ao Buteco Agentes foi desativado. Fale com o administrador do painel." e o botão "Entrar com Google"

#### Scenario: E-mail não confirmado
- **WHEN** o `/entrar?recusa=email-nao-verificado&provedor=corporativo` abre
- **THEN** a tela mostra o aviso amarelo "E-mail não confirmado" / "O Google não confirmou o e-mail desta conta. Entre com outra conta Google ou com e-mail e senha." e o botão "Entrar com outra conta Google"

#### Scenario: Domínio não permitido
- **WHEN** o `/entrar?recusa=dominio-nao-permitido&provedor=corporativo` abre
- **THEN** a tela mostra o aviso vermelho "Domínio não permitido" / "Esta conta Google não é do domínio permitido neste painel." e o botão "Entrar com outra conta Google"

#### Scenario: Falha
- **WHEN** o `/entrar?recusa=falha&provedor=corporativo` abre, ou o `/entrar?recusa=falha` sem `provedor`
- **THEN** a tela mostra o aviso vermelho sem título "Não foi possível entrar com o Google. Tente de novo." e o botão "Entrar com Google"

#### Scenario: Motivo desconhecido sai como a falha
- **WHEN** o `/entrar?recusa=motivo-novo&provedor=corporativo` abre
- **THEN** a tela mostra o aviso vermelho sem título "Não foi possível entrar com o Google. Tente de novo.", e não uma tela sem aviso

#### Scenario: A limpeza mantém o retorno
- **WHEN** o `/entrar?recusa=desativado&provedor=corporativo&retorno=%2Fconnect%2Fauthorize%3F…` abre
- **THEN** depois de mostrar o aviso, o endereço passa a ser `/entrar?retorno=%2Fconnect%2Fauthorize%3F…`, e recarregar abre o formulário sem o aviso

### Requirement: O e-mail confirmado da recusa é lido uma vez
Na recusa `sem-acesso`, o `/entrar` SHALL chamar `POST /auth/federated/refusal` (JSON, sem corpo de dados) **uma única vez por montagem**, inclusive sob o `StrictMode` do React, cujo efeito duplo SHALL NOT disparar uma segunda chamada. Com `200`, o aviso SHALL levar o e-mail; com `404` ou falha de rede, SHALL sair na variante sem o e-mail. Os outros motivos SHALL NOT chamar a rota. O e-mail SHALL NOT ir para a URL, para o `sessionStorage`, para o `localStorage` nem para log.

O e-mail SHALL ser usado só quando a recusa gravada é a da query: o `motivo` da resposta igual ao da query e, quando a query trouxe `provedor`, o `provedor` da resposta igual a ele. Fora disso (o cookie de outra recusa, gravado e não lido dentro dos 120 s), o aviso SHALL sair na variante sem o e-mail.

#### Scenario: Uma chamada só no StrictMode
- **WHEN** o `/entrar?recusa=sem-acesso&provedor=corporativo` monta sob o `StrictMode`, e a rota do aviso responde `200` com o e-mail na primeira chamada e `404` em qualquer chamada seguinte
- **THEN** a rota recebe uma chamada só, e o aviso mostra o e-mail

#### Scenario: A variante sem o e-mail depois do prazo
- **WHEN** o `/entrar?recusa=sem-acesso&provedor=corporativo` abre depois de passados os 120 segundos do cookie, e a rota do aviso responde `404`
- **THEN** o aviso sai na variante sem o e-mail

#### Scenario: O aviso de outra recusa não empresta o e-mail
- **WHEN** o `/entrar?recusa=sem-acesso&provedor=corporativo` abre, e a rota do aviso responde `200 { motivo: "nao-conectada", provedor: "corporativo", email: "maria@empresa.com" }`
- **THEN** o aviso sai na variante sem o e-mail, sem "maria@empresa.com", e a rota recebe uma chamada só

#### Scenario: O aviso de outro provedor não empresta o e-mail
- **WHEN** o `/entrar?recusa=sem-acesso&provedor=corporativo` abre, e a rota do aviso responde `200 { motivo: "sem-acesso", provedor: "outro", email: "maria@empresa.com" }`
- **THEN** o aviso sai na variante sem o e-mail

#### Scenario: Sem o provedor na query, o motivo igual basta
- **WHEN** o `/entrar?recusa=sem-acesso` abre, sem `provedor`, e a rota do aviso responde `200 { motivo: "sem-acesso", provedor: "corporativo", email: "maria@empresa.com" }`
- **THEN** o aviso leva "maria@empresa.com"

#### Scenario: Falha de rede na leitura do aviso
- **WHEN** o `/entrar?recusa=sem-acesso&provedor=corporativo` abre, e o pedido a `POST /auth/federated/refusal` falha por rede
- **THEN** o aviso sai na variante sem o e-mail, "Conta sem acesso" / "Essa conta Google não tem acesso ao Buteco Agentes. O acesso é liberado só por convite do administrador."

#### Scenario: Os outros motivos não leem o aviso
- **WHEN** o `/entrar?recusa=convite-pendente&provedor=corporativo` abre
- **THEN** nenhuma chamada a `POST /auth/federated/refusal` sai

