## MODIFIED Requirements

### Requirement: Rotas de Minha conta atendem só o usuário real do token
As rotas de `/account` desta capability (`GET /account`, `POST /account/password`, `GET /account/sessions`, `POST /account/sessions/{id}/revoke` e `POST /account/sessions/revoke-others`) SHALL atender só o usuário do `sub` do access token do emissor, existente e ativo. Token de operador cujo `sub` não é usuário (cliente `buteco-painel`, subject `operator`, `sub` que não é id de usuário) e usuário desativado com access token ainda válido SHALL receber `403`, antes de qualquer outra conferência e sem gravar nada.

#### Scenario: Operador sem usuário em Minha conta
- **WHEN** um access token do `buteco-painel` cujo `sub` não é id de usuário chama cada uma das cinco rotas
- **THEN** todas respondem `403`, e nenhuma senha, carimbo ou autorização muda

#### Scenario: Usuário desativado com access token válido
- **WHEN** um usuário é desativado e, com o access token emitido antes, chama `GET /account`
- **THEN** a resposta é `403`
