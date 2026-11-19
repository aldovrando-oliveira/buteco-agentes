## MODIFIED Requirements

### Requirement: Decisão do login no retorno
Depois das conferências do retorno, o `apps/api` SHALL decidir nesta ordem, parando na primeira que casa:

1. `HostedDomain` configurado e claim `hd` ausente ou diferente → recusa `dominio-nao-permitido`;
2. vínculo pelo par com usuário desativado → recusa `desativado`;
3. vínculo pelo par com usuário ativo → **entra**: grava o cookie da sessão pendente (`issuer-user-login`) e responde `302 /entrar?federado=<chave>&segundoPasso=<totp|cadastro>`, com `&retorno=<retorno>` quando o início trouxe um;
4. sem vínculo e `email_verified` diferente de `true` → recusa `email-nao-verificado`;
5. sem vínculo, convidado com o e-mail verificado → recusa `convite-pendente`;
6. sem vínculo, usuário ativo com o e-mail verificado → recusa `nao-conectada`;
7. qualquer outro caso, inclusive o usuário desativado sem vínculo → recusa `sem-acesso`.

Qualquer falha das conferências, inclusive `error=access_denied` do provedor e a registração do state que não existe mais, SHALL ser a recusa `falha`. A registração do state que não existe mais SHALL ser recusada pelo cliente OIDC **antes** de resolver a registração, e SHALL NOT virar exceção nem `500`. Ela acontece quando a chave do provedor é trocada entre o início e o retorno **e quando o `apps/api` volta sem nenhum provedor configurado**: nos dois, o cliente OIDC SHALL conferir o state e restaurar as propriedades do Buteco antes de recusar, e o retorno SHALL NOT recusar sem ler o state.

A recusa `falha` SHALL ir a um de três destinos fixos, escolhido só pela intenção do state token, e a nenhum outro:

| intenção do state | destino |
|---|---|
| `login` | `/entrar?recusa=falha` |
| `conectar` | `/account?recusa=falha` |
| `convite` | `/convite?recusa=falha` |
| desconhecida | `/entrar?recusa=falha` |

com `&provedor=<chave>` do state quando a chave é conhecida. A intenção é **desconhecida** quando o cliente OIDC recusa o state antes de restaurar as propriedades: o state ausente, desconhecido ou adulterado, vencido, ou já usado. Nesses casos a resposta SHALL ser `302 /entrar?recusa=falha`, sem `provedor`, qualquer que tenha sido a intenção do início; nenhum outro dado do pedido, do cookie do fluxo ou da sessão SHALL escolher o destino. Nenhum valor do pedido SHALL entrar no caminho do destino, e a chave SHALL ir escapada.

Em toda recusa `falha`, nenhum cookie `buteco-sessao` SHALL ser gravado, nenhum vínculo SHALL ser gravado, nenhum cookie novo SHALL ser gravado, o cookie do fluxo SHALL ser apagado, e o convite SHALL continuar como estava. Sem nenhum provedor configurado, a linha `1199` SHALL levar a chave do state e o erro `sem_provedor_configurado`. O e-mail SHALL ser comparado normalizado. O retorno SHALL NOT mandar ao `/connect/authorize`.

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
- **WHEN** o login começou com o único provedor `corporativo`, e o retorno, com o state e o cookie do fluxo, chega depois de o `apps/api` reiniciar sem nenhum provedor, no mesmo banco
- **THEN** a resposta é `302 /entrar?recusa=falha&provedor=corporativo`, e não `500`, sem cookie de sessão, com o log `1199`, a chave `corporativo` e o erro `sem_provedor_configurado`

#### Scenario: Nenhum provedor configurado no retorno do conectar
- **WHEN** o conectar começou com o único provedor `corporativo`, e o retorno, com o state e o cookie do fluxo, chega depois de o `apps/api` reiniciar sem nenhum provedor, no mesmo banco
- **THEN** a resposta é `302 /account?recusa=falha&provedor=corporativo`, e não `/entrar` nem `500`, sem cookie de sessão, sem vínculo gravado e sem cookie novo, com o cookie do fluxo apagado e o state resgatado

#### Scenario: Nenhum provedor configurado no retorno do aceite
- **WHEN** o aceite começou com o único provedor `corporativo`, e o retorno, com o state e o cookie do fluxo, chega depois de o `apps/api` reiniciar sem nenhum provedor, no mesmo banco
- **THEN** a resposta é `302 /convite?recusa=falha&provedor=corporativo`, sem cookie de sessão e sem vínculo, e o convite continua pendente

#### Scenario: O state ausente cai no login
- **WHEN** o retorno chega sem o parâmetro `state`, com ou sem provedor configurado, e com o cookie do fluxo de um conectar começado
- **THEN** a resposta é `302 /entrar?recusa=falha`, sem `provedor`, sem cookie de sessão e sem vínculo

#### Scenario: O state forjado cai no login
- **WHEN** o retorno chega com um `state` que o Buteco não emitiu, com ou sem provedor configurado, e com o cookie do fluxo de um conectar começado
- **THEN** a resposta é `302 /entrar?recusa=falha`, sem `provedor`, sem cookie de sessão e sem vínculo

