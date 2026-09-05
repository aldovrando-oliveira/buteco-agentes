## MODIFIED Requirements

### Requirement: Detalhe de agente
O sistema SHALL prover, em `apps/frontend`, uma página que exibe os dados
completos de um agente consumindo `GET /agents/{id}`, incluindo seu estado
(`isActive`), o provider e o model configurados, um indicador de que o
agente precisa de reconfiguração quando o provider ou o model estão
ausentes, a descrição do agente (ou uma indicação explícita de que não há
descrição), a lista de skills do agente (nome e, quando houver, descrição
de cada uma; ou uma indicação explícita de que não há skills), as
instruções e as datas de criação e atualização, com um link para editar o
agente e uma ação para ativá-lo ou desativá-lo. O nome, o estado, a
descrição, o link de edição e a ação de ativar/desativar SHALL ser
exibidos em um cabeçalho comum a todas as abas da página (ver requisito
"Abas do detalhe do agente"), e o cabeçalho SHALL vir antes, na ordem do
documento, dos dados exibidos nas abas. A página SHALL NOT exibir link
para nenhuma página separada de gestão do vínculo com servidores MCP, nem
resumo textual dos servidores vinculados, porque essa informação passa a
ser responsabilidade da aba de ferramentas. O campo de instruções SHALL
ser renderizado interpretando sua sintaxe markdown (títulos, listas,
negrito, tabelas, texto riscado, `---` como separador) como formatação
real, dentro de um container que usa o espaço vertical disponível da
viewport (sem exceder esse espaço) e que nunca fica menor que uma altura
mínima utilizável, com rolagem interna própria quando o conteúdo excede o
espaço disponível — de forma que um conteúdo de instruções longo não faça
a página inteira crescer indefinidamente, nem fique menor do que o
necessário para ser lido confortavelmente.

#### Scenario: Detalhe carregado com sucesso
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe nome, descrição, um indicador do estado
  (`isActive`) do agente, um link para editar o agente e uma ação para
  ativá-lo ou desativá-lo no cabeçalho, e exibe instruções, skills, o
  provider e o model configurados e as datas de criação e atualização na
  aba de visão geral

#### Scenario: Descrição exibida quando presente
- **WHEN** o agente exibido tem `description` não nula
- **THEN** a interface exibe o texto da descrição junto ao nome do agente

#### Scenario: Ausência de descrição indicada explicitamente
- **WHEN** o agente exibido tem `description` nula
- **THEN** a interface exibe uma indicação de que o agente não tem
  descrição, em vez de um espaço vazio

#### Scenario: Skills listadas com nome e descrição
- **WHEN** o agente exibido tem uma ou mais entradas em `skills`
- **THEN** a interface exibe o nome de cada skill e, para as que têm
  `description` não nula, também a descrição

#### Scenario: Ausência de skills indicada explicitamente
- **WHEN** o agente exibido tem `skills` vazio
- **THEN** a interface exibe uma indicação de que nenhuma skill foi
  declarada, em vez de uma lista vazia sem explicação

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

#### Scenario: Cabeçalho exibido antes do conteúdo das abas
- **WHEN** a página de detalhe de um agente é renderizada
- **THEN** o nome, o link para editar e a ação de ativar/desativar
  aparecem, na ordem do documento, antes do conteúdo da aba ativa

#### Scenario: Nenhum link para página separada de vínculo com servidores MCP
- **WHEN** a página de detalhe de um agente é renderizada, com ou sem
  servidores MCP vinculados
- **THEN** a interface não exibe nenhum link para uma página separada de
  gestão do vínculo com servidores MCP

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

## ADDED Requirements

### Requirement: Abas do detalhe do agente

O sistema SHALL organizar o conteúdo da página de detalhe do agente em três
abas — visão geral, ferramentas e delegações — exibindo, nas duas últimas,
um contador com a quantidade de servidores MCP vinculados e de agentes-alvo
de delegação, oculto quando a quantidade é zero. A aba ativa SHALL ser
refletida na URL, de forma que o endereço seja compartilhável e sobreviva a
um recarregamento da página. Apenas o conteúdo da aba ativa SHALL estar
presente na página.

#### Scenario: Três abas exibidas no detalhe do agente
- **WHEN** o usuário acessa a página de detalhe de um agente existente
- **THEN** a interface exibe as abas de visão geral, ferramentas e
  delegações, com a visão geral ativa

#### Scenario: Contadores refletem os vínculos do agente
- **WHEN** o agente exibido tem um ou mais servidores MCP em `mcpServers`
  ou um ou mais agentes em `delegatesTo`
- **THEN** a interface exibe, junto do rótulo da aba correspondente, a
  quantidade de itens de cada vínculo

#### Scenario: Contador oculto quando o vínculo está vazio
- **WHEN** o agente exibido tem `mcpServers` vazio ou `delegatesTo` vazio
- **THEN** a interface não exibe contador junto do rótulo da aba
  correspondente

#### Scenario: Aba ativa refletida na URL
- **WHEN** o usuário aciona a aba de ferramentas ou a de delegações
- **THEN** a URL passa a identificar a aba ativa, e recarregar a página
  nesse endereço reabre a mesma aba

#### Scenario: Endereço sem identificação de aba abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente sem nenhuma aba
  identificada na URL
- **THEN** a interface exibe a aba de visão geral, sem alterar o endereço

#### Scenario: Identificação de aba desconhecida abre a visão geral
- **WHEN** o usuário acessa a página de detalhe do agente com uma
  identificação de aba que não corresponde a nenhuma das três
- **THEN** a interface exibe a aba de visão geral, sem quebrar a página e
  sem exibir erro

#### Scenario: Conteúdo da aba inativa não está presente na página
- **WHEN** o usuário está em uma das abas do detalhe do agente
- **THEN** o conteúdo das outras abas não está presente na página, e
  passa a existir apenas quando a aba correspondente é acionada
