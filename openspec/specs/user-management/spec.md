# user-management Specification

## Purpose
Diz como um usuário gere os outros no `apps/api`, sob `/users`: lista, detalhe com a
contagem das sessões vivas, edição de nome e e-mail, desativação sem nunca deixar a
instalação sem usuário ativo, reativação e o encerramento das sessões de outro. Na M1, sem
papéis, todo usuário real chama todas; a #184 restringe ao Admin. Introduzida pela change
`rotas-conta-usuarios` (#219). A redefinição das 2 etapas, no mesmo prefixo, está em
`two-factor-totp`; o convite e a redefinição de senha por link são da #181; as telas, da
#183.
## Requirements
### Requirement: Rotas de Usuários atendem só o usuário real do token
As rotas desta capability (`GET /users`, `GET /users/{id}`, `PUT /users/{id}`, `POST /users/{id}/deactivate`, `POST /users/{id}/activate` e `POST /users/{id}/sessions/revoke`) SHALL atender todo usuário real do `sub` do access token do emissor, existente e ativo, sem papéis (M1). Token HMAC de operador, `sub` sem usuário e usuário desativado com access token ainda válido SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada.

#### Scenario: HMAC de operador em Usuários
- **WHEN** um token HMAC `operator` válido chama cada uma das seis rotas, com o id de um usuário ativo com sessão viva
- **THEN** todas respondem `403`, e o alvo continua ativo, com o mesmo nome, o mesmo e-mail, o mesmo carimbo e a sessão viva

### Requirement: Lista de usuários
`GET /users` SHALL responder `200` com todos os usuários, ativos e desativados, sem filtro e sem paginação, cada um com `id`, `nome`, `email`, `estado` (`ativo` ou `desativado`), `formasDeEntrada` (`["senha"]` quando há senha gravada; vazia sem senha), `duasEtapas` (verdadeiro quando a verificação está ligada), `ultimoAcesso` e `criadoEm`. A lista SHALL sair ordenada por nome, com desempate pelo `id` como último critério (`api-response-ordering`).

#### Scenario: Campos e estados
- **WHEN** existem um usuário ativo com a verificação ligada e um desativado
- **THEN** os dois aparecem, com `estado` e `duasEtapas` conforme gravados, e `formasDeEntrada` igual a `["senha"]`

#### Scenario: Desempate pelo id
- **WHEN** dois usuários têm o mesmo nome
- **THEN** eles saem em ordem crescente de `id`, e a consulta emitida termina o `ORDER BY` no `id`

### Requirement: Detalhe de usuário com a contagem de sessões
`GET /users/{id}` SHALL responder `200` com os campos da lista, mais `desativadoEm` e `sessoes: { ativas, ultimaAtividade }`, em que `ativas` é o número de sessões vivas do usuário (a definição de `user-account`) e `ultimaAtividade` é o maior último uso entre elas, ou nulo sem sessão. Id inexistente SHALL responder `404`.

#### Scenario: Contagem de sessões de outro usuário
- **WHEN** um usuário com duas sessões vivas, uma renovada depois da outra, é consultado por outro
- **THEN** `sessoes.ativas` é 2, e `sessoes.ultimaAtividade` é o instante da renovação mais recente

#### Scenario: Usuário inexistente
- **WHEN** o detalhe é pedido para um id que não existe
- **THEN** a resposta é `404`

### Requirement: Editar nome e e-mail
`PUT /users/{id}` com `{ nome, email }` SHALL gravar o nome (sem espaços nas pontas, de 1 a 200 caracteres) e o e-mail (sem espaços nas pontas, formato de endereço válido, até 254 caracteres), com o e-mail normalizado recalculado, e responder `200` com o detalhe. O `id` (o `sub`) SHALL NOT mudar. A edição SHALL NOT trocar o carimbo de segurança nem encerrar sessões, e SHALL NOT pedir a senha de ninguém.

Um e-mail cujo normalizado já pertence a outro usuário SHALL responder `409`, sem gravar nada, inclusive quando duas edições concorrentes disputam o mesmo e-mail: a colisão pelo índice único do e-mail normalizado SHALL virar o mesmo `409`, nunca `500`. Mudar só a caixa do próprio e-mail SHALL ser aceito. Campos inválidos SHALL responder `400`, e id inexistente, `404`.

#### Scenario: Editar e entrar pelo e-mail novo
- **WHEN** o e-mail de um usuário é trocado
- **THEN** a resposta é `200` com o mesmo `id`, o login pelo e-mail novo responde `204`, o login pelo antigo responde `401`, e as sessões abertas antes continuam renovando

#### Scenario: E-mail de outro, noutra caixa
- **WHEN** um usuário recebe o e-mail de outro escrito com outra caixa
- **THEN** a resposta é `409`, e nada muda nos dois

#### Scenario: A própria caixa trocada
- **WHEN** um usuário tem o próprio e-mail regravado só com outra caixa
- **THEN** a resposta é `200`

#### Scenario: Duas edições concorrentes para o mesmo e-mail
- **WHEN** dois usuários diferentes são editados ao mesmo tempo para o mesmo e-mail livre
- **THEN** uma resposta é `200` e a outra `409`, e nenhuma é `500`

#### Scenario: Campos inválidos
- **WHEN** o nome é vazio depois de tirar os espaços, ou o e-mail não é um endereço válido
- **THEN** a resposta é `400`, e nada é gravado

### Requirement: Desativar outro usuário, sem nunca ficar sem usuário ativo
`POST /users/{id}/deactivate` SHALL marcar o usuário como desativado, gravar a data, trocar o carimbo de segurança e revogar todas as autorizações dele, numa mesma transação, respondendo `200` com o detalhe. O usuário já desativado SHALL receber `200` com o estado atual, sem gravar nada. O próprio usuário que chama SHALL receber `403`, e id inexistente, `404`.

A desativação SHALL rodar sob o `pg_advisory_xact_lock` de chave `BUTECOD1` (`4779819235982394417`), e SHALL reler dentro do lock quem chama e o alvo: quem chama já desativado SHALL receber `403`, sem gravar nada. Assim, duas desativações concorrentes nunca deixam a instalação sem usuário ativo.

#### Scenario: Desativar outro
- **WHEN** um usuário desativa outro que tem sessões vivas
- **THEN** a resposta é `200` com `estado` `desativado`, a renovação das sessões do outro recebe `invalid_grant`, o login dele com a senha certa responde `403`, e o detalhe dele mostra zero sessões

#### Scenario: Desativar a si mesmo
- **WHEN** um usuário pede a própria desativação
- **THEN** a resposta é `403`, e ele continua ativo

#### Scenario: Desativar de novo
- **WHEN** um usuário já desativado é desativado de novo
- **THEN** a resposta é `200`, e o carimbo dele não muda

#### Scenario: Dois únicos ativos desativando um ao outro
- **WHEN** os dois únicos usuários ativos pedem, ao mesmo tempo, a desativação um do outro
- **THEN** uma resposta é `200` e a outra `403`, um dos dois continua ativo, e a consulta emitida pede o `pg_advisory_xact_lock` antes de gravar o estado

### Requirement: Reativar usuário
`POST /users/{id}/activate` SHALL marcar o usuário como ativo e limpar a data de desativação, sem trocar o carimbo de segurança, respondendo `200` com o detalhe. O usuário já ativo SHALL receber `200`, sem gravar nada, e id inexistente, `404`. As sessões de antes da desativação SHALL NOT voltar: a pessoa entra de novo com a mesma senha, e a verificação em 2 etapas fica como estava.

#### Scenario: Reativar
- **WHEN** um usuário desativado, com a verificação ligada, é reativado
- **THEN** a resposta é `200`, o refresh de antes da desativação recebe `invalid_grant`, o login com a mesma senha pede o segundo passo, e a sessão nova renova

### Requirement: Encerrar todas as sessões de outro usuário
`POST /users/{id}/sessions/revoke` SHALL revogar todas as autorizações do usuário indicado, sem trocar o carimbo de segurança, respondendo `200 { encerradas }`. Id inexistente SHALL responder `404`. Chamada com o próprio id SHALL ser aceita e encerrar todas as sessões de quem chama, inclusive a atual.

#### Scenario: Encerrar as sessões de outro
- **WHEN** um usuário encerra as sessões de outro que tem duas sessões vivas
- **THEN** a resposta é `200` com `encerradas` igual a 2, a renovação das duas recebe `invalid_grant`, e o login seguinte do outro abre uma sessão nova que renova

