## MODIFIED Requirements

### Requirement: Fronteira de sessão por inatividade
O sistema SHALL reaproveitar a `Session` mais recente de um `Contact` quando
a interação nova ocorrer dentro do timeout de inatividade configurado
(contado a partir de `LastActivityAt`), e SHALL criar uma nova `Session`
(com `ContextId` novo) quando a interação ocorrer após esse timeout, ou
quando o `Contact` ainda não tiver nenhuma `Session`. Reaproveitar uma
`Session` SHALL atualizar `LastActivityAt` para o momento da interação. O
timeout SHALL ser configurável por ambiente, sem exigir recompilação.

Ao criar uma nova `Session` por expiração de timeout, o sistema SHALL
marcar a `Session` anterior como encerrada (`ClosedAt` preenchido com o
instante da nova interação), de forma persistida em banco. O sistema
SHALL garantir, com uma restrição de banco (não apenas lógica de
aplicação), que no máximo uma `Session` de um mesmo `Contact` esteja sem
`ClosedAt` (aberta) a qualquer momento — inclusive sob chamadas
concorrentes verdadeiras para o mesmo `Contact`, que SHALL resolver
deterministicamente para uma única `Session` aberta, sem exceção não
tratada em nenhuma das chamadas concorrentes.

#### Scenario: Interação dentro do timeout reaproveita a Session existente
- **WHEN** um `Contact` já tem uma `Session` cujo `LastActivityAt` está
  dentro do timeout de inatividade configurado, e uma nova interação chega
  para o mesmo `Contact`
- **THEN** a `Session` existente é reaproveitada (mesmo `ContextId`) e seu
  `LastActivityAt` é atualizado para o momento da nova interação

#### Scenario: Interação após o timeout cria uma nova Session e encerra a anterior
- **WHEN** um `Contact` já tem uma `Session` cujo `LastActivityAt` está além
  do timeout de inatividade configurado, e uma nova interação chega para o
  mesmo `Contact`
- **THEN** uma nova `Session` é criada para o mesmo `Contact`, com um
  `ContextId` novo, distinto do da `Session` anterior, e a `Session`
  anterior passa a ter `ClosedAt` preenchido com o instante dessa nova
  interação

#### Scenario: Chamadas verdadeiramente concorrentes para o mesmo Contact novo resolvem para uma única Session
- **WHEN** N chamadas concorrentes chegam para o mesmo `(ChannelId,
  ExternalId)` que nunca apareceu antes, todas dentro da mesma janela de
  corrida (nenhum `Contact` nem `Session` ainda existe)
- **THEN** exatamente uma `Session` é criada para o `Contact` resultante,
  nenhuma chamada recebe exceção não tratada, e todas as N chamadas
  resolvem para essa mesma `Session`

#### Scenario: Chamadas concorrentes na fronteira exata do timeout não duplicam a Session aberta
- **WHEN** duas chamadas concorrentes chegam para o mesmo `Contact` no
  instante em que sua `Session` mais recente acabou de expirar, e ambas
  decidem, de forma independente, que uma nova `Session` deve ser criada
- **THEN** apenas uma nova `Session` é criada; a chamada que perde a
  corrida resolve para essa mesma `Session` recém-criada, sem exceção não
  tratada nem segunda `Session` órfã

#### Scenario: Nenhum encerramento explícito de sessão é suportado
- **WHEN** um agente ou humano tenta marcar uma `Session` como resolvida ou
  encerrada explicitamente
- **THEN** o sistema não expõe nenhum mecanismo para isso nesta fatia; o
  único jeito de uma `Session` deixar de ser reaproveitada (e de ter
  `ClosedAt` preenchido) é o timeout de inatividade

### Requirement: Consulta de sessões de um contato
O sistema SHALL permitir, via `apps/inbox`, listar todas as sessões de um
contato específico, incluindo sessões antigas já fora do timeout de
inatividade, com o instante em que cada uma foi encerrada (`ClosedAt`),
quando aplicável.

#### Scenario: Lista de sessões de um contato existente
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um `Contact`
  existente que já teve pelo menos uma `Session`
- **THEN** a API responde com HTTP 200 e a lista de todas as sessões desse
  contato, incluindo `contextId`, `startedAt`, `lastActivityAt` e
  `closedAt` de cada uma

#### Scenario: Sessão aberta tem closedAt nulo
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um `Contact`
  cuja `Session` mais recente ainda está dentro do timeout de inatividade
- **THEN** essa `Session`, na resposta, tem `closedAt` nulo

#### Scenario: Sessão encerrada por timeout tem closedAt preenchido
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um `Contact`
  que já teve uma `Session` superada por uma interação após o timeout de
  inatividade
- **THEN** essa `Session` superada, na resposta, tem `closedAt` preenchido
  com o instante em que a `Session` seguinte foi criada

#### Scenario: Lista vazia para contato sem nenhuma sessão
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um `Contact`
  existente que ainda não teve nenhuma `Session`
- **THEN** a API responde com HTTP 200 e uma lista vazia

#### Scenario: Consulta de sessões de contato inexistente retorna 404
- **WHEN** um cliente envia `GET /contacts/{id}/sessions` para um id de
  `Contact` que não existe
- **THEN** a API responde com HTTP 404
