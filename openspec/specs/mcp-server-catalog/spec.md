# mcp-server-catalog Specification

## Purpose

TBD - defined by change backend-mcp-catalogo-vinculo. Update Purpose after archive.

## Requirements

### Requirement: Cadastro de servidor MCP
O sistema SHALL permitir, via `apps/api`, cadastrar um servidor MCP remoto
(HTTP) informando nome, descrição, URL, tipo de autenticação (`None` ou
`BearerToken`) e, quando o tipo de autenticação não for `None`, uma
credencial. A credencial SHALL ser criptografada antes de ser persistida e
nunca SHALL ser incluída em nenhuma resposta da API.

#### Scenario: Criar servidor MCP sem autenticação
- **WHEN** um cliente envia `POST /mcp-servers` com nome, descrição, URL e
  `authType: "None"`, sem credencial
- **THEN** a API cria o registro e responde com o servidor criado, incluindo
  um identificador único gerado pelo sistema, sem nenhum campo de credencial
  na resposta

#### Scenario: Criar servidor MCP com autenticação por token
- **WHEN** um cliente envia `POST /mcp-servers` com nome, descrição, URL,
  `authType: "BearerToken"` e uma credencial não vazia
- **THEN** a API cria o registro, persiste a credencial de forma
  criptografada, e responde com o servidor criado sem incluir a credencial
  em nenhum campo da resposta

#### Scenario: Criar servidor MCP sem nome ou sem URL é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` sem nome ou sem URL
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar servidor MCP com BearerToken sem credencial é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "BearerToken"` e sem credencial (ou credencial vazia)
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

### Requirement: Listagem de servidores MCP
O sistema SHALL permitir, via `apps/api`, listar todos os servidores MCP
cadastrados, incluindo servidores inativos, sem incluir a credencial de
nenhum deles na resposta.

#### Scenario: Lista retorna todos os servidores cadastrados
- **WHEN** um cliente envia `GET /mcp-servers` e existem servidores MCP
  cadastrados
- **THEN** a API responde com a lista de todos os servidores, incluindo id,
  nome, descrição, URL, `authType` e `isActive` de cada um, sem nenhum campo
  de credencial

#### Scenario: Lista inclui servidores inativos
- **WHEN** um cliente envia `GET /mcp-servers` e existe pelo menos um
  servidor desativado (`isActive = false`)
- **THEN** a API inclui esse servidor na resposta normalmente, com
  `isActive` refletindo `false`

### Requirement: Consulta de servidor MCP por id
O sistema SHALL permitir, via `apps/api`, consultar um servidor MCP
específico pelo seu identificador, incluindo servidores inativos, sem
incluir a credencial na resposta.

#### Scenario: Consulta de servidor existente retorna dados completos
- **WHEN** um cliente envia `GET /mcp-servers/{id}` para um id existente
- **THEN** a API responde com HTTP 200 e os dados completos do servidor
  (nome, descrição, URL, `authType`, `isActive`), sem nenhum campo de
  credencial

#### Scenario: Consulta de servidor inexistente retorna 404
- **WHEN** um cliente envia `GET /mcp-servers/{id}` para um id que não
  existe
- **THEN** a API responde com HTTP 404

#### Scenario: Consulta de servidor inativo retorna normalmente
- **WHEN** um cliente envia `GET /mcp-servers/{id}` para um id de servidor
  desativado
- **THEN** a API responde com HTTP 200 e `isActive: false`, sem tratar isso
  como não encontrado

### Requirement: Atualização de servidor MCP
O sistema SHALL permitir, via `apps/api`, atualizar nome, descrição, URL,
tipo de autenticação e credencial de um servidor MCP já cadastrado, com a
mesma validação usada no cadastro. Quando a atualização não incluir uma
nova credencial e o tipo de autenticação continuar exigindo credencial, a
credencial anteriormente persistida SHALL ser mantida. Quando a atualização
transicionar o tipo de autenticação para `None`, a credencial anteriormente
persistida SHALL ser removida, não permanecendo armazenada de forma
cifrada sem uso.

#### Scenario: Atualizar servidor MCP com sucesso
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com nome, descrição,
  URL e `authType` válidos para um id existente
- **THEN** a API atualiza o registro e responde com HTTP 200 e o servidor
  atualizado, sem incluir credencial na resposta

#### Scenario: Atualizar servidor MCP trocando a credencial
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com uma nova credencial
  para um servidor com `authType: "BearerToken"`
- **THEN** a API substitui a credencial criptografada persistida pela nova,
  e uma chamada subsequente ao teste de conexão desse servidor usa a nova
  credencial

#### Scenario: Atualizar servidor MCP sem nome ou sem URL é rejeitado
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` sem nome ou sem URL
- **THEN** a API responde com erro de validação (HTTP 400) e não altera o
  registro existente

