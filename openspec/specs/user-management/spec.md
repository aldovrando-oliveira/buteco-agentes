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
As rotas desta capability (`GET /users`, `GET /users/{id}`, `PUT /users/{id}`, `PUT /users/{id}/role`, `POST /users/{id}/deactivate`, `POST /users/{id}/activate` e `POST /users/{id}/sessions/revoke`) SHALL atender só o usuário real do `sub` do access token do emissor, existente, ativo e com o papel `admin` **gravado**, além do `role` `admin` no token (`route-roles`). Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário), usuário desativado com access token ainda válido e usuário cujo papel gravado não é `admin` SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada.

#### Scenario: Operador sem usuário em Usuários
- **WHEN** um access token do `buteco-painel` com `role` `admin` cujo `sub` não é id de usuário chama cada uma das sete rotas, com o id de um usuário ativo com sessão viva
- **THEN** todas respondem `403`, e o alvo continua ativo, com o mesmo nome, o mesmo e-mail, o mesmo papel, o mesmo carimbo e a sessão viva

#### Scenario: Operador e Leitor em Usuários
- **WHEN** um usuário ativo de papel `operator`, e outro de papel `reader`, chamam cada uma das sete rotas
- **THEN** todas respondem `403`, e nada é gravado

#### Scenario: Token de Admin de quem já foi rebaixado
- **WHEN** um usuário rebaixado a `operator` chama `GET /users` com o access token emitido quando era Admin, ainda no prazo
- **THEN** a resposta é `403`

### Requirement: Lista de usuários
`GET /users` SHALL responder `200` com todos os usuários, ativos, desativados e convidados, sem filtro e sem paginação, cada um com `id`, `nome`, `email`, `estado` (`ativo`, `desativado` ou `convidado`), `formasDeEntrada` (`["senha"]` quando há senha gravada; vazia sem senha), `duasEtapas` (verdadeiro quando a verificação está ligada), `ultimoAcesso`, `criadoEm` e `convite`. `convite` SHALL ser `{ expiraEm, vencido }` no convidado, com `vencido` calculado pelo relógio do servidor, e nulo nos outros. O convite vencido SHALL continuar na lista até gerar novo link ou cancelar. A lista SHALL sair ordenada por nome, com desempate pelo `id` como último critério (`api-response-ordering`).

#### Scenario: Campos e estados
- **WHEN** existem um usuário ativo com a verificação ligada e um desativado
- **THEN** os dois aparecem, com `estado` e `duasEtapas` conforme gravados, `formasDeEntrada` igual a `["senha"]` e `convite` nulo

#### Scenario: Convite pendente e convite vencido
- **WHEN** existem um convite dentro da validade e um vencido
- **THEN** os dois aparecem com `estado` `convidado`, `formasDeEntrada` vazia, `duasEtapas` falso, `ultimoAcesso` nulo, e `convite.vencido` falso no primeiro e verdadeiro no segundo

#### Scenario: Desempate pelo id
- **WHEN** dois usuários têm o mesmo nome
- **THEN** eles saem em ordem crescente de `id`, e a consulta emitida termina o `ORDER BY` no `id`

### Requirement: Detalhe de usuário com a contagem de sessões
`GET /users/{id}` SHALL responder `200` com os campos da lista, mais `desativadoEm`, `convidadoPor` e `sessoes: { ativas, ultimaAtividade }`, em que `ativas` é o número de sessões vivas do usuário (a definição de `user-account`) e `ultimaAtividade` é o maior último uso entre elas, ou nulo sem sessão. `convidadoPor` SHALL ser `{ id, nome }` de quem criou o convite, também depois de aceito, e nulo para quem não entrou por convite. Id inexistente SHALL responder `404`.

#### Scenario: Contagem de sessões de outro usuário
- **WHEN** um usuário com duas sessões vivas, uma renovada depois da outra, é consultado por outro
- **THEN** `sessoes.ativas` é 2, e `sessoes.ultimaAtividade` é o instante da renovação mais recente

