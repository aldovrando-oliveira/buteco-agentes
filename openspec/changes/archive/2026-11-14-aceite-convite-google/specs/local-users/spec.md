## MODIFIED Requirements

### Requirement: Usuário ativo sem senha
Um usuário ativo SHALL poder não ter senha gravada, quando a forma de entrada dele é só um provedor federado (`federated-login`). O caminho de produção que cria esse usuário SHALL ser o aceite do convite pelo provedor (`federated-login`, "Decisão do aceite de convite no retorno"); o comando `password-reset-link` SHALL continuar servindo só o Admin ativo (`user-access-links`), e um Operador ou um Leitor sem senha SHALL ganhar a primeira pelo link de redefinição que um Admin gera. Para ele, o login por senha SHALL responder como e-mail sem conta, **com o mesmo custo** (a verificação contra o hash fictício) e contando a falha no bloqueio do e-mail; a troca da própria senha (`POST /account/password`) SHALL responder `401` "Senha atual inválida." como a senha atual errada; e a confirmação de quem chama SHALL ser pelo código do app (`user-access-links`). O link de redefinição gerado pelo Admin (`user-access-links`) SHALL gravar a primeira senha dele, como grava a nova senha de quem já tem.

#### Scenario: Login por senha de quem não tem senha
- **WHEN** um usuário ativo sem senha recebe `POST /auth/session` com o e-mail dele e uma senha qualquer
- **THEN** a resposta é `401` com a mensagem genérica, sem cookie, e a falha conta no bloqueio do e-mail

#### Scenario: A redefinição grava a primeira senha
- **WHEN** o Admin gera o link de redefinição de um usuário ativo sem senha, e o link é concluído com uma senha válida
- **THEN** o usuário passa a ter senha, `formasDeEntrada` passa a começar por `"senha"`, e o login por senha passa a pedir o segundo passo

#### Scenario: O aceite pelo provedor cria o usuário sem senha
- **WHEN** um convite de papel `operador` é aceito pelo provedor, e o mesmo e-mail tenta `POST /auth/session` com uma senha qualquer
- **THEN** o usuário está `ativo`, com `formasDeEntrada` `["google"]`, e o login por senha responde `401` com a mensagem genérica, contando a falha no bloqueio do e-mail
