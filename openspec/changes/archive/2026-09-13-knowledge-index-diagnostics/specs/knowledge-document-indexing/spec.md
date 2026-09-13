## ADDED Requirements

### Requirement: Proveniência gravada do índice é servida por rota
O sistema SHALL oferecer, via `apps/api`, um recurso que devolve a
**proveniência gravada no índice de conhecimento**:
`GET /knowledge-index/diagnostics`.

A resposta SHALL ser a lista das **combinações distintas** de provedor, modelo e
dimensão de embedding presentes nos fragmentos, cada item com quatro campos:
`provider`, `model`, `dimensions` e `fragmentCount` — o número de fragmentos
gravados com aquela combinação.

A rota SHALL ser **global**: NÃO SHALL aceitar identificador de base de
conhecimento, e SHALL agregar o índice inteiro. Provedor, modelo e dimensão são
propriedade do **sistema**, não da base — a dimensão é fixada pelo tipo da coluna
(`vector(4096)`), que recusa qualquer vetor de outra dimensão, e a checagem de
integridade do boot de `apps/workers` exige combinação única no índice **inteiro**.
Uma rota por base afirmaria que bases diferentes podem ter proveniências
diferentes, o que o schema torna impossível.

A resposta SHALL refletir o que está **gravado**, nunca o que a configuração
declara. NÃO SHALL existir, na resposta, nenhum valor vindo de configuração de
embedding: `apps/api` não tem essa configuração, e introduzi-la criaria a segunda
fonte do mesmo valor.

**Mais de um item SHALL ser uma resposta válida desta rota**, não um erro. É o
estado em que `apps/workers` se recusa a subir — índice com vetores de modelos
incomparáveis — enquanto `apps/api` continua de pé; é exatamente nesse estado que
o operador abre a tela, e a contagem por combinação é o que torna a reindexação
decidível.

Índice vazio SHALL responder HTTP 200 com **lista vazia**, nunca HTTP 404 e nunca
um item com campos nulos ou zerados.

A lista SHALL ter ordenação determinística por `provider`, `model` e
`dimensions`, produzida pela **consulta** e não por comparação em memória. Os três
campos juntos são o identificador da combinação e são únicos por construção — são
a própria chave de agrupamento —, então o desempate por identificador que toda
lista ordenada de `apps/api` exige está satisfeito pelo critério primário, sem
quarto critério.

O custo da resposta SHALL ser de **uma única consulta**, independente do número
de bases e do número de documentos — nunca uma consulta por base.

A rota SHALL exigir operador autenticado, e SHALL recusar o token de serviço de
`apps/inbox`.

#### Scenario: Índice vazio devolve lista vazia, sem afirmar proveniência
- **WHEN** um cliente autenticado envia `GET /knowledge-index/diagnostics` e não
  existe nenhum fragmento gravado
- **THEN** a API responde HTTP 200 com uma lista vazia, nunca HTTP 404, e a
  resposta não contém nenhum nome de provedor, nome de modelo nem dimensão

#### Scenario: Uma combinação devolve um item com a contagem exata
- **WHEN** existem fragmentos gravados, todos com o mesmo provedor, modelo e
  dimensão
- **THEN** a resposta traz **um** item, com aquele provedor, aquele modelo,
  aquela dimensão, e `fragmentCount` igual ao número de fragmentos gravados

#### Scenario: Duas combinações devolvem dois itens, cada um com a sua contagem
- **WHEN** o índice contém fragmentos de **dois** modelos distintos — o estado que
  reprova o boot de `apps/workers`
- **THEN** a resposta é HTTP 200 com **dois** itens, cada um com o seu
  `fragmentCount`, e a rota não trata isso como erro

#### Scenario: Bases diferentes com a mesma combinação devolvem um item só
- **WHEN** duas bases de conhecimento distintas têm fragmentos gravados com o
  mesmo provedor, modelo e dimensão
- **THEN** a resposta traz **um** item, com `fragmentCount` igual à soma dos
  fragmentos das duas bases — a agregação é do índice, não da base

#### Scenario: A rota não aceita identificador de base
- **WHEN** um cliente tenta alcançar a proveniência por uma rota sob o recurso de
  bases, como `/knowledge-bases/{id}/index-diagnostics`
- **THEN** não existe tal rota, e a proveniência é servida apenas pelo recurso
  global

#### Scenario: A ordem dos itens é determinística
- **WHEN** o índice contém mais de uma combinação e a rota é chamada duas vezes
- **THEN** os itens saem na mesma ordem nas duas respostas, crescente por
  `provider`, depois `model`, depois `dimensions`

#### Scenario: O custo não cresce com o número de bases nem de documentos
- **WHEN** a proveniência é pedida com fragmentos espalhados por várias bases e
  vários documentos
- **THEN** `apps/api` emite **uma** consulta ao banco, nunca uma por base nem uma
  por documento

#### Scenario: Requisição sem token é recusada
- **WHEN** um cliente envia `GET /knowledge-index/diagnostics` sem token de
  operador
- **THEN** a API responde HTTP 401, pela política padrão que cobre toda rota não
  marcada como anônima

#### Scenario: Token de serviço é recusado
- **WHEN** a requisição chega com o token de serviço de `apps/inbox`, válido
  estruturalmente
- **THEN** a API responde HTTP 403, porque esta rota não está entre as que o
  serviço consome

#### Scenario: Os nomes dos campos no fio são os declarados
- **WHEN** a resposta de um índice com uma combinação é lida como **texto**
- **THEN** o JSON contém as chaves `provider`, `model`, `dimensions` e
  `fragmentCount`, com `dimensions` e `fragmentCount` como números
