## MODIFIED Requirements

### Requirement: Detalhe de agente

O sistema SHALL prover, em `apps/frontend`, uma página que exibe os dados
completos de um agente consumindo `GET /agents/{id}`, incluindo seu estado
(`isActive`), o provider e o model configurados, um indicador de que o
agente precisa de reconfiguração quando o provider ou o model estão
ausentes, um resumo dos servidores MCP vinculados ao agente
(`agent.mcpServers`), com um link para editar o agente, um link para a
página de gestão do vínculo com servidores MCP (`/agents/{id}/mcp-servers`)
e uma ação para ativá-lo ou desativá-lo. O bloco com o link de edição e a
ação de ativar/desativar SHALL ser exibido antes, na ordem do documento,
dos dados do agente (nome, instruções, datas de criação e atualização). O
campo de instruções SHALL ser renderizado interpretando sua sintaxe
markdown (títulos, listas, negrito, tabelas, texto riscado, `---` como
separador) como formatação real, dentro de um container que usa o espaço
vertical disponível da viewport (sem exceder esse espaço) e que nunca fica
menor que uma altura mínima utilizável, com rolagem interna própria quando
o conteúdo excede o espaço disponível — de forma que um conteúdo de
instruções longo não faça a página inteira crescer indefinidamente, nem
fique menor do que o necessário para ser lido confortavelmente.

#### Scenario: Detalhe carregado com sucesso
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe nome, instruções, as datas de criação e
  atualização, um indicador do estado (`isActive`) do agente, o provider e
  o model configurados, um resumo dos servidores MCP vinculados, um link
  para editar o agente, um link para a página de gestão do vínculo com
  servidores MCP e uma ação para ativá-lo ou desativá-lo

#### Scenario: Agente inexistente
- **WHEN** o usuário acessa a página de detalhe de um id que não
  corresponde a nenhum agente cadastrado (`GET /agents/{id}` responde 404)
- **THEN** a interface exibe um estado de "agente não encontrado", sem
  quebrar a navegação do restante da aplicação

#### Scenario: Ação de ativar exibida para agente inativo
- **WHEN** o agente exibido tem `isActive: false`
- **THEN** a interface exibe uma ação para ativá-lo, e não exibe uma ação
  para desativá-lo

#### Scenario: Ação de desativar exibida para agente ativo
- **WHEN** o agente exibido tem `isActive: true`
- **THEN** a interface exibe uma ação para desativá-lo, e não exibe uma
  ação para ativá-lo

#### Scenario: Bloco de ações exibido antes dos dados do agente
- **WHEN** a página de detalhe de um agente é renderizada
- **THEN** o link para editar e a ação de ativar/desativar aparecem, na
  ordem do documento, antes do nome, das instruções e das datas do agente

#### Scenario: Instruções renderizadas como markdown formatado
- **WHEN** o campo `instructions` do agente contém sintaxe markdown (por
  exemplo, um título iniciado com `#`, texto em `**negrito**`, ou um item
  de lista iniciado com `-`)
- **THEN** a interface renderiza esses elementos como formatação real
  (por exemplo, o título vira um elemento de heading real), em vez de
  exibir a sintaxe markdown como texto literal

#### Scenario: Bloco de instruções usa o espaço vertical disponível, com rolagem quando excede
- **WHEN** o conteúdo renderizado das instruções excede o espaço vertical
  disponível na viewport para o container de instruções
- **THEN** a interface exibe uma rolagem interna própria desse container,
  sem que o restante da página cresça além da altura necessária para o
  conteúdo

#### Scenario: Bloco de instruções mantém altura mínima utilizável
- **WHEN** o espaço vertical disponível na viewport para o container de
  instruções é pequeno (ex.: janela do navegador baixa ou nível de zoom
  alto)
- **THEN** a interface mantém uma altura mínima para o container de
  instruções, mesmo que isso exija rolagem da página para ver o restante
  do conteúdo abaixo dele

#### Scenario: Indicador de reconfiguração exibido quando provider ou model ausentes
- **WHEN** o agente exibido tem `provider` nulo ou `model` nulo
- **THEN** a interface exibe um indicador visual de que o agente precisa
  de reconfiguração

#### Scenario: Indicador de reconfiguração ausente quando provider e model configurados
- **WHEN** o agente exibido tem `provider` e `model` preenchidos
- **THEN** a interface não exibe o indicador de reconfiguração

#### Scenario: Resumo lista os nomes dos servidores MCP vinculados
- **WHEN** o agente exibido tem um ou mais servidores em `mcpServers`
- **THEN** a interface exibe o nome de cada servidor MCP vinculado no
  resumo da página de detalhe

#### Scenario: Resumo indica ausência de vínculo quando nenhum servidor MCP está vinculado
- **WHEN** o agente exibido tem `mcpServers` vazio
- **THEN** a interface exibe uma indicação de que nenhum servidor MCP está
  vinculado, em vez de uma lista vazia sem explicação

#### Scenario: Link para a página de gestão do vínculo sempre presente
- **WHEN** a página de detalhe de um agente é renderizada, com ou sem
  servidores MCP vinculados
- **THEN** a interface exibe um link para `/agents/{id}/mcp-servers`
