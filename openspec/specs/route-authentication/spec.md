# route-authentication Specification

## Purpose

TBD - defined by change auth-login-e-servico. Update Purpose after archive.
## Requirements
### Requirement: Rotas de negócio exigem token válido por padrão
`apps/api` e `apps/inbox` SHALL exigir um token válido (`Authorization:
Bearer <token>`) em toda rota HTTP, exceto as explicitamente
classificadas como anônimas. Uma requisição sem o header `Authorization`
ou com um token malformado, com assinatura inválida, ou expirado SHALL
ser rejeitada com `401 Unauthorized`, sem executar nenhuma lógica de
negócio da rota. O `401` SHALL trazer o cabeçalho `WWW-Authenticate: Bearer`.

No `apps/api`, a requisição sem token a uma rota inexistente SHALL receber a
mesma resposta que a requisição sem token a uma rota que existe: `401`, corpo
vazio e `WWW-Authenticate: Bearer`, para não revelar quais rotas existem. Com
token válido, a rota inexistente SHALL responder `404`.

#### Scenario: apps/api rejeita requisição sem token
- **WHEN** um cliente envia `GET /agents` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized` com `WWW-Authenticate: Bearer`

#### Scenario: apps/api rejeita requisição com token inválido
- **WHEN** um cliente envia `GET /agents` com `Authorization: Bearer
  token-invalido`
- **THEN** a API responde `401 Unauthorized`

#### Scenario: Rota inexistente sem token responde como a que existe
- **WHEN** um cliente envia `GET /rota-que-nao-existe` e `GET /agents`, os dois sem
  o header `Authorization`
- **THEN** as duas respostas são `401 Unauthorized`, com corpo vazio e
  `WWW-Authenticate: Bearer`

#### Scenario: apps/inbox rejeita requisição sem token
- **WHEN** um cliente envia `GET /channels` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized` com `WWW-Authenticate: Bearer`

#### Scenario: apps/inbox rejeita requisição com token inválido
- **WHEN** um cliente envia `GET /channels` com `Authorization: Bearer
  token-invalido`
- **THEN** a API responde `401 Unauthorized`

### Requirement: Rotas classificadas como anônimas permanecem acessíveis sem token
`apps/api` e `apps/inbox` SHALL manter acessível, sem nenhum header
`Authorization`, toda rota explicitamente classificada como anônima: as sondas
de saúde, o webhook de canal, o `AgentCard`, as push notifications e os pontos de
entrada do emissor (`POST /auth/session`, `POST /auth/session/second-factor`,
`/connect/authorize` e `/connect/logout`), que são anônimos por definição, porque não podem exigir o que
ainda não emitiram. `POST /auth/login` SHALL NOT existir.

#### Scenario: Descoberta do AgentCard continua anônima
- **WHEN** um cliente envia `GET /agents/{id}/.well-known/agent-card.json`
  para um agente existente, sem o header `Authorization`
- **THEN** a API responde `200 OK` com o `AgentCard`, sem exigir token

#### Scenario: Webhook de canal continua anônimo
- **WHEN** um canal externo envia `POST /webhooks/{channelId}` para um
  canal existente, sem o header `Authorization`
- **THEN** `apps/inbox` processa a requisição normalmente, sem responder
  `401 Unauthorized`

#### Scenario: Sonda de saúde de apps/api continua anônima
- **WHEN** um cliente envia `GET /health` para `apps/api`, sem o header
  `Authorization`
- **THEN** a API responde `200 OK`, sem exigir token

#### Scenario: Sonda de saúde de apps/inbox continua anônima
- **WHEN** um cliente envia `GET /health` para `apps/inbox`, sem o header
  `Authorization`
- **THEN** a API responde `200 OK`, sem exigir token

#### Scenario: Abrir a sessão do emissor é acessível sem token
- **WHEN** um cliente envia `POST /auth/session` com credenciais válidas, da
  origem do painel e sem o header `Authorization`
- **THEN** a API responde `204 No Content`, sem rejeitar a requisição por
  ausência do header `Authorization`

#### Scenario: O segundo passo do login é acessível sem token
- **WHEN** um cliente envia `POST /auth/session/second-factor` com o cookie da
  sessão pendente, um código válido, da origem do painel e sem o header
  `Authorization`
- **THEN** a API responde `204 No Content`, sem rejeitar a requisição por
  ausência do header `Authorization`

