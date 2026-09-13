# knowledge-index-diagnostics-ui Specification

## Purpose

A aba **Diagnóstico do índice** no detalhe de uma base de conhecimento, no painel
do operador. Ela existe para um estado específico e nomeado: `apps/workers`
**recusa o boot** porque o índice tem mais de uma combinação de provedor, modelo
e dimensão de embedding, `apps/api` continua de pé, e o operador abre o painel
para entender por quê. Consome `GET /knowledge-index/diagnostics`, cujo requisito
de backend vive em `knowledge-document-indexing`.

Três regras atravessam a capability, e as três são sobre **escala e proveniência
do que a tela afirma**.

A primeira é que **a proveniência é do sistema, não da base**. A rota não aceita
identificador de base e o schema torna impossível bases com proveniências
diferentes — a dimensão é fixada pelo tipo da coluna. A aba separa fisicamente as
duas escalas, e diz qual é qual: o que está gravado no índice **inteiro** num
grupo rotulado como do sistema, e o volume **desta base** noutro, derivado da
listagem de documentos que a tela já carregou, sem requisição adicional.

A segunda é a de não afirmar o que o sistema não sabe, e aqui ela tem **quatro**
estados a distinguir, não três: valor, zero medido, desconhecido — e **"sei que
não existe"**, que é o índice vazio. A rota responde `200` com lista vazia, e isso
é uma resposta que chegou, não uma pergunta sem resposta: a tela **explica** em vez
de preencher com travessão, e não insinua qual modelo *seria* usado, porque isso é
configuração de outro processo. Falha ao ler a proveniência é dita como falha, e
nunca como índice vazio. E a contagem de fragmentos da base é governada por
`indexedAt`, nunca pelo estado, porque documento que falhou ao reindexar continua
com os fragmentos anteriores respondendo.

A terceira é que **a tela não elege a combinação certa**. Com mais de uma
combinação ela nomeia a corrupção, mostra todas com a contagem de fragmentos de
cada uma — que é o número que torna a reindexação decidível —, e não marca nenhuma
como atual, correta ou configurada: `apps/api` não conhece a configuração
declarada, e a rota não diz qual é a pretendida. Pelo mesmo motivo a aba não
oferece reindexação em massa, que não existe no backend, e não afirma o estado de
execução de `apps/workers`, que ela não mede.

## Requirements

### Requirement: Aba de diagnóstico do índice no detalhe da base
O sistema SHALL exibir, no detalhe de uma base de conhecimento em
`apps/frontend`, uma aba **Diagnóstico do índice**, com a aba ativa refletida no
endereço.

A aba SHALL apresentar o seu conteúdo em **dois grupos separados e rotulados por
escala**: um grupo do **sistema**, alimentado por
`GET /knowledge-index/diagnostics`, e um grupo **desta base**, derivado da
listagem de documentos que o detalhe já carrega.

O rótulo do grupo do sistema SHALL identificá-lo como do sistema. Empilhar as
duas escalas em uma lista única, sob um cabeçalho que descreve apenas uma delas,
SHALL NOT ser feito: com a proveniência global, esse cabeçalho afirma que os
dados da base são do sistema, ou que os do sistema são da base — as duas leituras
são falsas.

A aba SHALL NOT emitir qualquer requisição que não seja a de
`GET /knowledge-index/diagnostics`. As contagens desta base são derivadas da
listagem de documentos já carregada.

#### Scenario: A aba existe e separa as duas escalas
- **WHEN** o operador abre a aba de diagnóstico do índice de uma base
- **THEN** a tela apresenta dois grupos distintos, um rotulado como do sistema e
  outro como desta base

#### Scenario: Nenhuma requisição adicional é emitida pela aba
- **WHEN** o operador abre a aba de diagnóstico do índice
- **THEN** a única requisição emitida pela aba é a da proveniência do índice, e
  nenhuma listagem de documentos adicional é pedida

### Requirement: A proveniência exibida é a do índice inteiro, nunca da base
O sistema SHALL apresentar provedor, modelo e dimensão como propriedade do
**índice de conhecimento inteiro**, e SHALL NOT apresentá-los como propriedade da
base sendo visualizada.

A requisição da proveniência SHALL NOT carregar identificador de base. A rota é
global por decisão do backend, e o schema torna impossível bases com
proveniências diferentes — uma consulta por base produziria a anomalia de uma
base exibir travessão enquanto a base ao lado, com o mesmo índice, exibe o
modelo.

#### Scenario: Duas bases exibem a mesma proveniência
- **WHEN** o operador abre a aba de diagnóstico de duas bases diferentes, com o
  mesmo índice
