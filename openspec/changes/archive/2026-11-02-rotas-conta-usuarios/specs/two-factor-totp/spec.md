## MODIFIED Requirements

### Requirement: Redefinição da verificação pelo admin
`POST /users/{id}/two-factor/reset` SHALL apagar o segredo, o cadastro pendente, a data de ativação, o último passo e os códigos de recuperação do usuário, trocar o `security_stamp` dele e revogar todas as autorizações dele no emissor, numa mesma transação, respondendo `204`. Usuário inexistente SHALL responder `404`; usuário sem a verificação ligada, `409`, sem trocar o stamp e sem revogar nada; e o próprio usuário que chama, `403`.

A revogação junto da troca do stamp é a regra de `user-account` ("Toda troca do carimbo de segurança revoga as sessões da pessoa"): sem ela, as sessões mortas pelo stamp continuariam na lista de sessões vivas por até 14 dias.

#### Scenario: Redefinir outro usuário
- **WHEN** um usuário redefine a verificação de outro que a tem ligada
- **THEN** a resposta é `204`, o login seguinte do outro responde `204` sem o segundo passo, e a próxima renovação de uma sessão do outro, aberta antes, recebe `invalid_grant`

#### Scenario: A redefinição encerra as sessões na lista
- **WHEN** um usuário com duas sessões vivas tem a verificação redefinida por outro
- **THEN** o detalhe dele em `GET /users/{id}` mostra zero sessões ativas, e as duas autorizações estão revogadas

#### Scenario: Redefinir a si mesmo
- **WHEN** um usuário chama a redefinição com o próprio id
- **THEN** a resposta é `403`, e a verificação dele continua ligada

#### Scenario: Redefinir quem não tem a verificação
- **WHEN** a redefinição é pedida para um usuário sem a verificação ligada
- **THEN** a resposta é `409`, o `security_stamp` dele não muda, e as sessões dele continuam vivas

#### Scenario: Redefinir usuário inexistente
- **WHEN** a redefinição é pedida para um id que não existe
- **THEN** a resposta é `404`
