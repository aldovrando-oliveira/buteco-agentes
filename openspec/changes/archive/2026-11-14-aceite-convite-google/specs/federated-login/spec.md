## ADDED Requirements

### Requirement: Início do aceite de convite pelo provedor
`POST /auth/invitation/federated/{chave}/start` SHALL ser anônima (`AuthEntryPoint`), com as barreiras do login (só JSON, `Origin` conferida) e no limite global de verificações, e SHALL receber `{ token }`, o token do link do convite (`user-access-links`), no corpo. Na ordem:

1. chave fora da configuração SHALL responder `404` com `title` "Provedor não configurado.", sem cookie;
2. o convite SHALL ser conferido como no aceite por senha (o hash do token, o propósito `invitation`, o vencimento e o estado convidado); inválido SHALL responder a resposta única de link inválido (`user-access-links`), sem cookie e sem nenhum pedido ao provedor;
3. o discovery do provedor SHALL ser buscado como no início do login (o prazo de 10 segundos, o `503` com o provedor fora do alcance);
4. o desafio SHALL levar no state token a intenção `convite`, o id do convidado e o SHA-256 do token em base64url, e SHALL NOT levar o token nem o link; SHALL NOT levar `retorno`;
5. a resposta SHALL ser `200 { url }`, com o cookie do fluxo na mesma resposta, como no início do login.

O pedido ao provedor SHALL ser o mesmo do login (escopos, `prompt`, modo de resposta), sem `login_hint`.

#### Scenario: Início do aceite devolve a URL e o cookie
- **WHEN** o painel chama `POST /auth/invitation/federated/corporativo/start` com o token de um convite pendente, JSON e a `Origin` do painel
- **THEN** a resposta é `200` com `url` em `https://accounts.google.com/`, com `Set-Cookie: buteco-fluxo-externo.<nonce>`, e o `url` não contém o token nem o e-mail do convite

#### Scenario: O state token leva o hash, e não o link
- **WHEN** o início do aceite responde `200`
- **THEN** o state token gravado em `openiddict_tokens`, decifrado, traz a intenção `convite`, o id do convidado e o SHA-256 do token em base64url, e o texto decifrado não contém o token

#### Scenario: Convite inválido no início
- **WHEN** o início recebe o token de um convite vencido, de um cancelado, de um já aceito e de um link de redefinição
- **THEN** as quatro respostas são iguais à resposta de link inválido de `POST /auth/invitation`, sem `Set-Cookie`, e o provedor não recebe nenhum pedido

#### Scenario: Chave desconhecida no início do aceite
- **WHEN** o painel chama `POST /auth/invitation/federated/inexistente/start` com o token de um convite pendente
- **THEN** a resposta é `404` "Provedor não configurado.", sem `Set-Cookie`

#### Scenario: Início do aceite de outra origem
- **WHEN** o início do aceite chega com `Origin` de outro site
- **THEN** a resposta é a recusa das barreiras do login, sem cookie, e o convite não é consultado

### Requirement: Decisão do aceite de convite no retorno
Com a intenção `convite` no state token, depois das conferências do retorno (`O apps/api é cliente OIDC do provedor, atrás do emissor do Buteco`), o `apps/api` SHALL decidir nesta ordem, parando na primeira que casa:

1. `HostedDomain` configurado e claim `hd` ausente ou diferente → recusa `dominio-nao-permitido`;
2. o usuário do state token não existe, não está no estado convidado, ou o link gravado dele não tem o hash do state token, o propósito `invitation` e o vencimento no futuro → recusa `convite-invalido`;
3. `email_verified` diferente de `true` → recusa `email-nao-verificado`;
4. o e-mail do provedor, normalizado, diferente do e-mail normalizado do convidado → recusa `outro-email`;
5. o par (issuer, `sub`) já vinculado a um usuário → recusa `em-uso`;
6. senão, numa mesma transação: o consumo do convite pela condição do aceite por senha (o id, o hash, o propósito, o vencimento e o estado convidado), mais o e-mail normalizado igual ao do provedor, que põe o usuário no estado ativo e apaga o link **sem gravar senha**; e a gravação do vínculo. O consumo com zero linhas SHALL desfazer a transação e recusar com `convite-invalido`; a violação de unicidade do vínculo SHALL desfazer a transação, inclusive o consumo, e recusar com `em-uso`.

