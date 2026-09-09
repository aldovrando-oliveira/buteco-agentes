## ADDED Requirements

### Requirement: Listagem de bases de conhecimento no painel
O sistema SHALL exibir, em `apps/frontend`, uma listagem de todas as bases de
conhecimento cadastradas, incluindo as inativas, com nome, descrição e estado de
cada uma. O nome SHALL ser um link real para o detalhe da base, para haver alvo
focável por teclado além da linha clicável.

A listagem SHALL exibir também quais agentes consultam cada base, derivado no
cliente a partir de `GET /agents` — uma requisição para a listagem inteira, não
uma por base.

A listagem SHALL NOT exibir contagem de documentos nem resumo de indexação:
nenhum dos dois existe em `KnowledgeBaseResponse`, e obtê-los exigiria uma
requisição por base (`design.md`, D1).

Quando o catálogo de agentes não puder ser carregado, a coluna SHALL indicar que
o dado é desconhecido, de forma distinta de "nenhum agente consulta" — uma
requisição que não respondeu não é evidência de ausência de vínculo.

#### Scenario: Listagem mostra nome, descrição e estado
- **WHEN** o operador acessa `/knowledge-bases` e existem bases cadastradas
- **THEN** cada linha exibe o nome da base, sua descrição e um badge de estado
  Ativa ou Inativa

#### Scenario: Listagem inclui bases inativas
- **WHEN** o operador acessa `/knowledge-bases` e existe pelo menos uma base
  desativada
- **THEN** essa base aparece na listagem, com o badge Inativa

#### Scenario: Listagem não afirma contagem que a API não devolve
- **WHEN** o operador visualiza a listagem de bases
- **THEN** nenhuma linha exibe contagem de documentos ou resumo de indexação —
  nem como valor, nem como zero, nem como travessão

#### Scenario: Listagem mostra quem consulta cada base
- **WHEN** o operador visualiza a listagem e dois agentes consultam a mesma base
- **THEN** os dois nomes aparecem na linha daquela base

#### Scenario: Base sem nenhum agente vinculado
- **WHEN** nenhum agente consulta uma base listada
- **THEN** a linha informa que nenhum agente a consulta

#### Scenario: Catálogo de agentes indisponível na listagem
- **WHEN** a consulta de agentes falha
- **THEN** a listagem continua sendo exibida, e a coluna de agentes não afirma
  que nenhum agente consulta as bases

#### Scenario: Nome da base é um link para o detalhe
- **WHEN** o operador visualiza uma linha da listagem
- **THEN** o nome da base é um link cuja rota é o detalhe daquela base

### Requirement: Busca e filtro por estado na listagem de bases
O sistema SHALL oferecer, na listagem de bases, um campo de busca que casa com
nome e descrição, insensível a caixa e **insensível a acentos nas duas
direções** — o termo digitado sem acento SHALL encontrar o texto acentuado, e o
inverso também.

O sistema SHALL oferecer um filtro por estado com exatamente três opções:
todas, ativas e inativas. O sistema SHALL NOT oferecer filtro por falha de
indexação: ele depende de estado de indexação agregado por base, que a API não
devolve (`design.md`, D9).

Busca e filtro SHALL rodar no cliente sobre a resposta inteira de
`GET /knowledge-bases`, que não tem busca nem paginação.

#### Scenario: Busca encontra por nome sem acento
- **WHEN** o operador digita um termo sem acento que corresponde ao nome
  acentuado de uma base
- **THEN** essa base permanece na listagem

#### Scenario: Busca encontra por descrição
- **WHEN** o operador digita um termo presente apenas na descrição de uma base
- **THEN** essa base permanece na listagem

#### Scenario: Filtro por estado tem três opções
- **WHEN** o operador visualiza o controle de filtro da listagem
- **THEN** as opções oferecidas são exatamente todas, ativas e inativas, sem
  nenhuma opção de falha de indexação

#### Scenario: Filtro de inativas esconde as ativas
- **WHEN** o operador seleciona o filtro de inativas
- **THEN** apenas as bases com estado inativo permanecem na listagem

