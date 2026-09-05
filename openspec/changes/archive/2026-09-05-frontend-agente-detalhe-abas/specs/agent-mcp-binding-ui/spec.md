## RENAMED Requirements

- FROM: `### Requirement: Página de gestão do vínculo agente↔servidores MCP`
- TO: `### Requirement: Aba Ferramentas no detalhe do agente`

## MODIFIED Requirements

### Requirement: Aba Ferramentas no detalhe do agente

O sistema SHALL prover, em `apps/frontend`, uma aba **Ferramentas** dentro
da página de detalhe do agente que lista todos os servidores MCP do
catálogo (`GET /mcp-servers`), permite selecionar quais estão vinculados ao
agente e, para cada servidor selecionado, escolher quais tools desse
servidor o agente pode usar, enviando o resultado via
`PUT /agents/{id}/mcp-servers`. A aba SHALL exibir um resumo com a
quantidade de servidores vinculados e de tools permitidas, e, para cada
servidor, a quantidade de tools selecionadas em relação ao total
descoberto. Não SHALL existir página própria para essa gestão.

#### Scenario: Aba carregada com sucesso
- **WHEN** o usuário abre a aba Ferramentas no detalhe de um agente
  existente e o catálogo de servidores MCP não está vazio
- **THEN** a interface exibe a lista de todos os servidores MCP
  cadastrados, cada um com um controle para selecioná-lo, e um resumo com
  a quantidade de servidores vinculados e de tools permitidas

#### Scenario: Catálogo de servidores MCP vazio
- **WHEN** o usuário abre a aba Ferramentas e `GET /mcp-servers` retorna
  uma lista vazia
- **THEN** a interface indica que não há nenhum servidor MCP cadastrado e
  oferece uma ação para cadastrar um servidor, sem exibir a lista de
  seleção nem permitir o submit

#### Scenario: Agente inexistente
- **WHEN** o usuário acessa o detalhe de um `id` que não corresponde a
  nenhum agente cadastrado (`GET /agents/{id}` responde 404)
- **THEN** a interface exibe um estado de "agente não encontrado", sem
  quebrar a navegação do restante da aplicação

#### Scenario: Indicador distingue servidor MCP inativo na lista de seleção
- **WHEN** a lista de servidores MCP disponíveis para seleção inclui um
  servidor com `isActive: false`
- **THEN** a interface exibe, na linha desse servidor, um indicador
  visual de que ele está inativo, mas mantém o controle de seleção
  habilitado

#### Scenario: Servidor vinculado e inativo explica a consequência
- **WHEN** um servidor com `isActive: false` está selecionado como parte
  do vínculo do agente
- **THEN** a interface exibe, junto desse servidor, um aviso de que as
  tools marcadas nele não estão sendo oferecidas ao agente enquanto o
  servidor permanecer inativo

#### Scenario: Contador de tools selecionadas por servidor
- **WHEN** um servidor está selecionado e suas tools já foram descobertas
- **THEN** a interface exibe, na linha desse servidor, quantas tools estão
  marcadas em relação ao total de tools que o servidor oferece

#### Scenario: Servidor não vinculado é identificado como tal
- **WHEN** um servidor do catálogo não faz parte do vínculo do agente
- **THEN** a interface indica, na linha desse servidor, que ele não está
  vinculado, em vez de exibir uma contagem de tools

### Requirement: Pré-seleção de servidores e tools a partir do vínculo atual

O sistema SHALL pré-selecionar, ao abrir a aba Ferramentas, os servidores
MCP presentes em `agent.mcpServers` (retornado por `GET /agents/{id}`),
cada um com o `allowedTools` atual como seleção inicial de tools.

#### Scenario: Servidores já vinculados aparecem pré-selecionados
- **WHEN** o usuário abre a aba Ferramentas de um agente cujo `mcpServers`
  já inclui um ou mais servidores
- **THEN** a interface exibe cada um desses servidores com o controle
  de seleção já marcado, sem exigir nenhuma ação do usuário

#### Scenario: Servidores não vinculados aparecem desmarcados
- **WHEN** o usuário abre a aba Ferramentas de um agente e o catálogo de
  servidores MCP inclui servidores ausentes de `agent.mcpServers`
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
do catálogo de uma vez ao abrir a aba. Selecionar um servidor SHALL, no
mesmo gesto, incluí-lo no vínculo, expandir sua área de tools e disparar
a descoberta quando ainda não houver resultado disponível.

