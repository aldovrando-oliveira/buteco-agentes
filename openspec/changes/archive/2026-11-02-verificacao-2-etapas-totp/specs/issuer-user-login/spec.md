## MODIFIED Requirements

### Requirement: Rota de login abre a sessão do emissor
O `apps/api` SHALL expor `POST /auth/session`, anônima, que recebe `{ "email", "senha" }` em JSON. Com credenciais válidas de um usuário ativo, SHALL gravar o cookie de sessão do emissor `buteco-sessao` com:

- `HttpOnly`;
- `SameSite=Strict`;
- `Path=/`, sem `Domain`;
- `Secure` fora do ambiente `Development`;
- vida de 5 minutos, sem renovação.

O cookie SHALL carregar o identificador e o carimbo de segurança (`security_stamp`) do usuário, e nenhuma outra credencial.

- **Sem a verificação em 2 etapas ligada**, a resposta SHALL ser `204`, e o cookie SHALL ser o da sessão completa.
- **Com a verificação ligada**, a resposta SHALL ser `200` com `{ "segundoPasso": "totp" }`, e o cookie SHALL ser o da sessão pendente, que o authorize não aceita. A sessão se completa pela rota do segundo passo.

Em qualquer recusa, nenhum cookie SHALL ser gravado.

#### Scenario: Login válido grava o cookie
- **WHEN** um usuário ativo, sem a verificação em 2 etapas, envia `POST /auth/session` com e-mail e senha corretos, `Content-Type: application/json` e `Origin` igual à origem do issuer
- **THEN** a resposta é `204`, com `Set-Cookie: buteco-sessao` `HttpOnly`, `SameSite=Strict` e com expiração de 5 minutos

#### Scenario: Login com a verificação ligada pede o segundo passo
- **WHEN** um usuário ativo com a verificação ligada envia `POST /auth/session` com e-mail e senha corretos
- **THEN** a resposta é `200` com `{ "segundoPasso": "totp" }` e `Set-Cookie: buteco-sessao`, e um pedido de autorização com esse cookie não recebe código

#### Scenario: Credencial errada não grava cookie
- **WHEN** o mesmo pedido chega com a senha errada
- **THEN** a resposta é `401` com a mensagem genérica, sem `Set-Cookie`

### Requirement: Fluxo authorization code com PKCE obrigatório
O emissor SHALL atender `GET /connect/authorize` com `response_type=code` e PKCE obrigatório (`code_challenge_method=S256`), e SHALL trocar o código por tokens em `POST /connect/token` mediante o `code_verifier` correspondente. Pedido de autorização sem `code_challenge`, com cliente desconhecido ou com `redirect_uri` não registrado SHALL ser recusado sem emitir código.

O authorize SHALL decidir pela sessão do emissor:

- **sem cookie válido e sem `prompt=none`:** `302` para `/entrar?retorno=<o pedido original>`. O `retorno` SHALL ser montado pelo servidor só com o caminho relativo e a query do pedido, URL-codificado. SHALL NOT ser uma URL absoluta, nem levar esquema, host ou porta, nem sair de parâmetro do cliente ou do header `Host`;
- **sem cookie válido e com `prompt=none`:** erro `login_required` devolvido ao `redirect_uri`;
- **com cookie válido de usuário ativo cujo carimbo de segurança confere:** código emitido ao `redirect_uri`.

**Cookie válido** SHALL considerar o estado atual do usuário: se ele tem a verificação em 2 etapas ligada, só o cookie da sessão completa, gravado pelo segundo passo, é válido. O cookie da sessão pendente, e o cookie gravado antes de a verificação ser ligada, SHALL ser tratados como ausência de sessão.

O token emitido para o usuário SHALL ter:

- `sub` igual ao id do usuário;
- `client_id` igual ao cliente;
- `aud` com os recursos dos escopos pedidos;
- `typ` `at+jwt`.

