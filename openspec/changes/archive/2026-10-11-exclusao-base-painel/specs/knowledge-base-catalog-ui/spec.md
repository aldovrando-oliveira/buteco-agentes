## ADDED Requirements

### Requirement: Exclusão de base de conhecimento no detalhe
O detalhe da base SHALL oferecer a ação `Excluir`, no cabeçalho, depois de
`Ativar`/`Desativar`, que chama `DELETE /knowledge-bases/{id}` (capability
`knowledge-base-catalog`) sempre por confirmação explícita.

**Base ativa.** O botão SHALL estar presente e indisponível, com
`aria-disabled="true"`, e SHALL expor, ao passar o mouse e ao receber foco, a
explicação de que só uma base inativa pode ser excluída e de que é preciso
desativá-la antes. O botão indisponível SHALL receber foco. Acionar o botão NÃO
SHALL abrir a confirmação nem fazer requisição. A condição é o `isActive` da base;
nenhuma outra condição SHALL bloquear o botão.

**Base inativa.** O botão SHALL abrir a confirmação, intitulada com o nome da base,
que SHALL dizer:

- que a base e os documentos dela são excluídos, com os fragmentos e o histórico,
  e que não há como desfazer. A quantidade de documentos SHALL vir da listagem de
  documentos da base; com a listagem respondida vazia, a confirmação SHALL dizer
  que a base não tem documentos; com a listagem carregando ou com falha, a
  confirmação SHALL NOT exibir número de documentos;
- os agentes vinculados à base, pelo nome, derivados no cliente de `GET /agents`,
  dizendo que os vínculos saem junto. Sem agentes vinculados, SHALL dizer que nenhum
  agente está vinculado. Com `GET /agents` carregando ou com falha, SHALL dizer que
  não foi possível conferir os agentes, e SHALL NOT dizer que nenhum agente está
  vinculado;
- em base `Synced`, que a pasta, pelo nome, fica livre para outra base e que os
  arquivos no provedor não são tocados. Em base `Manual`, essa frase SHALL NOT
  aparecer.

A confirmação NÃO SHALL exigir que o operador digite o nome da base. Cancelar,
fechar ou `Esc` NÃO SHALL fazer requisição.

**Respostas.** O texto SHALL ser escolhido pelo status e pelo `code`, e SHALL NOT
exibir o `title` nem o `detail` do `ProblemDetails`:

- `204`: o painel SHALL navegar para a listagem de bases, exibir uma notificação de
  base excluída com o nome, e a listagem SHALL ser pedida de novo, sem a base;
- `409` com `code: "knowledge-base-active"`: a confirmação SHALL dizer que a base
  foi ativada depois que a tela abriu e que nada foi excluído, SHALL NOT oferecer
  repetir a exclusão, e a base SHALL ser relida;
- `404`: a confirmação SHALL dizer que a base já tinha sido excluída e que nada
  mais foi excluído, e SHALL oferecer a volta para a listagem;
- `503`: a confirmação SHALL dizer que outra operação impediu a exclusão, que nada
  foi excluído e que é possível tentar de novo, e SHALL continuar oferecendo a
  exclusão;
- outro status, ou falha de rede: a confirmação SHALL dizer que a exclusão não foi
  confirmada e orientar recarregar a página para conferir o estado da base.

Em todas as respostas que não são `204`, o painel NÃO SHALL navegar.

#### Scenario: Base ativa não oferece a exclusão
- **WHEN** o operador abre o detalhe de uma base ativa e aciona `Excluir`
- **THEN** o botão está com `aria-disabled="true"`, a confirmação não é exibida e
  nenhuma requisição de exclusão é feita

#### Scenario: Base ativa explica a pré-condição no foco
- **WHEN** o operador leva o foco ao botão `Excluir` de uma base ativa
- **THEN** a tela exibe a explicação de que é preciso desativar a base antes de
  excluí-la

#### Scenario: Confirmação nomeia a base, os agentes e a quantidade de documentos
- **WHEN** o operador aciona `Excluir` numa base inativa com três documentos,
  consultada por dois agentes
- **THEN** a confirmação exibe o nome da base, o nome dos dois agentes, "3
  documentos" e que não há como desfazer, antes de qualquer requisição

#### Scenario: Base sem documentos e sem agentes
- **WHEN** o operador aciona `Excluir` numa base inativa cuja listagem de
  documentos respondeu vazia e que nenhum agente consulta
- **THEN** a confirmação diz que a base não tem documentos e que nenhum agente está
  vinculado

#### Scenario: Documentos e agentes desconhecidos não viram zero
- **WHEN** a listagem de documentos e `GET /agents` falharam, e o operador aciona
  `Excluir` numa base inativa
- **THEN** a confirmação não exibe número de documentos, diz que não foi possível
  conferir os agentes, e não diz que nenhum agente está vinculado

#### Scenario: Base sincronizada libera a pasta e não toca o Drive
- **WHEN** o operador aciona `Excluir` numa base `Synced` inativa com a pasta
  "Políticas"
- **THEN** a confirmação diz que a pasta "Políticas" fica livre para outra base e
  que os arquivos no Google Drive não são tocados

#### Scenario: Base manual não fala de pasta
- **WHEN** o operador aciona `Excluir` numa base `Manual` inativa
- **THEN** a confirmação não fala de pasta nem de Drive

#### Scenario: Cancelar não exclui
- **WHEN** o operador cancela a confirmação
- **THEN** nenhuma requisição de exclusão é feita e o detalhe continua na tela

#### Scenario: Exclusão concluída volta para a listagem
- **WHEN** o operador confirma e a rota responde `204`
- **THEN** o painel está em `/knowledge-bases`, exibe a notificação de base
  excluída com o nome, e a base não aparece na listagem

#### Scenario: Base ativada em outra aba
- **WHEN** o operador confirma e a rota responde `409` com
  `code: "knowledge-base-active"`
- **THEN** a confirmação diz que a base foi ativada e que nada foi excluído, não
  oferece repetir a exclusão, o painel continua no detalhe, e a base é relida

#### Scenario: Base já excluída em outra aba
- **WHEN** o operador confirma e a rota responde `404`
- **THEN** a confirmação diz que a base já tinha sido excluída, oferece a volta para
  a listagem, e o painel continua no detalhe

#### Scenario: Exclusão impedida por outra operação
- **WHEN** o operador confirma e a rota responde `503`
- **THEN** a confirmação diz que nada foi excluído e que é possível tentar de novo,
  continua oferecendo a exclusão, e o painel continua no detalhe

#### Scenario: Texto pelo código, nunca pelo servidor
- **WHEN** a rota responde `409`, `404` ou `503` com `title` e `detail` próprios
- **THEN** nem o `title` nem o `detail` aparecem na tela