Aceito, o `apps/api` SHALL zerar o bloqueio do e-mail do usuário, gravar o cookie da sessão pendente (`issuer-user-login`) e responder `302 /entrar?federado=<chave>&segundoPasso=cadastro&convite=aceito`. O retorno SHALL NOT mandar ao `/connect/authorize`.

#### Scenario: Aceitar o convite pelo provedor
- **WHEN** o convidado começa o aceite e volta do provedor com o e-mail do convite, verificado, e uma conta sem vínculo
- **THEN** a resposta é `302` para `/entrar?federado=corporativo&segundoPasso=cadastro&convite=aceito` com `Set-Cookie: buteco-sessao` da sessão pendente; o usuário fica `ativo`, sem senha, com o papel do convite; o vínculo existe com o issuer e o `sub`; o link do convite recebe a resposta de link inválido; e `GET /users/{id}` traz `formasDeEntrada` `["google"]`

#### Scenario: O aceite pelo provedor termina no token do Buteco
- **WHEN** depois do aceite, o painel cadastra a verificação com o cookie do retorno e troca o código do authorize no `/connect/token`
- **THEN** o access token tem o `iss` do emissor do Buteco, `aud` `buteco-api`, `client_id` `buteco-painel`, `sub` igual ao id do usuário e o `role` do convite

#### Scenario: E-mail diferente do convite
- **WHEN** o provedor devolve um e-mail verificado diferente do e-mail do convite
- **THEN** a resposta é `302 /convite?recusa=outro-email&provedor=corporativo`, com o `buteco-recusa`; o usuário continua convidado, sem vínculo, e o link continua aceito por senha

#### Scenario: E-mail não verificado
- **WHEN** o provedor devolve o e-mail do convite com `email_verified` falso
- **THEN** a resposta é `302 /convite?recusa=email-nao-verificado&provedor=corporativo`, sem `buteco-recusa`; o usuário continua convidado, sem vínculo, e o link continua aceito por senha

#### Scenario: O convite morto vem antes do e-mail
- **WHEN** o convite é cancelado entre o início e o retorno, e o provedor devolve um e-mail diferente do convite
- **THEN** a resposta é `302 /convite?recusa=convite-invalido&provedor=corporativo`, e não `outro-email`, sem `buteco-recusa`

#### Scenario: Convite que deixou de valer no meio
- **WHEN** entre o início e o retorno o convite vence, é cancelado, é aceito por senha, ou recebe um link novo
- **THEN** cada retorno responde `302 /convite?recusa=convite-invalido&provedor=corporativo`, sem vínculo gravado; no caso do link novo, o link novo continua aceito

#### Scenario: Conta do provedor já vinculada a outro usuário
- **WHEN** o provedor devolve o e-mail do convite, verificado, com um par já vinculado a outro usuário
- **THEN** a resposta é `302 /convite?recusa=em-uso&provedor=corporativo`; o usuário continua convidado, nenhum vínculo é gravado nem muda, e o vínculo do outro usuário continua o mesmo

#### Scenario: Domínio fora do permitido no aceite
- **WHEN** o `HostedDomain` é `empresa.com` e o provedor devolve o e-mail do convite, verificado, com `hd` `outra.com`
- **THEN** a resposta é `302 /convite?recusa=dominio-nao-permitido&provedor=corporativo`, e o convite continua pendente

#### Scenario: O e-mail do convidado editado no meio
- **WHEN** o e-mail do convidado é editado entre a conferência do e-mail e o consumo, e o provedor devolveu o e-mail antigo
- **THEN** o consumo grava zero linhas, a resposta é `302 /convite?recusa=convite-invalido&provedor=corporativo`, e nenhum vínculo é gravado