#### Scenario: Atualizar servidor MCP inexistente retorna 404
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` para um id que não
  existe
- **THEN** a API responde com HTTP 404 e não cria nenhum registro

#### Scenario: Atualizar servidor MCP transicionando para sem autenticação limpa a credencial
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com `authType: "None"`
  para um servidor que tinha `authType: "BearerToken"` e uma credencial
  persistida
- **THEN** a API atualiza o registro e a credencial anteriormente
  persistida deixa de existir, não permanecendo armazenada de forma
  cifrada sem uso

### Requirement: Ativação e desativação de servidor MCP
O sistema SHALL permitir, via `apps/api`, ativar e desativar um servidor MCP
cadastrado por meio de um campo de estado (`isActive`), sem excluir o
registro (nem exclusão definitiva, nem soft delete). As operações SHALL ser
idempotentes.

#### Scenario: Desativar servidor MCP ativo
- **WHEN** um cliente envia `POST /mcp-servers/{id}/deactivate` para um
  servidor atualmente ativo
- **THEN** a API responde com HTTP 200 e o servidor atualizado com
  `isActive: false`

#### Scenario: Desativar servidor MCP já inativo é idempotente
- **WHEN** um cliente envia `POST /mcp-servers/{id}/deactivate` para um
  servidor que já está `isActive: false`
- **THEN** a API responde com HTTP 200 e o servidor com `isActive: false`,
  sem erro

#### Scenario: Ativar servidor MCP inativo
- **WHEN** um cliente envia `POST /mcp-servers/{id}/activate` para um
  servidor atualmente inativo
- **THEN** a API responde com HTTP 200 e o servidor atualizado com
  `isActive: true`

#### Scenario: Ativar servidor MCP já ativo é idempotente
- **WHEN** um cliente envia `POST /mcp-servers/{id}/activate` para um
  servidor que já está `isActive: true`
- **THEN** a API responde com HTTP 200 e o servidor com `isActive: true`,
  sem erro

#### Scenario: Ativar ou desativar servidor MCP inexistente retorna 404
- **WHEN** um cliente envia `POST /mcp-servers/{id}/activate` ou
  `POST /mcp-servers/{id}/deactivate` para um id que não existe
- **THEN** a API responde com HTTP 404

### Requirement: Servidor MCP inativo permanece configurável
O sistema SHALL permitir consultar, atualizar e vincular a agentes um
servidor MCP desativado (`isActive: false`) sem nenhuma restrição adicional
em relação a um servidor ativo — o estado `isActive` nesta change afeta
apenas a exibição do campo, sem bloquear nenhuma operação de configuração.

#### Scenario: Servidor MCP inativo pode ser atualizado normalmente
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` para um servidor com
  `isActive: false`
- **THEN** a API atualiza o registro normalmente, sem exigir reativação
  prévia

### Requirement: Teste de configuração de servidor MCP não salva
O sistema SHALL permitir, via `apps/api`, testar uma configuração de
servidor MCP ainda não persistida, executando um handshake real do
protocolo MCP (mensagem `initialize`) contra a URL informada, sem exigir
que um `McpServer` correspondente já exista.

#### Scenario: Teste de configuração não salva com handshake bem-sucedido
- **WHEN** um cliente envia `POST /mcp-servers/test` com uma URL, tipo de
  autenticação e credencial (quando aplicável) que apontam para um servidor
  MCP acessível e que aceita a credencial informada
- **THEN** a API responde com HTTP 200 e um resultado indicando sucesso,
  sem persistir nenhum registro novo em `McpServer`

