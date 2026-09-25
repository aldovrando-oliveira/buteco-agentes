# frontend-visual-theme Specification

## Purpose

TBD - defined by change frontend-tema-identidade-visual. Update Purpose after archive.

## Requirements

### Requirement: Identidade visual declarada no tema, não nos componentes
O sistema SHALL declarar a identidade visual de `apps/frontend` — paleta,
tipografia, raios e sombras — no tema do Mantine, de forma que as telas
consumam nomes semânticos (`primary`, `red`, `yellow`, `green`, `gray`,
`dimmed`) e nunca valores literais de cor.

As escalas SHALL sobrescrever os nomes que a aplicação já usa (`red`,
`yellow`, `green`, `gray`, `dark`) em vez de introduzir nomes novos, e a cor
de destaque SHALL ser declarada como `butecoBlue` e definida como
`primaryColor`, com `primaryShade` `6` no esquema claro e `4` no escuro.

#### Scenario: Nenhum componente declara cor literal
- **WHEN** o código de `apps/frontend/src` é inspecionado em busca de valores
  hexadecimais de cor fora do módulo de tema
- **THEN** nenhum componente de feature ou de layout declara cor literal;
  todas as cores vêm de nomes semânticos resolvidos pelo tema

#### Scenario: A cor de destaque é a do protótipo no esquema claro
- **WHEN** a aplicação é renderizada no esquema de cor claro
- **THEN** os elementos de ação primária e os links usam a cor de destaque
  ancorada no valor de destaque do protótipo, e não o azul padrão do Mantine

#### Scenario: A cor de destaque clareia no esquema escuro
- **WHEN** a aplicação é renderizada no esquema de cor escuro
- **THEN** a cor de destaque usada em ações primárias e links é a variante
  clareada da mesma cor, sem que nenhuma tela precise declarar essa variante

### Requirement: Escalas de cor ancoradas nos papéis que o Mantine consome
O sistema SHALL definir cada escala de cor de dez tons de modo que os tons
lidos pelo Mantine para cada papel correspondam aos valores da identidade
visual, a saber: o tom de índice `1` como fundo da variante `light` no
esquema claro, o tom de índice `9` como texto da variante `light` no esquema
claro, e o tom de índice `4` como cor de texto e de preenchimento no esquema
escuro.

Os tons ancorados SHALL ser tratados como contrato verificável; os demais
tons são preenchimento livre, desde que a escala permaneça monotônica.

#### Scenario: Badge e alerta semânticos reproduzem as cores da identidade
- **WHEN** um badge ou alerta é renderizado na variante `light` no esquema
  claro, com qualquer uma das cores semânticas de sucesso, aviso ou erro
- **THEN** o fundo e o texto correspondem ao par de fundo e texto daquele
  papel na identidade visual

#### Scenario: As âncoras são verificadas automaticamente
- **WHEN** a suíte de testes de `apps/frontend` é executada
- **THEN** um teste afirma, para cada escala semântica e para a cor de
  destaque, que os tons ancorados valem exatamente os valores da identidade
  visual, e que `primaryShade` vale `6` no claro e `4` no escuro

#### Scenario: Texto secundário usa o tom correto da escala neutra
- **WHEN** um texto é renderizado com a cor semântica de texto secundário
- **THEN** ele resolve para o tom neutro secundário da identidade visual em
  cada esquema de cor, e não para um tom vizinho

### Requirement: Contraste mínimo nas combinações de uso
O sistema SHALL garantir que as combinações de cor efetivamente usadas pelo
painel atinjam ao menos a razão de contraste 4.5:1 exigida pelo nível AA da
WCAG para texto: a variante `light` (texto sobre o próprio fundo) em ambos os
esquemas, o texto secundário sobre a superfície de card e sobre o fundo da
página, e o texto principal sobre a superfície.

Combinações que não atingem esse limite SHALL NOT ser usadas pelo painel.

#### Scenario: Variante light legível nos dois esquemas
- **WHEN** um badge ou alerta na variante `light` é renderizado em qualquer
  cor semântica, em qualquer um dos dois esquemas de cor
- **THEN** o contraste entre o texto e o seu fundo atinge ao menos 4.5:1

#### Scenario: Texto secundário legível sobre página e sobre card
- **WHEN** um texto secundário é renderizado sobre o fundo da página ou sobre
  a superfície de um card
- **THEN** o contraste atinge ao menos 4.5:1 em ambos os casos

### Requirement: Degrau de superfície entre página e card
O sistema SHALL renderizar o fundo da página em cor distinta da superfície de
cards, tabelas e modais, nos dois esquemas de cor, de modo que o conteúdo
elevado se destaque do fundo sem depender apenas de borda.

#### Scenario: Card se distingue do fundo no esquema claro
- **WHEN** uma página com cards ou tabelas é renderizada no esquema claro
- **THEN** o fundo da página é mais escuro que a superfície branca dos cards,
  formando um degrau visível

#### Scenario: Card se distingue do fundo no esquema escuro
- **WHEN** a mesma página é renderizada no esquema escuro
- **THEN** o fundo da página é mais escuro que a superfície dos cards,
  preservando o mesmo degrau

### Requirement: Tipografia declarada no tema com distinção entre texto e valor técnico
O sistema SHALL declarar no tema a família tipográfica de texto e a família
monoespaçada, servidas pelo próprio bundle da aplicação e não por um serviço
externo, junto da escala de tamanhos e alturas de linha da identidade visual.

