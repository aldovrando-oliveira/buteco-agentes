# agent-knowledge-binding-ui Specification

## Purpose

A aba **Conhecimento** do detalhe do agente, em `apps/frontend`: ler quais bases
de conhecimento um agente consulta e mudar esse conjunto, consumindo
`PUT /agents/{id}/knowledge-bases` e `GET /knowledge-bases`. É a contraparte de
tela de `agent-knowledge-binding`, que cobre o mesmo vínculo do lado do cadastro
em `apps/api`.

A capability existe por uma pergunta de operação, não por CRUD: **por que este
agente está respondendo sem contexto?** É o sintoma que traz o operador até
aqui, e por isso o estado vazio explica a consequência de não haver vínculo em
vez de só informar a ausência, e a base vinculada e inativa recebe aviso próprio
— o backend aceita esse vínculo de propósito, porque o filtro por estado pertence
à resolução em runtime, e sem a tela dizer isso o operador não tem como saber por
que a consulta não devolve nada.

Duas escolhas de contrato moldam tudo o mais aqui, e é por elas que esta
capability se distingue das outras duas abas de vínculo do mesmo detalhe:

- **O vínculo é substituição do conjunto inteiro**, não operação por base. Daí a
  gravação acontecer uma vez, pela barra de alterações não salvas, e não a cada
  clique: indicar progresso ou falha por linha afirmaria sobre uma base
  específica algo que o contrato não distingue.
- **A tela não sabe nada sobre documentos nem indexação.**
  `KnowledgeBaseResponse` carrega identificador, nome, descrição, estado e datas;
  contagem de documento exigiria uma requisição por base vinculada. Os requisitos
  de asserção negativa daqui existem para impedir que alguém "complete" a linha
  com zero mais tarde — zero afirmaria que a contagem foi feita.

## Requirements

### Requirement: Aba Conhecimento no detalhe do agente

O sistema SHALL prover, em `apps/frontend`, uma aba **Conhecimento** dentro da
página de detalhe do agente, que permite visualizar e substituir o conjunto de
bases de conhecimento vinculadas ao agente.

A aba SHALL montar cada linha cruzando os **identificadores** vindos de
`agent.knowledgeBases` com o **catálogo** obtido de `GET /knowledge-bases`,
porque `AgentResponse.knowledgeBases` carrega apenas `id` e `name` e a
descrição e o estado da base pertencem ao catálogo.

A aba SHALL exibir, acima da lista, um resumo textual que distingue o caso sem
nenhuma base vinculada do caso com uma ou mais, e SHALL exibir, no cabeçalho do
card, o total de bases existentes no catálogo.

#### Scenario: Aba carregada com bases vinculadas
- **WHEN** o operador abre a aba Conhecimento de um agente que tem bases
  vinculadas
- **THEN** a interface exibe uma linha por base vinculada, cada uma com o nome
  da base como link para o detalhe dela e a descrição da base, mais um resumo
  da quantidade vinculada e o total do catálogo

#### Scenario: Descrição e estado vêm do catálogo, não do vínculo
- **WHEN** o operador abre a aba Conhecimento de um agente cujo vínculo carrega
  apenas identificador e nome de cada base
- **THEN** a interface exibe a descrição e o estado de cada base obtidos do
  catálogo de bases de conhecimento

#### Scenario: Ordem estável da lista
- **WHEN** a aba exibe duas ou mais bases vinculadas, incluindo bases de nome
  igual
- **THEN** a interface as ordena por nome, com desempate por identificador,
  reproduzindo a ordem que a API devolve, de forma que a lista não se reordene
  ao salvar

### Requirement: Estado vazio explicativo da aba Conhecimento

O sistema SHALL exibir, quando o agente não tem nenhuma base de conhecimento
vinculada, um estado vazio que explica a consequência — que o agente responde
apenas com o system prompt, sem nada para consultar — e oferece a ação de
vincular. O estado vazio SHALL NOT ser reduzido a uma única linha de texto.

#### Scenario: Agente sem nenhuma base vinculada
- **WHEN** o operador abre a aba Conhecimento de um agente sem nenhuma base
  vinculada
- **THEN** a interface exibe um bloco explicando que, sem vínculo, o agente
  responde só com o system prompt, junto da ação de vincular base

