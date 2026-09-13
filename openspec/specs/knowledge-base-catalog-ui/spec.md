# knowledge-base-catalog-ui Specification

## Purpose

O catálogo de bases de conhecimento no painel do operador: listar, consultar,
criar, editar, ativar e desativar bases, consumindo as rotas de
`knowledge-base-catalog` em `apps/api`.

A capability existe por um motivo que não é CRUD: a **descrição** de uma base é o
texto que o modelo lê para decidir se a base é relevante para a pergunta. Antes
desta tela, esse texto só podia ser escrito por quem tem `curl`. Por isso a
descrição não é tratada como campo a mais — tem seção própria no detalhe, bloco
próprio no formulário, e é a única coisa desta interface cujo leitor não é uma
pessoa.

A segunda regra que atravessa a capability é a de **não afirmar o que o sistema
não sabe**, e ela se manifesta em três lugares desta interface, sempre da mesma
forma: distinguindo "sei que é zero" de "não sei".

- A relação com agentes distingue "nenhum agente consulta" de "não foi possível
  saber".
- A contagem de documentos exibe o zero, porque ele é **medido** — a agregação
  percorreu os documentos daquela base e não encontrou nenhum. A contagem de
  fragmentos de um documento nunca indexado, do mesmo domínio e com o mesmo
  valor, **não** é exibida, porque é o default de uma coluna que ninguém
  escreveu. A régua é a proveniência do zero, nunca o tipo do campo.
- O resumo de indexação não separa documento pendente de documento em indexação,
  porque o recurso que o serve agrega os dois de propósito; a interface mostra o
  complemento como uma parcela só, em vez de inventar uma distinção que o dado
  não carrega. E só a parcela de falha recebe tom de alerta: documento em
  andamento é o funcionamento normal do pipeline.

Nenhuma das duas contagens vem de `KnowledgeBaseResponse`, que continua sem
campo de contagem nenhum. Elas vêm de um recurso próprio,
`GET /knowledge-bases/indexing-summary`, cujo custo é **independente do número de
bases** — é essa propriedade, e não uma mudança de regra, que permitiu ao
catálogo exibi-las.

## Requirements

### Requirement: Listagem de bases de conhecimento no painel
O sistema SHALL exibir, em `apps/frontend`, uma listagem de todas as bases de
conhecimento cadastradas, incluindo as inativas, com nome, descrição e estado de
cada uma. O nome SHALL ser um link real para o detalhe da base, para haver alvo
focável por teclado além da linha clicável.

A listagem SHALL exibir também quais agentes consultam cada base, derivado no
cliente a partir de `GET /agents` — uma requisição para a listagem inteira, não
uma por base.

A listagem SHALL exibir a contagem de documentos e o resumo de indexação de cada
base, derivados de `GET /knowledge-bases/indexing-summary` — **uma** requisição
para o conjunto das bases, não uma por base. É esse custo que autoriza as duas
colunas: obtê-las por base custaria uma requisição para cada uma, com 100+ bases
declaradas como volume real.

A contagem de documentos igual a zero SHALL ser exibida, e o sistema SHALL
distingui-la de ausência de dado. O zero de `documentCount` é uma contagem
**medida** — a agregação percorreu os documentos daquela base e não encontrou
nenhum —, ao contrário de `fragmentCount` igual a zero em documento nunca
indexado, que é o default de uma coluna que ninguém escreveu e que a interface
não exibe. As duas contagens têm o mesmo valor e significados opostos.

O resumo de indexação SHALL ser composto pelas contagens de documentos indexados,
de documentos em falha e do complemento não terminal obtido por subtração. O
sistema SHALL NOT distinguir documento pendente de documento em indexação nesta
listagem: o recurso não devolve as duas contagens em separado, por decisão
registrada, e quem precisa da distinção é o detalhe da base, que recebe o estado
por documento.

Quando não houver linha de resumo para uma base — porque a consulta não respondeu
ou porque a base não veio na resposta —, o sistema SHALL indicar que o dado é
desconhecido, e SHALL NOT exibir zero nem contagem nenhuma no lugar. Uma
requisição que não respondeu não é evidência de que a base está vazia.

Quando o resumo não puder ser carregado, a listagem SHALL continuar sendo
exibida, com as demais colunas servindo normalmente.

Quando o catálogo de agentes não puder ser carregado, a coluna SHALL indicar que
o dado é desconhecido, de forma distinta de "nenhum agente consulta" — uma
requisição que não respondeu não é evidência de ausência de vínculo.

A listagem SHALL exibir as linhas na ordem do catálogo de bases, e SHALL casar
cada linha com o seu resumo **por identificador**. O sistema SHALL NOT depender da
posição do item na resposta do resumo, ainda que as duas respostas usem hoje o
mesmo critério de ordenação.

#### Scenario: Listagem mostra nome, descrição e estado
- **WHEN** o operador acessa `/knowledge-bases` e existem bases cadastradas
- **THEN** cada linha exibe o nome da base, sua descrição e um badge de estado
  Ativa ou Inativa

#### Scenario: Listagem inclui bases inativas
- **WHEN** o operador acessa `/knowledge-bases` e existe pelo menos uma base
  desativada
- **THEN** essa base aparece na listagem, com o badge Inativa

#### Scenario: Listagem mostra a contagem de documentos de cada base
- **WHEN** o operador visualiza a listagem e o resumo traz uma base com quatro
  documentos
- **THEN** a linha daquela base exibe a contagem de quatro documentos

#### Scenario: Base sem documento exibe contagem medida, não ausência
- **WHEN** o resumo traz uma base com `documentCount` igual a zero
- **THEN** a linha informa que a base não tem nenhum documento, e o resumo de
  indexação daquela linha fica em branco — não repete a mesma informação nem
  afirma falha

