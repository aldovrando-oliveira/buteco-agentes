# agent-mcp-binding-ui Specification

## Purpose

TBD - defined by change frontend-agente-vinculo-mcp-tools. Update Purpose after archive.

## Requirements

### Requirement: Página de gestão do vínculo agente↔servidores MCP

O sistema SHALL prover, em `apps/frontend`, uma página em
`/agents/:id/mcp-servers` que lista todos os servidores MCP do catálogo
(`GET /mcp-servers`), permite selecionar quais estão vinculados ao
agente e, para cada servidor selecionado, escolher quais tools desse
servidor o agente pode usar, enviando o resultado via
`PUT /agents/{id}/mcp-servers`. A página SHALL incluir uma ação para
cancelar e voltar à página de detalhe do agente sem enviar nenhuma
requisição.

#### Scenario: Página carregada com sucesso
- **WHEN** o usuário acessa `/agents/:id/mcp-servers` para um agente
  existente e o catálogo de servidores MCP não está vazio
- **THEN** a interface exibe a lista de todos os servidores MCP
  cadastrados, cada um com um controle para selecioná-lo, e uma ação
  para salvar o vínculo e uma ação para cancelar

#### Scenario: Catálogo de servidores MCP vazio
- **WHEN** o usuário acessa `/agents/:id/mcp-servers` e `GET /mcp-servers`
  retorna uma lista vazia
- **THEN** a interface indica que não há nenhum servidor MCP cadastrado,
  sem exibir a lista de seleção nem permitir o submit

#### Scenario: Agente inexistente
- **WHEN** o usuário acessa `/agents/:id/mcp-servers` para um `id` que
  não corresponde a nenhum agente cadastrado (`GET /agents/{id}`
  responde 404)
- **THEN** a interface exibe um estado de "agente não encontrado", sem
  quebrar a navegação do restante da aplicação

#### Scenario: Indicador distingue servidor MCP inativo na lista de seleção
- **WHEN** a lista de servidores MCP disponíveis para seleção inclui um
  servidor com `isActive: false`
- **THEN** a interface exibe, na linha desse servidor, um indicador
  visual de que ele está inativo, mas mantém o controle de seleção
  habilitado

#### Scenario: Cancelar a gestão do vínculo
- **WHEN** o usuário aciona a ação "Cancelar" na página de gestão do
  vínculo, com ou sem alterações não salvas
- **THEN** a interface navega para a página de detalhe do agente
  (`/agents/{id}`) sem enviar nenhuma requisição a
  `PUT /agents/{id}/mcp-servers` e sem exibir nenhum diálogo de
  confirmação

### Requirement: Pré-seleção de servidores e tools a partir do vínculo atual

O sistema SHALL pré-selecionar, ao carregar a página, os servidores MCP
presentes em `agent.mcpServers` (retornado por `GET /agents/{id}`), cada
um com o `allowedTools` atual como seleção inicial de tools.

#### Scenario: Servidores já vinculados aparecem pré-selecionados
- **WHEN** o usuário acessa a página de gestão do vínculo de um agente
  cujo `mcpServers` já inclui um ou mais servidores
- **THEN** a interface exibe cada um desses servidores com o controle
  de seleção já marcado, sem exigir nenhuma ação do usuário

#### Scenario: Servidores não vinculados aparecem desmarcados
- **WHEN** o usuário acessa a página de gestão do vínculo de um agente e
  o catálogo de servidores MCP inclui servidores ausentes de
  `agent.mcpServers`
- **THEN** a interface exibe esses servidores com o controle de seleção
  desmarcado

#### Scenario: Tools permitidas aparecem pré-marcadas ao expandir um servidor vinculado
- **WHEN** o usuário expande um servidor já vinculado (cujo
  `allowedTools` não é vazio) e a descoberta de tools desse servidor
  retorna com sucesso
- **THEN** a interface exibe, marcadas, exatamente as tools que constam
  em `allowedTools` e também na lista retornada pela descoberta

#### Scenario: Tool permitida que o servidor não oferece mais não aparece marcada
- **WHEN** o usuário expande um servidor vinculado cujo `allowedTools`
  inclui uma tool que a descoberta ao vivo (`GET /mcp-servers/{id}/tools`)
  não retorna mais na lista de tools do servidor
- **THEN** a interface não exibe essa tool como marcada (ela deixa de
  fazer parte da seleção), sem exibir erro para esse caso

### Requirement: Descoberta lazy de tools por servidor

O sistema SHALL disparar a busca de tools de um servidor MCP
(`GET /mcp-servers/{id}/tools`) somente quando esse servidor é
selecionado ou expandido na interface — nunca para todos os servidores
do catálogo de uma vez ao carregar a página.

#### Scenario: Abrir a página não dispara busca de tools de nenhum servidor
- **WHEN** o usuário acessa a página de gestão do vínculo e ainda não
  expandiu nem selecionou nenhum servidor
- **THEN** a interface não envia nenhuma chamada a
  `GET /mcp-servers/{id}/tools`

#### Scenario: Selecionar ou expandir um servidor dispara a busca das suas tools
- **WHEN** o usuário seleciona ou expande um servidor MCP específico
  pela primeira vez
- **THEN** a interface envia `GET /mcp-servers/{id}/tools` para esse
  servidor e exibe um indicador de carregamento até a resposta chegar

#### Scenario: Recolher e reabrir um servidor não repete a busca desnecessariamente
- **WHEN** o usuário recolhe um servidor cujas tools já foram
  descobertas com sucesso e o expande novamente
- **THEN** a interface reutiliza o resultado já obtido, sem exigir uma
  nova chamada a `GET /mcp-servers/{id}/tools`

### Requirement: Seleção e desseleção de servidores e tools individuais

