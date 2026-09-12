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

O detalhe SHALL NOT apresentar estrutura de abas. A segunda aba do protótipo —
diagnóstico do índice — pertence à etapa posterior, e uma barra com uma aba só
afirmaria uma estrutura que a tela não tem (`design.md` de
`frontend-knowledge-base-catalogo`, D3).

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

#### Scenario: Detalhe não tem abas
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a tela não apresenta controle de abas

#### Scenario: Base inexistente
- **WHEN** o operador acessa o detalhe de um id que não existe e a API responde
  404
- **THEN** a tela informa que a base não foi encontrada, com volta para a
  listagem

## REMOVED Requirements

### Requirement: Ausência da área de documentos no detalhe
**Reason**: O requisito existia porque a etapa 5a-1 não consultava documento
nenhum, e um estado vazio ali afirmaria que a base foi consultada e está sem
documentos — a mesma regra da convenção 13 que proíbe contagem zerada. A etapa
5a-2 (`frontend-knowledge-base-documentos`) faz a consulta, então a premissa do
requisito deixa de existir: "Nenhum documento nesta base" passa a ser afirmação
**verificada**, e não suposição.

**Migration**: O comportamento é substituído pelos requisitos `Listagem de
documentos no detalhe da base` e seguintes, na capability
`knowledge-document-catalog-ui`. O componente
`KnowledgeBaseDocumentsPlaceholder` e seu teste são removidos da árvore em vez de
reaproveitados: ele existia para dizer que a tela **não** consulta documentos, e
o estado vazio novo diz outra coisa.

As asserções negativas sobre o **catálogo** — a listagem de bases não exibe
contagem de documentos nem resumo de indexação — **continuam valendo** e não são
tocadas por esta change. Elas estão no requisito `Listagem de bases de
conhecimento no painel`, que esta change não altera; a decisão de sequenciamento
que as mantém está em `design.md`, D1.
