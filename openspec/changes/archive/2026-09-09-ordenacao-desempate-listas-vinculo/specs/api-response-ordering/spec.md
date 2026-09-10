## Purpose

Responde a uma pergunta que nenhuma capability de domínio responde: **a ordem
de uma lista que `apps/api` devolve é uma garantia, ou um acidente do plano de
consulta?** Aqui ela é garantia — todo critério de ordenação exposto termina em
desempate por identificador, e uma mesma lista sai na mesma ordem nas duas
superfícies que a servem (consulta por id e listagem).

O que a distingue das vizinhas: `agent-catalog`, `agent-mcp-binding`,
`agent-delegation-binding`, `mcp-server-catalog`, `knowledge-base-catalog` e
`knowledge-document-catalog` são donas de **quais** itens cada resposta traz e
de **quais campos** cada item tem. Nenhuma é dona da **ordem**, e a ordem é
justamente o que era indefinido em oito consultas ao mesmo tempo, por uma causa
única: o critério ordenado não é único por construção. Esta capability é dona
dessa causa e da decisão que a resolve — **qual comparador é o canônico da
API** —, que é uma decisão só e não teria onde morar se fosse repetida em seis
specs de domínio.

Não é dona de **qual** critério cada lista usa. Catálogo em ordem de cadastro e
vínculo em ordem de nome continuam sendo escolha de cada capability de domínio;
o que esta exige é que, qualquer que seja o critério, ele termine determinístico.

## ADDED Requirements

### Requirement: Desempate estável por identificador em toda lista ordenada
Toda consulta de `apps/api` que ordena uma lista de resposta SHALL terminar a
sua ordenação por um desempate no identificador da entidade ordenada, como
último critério, sem substituir o critério primário.

O desempate é requisito, não detalhe de implementação, porque nenhum critério
primário em uso é único por construção. Nome não é único em nenhum catálogo de
`apps/api`: não existe índice único de nome no `AppDbContext`, nenhum handler de
criação valida nome duplicado, e `knowledge-base-catalog` tem o cenário "Nome
duplicado é permitido" como requisito. `CreatedAt` também não é único: é
atribuído no construtor da entidade e dois registros podem compartilhar o
instante. Sem desempate, a ordem entre empatados é a que o plano do PostgreSQL
devolver, e a mesma requisição pode responder em ordens diferentes sem nada ter
mudado no cadastro.

**A forma da verificação faz parte do requisito.** Um cenário que afirme apenas
"duas consultas devolvem a mesma ordem" é asserção sobre não-determinação e
**passa com o defeito presente** sempre que o plano do PostgreSQL calhar de ser
estável. A asserção que vale é sobre a **ordem crescente de identificador**, e o
arranjo SHALL criar os registros empatados em ordem de inserção **oposta** à
ordem crescente dos seus identificadores; sem isso a ordem "natural" do banco
coincide com a esperada e o cenário fica verde com e sem o desempate.

#### Scenario: Servidores MCP vinculados de nome igual vêm em ordem crescente de identificador
- **WHEN** um agente tem servidores MCP vinculados com o **mesmo** `name`,
  criados e vinculados em ordem de inserção **oposta** à ordem crescente dos
  seus `id`
- **THEN** a API retorna esses servidores em ordem **crescente de `id`**, tanto
  em `GET /agents/{id}` quanto em `GET /agents` — nunca na ordem de inserção nem
  em ordem indefinida pelo banco

#### Scenario: Agentes de delegação de nome igual vêm em ordem crescente de identificador
- **WHEN** um agente delega para agentes com o **mesmo** `name`, criados e
  vinculados em ordem de inserção **oposta** à ordem crescente dos seus `id`
- **THEN** a API retorna `delegatesTo` em ordem **crescente de `id`**, tanto em
  `GET /agents/{id}` quanto em `GET /agents`

#### Scenario: Catálogo de agentes desempata registros de mesmo instante de criação
- **WHEN** existem agentes cujo `CreatedAt` é **idêntico**, com a ordem
  crescente de `id` **oposta** à ordem em que foram cadastrados
- **THEN** `GET /agents` retorna esses agentes em ordem crescente de `id` entre
  si, mantida a ordem por `CreatedAt` em relação aos demais

#### Scenario: Catálogo de servidores MCP desempata registros de mesmo instante de criação
- **WHEN** existem servidores MCP cujo `CreatedAt` é **idêntico**, com a ordem
  crescente de `id` **oposta** à ordem em que foram cadastrados
- **THEN** `GET /mcp-servers` retorna esses servidores em ordem crescente de
  `id` entre si

