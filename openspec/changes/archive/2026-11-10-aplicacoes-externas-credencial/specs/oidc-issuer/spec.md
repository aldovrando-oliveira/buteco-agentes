## MODIFIED Requirements

### Requirement: Fluxos habilitados e os clientes mantidos pelo apps/api
O emissor SHALL habilitar os grants `client_credentials`, `authorization_code` e `refresh_token`, com PKCE obrigatório no `authorization_code`. Os únicos clientes que o próprio `apps/api` SHALL manter são o `buteco-painel` (capability `issuer-user-login`) e os clientes de serviço `buteco-servico-inbox` e `buteco-servico-connectors` (requisito "Clientes de serviço mantidos pelo apps/api"). Os únicos escopos são `buteco-api`, `buteco-inbox` e `buteco-connectors`. Todos são sincronizados em segundo plano depois do start. Fora deles, o único cliente que SHALL existir é a aplicação externa, criada pelo Admin (capability `external-applications`), com `client_id` fora do prefixo `buteco-`. Nenhum outro cliente, escopo ou recurso SHALL ser cadastrado por migration nem por código. Requisição com outro `grant_type` SHALL ser recusada com `unsupported_grant_type`.

#### Scenario: Banco recém-migrado tem só os clientes do apps/api
- **WHEN** o banco do `apps/api` é migrado do zero, o app sobe e o aquecimento termina
- **THEN** os clientes do emissor com prefixo `buteco-` são exatamente três: `buteco-painel`, `buteco-servico-inbox` e `buteco-servico-connectors`

#### Scenario: Grant de senha é recusado
- **WHEN** um cliente envia `POST /connect/token` com `grant_type=password`
- **THEN** a resposta é `400` com o erro `unsupported_grant_type`

#### Scenario: A sincronização não toca na aplicação externa
- **WHEN** existe uma aplicação externa e a sincronização do catálogo roda
- **THEN** o cliente dela continua como estava, e o log não tem erro sobre ele

## ADDED Requirements

### Requirement: Token client_credentials da aplicação externa
A aplicação externa SHALL obter o access token por `POST /connect/token` com `grant_type=client_credentials`, o `client_id` dela, um segredo vivo e `scope=buteco-api`. O token SHALL ter `sub` e `client_id` iguais ao `client_id`, `aud` só `buteco-api`, `typ` `at+jwt`, o claim `buteco_app` com o id da aplicação, nenhum claim `role`, e `exp − iat` de 300 segundos, posto no principal pelo emissor a partir de uma constante do código. O claim `buteco_app` SHALL sair só para cliente que está no cadastro de aplicações externas, e nunca para o `buteco-painel` nem para um `buteco-servico-*`. O access token do painel continua com 900 segundos. A aplicação SHALL NOT receber refresh token: pedido com `offline_access` ou `grant_type=refresh_token` SHALL ser recusado. Pedido com `scope=buteco-inbox` ou `buteco-connectors` SHALL ser recusado sem token, com `invalid_request`, como o do cliente de serviço. Pedido **sem** `scope` SHALL emitir um token sem `aud`, que o `apps/api` recusa com `401`.

#### Scenario: Token da aplicação
- **WHEN** uma aplicação ativa pede token com o segredo ativo e `scope=buteco-api`
- **THEN** a resposta traz um access token com `client_id` igual ao dela, `aud` só `buteco-api`, `buteco_app` igual ao id da aplicação, sem `role`, e `exp − iat` de 300 segundos

#### Scenario: O painel continua com 15 minutos
- **WHEN** um usuário entra pelo `buteco-painel` e recebe o access token
- **THEN** o `exp − iat` é de 900 segundos

#### Scenario: Os clientes do Buteco nunca levam buteco_app
- **WHEN** o `buteco-painel`, o `buteco-servico-inbox` e o `buteco-servico-connectors` obtêm access token
- **THEN** nenhum dos três tokens traz o claim `buteco_app`

#### Scenario: Escopo de outro app recusado para a aplicação
- **WHEN** uma aplicação pede token com `scope=buteco-inbox`
- **THEN** nenhum token é emitido, e o erro é `invalid_request`

#### Scenario: Sem refresh token
- **WHEN** uma aplicação pede token com `scope=buteco-api offline_access`
- **THEN** nenhum refresh token é emitido, e nenhuma linha é gravada em `openiddict_tokens` nem em `openiddict_authorizations`

#### Scenario: Sem scope, o apps/api recusa
- **WHEN** uma aplicação pede token sem `scope` e chama `GET /agents/` com ele
- **THEN** o token não tem `aud`, e a resposta do `apps/api` é `401`

### Requirement: A aplicação externa é autenticada pelos segredos dela; os outros clientes, pelo OpenIddict
O gerente de aplicações do OpenIddict SHALL ser estendido só na conferência do segredo do cliente, e a extensão vale para todo cliente:

- cliente que está no cadastro de aplicações externas SHALL ser aceito só por um segredo dele que não esteja revogado nem vencido, e só com a aplicação ativa. O segredo de enchimento gravado no OpenIddict SHALL NOT ser aceito, nunca: a resposta é `invalid_client`;
- qualquer outro cliente, inclusive `buteco-servico-inbox` e `buteco-servico-connectors`, SHALL seguir a conferência do próprio OpenIddict, sem consultar o cadastro de aplicações.

Segredo de uma aplicação apresentado com o `client_id` de outra SHALL receber `invalid_client`.

#### Scenario: Segredo de enchimento nunca vale
- **WHEN** um pedido de token chega com o `client_id` de uma aplicação e o segredo de enchimento que o OpenIddict guarda para ela
- **THEN** a resposta é `invalid_client`

#### Scenario: Cliente de serviço não passa pelo cadastro
- **WHEN** o `buteco-servico-inbox` pede token com o segredo configurado, e existe no cadastro um segredo de aplicação com o mesmo texto
- **THEN** o token é emitido pela conferência do OpenIddict, e o `ultimoUso` daquele segredo de aplicação continua `null`

#### Scenario: Segredo revogado ou vencido
- **WHEN** uma aplicação pede token com um segredo revogado, e depois com um segredo vencido
- **THEN** as duas respostas são `invalid_client`

#### Scenario: Substituído vale dentro da sobreposição
- **WHEN** uma aplicação pede token com um segredo `substituido` 6 dias depois da geração do novo, e de novo 8 dias depois
- **THEN** a primeira emite token e a segunda recebe `invalid_client`

#### Scenario: Segredo de outra aplicação
- **WHEN** a aplicação A pede token com o `client_id` dela e um segredo vivo da aplicação B
- **THEN** a resposta é `invalid_client`
