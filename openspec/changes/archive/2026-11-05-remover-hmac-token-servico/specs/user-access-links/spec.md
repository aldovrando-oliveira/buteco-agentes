## MODIFIED Requirements

### Requirement: Rotas administrativas do link atendem só o usuário real do token
As quatro rotas administrativas (`POST /users/invitations`, `POST /users/{id}/invitation/renew`, `POST /users/{id}/invitation/cancel` e `POST /users/{id}/password-reset`) SHALL atender todo usuário real do `sub` do access token do emissor, existente e ativo, sem papéis (M1). Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário) e usuário desativado com access token ainda válido SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada.

#### Scenario: Operador sem usuário nas rotas do link
- **WHEN** um access token do `buteco-painel` cujo `sub` não é id de usuário chama cada uma das quatro rotas
- **THEN** todas respondem `403`, e nenhum usuário, link ou bloqueio é gravado