#### Scenario: O state vencido cai no login
- **WHEN** um conectar começa e o retorno chega com o state dele depois do prazo de 10 minutos
- **THEN** a resposta é `302 /entrar?recusa=falha`, sem `provedor`, sem cookie de sessão e sem vínculo, porque o state vencido não se lê

#### Scenario: O state já usado cai no login
- **WHEN** um conectar termina, e a mesma URL de retorno, com o mesmo state, é aberta outra vez
- **THEN** a segunda resposta é `302 /entrar?recusa=falha`, sem `provedor`, sem cookie de sessão, e o vínculo da primeira volta não muda

#### Scenario: O destino da falha não sai do pedido
- **WHEN** o retorno de um conectar recusado chega com parâmetros a mais na query, como `retorno=https://outro.example/` ou `retorno=//outro.example`
- **THEN** o `Location` é exatamente um dos três destinos fixos, relativo ao painel, começando por `/` e não por `//`

### Requirement: Conectar o provedor em Minha conta
`POST /account/federated/{chave}/connect` SHALL atender todo papel, só o usuário real do token (`CurrentUser`), com a confirmação de quem chama (`user-access-links`: `senha`, ou `codigo` do app para quem não tem senha). Confirmada, SHALL responder `200 { url }` e gravar o cookie do fluxo, com a intenção `conectar`, o id e o stamp de quem chama no state token. Chave fora da configuração SHALL responder `404`.

O retorno do conectar SHALL, parando na primeira que casa:
1. recusar com `dominio-nao-permitido` (a mesma regra do login);
2. recusar com `sessao-expirada` se o usuário não está mais ativo ou o stamp mudou;
3. recusar com `em-uso` se o par já é de outro usuário;
4. recusar com `ja-conectado` se a pessoa já tem vínculo nesse issuer;
5. gravar o vínculo e responder `302 /account?conectado=<chave>`.

As recusas SHALL responder `302 /account?recusa=<motivo>&provedor=<chave>`, sem gravar nada, e a recusa `falha` vale como no login ("Decisão do login no retorno"): com o state lido, ela volta a `/account`, **inclusive quando o `apps/api` volta sem nenhum provedor configurado**; com a intenção desconhecida, vai ao `/entrar`. O conectar SHALL NOT exigir que o e-mail do provedor seja o do usuário, SHALL NOT trocar o stamp e SHALL NOT revogar sessões.

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

#### Scenario: A falha do conectar sem provedor volta a Minha conta
- **WHEN** um usuário logado no painel começa o conectar com o único provedor, o `apps/api` reinicia sem nenhum provedor, e o navegador volta do provedor
- **THEN** a resposta é `302 /account?recusa=falha&provedor=corporativo`, o painel continua logado, e nenhum vínculo é gravado

### Requirement: As recusas do aceite não criam sessão e voltam ao convite
Toda recusa do retorno com a intenção `convite` SHALL responder `302 /convite?recusa=<motivo>&provedor=<chave>`, com `motivo` em `outro-email`, `email-nao-verificado`, `convite-invalido`, `em-uso`, `dominio-nao-permitido` ou `falha`. Nenhuma recusa SHALL gravar o cookie `buteco-sessao`, gravar vínculo ou mudar o usuário, e todas SHALL apagar o cookie do fluxo. A URL SHALL NOT levar o token, o hash do link nem e-mail.

A recusa `falha` SHALL valer como no login: qualquer falha das conferências do cliente OIDC, inclusive `error=access_denied` (a pessoa cancelou no provedor) e o provedor que saiu da configuração entre o início e o retorno, seja pela chave trocada, seja pelo `apps/api` sem nenhum provedor. Com o state lido, a resposta SHALL ser `/convite?recusa=falha&provedor=<chave>`. Quando a intenção não se lê do state token (o state ausente, forjado, vencido ou já usado), a resposta SHALL ser a do login, `302 /entrar?recusa=falha`, sem sessão.

#### Scenario: Nenhuma recusa do aceite grava sessão
- **WHEN** o retorno do aceite termina, uma vez em cada um dos seis motivos
- **THEN** cada resposta é o `302` para `/convite` com o motivo, nenhuma traz `Set-Cookie: buteco-sessao`, nenhum vínculo é gravado, e um `GET /connect/authorize` seguinte com os cookies do navegador responde `302` para `/entrar`

#### Scenario: A pessoa cancelou no provedor
- **WHEN** o provedor volta com `error=access_denied` e o state do aceite
- **THEN** a resposta é `302 /convite?recusa=falha&provedor=corporativo`, sem cookie de sessão, e o convite continua aceito por senha

#### Scenario: O provedor saiu da configuração no meio
- **WHEN** o aceite começa com o provedor `corporativo`, e o retorno chega depois de a chave ser trocada, ou de o `apps/api` reiniciar sem nenhum provedor
- **THEN** a resposta, nos dois casos, é `302 /convite?recusa=falha&provedor=corporativo`, sem cookie de sessão e sem vínculo, e o convite continua pendente

#### Scenario: A URL da recusa não leva o token nem o e-mail
- **WHEN** o retorno do aceite recusa com o motivo `outro-email`
- **THEN** o `Location` não contém o token, o hash do link, o e-mail do convite nem o e-mail confirmado
