## MODIFIED Requirements

### Requirement: Detalhe da base de conhecimento
O sistema SHALL exibir, em `/knowledge-bases/{id}`, o nome, a descrição e o
estado da base, com uma volta explícita para a listagem.

O detalhe SHALL NOT exibir as datas de criação e atualização. Elas existem no
response, mas o protótipo as omite nesta tela de propósito: a tela carrega a
tabela de documentos, com a data de atualização de cada documento, e as datas da
base viram ruído ao lado delas.

O detalhe SHALL oferecer as ações `Editar` e, conforme o estado atual,
`Ativar` ou `Desativar`. `Desativar` SHALL passar por confirmação, no padrão de
agente e servidor MCP; `Ativar` SHALL ser imediato.

**O que mudou, e por quê.** Até esta etapa o detalhe era proibido de apresentar
estrutura de abas, porque só existia uma: uma barra com uma aba só afirmaria uma
estrutura que a tela não tinha (`design.md` de
`frontend-knowledge-base-catalogo`, D3). O gatilho registrado ali era a segunda
aba, e ela chegou — o diagnóstico do índice, cuja rota de backend entrou em
13/09/2026.

O detalhe SHALL apresentar uma barra com **duas** abas: `Documentos`, que reúne
a listagem de documentos, e `Diagnóstico do índice`, especificada em
`knowledge-index-diagnostics-ui`.

