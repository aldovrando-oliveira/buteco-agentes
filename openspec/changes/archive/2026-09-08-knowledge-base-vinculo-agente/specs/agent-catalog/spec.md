## MODIFIED Requirements

### Requirement: Listagem de agentes
O sistema SHALL permitir, via `apps/api`, listar todos os agentes
cadastrados, incluindo agentes inativos e agentes sem `provider`/`model`
configurados. Cada agente na lista SHALL incluir o conjunto de servidores
MCP atualmente vinculados a ele, o conjunto de agentes para os quais ele
delega (`delegatesTo`), o conjunto de bases de conhecimento vinculadas a ele
(`knowledgeBases`), além de `description` e `skills`.

#### Scenario: Lista retorna todos os agentes cadastrados
- **WHEN** um cliente envia `GET /agents` e existem agentes cadastrados
- **THEN** a API responde com a lista de todos os agentes, incluindo id,
  nome, instruções, `provider`, `model`, `description`, `skills`, o campo
  `isActive`, o conjunto de servidores MCP vinculados (`mcpServers`, cada
  item com id e nome), o conjunto de agentes para os quais delega
  (`delegatesTo`, cada item com id e nome) e o conjunto de bases de
  conhecimento vinculadas (`knowledgeBases`, cada item com id e nome) de
  cada um

#### Scenario: Lista inclui agentes inativos
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  desativado (`isActive = false`)
- **THEN** a API inclui esse agente na resposta normalmente, com
  `isActive` refletindo `false` — sem filtro escondendo agentes inativos

#### Scenario: Lista inclui agentes sem provider/model configurados
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  cadastrado antes desta capacidade existir, sem `provider`/`model`
  definidos
- **THEN** a API inclui esse agente na resposta normalmente, com
  `provider` e `model` retornados como nulos, sem filtro escondendo esse
  agente

#### Scenario: Lista inclui agente sem nenhum servidor MCP vinculado
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente sem
  nenhum servidor MCP vinculado
- **THEN** a API inclui esse agente na resposta com `mcpServers` como uma
  lista vazia, não nula

#### Scenario: Lista inclui agente legado sem description e sem skills
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  cadastrado antes desta capacidade existir, sem `description`/`skills`
  definidos
- **THEN** a API inclui esse agente na resposta normalmente, com
  `description` nulo e `skills` como uma lista vazia, sem filtro
  escondendo esse agente

#### Scenario: Lista inclui agente sem nenhuma delegação de saída
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente
  que não delega para nenhum outro agente
- **THEN** a API inclui esse agente na resposta com `delegatesTo` como uma
  lista vazia, não nula

#### Scenario: Lista inclui agente sem nenhuma base de conhecimento vinculada
- **WHEN** um cliente envia `GET /agents` e existe pelo menos um agente sem
  nenhuma base de conhecimento vinculada
- **THEN** a API inclui esse agente na resposta com `knowledgeBases` como uma
  lista vazia, não nula

#### Scenario: Lista reflete os vínculos corretos com vários agentes vinculados
- **WHEN** um cliente envia `GET /agents` e existem vários agentes, cada um
  com um conjunto diferente de bases de conhecimento vinculadas
- **THEN** cada agente da resposta traz em `knowledgeBases` exatamente as suas
  próprias bases, sem mistura entre agentes e sem omissão

### Requirement: Consulta de agente por id
O sistema SHALL permitir, via `apps/api`, consultar um agente específico
pelo seu identificador, incluindo agentes inativos e agentes sem
`provider`/`model` configurados. A resposta SHALL incluir o conjunto de
servidores MCP atualmente vinculados a esse agente, o conjunto de agentes
para os quais ele delega (`delegatesTo`), o conjunto de bases de conhecimento
vinculadas a ele (`knowledgeBases`), além de `description` e `skills`.

#### Scenario: Consulta de agente existente retorna dados completos
- **WHEN** um cliente envia `GET /agents/{id}` para um id existente
- **THEN** a API responde com HTTP 200 e os dados completos do agente,
  incluindo os campos `isActive`, `provider`, `model`, `description`,
  `skills`, `mcpServers` (cada item com id e nome dos servidores MCP
  vinculados), `delegatesTo` (cada item com id e nome dos agentes para os
  quais delega) e `knowledgeBases` (cada item com id e nome das bases de
  conhecimento vinculadas)

#### Scenario: Consulta de agente inexistente retorna 404
- **WHEN** um cliente envia `GET /agents/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404

#### Scenario: Consulta de agente inativo retorna normalmente
- **WHEN** um cliente envia `GET /agents/{id}` para um id de agente
  desativado
- **THEN** a API responde com HTTP 200 e `isActive: false`, sem tratar
  isso como não encontrado

#### Scenario: Consulta de agente sem provider/model retorna esses campos como nulos
- **WHEN** um cliente envia `GET /agents/{id}` para um agente cadastrado
  antes desta capacidade existir, sem `provider`/`model` definidos
- **THEN** a API responde com HTTP 200 e `provider`/`model` como nulos, sem
  tratar isso como não encontrado ou como erro

#### Scenario: Consulta de agente sem nenhum servidor MCP vinculado
- **WHEN** um cliente envia `GET /agents/{id}` para um agente sem nenhum
  servidor MCP vinculado
- **THEN** a API responde com HTTP 200 e `mcpServers` como uma lista vazia,
  não nula

#### Scenario: Consulta de agente legado sem description e sem skills
- **WHEN** um cliente envia `GET /agents/{id}` para um agente cadastrado
  antes desta capacidade existir, sem `description`/`skills` definidos
- **THEN** a API responde com HTTP 200, `description` nulo e `skills`
  como uma lista vazia, sem tratar isso como não encontrado ou como erro

#### Scenario: Consulta de agente sem nenhuma delegação de saída
- **WHEN** um cliente envia `GET /agents/{id}` para um agente que não
  delega para nenhum outro agente
- **THEN** a API responde com HTTP 200 e `delegatesTo` como uma lista
  vazia, não nula

#### Scenario: Consulta de agente sem nenhuma base de conhecimento vinculada
- **WHEN** um cliente envia `GET /agents/{id}` para um agente sem nenhuma
  base de conhecimento vinculada
- **THEN** a API responde com HTTP 200 e `knowledgeBases` como uma lista
  vazia, não nula

## ADDED Requirements

### Requirement: Conjunto de bases de conhecimento nas demais respostas de agente
O sistema SHALL incluir o conjunto de bases de conhecimento vinculadas
(`knowledgeBases`, cada item com id e nome) em **todas** as respostas que
representam um agente — criação, atualização, ativação e desativação —, e não
apenas na listagem, na consulta por id e na substituição do próprio vínculo.

#### Scenario: Agente recém-criado tem o conjunto vazio
- **WHEN** um cliente envia `POST /agents` e o agente é criado
- **THEN** a API responde com o agente e `knowledgeBases` como uma lista
  vazia, não nula

#### Scenario: Atualização de agente preserva as bases vinculadas
- **WHEN** um cliente envia `PUT /agents/{id}` alterando nome ou instruções de
  um agente que tem bases de conhecimento vinculadas
- **THEN** a API responde com HTTP 200 e `knowledgeBases` com as mesmas bases
  vinculadas de antes da atualização

#### Scenario: Ativação e desativação do agente preservam as bases vinculadas
- **WHEN** um cliente desativa e depois reativa um agente que tem bases de
  conhecimento vinculadas
- **THEN** as duas respostas incluem `knowledgeBases` com as mesmas bases
  vinculadas, sem alteração do conjunto
