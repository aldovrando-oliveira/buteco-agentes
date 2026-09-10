## MODIFIED Requirements

### Requirement: Agentes que consultam a base
O sistema SHALL exibir, no detalhe da base, quais agentes a consultam, com o nome
de cada um como link para o detalhe do agente e o estado dele. A relação SHALL
ser derivada no cliente a partir de `GET /agents`, que já devolve as bases
vinculadas a cada agente — não existe consulta inversa na API, e isso é decisão
registrada do backend.

Quando nenhum agente consulta a base, o sistema SHALL dizer isso e SHALL
indicar onde o vínculo é feito — a aba Conhecimento do detalhe do agente. A
proibição anterior de direcionar o operador a uma tela de vínculo existia
porque essa tela não existia; ela passa a existir.

Quando o catálogo de agentes não puder ser carregado, o sistema SHALL informar a
indisponibilidade e SHALL NOT afirmar que nenhum agente consulta a base — uma
requisição que não respondeu não é evidência de ausência de vínculo.

#### Scenario: Lista os agentes vinculados
- **WHEN** o operador visualiza o detalhe de uma base que dois agentes consultam
- **THEN** os dois aparecem, cada um como link para o seu detalhe, com o seu
  estado

#### Scenario: Agente vinculado a outra base não aparece
- **WHEN** existe agente vinculado apenas a outra base
- **THEN** ele não aparece na relação desta base

#### Scenario: Agente inativo vinculado aparece
- **WHEN** um agente inativo consulta a base
- **THEN** ele aparece na relação, marcado como inativo

#### Scenario: Nenhum agente consulta a base
- **WHEN** nenhum agente está vinculado à base
- **THEN** a tela informa isso e diz que o vínculo é feito na aba Conhecimento
  do detalhe do agente, sem anunciar etapa futura

#### Scenario: Catálogo de agentes indisponível
- **WHEN** a consulta de agentes falha
- **THEN** a tela informa que não foi possível carregar os agentes, não afirma
  ausência de vínculo, e o restante do detalhe continua sendo exibido