- **THEN** as duas exibem a mesma proveniência

#### Scenario: A requisição não leva identificador de base
- **WHEN** a aba de diagnóstico busca a proveniência
- **THEN** o endereço requisitado não contém o identificador da base

### Requirement: Índice vazio não afirma configuração
O sistema SHALL tratar a resposta **vazia** da proveniência como o estado
"índice vazio", e SHALL explicar que provedor, modelo e dimensão passam a existir
quando o primeiro documento terminar de indexar, porque são lidos do índice e não
da configuração pretendida.

A interface SHALL NOT exibir, nesse estado, nome de provedor, nome de modelo ou
valor de dimensão de qualquer origem. Não há o que exibir: o que existe é
configuração de outro processo, e afirmar qual modelo *seria* usado é afirmar
mais do que o sistema sabe por esta rota.

O gate do vazio SHALL ser a vacuidade do **índice inteiro** — a resposta vazia da
rota —, e SHALL NOT ser a contagem de documentos indexados da base. Com a
proveniência global, um gate por base nega um fato que o sistema conhece.

A explicação do estado vazio SHALL NOT dizer que o vazio é "desta base".

Nesse estado, a interface SHALL NOT renderizar as linhas de provedor, modelo e
dimensão com travessão. Nesta área do painel o travessão significa **dado
desconhecido** — a consulta não respondeu, ou o registro não veio —, e índice
vazio é fato conhecido e medido. Usar o mesmo símbolo para os dois apaga a
distinção justamente na tela em que o desconhecido também existe.

#### Scenario: Resposta vazia explica em vez de afirmar
- **WHEN** a rota de proveniência responde com lista vazia
- **THEN** a aba explica que o índice está vazio e que os dados aparecem após a
  primeira indexação, sem exibir nome de provedor, nome de modelo ou dimensão

#### Scenario: O vazio não é atribuído à base
- **WHEN** a rota de proveniência responde com lista vazia
- **THEN** o texto exibido não afirma que o vazio é uma propriedade desta base

#### Scenario: O vazio não é representado por travessão
- **WHEN** a rota de proveniência responde com lista vazia
- **THEN** o grupo do sistema não contém linhas de provedor, modelo ou dimensão
  preenchidas com travessão

#### Scenario: Base sem documento com índice povoado exibe a proveniência
- **WHEN** o operador abre a aba de diagnóstico de uma base sem nenhum documento,
  e o índice tem uma combinação gravada
- **THEN** a proveniência do sistema é exibida normalmente, e apenas o grupo desta
  base informa a ausência de conteúdo

### Requirement: Mais de uma combinação é nomeada como corrupção
O sistema SHALL exibir **todas** as combinações devolvidas pela rota, cada uma com
a sua contagem de fragmentos.

Quando houver mais de uma, a interface SHALL nomear o estado como corrupção do
índice e SHALL explicar a consequência: vetores de modelos diferentes são
incomparáveis, a busca continua devolvendo resultados sem erro, e a checagem de
integridade de `apps/workers` recusa o boot nesse estado. A contagem de
fragmentos de cada combinação SHALL ser exibida, porque é ela que torna a
reindexação decidível.

Mais de uma combinação SHALL NOT ser tratada como erro de requisição: a resposta
é `200`, e a tela existe principalmente para esse estado.

A interface SHALL NOT eleger uma das combinações como a correta, a atual ou a
configurada. `apps/api` não conhece a configuração declarada de embedding, e a
rota não informa qual combinação é a pretendida.

A interface SHALL NOT oferecer ação de reindexação em massa: ela não existe no
backend.

A interface SHALL NOT afirmar o estado de execução de `apps/workers` — que o
processo está no chão, ou de pé. Nenhuma medição desse processo chega a esta
tela.

#### Scenario: Duas combinações são exibidas com as suas contagens
- **WHEN** a rota devolve duas combinações
- **THEN** as duas são exibidas, cada uma com o seu provedor, modelo, dimensão e
  contagem de fragmentos

#### Scenario: A corrupção é nomeada
- **WHEN** a rota devolve mais de uma combinação
- **THEN** a tela nomeia o estado como corrupção do índice e explica a
  consequência para a busca e para o boot da indexação

#### Scenario: Nenhuma combinação é marcada como a correta
- **WHEN** a rota devolve mais de uma combinação
- **THEN** nenhuma delas é apresentada como atual, correta ou configurada

#### Scenario: Não há ação de reindexação em massa
- **WHEN** o operador visualiza a aba em qualquer estado
- **THEN** nenhuma ação de reindexar o acervo inteiro é oferecida