O usuário SHALL poder selecionar e desselecionar cada servidor MCP
independentemente, e, para um servidor selecionado cujas tools já foram
descobertas, marcar e desmarcar cada tool individualmente.

#### Scenario: Selecionar um servidor
- **WHEN** o usuário marca o controle de seleção de um servidor MCP
  ainda não selecionado
- **THEN** a interface passa a considerá-lo parte do vínculo a ser
  salvo, com `allowedTools` inicialmente vazio até que o usuário marque
  alguma tool

#### Scenario: Desselecionar um servidor remove seu vínculo do envio
- **WHEN** o usuário desmarca o controle de seleção de um servidor MCP
  previamente selecionado
- **THEN** a interface deixa de considerá-lo parte do vínculo a ser
  salvo, independentemente de quais tools estavam marcadas para ele

#### Scenario: Marcar e desmarcar uma tool individual
- **WHEN** o usuário, com um servidor selecionado e suas tools já
  descobertas, marca ou desmarca uma tool específica
- **THEN** a interface atualiza o `allowedTools` desse servidor para
  refletir exatamente as tools marcadas, sem afetar a seleção de
  nenhum outro servidor

### Requirement: Submit do vínculo via PUT /agents/{id}/mcp-servers

Ao confirmar, a interface SHALL enviar `PUT /agents/{id}/mcp-servers`
com `{ mcpServers: [...] }`, contendo um item `{ mcpServerId,
allowedTools }` para cada servidor selecionado — nunca omitindo o campo
`mcpServers` nem enviando-o como `null`. Em caso de sucesso, a interface
SHALL exibir uma notificação de sucesso e navegar de volta para a
página de detalhe do agente.

#### Scenario: Submit com um ou mais servidores selecionados
- **WHEN** o usuário confirma o vínculo com um ou mais servidores
  selecionados, cada um com seu `allowedTools`
- **THEN** a interface envia `PUT /agents/{id}/mcp-servers` com
  `mcpServers` contendo um item por servidor selecionado e o
  `allowedTools` correspondente, exibe uma notificação de sucesso e
  navega para `/agents/{id}`

#### Scenario: Submit sem nenhum servidor selecionado remove todos os vínculos
- **WHEN** o usuário confirma o vínculo sem nenhum servidor selecionado
  (incluindo o caso de um agente que já tinha servidores vinculados e
  todos foram desmarcados)
- **THEN** a interface envia `PUT /agents/{id}/mcp-servers` com
  `mcpServers: []`, exibe uma notificação de sucesso e navega para
  `/agents/{id}`

### Requirement: Tratamento do erro atômico de validação (502)

O sistema SHALL exibir, quando `PUT /agents/{id}/mcp-servers` responde
`502` (falha de handshake de validação contra um dos servidores do
payload), uma mensagem que identifica qual servidor MCP falhou a
validação e o motivo, deixando claro que nenhum vínculo foi salvo, sem
navegar para outra página e sem limpar a seleção já feita pelo usuário.

#### Scenario: Falha de handshake identifica o servidor e preserva a seleção
- **WHEN** o usuário confirma o vínculo e a API responde `502` com um
  `ProblemDetails` identificando um `McpServerId` e o motivo da falha
- **THEN** a interface exibe uma mensagem citando esse servidor e o
  motivo, indica que nada foi salvo, e mantém intacta a seleção de
  todos os servidores e tools que o usuário já tinha marcado — inclusive
  a dos servidores que não causaram o problema

#### Scenario: Usuário pode corrigir e tentar novamente após falha de handshake
- **WHEN** o usuário, após ver o erro de handshake de um servidor
  específico, desmarca esse servidor (ou o mantém e tenta novamente) e
  confirma outra vez
- **THEN** a interface envia um novo `PUT /agents/{id}/mcp-servers`
  refletindo o estado atual da seleção, sem exigir que a página seja
  recarregada

### Requirement: Falha na descoberta de tools de um servidor específico não bloqueia sua seleção

O sistema SHALL permitir, quando `GET /mcp-servers/{id}/tools` responde
com `success: false` (falha de conexão durante a descoberta, distinta do
erro de submit), que o usuário selecione esse servidor mesmo assim, com
`allowedTools` vazio, e SHALL oferecer uma ação para tentar buscar as
tools novamente, sem bloquear a seleção dos demais servidores.

#### Scenario: Falha de descoberta exibe o motivo e uma ação de tentar novamente
- **WHEN** o usuário expande um servidor e `GET /mcp-servers/{id}/tools`
  responde `200` com `success: false`
- **THEN** a interface exibe o motivo da falha (`message`) e uma ação
  "Tentar novamente" para esse servidor, em vez da lista de tools

#### Scenario: Servidor com falha de descoberta pode ser selecionado com allowedTools vazio
- **WHEN** o usuário seleciona um servidor cuja descoberta de tools
  falhou
- **THEN** a interface o inclui no vínculo a ser salvo com
  `allowedTools: []`, sem impedir a seleção nem exigir que a descoberta
  tenha sucesso primeiro

#### Scenario: Tentar novamente refaz a busca sem afetar outros servidores
- **WHEN** o usuário aciona "Tentar novamente" na descoberta de tools de
  um servidor cuja busca anterior falhou
- **THEN** a interface envia novamente `GET /mcp-servers/{id}/tools`
  para esse servidor, sem alterar a seleção nem o estado de nenhum
  outro servidor

#### Scenario: Descoberta bem-sucedida após retry libera a seleção de tools
- **WHEN** o retry de descoberta de tools de um servidor responde com
  `success: true`
- **THEN** a interface exibe a lista de tools desse servidor disponível
  para seleção, substituindo a mensagem de falha anterior
