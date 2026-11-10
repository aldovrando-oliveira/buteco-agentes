# oidc-issuer Specification

## Purpose

Diz o que o `apps/api` expõe como emissor OpenID Connect: discovery, JWKS, token,
authorize e encerramento de sessão, anônimos, com o issuer vindo de `PublicUrl:BaseUrl`,
obrigatória, e todos os endereços do discovery montados a partir dele, nunca do host da
requisição. Cobre os fluxos habilitados e o único cliente que o próprio `apps/api` mantém
(`buteco-painel`, desde a #180), o formato do token emitido, a convivência com o login e o `503` enquanto as chaves não carregam. O fluxo do usuário é da
capability `issuer-user-login`; quem cadastra clientes externos (#186) fica fora.
## Requirements
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

### Requirement: Url pública obrigatória
O boot do `apps/api` SHALL falhar, depois da construção do host e com mensagem que cita `PublicUrl:BaseUrl`, quando a url pública estiver ausente, vazia ou não for uma url absoluta `http` ou `https` sem query nem fragmento. O registro dos serviços SHALL NOT lançar exceção por isso, para o migration bundle continuar subindo até a construção do host.

#### Scenario: Sem url pública o boot falha
- **WHEN** o app sobe com `PublicUrl:BaseUrl` vazia
- **THEN** o boot falha com uma mensagem que cita `PublicUrl:BaseUrl`

#### Scenario: Url pública inválida derruba o boot
- **WHEN** o app sobe com `PublicUrl:BaseUrl` igual a `api.exemplo.test` (sem esquema)
- **THEN** o boot falha com uma mensagem que cita `PublicUrl:BaseUrl`

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

### Requirement: Clientes de serviço mantidos pelo apps/api
O `apps/api` SHALL manter, na mesma sincronização do `buteco-painel` (em segundo plano depois do start, sob o mesmo lock, sem acesso ao banco no boot), dois clientes de serviço:

- `buteco-servico-inbox`, com o segredo de `Identity:ServiceClients:Inbox:Secret`;
- `buteco-servico-connectors`, com o segredo de `Identity:ServiceClients:Connectors:Secret`.

Cada cliente de serviço SHALL ser:

- confidencial, com consentimento implícito;
- com permissão só do endpoint de token, do grant `client_credentials` e do escopo `buteco-api`;
- sem `redirect_uri`, sem `post_logout_redirect_uri` e sem requisito.

O OpenIddict SHALL guardar só o hash do segredo.

A cada sincronização, qualquer divergência do cliente gravado em relação ao descritor SHALL ser corrigida. Isso inclui o segredo: quando o segredo configurado não validar contra o gravado, a sincronização SHALL regravá-lo. Toda correção SHALL levar o segredo configurado, para que nenhuma correção deixe o cliente sem credencial. O log da correção SHALL nomear o cliente e SHALL NOT conter o segredo.

Cada escopo e cada cliente SHALL ser sincronizado isoladamente: a falha de um, inclusive um descritor que o OpenIddict recusa, SHALL gerar log de erro que nomeia o item e SHALL NOT conter o segredo. Essa falha SHALL NOT impedir os outros itens no mesmo ciclo, nem gravar o item que falhou. Quando algum item falha, o ciclo SHALL terminar com falha depois de gravar os outros, para ser tentado de novo no ciclo seguinte do aquecimento.

O token `client_credentials` de um cliente de serviço SHALL ter `sub` e `client_id` iguais ao `client_id` do cliente, `aud` igual a `buteco-api` e `typ` `at+jwt`.

#### Scenario: O aquecimento cria os clientes de serviço
- **WHEN** o app sobe contra um banco migrado sem nenhum cliente, e o aquecimento termina
- **THEN** existem os clientes `buteco-servico-inbox` e `buteco-servico-connectors`, confidenciais e com só as três permissões

#### Scenario: Token do cliente de serviço
- **WHEN** alguém envia `POST /connect/token` com `grant_type=client_credentials`, `client_id=buteco-servico-inbox`, o segredo configurado e `scope=buteco-api`
- **THEN** a resposta traz um access token com `client_id` `buteco-servico-inbox`, `aud` só `buteco-api` e `typ` `at+jwt`

#### Scenario: Escopo de outro app é recusado
- **WHEN** o mesmo pedido chega com `scope=buteco-connectors`
- **THEN** nenhum token é emitido, e o erro da resposta é `invalid_request`, porque o escopo existe e o cliente não tem permissão dele

#### Scenario: Segredo errado é recusado
- **WHEN** o pedido chega com um segredo diferente do configurado
- **THEN** nenhum token é emitido, e a resposta é `invalid_client`

#### Scenario: Cliente de serviço adulterado volta ao descritor com a credencial
- **WHEN** o cliente `buteco-servico-inbox` gravado ganhou a permissão do escopo `buteco-connectors`, e a sincronização roda
- **THEN** o cliente volta a ter só as três permissões, e um pedido com o segredo configurado continua emitindo token

#### Scenario: Um cliente de serviço que falha não impede os outros
- **WHEN** o `buteco-painel` e os dois clientes de serviço foram adulterados, e a sincronização roda com o descritor do `buteco-servico-inbox` recusado pelo OpenIddict
- **THEN** o `buteco-painel` e o `buteco-servico-connectors` voltam ao descritor no mesmo ciclo, o `buteco-servico-inbox` fica como estava e continua emitindo token com o segredo dele, o log tem um erro que cita `cliente buteco-servico-inbox` sem nenhum segredo, e o ciclo termina com falha

#### Scenario: Segredo trocado na configuração vale depois da sincronização
- **WHEN** o app sobe com um segredo do `buteco-servico-inbox` diferente do gravado, e o aquecimento termina
- **THEN** o segredo anterior recebe `invalid_client`, o novo emite token, e o log cita a troca do segredo do `buteco-servico-inbox` sem conter nenhum dos dois valores

### Requirement: Segredos dos clientes de serviço conferidos no boot
O boot do `apps/api` SHALL falhar, depois da construção do host e sem acesso ao banco, quando `Identity:ServiceClients:Inbox:Secret` ou `Identity:ServiceClients:Connectors:Secret`:

- estiver ausente ou vazio;
- não for base64 válido;
- tiver menos de 32 bytes depois de decodificado;
- for igual, em bytes decodificados, ao outro segredo de serviço;
- for igual, em bytes decodificados, a `Identity:SigningKeyEncryptionKey`, a `Identity:TwoFactorEncryptionKey` ou a `Mcp:CredentialEncryptionKey`. Esta última só é comparada quando estiver configurada e decodificar.

A mensagem SHALL nomear a chave do segredo, e, no caso de igualdade, as duas chaves iguais. Nenhuma mensagem SHALL conter o valor. Os dois segredos SHALL ser obrigatórios mesmo sem `Connectors:BaseUrl`. O registro dos serviços SHALL NOT lançar exceção por isso, para o migration bundle continuar subindo até a construção do host.

#### Scenario: Segredo ausente derruba o boot
- **WHEN** o app sobe sem `Identity:ServiceClients:Inbox:Secret`
- **THEN** o boot falha com uma mensagem que cita `Identity:ServiceClients:Inbox:Secret`

#### Scenario: Segredo curto derruba o boot sem ecoar o valor
- **WHEN** o app sobe com `Identity:ServiceClients:Connectors:Secret` igual ao base64 de 16 bytes
- **THEN** o boot falha com uma mensagem que cita `Identity:ServiceClients:Connectors:Secret` e não contém o valor configurado

#### Scenario: Segredos iguais entre si derrubam o boot
- **WHEN** o app sobe com os dois segredos de serviço iguais
- **THEN** o boot falha com uma mensagem que cita `Identity:ServiceClients:Inbox:Secret` e `Identity:ServiceClients:Connectors:Secret`, sem o valor

#### Scenario: Segredo igual a uma chave de cifra derruba o boot
- **WHEN** o app sobe com `Identity:ServiceClients:Inbox:Secret` igual a `Identity:TwoFactorEncryptionKey`, e, em outras subidas, igual a `Identity:SigningKeyEncryptionKey` e a `Mcp:CredentialEncryptionKey`
- **THEN** cada boot falha com uma mensagem que cita `Identity:ServiceClients:Inbox:Secret` e a chave igual, sem o valor

#### Scenario: Composição real sobe com os segredos válidos
- **WHEN** o app sobe com os dois segredos presentes, distintos entre si e das três chaves de cifra
- **THEN** a checagem passa e a aplicação responde

### Requirement: O access token do emissor é a credencial das rotas do apps/api
Com o emissor ligado, as rotas autenticadas do `apps/api` SHALL aceitar o access token do emissor que satisfaça a capability `route-authentication` (audience `buteco-api`, principal pela tabela de clientes), e nenhum outro token. Nenhuma rota do `apps/api` SHALL emitir token de operador fora do emissor.

#### Scenario: Token do emissor do painel é credencial das rotas do app
- **WHEN** um access token emitido pelo fluxo do `buteco-painel`, com a audience `buteco-api`, é enviado como `Bearer` em `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token do emissor de cliente fora da tabela não é credencial
- **WHEN** um access token emitido por `client_credentials` para um cliente de teste fora da tabela de clientes, com a audience `buteco-api`, é enviado em `GET /agents`
- **THEN** a resposta é `403 Forbidden`

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

