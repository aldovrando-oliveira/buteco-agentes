## MODIFIED Requirements

### Requirement: Rotas de Usuários atendem só o usuário real do token
As rotas desta capability (`GET /users`, `GET /users/{id}`, `PUT /users/{id}`, `POST /users/{id}/deactivate`, `POST /users/{id}/activate` e `POST /users/{id}/sessions/revoke`) SHALL atender todo usuário real do `sub` do access token do emissor, existente e ativo, sem papéis (M1). Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário) e usuário desativado com access token ainda válido SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada.

#### Scenario: Operador sem usuário em Usuários
- **WHEN** um access token do `buteco-painel` cujo `sub` não é id de usuário chama cada uma das seis rotas, com o id de um usuário ativo com sessão viva
- **THEN** todas respondem `403`, e o alvo continua ativo, com o mesmo nome, o mesmo e-mail, o mesmo carimbo e a sessão viva
