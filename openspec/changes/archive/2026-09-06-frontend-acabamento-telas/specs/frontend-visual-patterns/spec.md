## ADDED Requirements

### Requirement: Card com cabeçalho em faixa e linhas divididas
O sistema SHALL prover, em `apps/frontend`, um componente compartilhado para o
padrão de card composto por um cabeçalho destacado sobre superfície própria e
uma sequência de linhas separadas entre si por borda.

O componente SHALL ser a única fonte da borda, do fundo do cabeçalho e do
espaçamento interno dessas regiões: quem o utiliza fornece o conteúdo do
cabeçalho e das linhas, e SHALL NOT declarar borda, fundo ou padding próprios
para obter esse padrão.

Toda tela que hoje apresenta esse padrão — as listagens em tabela, o catálogo
de tools do servidor MCP e os cards de detalhe com linhas — SHALL consumir esse
componente.

#### Scenario: O cabeçalho se distingue das linhas
- **WHEN** o operador visualiza um card que segue esse padrão
- **THEN** o cabeçalho aparece sobre superfície distinta da das linhas e
  separado delas por uma borda

#### Scenario: As linhas são separadas entre si
- **WHEN** um card desse padrão exibe mais de uma linha
- **THEN** cada linha é separada da seguinte por uma borda

#### Scenario: Uma linha só não produz borda solta
- **WHEN** um card desse padrão exibe uma única linha
- **THEN** não aparece borda abaixo dela além da do próprio card

### Requirement: Rótulo de seção em maiúsculas
O sistema SHALL prover um componente compartilhado para o rótulo de seção em
caixa alta usado nos cabeçalhos de card, com o tom neutro terciário da
identidade visual e espaçamento entre letras.

O rótulo SHALL usar um tom mais claro que o do texto secundário do painel, para
recuar em relação ao conteúdo que encabeça, e SHALL NOT ser reproduzido por
cópia local em cada tela.

#### Scenario: O rótulo recua em relação ao conteúdo
- **WHEN** o operador visualiza um card com rótulo de seção
- **THEN** o rótulo aparece em tom mais claro que o texto secundário do corpo
  do card

#### Scenario: Todos os rótulos de seção têm a mesma aparência
- **WHEN** o operador percorre telas diferentes que possuem rótulo de seção
- **THEN** todos apresentam o mesmo tom, peso, caixa e espaçamento entre letras

### Requirement: Cabeçalho de página de detalhe com volta para a listagem
O sistema SHALL prover um componente compartilhado de cabeçalho para as páginas
de detalhe, contendo a navegação de volta para a listagem de origem, o título
do registro, o seu estado e a sua descrição.

Toda tela que não é listagem SHALL oferecer navegação de volta, sem depender do
histórico do navegador nem da barra lateral: as páginas de detalhe e de criação
voltam para a listagem correspondente, e as páginas de edição voltam para o
registro que está sendo editado.

A navegação de volta SHALL apontar para uma rota fixa, e não para a entrada
anterior do histórico, para funcionar também em acesso direto pela URL.

#### Scenario: O detalhe oferece volta para a sua listagem
- **WHEN** o operador acessa a página de detalhe de um agente, de um servidor
  MCP ou de um canal
- **THEN** o cabeçalho exibe um link que leva à listagem correspondente,
  nomeando-a

#### Scenario: A volta funciona em acesso direto pela URL
- **WHEN** o operador abre uma página de detalhe diretamente pelo endereço, sem
  ter passado pela listagem
- **THEN** o link de volta continua levando à listagem correspondente

#### Scenario: A criação volta para a listagem
- **WHEN** o operador está numa tela de cadastro de agente, de servidor MCP ou
  de canal
- **THEN** o cabeçalho exibe um link que leva à listagem correspondente

#### Scenario: A edição volta para o registro editado
- **WHEN** o operador está numa tela de edição de um registro
- **THEN** o cabeçalho exibe um link que leva ao detalhe daquele registro, e não
  à listagem

### Requirement: Campo de busca sem rótulo visível
O sistema SHALL apresentar os campos de busca das listagens sem rótulo visível,
usando o texto de exemplo para comunicar o que a busca alcança, e SHALL manter
esse texto como nome acessível do campo.

Os controles de busca e de filtro de uma listagem SHALL ocupar a mesma linha.

#### Scenario: A busca é alcançável sem rótulo visível
- **WHEN** o campo de busca de uma listagem é procurado pelo seu nome
  acessível
- **THEN** o campo é encontrado, sem que exista um rótulo visível acima dele

#### Scenario: Busca e filtro dividem uma linha
- **WHEN** o operador visualiza uma listagem que possui busca e filtro
- **THEN** os dois controles aparecem lado a lado, na mesma linha
