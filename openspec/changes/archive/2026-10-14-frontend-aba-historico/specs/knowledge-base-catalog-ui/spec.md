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

O detalhe SHALL apresentar uma barra com **três** abas, nesta ordem:
`Documentos`, que reúne a listagem de documentos; `Histórico`, especificada em
`knowledge-document-history-ui`; e `Diagnóstico do índice`, especificada em
`knowledge-index-diagnostics-ui`. A terceira aba chegou com a #101: o histórico
de documentos, cuja rota de backend entrou com a #98.

**O que é da base fica acima da barra, e o que é de uma aba fica no painel
dela.** A seção de descrição e a seção de agentes que consultam a base SHALL
ficar entre o cabeçalho de detalhe e a barra de abas, nesta ordem: descrição,
depois agentes. As duas SHALL ser exibidas com qualquer aba ativa, e SHALL NOT
estar dentro do painel de nenhuma aba. Elas descrevem a base, não o conteúdo de
uma aba; dentro do painel de `Documentos` elas sumiam ao abrir outra aba
(issue #99).

A aba ativa SHALL estar refletida no endereço, no mesmo desenho do detalhe do
agente: a primeira aba é a forma canônica e **não** carrega parâmetro; as outras
carregam (`?tab=historico` e `?tab=diagnostico`). Valor de aba desconhecido no
endereço SHALL cair na primeira aba, **sem** reescrever o endereço.

Apenas a aba ativa SHALL estar montada. Aba inativa não mantém consulta viva nem
acompanhamento em intervalo.

A barra SHALL exibir contador apenas na aba `Documentos`, e apenas quando a
listagem de documentos tiver respondido com pelo menos um documento. Enquanto a
listagem carrega, quando ela falha, e quando a base não tem documento, nenhum
contador SHALL ser exibido — exibir `0` durante o carregamento afirmaria uma
contagem que ainda não foi feita. A aba `Histórico` SHALL NOT exibir contador em
nenhum estado: a rota de eventos não devolve total, e a quantidade de eventos
carregados é a de páginas pedidas, não a do histórico.

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

#### Scenario: Detalhe apresenta as três abas, nesta ordem
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a tela apresenta exatamente as abas `Documentos`, `Histórico` e
  `Diagnóstico do índice`, nessa ordem, com `Documentos` ativa por padrão

#### Scenario: Descrição e agentes ficam acima da barra de abas, nessa ordem
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a seção de descrição e a seção de agentes aparecem antes da barra de
  abas no documento, a descrição antes dos agentes

#### Scenario: Descrição e agentes aparecem com a aba de diagnóstico ativa
- **WHEN** o operador abre o detalhe com a aba `Diagnóstico do índice` ativa
- **THEN** a seção de descrição e a seção de agentes são exibidas

#### Scenario: Descrição e agentes aparecem com a aba de histórico ativa
- **WHEN** o operador abre o detalhe com a aba `Histórico` ativa
- **THEN** a seção de descrição e a seção de agentes são exibidas, antes da barra
  e fora do painel da aba

#### Scenario: Descrição e agentes não estão dentro de painel de aba
- **WHEN** o operador visualiza o detalhe com qualquer aba ativa
- **THEN** nem a seção de descrição nem a seção de agentes está contida no
  painel da aba ativa

#### Scenario: A aba ativa está no endereço
- **WHEN** o operador seleciona a aba de diagnóstico do índice
- **THEN** o endereço passa a identificar essa aba, e abrir esse endereço
  diretamente abre a mesma aba

#### Scenario: A aba de histórico está no endereço
- **WHEN** o operador seleciona a aba `Histórico`
- **THEN** o endereço passa a ter `?tab=historico`, e abrir esse endereço
  diretamente abre a aba `Histórico`

#### Scenario: A barra se percorre pelo teclado
- **WHEN** o foco está na aba `Documentos` e o operador pressiona a seta para a
  direita
- **THEN** a aba `Histórico` fica selecionada, e o endereço passa a ter
  `?tab=historico`

#### Scenario: A aba canônica não carrega parâmetro
- **WHEN** o operador volta para a aba `Documentos`
- **THEN** o endereço volta à forma sem parâmetro de aba

#### Scenario: Aba desconhecida cai na primeira
- **WHEN** o operador acessa o detalhe com um valor de aba que não existe
- **THEN** a aba `Documentos` é exibida, e o endereço não é reescrito

#### Scenario: Aba inativa não está montada
- **WHEN** o operador está na aba `Documentos`
- **THEN** o conteúdo da aba de diagnóstico e o da aba de histórico não estão no
  documento, e nenhuma requisição à rota de eventos foi feita

#### Scenario: Contador só aparece com a listagem respondida
- **WHEN** a listagem de documentos ainda não respondeu, falhou, ou respondeu sem
  nenhum documento
- **THEN** a aba `Documentos` não exibe contador

#### Scenario: A aba de histórico não tem contador
- **WHEN** o histórico da base respondeu com eventos
- **THEN** a aba `Histórico` não exibe contador