#### Scenario: Abrir a aba não dispara busca de tools de nenhum servidor
- **WHEN** o usuário abre a aba Ferramentas e ainda não expandiu nem
  selecionou nenhum servidor
- **THEN** a interface não envia nenhuma chamada a
  `GET /mcp-servers/{id}/tools`

#### Scenario: Selecionar um servidor vincula, expande e busca suas tools
- **WHEN** o usuário marca o controle de seleção de um servidor MCP ainda
  não vinculado, pela primeira vez
- **THEN** a interface passa a considerá-lo parte do vínculo, exibe sua
  área de tools expandida, envia `GET /mcp-servers/{id}/tools` para esse
  servidor e mostra um indicador de carregamento até a resposta chegar

#### Scenario: Expandir um servidor sem selecioná-lo também busca suas tools
- **WHEN** o usuário expande a área de tools de um servidor que não está
  selecionado, pela primeira vez
- **THEN** a interface envia `GET /mcp-servers/{id}/tools` para esse
  servidor e exibe as tools encontradas em estado desabilitado, sem
  incluir o servidor no vínculo

#### Scenario: Recolher e reabrir um servidor não repete a busca desnecessariamente
- **WHEN** o usuário recolhe um servidor cujas tools já foram
  descobertas com sucesso e o expande novamente
- **THEN** a interface reutiliza o resultado já obtido, sem exigir uma
  nova chamada a `GET /mcp-servers/{id}/tools`

### Requirement: Submit do vínculo via PUT /agents/{id}/mcp-servers

Ao acionar a ação de salvar, a interface SHALL enviar
`PUT /agents/{id}/mcp-servers` com `{ mcpServers: [...] }`, contendo um
item `{ mcpServerId, allowedTools }` para cada servidor selecionado —
nunca omitindo o campo `mcpServers` nem enviando-o como `null`. Em caso de
sucesso, a interface SHALL exibir uma notificação de sucesso, descartar o
rascunho e **manter o usuário na aba Ferramentas**, refletindo o vínculo
salvo, sem navegar para outra tela.

#### Scenario: Submit com um ou mais servidores selecionados
- **WHEN** o usuário aciona a ação de salvar com um ou mais servidores
  selecionados, cada um com seu `allowedTools`
- **THEN** a interface envia `PUT /agents/{id}/mcp-servers` com
  `mcpServers` contendo um item por servidor selecionado e o
  `allowedTools` correspondente, exibe uma notificação de sucesso e
  permanece na aba Ferramentas do agente

#### Scenario: Submit sem nenhum servidor selecionado remove todos os vínculos
- **WHEN** o usuário aciona a ação de salvar sem nenhum servidor
  selecionado (incluindo o caso de um agente que já tinha servidores
  vinculados e todos foram desmarcados)
- **THEN** a interface envia `PUT /agents/{id}/mcp-servers` com
  `mcpServers: []`, exibe uma notificação de sucesso e permanece na aba
  Ferramentas do agente

#### Scenario: Progresso da validação exibido sem contagem por servidor
- **WHEN** o submit do vínculo está em andamento
- **THEN** a interface exibe um indicador de que as tools estão sendo
  validadas nos servidores MCP, sem afirmar em qual servidor a validação
  está, e mantém a ação de salvar inerte até a resposta chegar

## ADDED Requirements

### Requirement: Aviso de servidor vinculado sem nenhuma tool

O sistema SHALL sinalizar, na aba Ferramentas, todo servidor MCP que está
vinculado ao agente com `allowedTools` vazio, tanto na linha do servidor
quanto em um aviso no topo da aba que nomeia os servidores nessa
situação, deixando explícito que nenhuma ferramenta desse servidor será
oferecida ao modelo em runtime.

#### Scenario: Servidor vinculado sem nenhuma tool marcada é sinalizado
- **WHEN** um servidor está selecionado como parte do vínculo e nenhuma
  das suas tools está marcada
- **THEN** a interface exibe um indicador visual na linha desse servidor e
  um aviso no topo da aba nomeando-o, explicando que nenhuma ferramenta
  dele será oferecida ao modelo

#### Scenario: Vários servidores vinculados sem tools são nomeados juntos
- **WHEN** mais de um servidor está selecionado sem nenhuma tool marcada
- **THEN** o aviso no topo da aba nomeia todos eles

