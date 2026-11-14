# issuer-user-login Specification

## Purpose

Diz como um usuário local entra pelo emissor OIDC do `apps/api`: a sessão curta do
emissor aberta pelo `POST /auth/session`, o fluxo authorization code com PKCE do cliente
público `buteco-painel`, o catálogo de cliente e escopos que o próprio `apps/api` mantém,
a renovação que relê o usuário e o encerramento de sessão. Introduzida pela change
`usuarios-locais-login-emissor` (#180). A tela de login do painel é da #217.
## Requirements
### Requirement: Rota de login abre a sessão do emissor
O `apps/api` SHALL expor `POST /auth/session`, anônima, que recebe `{ "email", "senha" }` em JSON. Com credenciais válidas de um usuário ativo, SHALL gravar o cookie de sessão do emissor `buteco-sessao` com:

- `HttpOnly`;
- `SameSite=Strict`;
- `Path=/`, sem `Domain`;
- `Secure` fora do ambiente `Development`;
- vida de 5 minutos, sem renovação.

O cookie SHALL carregar o identificador e o carimbo de segurança (`security_stamp`) do usuário, e nenhuma outra credencial.

A verificação em 2 etapas é obrigatória para todo usuário, e a senha certa SHALL gravar sempre o cookie da **sessão pendente**, que o authorize não aceita. A rota SHALL NOT responder `204`:

- **com a verificação ligada**, a resposta SHALL ser `200` com `{ "segundoPasso": "totp" }`, sem zerar a contagem de falhas do e-mail e sem registrar o último acesso. A sessão se completa pela rota do segundo passo;
- **sem a verificação**, a resposta SHALL ser `200` com `{ "segundoPasso": "cadastro" }`. A contagem de falhas do e-mail SHALL ser zerada, e o último acesso SHALL NOT ser registrado. A sessão se completa pelo cadastro ("Cadastro da verificação no login, pelo cookie pendente").

Em qualquer recusa, nenhum cookie SHALL ser gravado.

#### Scenario: Login sem a verificação pede o cadastro
- **WHEN** um usuário ativo, sem a verificação em 2 etapas, envia `POST /auth/session` com e-mail e senha corretos, `Content-Type: application/json` e `Origin` igual à origem do issuer
- **THEN** a resposta é `200` com `{ "segundoPasso": "cadastro" }`, com `Set-Cookie: buteco-sessao` `HttpOnly`, `SameSite=Strict` e com expiração de 5 minutos, e um pedido de autorização com esse cookie não recebe código

#### Scenario: Login com a verificação ligada pede o segundo passo
- **WHEN** um usuário ativo com a verificação ligada envia `POST /auth/session` com e-mail e senha corretos
- **THEN** a resposta é `200` com `{ "segundoPasso": "totp" }` e `Set-Cookie: buteco-sessao`, e um pedido de autorização com esse cookie não recebe código

#### Scenario: A senha certa sem a verificação zera a contagem
- **WHEN** um usuário sem a verificação erra a senha 3 vezes e, em seguida, a acerta
- **THEN** a contagem de falhas do e-mail dele volta a zero, e o último acesso dele não muda

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

**Cookie válido** SHALL ser só o da sessão completa, gravado pelo segundo passo ou pela confirmação do cadastro, de um usuário que tem, no momento do pedido, a verificação em 2 etapas ligada. O cookie da sessão pendente (de código ou de cadastro), o cookie de usuário sem a verificação e o cookie gravado antes de a verificação ser ligada SHALL ser tratados como ausência de sessão.

O token emitido para o usuário SHALL ter:

- `sub` igual ao id do usuário;
- `client_id` igual ao cliente;
- `aud` com os recursos dos escopos pedidos;
- `typ` `at+jwt`.

#### Scenario: Fluxo completo emite token para os três apps
- **WHEN** um usuário com a verificação ligada abre a sessão com a senha e o segundo passo, o cliente `buteco-painel` pede autorização com PKCE e os escopos `openid offline_access buteco-api buteco-inbox buteco-connectors`, e troca o código com o `code_verifier`
- **THEN** a resposta do token traz access token, refresh token e id token; o access token tem `sub` = id do usuário, `client_id` `buteco-painel`, `aud` [`buteco-api`, `buteco-inbox`, `buteco-connectors`], `typ` `at+jwt`, e é verificável pelo JWKS

#### Scenario: Fluxo completo pelo cadastro no login
- **WHEN** um usuário sem a verificação abre a sessão com a senha, cadastra a verificação pelo `setup` e pelo `confirm`, e o cliente `buteco-painel` pede autorização com PKCE e troca o código
- **THEN** a resposta do token traz access token, refresh token e id token, com o mesmo contrato do fluxo com o segundo passo

#### Scenario: Sem sessão, o authorize manda para a tela de login
- **WHEN** um pedido de autorização válido chega sem o cookie `buteco-sessao`
- **THEN** a resposta é `302` com `Location` começando por `/entrar?retorno=`, e nenhum código é emitido

#### Scenario: Sessão pendente é tratada como ausência de sessão
- **WHEN** um pedido de autorização válido chega com o cookie da sessão pendente de um usuário com a verificação ligada
- **THEN** a resposta é `302` com `Location` começando por `/entrar?retorno=`, e nenhum código é emitido

#### Scenario: Cookie de cadastro é tratado como ausência de sessão
- **WHEN** um pedido de autorização válido chega com o cookie pendente de um usuário sem a verificação, antes ou depois do `setup`
- **THEN** a resposta é `302` com `Location` começando por `/entrar?retorno=`, e nenhum código é emitido

#### Scenario: Cookie anterior à verificação não serve depois de ligá-la
- **WHEN** um cookie é gravado para um usuário sem a verificação, a verificação é ligada em seguida, e um pedido de autorização chega com aquele cookie
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

O refresh resgatado SHALL continuar aceito por 30 segundos depois do primeiro resgate, para renovações concorrentes do mesmo cliente, cada uma recebendo um par novo. Reapresentado depois disso, SHALL ser recusado com `invalid_grant`, e os tokens da autorização dele SHALL ser revogados. O painel conta com essa tolerância para repetir uma renovação por até 25 segundos (`panel-issuer-session`).

O principal do usuário SHALL carregar o carimbo de segurança (`security_stamp`) vigente no authorize. O código de autorização e o refresh SHALL levá-lo adiante, o refresh a cada rotação. O carimbo SHALL NOT sair no access token nem no id token.

Todo token novo de usuário SHALL conferir o estado, o carimbo e a verificação: na troca do código (grant `authorization_code`) e em cada renovação (grant `refresh_token`), o emissor SHALL reler o usuário e SHALL recusar com `invalid_grant` quando:

- ele não existir mais;
- estiver desativado;
- o carimbo gravado for diferente do carimbo do código ou do refresh apresentado;
- ele não tiver a verificação em 2 etapas ligada. É o que encerra, na renovação seguinte, as sessões abertas antes da obrigatoriedade.

A recusa SHALL ser a do endpoint de token do OpenIddict: `400` com o corpo JSON `{ "error": "invalid_grant", … }`. É o formato que o painel já trata como sessão encerrada (`panel-issuer-session`), sem estado novo.

#### Scenario: Renovação emite novo par
- **WHEN** um refresh token válido de um usuário ativo com a verificação ligada é apresentado
- **THEN** a resposta traz um novo access token e um novo refresh token, e o refresh apresentado passa a resgatado

#### Scenario: O mesmo refresh reapresentado dentro da tolerância dá outro par
- **WHEN** um refresh token é resgatado, e o mesmo refresh é apresentado de novo 10 segundos depois (a segunda aba, que chegou depois do resgate)
- **THEN** as duas respostas são `200 OK` com pares novos, e o refresh mais novo de cada uma renova de novo com `200 OK`

#### Scenario: Reuso depois da tolerância revoga a cadeia
- **WHEN** um refresh resgatado há mais de 30 segundos é reapresentado
- **THEN** a resposta é `invalid_grant`, e o refresh emitido no primeiro resgate também passa a ser recusado

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

#### Scenario: Sessão sem a verificação não renova
- **WHEN** um refresh token emitido para um usuário que não tem a verificação ligada (a sessão aberta antes do deploy da obrigatoriedade) é apresentado
- **THEN** a resposta é `400` com `Content-Type` JSON e `error` igual a `invalid_grant`, e nenhum token é emitido

#### Scenario: O carimbo não sai nos tokens do cliente
- **WHEN** um usuário completa o fluxo e obtém access token e id token
- **THEN** nenhum dos dois contém o carimbo de segurança

### Requirement: Encerramento de sessão revoga a autorização e sempre termina
O emissor SHALL atender `GET /connect/logout`. Com um `id_token_hint` válido, SHALL revogar a autorização da sessão indicada pelo hint, apagar o cookie `buteco-sessao` e redirecionar ao `post_logout_redirect_uri` registrado do cliente. Com hint ausente ou inválido, SHALL apagar o cookie e redirecionar para `/entrar`. Em nenhum caso SHALL redirecionar para endereço não registrado.

Quando o pedido traz o cookie pendente válido de um usuário **sem** a verificação ligada (o cadastro em curso), o logout SHALL também descartar o cadastro pendente dele e trocar o carimbo de segurança dele, numa transação, antes de apagar o cookie. Com isso, uma cópia do cookie deixa de valer no servidor, e não só no navegador. O usuário sem a verificação não tem sessão que a troca do carimbo derrube: o authorize e a renovação já a recusam.

#### Scenario: Logout revoga a sessão
- **WHEN** o painel chama `/connect/logout` com o id token da sessão como `id_token_hint` e o `post_logout_redirect_uri` registrado
- **THEN** a autorização da sessão passa a `revoked`, o cookie é apagado, a resposta redireciona ao endereço registrado, e o refresh token da sessão passa a ser recusado

#### Scenario: Hint inválido ainda encerra localmente
- **WHEN** `/connect/logout` recebe um `id_token_hint` com assinatura inválida
- **THEN** o cookie é apagado e a resposta redireciona para `/entrar`

#### Scenario: Endereço de volta não registrado é ignorado
- **WHEN** `/connect/logout` recebe um `post_logout_redirect_uri` que não está registrado
- **THEN** a resposta não redireciona para ele

#### Scenario: Logout no meio do cadastro encerra o cookie no servidor
- **WHEN** um usuário sem a verificação chama o `setup`, e em seguida `/connect/logout` chega com o cookie pendente dele e sem hint
- **THEN** a resposta apaga o cookie e redireciona para `/entrar`, o cadastro pendente dele some, o carimbo de segurança dele muda, e um `confirm` com uma cópia do cookie anterior responde `401` "A entrada expirou. Informe e-mail e senha de novo."

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

### Requirement: A renovação leva o nome e o e-mail vigentes
Na renovação (grant `refresh_token`) de um usuário aceito pela conferência de estado e carimbo, o emissor SHALL substituir no principal os claims `name` e `email` pelos valores gravados do usuário naquele momento, antes de emitir os tokens. O id token e o refresh novo SHALL sair com esses valores, e o destino dos dois claims SHALL continuar sendo só o id token.

Sem isso, o principal da renovação é o do login (OpenIddict 7.7.2, `OpenIddictServerHandlers.Exchange.cs` l. 2132–2135), e um nome ou e-mail editado não chegaria ao painel, que os lê do id token, enquanto a sessão renovasse.

#### Scenario: Nome editado chega na renovação seguinte
- **WHEN** o nome de um usuário com sessão viva é editado, e a sessão renova
- **THEN** o id token da renovação traz o nome novo, e o refresh emitido nela, ao renovar de novo, também

#### Scenario: E-mail editado chega na renovação seguinte
- **WHEN** o e-mail de um usuário com sessão viva é editado, e a sessão renova
- **THEN** o id token da renovação traz o e-mail novo

#### Scenario: Os claims continuam fora do access token
- **WHEN** uma sessão renova depois da edição
- **THEN** o access token emitido não contém `name` nem `email`

### Requirement: Cadastro da verificação no login, pelo cookie pendente
O `apps/api` SHALL expor `POST /auth/session/enrollment/setup` e `POST /auth/session/enrollment/confirm`, anônimas, com as mesmas recusas de `Content-Type` (`415`) e de `Origin` (`403`) do `POST /auth/session` e o mesmo limite global de tentativas (`429`). As duas SHALL atuar só sobre o usuário do cookie `buteco-sessao`, e SHALL NOT receber usuário nem e-mail no corpo.

**O cookie aceito** SHALL ser o pendente, válido, de um usuário ativo, com o carimbo igual ao gravado e **sem** a verificação ligada. Sem ele (ausente, vencido, de usuário desativado, com carimbo diferente, ou de usuário que já tem a verificação), as duas SHALL responder `401` com "A entrada expirou. Informe e-mail e senha de novo.", sem contar falha. Com o e-mail bloqueado, `423` com `Retry-After`, sem efeito.

**`setup`**, sem corpo, SHALL gerar um segredo novo, gravado cifrado como cadastro pendente, e SHALL responder `200` com o segredo em base32 sem padding e o URI `otpauth://totp/Buteco%20Agentes:{e-mail}?secret={segredo}&issuer=Buteco%20Agentes&algorithm=SHA1&digits=6&period=30`, com o e-mail codificado. É a única resposta que SHALL conter o segredo. SHALL NOT pedir a senha: o cookie pendente já a provou.
- **O prazo do cadastro** SHALL ser de 10 minutos a partir do **primeiro** `setup` daquele cookie. O `setup` SHALL regravar o cookie pendente com o vencimento igual ao fim desse prazo, e o cadastro pendente com o mesmo início.
- **Chamar o `setup` de novo** SHALL trocar o segredo pendente por um novo, e SHALL NOT estender o prazo além do primeiro `setup` mais 10 minutos.

**`confirm`**, com `{ "codigo" }`, SHALL ligar a verificação só com um código válido do segredo pendente, na janela do TOTP (`two-factor-totp`):
- **código certo:** numa transação, o segredo pendente vira o ativo, o cadastro pendente é apagado, o passo do código é gravado como o último aceito, e os 10 códigos de recuperação são gerados; depois, a contagem de falhas do e-mail é zerada, o último acesso é registrado, e o cookie `buteco-sessao` é gravado de novo como sessão completa, com 5 minutos a partir dali. A resposta SHALL ser `200` com `{ "codigosRecuperacao": [10] }`;
- **código errado:** `401` com "Código inválido.", contando falha no bloqueio do e-mail;
- **sem cadastro pendente, ou com ele vencido:** `401` com "A entrada expirou. Informe e-mail e senha de novo.";
- **cookie sem `setup` próprio:** o `confirm` SHALL aceitar só o cookie regravado por um `setup` (o que leva o prazo do cadastro). O cookie pendente de um login seguinte, sem `setup` feito com ele, SHALL receber `401` com "A entrada expirou. Informe e-mail e senha de novo.", mesmo com um cadastro pendente ainda no prazo, aberto por um cookie anterior.

Ligar a verificação SHALL NOT trocar o `security_stamp`.

**Fora destas duas rotas e do logout**, o cookie de cadastro SHALL NOT ser aceito: o authorize o trata como ausência de sessão, e o `POST /auth/session/second-factor` responde `401` "A entrada expirou…".

#### Scenario: Setup devolve o segredo e o URI
- **WHEN** um usuário sem a verificação acerta a senha e chama o `setup` com o cookie pendente
- **THEN** a resposta é `200` com o segredo de 32 caracteres em base32 e o URI `otpauth://` com o issuer `Buteco Agentes`, o e-mail dele e os parâmetros `SHA1`, `6` e `30`

#### Scenario: Confirmação com código certo completa a sessão
- **WHEN** o `confirm` chega com um código válido do segredo pendente
- **THEN** a resposta é `200` com 10 códigos de recuperação distintos e `Set-Cookie: buteco-sessao`; a verificação fica ligada, o cadastro pendente some, e um pedido de autorização com o cookie novo recebe código

#### Scenario: O código da confirmação não serve para entrar
- **WHEN** o `confirm` aceita um código, e o mesmo código é enviado ao `POST /auth/session/second-factor` num login seguinte
- **THEN** o segundo passo responde `401` "Código inválido."

#### Scenario: Confirmação com código errado não liga
- **WHEN** o `confirm` chega com um código que não é do segredo pendente
- **THEN** a resposta é `401` "Código inválido.", a verificação continua desligada, e a contagem de falhas do e-mail aumenta

#### Scenario: Setup de novo troca o segredo
- **WHEN** o `setup` é chamado duas vezes com o mesmo cookie, e o `confirm` chega com um código do primeiro segredo
- **THEN** o `confirm` responde `401` "Código inválido.", e um código do segundo segredo é aceito

#### Scenario: O prazo é o do primeiro setup
- **WHEN** o `setup` é chamado no minuto 0, de novo no minuto 8, e o `confirm` chega no minuto 10 e meio com um código válido do segundo segredo
- **THEN** a resposta é `401` "A entrada expirou. Informe e-mail e senha de novo.", e a verificação continua desligada

#### Scenario: O cookie regravado vale até o fim do prazo
- **WHEN** a senha é aceita, o `setup` é chamado 4 minutos depois, e o `confirm` chega 13 minutos depois da senha, com um código válido
- **THEN** a resposta é `200`

#### Scenario: Cookie de um login novo, sem setup, não confirma
- **WHEN** um usuário sem a verificação acerta a senha e chama o `setup`, acerta a senha de novo (um cookie pendente novo, sem `setup`), e o `confirm` chega com esse cookie novo e um código válido do segredo pendente
- **THEN** a resposta é `401` "A entrada expirou. Informe e-mail e senha de novo.", e a verificação continua desligada

#### Scenario: Sem cookie, com cookie vencido ou de outro estado
- **WHEN** o `setup` ou o `confirm` chega sem o cookie, com o cookie vencido, com o carimbo do usuário trocado, de um usuário desativado, ou com o cookie pendente de um usuário que já tem a verificação ligada
- **THEN** a resposta é `401` "A entrada expirou. Informe e-mail e senha de novo.", nada é gravado, e nenhuma contagem de falhas muda

#### Scenario: O cookie de um usuário não cadastra outro
- **WHEN** o usuário A chama o `setup` com o próprio cookie pendente, e o usuário B chama o `confirm` com o cookie pendente dele e um código válido do segredo de A
- **THEN** o `confirm` de B responde `401`, e nenhum dos dois tem a verificação ligada

#### Scenario: Bloqueio no cadastro
- **WHEN** o e-mail do usuário está bloqueado, e o `setup` ou o `confirm` chega com o cookie pendente válido
- **THEN** a resposta é `423` com `Retry-After`, sem gerar segredo nem conferir código

#### Scenario: O cookie de cadastro não serve no segundo passo
- **WHEN** o `POST /auth/session/second-factor` chega com o cookie pendente de um usuário sem a verificação
- **THEN** a resposta é `401` "A entrada expirou. Informe e-mail e senha de novo."

#### Scenario: Origem de outro site é recusada
- **WHEN** o `setup` ou o `confirm` chega com `Origin: https://outro.exemplo`
- **THEN** a resposta é `403`, sem efeito

### Requirement: O papel no access token e no id token
O authorize SHALL pôr no principal do usuário o claim `role` com o papel gravado dele (`admin`, `operator` ou `reader`), com destino no access token **e** no id token. Na troca do código e em toda renovação de um usuário aceito pela conferência de estado e carimbo, o emissor SHALL substituir o `role` do principal pelo papel gravado naquele momento, com os dois destinos gravados de novo, antes de emitir os tokens. O refresh emitido na renovação SHALL levar o papel novo.

#### Scenario: Papel no login
- **WHEN** um usuário de papel `operator` entra pelo fluxo do painel
- **THEN** o access token e o id token trazem `role` `operator`

#### Scenario: Papel novo na renovação
- **WHEN** o papel de um usuário com sessão viva muda de `admin` para `reader`, e a sessão renova
- **THEN** o access token e o id token da renovação trazem `role` `reader`, e o refresh emitido nela, ao renovar de novo, também

#### Scenario: Refresh de antes do papel
- **WHEN** um refresh emitido antes desta change, cujo principal não tem `role`, renova
- **THEN** os tokens emitidos trazem o `role` gravado do usuário

#### Scenario: O papel não é o subject
- **WHEN** um usuário de papel `admin` obtém um access token pelo fluxo do painel
- **THEN** o `sub` continua sendo o id dele, e o `role` é um claim à parte

### Requirement: A sessão pendente também nasce do retorno do provedor federado
O retorno do provedor que decide pela entrada (`federated-login`, "Decisão do login no retorno") SHALL gravar o cookie `buteco-sessao` da **sessão pendente**, com os mesmos atributos e o mesmo conteúdo do login por senha (`HttpOnly`, `SameSite=Strict`, `Path=/`, `Secure` fora de `Development`, 5 minutos, o id e o `security_stamp`, **sem** o claim da verificação). A verificação em 2 etapas SHALL continuar obrigatória para todo usuário, qualquer que seja a forma de entrada: o retorno SHALL mandar ao passo `totp` (com a verificação ligada) ou ao `cadastro` (sem ela), e a sessão SHALL se completar só pelas rotas de hoje (`POST /auth/session/second-factor`, `POST /auth/session/enrollment/setup` e `/confirm`). O authorize, a troca do código e a renovação SHALL NOT mudar.

**Como o cookie atravessa a cadeia cross-site.** A navegação que volta do provedor começa em outro site. Por isso:

1. o cookie SHALL ser gravado na **resposta do retorno** (`GET /auth/federated/callback`), junto do `302` para o `/entrar`. A gravação é permitida porque o pedido é uma navegação de topo: draft-ietf-httpbis-rfc6265bis-22, §5.7, passo 18.3 ("If the cookie was received from a request which is navigating a top-level traversable … skip the remaining substeps and continue processing the cookie"; nota: "Top-level navigations can create a cookie with any `SameSite` value"). O que o §5.7 recusa é a gravação de `Lax` ou `Strict` em resposta a subrecurso cross-site (§4.1.2.7);
2. o cookie **não** acompanha o carregamento do `/entrar` que o `302` provoca, porque esse pedido ainda pertence à navegação iniciada no outro site (§5.6.7.1: "Same-site cookies in "Strict" enforcement mode will not be sent along with top-level navigations which are triggered from a cross-site document context"). O `/entrar` é o documento do painel e não lê a sessão;
3. o cookie **acompanha** o `POST` do segundo passo ou do cadastro, feito por `fetch` do documento do painel: o "site for cookies" desse documento é a origem do painel (§5.2.1: "For a document displayed in a top-level traversable, the document's "site for cookies" is the top-level origin"), a URL é da mesma origem, e o pedido é same-site (§5.2);
4. o cookie completo acompanha o `/connect/authorize` porque a navegação ao `retorno` é iniciada pelo próprio documento do painel, como no login por senha.

Por isso o retorno SHALL NOT responder `302` direto ao `/connect/authorize`: esse pedido ainda seria da cadeia cross-site, e o cookie `Strict` não iria.

#### Scenario: O cookie pendente do retorno não emite código
- **WHEN** um usuário vinculado volta do provedor, e o mesmo navegador pede `GET /connect/authorize` com o cookie gravado pelo retorno
- **THEN** a resposta é `302` para `/entrar`, sem código

#### Scenario: O segundo passo completa a sessão federada
- **WHEN** depois do retorno, o painel envia `POST /auth/session/second-factor` com o cookie gravado pelo retorno e um código válido
- **THEN** a resposta é `204`, e o authorize seguinte com o cookie emite o código

#### Scenario: Quem nunca cadastrou cadastra no login federado
- **WHEN** um usuário vinculado, sem a verificação, volta do provedor e o painel chama `POST /auth/session/enrollment/setup` e `/confirm` com o cookie do retorno
- **THEN** o `setup` responde `200` com o segredo, e o `confirm` com um código válido responde `200` com os códigos de recuperação e completa a sessão

#### Scenario: O retorno nunca manda ao authorize
- **WHEN** o retorno decide pela entrada, com um `retorno` de authorize no início
- **THEN** o `Location` é `/entrar?…`, com o `retorno` na query, e não `/connect/authorize`

### Requirement: A sessão pendente também nasce do aceite de convite pelo provedor
O retorno que aceita um convite pelo provedor (`federated-login`, "Decisão do aceite de convite no retorno") SHALL gravar o cookie `buteco-sessao` da **sessão pendente**, com os mesmos atributos e o mesmo conteúdo do retorno do login federado ("A sessão pendente também nasce do retorno do provedor federado"), só **depois** de a transação do consumo e do vínculo terminar com sucesso, e com o usuário relido no estado ativo. Como o convidado nunca tem a verificação em 2 etapas, o passo SHALL ser o `cadastro`, completado pelas rotas de hoje (`POST /auth/session/enrollment/setup` e `/confirm`). O retorno SHALL responder `302` para o `/entrar`, e SHALL NOT responder `302` ao `/connect/authorize`. O authorize, a troca do código e a renovação SHALL NOT mudar.

#### Scenario: O cookie do aceite não emite código
- **WHEN** um convite é aceito pelo provedor, e o mesmo navegador pede `GET /connect/authorize` com o cookie gravado pelo retorno
- **THEN** a resposta é `302` para `/entrar`, sem código

#### Scenario: O cadastro completa a sessão do aceite
- **WHEN** depois do aceite, o painel chama `POST /auth/session/enrollment/setup` e `/confirm` com o cookie do retorno e um código válido
- **THEN** o `setup` responde `200` com o segredo, o `confirm` responde `200` com os códigos de recuperação e o `Set-Cookie: buteco-sessao` completo, e o authorize seguinte com o cookie emite o código

#### Scenario: O passo do aceite é sempre o cadastro
- **WHEN** um convite é aceito pelo provedor
- **THEN** o `Location` do retorno traz `segundoPasso=cadastro`, e o `POST /auth/session/second-factor` com o cookie do retorno responde `401` "A entrada expirou. Informe e-mail e senha de novo."

### Requirement: Os logs do emissor não levam dado pessoal nem segredo, em nenhuma categoria
Nenhuma linha de log do login por senha, do segundo passo, do cadastro da verificação, do authorize, da troca do código, da renovação, do logout e do `client_credentials` (de serviço e de aplicação externa) SHALL conter o e-mail, o nome, a senha, o segredo ou o código das 2 etapas, os códigos de recuperação, o código do authorize, o `code_verifier`, os tokens nem o segredo do cliente. Vale para as linhas do Buteco e para as da biblioteca do servidor OIDC (`OpenIddict.Server`), no nível configurado em todo ambiente: o `appsettings.json` SHALL fixar as categorias `OpenIddict` em `Warning` ou acima, porque a biblioteca grava o pedido de token com o `code_verifier` em `Information` e os tokens inteiros em `Trace`.

#### Scenario: O login por senha, a renovação e o logout não vazam
- **WHEN** um usuário entra por senha, cadastra a verificação, entra de novo com o código, renova e sai, com o log capturado em todas as categorias do Buteco e do OpenIddict
- **THEN** nenhuma linha contém o e-mail, o nome, a senha, o segredo das 2 etapas, os códigos, o código do authorize, o `code_verifier` nem os tokens

#### Scenario: O client_credentials não vaza
- **WHEN** um cliente de serviço e uma aplicação externa pedem token por `client_credentials`
- **THEN** nenhuma linha contém o segredo do cliente nem o token emitido

#### Scenario: Sem o filtro da biblioteca, o pedido de token vaza
- **WHEN** a categoria `OpenIddict` volta ao nível `Information`
- **THEN** a linha `OpenIddictServerDispatcher[6075]` leva o `code_verifier` do pedido de token

