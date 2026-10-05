## ADDED Requirements

### Requirement: Documentos somente leitura em base sincronizada
Em base com `contentMode` `Synced`, a listagem de documentos SHALL NOT exibir as
ações "Adicionar documento", "Atualizar" e "Excluir", nem a coluna de ações, e o
painel SHALL NOT abrir o modal de documento nem a confirmação de exclusão. O
`apps/api` recusa as três escritas com `409` em base sincronizada. O cabeçalho da
listagem SHALL dizer que os documentos são somente leitura e que o conteúdo vem da
pasta.

"Reindexar documento" SHALL continuar disponível na faixa de falha em base
sincronizada, porque o `apps/api` a permite e a sincronização não reenvia um
arquivo cujo marcador não mudou.

Em base `Manual`, as ações SHALL continuar como antes desta mudança.

#### Scenario: Base sincronizada não oferece edição de documento
- **WHEN** o operador abre o detalhe de uma base `Synced` com documentos
- **THEN** a tela não exibe "Adicionar documento", "Atualizar" nem "Excluir", e o
  cabeçalho da listagem diz que o conteúdo vem da pasta e é somente leitura

#### Scenario: Base manual continua oferecendo edição de documento
- **WHEN** o operador abre o detalhe de uma base `Manual` com documentos
- **THEN** a tela exibe "Adicionar documento", e cada linha exibe "Atualizar" e
  "Excluir"

#### Scenario: Reindexar continua em base sincronizada
- **WHEN** o operador abre o detalhe de uma base `Synced` com um documento em
  `Failed`
- **THEN** a faixa de falha exibe "Reindexar documento", e acioná-lo chama a rota
  de reindexação

## MODIFIED Requirements

### Requirement: Listagem de documentos no detalhe da base
O sistema SHALL exibir, no detalhe de uma base de conhecimento em
`apps/frontend`, os documentos daquela base, consumindo
`GET /knowledge-bases/{knowledgeBaseId}/documents`. Cada linha SHALL exibir o
título do documento, o tipo de origem, o estado de indexação e as ações
disponíveis — em base sincronizada, nenhuma ação de edição (ver "Documentos
somente leitura em base sincronizada").

A ordem exibida SHALL ser a da resposta. `apps/api` já ordena por `CreatedAt`
com desempate por `Id` (`api-response-ordering`), e reordenar no cliente criaria
uma segunda ordenação com comparador diferente da do PostgreSQL.

Quando a listagem não puder ser carregada, a tela SHALL informar a falha e
SHALL NOT afirmar que a base não tem documentos — uma requisição que não
respondeu não é evidência de ausência.

Quando a listagem voltar vazia, a tela SHALL informar que a base não tem nenhum
documento. Em base `Manual`, SHALL oferecer a ação de adicionar o primeiro; em
base `Synced`, SHALL dizer que os documentos entram pela sincronização com a pasta
e SHALL NOT oferecer a ação de adicionar.

#### Scenario: Base com documentos exibe uma linha por documento
- **WHEN** o operador visualiza o detalhe de uma base cuja listagem de documentos
  devolve três documentos
- **THEN** a tela exibe três linhas, cada uma com título, tipo de origem e estado
  de indexação

#### Scenario: Base sem documentos exibe estado vazio verificado
- **WHEN** o operador visualiza o detalhe de uma base `Manual` cuja listagem
  devolve uma lista vazia
- **THEN** a tela informa que a base não tem nenhum documento e oferece a ação de
  adicionar o primeiro

#### Scenario: Base sincronizada sem documentos não oferece adicionar
- **WHEN** o operador visualiza o detalhe de uma base `Synced` cuja listagem
  devolve uma lista vazia
- **THEN** a tela informa que a base não tem nenhum documento e que eles entram
  pela sincronização com a pasta, e não exibe "Adicionar documento"

#### Scenario: Falha ao listar não vira estado vazio
- **WHEN** a requisição da listagem de documentos falha
- **THEN** a tela informa que não foi possível carregar os documentos, e não
  afirma que a base está sem documentos

#### Scenario: A ordem da resposta é preservada
- **WHEN** a listagem devolve documentos numa ordem
- **THEN** a tela os exibe exatamente nessa ordem
