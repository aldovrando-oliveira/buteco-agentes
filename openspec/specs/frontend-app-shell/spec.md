# frontend-app-shell Specification

## Purpose

TBD - defined by change frontend-shell-navegacao-e-icones. Update Purpose after archive.
## Requirements
### Requirement: Barra lateral única como casca do painel
O sistema SHALL renderizar, em `apps/frontend`, uma casca composta por uma
única barra lateral fixa à esquerda e uma área principal que exibe o conteúdo
da rota atual. A casca SHALL NOT ter uma barra superior separada.

A barra lateral SHALL conter três regiões distintas, na ordem: a identificação
do produto no topo, a navegação no meio, e os controles do operador no rodapé.

#### Scenario: A casca não tem barra superior
- **WHEN** o operador acessa qualquer rota da aplicação
- **THEN** a página exibe a barra lateral e a área principal, sem nenhuma
  barra superior com o nome do produto ou controles

#### Scenario: Navegar entre rotas preserva a barra lateral
- **WHEN** o operador navega entre páginas diferentes da aplicação
- **THEN** a barra lateral permanece visível e não é recarregada, mudando
  apenas o conteúdo da área principal

#### Scenario: A identificação do produto aparece uma vez
- **WHEN** o operador visualiza a casca em qualquer rota
- **THEN** o nome do produto aparece uma única vez, no topo da barra lateral,
  acompanhado da marca de identidade

### Requirement: Navegação com ícone e estado ativo por grupo de rotas
O sistema SHALL exibir, na região de navegação da barra lateral, um item para
cada área do painel que possua página real, cada um com um ícone e um rótulo
textual.

Um item SHALL ser apresentado como ativo enquanto a rota atual pertencer ao seu
grupo, incluindo rotas profundas de detalhe, criação e edição — e não apenas a
rota raiz do grupo. O item ativo SHALL ser distinguido pela cor de destaque, no
texto e no fundo, e SHALL NOT ocupar a largura inteira da barra.

Os ícones SHALL ser decorativos: o rótulo textual permanece a fonte do nome
acessível do item.

#### Scenario: Cada item de navegação tem ícone e rótulo
- **WHEN** o operador visualiza a navegação da barra lateral
- **THEN** cada item exibe um ícone junto do seu rótulo textual, e o nome
  acessível do item é o rótulo, não o ícone

#### Scenario: O item permanece ativo em rota profunda
- **WHEN** o operador acessa uma rota de detalhe, criação ou edição dentro de
  uma área do painel
- **THEN** o item de navegação daquela área continua marcado como ativo

#### Scenario: Navegação não lista itens sem página correspondente
- **WHEN** o operador visualiza a navegação da barra lateral
- **THEN** só aparecem itens que apontam para uma rota existente na aplicação

### Requirement: Controle de tema no rodapé da barra lateral
O sistema SHALL posicionar o controle de alternância de esquema de cor no
rodapé da barra lateral, preservando o comportamento já especificado em
`frontend-scaffold`: alternância manual, persistência entre sessões e rótulo
que descreve o esquema efetivo.

O rodapé SHALL NOT exibir nome, endereço de e-mail ou avatar do operador
enquanto o backend não fornecer essa identidade — o resultado da autenticação
devolve apenas credencial e validade.

#### Scenario: O controle de tema vive no rodapé
- **WHEN** o operador visualiza a barra lateral
- **THEN** o controle de alternância de esquema de cor aparece no rodapé da
  barra, abaixo da navegação

#### Scenario: A alternância continua funcionando após a mudança de lugar
- **WHEN** o operador aciona o controle de tema a partir do rodapé e recarrega
  a aplicação
- **THEN** a interface é renderizada no esquema escolhido anteriormente

#### Scenario: O rodapé não afirma a identidade do operador
- **WHEN** o operador visualiza o rodapé da barra lateral
- **THEN** nenhum nome, e-mail ou avatar é exibido

### Requirement: Ícones vindos de uma biblioteca compartilhada
O sistema SHALL obter os ícones da interface de uma biblioteca de ícones
declarada como dependência de `apps/frontend`, e SHALL NOT manter ícones
transcritos como SVG dentro de componentes de feature ou de layout.

A marca do produto SHALL ser tratada à parte dos ícones de interface: ela não
vem de biblioteca de ícones, e SHALL vir de arquivos de marca versionados em
`apps/frontend`, consumidos através do componente de marca definido em
`frontend-brand-identity`. A proibição de SVG transcrito à mão permanece nos
dois casos — nem ícone nem marca SHALL ter sua geometria declarada dentro de um
componente de feature ou de layout.

#### Scenario: Nenhum ícone desenhado à mão permanece
- **WHEN** o código de `apps/frontend` é inspecionado em busca de elementos SVG
  declarados diretamente em componentes
- **THEN** nenhum componente de feature ou de layout declara um ícone como SVG
  próprio; todos vêm da biblioteca de ícones

#### Scenario: A marca não é confundida com um ícone de interface
- **WHEN** a identificação do produto no topo da barra lateral é inspecionada
- **THEN** a marca vem do componente de marca, alimentado por arquivo de marca
  versionado, e não da biblioteca de ícones nem de SVG declarado no componente
  de layout