#### Scenario: Busca sem resultado é distinguida de catálogo vazio
- **WHEN** existem bases cadastradas mas nenhuma corresponde ao termo buscado
- **THEN** a tela informa que nenhuma base corresponde à busca, e não que não
  existe base cadastrada

### Requirement: Catálogo vazio de bases de conhecimento
O sistema SHALL exibir, quando não existe nenhuma base cadastrada, um estado
vazio explícito que diga o que é uma base de conhecimento e ofereça a ação de
criar a primeira.

#### Scenario: Nenhuma base cadastrada
- **WHEN** o operador acessa `/knowledge-bases` e a resposta é uma lista vazia
- **THEN** a tela exibe um estado vazio explicando o que é uma base de
  conhecimento e um controle que leva à criação de base

### Requirement: Detalhe da base de conhecimento
O sistema SHALL exibir, em `/knowledge-bases/{id}`, o nome, a descrição e o
estado da base, com uma volta explícita para a listagem.

O detalhe SHALL NOT exibir as datas de criação e atualização. Elas existem no
response, mas o protótipo as omite nesta tela de propósito — a tela ganha a
tabela de documentos em etapa posterior, e as datas viram ruído ali.

O detalhe SHALL oferecer as ações `Editar` e, conforme o estado atual,
`Ativar` ou `Desativar`. `Desativar` SHALL passar por confirmação, no padrão de
agente e servidor MCP; `Ativar` SHALL ser imediato.

O detalhe SHALL NOT apresentar estrutura de abas. As duas abas do protótipo
pertencem a etapas posteriores, e uma barra com uma aba só afirmaria uma
estrutura que a tela não tem (`design.md`, D3).

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

### Requirement: A descrição da base é apresentada como texto lido pelo modelo
O sistema SHALL apresentar a descrição da base em seção própria do detalhe,
rotulada de forma a identificá-la como o texto que o modelo lê para decidir se a
base é relevante para a pergunta, com a nota de que ela não é mostrada ao
cliente final.

A descrição SHALL NOT ser apresentada como subtítulo do cabeçalho de detalhe. A
posição é deliberada: como subtítulo ela lê como texto decorativo de UI, e ela é
campo de runtime (`design.md`, contexto e D7).

#### Scenario: Descrição aparece em seção própria e rotulada
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a descrição aparece em seção própria, com rótulo que a identifica como
  o texto lido pelo modelo, e com a nota de que não é mostrada ao cliente

#### Scenario: Descrição não é o subtítulo do cabeçalho
- **WHEN** o operador visualiza o cabeçalho de detalhe da base
- **THEN** o subtítulo do cabeçalho não é a descrição da base

### Requirement: Ausência da área de documentos no detalhe
O sistema SHALL indicar, no lugar da área de documentos, que a gestão de
documentos desta base chega em etapa posterior.

O sistema SHALL NOT exibir estado vazio de documentos. Dizer que não há
documentos afirmaria que a base foi consultada e está vazia, quando nenhuma
consulta de documento é feita nesta etapa (`design.md`, D4).

#### Scenario: Detalhe indica que a gestão de documentos vem depois
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a tela informa que a gestão de documentos chega em etapa posterior

#### Scenario: Detalhe não afirma que a base está sem documentos
- **WHEN** o operador visualiza o detalhe de uma base
- **THEN** a tela não afirma que a base não tem nenhum documento

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

### Requirement: Agentes que consultam a base
O sistema SHALL exibir, no detalhe da base, quais agentes a consultam, com o nome
de cada um como link para o detalhe do agente e o estado dele. A relação SHALL
ser derivada no cliente a partir de `GET /agents`, que já devolve as bases
vinculadas a cada agente — não existe consulta inversa na API, e isso é decisão
registrada do backend.

Quando nenhum agente consulta a base, o sistema SHALL dizer isso e SHALL NOT
direcionar o operador a uma tela de vínculo: a tela que vincula base a agente não
existe nesta etapa.