#### Scenario: As rotas de 2 etapas da própria pessoa não são anônimas
- **WHEN** um cliente envia `GET /account/two-factor` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized`

#### Scenario: A rota de login antiga não é anônima, porque não existe
- **WHEN** um cliente envia `POST /auth/login`, sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized`, como a qualquer rota inexistente
  sem token, e não processa credencial nenhuma

#### Scenario: Push notification continua anônimo para o middleware novo
- **WHEN** uma requisição `POST /internal/push-notifications` é enviada
  sem o header `Authorization`, mas com um `X-A2A-Notification-Token`
  válido para uma `PendingDispatch` existente
- **THEN** `apps/inbox` processa a requisição normalmente (`200 OK`), sem
  rejeitá-la por ausência do header `Authorization`

### Requirement: Checagem de integridade de classificação de rotas no startup
`apps/api` e `apps/inbox` SHALL, no startup, verificar que toda rota HTTP
mapeada está classificada — como autenticada (padrão) ou como anônima com
um motivo documentado. O processo SHALL falhar ao subir (lançando uma
exceção antes de aceitar requisições) se existir uma rota marcada como
anônima sem motivo documentado, ou se existir um motivo documentado no
código que não corresponda a nenhuma rota de fato mapeada.

#### Scenario: Startup íntegro sobe normalmente
- **WHEN** `apps/api` (ou `apps/inbox`) inicia com toda rota anônima
  mapeada tendo um motivo documentado, e todo motivo documentado
  correspondendo a uma rota de fato mapeada
- **THEN** o processo conclui o startup e passa a aceitar requisições,
  sem lançar exceção

#### Scenario: Rota anônima sem motivo documentado derruba o startup
- **WHEN** existe uma rota mapeada como anônima sem um motivo
  documentado associado a ela
- **THEN** o processo lança uma exceção durante o startup, identificando
  a rota sem classificação, e não chega a aceitar requisições

#### Scenario: Motivo documentado sem rota correspondente derruba o startup
- **WHEN** existe um motivo de rota anônima documentado no código que não
  corresponde a nenhuma rota de fato mapeada no host
- **THEN** o processo lança uma exceção durante o startup, identificando
  a entrada sem rota correspondente, e não chega a aceitar requisições

### Requirement: Subject desconhecido é recusado em apps/api
`apps/api` SHALL autorizar, entre os tokens válidos, apenas o subject `operator` (o token do painel), nas rotas em cuja entrada da tabela de papéis esteja o `role` dele (`route-roles`), cada subject de serviço conhecido (`service:inbox`, `service:connectors`) nas rotas da sua lista, e o subject `application` da aplicação externa nas rotas da lista dele (`route-roles`: só `POST /agents/{id}/a2a`, com a checagem do vínculo de `a2a-task-lifecycle`). O `operator` SHALL NOT ter acesso implícito a todas as rotas. Um token válido com qualquer outro subject SHALL receber `403 Forbidden` em toda rota que exige autenticação.

#### Scenario: Subject desconhecido recusado em rota do painel
- **WHEN** uma requisição `GET /knowledge-bases` chega com token validamente
  assinado e não expirado com `sub` `service:desconhecido`
- **THEN** a API responde `403 Forbidden`

#### Scenario: O painel não passa nas rotas de sincronização
- **WHEN** um usuário Admin, autenticado pelo login, chama `GET /knowledge-bases` e
  `GET /sync/knowledge-bases`
- **THEN** a API responde `200` na primeira e `403` na segunda

#### Scenario: Token de aplicação externa recusado em rota do painel
- **WHEN** uma requisição `GET /knowledge-bases` chega com o token de uma aplicação externa ativa
- **THEN** a API responde `403 Forbidden`

### Requirement: Lista de rotas dos subjects de serviço conferida no startup
`apps/api` SHALL verificar, no startup, que cada par de método e padrão de rota
das listas dos subjects de serviço corresponde a um endpoint mapeado, e SHALL
falhar o boot quando uma entrada não tiver rota correspondente.

#### Scenario: Entrada sem rota derruba a checagem
- **WHEN** a checagem roda com uma lista que contém um padrão inexistente entre os
  endpoints mapeados
- **THEN** ela falha nomeando o subject, o método e o padrão sem rota

#### Scenario: Composição real sobe
- **WHEN** `apps/api` sobe com as listas de produção
- **THEN** a checagem passa e a aplicação responde

