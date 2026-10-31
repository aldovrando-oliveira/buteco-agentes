## REMOVED Requirements

### Requirement: O access token de client_credentials não gera entrada de token
**Reason**: a regra cresce. Nenhum access token, de nenhum grant, e nenhum id token gera linha, porque os três apps validam pelo JWKS e ninguém lê essas linhas (#180, D3.8 e D3.9).
**Migration**: substituído pelo requisito "Access token e id token não geram entrada de token". O comportamento do `client_credentials` não muda.

## ADDED Requirements

### Requirement: Access token e id token não geram entrada de token
Com o emissor ligado, nenhum access token, de nenhum grant, e nenhum id token SHALL gerar linha em `openiddict_tokens`. Os tokens SHALL continuar verificáveis pelo JWKS exatamente como antes. O código de autorização e o refresh token SHALL continuar gerando a linha que o OpenIddict grava por padrão. O `client_credentials` SHALL continuar sem gerar linha em `openiddict_authorizations`.

#### Scenario: Pedido de client_credentials não grava nada
- **WHEN** um cliente confidencial cadastrado obtém um access token por `client_credentials`
- **THEN** a resposta traz o access token, o token é verificável pelo JWKS do emissor, e `openiddict_tokens` e `openiddict_authorizations` continuam sem nenhuma linha

#### Scenario: Fluxo do usuário grava só código e refresh
- **WHEN** um usuário completa o fluxo authorization code com `openid` e `offline_access`
- **THEN** `openiddict_tokens` tem a linha do código de autorização (resgatado) e a do refresh token (`valid`), ligadas a uma autorização `valid`, e nenhuma linha de access token nem de id token

#### Scenario: Renovação grava só o refresh novo
- **WHEN** o refresh token do usuário é trocado por um novo par
- **THEN** entra uma linha de refresh token, e nenhuma de access token nem de id token

#### Scenario: Só código e refresh ficam ligados
- **WHEN** o OpenIddict gera um token de qualquer tipo (access, refresh, id, código de autorização) em qualquer grant (`client_credentials`, `authorization_code`, `refresh_token`)
- **THEN** a criação da linha fica desligada para todo access token e todo id token, e ligada para o refresh token e o código de autorização