### Requirement: A ordem das combinações é a da resposta
O sistema SHALL renderizar as combinações na ordem em que a API as devolveu, e
SHALL NOT reordená-las no cliente.

A ordem é produzida pela collation do banco, na própria consulta, por exigência
de `api-response-ordering`. Reordenar no cliente introduziria um segundo
comparador — o do .NET contra o do PostgreSQL, que discordam de fato — e faria a
tela exibir uma ordem que a API não produziu.

#### Scenario: A ordem do DOM é a ordem da resposta
- **WHEN** a rota devolve combinações em ordem não alfabética para o comparador do
  cliente
- **THEN** a tela as exibe exatamente nessa ordem

### Requirement: O volume desta base é governado por `indexedAt`
O sistema SHALL derivar as contagens desta base da listagem de documentos já
carregada, usando como predicado **`indexedAt` não nulo**, qualquer que seja o
estado de indexação do documento.

O somatório de fragmentos SHALL NOT filtrar por estado. Documento que indexou e
depois falhou ao reindexar continua com os fragmentos anteriores vivos no índice,
respondendo às consultas do agente — somar apenas os documentos em estado
`Indexed` informa menos fragmentos do que o índice tem.

O rótulo da contagem de documentos SHALL descrever o predicado que ela mede, e
SHALL NOT reusar a palavra empregada pelo badge de estado `Indexado` na listagem
de documentos. Com o predicado correto, um documento apresentado como `Falhou` na
listagem conta nesta linha, e duas telas vizinhas usando a mesma palavra com
predicados diferentes é a confusão que essa regra já custou uma vez.

A regra SHALL ser protegida em **dois níveis**, como a distinção entre `pendente`
e `indexando` na listagem de bases: a função pura afirma o **predicado**
(`indexedAt` não nulo, sem filtrar por estado), e o componente afirma o **texto
renderizado**.

A asserção sobre o rótulo SHALL ser feita sobre o texto renderizado no documento,
e SHALL NOT comparar uma constante do teste com a mesma constante do código.
Comparar constante com constante passa verde quando alguém devolve o rótulo para
a palavra familiar — que é exatamente o movimento que a regra existe para
impedir, porque `Documentos indexados` lê melhor e o rótulo correto parece
burocrático.

#### Scenario: Documento que falhou depois de indexar conta no volume
- **WHEN** a base tem um documento com `indexedAt` preenchido, estado `Failed` e
  contagem de fragmentos maior que zero
- **THEN** os fragmentos dele entram no total desta base, e ele conta na linha de
  documentos com fragmentos no índice

#### Scenario: Documento nunca indexado não conta
- **WHEN** a base tem um documento com `indexedAt` nulo
- **THEN** ele não entra no total de fragmentos nem na contagem de documentos com
  fragmentos no índice

#### Scenario: O rótulo renderizado não reusa a palavra do badge de estado
- **WHEN** o operador visualiza o grupo de volume desta base
- **THEN** o texto renderizado da linha de contagem de documentos não contém a
  palavra empregada pelo badge de estado `Indexado` da listagem de documentos

#### Scenario: O predicado é afirmado separadamente do rótulo
- **WHEN** a regra da contagem é verificada
- **THEN** existe uma asserção sobre a função pura, que afirma o predicado sem
  montar componente, e uma asserção sobre o texto renderizado, que afirma o
  rótulo — e nenhuma das duas sozinha cobre o que a outra cobre

### Requirement: A aba não repete a lista de documentos em falha
O sistema SHALL informar, no grupo desta base, **quantos** documentos estão em
falha, e SHALL oferecer um caminho para a listagem de documentos, onde o motivo
completo e a ação de reindexar já vivem.

A aba SHALL NOT repetir o motivo da falha nem a ação de reindexar. A faixa de
falha da listagem de documentos é a cópia única desse estado: duplicá-la cria
duas superfícies que disparam a mesma mutação, com estados de carregamento
independentes, e dois lugares onde a cópia do motivo pode divergir.

Quando não houver documento em falha, a linha SHALL ser omitida, e SHALL NOT ser
exibida como contagem zero em tom de alerta.

#### Scenario: A aba informa a contagem e aponta
- **WHEN** a base tem documentos em falha e o operador abre a aba de diagnóstico
- **THEN** a aba informa quantos são e oferece caminho para a listagem de
  documentos

#### Scenario: A aba não repete motivo nem ação
- **WHEN** a base tem documentos em falha e o operador abre a aba de diagnóstico
- **THEN** a aba não exibe o texto do motivo da falha nem oferece ação de
  reindexar documento

