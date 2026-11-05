## MODIFIED Requirements

### Requirement: apps/inbox e apps/connectors aceitam o access token do emissor com a própria aud
O `apps/inbox` e o `apps/connectors` SHALL aceitar, nas rotas autenticadas, só o access token JWT do emissor do `apps/api`, e só o que satisfaça todas as condições:

- assinatura RS256 verificável por uma chave do JWKS do emissor, escolhida pelo `kid` do cabeçalho;
- cabeçalho com `typ` `at+jwt`;
- `iss` igual a `Identity:Issuer` normalizado com barra final;
- `aud` contendo a audience do app: `buteco-inbox` no `apps/inbox`, `buteco-connectors` no `apps/connectors`;
- `exp` presente e não vencido, com tolerância de relógio de no máximo 30 segundos.

Token que falhe qualquer condição SHALL receber `401 Unauthorized`, inclusive o que traga só a audience de outro app e o que não seja um JWS compacto. O `401` SHALL trazer o cabeçalho `WWW-Authenticate: Bearer`.

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

#### Scenario: Algoritmo diferente de RS256 é recusado, mesmo com a chave publicada
- **WHEN** um token assinado com `RS512` pela chave RSA publicada no JWKS, com todas as outras condições satisfeitas, chama uma rota autenticada
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: Token sem assinatura ou de outro tipo é recusado
- **WHEN** um token com `alg` `none`, ou com `typ` diferente de `at+jwt`, chama uma rota autenticada
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: Token no formato de dois segmentos é recusado
- **WHEN** um token `payload.assinatura`, com um ponto só, assinado com a chave simétrica que os apps usavam antes, chama `GET /channels` no `apps/inbox` e `GET /connectors/providers` no `apps/connectors`
- **THEN** as duas respostas são `401 Unauthorized`, com `WWW-Authenticate: Bearer`

### Requirement: O principal do emissor é mapeado pelo cliente, nunca pelo sub
O subject usado pela autorização de um token do emissor SHALL vir de uma tabela fechada `client_id` → subject de cada app:

- no `apps/inbox`, a entrada `buteco-painel` → `operator`;
- no `apps/connectors`, as entradas `buteco-painel` → `operator` e `buteco-servico-api` → `service:api`.

O `sub` do token SHALL NOT ser usado como subject. Token do emissor válido cujo `client_id` esteja ausente ou fora da tabela SHALL receber `403 Forbidden` em toda rota autenticada. O subject mapeado SHALL seguir as regras de rota da tabela de subjects do app.

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
- **THEN** a resposta é `403 Forbidden`, porque o `operator` não tem a rota de descrever pasta

#### Scenario: Cliente de serviço do apps/api só na rota de descrever pasta
- **WHEN** um token do emissor com `aud` `buteco-connectors` e `client_id` `buteco-servico-api` chama `GET /connectors/providers/google-drive/folder?id=x` e `GET /connectors/providers`
- **THEN** a primeira não recebe `403` nem `401`, e a segunda recebe `403 Forbidden`

#### Scenario: Cliente de serviço do apps/api sem a audience do connectors
- **WHEN** um token do emissor com `aud` `buteco-api` e `client_id` `buteco-servico-api` chama `GET /connectors/providers/google-drive/folder?id=x`
- **THEN** a resposta é `401 Unauthorized`

### Requirement: O app sobe e atende sem o apps/api, e as chaves têm ciclo de vida definido
O `apps/inbox` e o `apps/connectors` SHALL subir sem nenhuma chamada ao `apps/api` no boot. Depois do start, SHALL tentar obter as chaves em segundo plano, repetindo a cada 10 segundos até obter, e depois SHALL renová-las de modo que, com o `apps/api` respondendo, as chaves usadas nunca tenham mais de cerca de 1h15 de idade. Entre duas buscas que falharam SHALL haver pelo menos 10 segundos, inclusive as disparadas por requisição.

Enquanto nenhuma chave tiver sido obtida, uma requisição que traga o cabeçalho `Authorization` numa rota autenticada SHALL receber `503 Service Unavailable` com `Retry-After`. A requisição sem o cabeçalho `Authorization` SHALL receber `401 Unauthorized` com `WWW-Authenticate: Bearer`, com ou sem chaves obtidas. Depois de obtidas, uma falha de renovação SHALL manter as chaves anteriores em uso.

Um token cujo `kid` não esteja nas chaves em cache SHALL receber `401` e SHALL disparar uma nova busca, limitada a no máximo uma por minuto por instância; se a busca trouxer a chave, os tokens seguintes com aquele `kid` SHALL ser aceitos sem reinício.

#### Scenario: Sobe com o apps/api fora do ar
- **WHEN** o app sobe com o discovery e o JWKS inalcançáveis
- **THEN** `GET /health` responde `200 OK`, e um token do emissor numa rota autenticada recebe `503` com `Retry-After`

#### Scenario: Sem token antes das chaves
- **WHEN** o app sobe com o discovery e o JWKS inalcançáveis, e uma requisição sem `Authorization` chama uma rota autenticada
- **THEN** a resposta é `401 Unauthorized` com `WWW-Authenticate: Bearer`, e não `503`

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
O boot do `apps/inbox` e do `apps/connectors` SHALL falhar, com mensagem que cite a configuração, quando:

- `Identity:Issuer` estiver ausente ou vazio;
- `Identity:Issuer` não for uma url absoluta `http` ou `https` sem query nem fragmento;
- `Api:BaseUrl` estiver ausente ou vazio.

O boot SHALL falhar também quando um subject de destino da tabela de clientes não existir na tabela de subjects do app. As checagens SHALL rodar sobre o host construído, sem chamada de rede, e SHALL NOT ecoar o valor do `Api:BaseUrl`.

#### Scenario: Sem Identity:Issuer o boot falha
- **WHEN** o app sobe sem `Identity:Issuer`
- **THEN** o boot falha com uma mensagem que cita `Identity:Issuer`

#### Scenario: Issuer sem esquema derruba o boot
- **WHEN** o app sobe com `Identity:Issuer` igual a `issuer.exemplo.test`
- **THEN** o boot falha com uma mensagem que cita `Identity:Issuer`

#### Scenario: Issuer sem endereço interno derruba o boot
- **WHEN** o `apps/connectors` sobe com `Identity:Issuer` configurado e `Api:BaseUrl` vazio
- **THEN** o boot falha com uma mensagem que cita `Identity:Issuer` e `Api:BaseUrl`

#### Scenario: Cliente apontando para subject inexistente derruba a checagem
- **WHEN** a checagem da tabela de clientes roda com uma entrada cujo subject não está na tabela de subjects do app
- **THEN** ela falha nomeando o `client_id` e o subject

## REMOVED Requirements

### Requirement: O esquema é escolhido pela forma do token, e um formato nunca cai no outro
**Reason**: A #182 remove o token assinado com a chave compartilhada. Sobra um esquema só, o do emissor, e não há forma a escolher nem outro formato em que cair.
**Migration**: Nenhuma para os clientes: desde a #235 nenhum serviço manda o token antigo, e desde a #217 nenhuma rota o emite. O token antigo passa a receber `401` ("Token no formato de dois segmentos é recusado").
