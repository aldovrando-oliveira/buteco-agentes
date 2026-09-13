## ADDED Requirements

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

## MODIFIED Requirements

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
para o conjunto das bases, não uma por base.

**O que mudou, e por quê.** Até esta etapa a listagem era proibida de exibir os
dois: nenhum deles existia em `KnowledgeBaseResponse`, e obtê-los exigiria uma
requisição por base, com 100+ bases declaradas como volume real. A regra não
mudou — o dado mudou. O resumo de indexação passou a existir como recurso próprio,
com custo independente do número de bases, e é isso que autoriza as colunas.

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
`failedCount` é maior que zero.

**O que mudou, e por quê.** O filtro tinha exatamente três opções, e a quarta era
proibida porque dependia de estado de indexação agregado por base, que a API não
devolvia; oferecê-la vazia ou inerte seria pior que não oferecê-la. O resumo por
base passou a existir, e com ele a opção passa a ter o que filtrar.

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