#### Scenario: Sem falha, a linha some
- **WHEN** a base não tem documento em falha
- **THEN** nenhuma linha de falha é exibida na aba

### Requirement: Falha ao ler a proveniência não é índice vazio
O sistema SHALL distinguir, na aba, quatro situações do grupo do sistema:
carregando, índice vazio, índice com proveniência e **falha ao ler**.

Quando a consulta falha, a interface SHALL informar que a proveniência não pôde
ser lida, e SHALL NOT apresentar o texto de índice vazio nem qualquer contagem.
Uma requisição que não respondeu não é evidência de ausência.

#### Scenario: Erro informa indisponibilidade
- **WHEN** a consulta da proveniência falha
- **THEN** a aba informa que não foi possível ler a proveniência do índice

#### Scenario: Erro não vira índice vazio
- **WHEN** a consulta da proveniência falha
- **THEN** a aba não exibe o texto que descreve o índice vazio

#### Scenario: Listagem de documentos indisponível não vira zero
- **WHEN** a listagem de documentos da base não pôde ser lida
- **THEN** o grupo desta base informa a indisponibilidade e não exibe contagens
  zeradas

### Requirement: A proveniência é buscada apenas com a aba ativa, e acompanha a única transição que a muda
O sistema SHALL buscar a proveniência do índice **somente** quando a aba de
diagnóstico estiver ativa.

A consulta SHALL repetir-se automaticamente **apenas enquanto** o índice estiver
vazio **e** a base tiver documento em estado não terminal — a única transição que
muda a proveniência é o primeiro documento a terminar de indexar. Com o índice já
povoado, a consulta SHALL NOT repetir-se, porque a rota percorre o heap inteiro
da tabela de fragmentos e o valor não muda mais.

A condição SHALL viver em função pura exportada, testável sem timer.

#### Scenario: Aba inativa não busca
- **WHEN** o operador está na aba de documentos
- **THEN** nenhuma requisição de proveniência é emitida

#### Scenario: Índice vazio com documento em andamento acompanha
- **WHEN** a aba de diagnóstico está ativa, a proveniência está vazia e a base tem
  documento em estado não terminal
- **THEN** a consulta é repetida em intervalo, até o estado mudar

#### Scenario: Índice povoado não acompanha
- **WHEN** a aba de diagnóstico está ativa e a proveniência já tem pelo menos uma
  combinação
- **THEN** a consulta não é repetida em intervalo

#### Scenario: Índice vazio sem documento em andamento não acompanha
- **WHEN** a aba de diagnóstico está ativa, a proveniência está vazia e todos os
  documentos da base estão em estado terminal
- **THEN** a consulta não é repetida em intervalo

### Requirement: A aba não afirma o que o sistema não coleta
O sistema SHALL apresentar a aba como somente leitura, sem formulário para
provedor, modelo ou dimensão — são configuração de processo, validada no boot da
indexação.

A aba SHALL NOT exibir barra de progresso percentual: o sistema registra o estado
de cada documento, não o percentual de conclusão.

A aba SHALL NOT exibir métrica de uso — consultas por base, popularidade de
documento ou taxa de acerto. Nada disso é coletado.

A aba SHALL declarar, em uma linha, que esses dados não são coletados, para que a
ausência não seja lida como defeito.

Tom de alerta SHALL ser aplicado **apenas** ao que é falha — documentos em falha e
corrupção do índice —, nunca a documento em andamento, que é o funcionamento
normal do pipeline. A cor que carrega esse papel SHALL resolver por esquema de
cor, e SHALL NOT ser tom fixo da escala neutra.

#### Scenario: Não há formulário de configuração
- **WHEN** o operador visualiza a aba de diagnóstico
- **THEN** nenhum campo editável de provedor, modelo ou dimensão é oferecido

#### Scenario: Não há progresso percentual nem métrica de uso
- **WHEN** o operador visualiza a aba em qualquer estado
- **THEN** nenhuma barra de progresso percentual e nenhuma contagem de consultas
  ou acessos é exibida, e a tela declara que esses dados não são coletados

#### Scenario: Documento em andamento não recebe tom de alerta
- **WHEN** a base tem documentos não terminais e nenhuma falha
- **THEN** nenhuma parte da aba é apresentada em tom de alerta

#### Scenario: O tom não vem de valor fixo da escala neutra
- **WHEN** o código da aba é verificado quanto a papéis visuais
- **THEN** nenhum tom fixo da escala neutra é usado para papel que troca de ponta
  entre os esquemas, e as cores empregadas resolvem por esquema
