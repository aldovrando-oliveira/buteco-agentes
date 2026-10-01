## MODIFIED Requirements

### Requirement: Consumo por agente cruza com o catálogo e leva ao diagnóstico

O sistema SHALL apresentar o consumo por agente como tabela, cruzando os
identificadores que a rota agregada devolve com os nomes do catálogo de agentes.

**A população das linhas SHALL ser a união dos agentes que a resposta mede por
consumo com os agentes que ela mede por falha.** Agente que falhou e **não** tem
consumo medido SHALL ter linha, com a célula de consumo apresentada como **valor
não medido** — nunca `0`, e nunca com valor derivado de outro nível de agregação.

**A razão de a população ser a união, e não só o consumo:** a contagem de falhas de
um agente é apresentada **na linha dele**, então agente sem linha é agente com as
falhas **ausentes da tela**. Ausência de linha é o sintoma mais difícil de notar —
ninguém repara numa linha que não existe —, e a população que ela esconde
preferencialmente é a da falha anterior à primeira chamada de provedor, que é
justamente a falha de configuração.

**A contagem de falhas apresentada para um agente SHALL ser a SOMA de todas as
linhas de falha que a resposta serve para aquele agente.** A resposta agrupa a
falha por agente **e** por atributos de execução, então um agente pode chegar em
mais de uma linha; apresentar uma delas SHALL NOT ocorrer, e apresentar a de menor
contagem — que é onde a ordenação da resposta a deixa — SHALL NOT ocorrer.

Um agente presente na agregação e ausente do catálogo SHALL ter a linha
preservada, com o identificador apresentado em lugar do nome — o consumo dele é
real, e o que falta é o rótulo, não o número.

O nome do agente SHALL levar à **superfície de diagnóstico daquele agente** — a
aba de Insights do detalhe —, e não a uma superfície a partir da qual o operador
ainda precise escolher. A tabela responde **qual** agente olhar; a superfície de
destino responde **o que aconteceu com ele**, e a passagem entre as duas é de um
acionamento só.

**O destaque da contagem de falhas SHALL ser calculado sobre a mesma população que
a tabela apresenta.** Destacar a maior contagem de um subconjunto das linhas
apresentadas SHALL NOT ocorrer.

As colunas que o protótipo desenha e a rota agregada do sistema **não serve**
SHALL NOT ser apresentadas com valor inventado, derivado de outro nível de
agregação, nem com zero.

**A declaração dessas colunas junto da tabela foi removida deste requisito.** Elas
deixaram de ser lacuna e passaram a ser **ausência decidida** — têm fonte, são
apresentadas na superfície de destino, e ficam fora desta por decisão registrada.
O requisito *"Métrica aprovada no protótipo e sem fonte não é inventada"* governa
a apresentação dos dois casos, e para coluna ele proíbe o elemento que este
requisito antes exigia.

#### Scenario: Cada agente aparece com nome e consumo
- **WHEN** a agregação e o catálogo respondem
- **THEN** cada agente com consumo no período aparece uma vez, com o seu nome e
  os seus números

#### Scenario: Agente que falhou sem consumo medido tem linha
- **WHEN** a resposta mede falhas para um agente que **não** aparece na agregação
  de consumo
- **THEN** o agente aparece na tabela, com a contagem de falhas dele apresentada
- **AND** a célula de consumo dele é apresentada como valor **não medido**, e não
  como `0`

#### Scenario: A contagem de falhas de um agente é a soma das linhas dele
- **WHEN** a resposta serve **mais de uma** linha de falha para o mesmo agente,
  com contagens diferentes
- **THEN** a tabela apresenta a **soma** das contagens daquele agente
- **AND** NÃO apresenta a contagem de nenhuma linha isolada

#### Scenario: O destaque considera a população inteira
- **WHEN** a linha de maior contagem de falhas é a de um agente que entrou na
  tabela por falha, sem consumo medido
- **THEN** é essa linha que recebe o destaque

#### Scenario: Agente fora do catálogo mantém a linha
- **WHEN** a agregação traz um agente que não está no catálogo
- **THEN** a linha é apresentada com o identificador em lugar do nome, e os
  números dele permanecem

#### Scenario: O nome leva direto ao diagnóstico do agente
- **WHEN** o operador aciona o nome de um agente na tabela
- **THEN** a aplicação navega para a superfície de diagnóstico daquele agente,
  **já aberta nela**, sem exigir um segundo acionamento para alcançá-la

#### Scenario: Colunas sem fonte não são preenchidas
- **WHEN** o operador visualiza a tabela de consumo por agente
- **THEN** **nenhuma** das colunas que esta rota não serve aparece preenchida com
  zero ou com valor de outro nível de agregação

#### Scenario: NEGATIVO — a ausência decidida não vira elemento na tabela
- **WHEN** o operador visualiza a tabela de consumo por agente
- **THEN** **nenhum** elemento do card nomeia as colunas ausentes nem explica a
  ausência delas