#### Scenario: Resumo distingue vazio de não vazio
- **WHEN** o agente exibido não tem nenhuma base vinculada
- **THEN** o resumo acima da lista afirma que o agente não consulta
  conhecimento, e não uma contagem zero

### Requirement: Aviso de base inativa vinculada

O sistema SHALL exibir, na linha de cada base vinculada que esteja inativa, um
aviso de que a base continua vinculada e não é consultada enquanto estiver
desativada. O sistema SHALL NOT exibir esse aviso em base ativa.

Este aviso é a contraparte de produto da decisão do backend de aceitar vínculo
com base inativa: o filtro por estado pertence à resolução em runtime, não à
remoção do vínculo.

#### Scenario: Base vinculada e inativa
- **WHEN** o operador abre a aba Conhecimento de um agente com uma base
  vinculada que está inativa
- **THEN** a interface exibe, na linha dessa base, o aviso de que ela não é
  consultada enquanto estiver desativada

#### Scenario: Base vinculada e ativa não recebe aviso
- **WHEN** todas as bases vinculadas do agente estão ativas
- **THEN** a interface não exibe nenhum aviso de base inativa

### Requirement: A aba não afirma nada sobre documentos ou indexação

O sistema SHALL NOT exibir, na aba Conhecimento nem no modal de vincular,
contagem de documentos, quantidade de documentos indexados, estado de
indexação, ou qualquer aviso derivado deles.

`KnowledgeBaseResponse` carrega exatamente identificador, nome, descrição,
estado e as duas datas; documento vive em rota própria por base, o que tornaria
a tela dependente de uma requisição por base vinculada. Exibir zero afirmaria
que a contagem foi feita e deu zero.

#### Scenario: Linha da lista sem contagem de documentos
- **WHEN** o operador abre a aba Conhecimento de um agente com bases vinculadas
- **THEN** nenhuma linha exibe contagem de documentos nem quantidade de
  documentos indexados

#### Scenario: Linha do modal sem contagem de documentos
- **WHEN** o operador abre o modal de vincular base
- **THEN** nenhuma linha do modal exibe contagem de documentos nem quantidade
  de documentos indexados

#### Scenario: Ausência de aviso derivado de indexação
- **WHEN** o operador abre a aba Conhecimento de um agente com bases vinculadas
- **THEN** a interface não exibe nenhum aviso sobre base sem documento indexado

### Requirement: Modal de vincular base com busca sobre o catálogo

O sistema SHALL oferecer, a partir da aba Conhecimento, um modal que lista o
catálogo completo de bases de conhecimento, com busca por nome e descrição
executada no cliente sobre a resposta inteira e **insensível a sinais
diacríticos**, no mesmo tratamento de busca das demais listas do painel.

Cada linha do modal SHALL exibir o nome e a descrição da base, uma indicação
quando a base estiver inativa, e um controle que alterna entre vincular e
vinculada conforme a base já esteja escolhida.

Escolher uma base SHALL NOT fechar o modal, de forma que várias possam ser
escolhidas em sequência.

O modal SHALL oferecer, no rodapé, um caminho para criar uma nova base e uma
ação que apenas fecha o modal.

#### Scenario: Busca sem acento encontra base acentuada
- **WHEN** o operador digita um termo sem sinais diacríticos que corresponde ao
  nome de uma base acentuada
- **THEN** o modal exibe essa base entre os resultados

#### Scenario: Busca alcança a descrição
- **WHEN** o operador digita um termo presente apenas na descrição de uma base
- **THEN** o modal exibe essa base entre os resultados

#### Scenario: Alternância entre vincular e vinculada
- **WHEN** o operador aciona o controle de uma base ainda não escolhida
- **THEN** a base passa a constar como escolhida, o controle passa a indicar
  que ela está vinculada, e o modal permanece aberto

#### Scenario: Base inativa identificada no modal
- **WHEN** o catálogo contém uma base inativa
- **THEN** a linha dessa base no modal indica que ela está inativa

#### Scenario: Busca sem correspondência
- **WHEN** o termo buscado não corresponde a nenhuma base do catálogo
- **THEN** o modal informa que nenhuma base corresponde à busca

#### Scenario: Catálogo vazio
- **WHEN** não existe nenhuma base de conhecimento cadastrada
- **THEN** o modal informa que nenhuma base foi cadastrada ainda, com texto
  distinto do de busca sem correspondência

