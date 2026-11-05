## ADDED Requirements

### Requirement: GET /identity responde ao usuário ativo e recusa quem não é pessoa
O `apps/api` SHALL expor `GET /identity`. A rota SHALL responder `200` só a um usuário real e ativo, identificado pela `CurrentUser` (o `sub` do access token do emissor é um usuário existente e ativo). SHALL responder `401` sem token. SHALL responder `403` ao token de serviço (`service:inbox`, `service:connectors`), ao token do painel cujo `sub` não é usuário e ao usuário desativado com access token ainda no prazo.

#### Scenario: Usuário ativo lê a identidade
- **WHEN** um usuário ativo, com o access token do fluxo real do emissor, chama `GET /identity`
- **THEN** a resposta é `200` com os objetos `emissor` e `chavesDeAssinatura`

#### Scenario: Sem token
- **WHEN** `GET /identity` chega sem `Authorization`
- **THEN** a resposta é `401`

#### Scenario: Token de serviço é recusado
- **WHEN** `GET /identity` chega com o token de `service:inbox` ou de `service:connectors`
- **THEN** a resposta é `403`

#### Scenario: Token do painel sem usuário é recusado
- **WHEN** `GET /identity` chega com um token do `buteco-painel` cujo `sub` não é id de usuário
- **THEN** a resposta é `403` com o título da `CurrentUser`

#### Scenario: Usuário desativado é recusado
- **WHEN** um usuário é desativado e chama `GET /identity` com o access token emitido antes, ainda no prazo
- **THEN** a resposta é `403`

### Requirement: O emissor sai com os endereços que o discovery anuncia
O objeto `emissor` SHALL trazer `issuer`, `discovery`, `jwks` e `token` como URIs absolutas. Cada valor SHALL ser igual, byte a byte, ao que o discovery do mesmo host anuncia em `issuer`, ao endereço do próprio documento de discovery, a `jwks_uri` e a `token_endpoint`. Os valores SHALL vir do issuer configurado e nunca do `Host` nem do esquema da requisição. A rota SHALL NOT ter estado "emissor desligado": sem url pública o app não sobe.

#### Scenario: Endereços iguais aos do discovery
- **WHEN** o usuário ativo chama `GET /identity` e o discovery é buscado no mesmo host
- **THEN** `emissor.issuer` = `issuer`, `emissor.jwks` = `jwks_uri` e `emissor.token` = `token_endpoint`, e `emissor.discovery` = o issuer seguido de `.well-known/openid-configuration`

#### Scenario: Outro Host na requisição não muda os endereços
- **WHEN** `GET /identity` chega com `Host` diferente da url pública
- **THEN** os quatro endereços ficam sob o issuer configurado, e o texto da resposta não contém o host da requisição

### Requirement: As durações saem das mesmas constantes que o emissor usa
A resposta SHALL trazer, em segundos inteiros:

- `emissor.validadeDoTokenSegundos`: a duração do access token;
- `emissor.sessaoSemAtividadeSegundos`: a duração do refresh token, que é deslizante;
- `chavesDeAssinatura.intervaloDeTrocaSegundos`, `prePublicacaoSegundos`, `retencaoSegundos` e `verificacaoSegundos`: o intervalo de troca, a pré-publicação, a retenção e o intervalo da verificação de rotação.

As durações de access token, refresh token, id token e código de autorização SHALL ser definidas uma vez só, em `IdentityTokenLifetimes`, e lidas de lá pelo registro do OpenIddict, pelo gerador do token de serviço do `apps/api` e pela rota. As da rotação SHALL ser lidas da `IdentityKeyRotationPolicy`. Nenhuma duração SHALL mudar de valor nesta change.

#### Scenario: Durações do token iguais às opções do OpenIddict
- **WHEN** o usuário ativo chama `GET /identity`
- **THEN** `validadeDoTokenSegundos` é igual ao `AccessTokenLifetime` e `sessaoSemAtividadeSegundos` é igual ao `RefreshTokenLifetime` das `OpenIddictServerOptions` do host, ambos em segundos

#### Scenario: Durações da rotação iguais às janelas gravadas
- **WHEN** o usuário ativo chama `GET /identity`
- **THEN** para a chave `em-uso`, `assinaAte − assinaDesde` = `intervaloDeTrocaSegundos` e `saiDaPublicacaoEm − assinaAte` = `retencaoSegundos`

#### Scenario: Intervalo da verificação igual ao da rotação
- **WHEN** o usuário ativo chama `GET /identity`
- **THEN** `verificacaoSegundos` é igual ao `CheckInterval` do `IdentityKeyRotationSchedule` registrado no host, que é o intervalo do timer da rotação

#### Scenario: A pré-publicação é o limiar que cria a próxima chave
- **WHEN** a política planeja com a chave ativa a um segundo abaixo de `prePublicacaoSegundos` do fim, e depois a um segundo acima
- **THEN** no primeiro caso cria a próxima chave, e no segundo não cria