#### Scenario: O aceite zera o bloqueio
- **WHEN** o e-mail do convidado está bloqueado por tentativas de login, e o convite é aceito pelo provedor
- **THEN** o `POST /auth/session/enrollment/setup` com o cookie do retorno responde `200`, e não `423`

### Requirement: As recusas do aceite não criam sessão e voltam ao convite
Toda recusa do retorno com a intenção `convite` SHALL responder `302 /convite?recusa=<motivo>&provedor=<chave>`, com `motivo` em `outro-email`, `email-nao-verificado`, `convite-invalido`, `em-uso`, `dominio-nao-permitido` ou `falha`. Nenhuma recusa SHALL gravar o cookie `buteco-sessao`, gravar vínculo ou mudar o usuário, e todas SHALL apagar o cookie do fluxo. A URL SHALL NOT levar o token, o hash do link nem e-mail.

A recusa `falha` SHALL valer como no login: qualquer falha das conferências do cliente OIDC, inclusive `error=access_denied` (a pessoa cancelou no provedor) e o provedor que saiu da configuração entre o início e o retorno. Quando a intenção não se lê do state token, ou quando nenhum provedor está configurado, a resposta SHALL ser a do login, `302 /entrar?recusa=falha`, sem sessão.

#### Scenario: Nenhuma recusa do aceite grava sessão
- **WHEN** o retorno do aceite termina, uma vez em cada um dos seis motivos
- **THEN** cada resposta é o `302` para `/convite` com o motivo, nenhuma traz `Set-Cookie: buteco-sessao`, nenhum vínculo é gravado, e um `GET /connect/authorize` seguinte com os cookies do navegador responde `302` para `/entrar`

#### Scenario: A pessoa cancelou no provedor
- **WHEN** o provedor volta com `error=access_denied` e o state do aceite
- **THEN** a resposta é `302 /convite?recusa=falha&provedor=corporativo`, sem cookie de sessão, e o convite continua aceito por senha

#### Scenario: O provedor saiu da configuração no meio
- **WHEN** o aceite começa com o provedor `corporativo`, e o retorno chega depois de a chave ser trocada (ou de o `apps/api` ficar sem nenhum provedor)
- **THEN** a resposta é a recusa `falha` (`/convite?recusa=falha&provedor=corporativo` com a chave trocada; `/entrar?recusa=falha` sem nenhum provedor), sem cookie de sessão e sem vínculo, e o convite continua pendente

#### Scenario: A URL da recusa não leva o token nem o e-mail
- **WHEN** o retorno do aceite recusa com o motivo `outro-email`
- **THEN** o `Location` não contém o token, o hash do link, o e-mail do convite nem o e-mail confirmado

### Requirement: O aceite pelo provedor e o aceite por senha disputam o mesmo convite
O consumo do convite pelo provedor e o aceite por senha (`user-access-links`) SHALL usar a mesma condição de consumo, de modo que, para o mesmo convite, no máximo um tenha sucesso. O vínculo SHALL ser gravado só na transação do consumo que teve sucesso: SHALL NOT existir vínculo de um convite que não foi consumido pelo provedor, nem convite consumido pelo provedor sem vínculo.

#### Scenario: Senha e provedor ao mesmo tempo
- **WHEN** o mesmo convite é aceito ao mesmo tempo por senha e pelo retorno do provedor
- **THEN** uma das duas tem sucesso e a outra recebe a recusa dela (o `404` de link inválido ou o `convite-invalido`); se a senha venceu, o usuário tem a senha e nenhum vínculo; se o provedor venceu, o usuário tem o vínculo e nenhuma senha

#### Scenario: Dois retornos do provedor ao mesmo tempo
- **WHEN** dois retornos do aceite do mesmo convite, de dois fluxos, chegam ao mesmo tempo
- **THEN** um responde o `302` do aceite e o outro `convite-invalido`, e o usuário tem um vínculo só