Os tamanhos de título SHALL ser declarados no tema, e não em cada chamada de
componente de título.

#### Scenario: Fontes servidas pelo próprio bundle
- **WHEN** a aplicação é carregada em um ambiente sem acesso à internet
  pública, servida apenas pelo seu próprio container
- **THEN** as famílias tipográficas da identidade visual são aplicadas
  normalmente, sem requisição a domínio de terceiros

#### Scenario: Título de página vem do tema
- **WHEN** uma página renderiza seu título com o componente de título do
  Mantine, sem declarar tamanho
- **THEN** o título aparece no tamanho da identidade visual, definido no tema

#### Scenario: Valores técnicos em monoespaçada
- **WHEN** a interface exibe um valor técnico — url, nome de ferramenta,
  identificador de provedor ou modelo, motivo de falha
- **THEN** ele é renderizado na família monoespaçada declarada no tema

### Requirement: Aparência padrão do badge declarada no tema
O sistema SHALL declarar no tema a aparência padrão do badge — a variante
clara, que combina fundo e texto da mesma cor semântica, e a preservação da
caixa do texto como escrito.

Nenhuma tela SHALL precisar declarar variante ou caixa para obter essa
aparência, e todo badge do painel SHALL apresentá-la, independentemente da tela
em que aparece.

A variante preenchida com texto branco SHALL NOT ser usada nas cores semântica
de sucesso e de aviso, por não atingir o contraste mínimo exigido pelo
requisito de contraste desta capability.

#### Scenario: Um badge tem a mesma aparência em qualquer tela
- **WHEN** o operador percorre telas diferentes que exibem badges — listagens,
  detalhes e histórico de mensagens
- **THEN** todos os badges apresentam a variante clara, sem que nenhuma tela
  declare variante

#### Scenario: O texto do badge preserva a caixa como escrito
- **WHEN** um badge exibe um rótulo escrito em caixa de sentença
- **THEN** ele é apresentado em caixa de sentença, sem conversão para caixa alta

#### Scenario: O padrão é verificado automaticamente
- **WHEN** a suíte de testes de `apps/frontend` é executada
- **THEN** um teste afirma que o tema declara a variante clara e a preservação
  da caixa como padrão do badge

### Requirement: Escala de intensidade declarada nos dois esquemas de cor

O sistema SHALL declarar no tema a escala de intensidade usada por mapas de
calor, com um passo para o zero medido e os passos de intensidade crescente, e
SHALL declará-la **separadamente para cada esquema de cor**.

A rampa SHALL crescer em sentidos opostos entre os esquemas: no esquema escuro a
intensidade cresce **clareando**; no claro, cresce **escurecendo**. É o mesmo
papel visual trocando de ponta da escala, e por isso ele SHALL sair de variável
declarada por esquema e SHALL NOT sair de tom fixo da paleta — um tom fixo é
claro nos dois esquemas ou escuro nos dois, e quebraria em um deles.

O componente que desenha o mapa de calor SHALL consumir essas variáveis, e SHALL
NOT referenciar nenhum tom da paleta diretamente.

A escala SHALL ser composta de tons que já existem na paleta do tema; nenhuma cor
nova entra por ela.

#### Scenario: A escala existe nos dois esquemas
- **WHEN** a aplicação é renderizada em qualquer um dos dois esquemas de cor
- **THEN** todos os passos da escala de intensidade estão definidos naquele
  esquema

#### Scenario: A rampa inverte entre os esquemas
- **WHEN** os passos de intensidade são comparados entre os dois esquemas
- **THEN** o passo de maior intensidade do esquema escuro é mais claro que o de
  menor intensidade, e no esquema claro a relação é a inversa

#### Scenario: O componente lê a variável, não o tom
- **WHEN** o mapa de calor é renderizado
- **THEN** as células referenciam as variáveis da escala declaradas no tema, e
  **nenhum** tom da paleta aparece cravado no componente

### Requirement: Degrau de superfície entre card e superfície sutil

O sistema SHALL declarar no tema uma superfície sutil, distinta da superfície de
cards, para faixas de cabeçalho, quadros internos de métrica e texturas de
ausência de dado; e SHALL declará-la **separadamente para cada esquema de cor**.

O degrau entre essa superfície e a do card SHALL ser perceptível em área
chapada, nos dois esquemas — não basta que as duas cores sejam distintas.

**É um terceiro nível, e não o mesmo da página contra o card.** O primeiro
separa o fundo do conteúdo elevado; este separa, dentro do conteúdo elevado, o
que é destaque do que é base. Um degrau que existe no token e não se vê na tela
cumpre a primeira exigência e falha a segunda.

#### Scenario: A superfície sutil existe nos dois esquemas
- **WHEN** a aplicação é renderizada em qualquer um dos dois esquemas de cor
- **THEN** a superfície sutil está declarada naquele esquema, e é distinta da
  superfície do card

#### Scenario: O degrau é perceptível em área chapada
- **WHEN** um quadro de métrica ou uma faixa de cabeçalho é renderizado sobre um
  card, em qualquer um dos dois esquemas
- **THEN** a diferença entre as duas superfícies é perceptível sem depender de
  borda, e **não** fica abaixo do limiar em que uma área chapada se confunde com
  o fundo

#### Scenario: A distinção entre as cores não basta como garantia
- **WHEN** o contrato da superfície sutil é verificado
- **THEN** a verificação mede a **razão de contraste** entre ela e a superfície
  do card, e **não** apenas que as duas apontam para tons diferentes da paleta
