## MODIFIED Requirements

### Requirement: Emissor OIDC publica discovery, JWKS e endpoint de token
O `apps/api` SHALL atuar como emissor OpenID Connect, com a url pública configurada (`PublicUrl:BaseUrl`), e expor, sem exigir `Authorization`:

- `GET /.well-known/openid-configuration`, com o documento de discovery;
- `GET /.well-known/jwks`, com as chaves públicas de assinatura;
- `POST /connect/token`, o endpoint de token;
- `GET /connect/authorize`, o endpoint de autorização;
- `GET /connect/logout`, o endpoint de encerramento de sessão.

O documento de discovery SHALL declarar como `issuer` exatamente a url pública configurada, sem barra final duplicada. SHALL declarar `jwks_uri`, `token_endpoint`, `authorization_endpoint` e `end_session_endpoint` como endereços absolutos sob esse mesmo issuer. Nenhum endereço do documento SHALL ser montado a partir do host da requisição.

#### Scenario: Discovery declara o issuer da configuração
- **WHEN** `PublicUrl:BaseUrl` é `https://api.exemplo.test` e um cliente envia `GET /.well-known/openid-configuration` sem `Authorization`, com o header `Host` de outro nome
- **THEN** a resposta é `200 OK`, o campo `issuer` é `https://api.exemplo.test/`, e `jwks_uri`, `token_endpoint`, `authorization_endpoint` e `end_session_endpoint` começam por `https://api.exemplo.test/`

#### Scenario: JWKS é anônimo e publica chave com kid
- **WHEN** um cliente envia `GET /.well-known/jwks` sem `Authorization`
- **THEN** a resposta é `200 OK` com ao menos uma chave RSA de uso `sig`, cada uma com `kid` não vazio e sem nenhum parâmetro privado (`d`, `p`, `q`, `dp`, `dq`, `qi`)

#### Scenario: Endpoint de token é alcançável sem token
- **WHEN** um cliente envia `POST /connect/token` sem `Authorization`, com `grant_type=client_credentials` e um `client_id` que não existe
- **THEN** a resposta é `400` ou `401` com o erro OAuth `invalid_client`, e não `401` vazio do esquema de autenticação do app

### Requirement: O emissor convive com o login e a validação HMAC sem mudá-los
Com o emissor ligado, `POST /auth/login` SHALL continuar emitindo o token HMAC do operador, e toda rota autenticada do `apps/api` SHALL continuar aceitando os tokens HMAC de operador e de serviço exatamente como antes. Ao lado deles, as rotas autenticadas do `apps/api` SHALL aceitar o access token do emissor que satisfaça a capability `route-authentication` (audience `buteco-api`, principal pela tabela de clientes).

#### Scenario: Token HMAC do operador continua aceito com o emissor ligado
- **WHEN** o emissor está ligado e o operador faz login e chama `GET /agents` com o token HMAC
- **THEN** a resposta é `200 OK`

#### Scenario: Token do emissor do painel é credencial das rotas do app
- **WHEN** um access token emitido pelo fluxo do `buteco-painel`, com a audience `buteco-api`, é enviado como `Bearer` em `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token do emissor de cliente fora da tabela não é credencial
- **WHEN** um access token emitido por `client_credentials` para um cliente de teste fora da tabela de clientes, com a audience `buteco-api`, é enviado em `GET /agents`
- **THEN** a resposta é `403 Forbidden`

## REMOVED Requirements

### Requirement: Só o fluxo client_credentials é habilitado, sem cliente cadastrado
**Reason**: o fluxo do usuário (#180) habilita `authorization_code` e `refresh_token`, e o `apps/api` passa a manter o cliente `buteco-painel`.
**Migration**: substituído por "Fluxos habilitados e o único cliente mantido pelo apps/api". O `client_credentials` continua habilitado e sem cliente em produção.

### Requirement: Sem url pública, o emissor fica desligado e o app sobe
**Reason**: o login de usuário depende do emissor (#180). Sem url pública não há issuer, e o app subiria sem nenhuma forma de entrar depois da #217. A url pública passa a ser obrigatória.
**Migration**: definir `PublicUrl:BaseUrl`. O compose de produção já a exige (`PUBLIC_DOMAIN`), e o `appsettings.Development.json` já a traz. Ver o requisito "Url pública obrigatória".

## ADDED Requirements

### Requirement: Fluxos habilitados e o único cliente mantido pelo apps/api
O emissor SHALL habilitar os grants `client_credentials`, `authorization_code` e `refresh_token`, com PKCE obrigatório no `authorization_code`. O único cliente que o próprio `apps/api` SHALL manter é o `buteco-painel`, e os únicos escopos são `buteco-api`, `buteco-inbox` e `buteco-connectors`, sincronizados em segundo plano depois do start (capability `issuer-user-login`). Nenhum outro cliente, escopo ou recurso SHALL ser cadastrado por migration nem por código. Requisição com outro `grant_type` SHALL ser recusada com `unsupported_grant_type`.

#### Scenario: Banco recém-migrado tem só o cliente do painel
- **WHEN** o banco do `apps/api` é migrado do zero, o app sobe e o aquecimento termina
- **THEN** a tabela de aplicações do emissor tem exatamente uma linha, a do `buteco-painel`

#### Scenario: Grant de senha é recusado
- **WHEN** um cliente envia `POST /connect/token` com `grant_type=password`
- **THEN** a resposta é `400` com o erro `unsupported_grant_type`


### Requirement: Url pública obrigatória
O boot do `apps/api` SHALL falhar, depois da construção do host e com mensagem que cita `PublicUrl:BaseUrl`, quando a url pública estiver ausente, vazia ou não for uma url absoluta `http` ou `https` sem query nem fragmento. O registro dos serviços SHALL NOT lançar exceção por isso, para o migration bundle continuar subindo até a construção do host.

#### Scenario: Sem url pública o boot falha
- **WHEN** o app sobe com `PublicUrl:BaseUrl` vazia
- **THEN** o boot falha com uma mensagem que cita `PublicUrl:BaseUrl`

#### Scenario: Url pública inválida derruba o boot
- **WHEN** o app sobe com `PublicUrl:BaseUrl` igual a `api.exemplo.test` (sem esquema)
- **THEN** o boot falha com uma mensagem que cita `PublicUrl:BaseUrl`
