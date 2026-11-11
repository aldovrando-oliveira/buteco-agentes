## MODIFIED Requirements

### Requirement: Ativação e desativação de agente pela interface
O sistema SHALL prover, em `apps/frontend`, ações para ativar e desativar
um agente cadastrado, consumindo `POST /agents/{id}/activate` e
`POST /agents/{id}/deactivate` a partir da página de detalhe do agente.
Desativar um agente SHALL exigir confirmação explícita do usuário antes de
enviar a requisição; ativar um agente SHALL NOT exigir confirmação.

Quando o agente tem aplicações externas ativas com acesso
(`panel-external-application-agents`, card "Aplicações com acesso"), o diálogo
de confirmação SHALL manter o título e o texto de hoje e acrescentar, abaixo,
"Desativar este agente afeta **{N} aplicações ativas**: as tasks que elas
abrirem nele passam a terminar recusadas." (no singular, "Desativar este agente
afeta **1 aplicação ativa**: as tasks que ela abrir nele passam a terminar
recusadas."),
com o mesmo N do card. Sem aplicação ativa, ou com a contagem ainda sem
resposta ou com falha, o diálogo SHALL ficar como hoje.

#### Scenario: Ativar agente com sucesso, sem confirmação
- **WHEN** o usuário aciona a ação de ativar um agente inativo
- **THEN** a interface envia `POST /agents/{id}/activate` imediatamente,
  sem exibir nenhum diálogo de confirmação, exibe uma notificação de
  sucesso e atualiza o indicador de estado exibido para "ativo"

#### Scenario: Desativar agente exige confirmação antes de enviar a requisição
- **WHEN** o usuário aciona a ação de desativar um agente ativo
- **THEN** a interface exibe um diálogo de confirmação antes de enviar
  qualquer requisição, e `POST /agents/{id}/deactivate` só é enviado se o
  usuário confirmar explicitamente a ação nesse diálogo

#### Scenario: Cancelar a confirmação de desativação não envia a requisição
- **WHEN** o usuário aciona a ação de desativar um agente ativo e, no
  diálogo de confirmação exibido, escolhe cancelar
- **THEN** a interface fecha o diálogo sem enviar `POST
  /agents/{id}/deactivate` e o agente permanece exibido como ativo

#### Scenario: Confirmar a desativação envia a requisição e atualiza o estado
- **WHEN** o usuário aciona a ação de desativar um agente ativo e, no
  diálogo de confirmação exibido, confirma a ação
- **THEN** a interface envia `POST /agents/{id}/deactivate`, exibe uma
  notificação de sucesso e atualiza o indicador de estado exibido para
  "inativo"

#### Scenario: Falha de rede ou do servidor ao ativar ou desativar
- **WHEN** a chamada a `POST /agents/{id}/activate` ou `POST
  /agents/{id}/deactivate` falha (erro de rede ou erro do servidor)
- **THEN** a interface exibe uma notificação de erro genérica e mantém o
  indicador de estado exibido igual ao estado anterior à tentativa

#### Scenario: O diálogo diz quantas aplicações ativas são afetadas
- **WHEN** um Operador aciona "Desativar" num agente cuja contagem é
  `{ total: 3, ativas: 2 }`
- **THEN** o diálogo mostra o texto de hoje e, abaixo, "Desativar este
  agente afeta 2 aplicações ativas: as tasks que elas abrirem nele passam a
  terminar recusadas."

#### Scenario: Sem aplicação ativa, o diálogo de hoje
- **WHEN** o usuário aciona "Desativar" num agente com `{ total: 1, ativas: 0 }`
- **THEN** o diálogo mostra só o título e o texto de hoje