**O que é da base fica acima da barra, e o que é de uma aba fica no painel
dela.** A seção de descrição e a seção de agentes que consultam a base SHALL
ficar entre o cabeçalho de detalhe e a barra de abas, nesta ordem: descrição,
depois agentes. As duas SHALL ser exibidas com qualquer aba ativa, e SHALL NOT
estar dentro do painel de nenhuma aba. Elas descrevem a base, não o conteúdo de
uma aba; dentro do painel de `Documentos` elas sumiam ao abrir outra aba
(issue #99).

A aba ativa SHALL estar refletida no endereço, no mesmo desenho do detalhe do
agente: a primeira aba é a forma canônica e **não** carrega parâmetro; a outra
carrega. Valor de aba desconhecido no endereço SHALL cair na primeira aba, **sem**
reescrever o endereço.

Apenas a aba ativa SHALL estar montada. Aba inativa não mantém consulta viva nem
acompanhamento em intervalo.

A barra SHALL exibir contador apenas na aba `Documentos`, e apenas quando a
listagem de documentos tiver respondido com pelo menos um documento. Enquanto a
listagem carrega, quando ela falha, e quando a base não tem documento, nenhum
contador SHALL ser exibido — exibir `0` durante o carregamento afirmaria uma
contagem que ainda não foi feita.

#### Scenario: Detalhe exibe os campos que a API devolve
- **WHEN** o operador acessa o detalhe de uma base existente
- **THEN** a tela exibe nome, descrição e badge de estado

#### Scenario: Detalhe não exibe datas
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** as datas de criação e atualização não são exibidas

#### Scenario: Detalhe oferece volta para a listagem
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** existe um controle de volta cuja rota é a listagem de bases

#### Scenario: Desativar passa por confirmação
- **WHEN** o operador aciona `Desativar` no detalhe de uma base ativa
- **THEN** uma confirmação é exibida antes de qualquer requisição, e a base só é
  desativada após o operador confirmar

#### Scenario: Ativar é imediato
- **WHEN** o operador aciona `Ativar` no detalhe de uma base inativa
- **THEN** a base é ativada sem confirmação intermediária

#### Scenario: Detalhe apresenta as duas abas
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a tela apresenta as abas `Documentos` e `Diagnóstico do índice`, com
  `Documentos` ativa por padrão

#### Scenario: Descrição e agentes ficam acima da barra de abas, nessa ordem
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a seção de descrição e a seção de agentes aparecem antes da barra de
  abas no documento, a descrição antes dos agentes

#### Scenario: Descrição e agentes aparecem com a aba de diagnóstico ativa
- **WHEN** o operador abre o detalhe com a aba `Diagnóstico do índice` ativa
- **THEN** a seção de descrição e a seção de agentes são exibidas

#### Scenario: Descrição e agentes não estão dentro de painel de aba
- **WHEN** o operador visualiza o detalhe com qualquer aba ativa
- **THEN** nem a seção de descrição nem a seção de agentes está contida no
  painel da aba ativa

#### Scenario: A aba ativa está no endereço
- **WHEN** o operador seleciona a aba de diagnóstico do índice
- **THEN** o endereço passa a identificar essa aba, e abrir esse endereço
  diretamente abre a mesma aba

#### Scenario: A aba canônica não carrega parâmetro
- **WHEN** o operador volta para a aba `Documentos`
- **THEN** o endereço volta à forma sem parâmetro de aba

#### Scenario: Aba desconhecida cai na primeira
- **WHEN** o operador acessa o detalhe com um valor de aba que não existe
- **THEN** a aba `Documentos` é exibida, e o endereço não é reescrito

#### Scenario: Aba inativa não está montada
- **WHEN** o operador está na aba `Documentos`
- **THEN** o conteúdo da aba de diagnóstico não está no documento

#### Scenario: Contador só aparece com a listagem respondida
- **WHEN** a listagem de documentos ainda não respondeu, falhou, ou respondeu sem
  nenhum documento
- **THEN** a aba `Documentos` não exibe contador

### Requirement: Agentes que consultam a base
O sistema SHALL exibir, no detalhe da base, quais agentes a consultam, com o nome
de cada um como link para o detalhe do agente. A relação SHALL ser derivada no
cliente a partir de `GET /agents`, que já devolve as bases vinculadas a cada
agente — não existe consulta inversa na API, e isso é decisão registrada do
backend.

Cada agente SHALL ser apresentado como um chip, e os chips SHALL quebrar na
horizontal, ocupando a largura do conteúdo: muitos agentes viram mais linhas de
chips, não uma lista vertical com um agente por linha. O nome acessível do link
de cada chip SHALL ser o nome do agente.

O estado do agente SHALL continuar identificável **por texto**, e não só por
cor: o chip de um agente inativo SHALL exibir `Inativo`. O chip de agente ativo
SHALL NOT carregar marca de estado. Agente inativo não consulta a base, e
escondê-lo atrás de um chip igual aos outros afirmaria uma consulta que não
acontece (`design.md`, D2).

O cabeçalho da seção SHALL exibir a contagem de agentes vinculados (por exemplo,
`7 agentes`; `1 agente` no singular) **somente** quando o catálogo de agentes
tiver respondido e pelo menos um agente estiver vinculado. Enquanto o catálogo
não respondeu, e quando ele falhou, nenhuma contagem SHALL ser exibida: um
número ali afirmaria uma contagem que não foi feita. Sem nenhum agente
vinculado, a contagem SHALL NOT ser exibida, e o estado vazio diz o fato.

Quando nenhum agente consulta a base, o sistema SHALL dizer isso e SHALL
indicar onde o vínculo é feito — a aba Conhecimento do detalhe do agente. A
proibição anterior de direcionar o operador a uma tela de vínculo existia
porque essa tela não existia; ela passa a existir.

Quando o catálogo de agentes não puder ser carregado, o sistema SHALL informar a
indisponibilidade e SHALL NOT afirmar que nenhum agente consulta a base — uma
requisição que não respondeu não é evidência de ausência de vínculo.

Enquanto o catálogo de agentes ainda não respondeu, a seção SHALL indicar
carregamento. Nesse estado ela SHALL NOT exibir o texto de indisponibilidade,
SHALL NOT exibir o estado vazio e SHALL NOT exibir contagem: carregamento não é
falha nem ausência, e dizer qualquer um dos dois afirmaria um resultado que a
requisição ainda não deu. Carregamento, falha e resposta SHALL ser distinguidos
a partir do estado da consulta, e não deduzidos da ausência de dado.

#### Scenario: Lista os agentes vinculados
- **WHEN** o operador visualiza o detalhe de uma base que dois agentes consultam
- **THEN** os dois aparecem, cada um como chip com link para o seu detalhe, e o
  nome acessível de cada link é o nome do agente

#### Scenario: Agente vinculado a outra base não aparece
- **WHEN** existe agente vinculado apenas a outra base
- **THEN** ele não aparece na relação desta base

#### Scenario: Agente inativo vinculado aparece marcado por texto
- **WHEN** um agente inativo consulta a base
- **THEN** ele aparece na relação, e o chip dele exibe o texto `Inativo`

#### Scenario: Agente ativo não carrega marca de estado
- **WHEN** um agente ativo consulta a base
- **THEN** o chip dele não exibe `Inativo`

#### Scenario: Muitos agentes viram linhas de chips
- **WHEN** sete ou mais agentes consultam a base
- **THEN** todos aparecem como chips no mesmo agrupamento, sem um agente por
  linha

#### Scenario: Contagem com a lista carregada
- **WHEN** o catálogo de agentes respondeu e sete agentes consultam a base
- **THEN** o cabeçalho da seção exibe `7 agentes`

#### Scenario: Carregamento não afirma falha nem ausência
- **WHEN** o catálogo de agentes ainda não respondeu
- **THEN** a seção indica carregamento, não exibe o texto de
  indisponibilidade, não exibe o estado vazio e não exibe contagem

#### Scenario: Nenhuma contagem durante o carregamento
- **WHEN** o catálogo de agentes ainda não respondeu
- **THEN** o cabeçalho da seção não exibe contagem de agentes

#### Scenario: Nenhuma contagem com o catálogo indisponível
- **WHEN** a consulta de agentes falha
- **THEN** o cabeçalho da seção não exibe contagem de agentes

#### Scenario: Nenhum agente consulta a base
- **WHEN** nenhum agente está vinculado à base
- **THEN** a tela informa isso e diz que o vínculo é feito na aba Conhecimento
  do detalhe do agente, sem anunciar etapa futura, e o cabeçalho não exibe
  contagem

#### Scenario: Catálogo de agentes indisponível
- **WHEN** a consulta de agentes falha
- **THEN** a tela informa que não foi possível carregar os agentes, não afirma
  ausência de vínculo, e o restante do detalhe continua sendo exibido
