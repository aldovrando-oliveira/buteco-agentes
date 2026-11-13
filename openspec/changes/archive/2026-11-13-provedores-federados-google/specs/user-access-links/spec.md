## MODIFIED Requirements

### Requirement: Criar convite, gerar novo link e gerar redefinição pedem a senha de quem chama
`POST /users/invitations`, `POST /users/{id}/invitation/renew` e `POST /users/{id}/password-reset` SHALL conferir a **confirmação de quem chama**. A mesma confirmação SHALL valer nas outras rotas que a pedem: promover a Admin (`user-management`), criar a aplicação externa e gerar segredo (`external-applications`) e conectar provedor (`federated-login`).

- **Quem tem senha gravada** SHALL confirmar com a senha atual no campo `senha` do corpo, conferida como o login confere. Senha ausente ou errada SHALL responder `401` "Senha inválida.". Um `codigo` no corpo SHALL NOT substituir a senha de quem a tem: sem `senha` certa, a resposta é o mesmo `401`.
- **Quem não tem senha gravada** SHALL confirmar com o código do app autenticador no campo `codigo`, conferido como o segundo passo confere o código do app, inclusive a recusa do passo já usado. O código de recuperação SHALL NOT valer aqui. Código ausente, errado, já usado ou de recuperação SHALL responder `401` "Código inválido.". Segredo que não decifra SHALL responder `500` "Não foi possível conferir o código do app autenticador. Fale com o administrador.", com o log de erro que já existe para esse caso.

Durante o bloqueio do e-mail de quem chama, SHALL responder `423` com `Retry-After`, sem conferir nada e sem gravar nada. Toda confirmação recusada SHALL contar uma falha no bloqueio do e-mail de quem chama (e não no do alvo) e não gravar nada. O acerto SHALL NOT zerar a contagem. `POST /users/{id}/invitation/cancel` SHALL NOT pedir confirmação.

#### Scenario: Senha errada não gera link
- **WHEN** um usuário cria um convite, gera um novo link e gera uma redefinição, cada vez com a própria senha errada
- **THEN** as três respostas são `401`, nenhum usuário é criado, o hash do link do convite continua o mesmo, e nenhum link de redefinição é gravado

#### Scenario: A falha conta no bloqueio de quem chama
- **WHEN** um usuário erra a própria senha cinco vezes seguidas nessas rotas e tenta a sexta com a senha certa
- **THEN** as cinco primeiras respondem `401`, a sexta responde `423` com `Retry-After` sem gravar nada, e o bloqueio do e-mail do alvo não muda

#### Scenario: Cancelar não pede senha
- **WHEN** um convite é cancelado sem `senha` no corpo
- **THEN** a resposta é `204`

#### Scenario: Quem tem senha não confirma com o código
- **WHEN** um Admin com senha cria um convite com um `codigo` válido do app e sem `senha`
- **THEN** a resposta é `401` "Senha inválida.", contada no bloqueio do e-mail dele, e nenhum usuário é criado

#### Scenario: Quem não tem senha confirma com o código do app
- **WHEN** um Admin sem senha, com a verificação ligada, cria um convite com um `codigo` válido do app
- **THEN** a resposta é a do sucesso, e o convite é criado

#### Scenario: O código do app não vale duas vezes
- **WHEN** o mesmo Admin sem senha usa o mesmo código, já aceito, numa segunda confirmação
- **THEN** a resposta é `401` "Código inválido.", contada no bloqueio

#### Scenario: Código de recuperação não confirma
- **WHEN** o mesmo Admin sem senha confirma com um código de recuperação válido
- **THEN** a resposta é `401` "Código inválido.", e o código de recuperação continua sem uso