#### Scenario: Catálogo de bases de conhecimento desempata registros de mesmo instante de criação
- **WHEN** existem bases de conhecimento cujo `CreatedAt` é **idêntico**, com a
  ordem crescente de `id` **oposta** à ordem em que foram cadastradas
- **THEN** `GET /knowledge-bases` retorna essas bases em ordem crescente de `id`
  entre si

#### Scenario: Listagem de documentos desempata registros de mesmo instante de criação
- **WHEN** uma base de conhecimento tem documentos cujo `CreatedAt` é
  **idêntico**, com a ordem crescente de `id` **oposta** à ordem em que foram
  criados
- **THEN** `GET /knowledge-bases/{id}/documents` retorna esses documentos em
  ordem crescente de `id` entre si

#### Scenario: Lista sem nenhum empate mantém a ordem do critério primário
- **WHEN** uma lista não tem dois itens empatados no critério primário
- **THEN** a resposta vem na ordem do critério primário, e o desempate não a
  altera

#### Scenario: Lista vazia continua sendo resposta normal
- **WHEN** uma das listas cobertas por este requisito não tem nenhum item
- **THEN** a API responde normalmente com uma lista vazia, sem erro

### Requirement: Critério de ordenação não muda por causa do desempate
O desempate SHALL ser acrescentado sem alterar o critério primário de nenhuma
lista. Os catálogos de `apps/api` SHALL continuar ordenando por instante de
cadastro e as consultas de vínculo SHALL continuar ordenando por nome.

Requisito explícito porque a alternativa é tentadora e errada: normalizar os
critérios "por simetria" seria mudança de comportamento observável sem cenário
que a peça, e contraria o padrão que os quatro catálogos já seguem — catálogo em
ordem de cadastro, vínculo em ordem de leitura.

#### Scenario: Catálogos continuam em ordem de cadastro
- **WHEN** um cliente lista agentes, servidores MCP, bases de conhecimento ou
  documentos de uma base, com registros de instantes de cadastro distintos
- **THEN** a resposta vem em ordem crescente de instante de cadastro, não em
  ordem de nome

#### Scenario: Vínculos continuam em ordem de nome
- **WHEN** um cliente consulta um agente cujos servidores MCP vinculados,
  agentes de delegação ou bases de conhecimento têm nomes distintos, cadastrados
  fora de ordem alfabética
- **THEN** a resposta vem em ordem de `name`, não em ordem de cadastro

### Requirement: Uma lista tem uma ordem só, independente da superfície que a serve
Duas rotas que servem a mesma lista lógica SHALL devolvê-la na **mesma** ordem,
no critério primário e no desempate — o conjunto de vínculos de um agente
aparece tanto em `GET /agents/{id}` quanto em `GET /agents`.

O comparador canônico SHALL ser o do banco de dados: a ordenação dessas listas
SHALL ser produzida pela consulta, nunca por comparação em memória no processo
de `apps/api`. Motivo medido, não presumido: a collation do PostgreSQL e o
comparador de `string` do .NET **discordam de fato** para nomes que diferem em
caixa e pontuação, e o comparador do .NET depende ainda da cultura do processo,
que este repositório não fixa em lugar nenhum. Duas superfícies com dois
comparadores devolvem ordens diferentes para o mesmo conjunto sem nenhum empate
envolvido, o que nenhum desempate por identificador alcança — a divergência está
no critério primário.

#### Scenario: Nomes que as duas collations ordenam diferente saem na mesma ordem nas duas rotas
- **WHEN** um agente tem dois servidores MCP vinculados cujos nomes diferem
  entre si apenas em caixa e pontuação, de forma que a collation do banco e o
  comparador de `string` do .NET os ordenem em ordens **opostas**
- **THEN** `GET /agents/{id}` e `GET /agents` devolvem `mcpServers` na mesma
  ordem, e essa ordem é a da collation do banco

#### Scenario: A mesma garantia vale para os agentes de delegação
- **WHEN** um agente delega para dois agentes cujos nomes diferem entre si
  apenas em caixa e pontuação, ordenados em ordens opostas pelos dois
  comparadores
- **THEN** `GET /agents/{id}` e `GET /agents` devolvem `delegatesTo` na mesma
  ordem, e essa ordem é a da collation do banco

#### Scenario: A ordem da resposta não depende da cultura do processo
- **WHEN** a mesma lista de vínculos é servida por um processo de `apps/api` sob
  culturas de sistema diferentes
- **THEN** a ordem da resposta é a mesma, porque nenhuma comparação de nome
  acontece no processo
