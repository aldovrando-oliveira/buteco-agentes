## ADDED Requirements

### Requirement: AgentCard declara o esquema de segurança do endpoint A2A
O sistema SHALL fazer o `AgentCard` retornado por
`GET /agents/{id}/.well-known/agent-card.json` declarar, em
`SecuritySchemes` e `SecurityRequirements`, que o endpoint A2A do agente
(`SupportedInterfaces[].Url`) exige autenticação HTTP Bearer, mesmo o
próprio `AgentCard` permanecendo acessível sem token (rota de descoberta
pública).

#### Scenario: Card inclui o esquema de segurança Bearer
- **WHEN** um cliente consulta `GET
  /agents/{id}/.well-known/agent-card.json` para um agente existente
- **THEN** o `AgentCard` retornado inclui, em `SecuritySchemes`, um
  esquema HTTP com `Scheme: "Bearer"`

#### Scenario: Card referencia o esquema em SecurityRequirements
- **WHEN** um cliente consulta `GET
  /agents/{id}/.well-known/agent-card.json` para um agente existente
- **THEN** o `AgentCard` retornado inclui, em `SecurityRequirements`,
  uma entrada que referencia o esquema declarado em `SecuritySchemes`
