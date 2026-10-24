## ADDED Requirements

### Requirement: Emissor OIDC publica discovery, JWKS e endpoint de token
Com a url pública configurada (`PublicUrl:BaseUrl`), o `apps/api` SHALL atuar como emissor OpenID Connect e expor, sem exigir `Authorization`:

- `GET /.well-known/openid-configuration`, com o documento de discovery;
- `GET /.well-known/jwks`, com as chaves públicas de assinatura;
- `POST /connect/token`, o endpoint de token.

O documento de discovery SHALL declarar como `issuer` exatamente a url pública configurada, sem barra final duplicada, e SHALL declarar `jwks_uri` e `token_endpoint` como endereços absolutos sob esse mesmo issuer. Nenhum endereço do documento SHALL ser montado a partir do host da requisição.

#### Scenario: Discovery declara o issuer da configuração
- **WHEN** `PublicUrl:BaseUrl` é `https://api.exemplo.test` e um cliente envia `GET /.well-known/openid-configuration` sem `Authorization`, com o header `Host` de outro nome
- **THEN** a resposta é `200 OK`, o campo `issuer` é `https://api.exemplo.test/`, e `jwks_uri` e `token_endpoint` começam por `https://api.exemplo.test/`

#### Scenario: JWKS é anônimo e publica chave com kid
- **WHEN** um cliente envia `GET /.well-known/jwks` sem `Authorization`
- **THEN** a resposta é `200 OK` com ao menos uma chave RSA de uso `sig`, cada uma com `kid` não vazio e sem nenhum parâmetro privado (`d`, `p`, `q`, `dp`, `dq`, `qi`)

#### Scenario: Endpoint de token é alcançável sem token
- **WHEN** um cliente envia `POST /connect/token` sem `Authorization`, com `grant_type=client_credentials` e um `client_id` que não existe
- **THEN** a resposta é `400` ou `401` com o erro OAuth `invalid_client`, e não `401` vazio do esquema de autenticação do app

### Requirement: Só o fluxo client_credentials é habilitado, sem cliente cadastrado
O emissor SHALL habilitar apenas o grant `client_credentials`. Esta capability SHALL NOT cadastrar nenhum cliente, escopo ou recurso, nem por migration nem por seed no boot. Requisição com outro `grant_type` SHALL ser recusada com `unsupported_grant_type`.

#### Scenario: Banco recém-migrado não tem cliente
- **WHEN** o banco do `apps/api` é migrado do zero e o app sobe com o emissor ligado
- **THEN** a tabela de aplicações do emissor está vazia

#### Scenario: Grant de senha é recusado
- **WHEN** um cliente envia `POST /connect/token` com `grant_type=password`
- **THEN** a resposta é `400` com o erro `unsupported_grant_type`

### Requirement: Token emitido é JWT assinado, verificável pelo JWKS, com aud, kid e iat
Para um cliente confidencial cadastrado, com permissão de `client_credentials` e de um escopo associado a um recurso, o endpoint de token SHALL emitir um access token no formato JWT **não cifrado**, assinado com a chave de assinatura ativa. O token SHALL conter:

- `iss` igual ao issuer configurado;
- `sub` igual ao `client_id`;
- `aud` com o(s) recurso(s) do(s) escopo(s) concedido(s);
- `iat` e `exp`.

O cabeçalho do JWT SHALL conter o `kid` da chave ativa. A assinatura SHALL ser verificável só com as chaves publicadas em `/.well-known/jwks`.

#### Scenario: Token de client_credentials verificável pelo JWKS
- **WHEN** um cliente cadastrado só para o teste, com o escopo `teste.api` associado ao recurso `buteco-teste`, envia `POST /connect/token` com `grant_type=client_credentials`, as próprias credenciais e `scope=teste.api`
- **THEN** a resposta é `200 OK` com um `access_token` JWT de três partes, cujo cabeçalho tem o `kid` da chave ativa, cuja assinatura valida contra as chaves de `/.well-known/jwks` com `iss` e `aud` (`buteco-teste`) exigidos, e cujo conteúdo traz `sub`, `iat` e `exp`

