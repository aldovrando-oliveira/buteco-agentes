## ADDED Requirements

### Requirement: Origem e falha de sincronização na listagem de bases
A listagem de bases SHALL exibir, sob a descrição de cada base, na coluna da base,
uma linha de origem: "Manual" para base `Manual`, e o provedor pelo nome de
exibição seguido do nome da pasta (`syncSource.folderName`) para base `Synced`.

Para base `Synced` com `syncState.failingSince` preenchido, a listagem SHALL exibir
também uma linha "Sincronização falhando desde" com a data e a hora de
`failingSince`, no tom de falha. Base `Synced` que nunca sincronizou SHALL NOT
exibir linha de falha. A listagem SHALL NOT acrescentar coluna nova.

#### Scenario: Base manual na listagem
- **WHEN** a listagem traz uma base `Manual`
- **THEN** a linha da base exibe "Manual" e nenhuma linha de falha de
  sincronização

#### Scenario: Base sincronizada na listagem
- **WHEN** a listagem traz uma base `Synced` do provedor `google-drive` com a pasta
  "FAQ Suporte" e `failingSince` nulo
- **THEN** a linha da base exibe "Google Drive" e "FAQ Suporte", e nenhuma linha de
  falha

#### Scenario: Base sincronizada falhando na listagem
- **WHEN** a listagem traz uma base `Synced` com `failingSince` preenchido
- **THEN** a linha da base exibe "Sincronização falhando desde" com a data e a hora
  de `failingSince`

#### Scenario: Nunca sincronizou não aparece como falha na listagem
- **WHEN** a listagem traz uma base `Synced` com os três instantes nulos
- **THEN** a linha da base não exibe linha de falha

## MODIFIED Requirements

### Requirement: Busca e filtro por estado na listagem de bases
O sistema SHALL oferecer, na listagem de bases, um campo de busca que casa com
nome e descrição, insensível a caixa e **insensível a acentos nas duas
direções** — o termo digitado sem acento SHALL encontrar o texto acentuado, e o
inverso também.

O sistema SHALL oferecer um filtro com exatamente quatro opções: todas, ativas,
inativas e com falha. A opção de falha SHALL selecionar as bases cujo
`failedCount` é maior que zero **ou** cujo `syncState.failingSince` está
preenchido, e SHALL incluir as inativas — desativar uma base impede o uso pelo
agente, não a manutenção do conteúdo, e a sincronização roda também sobre base
inativa.

Quando o resumo de indexação não estiver disponível, a opção de falha SHALL ser
apresentada **desabilitada**, e o sistema SHALL informar que o resumo não pôde ser
carregado. Isso SHALL valer mesmo havendo base com sincronização falhando: filtrar
só pela sincronização esconderia em silêncio as falhas de indexação. O sistema
SHALL NOT apresentar a opção de falha ativa e sem efeito, e SHALL NOT removê-la
do controle.

Quando a opção de falha estiver selecionada e o resumo deixar de estar
disponível, a listagem SHALL continuar exibindo as bases em vez de esvaziar.
Esvaziar afirmaria que nenhuma base tem falha, que é exatamente o que a consulta
que não respondeu não permite afirmar.

Busca e filtro SHALL rodar no cliente sobre a resposta inteira de
`GET /knowledge-bases`, que não tem busca nem paginação.

#### Scenario: Busca encontra por nome sem acento
- **WHEN** o operador digita um termo sem acento que corresponde ao nome
  acentuado de uma base
- **THEN** essa base permanece na listagem

#### Scenario: Busca encontra por descrição
- **WHEN** o operador digita um termo presente apenas na descrição de uma base
- **THEN** essa base permanece na listagem

#### Scenario: Filtro tem quatro opções
- **WHEN** o operador visualiza o controle de filtro da listagem
- **THEN** as opções oferecidas são exatamente todas, ativas, inativas e com
  falha

#### Scenario: Filtro de inativas esconde as ativas
- **WHEN** o operador seleciona o filtro de inativas
- **THEN** apenas as bases com estado inativo permanecem na listagem

#### Scenario: Filtro de falha isola as bases com documento em falha
- **WHEN** o operador seleciona o filtro de falha e o resumo traz uma única base
  com `failedCount` maior que zero, e nenhuma base tem sincronização falhando
- **THEN** apenas essa base permanece na listagem, inclusive quando ela está
  inativa

#### Scenario: Filtro de falha inclui base só com sincronização falhando
- **WHEN** o operador seleciona o filtro de falha, o resumo traz `failedCount`
  zero para todas as bases, e uma base `Synced` inativa tem `failingSince`
  preenchido
- **THEN** essa base permanece na listagem, e as bases sem falha de nenhum dos
  dois tipos saem

#### Scenario: Opção de falha fica desabilitada sem o resumo
- **WHEN** a consulta do resumo de indexação falha
- **THEN** a opção de falha é exibida desabilitada e a tela informa que o resumo
  não pôde ser carregado

#### Scenario: Opção de falha fica desabilitada sem o resumo mesmo com sincronização falhando
- **WHEN** a consulta do resumo de indexação falha e uma base `Synced` tem
  `failingSince` preenchido
- **THEN** a opção de falha continua desabilitada, e a listagem exibe todas as
  bases

#### Scenario: Filtro de falha selecionado sem resumo não esvazia a listagem
- **WHEN** a opção de falha está selecionada e o resumo não está disponível
- **THEN** as bases continuam sendo exibidas, e a tela não afirma que nenhuma
  base tem falha

#### Scenario: Busca sem resultado é distinguida de catálogo vazio
- **WHEN** existem bases cadastradas mas nenhuma corresponde ao termo buscado
- **THEN** a tela informa que nenhuma base corresponde à busca, e não que não
  existe base cadastrada
