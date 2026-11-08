## MODIFIED Requirements

### Requirement: Gerar o link de redefinição de senha
`POST /users/{id}/password-reset` com `{ senha }` SHALL gerar o link de redefinição para um usuário ativo e responder `200 { link, expiraEm }`. Gerar o link SHALL NOT mudar a senha, o carimbo de segurança nem as sessões do alvo: a senha atual continua valendo até a nova ser criada. O próprio usuário que chama SHALL receber `403` "Use Minha conta para trocar a própria senha.", antes de conferir a senha. O convidado e o desativado SHALL receber `409`, e id inexistente, `404`.

#### Scenario: A senha antiga continua valendo
- **WHEN** um link de redefinição é gerado para um usuário
- **THEN** a resposta é `200` com validade de 24 horas, o login com a senha atual dele continua aceito (`200` com `{ "segundoPasso": "totp" }`), e as sessões dele continuam renovando

#### Scenario: Para si mesmo
- **WHEN** um usuário pede o link de redefinição da própria senha
- **THEN** a resposta é `403`, e nenhum link é gerado

#### Scenario: Para convidado ou desativado
- **WHEN** o link de redefinição é pedido para um convidado ou para um desativado
- **THEN** a resposta é `409`

### Requirement: Consultar e aceitar o convite
`POST /auth/invitation` com `{ token }` SHALL responder `200 { nome, email, convidadoPor, expiraEm }` só para um link de convite válido (existente, de propósito `invitation`, de usuário convidado e não vencido), sem gravar nada.

`POST /auth/invitation/accept` com `{ token, senha }` SHALL conferir a senha nova pela política de senha (`user-account`), com `400` fora dela, e procurar o link antes de calcular o hash. Com o link válido, SHALL gravar o hash Argon2id da senha, pôr o usuário no estado ativo e apagar o link numa única instrução condicionada ao hash do link, ao propósito, ao vencimento e ao estado convidado, de modo que dois aceites do mesmo link nunca têm sucesso os dois. Em seguida SHALL zerar o bloqueio do e-mail do usuário e responder `204`, sem abrir sessão. A sessão SHALL nascer só do login com a senha criada.

#### Scenario: Consulta do convite
- **WHEN** o link de um convite válido é consultado
- **THEN** a resposta é `200` com o nome e o e-mail do convidado, o nome de quem convidou e o vencimento, e nada muda no banco

#### Scenario: Aceitar e entrar
- **WHEN** o convidado aceita com uma senha dentro da política e depois entra com o e-mail e essa senha
- **THEN** o aceite responde `204` sem cookie de sessão, o usuário fica `ativo`, e o login responde `200` com `{ "segundoPasso": "cadastro" }`, porque o convidado ainda não tem a verificação

#### Scenario: Senha fora da política
- **WHEN** o aceite recebe uma senha de 11 caracteres
- **THEN** a resposta é `400`, e o link continua valendo

#### Scenario: Dois aceites concorrentes
- **WHEN** o mesmo link é aceito duas vezes ao mesmo tempo, com senhas diferentes
- **THEN** uma resposta é `204` e a outra é a de link inválido, só a senha do aceite bem-sucedido entra, e a instrução emitida grava e consome numa única atualização condicionada ao hash, ao vencimento e ao estado

#### Scenario: O aceite zera o bloqueio
- **WHEN** o e-mail do convidado está bloqueado por tentativas de login e o convite é aceito
- **THEN** o login seguinte com a senha criada responde `200` com `{ "segundoPasso": "cadastro" }`, e não `423`

### Requirement: Consultar o link de redefinição e concluir a redefinição
`POST /auth/password-reset` com `{ token }` SHALL responder `200 { nome, email, expiraEm }` só para um link de redefinição válido (existente, de propósito `password_reset`, de usuário ativo e não vencido), sem gravar nada.

`POST /auth/password-reset/complete` com `{ token, senha }` SHALL conferir a política e o link como o aceite. Com o link válido, SHALL, numa mesma transação, gravar o hash Argon2id da senha nova, tirar o marcador de hash legado, trocar o carimbo de segurança e apagar o link numa única instrução condicionada ao hash, ao propósito, ao vencimento e ao estado ativo, e revogar todas as autorizações do usuário. Em seguida SHALL zerar o bloqueio do e-mail do usuário e responder `204`, sem abrir sessão. A verificação em 2 etapas do usuário SHALL NOT mudar.

#### Scenario: Concluir a redefinição
- **WHEN** a pessoa conclui a redefinição com uma senha dentro da política
- **THEN** a resposta é `204`, o login com a senha antiga responde `401`, e o login com a nova responde `200` (com `{ "segundoPasso": "totp" }` para quem tem a verificação, e `{ "segundoPasso": "cadastro" }` para quem não tem)

#### Scenario: Concluir encerra todas as sessões
- **WHEN** a redefinição é concluída para um usuário com duas sessões vivas
- **THEN** a renovação das duas recebe `invalid_grant`, e a lista de sessões vivas dele fica vazia

#### Scenario: As 2 etapas continuam
- **WHEN** a redefinição é concluída para um usuário com a verificação em 2 etapas ligada
- **THEN** o login com a senha nova responde `200 { "segundoPasso": "totp" }`

#### Scenario: Duas conclusões concorrentes
- **WHEN** o mesmo link de redefinição é concluído duas vezes ao mesmo tempo
- **THEN** uma resposta é `204` e a outra é a de link inválido
