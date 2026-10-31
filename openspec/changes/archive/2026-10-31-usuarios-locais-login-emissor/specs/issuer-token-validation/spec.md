## MODIFIED Requirements

### Requirement: Acordo com o emissor real
O access token de usuário emitido pelo `apps/api` real, pelo fluxo authorization code + PKCE do cliente `buteco-painel`, SHALL ser aceito pelo app, com as chaves obtidas do `apps/api` pelo endereço interno, e SHALL continuar aceito depois que o `apps/api` deixar de responder. O teste de acordo SHALL usar esse fluxo real, e SHALL NOT cadastrar um cliente `buteco-painel` próprio.

#### Scenario: Token de usuário do apps/api real aceito pelo apps/inbox
- **WHEN** um usuário criado pelo bootstrap do `apps/api` real abre a sessão, o fluxo do `buteco-painel` com PKCE emite o access token, e o token chama `GET /channels` no `apps/inbox`
- **THEN** a resposta é `200 OK`, e repetida depois de o `apps/api` ficar inalcançável continua `200 OK`

#### Scenario: Token de usuário do apps/api real aceito pelo apps/connectors, e recusado com a aud do outro
- **WHEN** o access token de usuário do mesmo fluxo chama `GET /connectors/providers`, e um token emitido pelo `apps/api` real por `client_credentials` para um cliente de teste fora do prefixo `buteco-`, só com o recurso `buteco-inbox`, chama a mesma rota
- **THEN** o primeiro recebe `200 OK` e o segundo `401 Unauthorized`
