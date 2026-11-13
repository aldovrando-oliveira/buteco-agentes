# federated-login Specification

## Purpose
Diz como o `apps/api` faz o login pelo provedor federado atrás do emissor do Buteco (#189, M3): o `apps/api` é cliente OIDC do provedor, o painel continua falando só com o emissor, e o token que os apps validam não muda. Cobre o início do fluxo (com o prazo e o `503` do provedor fora do alcance), o cookie do fluxo externo, a decisão do retorno pelo par (`issuer`, `sub`), as recusas sem sessão com o e-mail num cookie de uso único, os logs sem dado pessoal, conectar e desconectar, e a regra da última forma de entrada. Introduzida pela change `provedores-federados-google`. A sessão pendente que o retorno grava e o segundo passo estão em `issuer-user-login`; as telas são da #190; o aceite de convite pelo provedor é da #274.
## Requirements
### Requirement: O apps/api é cliente OIDC do provedor, atrás do emissor do Buteco
O `apps/api` SHALL autenticar no provedor federado como cliente OIDC (authorization code com PKCE S256 e nonce), e SHALL ser o único a falar com ele. O painel SHALL continuar falando só com o emissor do Buteco. O provedor SHALL só autenticar: SHALL NOT criar usuário, e nenhum token do provedor SHALL ser aceito pelos apps do Buteco. Os tokens que o emissor emite depois de um login federado SHALL ser os mesmos do login por senha: mesmo `iss`, `aud`, `client_id` e `sub` (o id do usuário).

O retorno SHALL conferir, antes de qualquer decisão do Buteco: o state token (emitido pelo Buteco, no prazo e não usado), a correlação com o cookie do fluxo, o PKCE, o nonce, a assinatura do id token pelas chaves do provedor, o `aud` igual ao `ClientId` e o `iss` igual ao issuer do tipo (com ou sem a barra final). A identidade do provedor SHALL ser o par (issuer fixo do tipo, `sub` do id token).

#### Scenario: O token do Buteco não muda com o login federado
- **WHEN** um usuário entra pelo provedor, completa o segundo passo e troca o código no `/connect/token`
- **THEN** o access token tem o `iss` do emissor do Buteco, `aud` `buteco-api`, `client_id` `buteco-painel` e `sub` igual ao id do usuário, como no login por senha

#### Scenario: Id token com iss diferente é recusado
- **WHEN** o provedor devolve um id token com `iss` `https://outro.exemplo.com` ou `accounts.google.com`, sem esquema
- **THEN** o retorno recusa com o motivo `falha`, e nenhum cookie de sessão é gravado

#### Scenario: Id token de outro cliente é recusado
- **WHEN** o id token traz `aud` diferente do `ClientId` configurado
- **THEN** o retorno recusa com o motivo `falha`

#### Scenario: Nonce errado é recusado
- **WHEN** o id token traz um `nonce` diferente do pedido
- **THEN** o retorno recusa com o motivo `falha`

#### Scenario: Assinatura de outra chave é recusada
- **WHEN** o id token é assinado por uma chave fora do JWKS do provedor
- **THEN** o retorno recusa com o motivo `falha`

### Requirement: Início do login federado
`POST /auth/federated/{chave}/start` SHALL ser anônima (`AuthEntryPoint`), com as barreiras do login (só JSON, `Origin` conferida) e no limite global de verificações. SHALL receber `{ retorno? }`; um `retorno` que não começa por `/connect/authorize?` SHALL ser descartado. SHALL responder `200 { url }` com o endereço de autorização do provedor, e SHALL gravar o cookie do fluxo na mesma resposta. Chave fora da configuração SHALL responder `404` com `title` "Provedor não configurado.", sem cookie. Antes do desafio, o `apps/api` SHALL buscar o discovery do provedor com um prazo explícito de 10 segundos, **pela configuração em cache do cliente OIDC** (o `ConfigurationManager` da registração, o mesmo que o desafio lê, renovado a cada 12 horas): enquanto o cache vale, o início não faz pedido nenhum ao provedor; com o provedor fora do alcance (conexão recusada, erro ou prazo vencido), SHALL responder `503` com `application/problem+json` e `title` "Não foi possível falar com o provedor. Tente de novo em instantes.", sem cookie e sem URL, e SHALL registrar o `EventId` 1199. O mesmo vale para o início do conectar. Com o provedor fora do alcance **no retorno** (a troca do código ou as chaves), a resposta SHALL continuar sendo a recusa `falha`.

#### Scenario: Início devolve a URL e o cookie
- **WHEN** o painel chama `POST /auth/federated/corporativo/start` com `{ "retorno": "/connect/authorize?client_id=buteco-painel&…" }`, JSON e a `Origin` do painel
- **THEN** a resposta é `200` com `url` em `https://accounts.google.com/`, e com `Set-Cookie: buteco-fluxo-externo.<nonce>`

#### Scenario: Início de outra origem é recusado
- **WHEN** o mesmo pedido chega com `Origin` de outro site
- **THEN** a resposta é a recusa das barreiras do login, sem cookie e sem URL

#### Scenario: Provedor fora do alcance no início
- **WHEN** o painel chama `POST /auth/federated/corporativo/start` com o provedor recusando a conexão
- **THEN** a resposta é `503` com `application/problem+json` e o `title` "Não foi possível falar com o provedor. Tente de novo em instantes.", sem `Set-Cookie`, e não o `400` em texto do cliente OIDC

#### Scenario: O discovery vem do cache
- **WHEN** o painel começa dois fluxos seguidos com o mesmo provedor
- **THEN** o provedor recebe um só pedido de discovery

#### Scenario: Provedor que não responde
- **WHEN** o provedor não responde ao discovery
- **THEN** o início responde `503` em cerca de 10 segundos, o prazo explícito, e não no das tentativas do cliente OIDC

#### Scenario: Provedor fora do alcance no retorno
- **WHEN** o início passou, e o provedor fica fora do alcance antes da troca do código
- **THEN** o retorno responde `302 /entrar?recusa=falha&provedor=corporativo`, sem cookie de sessão

#### Scenario: Chave desconhecida
- **WHEN** o painel chama `POST /auth/federated/inexistente/start`
- **THEN** a resposta é `404` "Provedor não configurado.", sem `Set-Cookie`

### Requirement: O cookie do fluxo externo
O cookie do fluxo SHALL se chamar `buteco-fluxo-externo.<nonce>` e SHALL ser `HttpOnly`, `SameSite=Lax`, `Path=/auth/federated/callback`, sem `Domain`, `Secure` fora do ambiente `Development`, com vida de 10 minutos, igual à do state token. SHALL carregar só o valor anti-falsificação que o state token também carrega; o nonce, o `code_verifier`, a intenção, o `retorno` e, no conectar, o id e o stamp de quem conecta SHALL ir no state token, cifrado e assinado pelo anel do emissor e de uso único. O retorno SHALL apagar o cookie, aceito ou recusado.

#### Scenario: Atributos do cookie
- **WHEN** o início do fluxo responde
- **THEN** o `Set-Cookie` de `buteco-fluxo-externo.<nonce>` traz `HttpOnly`, `SameSite=Lax`, `Path=/auth/federated/callback` e expiração de 10 minutos, e o valor não contém o `code_verifier` nem o `retorno`

#### Scenario: Retorno sem o cookie do fluxo é recusado
- **WHEN** o retorno chega com código e state válidos e sem o cookie do fluxo
- **THEN** a resposta é o `302` com o motivo `falha`, e nenhum cookie de sessão é gravado

#### Scenario: Cookie de outro fluxo não serve
- **WHEN** o retorno chega com o state do fluxo A e só o cookie do fluxo B do mesmo navegador
- **THEN** a resposta é o `302` com o motivo `falha`, e nenhum cookie de sessão é gravado

#### Scenario: State token usado uma vez só
- **WHEN** o mesmo retorno, aceito uma vez, é repetido com o mesmo código, state e cookie
- **THEN** a segunda resposta é o `302` com o motivo `falha`

#### Scenario: State vencido
- **WHEN** o retorno chega 11 minutos depois do início
- **THEN** a resposta é o `302` com o motivo `falha`

### Requirement: O vínculo pelo par (issuer, sub)
O `apps/api` SHALL guardar o vínculo entre um usuário e uma conta do provedor em `user_federated_identities`, com o issuer, o `sub`, o último e-mail confirmado (só para exibição), o instante da conexão e o do último uso. SHALL haver no máximo um vínculo por (issuer, `sub`) e no máximo um por (usuário, issuer), garantidos por índice único. O login SHALL achar o usuário **só pelo par**, e nunca pelo e-mail. O login aceito SHALL regravar o e-mail e o último uso do vínculo, e SHALL NOT mudar o e-mail do usuário.

#### Scenario: O par decide, e não o e-mail
- **WHEN** o usuário X está vinculado ao `sub` `A`, e o provedor devolve o `sub` `B` com o e-mail de X, verificado
- **THEN** o login não entra como X: o retorno recusa com o motivo `nao-conectada`, e nenhum cookie de sessão é gravado

#### Scenario: O e-mail mudou no provedor
- **WHEN** o usuário X está vinculado ao `sub` `A`, e o provedor devolve o `sub` `A` com outro e-mail
- **THEN** o login entra como X, o e-mail do vínculo passa a ser o novo, e o e-mail de X no Buteco não muda

#### Scenario: Uma conta do provedor liga um usuário só
- **WHEN** se tenta gravar um segundo vínculo com o mesmo issuer e `sub`
- **THEN** a gravação falha por violação de unicidade

### Requirement: Decisão do login no retorno
Depois das conferências do retorno, o `apps/api` SHALL decidir nesta ordem, parando na primeira que casa:

1. `HostedDomain` configurado e claim `hd` ausente ou diferente → recusa `dominio-nao-permitido`;
2. vínculo pelo par com usuário desativado → recusa `desativado`;
3. vínculo pelo par com usuário ativo → **entra**: grava o cookie da sessão pendente (`issuer-user-login`) e responde `302 /entrar?federado=<chave>&segundoPasso=<totp|cadastro>`, com `&retorno=<retorno>` quando o início trouxe um;
4. sem vínculo e `email_verified` diferente de `true` → recusa `email-nao-verificado`;
5. sem vínculo, convidado com o e-mail verificado → recusa `convite-pendente`;
6. sem vínculo, usuário ativo com o e-mail verificado → recusa `nao-conectada`;
7. qualquer outro caso, inclusive o usuário desativado sem vínculo → recusa `sem-acesso`.

Qualquer falha das conferências, inclusive `error=access_denied` do provedor e a registração do state que não existe mais, SHALL ser a recusa `falha`. O e-mail SHALL ser comparado normalizado. O retorno SHALL NOT mandar ao `/connect/authorize`.

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

### Requirement: As recusas não criam sessão
Toda recusa do retorno SHALL responder `302 /entrar?recusa=<motivo>&provedor=<chave>`, com `motivo` em `dominio-nao-permitido`, `desativado`, `email-nao-verificado`, `convite-pendente`, `nao-conectada`, `sem-acesso` ou `falha`. Nenhuma recusa SHALL gravar o cookie `buteco-sessao`, e todas SHALL apagar o cookie do fluxo. Um authorize feito depois de uma recusa SHALL ser tratado como sem sessão. A recusa SHALL NOT contar falha no bloqueio por e-mail. A URL SHALL NOT levar o e-mail.

#### Scenario: Nenhuma recusa grava sessão
- **WHEN** o retorno termina, uma vez em cada um dos sete motivos
- **THEN** cada resposta é o `302` com o motivo, nenhuma traz `Set-Cookie: buteco-sessao`, e um `GET /connect/authorize` seguinte com os cookies do navegador responde `302` para `/entrar`

#### Scenario: A URL não leva o e-mail
- **WHEN** o retorno recusa com o motivo `sem-acesso`
- **THEN** o `Location` não contém o e-mail confirmado

### Requirement: O e-mail confirmado da recusa, por cookie de uso único
As recusas `convite-pendente`, `nao-conectada` e `sem-acesso` SHALL gravar, na mesma resposta, o cookie `buteco-recusa` com `{ motivo, provedor, email }` protegido pelo Data Protection, `HttpOnly`, `SameSite=Strict`, `Path=/auth/federated/refusal`, sem `Domain`, `Secure` fora de `Development`, com `Max-Age` de 120 segundos.

`POST /auth/federated/refusal` SHALL ser anônima (`AuthEntryPoint`), com as barreiras do login. Com o cookie válido e no prazo, SHALL responder `200 { motivo, provedor, email }` e apagar o cookie na mesma resposta. Sem cookie, com cookie vencido, adulterado ou já lido, SHALL responder `404` com o corpo vazio, igual nos quatro casos, e apagar o cookie que houver.

#### Scenario: O painel lê o e-mail uma vez
- **WHEN** o painel chama `POST /auth/federated/refusal` logo depois de uma recusa `sem-acesso`, e chama de novo
- **THEN** a primeira resposta é `200` com `motivo` `sem-acesso`, `provedor` e o e-mail confirmado, com o cookie apagado, e a segunda é `404` vazio

#### Scenario: A rota não revela nada sem o cookie
- **WHEN** a rota é chamada sem cookie, com um cookie adulterado e com um cookie de mais de 120 segundos
- **THEN** as três respostas são `404` com o corpo vazio, iguais entre si

#### Scenario: Atributos do cookie da recusa
- **WHEN** o retorno recusa com o motivo `nao-conectada`
- **THEN** o `Set-Cookie: buteco-recusa` traz `HttpOnly`, `SameSite=Strict`, `Path=/auth/federated/refusal` e `Max-Age=120`, e o valor não traz o e-mail em claro

### Requirement: Logs do login federado sem dado pessoal
Toda recusa SHALL gerar uma linha `Warning` com o `EventId` 1198 (`FederatedLoginRefused`), com a chave do provedor, o motivo e, quando há, o id do usuário; a recusa `falha` SHALL gerar, em vez dela, o `EventId` 1199 (`FederatedLoginFailed`), com o `error` do cliente OIDC e sem o `error_description` do provedor. Conectar e desconectar SHALL gerar `Information` com os `EventId` 1200 (`FederatedIdentityConnected`) e 1201 (`FederatedIdentityDisconnected`), com o id do usuário, a chave e, na desconexão, quem a fez. Nenhuma linha de log do fluxo SHALL conter o e-mail do provedor, o e-mail do usuário, o `sub`, o código, o state, o id token nem o segredo.

#### Scenario: O log não leva e-mail nem sub
- **WHEN** o fluxo é percorrido uma vez em cada desfecho (entrada, as sete recusas, conectar, desconectar), com o log capturado
- **THEN** nenhuma linha capturada contém o e-mail confirmado, o e-mail do usuário nem o `sub` devolvido pelo provedor, e cada recusa tem a sua linha com o motivo

### Requirement: Conectar o provedor em Minha conta
`POST /account/federated/{chave}/connect` SHALL atender todo papel, só o usuário real do token (`CurrentUser`), com a confirmação de quem chama (`user-access-links`: `senha`, ou `codigo` do app para quem não tem senha). Confirmada, SHALL responder `200 { url }` e gravar o cookie do fluxo, com a intenção `conectar`, o id e o stamp de quem chama no state token. Chave fora da configuração SHALL responder `404`.

O retorno do conectar SHALL, parando na primeira que casa:
1. recusar com `dominio-nao-permitido` (a mesma regra do login);
2. recusar com `sessao-expirada` se o usuário não está mais ativo ou o stamp mudou;
3. recusar com `em-uso` se o par já é de outro usuário;
4. recusar com `ja-conectado` se a pessoa já tem vínculo nesse issuer;
5. gravar o vínculo e responder `302 /account?conectado=<chave>`.

As recusas SHALL responder `302 /account?recusa=<motivo>&provedor=<chave>`, sem gravar nada, e a recusa `falha` vale como no login. O conectar SHALL NOT exigir que o e-mail do provedor seja o do usuário, SHALL NOT trocar o stamp e SHALL NOT revogar sessões.

#### Scenario: Conectar grava o vínculo
- **WHEN** um usuário com senha confirma com a senha certa, vai ao provedor e volta com uma conta sem vínculo
- **THEN** o retorno responde `302 /account?conectado=corporativo`, o vínculo existe com o issuer e o `sub`, o stamp não muda, e as sessões dele continuam vivas

#### Scenario: Conectar sem a confirmação
- **WHEN** o mesmo usuário chama o conectar sem `senha`
- **THEN** a resposta é `401` "Senha inválida.", contada no bloqueio do e-mail dele, sem URL e sem cookie

#### Scenario: Conta do provedor de outro usuário
- **WHEN** o retorno do conectar traz um par já vinculado a outro usuário
- **THEN** a resposta é `302 /account?recusa=em-uso&provedor=corporativo`, e nenhum vínculo muda

#### Scenario: Stamp trocado no meio
- **WHEN** a senha do usuário é trocada entre o início do conectar e o retorno
- **THEN** a resposta é `302 /account?recusa=sessao-expirada&provedor=corporativo`, e nenhum vínculo é gravado

### Requirement: A última forma de entrada não pode ser removida
As formas de entrada de um usuário SHALL ser a senha gravada e cada vínculo com um provedor presente na configuração. Desconectar SHALL responder `409` com `title` "A última forma de entrada não pode ser removida." quando o usuário ficaria sem nenhuma, sem apagar nada. A conferência e a remoção SHALL rodar na mesma transação, com a linha do usuário travada (`SELECT … FOR UPDATE`): duas desconexões simultâneas do mesmo usuário SHALL NOT deixá-lo sem forma de entrada.

#### Scenario: Só o provedor não sai
- **WHEN** um usuário sem senha, com um vínculo só, pede a desconexão
- **THEN** a resposta é `409`, e o vínculo continua

#### Scenario: Com senha, o provedor sai
- **WHEN** um usuário com senha e um vínculo pede a desconexão
- **THEN** a resposta é `204`, e o vínculo some

#### Scenario: Vínculo de provedor fora da configuração não conta
- **WHEN** um usuário sem senha tem um vínculo com um provedor disponível e outro com um issuer que saiu da configuração, e pede a desconexão do disponível
- **THEN** a resposta é `409`

#### Scenario: A conferência trava a linha do usuário
- **WHEN** um usuário com senha e vínculo pede a desconexão
- **THEN** a consulta emitida pede o `SELECT … FOR UPDATE` da linha do usuário antes do `DELETE` do vínculo, na mesma transação (na M3, com um provedor por issuer, quem não tem senha tem no máximo uma forma disponível, e a corrida de duas desconexões não se monta; a trava é a defesa para a M4)

### Requirement: Desconectar pela própria pessoa
`DELETE /account/federated/{chave}` SHALL atender todo papel, só o usuário real do token. SHALL apagar o vínculo do usuário no issuer do provedor e responder `204`, sujeita à regra da última forma de entrada. Sem vínculo, SHALL responder `404`. SHALL NOT pedir senha, SHALL NOT trocar o stamp e SHALL NOT revogar sessões.

#### Scenario: Desconectar a si mesmo mantém as sessões
- **WHEN** um usuário com senha desconecta o próprio provedor
- **THEN** a resposta é `204`, o stamp dele não muda, e a sessão dele continua renovando

### Requirement: Desconectar pelo Admin
`DELETE /users/{id}/federated/{chave}` SHALL atender só o Admin, pelo papel gravado. SHALL apagar o vínculo do usuário indicado no issuer do provedor e, **na mesma transação**, trocar o `security_stamp` dele e revogar as sessões dele (`user-account`), e responder `204`, sujeita à regra da última forma de entrada. A si mesmo SHALL responder `403` com `title` "Para desconectar a sua própria conta, use Minha conta.". Usuário inexistente ou sem vínculo SHALL responder `404`. SHALL NOT pedir senha. O Admin SHALL NOT poder conectar a conta de outro usuário.

#### Scenario: Admin desconecta e as sessões caem
- **WHEN** um Admin desconecta o provedor de um usuário com senha e uma sessão viva
- **THEN** a resposta é `204`, o vínculo some, o stamp do usuário muda, e o refresh dele recebe `invalid_grant` na renovação seguinte

#### Scenario: Admin não desconecta a si mesmo por aqui
- **WHEN** um Admin chama a rota com o próprio id
- **THEN** a resposta é `403`, e o vínculo continua

#### Scenario: Operador não desconecta ninguém
- **WHEN** um Operador chama a rota
- **THEN** a resposta é `403`, e o vínculo continua

### Requirement: Vínculo de provedor que saiu da configuração
O vínculo cujo issuer nenhum provedor configurado tem SHALL continuar gravado, SHALL aparecer em `contasConectadas` com `disponivel` falso, SHALL NOT entrar no `formasDeEntrada` e SHALL NOT contar para a última forma de entrada. Ele SHALL voltar a valer quando um provedor com o mesmo issuer voltar à configuração.

#### Scenario: Provedor removido
- **WHEN** o `apps/api` reinicia sem o provedor de um usuário vinculado
- **THEN** `GET /users/{id}` traz o vínculo em `contasConectadas` com `disponivel` falso, e `formasDeEntrada` sem `google`