#### Scenario: Segredo errado não emite token
- **WHEN** o mesmo cliente envia `POST /connect/token` com o segredo errado
- **THEN** a resposta é `invalid_client` e nenhum `access_token` é emitido

### Requirement: Sem url pública, o emissor fica desligado e o app sobe
Quando `PublicUrl:BaseUrl` está ausente ou vazia, o `apps/api` SHALL subir sem o emissor: os três endereços do emissor SHALL NOT existir, e o boot SHALL registrar um aviso no log dizendo que o emissor está desligado por falta de url pública. As demais rotas, o login e a validação de token SHALL funcionar como antes. Quando `PublicUrl:BaseUrl` está presente e não é uma url absoluta `http` ou `https` sem query nem fragmento, o boot SHALL falhar com mensagem que nomeia a configuração.

#### Scenario: Sem url pública o discovery não existe
- **WHEN** o app sobe com `PublicUrl:BaseUrl` vazia e um cliente envia `GET /.well-known/openid-configuration`
- **THEN** a resposta não é `200` com documento de discovery, e o log de boot contém o aviso de emissor desligado

#### Scenario: Sem url pública o login continua
- **WHEN** o app sobe com `PublicUrl:BaseUrl` vazia e o operador envia `POST /auth/login` com credenciais corretas
- **THEN** a resposta é `200 OK` com o token HMAC, como antes

#### Scenario: Url pública inválida derruba o boot
- **WHEN** o app sobe com `PublicUrl:BaseUrl` igual a `api.exemplo.test` (sem esquema)
- **THEN** o boot falha com uma mensagem que cita `PublicUrl:BaseUrl`

### Requirement: O emissor convive com o login e a validação HMAC sem mudá-los
Com o emissor ligado, `POST /auth/login` SHALL continuar emitindo o token HMAC do operador, e toda rota autenticada do `apps/api` SHALL continuar aceitando os tokens HMAC de operador e de serviço exatamente como antes. Um token emitido pelo emissor SHALL NOT ser aceito como credencial nas rotas autenticadas do `apps/api` por esta capability.

#### Scenario: Token HMAC do operador continua aceito com o emissor ligado
- **WHEN** o emissor está ligado e o operador faz login e chama `GET /agents` com o token HMAC
- **THEN** a resposta é `200 OK`

#### Scenario: Token do emissor não é credencial das rotas do app
- **WHEN** um access token emitido pelo emissor para um cliente de teste é enviado como `Bearer` em `GET /agents`
- **THEN** a resposta é `401 Unauthorized`

### Requirement: Endpoints do emissor respondem 503 enquanto as chaves não carregam
Com o emissor ligado, enquanto o conjunto de chaves não puder ser carregado do banco, o `apps/api` SHALL responder `503 Service Unavailable` com `Retry-After` a toda requisição, e SHALL tentar carregar de novo na requisição seguinte, sem guardar a falha. Carregadas as chaves, SHALL atender normalmente sem consultar o banco por requisição.

Logo depois do start, o `apps/api` SHALL tentar carregar as chaves em segundo plano, sem bloquear o boot. Se a tentativa falhar, SHALL repetir com intervalo até carregar, sem guardar a falha. A carga pela requisição continua valendo em paralelo, sob o mesmo semáforo.

#### Scenario: Banco ainda não migrado
- **WHEN** o app sobe com o emissor ligado, antes de o banco ter as tabelas de chave, e recebe `GET /health`
- **THEN** a resposta é `503` com `Retry-After`

#### Scenario: Banco migrado antes do primeiro request
- **WHEN** o banco já está migrado quando o app sobe com o emissor ligado, e o primeiro `GET /health` chega depois de a carga em segundo plano concluir
- **THEN** a resposta é `200 OK`, e a requisição não executou a carga das chaves

#### Scenario: Recupera sem reiniciar depois da migração
- **WHEN** o mesmo processo recebe `GET /.well-known/jwks` depois de o banco ser migrado
- **THEN** a resposta é `200 OK` com uma chave, sem reinício do processo
