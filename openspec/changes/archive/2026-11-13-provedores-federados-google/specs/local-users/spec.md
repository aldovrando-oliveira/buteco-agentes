## ADDED Requirements

### Requirement: Usuário ativo sem senha
Um usuário ativo SHALL poder não ter senha gravada, quando a forma de entrada dele é só um provedor federado (`federated-login`). Para ele, o login por senha SHALL responder como e-mail sem conta, **com o mesmo custo** (a verificação contra o hash fictício) e contando a falha no bloqueio do e-mail; a troca da própria senha (`POST /account/password`) SHALL responder `401` "Senha atual inválida." como a senha atual errada; e a confirmação de quem chama SHALL ser pelo código do app (`user-access-links`). O link de redefinição gerado pelo Admin (`user-access-links`) SHALL gravar a primeira senha dele, como grava a nova senha de quem já tem.

#### Scenario: Login por senha de quem não tem senha
- **WHEN** um usuário ativo sem senha recebe `POST /auth/session` com o e-mail dele e uma senha qualquer
- **THEN** a resposta é `401` com a mensagem genérica, sem cookie, e a falha conta no bloqueio do e-mail

#### Scenario: A redefinição grava a primeira senha
- **WHEN** o Admin gera o link de redefinição de um usuário ativo sem senha, e o link é concluído com uma senha válida
- **THEN** o usuário passa a ter senha, `formasDeEntrada` passa a começar por `"senha"`, e o login por senha passa a pedir o segundo passo