Quando o catálogo de agentes não puder ser carregado, o sistema SHALL informar a
indisponibilidade e SHALL NOT afirmar que nenhum agente consulta a base — uma
requisição que não respondeu não é evidência de ausência de vínculo.

#### Scenario: Lista os agentes vinculados
- **WHEN** o operador visualiza o detalhe de uma base que dois agentes consultam
- **THEN** os dois aparecem, cada um como link para o seu detalhe, com o seu
  estado

#### Scenario: Agente vinculado a outra base não aparece
- **WHEN** existe agente vinculado apenas a outra base
- **THEN** ele não aparece na relação desta base

#### Scenario: Agente inativo vinculado aparece
- **WHEN** um agente inativo consulta a base
- **THEN** ele aparece na relação, marcado como inativo

#### Scenario: Nenhum agente consulta a base
- **WHEN** nenhum agente está vinculado à base
- **THEN** a tela informa isso, sem indicar uma tela de vínculo

#### Scenario: Catálogo de agentes indisponível
- **WHEN** a consulta de agentes falha
- **THEN** a tela informa que não foi possível carregar os agentes, não afirma
  ausência de vínculo, e o restante do detalhe continua sendo exibido

### Requirement: Orientação e preview da descrição no formulário
O formulário SHALL apresentar a descrição em seção visualmente distinta dos
demais campos, com a orientação de como escrevê-la — que ela é lida pelo modelo
durante a conversa, e que deve dizer que assunto a base cobre e em que situação
consultá-la.

O formulário SHALL exibir, dentro dessa mesma seção, um preview do que o agente
recebe: o nome e a descrição. Com a descrição em branco, o preview SHALL dizer a
consequência — que o modelo recebe só o nome.

O preview SHALL NOT exibir nome de ferramenta. O nome da tool de uma base de
conhecimento não está definido em nenhuma spec: é decisão da etapa do resolvedor
de tool, e passa pelo mecanismo de deduplicação de nomes. Exibi-lo afirmaria o
que o sistema não sabe.

O formulário SHALL indicar que documentos são carregados depois, na tela de
detalhe da base.

#### Scenario: A descrição tem seção própria, com orientação
- **WHEN** o operador visualiza o formulário de base
- **THEN** a descrição aparece em seção distinta dos demais campos, com a
  orientação de que é o texto lido pelo modelo e do que deve conter

#### Scenario: Preview acompanha o que foi digitado
- **WHEN** o operador preenche nome e descrição
- **THEN** o preview exibe os dois valores

#### Scenario: Preview sem descrição diz a consequência
- **WHEN** o campo de descrição está em branco
- **THEN** o preview informa que o modelo recebe só o nome para decidir quando
  consultar a base

#### Scenario: Preview não afirma nome de ferramenta
- **WHEN** o operador visualiza o preview
- **THEN** nenhum identificador de ferramenta é exibido

#### Scenario: Contagem de caracteres da descrição
- **WHEN** o operador visualiza o campo de descrição
- **THEN** a quantidade de caracteres é exibida, e recebe aviso quando a
  descrição é curta

#### Scenario: Formulário indica onde os documentos entram
- **WHEN** o operador visualiza o formulário de base
- **THEN** a tela informa que documentos são carregados depois, na tela de
  detalhe

### Requirement: Acesso à área de conhecimento pela navegação lateral
O sistema SHALL oferecer, na navegação lateral, um item que leva ao catálogo de
bases de conhecimento, com ícone e rótulo textual, ativo em todas as rotas do
grupo — listagem, detalhe, criação e edição.

#### Scenario: Item de conhecimento leva ao catálogo
- **WHEN** o operador aciona o item de conhecimento na navegação lateral
- **THEN** a aplicação navega para a listagem de bases de conhecimento

#### Scenario: Item continua ativo em rota profunda de conhecimento
- **WHEN** o operador está no detalhe, na criação ou na edição de uma base
- **THEN** o item de conhecimento da navegação continua marcado como ativo

#### Scenario: Nome acessível do item é o rótulo
- **WHEN** o item de conhecimento é lido por tecnologia assistiva
- **THEN** o nome acessível é o rótulo textual, não o ícone