#### Scenario: Teste de configuração não salva com host inalcançável
- **WHEN** um cliente envia `POST /mcp-servers/test` com uma URL que não
  responde (host inalcançável, conexão recusada ou timeout)
- **THEN** a API responde com HTTP 200 e um resultado indicando falha, com
  um motivo que identifica problema de conectividade, sem persistir nenhum
  registro novo em `McpServer`

#### Scenario: Teste de configuração não salva com credencial rejeitada
- **WHEN** um cliente envia `POST /mcp-servers/test` com uma URL alcançável
  cujo servidor MCP rejeita a credencial informada (HTTP 401 ou 403 durante
  o handshake)
- **THEN** a API responde com HTTP 200 e um resultado indicando falha, com
  um motivo que identifica rejeição de credencial, sem persistir nenhum
  registro novo em `McpServer`

### Requirement: Teste de conexão de servidor MCP salvo
O sistema SHALL permitir, via `apps/api`, re-testar a conexão de um
`McpServer` já cadastrado usando a URL, tipo de autenticação e credencial
atualmente persistidos, executando o mesmo handshake real do protocolo MCP.
Quando a credencial persistida não puder ser decifrada com a chave de
criptografia atualmente configurada, o sistema SHALL reportar isso como um
resultado de falha do teste, com um motivo distinto de credencial rejeitada
pelo próprio servidor MCP, sem tentar nenhuma conexão de rede.

#### Scenario: Re-teste de servidor MCP salvo com sucesso
- **WHEN** um cliente envia `POST /mcp-servers/{id}/test` para um
  `McpServer` cuja configuração persistida aponta para um servidor MCP
  acessível que aceita a credencial persistida
- **THEN** a API responde com HTTP 200 e um resultado indicando sucesso

#### Scenario: Re-teste de servidor MCP salvo inexistente retorna 404
- **WHEN** um cliente envia `POST /mcp-servers/{id}/test` para um id que
  não existe
- **THEN** a API responde com HTTP 404

#### Scenario: Re-teste de servidor MCP inativo é permitido
- **WHEN** um cliente envia `POST /mcp-servers/{id}/test` para um
  `McpServer` com `isActive: false`
- **THEN** a API executa o teste normalmente e responde com o resultado do
  handshake, sem bloquear a operação por causa do estado inativo

#### Scenario: Falha ao decifrar a credencial persistida é reportada como falha do teste
- **WHEN** um cliente envia `POST /mcp-servers/{id}/test` para um
  `McpServer` cuja credencial persistida não pode ser decifrada com a chave
  de criptografia atualmente configurada (ex.: a chave foi rotacionada
  desde que a credencial foi salva)
- **THEN** a API responde com HTTP 200 e um resultado indicando falha, com
  um motivo que identifica falha de decifragem local, distinto de
  credencial rejeitada pelo servidor MCP, sem tentar nenhuma conexão de
  rede e sem retornar erro 500

### Requirement: Resultado do teste de conexão não é persistido
O sistema SHALL tratar o resultado de qualquer teste de conexão (salvo ou
não salvo) como efêmero — nenhum dado sobre testes anteriores SHALL ser
armazenado ou refletido em respostas subsequentes de `GET /mcp-servers` ou
`GET /mcp-servers/{id}`.

#### Scenario: Resultado de teste não aparece em consultas subsequentes
- **WHEN** um cliente executa `POST /mcp-servers/{id}/test` e, em seguida,
  `GET /mcp-servers/{id}`
- **THEN** a resposta de `GET /mcp-servers/{id}` não contém nenhum campo
  relacionado ao resultado do teste executado anteriormente

### Requirement: Persistência durável do catálogo de servidores MCP
O sistema SHALL persistir o catálogo de servidores MCP em PostgreSQL, sem
uso de armazenamento em memória, garantindo que os dados sobrevivam a
reinícios da aplicação.

#### Scenario: Servidor MCP cadastrado sobrevive a restart da API
- **WHEN** um servidor MCP é cadastrado e o processo de `apps/api` é
  reiniciado
- **THEN** uma consulta subsequente a `GET /mcp-servers/{id}` continua
  retornando o servidor com os mesmos dados
