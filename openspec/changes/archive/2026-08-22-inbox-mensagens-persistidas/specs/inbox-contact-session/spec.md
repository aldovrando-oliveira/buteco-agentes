## ADDED Requirements

### Requirement: Captura e atualização contínua do nome de exibição do contato
O sistema SHALL aceitar, na resolução de contato e sessão, um nome de
exibição opcional extraído pelo adapter de origem a partir do payload da
mensagem recebida, e SHALL gravá-lo no `Contact` tanto na criação quanto em
toda chamada subsequente para o mesmo `(ChannelId, ExternalId)` — diferente
do metadado de criação (Requirement "Captura de metadado do contato na
criação"), que é gravado só uma vez. Quando a chamada não informar nenhum
nome de exibição, o `Contact` SHALL manter o valor já persistido
(nulo, se nunca informado), sem ser apagado por essa chamada.

#### Scenario: Nome de exibição é gravado na criação de um novo Contact
- **WHEN** o serviço de resolução é chamado com um `(ChannelId, ExternalId)`
  que nunca apareceu antes, informando um nome de exibição não vazio
- **THEN** o `Contact` criado persiste esse nome de exibição

#### Scenario: Nome de exibição é atualizado em chamada subsequente para um Contact existente
- **WHEN** o serviço de resolução é chamado para um `(ChannelId, ExternalId)`
  que já corresponde a um `Contact` existente, informando um nome de
  exibição diferente do persistido
- **THEN** o `Contact` resolvido passa a ter o novo nome de exibição
  persistido, substituindo o anterior

#### Scenario: Chamada sem nome de exibição não apaga o valor já persistido
- **WHEN** o serviço de resolução é chamado para um `(ChannelId, ExternalId)`
  que já corresponde a um `Contact` existente com nome de exibição
  persistido, sem informar nenhum nome de exibição nesta chamada
- **THEN** o `Contact` resolvido mantém o nome de exibição já persistido,
  sem ser apagado

#### Scenario: Contact criado sem nome de exibição informado fica com o campo nulo
- **WHEN** o serviço de resolução é chamado com um `(ChannelId, ExternalId)`
  que nunca apareceu antes, sem informar nenhum nome de exibição
- **THEN** o `Contact` criado persiste nome de exibição nulo

### Requirement: Consulta de sessões de um canal por última atividade, com prévia da última mensagem
O sistema SHALL permitir, via `apps/inbox`, listar todas as sessões de um
`Channel` específico, ordenadas pela mais recente atividade primeiro,
incluindo o identificador externo e o nome de exibição do `Contact` de cada
sessão, e uma prévia da última mensagem dessa sessão (ver capability
`inbox-message-history`) — direção, conteúdo e instante.

#### Scenario: Lista de sessões de um canal com sessões existentes
- **WHEN** um cliente envia `GET /channels/{channelId}/sessions` para um
  `Channel` existente que já tem pelo menos uma `Session` com mensagem
  persistida
- **THEN** a API responde com HTTP 200 e as sessões desse canal ordenadas
  por última atividade (mais recente primeiro), cada uma com o
  identificador externo e o nome de exibição do `Contact`, e a prévia
  (direção, conteúdo e instante) da última mensagem dessa sessão

#### Scenario: Canal sem nenhuma sessão retorna lista vazia
- **WHEN** um cliente envia `GET /channels/{channelId}/sessions` para um
  `Channel` existente que ainda não tem nenhuma `Session`
- **THEN** a API responde com HTTP 200 e uma lista vazia

#### Scenario: Consulta de sessões de canal inexistente retorna 404
- **WHEN** um cliente envia `GET /channels/{channelId}/sessions` para um id
  de `Channel` que não existe
- **THEN** a API responde com HTTP 404
