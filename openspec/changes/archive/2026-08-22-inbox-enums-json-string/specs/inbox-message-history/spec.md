## MODIFIED Requirements

### Requirement: Consulta cronológica de mensagens de uma sessão
O sistema SHALL permitir, via `apps/inbox`, listar todas as mensagens (de
entrada e de saída) de uma `Session` específica, em ordem cronológica
pelo instante de cada mensagem. Os campos `Direction`, `ContentType`,
`DeliveryStatus` e `DispatchStatus` SHALL ser serializados como string
com o nome do valor do enum (ex. `"Failed"`), nunca como o inteiro
ordinal subjacente.

#### Scenario: Lista de mensagens de uma sessão com histórico
- **WHEN** um cliente consulta as mensagens de uma `Session` existente que
  já tem mensagens de entrada e/ou saída persistidas
- **THEN** a resposta traz todas as mensagens dessa `Session`, ordenadas
  cronologicamente pelo instante de cada uma, incluindo direção, conteúdo,
  tipo de conteúdo e, quando aplicável, status de entrega ou status de
  dispatch

#### Scenario: Sessão sem nenhuma mensagem retorna lista vazia
- **WHEN** um cliente consulta as mensagens de uma `Session` existente que
  ainda não tem nenhuma mensagem persistida
- **THEN** a resposta traz uma lista vazia, sem erro

#### Scenario: Consulta de mensagens de sessão inexistente retorna 404
- **WHEN** um cliente consulta as mensagens de um id de `Session` que não
  existe
- **THEN** a resposta é HTTP 404

#### Scenario: Enums de mensagem de entrada serializados como string
- **WHEN** um cliente consulta as mensagens de uma `Session` que inclui
  uma mensagem de entrada com `Direction: Inbound`, `ContentType: Text` e
  `DispatchStatus: Failed`
- **THEN** os campos `Direction`, `ContentType` e `DispatchStatus` na
  resposta JSON são as strings `"Inbound"`, `"Text"` e `"Failed"`,
  respectivamente — nenhum dos três é um valor numérico

#### Scenario: Enums de mensagem de saída serializados como string
- **WHEN** um cliente consulta as mensagens de uma `Session` que inclui
  uma mensagem de saída com `Direction: Outbound` e
  `DeliveryStatus: Sent`
- **THEN** os campos `Direction` e `DeliveryStatus` na resposta JSON são
  as strings `"Outbound"` e `"Sent"`, respectivamente — nenhum dos dois
  é um valor numérico
