## MODIFIED Requirements

### Requirement: Cadastro de canal de entrada
O sistema SHALL permitir, via `apps/inbox`, cadastrar um canal de entrada
informando tipo de canal, nome, credenciais e o identificador (`AgentId`)
do agente de `apps/api` responsável por esse canal. O tipo de canal
informado SHALL corresponder a um adapter efetivamente registrado no
processo (ver capability `inbox-channel-adapter-plugin`) — não a uma
lista fixa de valores conhecidos em tempo de compilação. As credenciais
SHALL ser criptografadas antes de serem persistidas e nunca SHALL ser
incluídas em nenhuma resposta da API. O `AgentId` informado SHALL ser
validado contra `apps/api` antes do cadastro ser efetivado. Quando o
adapter do `channelType` informado implementa o contrato opcional de
provisionamento automático de configuração externa (ver capability
`inbox-channel-adapter-plugin`), o sistema SHALL invocá-lo antes de
persistir o canal, usando a URL de webhook computada para o identificador
do canal ainda não persistido. Uma falha nesse provisionamento SHALL
impedir o cadastro inteiro — nenhum canal SHALL ser persistido sem o
provisionamento externo correspondente ter tido sucesso.

#### Scenario: Criar canal com sucesso
- **WHEN** um cliente envia `POST /channels` com um `channelType`
  correspondente a um adapter registrado, nome, credenciais válidas para
  esse adapter e um `agentId` que corresponde a um agente existente em
  `apps/api`
- **THEN** a API cria o registro e responde com o canal criado, incluindo
  um identificador único gerado pelo sistema, sem nenhum campo de
  credencial na resposta

#### Scenario: Criar canal sem nome, sem tipo de canal ou sem credenciais é rejeitado
- **WHEN** um cliente envia `POST /channels` sem nome, sem `channelType` ou
  sem credenciais
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar canal com tipo de canal sem adapter registrado é rejeitado
- **WHEN** um cliente envia `POST /channels` com um `channelType` que não
  corresponde a nenhum adapter registrado no processo
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar canal com AgentId inexistente é rejeitado
- **WHEN** um cliente envia `POST /channels` com um `agentId` que
  `apps/api` responde não existir (`GET /agents/{id}` retorna HTTP 404)
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar canal com apps/api inalcançável é rejeitado
- **WHEN** um cliente envia `POST /channels` e a chamada a `apps/api` para
  validar o `agentId` falha (timeout, conexão recusada, ou resposta de
  erro do servidor)
- **THEN** a API responde com erro (HTTP 400 ou 502, indicando falha na
  validação da referência) e não cria nenhum registro

#### Scenario: Criar canal vinculado a agente inativo é permitido
- **WHEN** um cliente envia `POST /channels` com um `agentId` que
  corresponde a um agente existente em `apps/api`, porém com
  `isActive: false`
- **THEN** a API cria o registro normalmente, sem tratar o estado inativo
  do agente como impedimento

#### Scenario: Criar canal de tipo com provisionamento automático invoca o provisionamento
- **WHEN** um cliente envia `POST /channels` com um `channelType` cujo
  adapter implementa o contrato opcional de provisionamento automático,
  com nome, credenciais e `agentId` válidos
- **THEN** a API invoca o provisionamento antes de persistir o canal e,
  em caso de sucesso, cria o registro com a credencial eventualmente
  atualizada pelo provisionamento, sem nenhum campo de credencial na
  resposta

#### Scenario: Falha no provisionamento automático impede o cadastro
- **WHEN** um cliente envia `POST /channels` com um `channelType` cujo
  adapter implementa o contrato opcional de provisionamento automático, e
  a chamada de provisionamento falha (credencial rejeitada pela
  plataforma externa, ou plataforma externa inalcançável)
- **THEN** a API responde com erro (HTTP 502) e não cria nenhum registro

### Requirement: Atualização de canal de entrada
O sistema SHALL permitir, via `apps/inbox`, atualizar nome, credenciais e
`AgentId` de um canal já cadastrado, com a mesma validação usada no
cadastro. Quando a atualização não incluir novas credenciais, as
credenciais anteriormente persistidas SHALL ser mantidas. Quando a
atualização incluir um novo `AgentId`, este SHALL ser validado contra
`apps/api` antes da atualização ser efetivada. Quando a atualização
incluir novas credenciais para um canal cujo adapter implementa o
contrato opcional de provisionamento automático de configuração externa
(ver capability `inbox-channel-adapter-plugin`), o sistema SHALL invocar
esse provisionamento novamente antes de substituir as credenciais
persistidas. Uma falha nesse provisionamento SHALL impedir a atualização
inteira — as credenciais anteriormente persistidas SHALL permanecer
inalteradas.

#### Scenario: Atualizar canal com sucesso
- **WHEN** um cliente envia `PUT /channels/{id}` com nome e `agentId`
  válidos (existente em `apps/api`) para um id existente
- **THEN** a API atualiza o registro e responde com HTTP 200 e o canal
  atualizado, sem incluir credencial na resposta

#### Scenario: Atualizar canal trocando as credenciais
- **WHEN** um cliente envia `PUT /channels/{id}` com novas credenciais
- **THEN** a API substitui as credenciais criptografadas persistidas pelas
  novas

#### Scenario: Atualizar canal mantendo as credenciais existentes
- **WHEN** um cliente envia `PUT /channels/{id}` sem informar credenciais
- **THEN** a API mantém as credenciais criptografadas anteriormente
  persistidas, sem exigi-las novamente

#### Scenario: Atualizar canal trocando o AgentId para um inexistente é rejeitado
- **WHEN** um cliente envia `PUT /channels/{id}` com um `agentId` que
  `apps/api` responde não existir
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar canal inexistente retorna 404
- **WHEN** um cliente envia `PUT /channels/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404 e não cria nenhum registro

#### Scenario: Atualizar canal vinculado a agente inativo é permitido
- **WHEN** um cliente envia `PUT /channels/{id}` com um `agentId` que
  corresponde a um agente existente em `apps/api`, porém com
  `isActive: false`
- **THEN** a API atualiza o registro normalmente, sem tratar o estado
  inativo do agente como impedimento

#### Scenario: Atualizar credenciais de canal com provisionamento automático reprovisiona
- **WHEN** um cliente envia `PUT /channels/{id}` com novas credenciais
  para um canal cujo adapter implementa o contrato opcional de
  provisionamento automático
- **THEN** a API invoca o provisionamento novamente antes de substituir
  as credenciais persistidas e, em caso de sucesso, atualiza o registro
  com a credencial eventualmente atualizada pelo provisionamento

#### Scenario: Falha no reprovisionamento impede a atualização
- **WHEN** um cliente envia `PUT /channels/{id}` com novas credenciais
  para um canal cujo adapter implementa o contrato opcional de
  provisionamento automático, e a chamada de provisionamento falha
- **THEN** a API responde com erro (HTTP 502) e as credenciais
  anteriormente persistidas permanecem inalteradas
