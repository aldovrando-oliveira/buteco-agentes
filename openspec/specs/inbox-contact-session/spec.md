# inbox-contact-session Specification

## Purpose

TBD - defined by change inbox-crm-contato-sessao. Update Purpose after archive.

## Requirements

### Requirement: Resolução de contato e sessão a partir de canal e identificador externo
O sistema SHALL expor, via `apps/inbox`, um serviço interno que, dado um
`ChannelId` e um `ExternalId` (identificador do contato na plataforma de
origem, ex. número de telefone, `chat_id`), encontra ou cria o `Contact`
correspondente e resolve a `Session` ativa a ser usada para essa interação,
sem exigir nenhuma chamada de rede a `apps/api`.

#### Scenario: Primeira interação de um contato cria Contact e Session
- **WHEN** o serviço é chamado com um `(ChannelId, ExternalId)` que nunca
  apareceu antes
- **THEN** um novo `Contact` é criado para esse `(ChannelId, ExternalId)` e
  uma nova `Session` é criada para esse `Contact`, com um `ContextId` novo

#### Scenario: Duas chamadas com o mesmo par (ChannelId, ExternalId) sempre resolvem para o mesmo Contact
- **WHEN** o serviço é chamado duas vezes com o mesmo `(ChannelId, ExternalId)`
- **THEN** as duas chamadas resolvem para o mesmo `Contact`, nunca criando um
  segundo registro para o mesmo par

#### Scenario: O mesmo identificador externo em canais diferentes gera Contacts distintos
- **WHEN** o serviço é chamado com o mesmo `ExternalId` mas `ChannelId`
  diferentes
- **THEN** cada `ChannelId` resolve para um `Contact` distinto, sem
  unificação de identidade entre canais

### Requirement: Captura de metadado do contato na criação
O sistema SHALL aceitar, na resolução de contato e sessão, um metadado
adicional (dicionário de string) fornecido pelo adapter de origem, e
SHALL gravá-lo no `Contact` somente no momento da criação. Quando a
resolução encontrar um `Contact` já existente para o par
`(ChannelId, ExternalId)`, o metadado informado na chamada SHALL ser
ignorado — o metadado persistido do `Contact` não é atualizado por
chamadas subsequentes.

#### Scenario: Metadado é gravado na criação de um novo Contact
- **WHEN** o serviço de resolução é chamado com um `(ChannelId, ExternalId)`
  que nunca apareceu antes, informando um metadado não vazio
- **THEN** o `Contact` criado persiste esse metadado

#### Scenario: Metadado informado em chamada subsequente não altera o Contact existente
- **WHEN** o serviço de resolução é chamado para um `(ChannelId, ExternalId)`
  que já corresponde a um `Contact` existente, informando um metadado
  diferente do persistido
- **THEN** o `Contact` resolvido mantém o metadado gravado na criação, sem
  ser alterado pelo metadado informado nesta chamada

### Requirement: Fronteira de sessão por inatividade
O sistema SHALL reaproveitar a `Session` mais recente de um `Contact` quando
a interação nova ocorrer dentro do timeout de inatividade configurado
(contado a partir de `LastActivityAt`), e SHALL criar uma nova `Session`
(com `ContextId` novo) quando a interação ocorrer após esse timeout, ou
quando o `Contact` ainda não tiver nenhuma `Session`. Reaproveitar uma
`Session` SHALL atualizar `LastActivityAt` para o momento da interação. O
timeout SHALL ser configurável por ambiente, sem exigir recompilação.

#### Scenario: Interação dentro do timeout reaproveita a Session existente
- **WHEN** um `Contact` já tem uma `Session` cujo `LastActivityAt` está
  dentro do timeout de inatividade configurado, e uma nova interação chega
  para o mesmo `Contact`
- **THEN** a `Session` existente é reaproveitada (mesmo `ContextId`) e seu
  `LastActivityAt` é atualizado para o momento da nova interação

#### Scenario: Interação após o timeout cria uma nova Session
- **WHEN** um `Contact` já tem uma `Session` cujo `LastActivityAt` está além
  do timeout de inatividade configurado, e uma nova interação chega para o
  mesmo `Contact`
- **THEN** uma nova `Session` é criada para o mesmo `Contact`, com um
  `ContextId` novo, distinto do da `Session` anterior

#### Scenario: Nenhum encerramento explícito de sessão é suportado
- **WHEN** um agente ou humano tenta marcar uma `Session` como resolvida ou
  encerrada explicitamente
- **THEN** o sistema não expõe nenhum mecanismo para isso nesta fatia; o
  único jeito de uma `Session` deixar de ser reaproveitada é o timeout de
  inatividade

### Requirement: Consulta de contatos
O sistema SHALL permitir, via `apps/inbox`, listar todos os contatos
cadastrados, com seu canal de origem, identificador externo e metadado
capturado na criação.

#### Scenario: Lista retorna todos os contatos cadastrados
- **WHEN** um cliente envia `GET /contacts` e existem contatos cadastrados
- **THEN** a API responde com a lista de todos os contatos, incluindo id,
  `channelId`, `externalId`, `metadata` e `createdAt` de cada um

#### Scenario: Lista vazia quando não há contatos
- **WHEN** um cliente envia `GET /contacts` e não existe nenhum contato
  cadastrado
- **THEN** a API responde com HTTP 200 e uma lista vazia

### Requirement: Consulta de sessões de um contato
O sistema SHALL permitir, via `apps/inbox`, listar todas as sessões de um
contato específico, incluindo sessões antigas já fora do timeout de
inatividade.

#### Scenario: Lista de sessões de um contato existente
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um `Contact`
  existente que já teve pelo menos uma `Session`
- **THEN** a API responde com HTTP 200 e a lista de todas as sessões desse
  contato, incluindo `contextId`, `startedAt` e `lastActivityAt` de cada uma

#### Scenario: Lista vazia para contato sem nenhuma sessão
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um `Contact`
  existente que ainda não teve nenhuma `Session`
- **THEN** a API responde com HTTP 200 e uma lista vazia

#### Scenario: Consulta de sessões de contato inexistente retorna 404
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um id de
  `Contact` que não existe
- **THEN** a API responde com HTTP 404

### Requirement: Persistência durável do CRM de contatos e sessões
O sistema SHALL persistir `Contact` e `Session` no mesmo banco PostgreSQL já
usado por `apps/inbox` para o catálogo de canais, sem uso de armazenamento
em memória, garantindo que os dados sobrevivam a reinícios da aplicação, e
sem armazenar nenhum conteúdo de mensagem — apenas o ponteiro (`ContextId`)
para a conversa, cujo conteúdo real continua persistido em `apps/api`.

#### Scenario: Contato e sessão sobrevivem a restart de apps/inbox
- **WHEN** um `Contact` e uma `Session` são criados e o processo de
  `apps/inbox` é reiniciado
- **THEN** uma consulta subsequente a `GET /contacts/{id}/sessions` continua
  retornando a sessão com os mesmos dados

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
