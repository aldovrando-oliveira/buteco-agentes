## MODIFIED Requirements

### Requirement: Consultar e aceitar o convite
`POST /auth/invitation` com `{ token }` SHALL responder `200 { nome, email, convidadoPor, expiraEm }` só para um link de convite válido (existente, de propósito `invitation`, de usuário convidado e não vencido), sem gravar nada.

`POST /auth/invitation/accept` com `{ token, senha }` SHALL conferir a senha nova pela política de senha (`user-account`), com `400` fora dela, e procurar o link antes de calcular o hash. Com o link válido, SHALL gravar o hash Argon2id da senha, pôr o usuário no estado ativo e apagar o link numa única instrução condicionada ao hash do link, ao propósito, ao vencimento e ao estado convidado, de modo que dois aceites do mesmo link nunca têm sucesso os dois. Em seguida SHALL zerar o bloqueio do e-mail do usuário e responder `204`, sem abrir sessão. A sessão SHALL nascer só do login com a senha criada.

O convite SHALL poder ser aceito também pelo provedor federado (`federated-login`, "Decisão do aceite de convite no retorno"), sem senha. A condição de consumo (o hash do link, o propósito, o vencimento e o estado convidado) SHALL ser a mesma nos dois aceites, de modo que o mesmo convite nunca é aceito pelos dois; o consumo pelo provedor acrescenta a ela o e-mail normalizado e não grava senha.

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

#### Scenario: Aceite por senha depois do aceite pelo provedor
- **WHEN** um convite é aceito pelo provedor, e o mesmo link é usado em `POST /auth/invitation/accept` com uma senha dentro da política
- **THEN** a resposta é a de link inválido, e o usuário continua sem senha

### Requirement: Rotas públicas do link: barreiras, limite e resposta única
As cinco rotas públicas (`POST /auth/invitation`, `POST /auth/invitation/accept`, `POST /auth/password-reset`, `POST /auth/password-reset/complete` e o início do aceite pelo provedor, `POST /auth/invitation/federated/{chave}/start`, de `federated-login`) SHALL ser anônimas e SHALL aplicar as barreiras do login antes de tudo: só `application/json` (`415`) e `Origin` igual à do issuer ou à de `Identity:PanelBaseUrl` (`403`). O aceite, a conclusão e o início do aceite pelo provedor SHALL entrar no limite global de verificações de senha (`local-users`); as duas consultas SHALL NOT entrar.

Link inexistente, vencido, usado, cancelado, substituído, de outro propósito ou de usuário fora do estado esperado SHALL receber a mesma resposta: `404` "Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.", com o mesmo corpo em todos os casos e sem calcular hash de senha. No início do aceite pelo provedor, a mesma resposta SHALL valer, sem cookie do fluxo e sem pedido ao provedor.

#### Scenario: Resposta única para link inválido
- **WHEN** a consulta e o uso recebem um token inexistente, um vencido, um já usado, um cancelado e um de outro propósito
- **THEN** todas as respostas têm o mesmo status e o mesmo corpo

#### Scenario: Origem de fora
- **WHEN** uma rota pública recebe um `POST` JSON com `Origin` diferente da do issuer e da do painel
- **THEN** a resposta é `403`, e nada é consultado nem gravado

#### Scenario: Aceite e conclusão no limite global
- **WHEN** o aceite recebe mais pedidos simultâneos do que o limite global de verificações de senha admite
- **THEN** os excedentes esperam na fila ou recebem `429` com `Retry-After`, como no login

#### Scenario: Resposta única também no início do aceite pelo provedor
- **WHEN** `POST /auth/invitation` e `POST /auth/invitation/federated/corporativo/start` recebem o mesmo token vencido
- **THEN** as duas respostas têm o mesmo status e o mesmo corpo, e a do início não traz `Set-Cookie`
