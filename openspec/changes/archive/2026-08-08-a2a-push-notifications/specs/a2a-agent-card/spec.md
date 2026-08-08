## MODIFIED Requirements

### Requirement: Capabilities do card refletem funcionalidade real
O sistema SHALL declarar `Capabilities.Streaming` como `false` e
`Capabilities.PushNotifications` como `true` em todo `AgentCard` retornado.

#### Scenario: Streaming continua false, push notifications passa a true
- **WHEN** o card de qualquer agente é consultado
- **THEN** `Capabilities.Streaming` é `false` e `Capabilities.PushNotifications`
  é `true` no corpo da resposta
