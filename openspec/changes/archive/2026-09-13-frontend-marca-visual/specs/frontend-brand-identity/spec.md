## ADDED Requirements

### Requirement: Marca servida por um componente único
`apps/frontend` SHALL expor um único componente de marca, e toda tela que
exibir a marca do produto SHALL consumi-lo. Nenhum componente de feature, de
layout ou de página SHALL declarar a geometria da marca por conta própria.

O desenho da marca SHALL vir de arquivos versionados no repositório, e não de
uma biblioteca de ícones de terceiros nem de transcrição manual dentro de um
componente.

#### Scenario: A marca aparece por composição, não por transcrição
- **WHEN** o código de `apps/frontend` é inspecionado em busca da geometria da
  marca
- **THEN** ela aparece somente nos arquivos de marca versionados e no componente
  que os consome; nenhuma tela declara caminhos vetoriais próprios

#### Scenario: Trocar o desenho da marca não toca em tela
- **WHEN** os arquivos de marca versionados são substituídos por uma revisão
  nova do mesmo desenho
- **THEN** nenhuma página, componente de feature ou componente de layout precisa
  ser editado para refletir a troca

### Requirement: A marca acompanha o esquema de cor
O sistema SHALL renderizar a marca nas cores do esquema de cor efetivo, tanto no
claro quanto no escuro, sem que a tela que a exibe declare cor ou consulte o
esquema atual.

O acento da marca SHALL ser a mesma cor de destaque que o restante do painel usa
naquele esquema, de modo que a marca e os elementos de destaque vizinhos — como
o item de navegação ativo — não exibam tons divergentes lado a lado.

#### Scenario: A marca troca de cor junto com o painel
- **WHEN** o operador alterna o esquema de cor do painel
- **THEN** a marca é renderizada nas cores correspondentes ao novo esquema, sem
  recarregar a página

#### Scenario: O acento da marca é o acento do painel
- **WHEN** a marca é exibida na barra lateral junto de um item de navegação
  ativo, em qualquer esquema de cor
- **THEN** a cor de acento da marca e a cor de destaque do item ativo são a
  mesma

### Requirement: Guarda de legibilidade por tamanho
O componente de marca SHALL aceitar o tamanho de renderização e SHALL degradar a
peça exibida quando o tamanho pedido estiver abaixo do mínimo em que aquela peça
é legível.

Abaixo do limiar em que as duas cores da marca deixam de construir forma, o
componente SHALL renderizar uma versão de cor única em vez da versão de duas
cores — mesmo que a chamada peça a versão de duas cores. A guarda SHALL viver no
componente, e não na decisão de cada tela.

#### Scenario: Duotone pedido abaixo do limiar cai para cor única
- **WHEN** uma tela solicita a versão de duas cores da marca em um tamanho
  abaixo do limiar de legibilidade
- **THEN** o componente renderiza a versão de cor única, sem erro e sem exigir
  mudança na chamada

#### Scenario: Acima do limiar a versão pedida é respeitada
- **WHEN** uma tela solicita a versão de duas cores da marca em um tamanho igual
  ou acima do limiar
- **THEN** o componente renderiza a versão de duas cores

### Requirement: A marca não duplica o nome do produto para leitores de tela
Quando a marca for exibida acompanhada do nome do produto em texto, ela SHALL
ser apresentada como decorativa, de modo que tecnologias assistivas anunciem o
nome do produto uma única vez.

#### Scenario: O nome do produto é anunciado uma vez na barra lateral
- **WHEN** um leitor de tela percorre a identificação do produto no topo da
  barra lateral, onde a marca aparece ao lado do nome em texto
- **THEN** o nome do produto é anunciado uma única vez

#### Scenario: O nome do produto é anunciado uma vez na tela de login
- **WHEN** um leitor de tela percorre o cabeçalho da tela de login, onde a marca
  aparece acima do nome do produto em texto
- **THEN** o nome do produto é anunciado uma única vez

### Requirement: Identidade do documento e do ícone de aplicativo
`apps/frontend` SHALL identificar o produto fora da área de renderização da
aplicação: o título do documento SHALL nomear o produto, o ícone do site SHALL
ser a marca do produto, e o documento SHALL declarar a cor de destaque da marca
como cor de tema do navegador.

O ícone do site e o ícone de aplicativo SHALL ser peças desenhadas para o
tamanho em que são exibidos, e SHALL NOT ser a peça principal da marca reduzida.

#### Scenario: A aba do navegador identifica o produto
- **WHEN** o painel é aberto em um navegador
- **THEN** a aba exibe o nome do produto e o ícone da marca, e não um título ou
  ícone remanescente do scaffold do projeto

#### Scenario: O ícone de aplicativo existe em peça própria
- **WHEN** o painel é adicionado à tela inicial de um dispositivo ou instalado
  como aplicativo
- **THEN** o ícone usado é a peça de ícone de aplicativo da marca, e não o
  símbolo isolado
