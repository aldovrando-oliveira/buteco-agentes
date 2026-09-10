## MODIFIED Requirements

### Requirement: Formato de fio do conjunto de bases vinculadas
O sistema SHALL expor o conjunto de bases vinculadas sob a chave
`knowledgeBases`, com cada item contendo `id` e `name`, ordenados por nome e,
para nomes iguais, **com desempate estável por identificador**. A chave e os
nomes dos campos SHALL ser verificados sobre o texto do JSON da resposta, não
por desserialização para o mesmo tipo.

A ordenação SHALL ser produzida pela consulta ao banco, nunca por comparação em
memória no processo de `apps/api`, **tanto na consulta por id quanto na
listagem** — o comparador canônico de "ordem de nome" é o da collation do banco
de dados. Sem isso a garantia de "a mesma ordem nas duas superfícies", que este
requisito sempre pretendeu, não se sustenta: a collation do PostgreSQL e o
comparador de `string` do .NET discordam para nomes que diferem em caixa e
pontuação, e o comparador do .NET depende ainda da cultura do processo, que este
repositório não fixa. Com dois comparadores, as duas rotas podem devolver as
mesmas bases em ordens diferentes **sem nenhum empate de nome envolvido** — o
que o desempate por identificador não alcança, porque a divergência está no
critério primário. Ver `api-response-ordering`, que é dona dessa garantia para
todas as listas de `apps/api`.

O desempate é requisito, não detalhe de implementação: nome de base de
conhecimento **não é único** (`knowledge-base-catalog`, cenário "Nome duplicado
é permitido"), então ordenar só por nome deixa a ordem entre homônimas a cargo
do plano de consulta do PostgreSQL, e a mesma requisição pode devolver ordens
diferentes sem nada ter mudado no cadastro.

**A forma da verificação faz parte do requisito.** Um teste que afirma apenas
"duas consultas devolvem a mesma ordem" é asserção sobre não-determinação e
**passa com o defeito presente** sempre que o plano do PostgreSQL calhar de ser
estável — é o perfil de guarda que esta base já teve de consertar quatro vezes.
A asserção que vale é sobre a **ordem crescente de identificador**, e o arranjo
precisa criar as bases homônimas em ordem de inserção **oposta** à ordem de
`id`; sem isso, a ordem "natural" do banco coincide com a esperada e o teste
fica verde com e sem o desempate.

O guarda de "mesma ordem nas duas superfícies" da ordenação por nome é a única
exceção legítima à regra acima, e só porque não afirma sobre não-determinação:
ele compara **dois comparadores conhecidos e medidos**, com ordem esperada fixa
e verificada nos dois runtimes reais.

#### Scenario: Chave e campos aparecem no JSON da resposta
- **WHEN** um cliente consulta um agente com pelo menos uma base vinculada e o
  corpo bruto da resposta é inspecionado como texto
- **THEN** o JSON contém a chave `knowledgeBases`, e cada item contém as chaves
  `id` e `name`

#### Scenario: Bases vinculadas vêm ordenadas por nome
- **WHEN** um cliente consulta um agente com várias bases vinculadas cujos
  nomes, em ordem alfabética, diferem da ordem em que foram vinculadas
- **THEN** a API retorna `knowledgeBases` em ordem alfabética de `name`

#### Scenario: Bases de nome igual vêm em ordem crescente de identificador
- **WHEN** um agente tem três bases vinculadas com o **mesmo** `name`, criadas e
  vinculadas em ordem de inserção **oposta** à ordem crescente dos seus `id`
- **THEN** a API retorna essas bases em ordem **crescente de `id`**, tanto na
  consulta por id quanto na listagem — nunca na ordem de inserção nem em ordem
  indefinida pelo banco

#### Scenario: Nomes que as duas collations ordenam diferente saem na mesma ordem nas duas rotas
- **WHEN** um agente tem duas bases vinculadas cujos nomes diferem entre si
  apenas em caixa e pontuação, de forma que a collation do banco e o comparador
  de `string` do .NET as ordenem em ordens **opostas**
- **THEN** `GET /agents/{id}` e `GET /agents` devolvem `knowledgeBases` na mesma
  ordem, e essa ordem é a da collation do banco
