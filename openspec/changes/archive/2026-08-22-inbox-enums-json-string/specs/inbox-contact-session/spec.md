## MODIFIED Requirements

### Requirement: Consulta de sessões de um canal por última atividade, com prévia da última mensagem
O sistema SHALL permitir, via `apps/inbox`, listar todas as sessões de um
`Channel` específico, ordenadas pela mais recente atividade primeiro,
incluindo o identificador externo e o nome de exibição do `Contact` de cada
sessão, e uma prévia da última mensagem dessa sessão (ver capability
`inbox-message-history`) — direção, conteúdo e instante. O campo de
direção dessa prévia SHALL ser serializado como string com o nome do
valor do enum (`"Inbound"` ou `"Outbound"`), nunca como inteiro ordinal.

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

#### Scenario: Direção da prévia serializada como string
- **WHEN** um cliente envia `GET /channels/{channelId}/sessions` para um
  `Channel` cuja sessão mais recente tem, como última mensagem, uma
  mensagem de entrada (`Direction: Inbound`)
- **THEN** o campo de direção dentro da prévia da última mensagem, na
  resposta JSON, é a string `"Inbound"`, não um valor numérico
