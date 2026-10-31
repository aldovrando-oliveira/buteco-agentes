# issuer-user-login Specification

## Purpose

Diz como um usuário local entra pelo emissor OIDC do `apps/api`: a sessão curta do
emissor aberta pelo `POST /auth/session`, o fluxo authorization code com PKCE do cliente
público `buteco-painel`, o catálogo de cliente e escopos que o próprio `apps/api` mantém,
a renovação que relê o usuário e o encerramento de sessão. Introduzida pela change
`usuarios-locais-login-emissor` (#180). A tela de login do painel é da #217.

## Requirements

### Requirement: Rota de login abre a sessão do emissor
O `apps/api` SHALL expor `POST /auth/session`, anônima, que recebe `{ "email", "senha" }` em JSON. Com credenciais válidas de um usuário ativo, SHALL responder `204` e gravar o cookie de sessão do emissor `buteco-sessao` com:

- `HttpOnly`;
- `SameSite=Strict`;
- `Path=/`, sem `Domain`;
- `Secure` fora do ambiente `Development`;
- vida de 5 minutos, sem renovação.

O cookie SHALL carregar o identificador e o carimbo de segurança (`security_stamp`) do usuário, e nenhuma outra credencial. Em qualquer recusa, nenhum cookie SHALL ser gravado.

#### Scenario: Login válido grava o cookie
- **WHEN** um usuário ativo envia `POST /auth/session` com e-mail e senha corretos, `Content-Type: application/json` e `Origin` igual à origem do issuer
- **THEN** a resposta é `204`, com `Set-Cookie: buteco-sessao` `HttpOnly`, `SameSite=Strict` e com expiração de 5 minutos

#### Scenario: Credencial errada não grava cookie
- **WHEN** o mesmo pedido chega com a senha errada
- **THEN** a resposta é `401` com a mensagem genérica, sem `Set-Cookie`

### Requirement: A rota de login só aceita JSON da origem do emissor
`POST /auth/session` SHALL recusar com `415` o pedido que não for `application/json`. SHALL recusar com `403` o pedido cujo `Origin` estiver ausente ou for diferente da origem do issuer e da origem de `Identity:PanelBaseUrl`, quando ela estiver configurada. Em nenhum dos dois casos a senha SHALL ser verificada.

#### Scenario: Origem de outro site é recusada
- **WHEN** o pedido de login chega com `Origin: https://outro.exemplo`
- **THEN** a resposta é `403`, sem `Set-Cookie`, e a contagem de falhas do e-mail não muda

#### Scenario: Pedido sem Origin é recusado
- **WHEN** o pedido de login chega sem o header `Origin`
- **THEN** a resposta é `403`

#### Scenario: Formulário HTML é recusado
- **WHEN** o pedido de login chega com `Content-Type: application/x-www-form-urlencoded`
- **THEN** a resposta é `415`, sem `Set-Cookie`

### Requirement: Fluxo authorization code com PKCE obrigatório
O emissor SHALL atender `GET /connect/authorize` com `response_type=code` e PKCE obrigatório (`code_challenge_method=S256`), e SHALL trocar o código por tokens em `POST /connect/token` mediante o `code_verifier` correspondente. Pedido de autorização sem `code_challenge`, com cliente desconhecido ou com `redirect_uri` não registrado SHALL ser recusado sem emitir código.

O authorize SHALL decidir pela sessão do emissor:

- **sem cookie válido e sem `prompt=none`:** `302` para `/entrar?retorno=<o pedido original>`. O `retorno` SHALL ser montado pelo servidor só com o caminho relativo e a query do pedido, URL-codificado. SHALL NOT ser uma URL absoluta, nem levar esquema, host ou porta, nem sair de parâmetro do cliente ou do header `Host`;
- **sem cookie válido e com `prompt=none`:** erro `login_required` devolvido ao `redirect_uri`;
- **com cookie válido de usuário ativo cujo carimbo de segurança confere:** código emitido ao `redirect_uri`.

O token emitido para o usuário SHALL ter:

- `sub` igual ao id do usuário;
- `client_id` igual ao cliente;
- `aud` com os recursos dos escopos pedidos;
- `typ` `at+jwt`.

#### Scenario: Fluxo completo emite token para os três apps
- **WHEN** um usuário abre a sessão do emissor, o cliente `buteco-painel` pede autorização com PKCE e os escopos `openid offline_access buteco-api buteco-inbox buteco-connectors`, e troca o código com o `code_verifier`
- **THEN** a resposta do token traz access token, refresh token e id token; o access token tem `sub` = id do usuário, `client_id` `buteco-painel`, `aud` [`buteco-api`, `buteco-inbox`, `buteco-connectors`], `typ` `at+jwt`, e é verificável pelo JWKS

#### Scenario: Sem sessão, o authorize manda para a tela de login
- **WHEN** um pedido de autorização válido chega sem o cookie `buteco-sessao`
- **THEN** a resposta é `302` com `Location` começando por `/entrar?retorno=`, e nenhum código é emitido

#### Scenario: O retorno é o caminho relativo montado pelo servidor
- **WHEN** um pedido de autorização válido chega sem o cookie e com o header `Host` de outro nome
- **THEN** o `retorno` do `Location`, decodificado, começa por `/connect/authorize?`, traz a query original do pedido, e não contém esquema nem host

#### Scenario: prompt=none sem sessão devolve login_required
- **WHEN** um pedido de autorização válido com `prompt=none` chega sem o cookie
- **THEN** a resposta redireciona ao `redirect_uri` com `error=login_required`

#### Scenario: PKCE ausente é recusado
- **WHEN** um pedido de autorização chega sem `code_challenge`
- **THEN** nenhum código é emitido

#### Scenario: Verifier errado não troca o código
- **WHEN** o código é apresentado ao endpoint de token com um `code_verifier` que não corresponde ao desafio
- **THEN** a resposta é `invalid_grant` e nenhum token é emitido

#### Scenario: Cookie de usuário desativado depois do login é recusado
- **WHEN** o usuário é desativado depois de gravar o cookie, e o authorize recebe esse cookie
- **THEN** nenhum código é emitido, e a resposta manda para `/entrar`

### Requirement: Cliente público buteco-painel e três escopos mantidos pelo apps/api
O `apps/api` SHALL manter, em segundo plano depois do start e sem acesso ao banco no boot, o cliente `buteco-painel` e os escopos `buteco-api`, `buteco-inbox` e `buteco-connectors`, cada escopo com o recurso de mesmo nome. O cliente SHALL ser:

- público, sem segredo, com consentimento implícito;
- com PKCE exigido;
- com permissão de `authorization_code`, `refresh_token` e encerramento de sessão;
- sem permissão de `client_credentials`;
- com `redirect_uri` `{base do painel}/entrar/retorno` e `post_logout_redirect_uri` `{base do painel}/entrar`. A base do painel é `Identity:PanelBaseUrl` ou, na ausência dela, `PublicUrl:BaseUrl`.

A sincronização SHALL rodar com tratamento de falha próprio: uma falha dela SHALL NOT impedir a carga das chaves nem o bootstrap, e ela SHALL ser tentada de novo no ciclo seguinte do aquecimento até concluir, e depois a cada verificação periódica. O gate de indisponibilidade (`503`) SHALL NOT esperar a sincronização. Antes da primeira sincronização, o authorize do `buteco-painel` SHALL ser recusado com `invalid_request`, sem emitir código e sem redirecionar.

A cada sincronização, qualquer divergência do cliente gravado em relação a esse descritor SHALL ser corrigida. Um cliente com prefixo `buteco-` que o código não declara SHALL gerar log de erro e SHALL NOT ser alterado nem removido.

#### Scenario: Banco migrado ganha o cliente e os escopos
- **WHEN** o app sobe contra um banco migrado sem nenhum cliente
- **THEN** depois do aquecimento existem o cliente `buteco-painel`, público e sem segredo, e os três escopos com seus recursos

#### Scenario: Authorize antes da primeira sincronização
- **WHEN** o anel de chaves carregou, o catálogo ainda não sincronizou, e chega um pedido de autorização do `buteco-painel`
- **THEN** a resposta é `400` com `invalid_request`, sem código e sem redirecionar ao `redirect_uri`, e `GET /health` responde `200`, e não `503`

#### Scenario: Cliente adulterado volta ao descritor
- **WHEN** o cliente `buteco-painel` gravado foi alterado para confidencial, com segredo e permissão de `client_credentials`, e a sincronização roda
- **THEN** o cliente volta a ser público, sem segredo e sem `client_credentials`

#### Scenario: client_credentials com buteco-painel é recusado
- **WHEN** alguém pede `grant_type=client_credentials` com `client_id=buteco-painel`
- **THEN** nenhum token é emitido

#### Scenario: Cliente buteco- desconhecido só é logado
- **WHEN** existe no banco um cliente `buteco-outro` que o código não declara, e a sincronização roda
- **THEN** o log contém um erro que cita `buteco-outro`, e o cliente continua como estava

### Requirement: Refresh rotativo e deslizante de 14 dias; todo token novo relê o usuário
O emissor SHALL emitir refresh token com duração de 14 dias, rotativo: o refresh apresentado vira resgatado, e um novo é emitido com a duração inteira. O access token SHALL durar 15 minutos.

O principal do usuário SHALL carregar o carimbo de segurança (`security_stamp`) vigente no authorize. O código de autorização e o refresh SHALL levá-lo adiante, o refresh a cada rotação. O carimbo SHALL NOT sair no access token nem no id token.

Todo token novo de usuário SHALL conferir o estado e o carimbo: na troca do código (grant `authorization_code`) e em cada renovação (grant `refresh_token`), o emissor SHALL reler o usuário e SHALL recusar com `invalid_grant` quando:

- ele não existir mais;
- estiver desativado;
- o carimbo gravado for diferente do carimbo do código ou do refresh apresentado.

#### Scenario: Renovação emite novo par
- **WHEN** um refresh token válido de um usuário ativo é apresentado
- **THEN** a resposta traz um novo access token e um novo refresh token, e o refresh apresentado passa a resgatado

#### Scenario: Access token dura 15 minutos
- **WHEN** um access token de usuário é emitido
- **THEN** o `exp` dele é 15 minutos depois do `iat`

#### Scenario: Usuário desativado não renova
- **WHEN** o refresh token de um usuário desativado depois do login é apresentado
- **THEN** a resposta é `invalid_grant`, e nenhum token é emitido

#### Scenario: Desativado entre o authorize e a troca do código não recebe tokens
- **WHEN** um usuário obtém um código de autorização, é desativado antes da troca, e o código é apresentado com o `code_verifier` correto
- **THEN** a resposta é `invalid_grant`, e nenhum token é emitido

#### Scenario: Desativado e reativado não renova com o refresh de antes
- **WHEN** um usuário obtém um refresh token, é desativado (o que troca o carimbo de segurança) e reativado, e o refresh de antes da desativação é apresentado
- **THEN** a resposta é `invalid_grant`, e nenhum token é emitido

#### Scenario: O carimbo não sai nos tokens do cliente
- **WHEN** um usuário completa o fluxo e obtém access token e id token
- **THEN** nenhum dos dois contém o carimbo de segurança

### Requirement: Encerramento de sessão revoga a autorização e sempre termina
O emissor SHALL atender `GET /connect/logout`. Com um `id_token_hint` válido, SHALL revogar a autorização da sessão indicada pelo hint, apagar o cookie `buteco-sessao` e redirecionar ao `post_logout_redirect_uri` registrado do cliente. Com hint ausente ou inválido, SHALL apagar o cookie e redirecionar para `/entrar`. Em nenhum caso SHALL redirecionar para endereço não registrado.

#### Scenario: Logout revoga a sessão
- **WHEN** o painel chama `/connect/logout` com o id token da sessão como `id_token_hint` e o `post_logout_redirect_uri` registrado
- **THEN** a autorização da sessão passa a `revoked`, o cookie é apagado, a resposta redireciona ao endereço registrado, e o refresh token da sessão passa a ser recusado

#### Scenario: Hint inválido ainda encerra localmente
- **WHEN** `/connect/logout` recebe um `id_token_hint` com assinatura inválida
- **THEN** o cookie é apagado e a resposta redireciona para `/entrar`

#### Scenario: Endereço de volta não registrado é ignorado
- **WHEN** `/connect/logout` recebe um `post_logout_redirect_uri` que não está registrado
- **THEN** a resposta não redireciona para ele