#### Scenario: Quem convidou
- **WHEN** o detalhe de um usuário que aceitou um convite é consultado
- **THEN** `convidadoPor` traz o id e o nome de quem convidou, e `criadoEm` é o instante do convite

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
`POST /users/{id}/deactivate` SHALL marcar o usuário como desativado, gravar a data, trocar o carimbo de segurança, apagar o link de redefinição pendente e revogar todas as autorizações dele, numa mesma transação, respondendo `200` com o detalhe. O usuário já desativado SHALL receber `200` com o estado atual, sem gravar nada. O convidado SHALL receber `409` "Este usuário ainda não aceitou o convite.", sem gravar nada: o convite sai pelo cancelamento. O próprio usuário que chama SHALL receber `403`, e id inexistente, `404`.

A desativação SHALL rodar sob o `pg_advisory_xact_lock` de chave `BUTECOD1` (`4779819235982394417`), a mesma da mudança de papel, e SHALL reler dentro do lock quem chama e o alvo: quem chama já desativado, ou que já não é Admin, SHALL receber `403`, sem gravar nada. Assim, duas desativações concorrentes, ou uma desativação e um rebaixamento concorrentes, nunca deixam a instalação sem Admin ativo.

#### Scenario: Desativar outro
- **WHEN** um Admin desativa outro usuário que tem sessões vivas
- **THEN** a resposta é `200` com `estado` `desativado`, a renovação das sessões do outro recebe `invalid_grant`, o login dele com a senha certa responde `403`, e o detalhe dele mostra zero sessões

#### Scenario: Desativar o convidado
- **WHEN** um convidado é desativado
- **THEN** a resposta é `409`, ele continua no estado convidado, e o link do convite continua valendo

#### Scenario: Desativar a si mesmo
- **WHEN** um Admin pede a própria desativação
- **THEN** a resposta é `403`, e ele continua ativo

#### Scenario: Desativar de novo
- **WHEN** um usuário já desativado é desativado de novo
- **THEN** a resposta é `200`, e o carimbo dele não muda

#### Scenario: Dois únicos Admins desativando um ao outro
- **WHEN** os dois únicos Admins ativos pedem, ao mesmo tempo, a desativação um do outro
- **THEN** uma resposta é `200` e a outra `403`, um dos dois continua ativo e Admin, e a consulta emitida pede o `pg_advisory_xact_lock` antes de gravar o estado

#### Scenario: Desativar e rebaixar ao mesmo tempo
- **WHEN** dos dois únicos Admins ativos, um desativa o outro enquanto o outro rebaixa o primeiro
- **THEN** uma resposta é `200` e a outra `403`, e sobra um Admin ativo

### Requirement: Reativar usuário
`POST /users/{id}/activate` SHALL marcar o usuário desativado como ativo e limpar a data de desativação, sem trocar o carimbo de segurança, respondendo `200` com o detalhe. O usuário já ativo SHALL receber `200`, sem gravar nada. O convidado SHALL receber `409` "Este usuário ainda não aceitou o convite.", sem gravar nada: ele só fica ativo pelo aceite do convite. Id inexistente SHALL responder `404`. As sessões de antes da desativação SHALL NOT voltar: a pessoa entra de novo com a mesma senha, e a verificação em 2 etapas fica como estava. O link de redefinição apagado pela desativação SHALL NOT voltar.

#### Scenario: Reativar
- **WHEN** um usuário desativado, com a verificação ligada, é reativado
- **THEN** a resposta é `200`, o refresh de antes da desativação recebe `invalid_grant`, o login com a mesma senha pede o segundo passo, e a sessão nova renova

#### Scenario: Reativar o convidado
- **WHEN** um convidado é reativado
- **THEN** a resposta é `409`, e ele continua no estado convidado, sem senha

### Requirement: Encerrar todas as sessões de outro usuário
`POST /users/{id}/sessions/revoke` SHALL revogar todas as autorizações do usuário indicado, sem trocar o carimbo de segurança, respondendo `200 { encerradas }`. Id inexistente SHALL responder `404`. Chamada com o próprio id SHALL ser aceita e encerrar todas as sessões de quem chama, inclusive a atual.

