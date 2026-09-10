## MODIFIED Requirements

### Requirement: Abas do detalhe do agente

O sistema SHALL organizar o conteúdo da página de detalhe do agente em quatro
abas — visão geral, ferramentas, conhecimento e delegações, nessa ordem —
exibindo, nas três últimas, um contador com a quantidade de servidores MCP
vinculados, de bases de conhecimento vinculadas e de agentes-alvo de delegação,
oculto quando a quantidade é zero. A aba ativa SHALL ser refletida na URL, de
forma que o endereço seja compartilhável e sobreviva a um recarregamento da
página. Apenas o conteúdo da aba ativa SHALL estar presente na página.

#### Scenario: Quatro abas exibidas no detalhe do agente
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe as abas de visão geral, ferramentas, conhecimento
  e delegações, nessa ordem, com a visão geral ativa

#### Scenario: Contadores refletem os vínculos do agente
- **WHEN** o agente exibido tem um ou mais servidores MCP em `mcpServers`, uma
  ou mais bases em `knowledgeBases`, ou um ou mais agentes em `delegatesTo`
- **THEN** a interface exibe, junto do rótulo da aba correspondente, a
  quantidade de itens de cada vínculo

#### Scenario: Contador oculto quando o vínculo está vazio
- **WHEN** o agente exibido tem `mcpServers` vazio, `knowledgeBases` vazio ou
  `delegatesTo` vazio
- **THEN** a interface não exibe contador junto do rótulo da aba
  correspondente

#### Scenario: Aba ativa refletida na URL
- **WHEN** o usuário aciona a aba de ferramentas, a de conhecimento ou a de
  delegações
- **THEN** a URL passa a identificar a aba ativa, e recarregar a página
  nesse endereço reabre a mesma aba

#### Scenario: Endereço sem identificação de aba abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente sem nenhuma aba
  identificada na URL
- **THEN** a interface exibe a aba de visão geral, sem alterar o endereço

#### Scenario: Identificação de aba desconhecida abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente com uma
  identificação de aba que não corresponde a nenhuma das quatro
- **THEN** a interface exibe a aba de visão geral, sem quebrar a página e
  sem exibir erro

#### Scenario: Conteúdo da aba inativa não está presente na página
- **WHEN** o usuário está em uma das abas do detalhe do agente
- **THEN** o conteúdo das outras abas não está presente na página, e
  passa a existir apenas quando a aba correspondente é acionada
