# user-access-links Specification

## Purpose
O link de acesso de uso único do `apps/api` (#181): o convite que cria o usuário no estado `convidado` e a redefinição de senha gerada pelo admin. O token vai no fragmento da URL e só o hash fica no banco. As rotas administrativas pedem a senha de quem chama, e as públicas, sob `/auth`, dão uma resposta só para todo link inválido. Sem envio de e-mail: o admin entrega o link.
## Requirements
### Requirement: Link de acesso de uso único, guardado só em hash
O `apps/api` SHALL gerar o token de cada link de acesso (convite ou redefinição de senha) com 32 bytes de um gerador criptográfico, codificados em base64url sem preenchimento (43 caracteres), e SHALL guardar só o SHA-256 do token, nunca o token. Cada usuário SHALL ter no máximo um link pendente, com o propósito (`invitation` ou `password_reset`) e o instante de vencimento. O vencimento SHALL ser de 72 horas para o convite e de 24 horas para a redefinição, contadas do momento em que o link é gerado. Gerar um link novo para o mesmo usuário SHALL invalidar o anterior.

#### Scenario: Só o hash é gravado
- **WHEN** um link é gerado
- **THEN** o token devolvido tem 43 caracteres base64url, e nenhuma coluna do banco contém o token, só o SHA-256 dele

#### Scenario: Gerar de novo invalida o anterior
- **WHEN** um link é gerado para um usuário que já tinha outro pendente
- **THEN** o link anterior recebe a resposta de link inválido, e o novo é aceito

### Requirement: O link leva o token no fragmento, a partir da base configurada do painel
O link devolvido SHALL ser `<base do painel>/convite#<token>` para o convite e `<base do painel>/redefinir-senha#<token>` para a redefinição, com o token só depois de `#`. A base do painel SHALL ser `Identity:PanelBaseUrl` quando configurada e, sem ela, `PublicUrl:BaseUrl`, a mesma base dos endereços de retorno do cliente `buteco-painel`. A base SHALL NOT vir do `Host`, do `X-Forwarded-Host` nem do esquema da requisição. Toda rota que recebe um token SHALL recebê-lo no corpo de um `POST`, nunca no caminho nem na query.

#### Scenario: Base configurada e token no fragmento
- **WHEN** um convite é criado com `Identity:PanelBaseUrl` igual a `http://localhost:5173`
- **THEN** o link começa por `http://localhost:5173/convite#`, e o caminho não contém o token

#### Scenario: Host forjado não muda o link
- **WHEN** um convite é criado por uma requisição com `Host: evil.example`
- **THEN** o link usa a base configurada, e não `evil.example`

#### Scenario: O token não chega aos logs
- **WHEN** um convite é criado, consultado e aceito com o log de hosting do ASP.NET Core em `Information`
- **THEN** nenhuma linha de log capturada contém o token

### Requirement: Convidar cria o usuário no estado convidado
`POST /users/invitations` com `{ nome, email, senha }` SHALL criar um usuário no estado convidado, sem senha, com quem chama registrado como quem convidou, gerar o link de convite e responder `201 { usuario, link, expiraEm }`. O link SHALL ser devolvido só nesta resposta. O nome e o e-mail SHALL seguir as regras da edição de usuário (`user-management`), com `400` para campos inválidos. Um e-mail cujo normalizado já pertence a um usuário ativo, desativado ou convidado SHALL responder `409` "Já existe um usuário com este e-mail.", sem gravar nada, inclusive quando dois convites concorrentes disputam o mesmo e-mail: a colisão pelo índice único SHALL virar o mesmo `409`, nunca `500`.

#### Scenario: Convite criado
- **WHEN** um usuário convida um e-mail livre com a própria senha certa
- **THEN** a resposta é `201` com o link e a validade de 72 horas, e o convidado aparece na lista com `estado` `convidado`

#### Scenario: E-mail de um convite pendente, noutra caixa
- **WHEN** um usuário convida um e-mail que já tem convite pendente, escrito com outra caixa
- **THEN** a resposta é `409`, e nada é gravado

#### Scenario: E-mail de um desativado
- **WHEN** um usuário convida o e-mail de um usuário desativado
- **THEN** a resposta é `409`

#### Scenario: Dois convites concorrentes para o mesmo e-mail
- **WHEN** dois convites para o mesmo e-mail livre chegam ao mesmo tempo
- **THEN** uma resposta é `201` e a outra `409`, e nenhuma é `500`

### Requirement: Gerar novo link e cancelar o convite
`POST /users/{id}/invitation/renew` com `{ senha }` SHALL gerar um novo link para o convidado, vencido ou não, com nova validade de 72 horas, e responder `200 { link, expiraEm }`. Para usuário que não está no estado convidado, SHALL responder `409`.

`POST /users/{id}/invitation/cancel`, sem corpo e sem a senha de quem chama, SHALL apagar o usuário convidado, o que invalida o link e libera o e-mail, e responder `204`. A exclusão SHALL ser condicionada ao estado convidado na mesma instrução, de modo que um convite aceito no meio nunca é apagado: o usuário que não está no estado convidado SHALL receber `409` "O convite já foi aceito.". Id inexistente SHALL responder `404` nas duas rotas.

#### Scenario: Novo link para convite vencido
- **WHEN** um convite venceu e o admin gera novo link
- **THEN** a resposta é `200`, o link novo é aceito por 72 horas, e o antigo continua inválido

#### Scenario: Cancelar libera o e-mail
- **WHEN** um convite é cancelado
- **THEN** a resposta é `204`, o link recebe a resposta de link inválido, o usuário some da lista, e um novo convite para o mesmo e-mail responde `201`

#### Scenario: Cancelar um convite já aceito
- **WHEN** o cancelamento chega depois de o convite ter sido aceito
- **THEN** a resposta é `409`, e o usuário continua ativo

#### Scenario: Gerar novo link para um ativo
- **WHEN** o novo link de convite é pedido para um usuário ativo
- **THEN** a resposta é `409`

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

### Requirement: Criar convite, gerar novo link e gerar redefinição pedem a senha de quem chama
`POST /users/invitations`, `POST /users/{id}/invitation/renew` e `POST /users/{id}/password-reset` SHALL conferir a senha atual de quem chama, recebida no campo `senha` do corpo, como o login confere. Durante o bloqueio do e-mail de quem chama, SHALL responder `423` com `Retry-After`, sem conferir a senha e sem gravar nada. Senha ausente ou errada SHALL responder `401` "Senha inválida.", contar uma falha no bloqueio do e-mail de quem chama (e não no do alvo) e não gravar nada. O acerto SHALL NOT zerar a contagem. `POST /users/{id}/invitation/cancel` SHALL NOT pedir senha.

#### Scenario: Senha errada não gera link
- **WHEN** um usuário cria um convite, gera um novo link e gera uma redefinição, cada vez com a própria senha errada
- **THEN** as três respostas são `401`, nenhum usuário é criado, o hash do link do convite continua o mesmo, e nenhum link de redefinição é gravado

#### Scenario: A falha conta no bloqueio de quem chama
- **WHEN** um usuário erra a própria senha cinco vezes seguidas nessas rotas e tenta a sexta com a senha certa
- **THEN** as cinco primeiras respondem `401`, a sexta responde `423` com `Retry-After` sem gravar nada, e o bloqueio do e-mail do alvo não muda

#### Scenario: Cancelar não pede senha
- **WHEN** um convite é cancelado sem `senha` no corpo
- **THEN** a resposta é `204`

### Requirement: Rotas administrativas do link atendem só o usuário real do token
As quatro rotas administrativas (`POST /users/invitations`, `POST /users/{id}/invitation/renew`, `POST /users/{id}/invitation/cancel` e `POST /users/{id}/password-reset`) SHALL atender todo usuário real do `sub` do access token do emissor, existente e ativo, sem papéis (M1). Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário) e usuário desativado com access token ainda válido SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada.

