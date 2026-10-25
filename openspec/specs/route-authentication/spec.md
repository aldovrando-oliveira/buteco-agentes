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
negócio da rota.

#### Scenario: apps/api rejeita requisição sem token
- **WHEN** um cliente envia `GET /agents` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized`

#### Scenario: apps/api rejeita requisição com token inválido
- **WHEN** um cliente envia `GET /agents` com `Authorization: Bearer
  token-invalido`
- **THEN** a API responde `401 Unauthorized`

#### Scenario: apps/inbox rejeita requisição sem token
- **WHEN** um cliente envia `GET /channels` sem o header `Authorization`
- **THEN** a API responde `401 Unauthorized`

#### Scenario: apps/inbox rejeita requisição com token inválido
- **WHEN** um cliente envia `GET /channels` com `Authorization: Bearer
  token-invalido`
- **THEN** a API responde `401 Unauthorized`

### Requirement: Rotas classificadas como anônimas permanecem acessíveis sem token
`apps/api` e `apps/inbox` SHALL manter acessível, sem nenhum header
`Authorization`, toda rota explicitamente classificada como anônima —
tanto as rotas pré-existentes a esta mudança (sondas de saúde, webhook de
canal, `AgentCard`, push notifications), que continuam com o mesmo
comportamento de antes, quanto a rota nova introduzida por esta mudança
(`POST /auth/login`), que é anônima por definição — ela é o próprio ponto
de entrada de autenticação, não pode exigir o que ainda não emitiu.

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

#### Scenario: Login é acessível sem token
- **WHEN** um cliente envia `POST /auth/login` com credenciais válidas,
  sem o header `Authorization`
- **THEN** a API responde `200 OK` com um token, sem rejeitar a
  requisição por ausência do header `Authorization`

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

### Requirement: Token de operador emitido por apps/api é aceito por apps/inbox sem chamada de rede entre os processos
`apps/inbox` SHALL aceitar, em suas rotas autenticadas, um token de
operador emitido por `apps/api`, validando-o localmente (mesma chave de
assinatura compartilhada) sem realizar nenhuma chamada de rede a
`apps/api` para validar esse token. Isso SHALL valer também com a validação
do token do emissor ligada (`Identity:Issuer` configurado) e nenhuma chave do
emissor obtida: o token HMAC SHALL NOT depender das chaves do emissor.

#### Scenario: Token emitido por apps/api é aceito por apps/inbox
- **WHEN** um operador realiza login com sucesso em `apps/api`
  (`POST /auth/login`) e usa o token retornado para acessar uma rota
  autenticada de `apps/inbox` (ex. `GET /channels`), mesmo com
  `apps/inbox` configurado sem conseguir alcançar `apps/api` pela rede
  nesse instante
- **THEN** `apps/inbox` responde de acordo com a rota (`200 OK`), sem
  `401 Unauthorized`

#### Scenario: Token HMAC aceito com o emissor ligado e sem chaves
- **WHEN** `apps/inbox` sobe com `Identity:Issuer` configurado e o `apps/api`
  inalcançável desde o boot, e o operador usa o token HMAC em `GET /channels`
- **THEN** `apps/inbox` responde `200 OK`, e nenhuma requisição foi feita ao
  discovery nem ao JWKS por causa dessa chamada

### Requirement: Subject desconhecido é recusado em apps/api
`apps/api` SHALL autorizar, entre os tokens válidos, apenas o subject `operator`
em todas as rotas e cada subject de serviço conhecido (`service:inbox`,
`service:connectors`) nas rotas da sua lista. Um token válido com qualquer outro
subject SHALL receber `403 Forbidden` em toda rota que exige autenticação.

#### Scenario: Subject desconhecido recusado em rota do operador
- **WHEN** uma requisição `GET /knowledge-bases` chega com token validamente
  assinado e não expirado com `sub` `service:desconhecido`
- **THEN** a API responde `403 Forbidden`

#### Scenario: Operador continua com acesso total
- **WHEN** o operador, autenticado pelo login, chama `GET /knowledge-bases` e
  `GET /sync/knowledge-bases`
- **THEN** a API responde `200` nas duas

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
`operator` nas rotas que exigem autenticação. Um token validamente assinado e não
expirado com qualquer outro subject, inclusive `service:inbox` e
`service:connectors`, SHALL receber `403 Forbidden` em toda rota que exige
autenticação, sem executar nenhuma lógica de negócio da rota. Token ausente,
malformado, com assinatura inválida ou expirado SHALL continuar recebendo
`401 Unauthorized`.

#### Scenario: Operador continua com acesso às rotas do operador
- **WHEN** o operador, autenticado pelo login de `apps/api`, chama
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
Os endpoints do emissor (`/.well-known/openid-configuration`, `/.well-known/jwks` e `/connect/token`) SHALL ser anônimos e SHALL ser conferidos no boot por uma checagem própria. Eles não passam pelo roteamento do ASP.NET Core: o OpenIddict os atende dentro do middleware de autenticação, e por isso a checagem de rotas anônimas existente não os enxerga.

O `apps/api` SHALL manter uma lista esperada desses endpoints, cada um com motivo (`PublicDiscovery` para discovery e JWKS, `AuthEntryPoint` para o token). Com o emissor ligado, o boot SHALL comparar essa lista, nos dois sentidos, com as URIs de endpoint que a composição real configura no servidor OIDC, e SHALL falhar se houver:

- endpoint configurado fora da lista;
- item da lista sem endpoint configurado;
- endpoint configurado que coincide com uma rota mapeada do app.

A checagem SHALL NOT consultar o banco.

#### Scenario: Endpoint configurado fora da lista derruba o boot
- **WHEN** a composição habilita um endpoint do servidor OIDC que não está na lista esperada (por exemplo, o de introspecção)
- **THEN** o boot falha com uma mensagem que cita a URI do endpoint

#### Scenario: Item da lista sem endpoint derruba o boot
- **WHEN** a lista esperada contém `/connect/token` e a composição não configura endpoint de token
- **THEN** o boot falha com uma mensagem que cita `/connect/token`

#### Scenario: Composição real passa na checagem
- **WHEN** o app sobe com a composição do `Program.cs` e o emissor ligado
- **THEN** o boot conclui, e remover a chamada da checagem do `Program.cs` reprova o teste que monta a composição real com um endpoint a mais

#### Scenario: Rotas existentes não mudam
- **WHEN** o emissor está ligado
- **THEN** `/health`, `/auth/login` e `/agents/{id:guid}/.well-known/agent-card.json` continuam as únicas rotas mapeadas classificadas como anônimas, e toda outra rota mapeada continua exigindo token HMAC válido
