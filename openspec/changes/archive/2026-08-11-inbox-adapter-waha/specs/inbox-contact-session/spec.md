## ADDED Requirements

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

## MODIFIED Requirements

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
