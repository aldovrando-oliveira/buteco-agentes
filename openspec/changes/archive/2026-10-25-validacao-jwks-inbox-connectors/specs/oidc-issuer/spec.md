## MODIFIED Requirements

### Requirement: Token emitido é JWT assinado, verificável pelo JWKS, com aud, kid e iat
Para um cliente confidencial cadastrado, com permissão de `client_credentials` e de um escopo associado a um recurso, o endpoint de token SHALL emitir um access token no formato JWT **não cifrado**, assinado com a chave de assinatura ativa. O token SHALL conter:

- `iss` igual ao issuer configurado;
- `sub` igual ao `client_id`;
- `client_id` igual ao identificador do cliente que pediu o token (RFC 9068), em todo fluxo habilitado;
- `aud` com o(s) recurso(s) do(s) escopo(s) concedido(s);
- `iat` e `exp`.

O cabeçalho do JWT SHALL conter o `kid` da chave ativa e o `typ` `at+jwt`. A assinatura SHALL ser verificável só com as chaves publicadas em `/.well-known/jwks`.

#### Scenario: Token de client_credentials verificável pelo JWKS
- **WHEN** um cliente cadastrado só para o teste, com o escopo `teste.api` associado ao recurso `buteco-teste`, envia `POST /connect/token` com `grant_type=client_credentials`, as próprias credenciais e `scope=teste.api`
- **THEN** a resposta é `200 OK` com um `access_token` JWT de três partes, cujo cabeçalho tem o `kid` da chave ativa e `typ` `at+jwt`, cuja assinatura valida contra as chaves de `/.well-known/jwks` com `iss` e `aud` (`buteco-teste`) exigidos, e cujo conteúdo traz `sub`, `client_id` igual ao do cliente, `iat` e `exp`

#### Scenario: Segredo errado não emite token
- **WHEN** o mesmo cliente envia `POST /connect/token` com o segredo errado
- **THEN** a resposta é `invalid_client` e nenhum `access_token` é emitido
