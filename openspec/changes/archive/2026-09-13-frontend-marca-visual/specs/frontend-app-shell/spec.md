## MODIFIED Requirements

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
