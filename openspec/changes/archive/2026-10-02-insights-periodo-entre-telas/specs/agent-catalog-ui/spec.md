## MODIFIED Requirements

### Requirement: Abas do detalhe do agente

O sistema SHALL organizar o conteúdo da página de detalhe do agente em cinco
abas — visão geral, ferramentas, conhecimento, delegações e insights, nessa
ordem — exibindo, na segunda, na terceira e na quarta, um contador com a
quantidade de servidores MCP vinculados, de bases de conhecimento vinculadas e
de agentes-alvo de delegação, oculto quando a quantidade é zero.

A aba de insights SHALL NOT exibir contador: ela não representa um vínculo, e um
número ao lado do rótulo afirmaria uma quantidade que a aba não tem.

A aba ativa SHALL ser refletida na URL, de forma que o endereço seja
compartilhável e sobreviva a um recarregamento da página. Apenas o conteúdo da
aba ativa SHALL estar presente na página.

**A identificação da aba é UMA CHAVE do endereço, e não o endereço inteiro.**
Trocar de aba SHALL alterar apenas a identificação da aba e SHALL preservar os
demais parâmetros do endereço, inclusive ao voltar para a visão geral — que
remove a identificação da aba e SHALL NOT remover o resto. Substituir a busca
inteira a cada troca de aba SHALL NOT ocorrer: ela apagaria em silêncio qualquer
outro parâmetro que a página carregue, e o sintoma seria uma escolha do operador
perdida sem aviso.

#### Scenario: Cinco abas exibidas no detalhe do agente
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe as abas de visão geral, ferramentas, conhecimento,
  delegações e insights, nessa ordem, com a visão geral ativa

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

#### Scenario: A aba de insights nunca exibe contador
- **WHEN** o usuário acessa a página de detalhe de qualquer agente
- **THEN** **nenhum** contador aparece junto do rótulo da aba de insights,
  qualquer que seja a quantidade de vínculos ou de métricas do agente

#### Scenario: Aba ativa refletida na URL
- **WHEN** o usuário aciona a aba de ferramentas, a de conhecimento, a de
  delegações ou a de insights
- **THEN** a URL passa a identificar a aba ativa, e recarregar a página
  nesse endereço reabre a mesma aba

#### Scenario: Endereço sem identificação de aba abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente sem nenhuma aba
  identificada na URL
- **THEN** a interface exibe a aba de visão geral, sem alterar o endereço

#### Scenario: Identificação de aba desconhecida abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente com uma
  identificação de aba que não corresponde a nenhuma das cinco
- **THEN** a interface exibe a aba de visão geral, sem quebrar a página e
  sem exibir erro

#### Scenario: Conteúdo da aba inativa não está presente na página
- **WHEN** o usuário está em uma das abas do detalhe do agente
- **THEN** o conteúdo das outras abas não está presente na página, e
  passa a existir apenas quando a aba correspondente é acionada

#### Scenario: Trocar de aba preserva os demais parâmetros do endereço
- **WHEN** o endereço identifica uma aba e também outro parâmetro, e o usuário
  aciona outra aba
- **THEN** a identificação da aba passa a ser a nova, e o outro parâmetro
  **permanece** no endereço

#### Scenario: Voltar à visão geral remove só a identificação da aba
- **WHEN** o endereço identifica uma aba e também outro parâmetro, e o usuário
  aciona a visão geral
- **THEN** a identificação da aba sai do endereço, e o outro parâmetro
  **permanece**