### Requirement: Subject diferente de operator é recusado em apps/inbox
`apps/inbox` SHALL autorizar, entre os tokens válidos, apenas o subject
`operator` nas rotas que exigem autenticação, e só nas rotas em cuja entrada da tabela de papéis esteja o `role` do token (`route-roles`). Um token validamente assinado e não
expirado com qualquer outro subject, inclusive `service:inbox` e
`service:connectors`, SHALL receber `403 Forbidden` em toda rota que exige
autenticação, sem executar nenhuma lógica de negócio da rota. Token ausente,
malformado, com assinatura inválida, expirado ou do painel sem `role` SHALL continuar recebendo
`401 Unauthorized`.

#### Scenario: Operador continua com acesso às rotas do operador
- **WHEN** um usuário de papel `operator`, autenticado pelo login de `apps/api`, chama
  `GET /channels` em `apps/inbox`
- **THEN** `apps/inbox` responde `200 OK`

#### Scenario: Token de serviço do apps/connectors recusado
- **WHEN** uma requisição `GET /channels` chega a `apps/inbox` com token
  validamente assinado e não expirado com `sub` `service:connectors`
- **THEN** `apps/inbox` responde `403 Forbidden`

#### Scenario: Token de serviço do próprio apps/inbox recusado
- **WHEN** uma requisição `GET /channels` chega a `apps/inbox` com token
  validamente assinado e não expirado com `sub` `service:inbox`
- **THEN** `apps/inbox` responde `403 Forbidden`

#### Scenario: Subject desconhecido recusado
- **WHEN** uma requisição `GET /channels` chega a `apps/inbox` com token
  validamente assinado e não expirado com `sub` `service:desconhecido`
- **THEN** `apps/inbox` responde `403 Forbidden`

#### Scenario: Recusa vale para toda rota autenticada mapeada
- **WHEN** cada rota autenticada mapeada no host de `apps/inbox` recebe uma
  requisição com token validamente assinado com `sub` `service:connectors`
- **THEN** todas respondem `403 Forbidden`

#### Scenario: Escrita recusada não altera o estado
- **WHEN** uma requisição `POST /channels` com corpo válido chega a `apps/inbox`
  com token validamente assinado com `sub` `service:connectors`
- **THEN** `apps/inbox` responde `403 Forbidden` e nenhum canal é criado

### Requirement: Rotas anônimas de apps/inbox não mudam com token de serviço presente
`apps/inbox` SHALL continuar processando as rotas classificadas como anônimas
(`/health`, `/webhooks/{channelId:guid}`, `/internal/push-notifications`) com a
mesma autenticação própria de antes, com ou sem token, e SHALL NOT responder
`403 Forbidden` nelas por causa do subject de um token presente na requisição.

#### Scenario: Webhook com token de serviço é processado pela rota
- **WHEN** uma requisição `POST /webhooks/{channelId}` para um canal inexistente
  chega com token validamente assinado com `sub` `service:connectors`
- **THEN** `apps/inbox` responde `404 Not Found`, decidido pela rota, e não `403`

#### Scenario: Sonda de saúde com token de serviço
- **WHEN** uma requisição `GET /health` chega com token validamente assinado com
  `sub` `service:inbox`
- **THEN** `apps/inbox` responde `200 OK`

### Requirement: Endpoints do emissor OIDC são anônimos, classificados e conferidos no boot
Os endpoints do emissor SHALL ser anônimos e SHALL ser conferidos no boot por uma checagem própria:

- `/.well-known/openid-configuration` e `/.well-known/jwks`;
- `/connect/token`;
- `/connect/authorize` e `/connect/logout`.

Eles são atendidos pelo OpenIddict dentro do middleware de autenticação, e por isso a checagem de rotas anônimas existente não os enxerga. O authorize e o logout funcionam em modo passthrough: depois de o OpenIddict validar o pedido, uma rota mapeada do app decide a resposta.

O `apps/api` SHALL manter uma lista esperada desses endpoints, cada um com:

- **motivo:** `PublicDiscovery` para discovery e JWKS, `AuthEntryPoint` para o token e o authorize, `SessionEnd` para o logout;
- **marcação de passthrough.**

O boot SHALL comparar essa lista, nos dois sentidos, com as URIs de endpoint que a composição real configura no servidor OIDC, e SHALL falhar se houver:

- endpoint configurado fora da lista;
- item da lista sem endpoint configurado;
- endpoint **sem** passthrough que coincide com uma rota mapeada do app;
- endpoint **com** passthrough sem rota mapeada de mesmo caminho, anônima e classificada com o mesmo motivo.

A checagem SHALL NOT consultar o banco.

#### Scenario: Endpoint configurado fora da lista derruba o boot
- **WHEN** a composição habilita um endpoint do servidor OIDC que não está na lista esperada (por exemplo, o de introspecção)
- **THEN** o boot falha com uma mensagem que cita a URI do endpoint

