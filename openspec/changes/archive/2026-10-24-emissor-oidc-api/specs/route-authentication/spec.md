## ADDED Requirements

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
