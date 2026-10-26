## ADDED Requirements

### Requirement: apps/inbox e apps/connectors aceitam o access token do emissor com a própria aud
Com `Identity:Issuer` configurado, o `apps/inbox` e o `apps/connectors` SHALL aceitar, nas rotas autenticadas, um access token JWT do emissor do `apps/api` que satisfaça todas as condições:

- assinatura RS256 verificável por uma chave do JWKS do emissor, escolhida pelo `kid` do cabeçalho;
- cabeçalho com `typ` `at+jwt`;
- `iss` igual a `Identity:Issuer` normalizado com barra final;
- `aud` contendo a audience do app: `buteco-inbox` no `apps/inbox`, `buteco-connectors` no `apps/connectors`;
- `exp` presente e não vencido, com tolerância de relógio de no máximo 30 segundos.

Token que falhe qualquer condição SHALL receber `401 Unauthorized`, inclusive o que traga só a audience de outro app.

#### Scenario: Token do painel aceito no apps/inbox
- **WHEN** um token do emissor com `aud` `buteco-inbox` e `client_id` `buteco-painel` chama `GET /channels` no `apps/inbox`
- **THEN** a resposta é `200 OK`

#### Scenario: Token do painel aceito no apps/connectors
- **WHEN** um token do emissor com `aud` `buteco-connectors` e `client_id` `buteco-painel` chama `GET /connectors/providers` no `apps/connectors`
- **THEN** a resposta é `200 OK`

#### Scenario: Token com as três audiences vale em cada app
- **WHEN** um token do emissor com `aud` [`buteco-api`, `buteco-inbox`, `buteco-connectors`] e `client_id` `buteco-painel` chama `GET /channels` no `apps/inbox` e `GET /connectors/providers` no `apps/connectors`
- **THEN** as duas respostas são `200 OK`

#### Scenario: Token emitido para outro app é recusado
- **WHEN** um token do emissor com `aud` só `buteco-connectors` chama `GET /channels` no `apps/inbox`
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: Issuer errado é recusado
- **WHEN** um token assinado por uma chave publicada no JWKS, com `iss` diferente de `Identity:Issuer`, chama uma rota autenticada
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: Token expirado é recusado
- **WHEN** um token do emissor com `exp` mais de 30 segundos no passado chama uma rota autenticada
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: Algoritmo ou tipo diferentes são recusados
- **WHEN** um token com `alg` diferente de `RS256`, inclusive `HS256` assinado com a `Auth:TokenSigningKey` e `none`, ou com `typ` diferente de `at+jwt`, chama uma rota autenticada
- **THEN** a resposta é `401 Unauthorized`

### Requirement: O esquema é escolhido pela forma do token, e um formato nunca cai no outro
Cada requisição com `Authorization: Bearer` SHALL ser autenticada por exatamente um esquema, escolhido antes de qualquer validação: token com exatamente três segmentos separados por ponto pelo esquema do emissor; qualquer outro, e a requisição sem token, pelo esquema HMAC. O esquema HMAC SHALL continuar aceitando e recusando tokens exatamente como antes desta capability. Um token recusado por um esquema SHALL NOT ser tentado no outro.

#### Scenario: Token HMAC do operador continua aceito
- **WHEN** o operador faz login no `apps/api` e chama `GET /channels` no `apps/inbox` e `GET /connectors/providers` no `apps/connectors` com o token HMAC
- **THEN** as duas respostas são `200 OK`

#### Scenario: Token HMAC não toca nas chaves do emissor
- **WHEN** tokens HMAC, válidos e inválidos, chamam rotas autenticadas de um app cujo emissor nunca respondeu
- **THEN** as respostas são as de antes (`200` ou `401`) e nenhuma requisição foi feita ao discovery nem ao JWKS

#### Scenario: JWT inválido não passa pelo HMAC
- **WHEN** um token de três segmentos com assinatura inválida chama uma rota autenticada
- **THEN** a resposta é `401 Unauthorized` e o validador HMAC não foi chamado

