## ADDED Requirements

### Requirement: O state token do cliente OIDC gera entrada de uso único
Cada início do fluxo federado (`federated-login`) SHALL gerar uma linha de state token em `openiddict_tokens`, sem autorização ligada, que o retorno SHALL resgatar: um state token SHALL ser aceito uma vez só. Essas linhas SHALL NOT aparecer como sessão (`user-account`), e SHALL ser removidas pela mesma limpeza das outras entradas mortas, com a mesma idade mínima. O fluxo do usuário por senha SHALL continuar gravando só o código de autorização e o refresh token.

#### Scenario: O início grava o state, e o retorno o resgata
- **WHEN** um fluxo federado começa e o retorno é aceito
- **THEN** `openiddict_tokens` tem uma linha de state token, sem autorização, já resgatada, e nenhuma sessão nova aparece em `GET /account/sessions` antes do segundo passo

#### Scenario: A limpeza remove o state morto
- **WHEN** a limpeza roda com uma linha de state token resgatada há mais de 14 dias
- **THEN** a linha é removida