#### Scenario: Fechar o modal não descarta as escolhas
- **WHEN** o operador escolhe uma ou mais bases e aciona a ação de concluir
- **THEN** o modal fecha e as bases escolhidas permanecem na lista da aba,
  ainda não gravadas

### Requirement: Gravação do vínculo por substituição do conjunto inteiro

O sistema SHALL acumular as escolhas do operador em um rascunho local e gravá-las
em uma única requisição `PUT /agents/{id}/knowledge-bases`, enviando **sempre o
conjunto completo resultante** de identificadores, nunca uma diferença.

O sistema SHALL exibir uma barra de alterações não salvas enquanto o rascunho
diferir do vínculo gravado, com as ações de salvar e de descartar, e SHALL
guardar a navegação para fora enquanto houver rascunho não salvo — o mesmo
idioma das demais abas de vínculo do detalhe do agente.

O sistema SHALL NOT emitir requisição ao vincular ou desvincular uma base
individualmente, e SHALL NOT exibir indicação de gravação por linha: a operação
é de conjunto, e indicar progresso ou falha por linha afirmaria sobre uma base
específica algo que o contrato não distingue.

Ao concluir a gravação com sucesso, o sistema SHALL rebasear o rascunho na
resposta recebida, de forma que uma gravação seguinte não reenvie estado
anterior.

#### Scenario: Vincular e salvar
- **WHEN** o operador escolhe uma base ainda não vinculada e aciona salvar
- **THEN** o sistema emite uma requisição de substituição contendo os
  identificadores das bases já vinculadas mais o da base escolhida, e notifica
  o sucesso

#### Scenario: Desvincular e salvar
- **WHEN** o operador remove uma base vinculada e aciona salvar
- **THEN** o sistema emite uma requisição de substituição contendo os
  identificadores das bases restantes, sem o da base removida

#### Scenario: Remover todas as bases vinculadas
- **WHEN** o operador remove todas as bases vinculadas e aciona salvar
- **THEN** o sistema emite uma requisição de substituição com o conjunto vazio,
  e a aba passa a exibir o estado vazio

#### Scenario: Nenhuma requisição ao alternar uma base
- **WHEN** o operador vincula ou desvincula uma base sem acionar salvar
- **THEN** nenhuma requisição de vínculo é emitida, e a lista reflete a escolha
  apenas localmente

#### Scenario: Barra de alterações não salvas
- **WHEN** o rascunho difere do vínculo gravado
- **THEN** a interface exibe a barra de alterações não salvas, com as ações de
  salvar e de descartar

#### Scenario: Descartar volta ao vínculo gravado
- **WHEN** o operador aciona descartar depois de alterar o rascunho
- **THEN** a lista volta a exibir exatamente as bases gravadas e a barra
  desaparece

#### Scenario: Guarda de navegação com rascunho não salvo
- **WHEN** o operador tenta sair da página com rascunho não salvo, inclusive
  pelo caminho de criar uma nova base a partir do modal
- **THEN** a interface pede confirmação antes de descartar as alterações

#### Scenario: Falha na gravação preserva o rascunho
- **WHEN** a requisição de substituição falha
- **THEN** a interface notifica o erro, mantém o rascunho intacto na tela, e
  não indica falha em nenhuma linha específica

#### Scenario: Sucesso rebaseia o rascunho na resposta
- **WHEN** a gravação conclui com sucesso
- **THEN** a barra de alterações não salvas desaparece, e uma gravação seguinte
  parte do conjunto devolvido pela resposta

### Requirement: Catálogo de bases consultado apenas com a aba ativa

O sistema SHALL requisitar `GET /knowledge-bases` somente enquanto a aba
Conhecimento estiver ativa, e SHALL informar a falha dessa consulta sem
derrubar a página nem exibir a lista pela metade.

#### Scenario: Catálogo não requisitado nas demais abas
- **WHEN** o operador abre a página de detalhe do agente em qualquer aba que
  não a de conhecimento
- **THEN** o catálogo de bases de conhecimento não é requisitado

#### Scenario: Falha ao carregar o catálogo
- **WHEN** a consulta ao catálogo de bases falha com a aba de conhecimento
  ativa
- **THEN** a interface informa que não foi possível carregar as bases de
  conhecimento, não exibe a lista de vinculadas, e o restante da página
  continua utilizável