#### Scenario: Id token e código registrados com as constantes
- **WHEN** o host sobe
- **THEN** as `OpenIddictServerOptions` têm `IdentityTokenLifetime` = `IdentityTokenLifetimes.IdentityToken` (20 min) e `AuthorizationCodeLifetime` = `IdentityTokenLifetimes.AuthorizationCode` (5 min)

#### Scenario: O token de serviço do apps/api dura o mesmo que o access token
- **WHEN** o `apps/api` gera o token que manda ao `apps/connectors`
- **THEN** `exp − iat` é igual ao `AccessTokenLifetime` das opções do host

### Requirement: As chaves de assinatura saem com estado e datas da política de rotação
`chavesDeAssinatura.chaves` SHALL listar as chaves de uso `signing` que ainda validam (`validates_until` depois do agora). Cada uma SHALL trazer exatamente `kid`, `estado`, `criadaEm`, `assinaDesde`, `assinaAte` e `saiDaPublicacaoEm`, com as datas iguais às colunas da chave. O estado SHALL sair da `IdentityKeyRotationPolicy`, no instante da requisição:

- `em-uso`: assina agora e, entre as que assinam, é a de maior início, a mesma que o anel escolhe;
- `proxima`: começa a assinar depois do agora;
- `aposentando`: não assina mais, ou assina mas não é a escolhida, e ainda valida.

A chave de cifra e a chave que já não valida SHALL ficar fora. A ordem SHALL ser `em-uso`, `proxima`, `aposentando`, e dentro do mesmo estado `assinaDesde` decrescente.

#### Scenario: Banco recém-subido tem uma chave em uso
- **WHEN** o app sobe contra um banco sem chave e o usuário ativo chama `GET /identity`
- **THEN** `chaves` tem um item, com `estado` `em-uso` e `criadaEm` igual à criação gravada

#### Scenario: Próxima e aposentando ao lado da em uso
- **WHEN** há, além da chave em uso, uma que começa a assinar no futuro e uma que parou de assinar e ainda valida
- **THEN** `chaves` traz `em-uso`, `proxima` e `aposentando`, nessa ordem, com `assinaDesde`, `assinaAte` e `saiDaPublicacaoEm` iguais a `signs_from`, `signs_until` e `validates_until` de cada uma

#### Scenario: Bordas das janelas
- **WHEN** o agora é exatamente o `signs_from` de uma chave, exatamente o `signs_until` de outra e exatamente o `validates_until` de uma terceira
- **THEN** a primeira é `em-uso`, a segunda é `aposentando` e a terceira não aparece

#### Scenario: Chave de cifra e chave vencida ficam fora
- **WHEN** o banco tem a chave de cifra e uma chave de assinatura com `validates_until` no passado
- **THEN** nenhuma das duas aparece em `chaves`

#### Scenario: Em uso é a que o anel escolhe
- **WHEN** a política classifica um conjunto de chaves
- **THEN** a `em-uso` é a mesma chave que `IdentityKeyRingSnapshot.Build` escolhe para assinar no mesmo instante

### Requirement: A resposta nunca traz material de chave
A resposta SHALL NOT conter material de chave, nem privado nem público: nenhum parâmetro de JWK (`d`, `p`, `q`, `dp`, `dq`, `qi`, `n`, `e`, `k`, `x5c`), nenhum certificado e nenhum valor da coluna de material cifrado. A leitura das chaves SHALL ser feita pelo `IdentityKeyRing`, por projeção que não lê a coluna de material.

#### Scenario: Nenhum parâmetro de chave no JSON
- **WHEN** o usuário ativo chama `GET /identity`
- **THEN** o texto do JSON não tem nenhuma propriedade `d`, `p`, `q`, `dp`, `dq`, `qi`, `n`, `e`, `k` nem `x5c`, e as propriedades de cada chave são exatamente as seis do contrato

#### Scenario: Nenhum material cifrado no JSON
- **WHEN** o usuário ativo chama `GET /identity` com três chaves de assinatura no banco
- **THEN** o texto da resposta não contém o material cifrado de nenhuma linha de `identity_keys`, nem em base64 nem em hexadecimal

### Requirement: Nomes do fio fixos
Os nomes das propriedades da resposta SHALL ser exatamente `emissor`, `issuer`, `discovery`, `jwks`, `token`, `validadeDoTokenSegundos`, `sessaoSemAtividadeSegundos`, `chavesDeAssinatura`, `intervaloDeTrocaSegundos`, `prePublicacaoSegundos`, `retencaoSegundos`, `verificacaoSegundos`, `chaves`, `kid`, `estado`, `criadaEm`, `assinaDesde`, `assinaAte` e `saiDaPublicacaoEm`. Os estados SHALL ser exatamente `em-uso`, `proxima` e `aposentando`.

#### Scenario: Nomes conferidos no texto
- **WHEN** o usuário ativo chama `GET /identity`
- **THEN** o texto do JSON tem exatamente esses nomes, e nenhum outro, nos dois níveis
