## MODIFIED Requirements

### Requirement: Token anexado pelo handler de saída, e 401 do destino sem repetição
O handler de saída de cada serviço SHALL anexar `Authorization: Bearer <token de serviço>`, o access token do emissor, a toda chamada ao destino. Quando o destino responder `401`, o handler SHALL descartar o token do cache, se o anexado ainda for o do cache, e SHALL devolver a resposta sem repetir a chamada.

#### Scenario: 401 descarta o cache sem repetir
- **WHEN** o destino responde `401` a uma chamada
- **THEN** o destino recebeu essa chamada uma única vez, e a chamada seguinte pede um token novo

#### Scenario: 401 atrasado não derruba o token novo
- **WHEN** um `401` chega para um token que já foi trocado no cache
- **THEN** o token novo continua no cache