#### Scenario: Operador sem usuário nas rotas do link
- **WHEN** um access token do `buteco-painel` cujo `sub` não é id de usuário chama cada uma das quatro rotas
- **THEN** todas respondem `403`, e nenhum usuário, link ou bloqueio é gravado

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

### Requirement: O que invalida o link
O link SHALL deixar de valer quando: um link novo é gerado para o mesmo usuário; o link é usado; o convite é cancelado; o usuário é desativado; o usuário troca a própria senha (o link de redefinição); ou o vencimento passa. Reativar um usuário SHALL NOT devolver o link apagado pela desativação. Editar nome ou e-mail, redefinir as 2 etapas e encerrar sessões SHALL NOT mudar o link.

#### Scenario: Vencido
- **WHEN** o link de convite é usado depois de 72 horas, ou o de redefinição depois de 24 horas
- **THEN** a consulta e o uso recebem a resposta de link inválido

#### Scenario: Desativar apaga o link
- **WHEN** um usuário com link de redefinição pendente é desativado e depois reativado
- **THEN** o link recebe a resposta de link inválido nas duas situações

#### Scenario: Link de um propósito na rota do outro
- **WHEN** um link de redefinição é usado na rota do convite, ou um de convite na rota da redefinição
- **THEN** a resposta é a de link inválido

### Requirement: Rotas públicas do link: barreiras, limite e resposta única
As quatro rotas públicas (`POST /auth/invitation`, `POST /auth/invitation/accept`, `POST /auth/password-reset` e `POST /auth/password-reset/complete`) SHALL ser anônimas e SHALL aplicar as barreiras do login antes de tudo: só `application/json` (`415`) e `Origin` igual à do issuer ou à de `Identity:PanelBaseUrl` (`403`). O aceite e a conclusão SHALL entrar no limite global de verificações de senha (`local-users`); as duas consultas SHALL NOT entrar.

Link inexistente, vencido, usado, cancelado, substituído, de outro propósito ou de usuário fora do estado esperado SHALL receber a mesma resposta: `404` "Este link não vale mais. Se você já criou a senha, entre normalmente; se não, peça um novo link ao administrador.", com o mesmo corpo em todos os casos e sem calcular hash de senha.

#### Scenario: Resposta única para link inválido
- **WHEN** a consulta e o uso recebem um token inexistente, um vencido, um já usado, um cancelado e um de outro propósito
- **THEN** todas as respostas têm o mesmo status e o mesmo corpo

#### Scenario: Origem de fora
- **WHEN** uma rota pública recebe um `POST` JSON com `Origin` diferente da do issuer e da do painel
- **THEN** a resposta é `403`, e nada é consultado nem gravado

#### Scenario: Aceite e conclusão no limite global
- **WHEN** o aceite recebe mais pedidos simultâneos do que o limite global de verificações de senha admite
- **THEN** os excedentes esperam na fila ou recebem `429` com `Retry-After`, como no login