#### Scenario: Fluxo completo emite token para os três apps
- **WHEN** um usuário abre a sessão do emissor, o cliente `buteco-painel` pede autorização com PKCE e os escopos `openid offline_access buteco-api buteco-inbox buteco-connectors`, e troca o código com o `code_verifier`
- **THEN** a resposta do token traz access token, refresh token e id token; o access token tem `sub` = id do usuário, `client_id` `buteco-painel`, `aud` [`buteco-api`, `buteco-inbox`, `buteco-connectors`], `typ` `at+jwt`, e é verificável pelo JWKS

#### Scenario: Fluxo completo com a verificação em 2 etapas
- **WHEN** um usuário com a verificação ligada abre a sessão com a senha, completa o segundo passo com um código válido, e o cliente `buteco-painel` pede autorização com PKCE e troca o código
- **THEN** a resposta do token traz access token, refresh token e id token, com o mesmo contrato do fluxo sem a verificação

#### Scenario: Sem sessão, o authorize manda para a tela de login
- **WHEN** um pedido de autorização válido chega sem o cookie `buteco-sessao`
- **THEN** a resposta é `302` com `Location` começando por `/entrar?retorno=`, e nenhum código é emitido

#### Scenario: Sessão pendente é tratada como ausência de sessão
- **WHEN** um pedido de autorização válido chega com o cookie da sessão pendente de um usuário com a verificação ligada
- **THEN** a resposta é `302` com `Location` começando por `/entrar?retorno=`, e nenhum código é emitido

#### Scenario: Cookie anterior à verificação não serve depois de ligá-la
- **WHEN** um usuário sem a verificação grava o cookie da sessão completa, liga a verificação em seguida, e um pedido de autorização chega com aquele cookie
- **THEN** nenhum código é emitido, e a resposta manda para `/entrar`

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

## ADDED Requirements

### Requirement: O segundo passo completa a sessão do emissor
O `apps/api` SHALL expor `POST /auth/session/second-factor`, anônima, que recebe `{ "codigo" }` em JSON, com as mesmas recusas de `Content-Type` (`415`) e de `Origin` (`403`) do `POST /auth/session` e o mesmo limite global de tentativas (`429`). O código SHALL ser um código do app autenticador ou um código de recuperação, distinguidos pela forma.

- **Sem cookie de sessão pendente válido** (ausente, vencido, de usuário desativado, com carimbo diferente do gravado, ou de usuário que não tem mais a verificação), a resposta SHALL ser `401` com "A entrada expirou. Informe e-mail e senha de novo.", sem contar falha.
- **Com o e-mail bloqueado**, `423` com `Retry-After`, sem conferir o código.
- **Com código errado**, `401` com "Código inválido.", contando falha no bloqueio do e-mail do usuário.
- **Com código válido**, a contagem de falhas do e-mail SHALL ser zerada, o último acesso registrado, e o cookie `buteco-sessao` gravado de novo como sessão completa, com 5 minutos a partir dali; a resposta SHALL ser `204`.

#### Scenario: Código válido completa a sessão
- **WHEN** um usuário com o cookie da sessão pendente envia um código válido do app
- **THEN** a resposta é `204` com `Set-Cookie: buteco-sessao`, e um pedido de autorização com o cookie novo recebe código

#### Scenario: Código de recuperação completa a sessão
- **WHEN** um usuário com o cookie da sessão pendente envia um código de recuperação válido
- **THEN** a resposta é `204`

#### Scenario: Código errado
- **WHEN** um usuário com o cookie da sessão pendente envia um código errado
- **THEN** a resposta é `401` com "Código inválido.", e a contagem de falhas do e-mail dele aumenta

#### Scenario: Sem sessão pendente
- **WHEN** `POST /auth/session/second-factor` chega sem o cookie, com o cookie vencido, ou depois de a verificação do usuário ser redefinida
- **THEN** a resposta é `401` com "A entrada expirou. Informe e-mail e senha de novo.", e nenhuma contagem de falhas muda

#### Scenario: Origem de outro site é recusada
- **WHEN** o pedido do segundo passo chega com `Origin: https://outro.exemplo`
- **THEN** a resposta é `403`, sem conferir o código