#### Scenario: O vínculo que falha desfaz o consumo
- **WHEN** a gravação do vínculo viola a unicidade depois do consumo do convite
- **THEN** a resposta é `em-uso`, o usuário continua convidado, com o link que tinha, e o link continua aceito por senha

### Requirement: Logs do aceite de convite pelo provedor sem dado pessoal
Toda recusa do aceite SHALL gerar uma linha `Warning` com o `EventId` 1202 (`FederatedInvitationRefused`), com a chave do provedor, o motivo e o id do convidado do state token; a recusa `falha` SHALL gerar, em vez dela, o `EventId` 1199 (`FederatedLoginFailed`), como no login. O aceite SHALL gerar uma linha `Information` com o `EventId` 1203 (`FederatedInvitationAccepted`), com o id do usuário e a chave. Nenhuma linha, **de nenhuma categoria** (as do Buteco e as do OpenIddict, cliente e servidor), SHALL conter o e-mail do provedor, o e-mail do convite, o `sub`, o token, o hash do link, o código, o state nem o id token; na recusa `em-uso`, SHALL NOT conter o id do outro usuário.

#### Scenario: O log do aceite não leva e-mail nem sub
- **WHEN** o aceite é percorrido uma vez em cada desfecho (o aceite e as seis recusas), com o log capturado
- **THEN** nenhuma linha capturada contém o e-mail confirmado, o e-mail do convite, o `sub`, o token nem o hash do link, e cada desfecho tem a sua linha com o `EventId` e o motivo

## MODIFIED Requirements

### Requirement: O e-mail confirmado da recusa, por cookie de uso único
As recusas `convite-pendente`, `nao-conectada` e `sem-acesso` do login, e a recusa `outro-email` do aceite de convite pelo provedor, SHALL gravar, na mesma resposta, o cookie `buteco-recusa` com `{ motivo, provedor, email }` protegido pelo Data Protection, `HttpOnly`, `SameSite=Strict`, `Path=/auth/federated/refusal`, sem `Domain`, `Secure` fora de `Development`, com `Max-Age` de 120 segundos.

`POST /auth/federated/refusal` SHALL ser anônima (`AuthEntryPoint`), com as barreiras do login. Com o cookie válido e no prazo, SHALL responder `200 { motivo, provedor, email }` e apagar o cookie na mesma resposta. Sem cookie, com cookie vencido, adulterado ou já lido, SHALL responder `404` com o corpo vazio, igual nos quatro casos, e apagar o cookie que houver. O `email` do cookie SHALL ser sempre o confirmado pelo provedor, e nunca o do convite nem o do usuário do Buteco.

#### Scenario: O painel lê o e-mail uma vez
- **WHEN** o painel chama `POST /auth/federated/refusal` logo depois de uma recusa `sem-acesso`, e chama de novo
- **THEN** a primeira resposta é `200` com `motivo` `sem-acesso`, `provedor` e o e-mail confirmado, com o cookie apagado, e a segunda é `404` vazio

#### Scenario: A rota não revela nada sem o cookie
- **WHEN** a rota é chamada sem cookie, com um cookie adulterado e com um cookie de mais de 120 segundos
- **THEN** as três respostas são `404` com o corpo vazio, iguais entre si

#### Scenario: Atributos do cookie da recusa
- **WHEN** o retorno recusa com o motivo `nao-conectada`
- **THEN** o `Set-Cookie: buteco-recusa` traz `HttpOnly`, `SameSite=Strict`, `Path=/auth/federated/refusal` e `Max-Age=120`, e o valor não traz o e-mail em claro

#### Scenario: O aviso de outro e-mail no aceite de convite
- **WHEN** o retorno do aceite de convite recusa com o motivo `outro-email`, e o painel chama `POST /auth/federated/refusal` a partir de `/convite`
- **THEN** a resposta é `200` com `motivo` `outro-email`, `provedor` e o e-mail que o provedor confirmou, e não o e-mail do convite; uma segunda chamada responde `404` vazio

