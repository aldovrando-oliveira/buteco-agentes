# agent-catalog Specification

## Purpose

TBD - defined by change backend-agente-a2a-mvp. Update Purpose after archive.

## Requirements

### Requirement: Cadastro de agente
O sistema SHALL permitir, via `apps/api`, cadastrar um agente informando
nome, instruções (system prompt), `provider`, `model`, `description`
(opcional) e `skills` (opcional), persistindo o registro em PostgreSQL
via EF Core. `provider` e `model` SHALL ser validados contra o catálogo
de provedores/modelos atualmente disponível (a mesma fonte usada por
`GET /providers`). Cada item de `skills`, se informado, SHALL ter `name`
não vazio.

#### Scenario: Criar agente com sucesso
- **WHEN** um cliente envia `POST /agents` com nome, instruções, `provider`
  e `model` válidos, sendo a combinação `provider`+`model` atualmente
  disponível
- **THEN** a API cria o registro no banco e responde com o agente criado,
  incluindo um identificador único gerado pelo sistema e os valores de
  `provider`/`model` persistidos

#### Scenario: Criar agente sem nome ou sem instruções é rejeitado
- **WHEN** um cliente envia `POST /agents` sem nome ou sem instruções
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar agente sem provider ou sem model é rejeitado
- **WHEN** um cliente envia `POST /agents` sem `provider` ou sem `model`
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar agente com provider ou model indisponíveis é rejeitado
- **WHEN** um cliente envia `POST /agents` com `provider` não configurado
  no ambiente, ou com `model` que não consta no catálogo do `provider`
  informado
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar agente com description e skills
- **WHEN** um cliente envia `POST /agents` com `description` e uma lista
  de `skills` (cada uma com `name` e, opcionalmente, `description`),
  além dos demais campos válidos
- **THEN** a API cria o registro e responde com o agente criado,
  incluindo `description` e `skills` exatamente como enviados

#### Scenario: Criar agente sem description e sem skills usa os defaults
- **WHEN** um cliente envia `POST /agents` sem `description` e sem
  `skills`, com os demais campos válidos
- **THEN** a API cria o registro e responde com `description` nulo e
  `skills` como uma lista vazia

#### Scenario: Criar agente com skill sem nome é rejeitado
- **WHEN** um cliente envia `POST /agents` com pelo menos um item em
  `skills` cujo `name` é vazio ou ausente
- **THEN** a API responde com erro de validação (HTTP 400) identificando
  o item inválido e não cria nenhum registro

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

### Requirement: Atualização de agente
O sistema SHALL permitir, via `apps/api`, atualizar nome, instruções,
`provider`, `model`, `description` e `skills` de um agente já cadastrado,
com a mesma validação usada no cadastro (incluindo a validação de
`provider`/`model` contra o catálogo disponível e de `name` não vazio em
cada item de `skills`). Atualizar um agente que estava sem
`provider`/`model` definidos com valores válidos e disponíveis SHALL
retirá-lo do estado implícito de "precisa de reconfiguração". A
atualização de `skills` SHALL substituir o conjunto inteiro anteriormente
persistido (replace completo, sem merge).

#### Scenario: Atualizar agente com sucesso
- **WHEN** um cliente envia `PUT /agents/{id}` com nome, instruções,
  `provider` e `model` válidos para um id existente, sendo a combinação
  `provider`+`model` atualmente disponível
- **THEN** a API atualiza o registro no banco e responde com HTTP 200 e o
  agente atualizado, incluindo `isActive` inalterado

#### Scenario: Atualizar agente sem nome ou sem instruções é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` sem nome ou sem instruções
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar agente sem provider ou sem model é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` sem `provider` ou sem
  `model`
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar agente com provider ou model indisponíveis é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` com `provider` não
  configurado no ambiente, ou com `model` que não consta no catálogo do
  `provider` informado
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar agente inexistente retorna 404
- **WHEN** um cliente envia `PUT /agents/{id}` para um id que não existe
- **THEN** a API responde com HTTP 404 e não cria nenhum registro

#### Scenario: Atualizar agente legado com provider/model válidos sai do estado de reconfiguração
- **WHEN** um cliente envia `PUT /agents/{id}` com `provider`/`model`
  válidos e disponíveis para um agente que estava com `provider`/`model`
  nulos
- **THEN** a API atualiza o registro com os novos `provider`/`model` e o
  agente deixa de estar no estado implícito de "precisa de reconfiguração"

#### Scenario: Atualizar agente substituindo o conjunto inteiro de skills
- **WHEN** um cliente envia `PUT /agents/{id}` para um agente que já
  tinha `skills` cadastradas, com uma nova lista de `skills` diferente da
  anterior
