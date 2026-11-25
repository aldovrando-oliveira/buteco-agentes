## MODIFIED Requirements

### Requirement: Atualização de agente
O sistema SHALL permitir, via `apps/api`, atualizar nome, instruções,
`provider`, `model`, `description` e `skills` de um agente já cadastrado,
com a mesma validação usada no cadastro (incluindo a validação de
`provider`/`model` contra a lista curada de modelos habilitados, que é a
mesma fonte de `GET /providers`, e de `name` não vazio em
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
  configurado no ambiente, ou com `model` que não está habilitado na
  lista curada do `provider` informado
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

#### Scenario: Atualizar agente mantendo um modelo que foi retirado é rejeitado
- **WHEN** um agente foi cadastrado com um par `provider`+`model` habilitado,
  o Admin retirou esse modelo da lista curada, e um cliente envia
  `PUT /agents/{id}` mudando só o nome e mantendo o mesmo `provider`/`model`
- **THEN** a API responde com erro de validação (HTTP 400) no campo `model` e
  não altera o registro existente

#### Scenario: Atualizar agente com modelo retirado para um modelo habilitado volta a aceitar tarefas
- **WHEN** um agente tem um `model` que foi retirado da lista curada e um
  cliente envia `PUT /agents/{id}` com um `model` habilitado para o mesmo
  `provider`
- **THEN** a API atualiza o registro, responde com HTTP 200, e o `SendMessage`
  seguinte para o agente é aceito e publica o job de execução