### Requirement: O principal do emissor é mapeado pelo cliente, nunca pelo sub
O subject usado pela autorização de um token do emissor SHALL vir de uma tabela fechada `client_id` → subject de cada app, com a entrada `buteco-painel` → `operator` no `apps/inbox` e no `apps/connectors`. O `sub` do token SHALL NOT ser usado como subject. Token do emissor válido cujo `client_id` esteja ausente ou fora da tabela SHALL receber `403 Forbidden` em toda rota autenticada. O subject mapeado SHALL seguir as mesmas regras de rota que o subject tem com o token HMAC.

#### Scenario: Cliente fora da tabela é recusado
- **WHEN** um token do emissor válido, com a `aud` do app e `client_id` `cliente-desconhecido`, chama uma rota autenticada
- **THEN** a resposta é `403 Forbidden`

#### Scenario: sub operator forjado não vira operador
- **WHEN** um token do emissor válido, com a `aud` do app, `sub` `operator` e `client_id` `cliente-desconhecido`, chama `GET /channels` no `apps/inbox`
- **THEN** a resposta é `403 Forbidden`

#### Scenario: Token sem client_id é recusado
- **WHEN** um token do emissor válido, com a `aud` do app e sem o claim `client_id`, chama uma rota autenticada
- **THEN** a resposta é `403 Forbidden`

#### Scenario: Subject mapeado segue a tabela de rotas do apps/connectors
- **WHEN** um token do emissor com `aud` `buteco-connectors` e `client_id` `buteco-painel` chama `GET /connectors/providers/google-drive/folder?id=x`
- **THEN** a resposta é `403 Forbidden`, como para o token HMAC `operator`

### Requirement: Chaves obtidas só pelo endereço interno, com o issuer conferido
As chaves do emissor SHALL ser obtidas pelo endereço interno `Api:BaseUrl`, lendo `/.well-known/openid-configuration` e `/.well-known/jwks` sob ele, e SHALL NOT ser obtidas pelo `jwks_uri` anunciado no discovery. Quando o `issuer` do discovery for diferente de `Identity:Issuer`, a obtenção SHALL falhar e registrar no log um erro que contenha os dois valores.

#### Scenario: JWKS lido pelo endereço interno
- **WHEN** o discovery do emissor anuncia `jwks_uri` sob o issuer público e o app obtém as chaves
- **THEN** as requisições feitas são `GET {Api:BaseUrl}/.well-known/openid-configuration` e `GET {Api:BaseUrl}/.well-known/jwks`, e nenhuma vai ao endereço público

#### Scenario: Issuer divergente impede a obtenção
- **WHEN** o discovery lido pelo endereço interno declara um `issuer` diferente de `Identity:Issuer`
- **THEN** nenhuma chave é obtida, um token do emissor recebe `503`, e o log contém um erro com os dois valores

### Requirement: O app sobe e atende sem o apps/api, e as chaves têm ciclo de vida definido
O `apps/inbox` e o `apps/connectors` SHALL subir sem nenhuma chamada ao `apps/api` no boot. Depois do start, SHALL tentar obter as chaves em segundo plano, repetindo a cada 10 segundos até obter, e depois SHALL renová-las de modo que, com o `apps/api` respondendo, as chaves usadas nunca tenham mais de cerca de 1h15 de idade. Entre duas buscas que falharam SHALL haver pelo menos 10 segundos, inclusive as disparadas por requisição.

Enquanto nenhuma chave tiver sido obtida, um token do emissor SHALL receber `503 Service Unavailable` com `Retry-After`. Depois de obtidas, uma falha de renovação SHALL manter as chaves anteriores em uso.

Um token cujo `kid` não esteja nas chaves em cache SHALL receber `401` e SHALL disparar uma nova busca, limitada a no máximo uma por minuto por instância; se a busca trouxer a chave, os tokens seguintes com aquele `kid` SHALL ser aceitos sem reinício.

#### Scenario: Sobe com o apps/api fora do ar
- **WHEN** o app sobe com o discovery e o JWKS inalcançáveis
- **THEN** `GET /health` responde `200 OK`, o token HMAC do operador recebe `200 OK` numa rota autenticada, e um token do emissor recebe `503` com `Retry-After`

#### Scenario: Recupera sem reiniciar quando o apps/api volta
- **WHEN** o emissor passa a responder depois do start, no mesmo processo
- **THEN** em até a próxima tentativa em segundo plano, um token do emissor válido recebe `200 OK`