#### Scenario: Nenhum aviso quando todo servidor vinculado tem ao menos uma tool
- **WHEN** todos os servidores selecionados têm ao menos uma tool marcada
- **THEN** a interface não exibe o aviso de vínculo sem tools, nem no topo
  da aba nem em nenhuma linha

#### Scenario: Servidor não vinculado sem tools marcadas não gera aviso
- **WHEN** um servidor não está selecionado e, portanto, não tem tools
  marcadas
- **THEN** a interface não o trata como vínculo sem tools nem o nomeia no
  aviso

### Requirement: Barra de alterações não salvas no vínculo

O sistema SHALL exibir, na aba Ferramentas, uma barra fixa de alterações
não salvas somente enquanto o rascunho diferir do vínculo salvo do agente,
com uma ação para descartar as alterações e uma ação para salvá-las. A
comparação SHALL ser feita sobre o conjunto de servidores e tools
independentemente da ordem em que foram marcados.

#### Scenario: Sem alterações, a barra não aparece
- **WHEN** o usuário abre a aba Ferramentas e não altera nada
- **THEN** a interface não exibe a barra de alterações não salvas

#### Scenario: Alterar a seleção faz a barra aparecer
- **WHEN** o usuário marca ou desmarca um servidor, ou marca ou desmarca
  uma tool, deixando o rascunho diferente do vínculo salvo
- **THEN** a interface exibe a barra de alterações não salvas, com uma
  ação para descartar e uma ação para salvar

#### Scenario: Desfazer manualmente as alterações faz a barra sumir
- **WHEN** o usuário altera a seleção e depois a devolve, por ações
  próprias, exatamente ao conjunto salvo do agente
- **THEN** a interface deixa de exibir a barra de alterações não salvas,
  mesmo que a ordem em que as tools foram marcadas seja diferente da
  original

#### Scenario: Descartar devolve o rascunho ao vínculo salvo
- **WHEN** o usuário aciona a ação de descartar na barra de alterações
  não salvas
- **THEN** a interface restaura a seleção de servidores e tools para o
  vínculo salvo do agente, sem enviar nenhuma requisição a
  `PUT /agents/{id}/mcp-servers`, e a barra deixa de ser exibida

### Requirement: Aviso ao sair com alterações não salvas no vínculo

O sistema SHALL avisar o usuário antes de descartar um rascunho de vínculo
não salvo, quando ele tenta trocar de aba, sair da página de detalhe do
agente ou fechar a janela, oferecendo a opção de permanecer editando.

#### Scenario: Trocar de aba com alterações não salvas pede confirmação
- **WHEN** o usuário tem alterações não salvas na aba Ferramentas e
  aciona outra aba do detalhe do agente
- **THEN** a interface exibe um pedido de confirmação antes de trocar de
  aba, informando que as alterações serão descartadas

#### Scenario: Cancelar a confirmação mantém o rascunho e a aba
- **WHEN** o usuário, no pedido de confirmação, escolhe continuar editando
- **THEN** a interface permanece na aba Ferramentas com o rascunho
  intacto, e a URL continua apontando para essa aba

#### Scenario: Confirmar a saída descarta o rascunho
- **WHEN** o usuário, no pedido de confirmação, escolhe sair mesmo assim
- **THEN** a interface navega para o destino pretendido e o rascunho é
  descartado

#### Scenario: Sair da página de detalhe com alterações não salvas pede confirmação
- **WHEN** o usuário tem alterações não salvas na aba Ferramentas e
  aciona um link que leva para fora da página de detalhe do agente
- **THEN** a interface exibe o mesmo pedido de confirmação antes de sair

#### Scenario: Sem alterações, a navegação não é interrompida
- **WHEN** o usuário não tem alterações não salvas e troca de aba ou sai
  da página
- **THEN** a interface navega diretamente, sem exibir nenhum pedido de
  confirmação

### Requirement: Redirecionamento da rota antiga de gestão do vínculo

O sistema SHALL redirecionar acessos a `/agents/{id}/mcp-servers` para a
aba Ferramentas do detalhe desse agente, de forma que links salvos
anteriormente continuem funcionando, sem renderizar nenhuma página de
gestão separada.

#### Scenario: Acesso à rota antiga leva à aba Ferramentas
- **WHEN** o usuário acessa `/agents/{id}/mcp-servers`
- **THEN** a interface o leva para a página de detalhe desse agente com a
  aba Ferramentas ativa, sem exibir nenhuma página de gestão do vínculo
  separada
