## ADDED Requirements

### Requirement: Cadastro de base de conhecimento
O sistema SHALL permitir, via `apps/api`, cadastrar uma base de conhecimento
informando nome e descrição. A base SHALL nascer ativa (`isActive: true`) e
receber um identificador único gerado pelo sistema.

A descrição NÃO é texto decorativo: a partir da etapa de execução ela é o texto
que o modelo lê para decidir se a base é relevante para a pergunta. Por isso ela
SHALL ser obrigatória e não vazia.

#### Scenario: Criar base com nome e descrição
- **WHEN** um cliente envia `POST /knowledge-bases` com nome e descrição não
  vazios
- **THEN** a API responde HTTP 201 com a base criada, incluindo id, nome,
  descrição e `isActive: true`

#### Scenario: Criar base sem nome é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases` sem nome, ou com nome
  vazio ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

#### Scenario: Criar base sem descrição é rejeitado
- **WHEN** um cliente envia `POST /knowledge-bases` sem descrição, ou com
  descrição vazia ou só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e não cria
  nenhum registro

#### Scenario: Nome duplicado é permitido
- **WHEN** um cliente envia `POST /knowledge-bases` com um nome já usado por
  outra base
- **THEN** a API cria a segunda base normalmente, com identificador próprio

### Requirement: Listagem de bases de conhecimento
O sistema SHALL permitir, via `apps/api`, listar todas as bases cadastradas,
incluindo as inativas.

#### Scenario: Lista retorna todas as bases cadastradas
- **WHEN** um cliente envia `GET /knowledge-bases` e existem bases cadastradas
- **THEN** a API responde HTTP 200 com a lista, incluindo id, nome, descrição e
  `isActive` de cada uma

#### Scenario: Lista inclui bases inativas
- **WHEN** um cliente envia `GET /knowledge-bases` e existe pelo menos uma base
  desativada
- **THEN** a API inclui essa base na resposta normalmente, com
  `isActive: false`

#### Scenario: Lista sem nenhuma base cadastrada
- **WHEN** um cliente envia `GET /knowledge-bases` e não existe nenhuma base
- **THEN** a API responde HTTP 200 com uma lista vazia, nunca HTTP 404

### Requirement: Consulta de base de conhecimento por id
O sistema SHALL permitir, via `apps/api`, consultar uma base específica pelo seu
identificador, incluindo bases inativas.

#### Scenario: Consulta de base existente retorna dados completos
- **WHEN** um cliente envia `GET /knowledge-bases/{id}` para um id existente
- **THEN** a API responde HTTP 200 com id, nome, descrição e `isActive`

#### Scenario: Consulta de base inexistente retorna 404
- **WHEN** um cliente envia `GET /knowledge-bases/{id}` para um id que não
  existe
- **THEN** a API responde HTTP 404

#### Scenario: Consulta de base inativa retorna normalmente
- **WHEN** um cliente envia `GET /knowledge-bases/{id}` para uma base desativada
- **THEN** a API responde HTTP 200 com `isActive: false`, sem tratar isso como
  não encontrado

### Requirement: Edição de base de conhecimento
O sistema SHALL permitir, via `apps/api`, alterar nome e descrição de uma base
existente, com as mesmas validações do cadastro.

#### Scenario: Editar nome e descrição de base existente
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` com nome e descrição não
  vazios, para um id existente
- **THEN** a API responde HTTP 200 com a base atualizada refletindo os novos
  valores

#### Scenario: Editar base inexistente retorna 404
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` para um id que não existe
- **THEN** a API responde HTTP 404 e nenhum registro é alterado

#### Scenario: Editar base com descrição vazia é rejeitado
- **WHEN** um cliente envia `PUT /knowledge-bases/{id}` com descrição vazia ou
  só de espaços
- **THEN** a API responde HTTP 400 com `ValidationProblemDetails` e a base
  permanece com os valores anteriores

### Requirement: Ativação e desativação de base de conhecimento
O sistema SHALL permitir ativar e desativar uma base, sem excluí-la. Base de
conhecimento NÃO SHALL ter rota de exclusão: ela é entidade de catálogo com
vínculos de agente apontando para ela a partir da etapa de vínculo, e segue o
mesmo padrão de `McpServer`.

#### Scenario: Desativar base ativa
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/deactivate` para uma
  base ativa
- **THEN** a API responde HTTP 200 com `isActive: false` e a base continua
  existindo, com seus documentos intactos

#### Scenario: Ativar base inativa
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/activate` para uma base
  desativada
- **THEN** a API responde HTTP 200 com `isActive: true`

#### Scenario: Desativar base já inativa é idempotente
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/deactivate` para uma
  base que já está inativa
- **THEN** a API responde HTTP 200 com `isActive: false`, sem erro

#### Scenario: Ativar ou desativar base inexistente retorna 404
- **WHEN** um cliente envia `POST /knowledge-bases/{id}/activate` ou
  `POST /knowledge-bases/{id}/deactivate` para um id que não existe
- **THEN** a API responde HTTP 404

### Requirement: Autenticação das rotas de base de conhecimento
Todas as rotas de base de conhecimento SHALL exigir token de operador válido.
Nenhuma delas SHALL ser adicionada à allowlist de rotas anônimas.

#### Scenario: Requisição sem token é rejeitada
- **WHEN** um cliente envia qualquer requisição para `/knowledge-bases` sem
  cabeçalho de autorização
- **THEN** a API responde HTTP 401 e nenhuma operação é executada
