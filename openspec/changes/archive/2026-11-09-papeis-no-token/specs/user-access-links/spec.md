## MODIFIED Requirements

### Requirement: Convidar cria o usuário no estado convidado
`POST /users/invitations` com `{ nome, email, papel, senha }` SHALL criar um usuário no estado convidado, sem senha, com o papel pedido e com quem chama registrado como quem convidou, gerar o link de convite e responder `201 { usuario, link, expiraEm }`. O `papel` SHALL ser obrigatório (`admin`, `operador` ou `leitor`), e ausente ou fora dos três SHALL responder `400`, sem gravar nada e antes de conferir a senha. O aceite SHALL manter o papel do convite. O link SHALL ser devolvido só nesta resposta. O nome e o e-mail SHALL seguir as regras da edição de usuário (`user-management`), com `400` para campos inválidos. Um e-mail cujo normalizado já pertence a um usuário ativo, desativado ou convidado SHALL responder `409` "Já existe um usuário com este e-mail.", sem gravar nada, inclusive quando dois convites concorrentes disputam o mesmo e-mail: a colisão pelo índice único SHALL virar o mesmo `409`, nunca `500`.

#### Scenario: Convite criado
- **WHEN** um Admin convida um e-mail livre com `papel` `operador` e a própria senha certa
- **THEN** a resposta é `201` com o link e a validade de 72 horas, e o convidado aparece na lista com `estado` `convidado` e `papel` `operador`

#### Scenario: Convite sem papel
- **WHEN** um Admin convida um e-mail livre sem o campo `papel`
- **THEN** a resposta é `400`, nada é gravado, e o bloqueio do e-mail de quem chama não muda

#### Scenario: O aceite mantém o papel
- **WHEN** um convite de `papel` `leitor` é aceito, e o convidado entra pelo fluxo do painel
- **THEN** o access token dele traz `role` `reader`

#### Scenario: E-mail de um convite pendente, noutra caixa
- **WHEN** um Admin convida um e-mail que já tem convite pendente, escrito com outra caixa
- **THEN** a resposta é `409`, e nada é gravado

#### Scenario: E-mail de um desativado
- **WHEN** um Admin convida o e-mail de um usuário desativado
- **THEN** a resposta é `409`

#### Scenario: Dois convites concorrentes para o mesmo e-mail
- **WHEN** dois convites para o mesmo e-mail livre chegam ao mesmo tempo
- **THEN** uma resposta é `201` e a outra `409`, e nenhuma é `500`

### Requirement: Rotas administrativas do link atendem só o usuário real do token
As quatro rotas administrativas (`POST /users/invitations`, `POST /users/{id}/invitation/renew`, `POST /users/{id}/invitation/cancel` e `POST /users/{id}/password-reset`) SHALL atender só o usuário real do `sub` do access token do emissor, existente, ativo e com o papel `admin` gravado, além do `role` `admin` no token (`route-roles`). Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário), usuário desativado com access token ainda válido e usuário cujo papel gravado não é `admin` SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada. O papel do alvo SHALL NOT restringir as rotas: um Admin SHALL poder gerar o link de redefinição de senha de outro Admin.

#### Scenario: Operador sem usuário nas rotas do link
- **WHEN** um access token do `buteco-painel` com `role` `admin` cujo `sub` não é id de usuário chama cada uma das quatro rotas
- **THEN** todas respondem `403`, e nenhum usuário, link ou bloqueio é gravado

#### Scenario: Operador nas rotas do link
- **WHEN** um usuário ativo de papel `operator` chama cada uma das quatro rotas, com a própria senha certa
- **THEN** todas respondem `403`, e nenhum usuário, link ou bloqueio é gravado

#### Scenario: Redefinição de outro Admin
- **WHEN** um Admin gera, com a própria senha certa, o link de redefinição de outro Admin ativo
- **THEN** a resposta é `200` com o link
