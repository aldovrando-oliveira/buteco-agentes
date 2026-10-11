## MODIFIED Requirements

### Requirement: Texto em português para cada código de erro
O painel SHALL traduzir o `code` do `ProblemDetails` em texto próprio, pela tabela
da D3 do `design.md`, e SHALL NOT exibir o `title` nem o `detail` como mensagem,
exceto os dados que a tabela declara: o e-mail da conta em `access-denied`, o nome
da base em `folder-in-use` (de `knowledgeBaseName`), o status em
`connectors-error` e o motivo em `provider-error`. Código desconhecido no formato
de código SHALL ter texto genérico que o cita, e falha de rede SHALL ter texto
próprio de serviço fora do ar.

O erro do cadastro sincronizado SHALL ser exibido no card de Origem, mantendo o
que o operador preencheu, e SHALL dizer que nenhuma base foi criada. O texto de
`folder-in-use` SHALL nomear a base que usa a pasta, dizer que a pasta continua
ocupada mesmo com a base inativa, e orientar escolher outra pasta ou desativar e
excluir essa base para liberar a pasta, avisando que a exclusão apaga os documentos
dela e não pode ser desfeita. Sem `knowledgeBaseName` na resposta, o texto SHALL se
referir a "a base que usa esta pasta", e SHALL NOT dizer "sem nome". O `401` do
`apps/connectors` SHALL NOT encerrar a sessão do operador nem redirecionar para o
login.

A orientação de excluir existe desde a change `exclusao-base-painel` (#136), que
trouxe a exclusão de base ao painel; até ali o texto era proibido de falar em
excluir, porque a tela não oferecia a ação.

#### Scenario: Cada código mostra o seu texto
- **WHEN** a navegação ou o cadastro respondem com cada código da tabela da D3
- **THEN** o painel exibe o texto daquele código, e nenhum `title` do
  `ProblemDetails` aparece na tela

#### Scenario: Pasta em uso no cadastro orienta desativar e excluir a base
- **WHEN** o `POST /knowledge-bases` responde `409` com `code: "folder-in-use"` e
  `knowledgeBaseName: "Políticas internas de RH"`
- **THEN** o card de Origem exibe um texto com "Políticas internas de RH", que diz
  que a pasta continua ocupada mesmo com a base inativa, que contém "desative",
  "exclua" e "não pode ser desfeita", e os campos preenchidos continuam na tela

#### Scenario: Pasta em uso sem o nome da base
- **WHEN** o `POST /knowledge-bases` responde `409` com `code: "folder-in-use"` sem
  `knowledgeBaseName`
- **THEN** o texto se refere a "a base que usa esta pasta" e não contém "sem nome"

#### Scenario: Sem acesso mostra o e-mail da conta
- **WHEN** o cadastro responde `422` com `code: "access-denied"` e `detail` igual a
  `conta@projeto.iam.gserviceaccount.com`
- **THEN** o texto exibido contém esse e-mail e a instrução de compartilhar a pasta
  como Leitor

#### Scenario: Cota passageira
- **WHEN** a navegação ou o cadastro respondem `503` com `code: "rate-limited"`
- **THEN** o texto diz que a falha é passageira e pede para tentar de novo, e não
  fala de acesso à pasta

#### Scenario: Código desconhecido
- **WHEN** o cadastro responde `422` com `code: "folder-too-deep"`
- **THEN** o painel exibe o texto genérico com `folder-too-deep`

#### Scenario: Token recusado pelo apps/connectors não desloga
- **WHEN** a consulta de pastas responde `401`
- **THEN** o token do operador continua guardado, o painel não navega para
  `/login`, e o seletor exibe o texto de sessão recusada pelo serviço de conectores
