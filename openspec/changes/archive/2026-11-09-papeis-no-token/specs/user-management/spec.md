## ADDED Requirements

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

## MODIFIED Requirements

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