### Requirement: Decisão do login no retorno
Depois das conferências do retorno, o `apps/api` SHALL decidir nesta ordem, parando na primeira que casa:

1. `HostedDomain` configurado e claim `hd` ausente ou diferente → recusa `dominio-nao-permitido`;
2. vínculo pelo par com usuário desativado → recusa `desativado`;
3. vínculo pelo par com usuário ativo → **entra**: grava o cookie da sessão pendente (`issuer-user-login`) e responde `302 /entrar?federado=<chave>&segundoPasso=<totp|cadastro>`, com `&retorno=<retorno>` quando o início trouxe um;
4. sem vínculo e `email_verified` diferente de `true` → recusa `email-nao-verificado`;
5. sem vínculo, convidado com o e-mail verificado → recusa `convite-pendente`;
6. sem vínculo, usuário ativo com o e-mail verificado → recusa `nao-conectada`;
7. qualquer outro caso, inclusive o usuário desativado sem vínculo → recusa `sem-acesso`.

Qualquer falha das conferências, inclusive `error=access_denied` do provedor e a registração do state que não existe mais, SHALL ser a recusa `falha`. A registração do state que não existe mais (a chave do provedor trocada entre o início e o retorno) SHALL ser recusada pelo cliente OIDC **antes** de resolver a registração, e SHALL NOT virar exceção nem `500`; com as propriedades do state, a `falha` SHALL voltar para onde a intenção começou: `/entrar` no login, `/account` no conectar, `/convite` no aceite de convite, com `&provedor=<chave>` do início. Sem nenhum provedor configurado, o retorno SHALL responder `302 /entrar?recusa=falha` sem ler o state, qualquer que seja a intenção. Nos dois casos, nenhum cookie `buteco-sessao` SHALL ser gravado, nenhum vínculo SHALL ser gravado, e o convite SHALL continuar como estava. O e-mail SHALL ser comparado normalizado. O retorno SHALL NOT mandar ao `/connect/authorize`.

#### Scenario: Vinculado ativo com a verificação ligada
- **WHEN** um usuário ativo, vinculado, com a verificação ligada, volta do provedor
- **THEN** a resposta é `302` para `/entrar?federado=corporativo&segundoPasso=totp` com o `retorno` do início, e com `Set-Cookie: buteco-sessao` da sessão pendente

#### Scenario: Vinculado ativo sem a verificação
- **WHEN** um usuário ativo, vinculado, sem a verificação, volta do provedor
- **THEN** a resposta é `302` para `/entrar?federado=corporativo&segundoPasso=cadastro`, com o cookie da sessão pendente

#### Scenario: Usuário desativado sem vínculo não vê "desativado"
- **WHEN** um usuário desativado e sem vínculo tem o e-mail que o provedor confirmou
- **THEN** o retorno recusa com o motivo `sem-acesso`

#### Scenario: E-mail não verificado sem vínculo
- **WHEN** a conta do provedor, sem vínculo, tem o e-mail de um usuário ativo com `email_verified` falso
- **THEN** o retorno recusa com o motivo `email-nao-verificado`

#### Scenario: Domínio fora do permitido, mesmo vinculado
- **WHEN** o `HostedDomain` é `empresa.com` e um usuário vinculado volta com `hd` `outra.com`
- **THEN** o retorno recusa com o motivo `dominio-nao-permitido`

#### Scenario: Chave trocada no meio do login
- **WHEN** um usuário vinculado começa o login com o provedor `corporativo`, e o retorno chega depois de o `apps/api` deixar de ter a registração `corporativo` (outra registração presente)
- **THEN** a resposta é `302 /entrar?recusa=falha&provedor=corporativo`, e não `500`, sem `Set-Cookie: buteco-sessao`, com o log `1199` e o erro `invalid_request`