#### Scenario: Item da lista sem endpoint derruba o boot
- **WHEN** a lista esperada contém `/connect/token` e a composição não configura endpoint de token
- **THEN** o boot falha com uma mensagem que cita `/connect/token`

#### Scenario: Passthrough sem rota derruba o boot
- **WHEN** a composição configura `/connect/authorize` em passthrough e a rota `GET /connect/authorize` não está mapeada
- **THEN** o boot falha com uma mensagem que cita `/connect/authorize`

#### Scenario: Composição real passa na checagem
- **WHEN** o app sobe com a composição do `Program.cs`
- **THEN** o boot conclui, e remover a chamada da checagem do `Program.cs` reprova o teste que monta a composição real com um endpoint a mais

#### Scenario: Rotas anônimas do app são as conhecidas
- **WHEN** o app sobe
- **THEN** as rotas mapeadas classificadas como anônimas são exatamente:
  - `/health`;
  - `/auth/session`;
  - `/connect/authorize`;
  - `/connect/logout`;
  - `/agents/{id:guid}/.well-known/agent-card.json`.

  Toda outra rota mapeada continua exigindo token válido.

#### Scenario: Allowlist com rota que não existe derruba o boot
- **WHEN** a allowlist de rotas anônimas contém um caminho que nenhuma rota mapeada tem
- **THEN** o boot falha com uma mensagem que cita esse caminho

### Requirement: apps/api aceita só o access token do emissor
O `apps/api` SHALL autenticar cada requisição com um esquema só, o do access token do próprio emissor, e SHALL aceitar o token que satisfaça todas as condições abaixo, recusando com `401` o que falhar qualquer uma, inclusive o que não seja um JWS compacto:

- assinatura RS256 verificável por uma chave do anel de chaves do próprio processo, sem chamada HTTP;
- `typ` `at+jwt`;
- `iss` igual ao issuer;
- `aud` contendo `buteco-api`;
- `exp` não vencido, com tolerância de no máximo 30 segundos.

O subject usado pela autorização SHALL vir de uma tabela fechada `client_id` → subject, com as entradas:

- `buteco-painel` → `operator`;
- `buteco-servico-inbox` → `service:inbox`;
- `buteco-servico-connectors` → `service:connectors`.

O `sub` do token SHALL NOT ser usado como subject. Token válido com `client_id` ausente ou fora da tabela SHALL receber `403`. O subject mapeado de um cliente de serviço SHALL seguir a lista de rotas do subject na tabela de subjects do `apps/api`. O boot SHALL falhar se um subject de destino da tabela não existir na tabela de subjects do `apps/api`.

#### Scenario: Token do painel aceito no apps/api
- **WHEN** um access token do fluxo do `buteco-painel`, com `aud` contendo `buteco-api`, chama `GET /agents`
- **THEN** a resposta é `200 OK`

#### Scenario: Token sem a audience do apps/api é recusado
- **WHEN** um access token do emissor com `aud` só `buteco-inbox` chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`

#### Scenario: sub operator forjado não vira operador
- **WHEN** um access token do emissor válido, com `aud` `buteco-api`, `sub` `operator` e `client_id` de um cliente fora da tabela, chama `GET /agents`
- **THEN** a resposta é `403 Forbidden`

#### Scenario: Token no formato de dois segmentos é recusado
- **WHEN** um token `payload.assinatura`, com um ponto só, assinado com a chave simétrica que os apps usavam antes, chama `GET /agents`
- **THEN** a resposta é `401 Unauthorized`, com `WWW-Authenticate: Bearer`

#### Scenario: Cliente apontando para subject inexistente derruba a checagem
- **WHEN** a checagem da tabela de clientes roda com uma entrada cujo subject não está na tabela de subjects do `apps/api`
- **THEN** ela falha nomeando o `client_id` e o subject

#### Scenario: Token do cliente de serviço do inbox só nas rotas do service:inbox
- **WHEN** um access token `client_credentials` do `buteco-servico-inbox` chama `GET /agents/{id}` de um agente existente, `GET /agents` e `GET /sync/knowledge-bases/`
- **THEN** as respostas são `200 OK`, `403 Forbidden` e `403 Forbidden`

#### Scenario: Token do cliente de serviço do connectors só nas rotas do service:connectors
- **WHEN** um access token `client_credentials` do `buteco-servico-connectors` chama `GET /sync/knowledge-bases/` e `GET /agents`
- **THEN** as respostas são `200 OK` e `403 Forbidden`

