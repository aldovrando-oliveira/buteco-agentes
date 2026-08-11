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
validado contra `apps/api` antes do cadastro ser efetivado.

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