#### Scenario: Chave trocada no meio do conectar
- **WHEN** um usuário começa o conectar com o provedor `corporativo`, e o retorno chega depois de o `apps/api` deixar de ter a registração `corporativo`
- **THEN** a resposta é `302 /account?recusa=falha&provedor=corporativo`, e não `500`, sem cookie de sessão e sem vínculo gravado

#### Scenario: Chave trocada no meio do aceite de convite
- **WHEN** um convidado começa o aceite com o provedor `corporativo`, e o retorno chega depois de o `apps/api` deixar de ter a registração `corporativo`
- **THEN** a resposta é `302 /convite?recusa=falha&provedor=corporativo`, e não `500`, sem cookie de sessão e sem vínculo, e o convite continua pendente

#### Scenario: Nenhum provedor configurado no retorno do login
- **WHEN** o login começou com o único provedor, e o retorno chega depois de o `apps/api` reiniciar sem nenhum provedor
- **THEN** a resposta é `302 /entrar?recusa=falha`, sem cookie de sessão

#### Scenario: Nenhum provedor configurado no retorno do conectar
- **WHEN** o conectar começou com o único provedor, e o retorno chega depois de o `apps/api` reiniciar sem nenhum provedor
- **THEN** a resposta é `302 /entrar?recusa=falha`, sem cookie de sessão e sem vínculo gravado: sem provedor, o state não é lido e a intenção não se conhece

#### Scenario: Nenhum provedor configurado no retorno do aceite
- **WHEN** o aceite começou com o único provedor, e o retorno chega depois de o `apps/api` reiniciar sem nenhum provedor
- **THEN** a resposta é `302 /entrar?recusa=falha`, sem cookie de sessão e sem vínculo, e o convite continua pendente

### Requirement: Logs do login federado sem dado pessoal
Toda recusa SHALL gerar uma linha `Warning` com o `EventId` 1198 (`FederatedLoginRefused`), com a chave do provedor, o motivo e, quando há, o id do usuário; a recusa `falha` SHALL gerar, em vez dela, o `EventId` 1199 (`FederatedLoginFailed`), com o `error` do cliente OIDC e sem o `error_description` do provedor. Conectar e desconectar SHALL gerar `Information` com os `EventId` 1200 (`FederatedIdentityConnected`) e 1201 (`FederatedIdentityDisconnected`), com o id do usuário, a chave e, na desconexão, quem a fez. Nenhuma linha de log do fluxo, **de nenhuma categoria**, SHALL conter o e-mail do provedor, o e-mail do usuário, o `sub`, o código, o state, o id token nem o segredo: vale para as linhas do Buteco e para as da biblioteca do cliente e do servidor OIDC (`OpenIddict.*`), no nível configurado em todo ambiente. O `appsettings.json` SHALL fixar as categorias `OpenIddict` em `Warning` ou acima, porque a biblioteca grava a resposta do userinfo do provedor (com o `sub` e o e-mail) em `Information`.

#### Scenario: O log não leva e-mail nem sub
- **WHEN** o fluxo é percorrido uma vez em cada desfecho (entrada, as sete recusas, conectar, desconectar), com o log capturado
- **THEN** nenhuma linha capturada, das categorias do Buteco e das do OpenIddict, contém o e-mail confirmado, o e-mail do usuário nem o `sub` devolvido pelo provedor, e cada recusa tem a sua linha com o motivo

#### Scenario: A linha do userinfo da biblioteca não chega ao log
- **WHEN** um usuário vinculado entra pelo provedor com a configuração de log do `appsettings.json`
- **THEN** nenhuma linha da categoria `OpenIddict.Client` contém o `sub` nem o e-mail devolvidos pelo userinfo do provedor

#### Scenario: Sem o filtro da biblioteca, o log vaza
- **WHEN** a categoria `OpenIddict` volta ao nível `Information`
- **THEN** a linha `OpenIddictClientDispatcher[6195]` leva o `sub` e o e-mail do provedor, e por isso o `appsettings.json` SHALL NOT deixar a categoria abaixo de `Warning`