#### Scenario: Resumo de indexação soma indexados, em andamento e falhas
- **WHEN** o resumo traz uma base com `documentCount` cinco, `indexedCount` três
  e `failedCount` um
- **THEN** a linha exibe três indexados, um em andamento e um que falhou

#### Scenario: A célula de indexação não contém pendente nem indexando
- **WHEN** o operador visualiza a coluna de indexação de uma base com documentos
  em qualquer combinação de contagens, inclusive com o complemento não terminal
  maior que um
- **THEN** o texto renderizado da célula **não contém** o termo pendente nem o
  termo indexando, e o complemento aparece como uma **única** parcela — o recurso
  não devolve as duas contagens em separado, e exibir duas parcelas afirmaria uma
  distinção que o dado não carrega

#### Scenario: Base sem linha no resumo não é exibida como zero
- **WHEN** o resumo responde sem incluir uma das bases da listagem
- **THEN** as células de documentos e de indexação daquela linha indicam dado
  desconhecido, e não exibem zero, "nenhum documento" nem contagem alguma

#### Scenario: Resumo indisponível não derruba a listagem
- **WHEN** a consulta do resumo de indexação falha
- **THEN** as bases continuam listadas com nome, descrição, agentes e estado, e
  as colunas de documentos e indexação indicam dado desconhecido

#### Scenario: Cada linha recebe as contagens da sua própria base
- **WHEN** o resumo responde com os itens em ordem diferente da ordem do catálogo
- **THEN** as linhas são exibidas na ordem do catálogo e cada uma exibe as
  contagens correspondentes ao seu próprio identificador

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

O sistema SHALL oferecer um filtro com exatamente quatro opções: todas, ativas,
inativas e com falha. A opção de falha SHALL selecionar as bases cujo
`failedCount` é maior que zero, e SHALL incluir as inativas — desativar uma base
impede o uso pelo agente, não a manutenção do conteúdo.

Quando o resumo de indexação não estiver disponível, a opção de falha SHALL ser
apresentada **desabilitada**, e o sistema SHALL informar que o resumo não pôde ser
carregado. O sistema SHALL NOT apresentar a opção de falha ativa e sem efeito, e
SHALL NOT removê-la do controle.

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
  com `failedCount` maior que zero
- **THEN** apenas essa base permanece na listagem, inclusive quando ela está
  inativa

#### Scenario: Opção de falha fica desabilitada sem o resumo
- **WHEN** a consulta do resumo de indexação falha
- **THEN** a opção de falha é exibida desabilitada e a tela informa que o resumo
  não pôde ser carregado

#### Scenario: Filtro de falha selecionado sem resumo não esvazia a listagem
- **WHEN** a opção de falha está selecionada e o resumo não está disponível
- **THEN** as bases continuam sendo exibidas, e a tela não afirma que nenhuma
  base tem falha

#### Scenario: Busca sem resultado é distinguida de catálogo vazio
- **WHEN** existem bases cadastradas mas nenhuma corresponde ao termo buscado
- **THEN** a tela informa que nenhuma base corresponde à busca, e não que não
  existe base cadastrada

### Requirement: Tom de alerta no resumo de indexação da listagem
O sistema SHALL aplicar tom de alerta **apenas** à parcela de documentos em falha
do resumo de indexação. As parcelas de documentos indexados e de documentos em
andamento SHALL ser apresentadas no tom padrão do texto da listagem.

Documento em andamento é o funcionamento normal do pipeline, não defeito. Pintar a
célula inteira de alerta quando existe qualquer documento não terminal faria a
tela afirmar que a base precisa de atenção em um estado que se resolve sozinho, e
apagaria a diferença entre "está indexando" e "falhou" — que é a única distinção
que essa coluna existe para mostrar.

O tom de alerta SHALL ser o mesmo empregado para documento em falha na listagem
de documentos do detalhe da base, para que falha tenha a mesma aparência nas duas
telas da área.

A cor que carrega esse tom SHALL resolver por esquema de cor — nome de cor
semântica ou variável declarada nos dois esquemas. O sistema SHALL NOT apoiar
esse papel visual em tom fixo da escala neutra, que é claro nos dois esquemas ou
escuro nos dois.

#### Scenario: Só a parcela de falha recebe tom de alerta
- **WHEN** o operador visualiza a linha de uma base com documentos indexados e
  documentos em falha
- **THEN** a parcela que informa a falha é apresentada em tom de alerta, e a
  parcela que informa os indexados não é

#### Scenario: Documento em andamento não recebe tom de alerta
- **WHEN** o operador visualiza a linha de uma base sem nenhuma falha e com
  documentos ainda não terminais
- **THEN** nenhuma parte da célula de indexação é apresentada em tom de alerta

#### Scenario: Falha tem a mesma aparência nas duas telas
- **WHEN** o operador compara a parcela de falha na listagem de bases com o
  estado de documento em falha na listagem de documentos
- **THEN** as duas empregam o mesmo tom de alerta

#### Scenario: O tom não vem de valor fixo da escala neutra
- **WHEN** o código da listagem é verificado quanto a papéis visuais
- **THEN** nenhum tom fixo da escala neutra é usado para esse papel, e a cor
  empregada resolve por esquema

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

Quando nenhum agente consulta a base, o sistema SHALL dizer isso e SHALL
indicar onde o vínculo é feito — a aba Conhecimento do detalhe do agente. A
proibição anterior de direcionar o operador a uma tela de vínculo existia
porque essa tela não existia; ela passa a existir.

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
- **THEN** a tela informa isso e diz que o vínculo é feito na aba Conhecimento
  do detalhe do agente, sem anunciar etapa futura

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
