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
cadastrados, com seu canal de origem e identificador externo.

#### Scenario: Lista retorna todos os contatos cadastrados
- **WHEN** um cliente envia `GET /contacts` e existem contatos cadastrados
- **THEN** a API responde com a lista de todos os contatos, incluindo id,
  `channelId`, `externalId` e `createdAt` de cada um

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