#### Scenario: apps/api fora depois de obtidas as chaves
- **WHEN** as chaves já foram obtidas e o discovery e o JWKS passam a falhar
- **THEN** um token do emissor válido continua recebendo `200 OK`

#### Scenario: Rajada com o apps/api fora não multiplica buscas
- **WHEN** vinte tokens do emissor chegam em menos de 10 segundos, com o emissor inalcançável e nenhuma chave obtida
- **THEN** todos recebem `503`, e foi feita no máximo uma busca ao discovery nesse intervalo

#### Scenario: Chave nova fora do cache
- **WHEN** o emissor publica uma chave nova e um token assinado por ela chega antes de o cache ser renovado
- **THEN** esse token recebe `401`, uma busca é disparada, e o mesmo token enviado depois da busca recebe `200 OK`

#### Scenario: kid inventado não vira rajada de buscas
- **WHEN** dois tokens com `kid`s que o emissor nunca publicou chegam dentro de um minuto
- **THEN** os dois recebem `401`, e foi feita no máximo uma busca ao JWKS por causa deles

#### Scenario: Chave rotacionada
- **WHEN** o emissor publica a chave nova ao lado da anterior e depois retira a anterior, com o cache renovado em cada passo
- **THEN** os tokens das duas chaves são aceitos enquanto as duas estão publicadas, e o token da anterior recebe `401` depois de ela sair

### Requirement: Configuração do emissor conferida no boot
Com `Identity:Issuer` ausente ou vazio, o app SHALL subir com a validação do token do emissor desligada, todo token SHALL ser autenticado pelo esquema HMAC, e o boot SHALL registrar um aviso que cite `Identity:Issuer`. Com `Identity:Issuer` presente, o boot SHALL falhar com mensagem que cite a configuração quando:

- `Identity:Issuer` não for uma url absoluta `http` ou `https` sem query nem fragmento;
- `Api:BaseUrl` estiver ausente ou vazio.

O boot SHALL falhar também quando um subject de destino da tabela de clientes não existir na tabela de subjects do app. As checagens SHALL rodar sobre o host construído, sem chamada de rede.

#### Scenario: Sem Identity:Issuer a validação fica desligada
- **WHEN** o app sobe sem `Identity:Issuer` e recebe um token do emissor válido e um token HMAC do operador
- **THEN** o token do emissor recebe `401`, o HMAC recebe `200 OK`, e o log de boot contém o aviso citando `Identity:Issuer`

#### Scenario: Issuer sem esquema derruba o boot
- **WHEN** o app sobe com `Identity:Issuer` igual a `issuer.exemplo.test`
- **THEN** o boot falha com uma mensagem que cita `Identity:Issuer`

#### Scenario: Issuer sem endereço interno derruba o boot
- **WHEN** o `apps/connectors` sobe com `Identity:Issuer` configurado e `Api:BaseUrl` vazio
- **THEN** o boot falha com uma mensagem que cita `Identity:Issuer` e `Api:BaseUrl`

#### Scenario: Cliente apontando para subject inexistente derruba a checagem
- **WHEN** a checagem da tabela de clientes roda com uma entrada cujo subject não está na tabela de subjects do app
- **THEN** ela falha nomeando o `client_id` e o subject

### Requirement: Acordo com o emissor real
O token emitido pelo `apps/api` real, para um cliente `buteco-painel` com escopo cujo recurso é a audience do app, SHALL ser aceito pelo app, com as chaves obtidas do `apps/api` pelo endereço interno, e SHALL continuar aceito depois que o `apps/api` deixar de responder.

#### Scenario: Token do apps/api real aceito pelo apps/inbox
- **WHEN** o `apps/api` com o emissor ligado emite um token por `client_credentials` para `buteco-painel` com recurso `buteco-inbox`, e o token chama `GET /channels` no `apps/inbox`
- **THEN** a resposta é `200 OK`, e repetida depois de o `apps/api` ficar inalcançável continua `200 OK`

#### Scenario: Token do apps/api real aceito pelo apps/connectors, e recusado com a aud do outro
- **WHEN** o `apps/api` emite um token para `buteco-painel` com recurso `buteco-connectors` e outro só com recurso `buteco-inbox`, e os dois chamam `GET /connectors/providers`
- **THEN** o primeiro recebe `200 OK` e o segundo `401 Unauthorized`
