## MODIFIED Requirements

### Requirement: AgentCard declara o esquema de segurança do endpoint A2A
O sistema SHALL fazer o `AgentCard` retornado por
`GET /agents/{id}/.well-known/agent-card.json` declarar, em
`SecuritySchemes` e `SecurityRequirements`, que o endpoint A2A do agente
(`SupportedInterfaces[].Url`) exige o access token do emissor do Buteco,
obtido pelo fluxo OAuth2 `client_credentials`, mesmo o próprio `AgentCard`
permanecendo acessível sem token (rota de descoberta pública). O esquema
SHALL se chamar `oauth2` e SHALL ser um `OAuth2SecurityScheme` com
`Flows.ClientCredentials.TokenUrl` igual a `{issuer}connect/token`,
`Flows.ClientCredentials.Scopes` com só `buteco-api`, e `OAuth2MetadataUrl`
igual a `{issuer}.well-known/openid-configuration`, a mesma
declaração em todo agente. O `{issuer}` é o do emissor do `apps/api`: a
`PublicUrl:BaseUrl` com uma barra final, que o boot exige (`oidc-issuer`,
"Url pública obrigatória"). O card SHALL NOT dizer quais aplicações têm acesso
ao agente.

#### Scenario: Card inclui o esquema OAuth2 do Buteco
- **WHEN** um cliente consulta `GET
  /agents/{id}/.well-known/agent-card.json` para um agente existente, sem token
- **THEN** a resposta é `200`, e o `AgentCard` inclui, em `SecuritySchemes`,
  só a entrada `oauth2`, com o fluxo `clientCredentials`, o `tokenUrl` público
  do emissor e o escopo `buteco-api`

#### Scenario: Card referencia o esquema em SecurityRequirements
- **WHEN** um cliente consulta `GET
  /agents/{id}/.well-known/agent-card.json` para um agente existente
- **THEN** o `AgentCard` retornado inclui, em `SecurityRequirements`,
  uma entrada que referencia `oauth2` com o escopo `buteco-api`
