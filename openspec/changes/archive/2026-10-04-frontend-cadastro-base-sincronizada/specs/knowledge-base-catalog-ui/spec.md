## MODIFIED Requirements

### Requirement: Criação e edição de base de conhecimento
O sistema SHALL permitir criar uma base em `/knowledge-bases/new` e editar nome e
descrição em `/knowledge-bases/{id}/edit`.

Nome e descrição SHALL ser obrigatórios e não vazios no cliente, espelhando a
validação que a API já impõe nas duas rotas. Erro de validação devolvido pela
API SHALL ser apresentado no campo correspondente, a partir de
`ValidationProblemDetails`.

O formulário SHALL exibir um aviso quando a descrição for curta, por ser o texto
que o modelo lê. O sistema SHALL NOT apresentar tratamento para base **sem**
descrição: esse estado é inalcançável, porque a API rejeita descrição vazia na
criação e na edição e o campo do response não é anulável (`design.md`, D7).

O formulário SHALL NOT bloquear navegação com alteração pendente. Nenhum
formulário do painel usa guarda de navegação; ela protege rascunho de vínculo em
aba, onde a troca de aba perde trabalho sem sair da rota (`design.md`, D11).

A criação SHALL exibir, depois do bloco da descrição, o card "Origem dos
documentos" com duas opções, Manual e Sincronizada, e o aviso de que a origem não
pode ser alterada depois de criar a base. Manual SHALL vir marcada. Com Manual
marcada, o formulário SHALL enviar exatamente o corpo de hoje, só com `name` e
`description`, sem `contentMode`, `provider` nem `folderId`, e SHALL NOT fazer
nenhuma chamada ao `apps/connectors`. A opção Sincronizada segue a capability
`knowledge-base-sync-source-ui`.

A nota de rodapé do formulário SHALL mudar com a origem: com Manual, a nota de hoje
(documentos carregados na tela de detalhe, só markdown); com Sincronizada, uma
nota que diz que os documentos não são carregados nesta tela e entram pela
sincronização com a pasta depois de criar a base, sem afirmar prazo para a
primeira sincronização.

A edição SHALL NOT exibir o card de Origem nem permitir mudar a origem, e SHALL
continuar enviando só `name` e `description`.

#### Scenario: Criar base com nome e descrição
- **WHEN** o operador preenche nome e descrição e confirma a criação
- **THEN** a base é criada e o operador é levado ao detalhe dela

#### Scenario: Nome vazio é barrado no cliente
- **WHEN** o operador tenta salvar com o nome vazio ou só de espaços
- **THEN** o formulário exibe erro no campo de nome e nenhuma requisição de
  criação é enviada

#### Scenario: Descrição vazia é barrada no cliente
- **WHEN** o operador tenta salvar com a descrição vazia ou só de espaços
- **THEN** o formulário exibe erro no campo de descrição e nenhuma requisição de
  criação é enviada

#### Scenario: Erro de validação da API vira erro por campo
- **WHEN** a API responde 400 com `ValidationProblemDetails` apontando um campo
- **THEN** a mensagem é exibida naquele campo, não como erro genérico da tela

#### Scenario: Descrição curta recebe aviso sem bloquear
- **WHEN** o operador digita uma descrição curta
- **THEN** o formulário exibe um aviso sobre o texto que o modelo lê, e o
  salvamento continua permitido

#### Scenario: Edição carrega os valores atuais
- **WHEN** o operador acessa a edição de uma base existente
- **THEN** os campos de nome e descrição já contêm os valores atuais da base

#### Scenario: Sair com alteração pendente não é bloqueado
- **WHEN** o operador altera um campo do formulário e navega para outra rota
- **THEN** a navegação acontece sem diálogo de bloqueio

#### Scenario: Card de Origem na criação, com Manual marcada
- **WHEN** o operador abre `/knowledge-bases/new`
- **THEN** o card "Origem dos documentos" aparece com Manual marcada, Sincronizada
  desmarcada e o aviso de que a origem não pode ser alterada depois de criar a base

#### Scenario: Cadastro manual envia o corpo de hoje
- **WHEN** o operador cria uma base com Manual marcada
- **THEN** o corpo do `POST /knowledge-bases` tem exatamente as chaves `name` e
  `description`, e nenhuma requisição é feita ao endereço do `apps/connectors`

#### Scenario: Nota de rodapé por origem
- **WHEN** o operador alterna entre Manual e Sincronizada
- **THEN** com Manual a nota fala de carregar documentos na tela de detalhe, e com
  Sincronizada a nota fala de sincronização com a pasta e não contém prazo em
  minutos nem as palavras "imediatamente" ou "agora"

#### Scenario: Edição sem card de Origem
- **WHEN** o operador abre a edição de uma base, manual ou sincronizada
- **THEN** o card "Origem dos documentos" não aparece, e o `PUT` leva só `name` e
  `description`
