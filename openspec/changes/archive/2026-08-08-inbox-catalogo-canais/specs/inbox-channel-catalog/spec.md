## ADDED Requirements

### Requirement: Cadastro de canal de entrada
O sistema SHALL permitir, via `apps/inbox`, cadastrar um canal de entrada
informando tipo de canal (`WhatsApp` ou `Telegram`), nome, credenciais e o
identificador (`AgentId`) do agente de `apps/api` responsável por esse
canal. As credenciais SHALL ser criptografadas antes de serem persistidas e
nunca SHALL ser incluídas em nenhuma resposta da API. O `AgentId` informado
SHALL ser validado contra `apps/api` antes do cadastro ser efetivado.

#### Scenario: Criar canal com sucesso
- **WHEN** um cliente envia `POST /channels` com `channelType: "WhatsApp"`,
  nome, credenciais e um `agentId` que corresponde a um agente existente em
  `apps/api`
- **THEN** a API cria o registro e responde com o canal criado, incluindo
  um identificador único gerado pelo sistema, sem nenhum campo de
  credencial na resposta

#### Scenario: Criar canal sem nome, sem tipo de canal ou sem credenciais é rejeitado
- **WHEN** um cliente envia `POST /channels` sem nome, sem `channelType` ou
  sem credenciais
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar canal com tipo de canal inválido é rejeitado
- **WHEN** um cliente envia `POST /channels` com `channelType` diferente de
  `WhatsApp` ou `Telegram`
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

### Requirement: Listagem de canais de entrada
O sistema SHALL permitir, via `apps/inbox`, listar todos os canais
cadastrados, incluindo canais inativos, sem incluir a credencial de nenhum
deles na resposta.

#### Scenario: Lista retorna todos os canais cadastrados
- **WHEN** um cliente envia `GET /channels` e existem canais cadastrados
- **THEN** a API responde com a lista de todos os canais, incluindo id,
  tipo de canal, nome, `agentId` e `isActive` de cada um, sem nenhum campo
  de credencial

#### Scenario: Lista inclui canais inativos
- **WHEN** um cliente envia `GET /channels` e existe pelo menos um canal
  desativado (`isActive = false`)
- **THEN** a API inclui esse canal na resposta normalmente, com `isActive`
  refletindo `false`

### Requirement: Consulta de canal de entrada por id
O sistema SHALL permitir, via `apps/inbox`, consultar um canal específico
pelo seu identificador, incluindo canais inativos, sem incluir a credencial
na resposta.

#### Scenario: Consulta de canal existente retorna dados completos
- **WHEN** um cliente envia `GET /channels/{id}` para um id existente
- **THEN** a API responde com HTTP 200 e os dados completos do canal (tipo
  de canal, nome, `agentId`, `isActive`), sem nenhum campo de credencial

#### Scenario: Consulta de canal inexistente retorna 404
- **WHEN** um cliente envia `GET /channels/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404

### Requirement: Atualização de canal de entrada
O sistema SHALL permitir, via `apps/inbox`, atualizar nome, credenciais e
`AgentId` de um canal já cadastrado, com a mesma validação usada no
cadastro. Quando a atualização não incluir novas credenciais, as
credenciais anteriormente persistidas SHALL ser mantidas. Quando a
atualização incluir um novo `AgentId`, este SHALL ser validado contra
`apps/api` antes da atualização ser efetivada.

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

### Requirement: Ativação e desativação de canal de entrada
O sistema SHALL permitir, via `apps/inbox`, ativar e desativar um canal
cadastrado por meio de um campo de estado (`isActive`), sem excluir o
registro (nem exclusão definitiva, nem soft delete). As operações SHALL ser
idempotentes.

#### Scenario: Desativar canal ativo
- **WHEN** um cliente envia `POST /channels/{id}/deactivate` para um canal
  atualmente ativo
- **THEN** a API responde com HTTP 200 e o canal atualizado com
  `isActive: false`

#### Scenario: Desativar canal já inativo é idempotente
- **WHEN** um cliente envia `POST /channels/{id}/deactivate` para um canal
  que já está `isActive: false`
- **THEN** a API responde com HTTP 200 e o canal com `isActive: false`, sem
  erro

#### Scenario: Ativar canal inativo
- **WHEN** um cliente envia `POST /channels/{id}/activate` para um canal
  atualmente inativo
- **THEN** a API responde com HTTP 200 e o canal atualizado com
  `isActive: true`

#### Scenario: Ativar canal já ativo é idempotente
- **WHEN** um cliente envia `POST /channels/{id}/activate` para um canal
  que já está `isActive: true`
- **THEN** a API responde com HTTP 200 e o canal com `isActive: true`, sem
  erro

#### Scenario: Ativar ou desativar canal inexistente retorna 404
- **WHEN** um cliente envia `POST /channels/{id}/activate` ou
  `POST /channels/{id}/deactivate` para um id que não existe
- **THEN** a API responde com HTTP 404

### Requirement: Persistência durável e isolada do catálogo de canais
O sistema SHALL persistir o catálogo de canais em um banco PostgreSQL
próprio de `apps/inbox`, logicamente isolado (sem tabelas em comum) do
banco usado por `apps/api`/`apps/workers`, sem uso de armazenamento em
memória, garantindo que os dados sobrevivam a reinícios da aplicação.

#### Scenario: Canal cadastrado sobrevive a restart de apps/inbox
- **WHEN** um canal é cadastrado e o processo de `apps/inbox` é reiniciado
- **THEN** uma consulta subsequente a `GET /channels/{id}` continua
  retornando o canal com os mesmos dados