#### Scenario: Encerrar as sessões de outro
- **WHEN** um usuário encerra as sessões de outro que tem duas sessões vivas
- **THEN** a resposta é `200` com `encerradas` igual a 2, a renovação das duas recebe `invalid_grant`, e o login seguinte do outro abre uma sessão nova que renova

### Requirement: Mudar o papel de um usuário
`PUT /users/{id}/role` com `{ papel, senha? }` (`papel` em `admin`, `operador` ou `leitor`, pela tabela de correspondência do design, D3) SHALL gravar o papel do usuário indicado (ativo ou convidado; o desativado SHALL receber `409`) e responder `200` com o detalhe. Papel igual ao gravado SHALL responder `200` sem gravar nada. Papel fora dos três valores SHALL responder `400`, e id inexistente, `404`.

- **Promover a Admin** (papel novo `admin`, alvo de outro papel) SHALL pedir a senha atual de quem chama no campo `senha`, com as regras de "Criar convite, gerar novo link e gerar redefinição pedem a senha de quem chama" (`user-access-links`): `423` durante o bloqueio do e-mail de quem chama, `401` "Senha inválida." contado nesse bloqueio, nada gravado. Rebaixar SHALL NOT pedir senha.
- **A mudança SHALL rodar sob o `pg_advisory_xact_lock` `BUTECOD1`** (`4779819235982394417`), a mesma chave da desativação, relendo dentro do lock quem chama e o alvo. Quem chama que já não é Admin ativo SHALL receber `403`, sem gravar nada.
- **O último Admin:** tirar o papel `admin` de um Admin quando ele é o único Admin ativo, contado dentro do lock, SHALL responder `409` "A instalação precisa de pelo menos um Admin ativo.", sem gravar nada.
- **A si mesmo:** quem chama SHALL poder mudar o próprio papel, sujeito à regra do último Admin.
- Mudar o papel SHALL NOT trocar o carimbo de segurança nem revogar sessões (`route-roles`, "Prazos da mudança de papel").

#### Scenario: Rebaixar outro Admin
- **WHEN** um de dois Admins ativos rebaixa o outro a `operador`
- **THEN** a resposta é `200` com `papel` `operador`, o carimbo do alvo não muda, e a sessão dele continua renovando, já com `role` `operator`

#### Scenario: Rebaixar a si mesmo com outro Admin
- **WHEN** um de dois Admins ativos rebaixa a si mesmo a `leitor`
- **THEN** a resposta é `200`, e o `GET /users` seguinte dele, com o mesmo access token, responde `403`

#### Scenario: O último Admin não se rebaixa
- **WHEN** o único Admin ativo pede o próprio rebaixamento
- **THEN** a resposta é `409`, e ele continua `admin`

#### Scenario: Dois Admins rebaixando um ao outro
- **WHEN** os dois únicos Admins ativos pedem, ao mesmo tempo, o rebaixamento um do outro
- **THEN** uma resposta é `200` e a outra `403`, um dos dois continua `admin`, e a consulta emitida pede o `pg_advisory_xact_lock` antes de gravar o papel

#### Scenario: Promover pede a senha
- **WHEN** um Admin promove um Operador a `admin` sem `senha`, e depois com a própria senha certa
- **THEN** a primeira resposta é `401`, contada no bloqueio do e-mail de quem chama, o alvo continua `operador`, e a segunda é `200` com `papel` `admin`

#### Scenario: Papel inválido
- **WHEN** o corpo traz `papel` `superusuario`
- **THEN** a resposta é `400`, e nada é gravado

### Requirement: O papel na lista e no detalhe
`GET /users` e `GET /users/{id}` SHALL trazer, em cada usuário, o campo `papel` (`admin`, `operador` ou `leitor`), também no convidado.

#### Scenario: Papéis na lista
- **WHEN** existem um Admin, um Operador e um convite de Leitor
- **THEN** a lista traz `papel` `admin`, `operador` e `leitor`, cada um no seu usuário

