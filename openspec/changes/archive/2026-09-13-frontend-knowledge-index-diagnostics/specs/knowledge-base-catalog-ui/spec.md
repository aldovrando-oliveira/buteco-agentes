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

O detalhe SHALL apresentar uma barra com **duas** abas: `Documentos`, que reúne a
descrição, a listagem de documentos e os agentes que consultam a base, e
`Diagnóstico do índice`, especificada em `knowledge-index-diagnostics-ui`.

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