- **THEN** a API responde com HTTP 200 e `skills` refletindo exatamente a
  nova lista enviada, sem nenhum item da lista anterior remanescente

#### Scenario: Atualizar agente com skill sem nome é rejeitado
- **WHEN** um cliente envia `PUT /agents/{id}` com pelo menos um item em
  `skills` cujo `name` é vazio ou ausente
- **THEN** a API responde com erro de validação (HTTP 400) identificando
  o item inválido e não altera o registro existente

### Requirement: Ativação e desativação de agente
O sistema SHALL permitir, via `apps/api`, ativar e desativar um agente
cadastrado por meio de um campo de estado (`isActive`), sem excluir o
registro do agente (nem exclusão definitiva, nem soft delete). As
operações SHALL ser idempotentes.

#### Scenario: Desativar agente ativo
- **WHEN** um cliente envia `POST /agents/{id}/deactivate` para um agente
  atualmente ativo
- **THEN** a API responde com HTTP 200 e o agente atualizado com
  `isActive: false`

#### Scenario: Desativar agente já inativo é idempotente
- **WHEN** um cliente envia `POST /agents/{id}/deactivate` para um agente
  que já está `isActive: false`
- **THEN** a API responde com HTTP 200 e o agente com `isActive: false`,
  sem erro

#### Scenario: Ativar agente inativo
- **WHEN** um cliente envia `POST /agents/{id}/activate` para um agente
  atualmente inativo
- **THEN** a API responde com HTTP 200 e o agente atualizado com
  `isActive: true`

#### Scenario: Ativar agente já ativo é idempotente
- **WHEN** um cliente envia `POST /agents/{id}/activate` para um agente
  que já está `isActive: true`
- **THEN** a API responde com HTTP 200 e o agente com `isActive: true`,
  sem erro

#### Scenario: Ativar ou desativar agente inexistente retorna 404
- **WHEN** um cliente envia `POST /agents/{id}/activate` ou
  `POST /agents/{id}/deactivate` para um id que não existe
- **THEN** a API responde com HTTP 404

### Requirement: Persistência durável do catálogo de agentes
O sistema SHALL persistir o catálogo de agentes em PostgreSQL, sem uso de
armazenamento em memória, garantindo que os dados sobrevivam a reinícios da
aplicação.

#### Scenario: Agente cadastrado sobrevive a restart da API
- **WHEN** um agente é cadastrado e o processo de `apps/api` é reiniciado
- **THEN** uma consulta subsequente a `GET /agents/{id}` continua
  retornando o agente com os mesmos dados

### Requirement: Endereços A2A do agente na resposta da API
A resposta de agente de `apps/api` SHALL incluir os dois endereços públicos A2A
daquele agente: o endpoint de execução e o endereço do card de descoberta.

Os endereços SHALL ser montados no servidor, a partir da URL pública
configurada, e SHALL ser idênticos aos que o card de descoberta do mesmo agente
anuncia. Nenhum consumidor SHALL precisar concatenar host com identificador para
obtê-los.

Quando a URL pública não estiver configurada, os endereços SHALL estar ausentes
da resposta, e a API SHALL NOT devolver endereço relativo, vazio ou construído
a partir do host da requisição.

Os endereços SHALL ser expostos independentemente do estado do agente, porque o
card de descoberta também é — a resposta descreve onde o agente é alcançável,
não se ele aceitará o que receber.

#### Scenario: Consulta por id inclui os endereços
- **WHEN** um cliente consulta um agente existente pelo seu identificador, com a
  URL pública configurada
- **THEN** a resposta inclui o endereço do endpoint de execução e o endereço do
  card de descoberta daquele agente

#### Scenario: Os endereços coincidem com os do card de descoberta
- **WHEN** o card de descoberta de um agente é consultado e comparado com a
  resposta de agente do mesmo identificador
- **THEN** o endereço de execução anunciado nos dois é o mesmo

#### Scenario: Agente inativo também expõe os endereços
- **WHEN** um agente desativado é consultado
- **THEN** a resposta inclui os dois endereços normalmente

#### Scenario: Sem URL pública configurada, os endereços ficam ausentes
- **WHEN** a URL pública do servidor não está configurada e um agente é
  consultado
- **THEN** a resposta não inclui os endereços A2A, em vez de incluir endereço
  incompleto ou construído a partir do host da requisição

#### Scenario: A listagem carrega os mesmos endereços
- **WHEN** um cliente lista os agentes
- **THEN** cada agente da lista carrega os mesmos endereços que a consulta por
  id devolveria para ele
