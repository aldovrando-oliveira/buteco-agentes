# mcp-server-catalog Specification

## Purpose

TBD - defined by change backend-mcp-catalogo-vinculo. Update Purpose after archive.
## Requirements
### Requirement: Cadastro de servidor MCP
O sistema SHALL permitir, via `apps/api`, cadastrar um servidor MCP remoto
(HTTP) informando nome, descrição, URL, tipo de autenticação (`None`,
`BearerToken` ou `CustomHeader`) e, quando o tipo de autenticação não for
`None`, uma credencial. Quando o tipo for `CustomHeader`, o cadastro SHALL
exigir também o nome do header em que a credencial é enviada
(`authHeaderName`). A credencial SHALL ser criptografada antes de ser
persistida e nunca SHALL ser incluída em nenhuma resposta da API. O nome do
header é configuração, não segredo: SHALL ser persistido em claro e devolvido
nas respostas.

#### Scenario: Criar servidor MCP sem autenticação
- **WHEN** um cliente envia `POST /mcp-servers` com nome, descrição, URL e
  `authType: "None"`, sem credencial
- **THEN** a API cria o registro e responde com o servidor criado, incluindo
  um identificador único gerado pelo sistema, sem nenhum campo de credencial
  na resposta e com `authHeaderName: null`

#### Scenario: Criar servidor MCP com autenticação por token
- **WHEN** um cliente envia `POST /mcp-servers` com nome, descrição, URL,
  `authType: "BearerToken"` e uma credencial não vazia
- **THEN** a API cria o registro, persiste a credencial de forma
  criptografada, e responde com o servidor criado sem incluir a credencial
  em nenhum campo da resposta, com `authHeaderName: null`

#### Scenario: Criar servidor MCP com token em header customizado
- **WHEN** um cliente envia `POST /mcp-servers` com nome, descrição, URL,
  `authType: "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e uma
  credencial não vazia
- **THEN** a API cria o registro, persiste a credencial de forma
  criptografada e o nome do header em claro, e responde com HTTP 201,
  `authType: "CustomHeader"` e `authHeaderName: "X-API-TOKEN"`, sem incluir a
  credencial em nenhum campo da resposta

#### Scenario: authHeaderName informado com tipo que não é CustomHeader é ignorado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "BearerToken"`, uma credencial não vazia e `authHeaderName: "X-API-TOKEN"`
- **THEN** a API cria o registro com `authHeaderName` nulo, e responde com
  `authHeaderName: null`

#### Scenario: Criar servidor MCP sem nome ou sem URL é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` sem nome ou sem URL
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar servidor MCP com BearerToken sem credencial é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "BearerToken"` e sem credencial (ou credencial vazia)
- **THEN** a API responde com erro de validação (HTTP 400) e não cria
  nenhum registro

#### Scenario: Criar servidor MCP com CustomHeader sem credencial é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e sem credencial (ou
  credencial vazia)
- **THEN** a API responde com erro de validação (HTTP 400) no campo
  `credential` e não cria nenhum registro

#### Scenario: Criar servidor MCP com CustomHeader sem nome de header é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, uma credencial não vazia e sem `authHeaderName` (ou com
  ele vazio ou só com espaços)
- **THEN** a API responde com erro de validação (HTTP 400) no campo
  `authHeaderName` e não cria nenhum registro

### Requirement: Listagem de servidores MCP
O sistema SHALL permitir, via `apps/api`, listar todos os servidores MCP
cadastrados, incluindo servidores inativos, sem incluir a credencial de
nenhum deles na resposta.

#### Scenario: Lista retorna todos os servidores cadastrados
- **WHEN** um cliente envia `GET /mcp-servers` e existem servidores MCP
  cadastrados
- **THEN** a API responde com a lista de todos os servidores, incluindo id,
  nome, descrição, URL, `authType`, `authHeaderName` e `isActive` de cada um,
  sem nenhum campo de credencial

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
  (nome, descrição, URL, `authType`, `authHeaderName`, `isActive`), sem
  nenhum campo de credencial

#### Scenario: Consulta de servidor com header customizado devolve o nome do header
- **WHEN** um cliente envia `GET /mcp-servers/{id}` para um servidor
  cadastrado com `authType: "CustomHeader"` e `authHeaderName: "X-API-TOKEN"`
- **THEN** a API responde com `authHeaderName: "X-API-TOKEN"`, e o texto
  bruto da resposta não contém o valor da credencial

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
tipo de autenticação, nome do header e credencial de um servidor MCP já
cadastrado, com a mesma validação usada no cadastro. A credencial
persistida está vinculada ao tipo de autenticação para o qual foi informada:

- quando a atualização não incluir uma nova credencial e o tipo de
  autenticação **não mudar**, a credencial anteriormente persistida SHALL ser
  mantida;
- quando a atualização mudar o tipo de autenticação entre dois tipos que
  exigem credencial (`BearerToken` e `CustomHeader`, nos dois sentidos), a
  credencial anteriormente persistida SHALL NOT ser reaproveitada, e a
  atualização SHALL exigir uma credencial nova;
- quando a atualização transicionar o tipo de autenticação para `None`, a
  credencial anteriormente persistida SHALL ser removida, não permanecendo
  armazenada de forma cifrada sem uso.

O nome do header SHALL ser persistido só quando o tipo resultante for
`CustomHeader`. Em qualquer outro tipo resultante, ele SHALL ficar nulo. Como
é configuração devolvida na resposta, o nome do header não segue a regra
"deixe em branco para manter": em `CustomHeader` ele é obrigatório em toda
atualização.

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

#### Scenario: Atualizar servidor com header customizado sem credencial mantém o segredo
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com `authType:
  "CustomHeader"`, `authHeaderName: "X-Outro-Token"` e sem credencial, para um
  servidor que já era `CustomHeader` com credencial persistida
- **THEN** a API responde com HTTP 200 e `authHeaderName: "X-Outro-Token"`,
  e uma chamada subsequente ao teste de conexão desse servidor envia a
  credencial anteriormente persistida no header `X-Outro-Token`

#### Scenario: Trocar de BearerToken para CustomHeader sem credencial nova é rejeitado
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e sem credencial, para um
  servidor com `authType: "BearerToken"` e credencial persistida
- **THEN** a API responde com erro de validação (HTTP 400) no campo
  `credential` e o registro persistido continua `BearerToken` com a
  credencial anterior

#### Scenario: Trocar de CustomHeader para BearerToken sem credencial nova é rejeitado
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com `authType:
  "BearerToken"` e sem credencial, para um servidor com `authType:
  "CustomHeader"` e credencial persistida
- **THEN** a API responde com erro de validação (HTTP 400) no campo
  `credential` e o registro persistido continua `CustomHeader`, com o mesmo
  `authHeaderName` e a credencial anterior

#### Scenario: Trocar de BearerToken para CustomHeader com credencial nova
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e uma credencial nova,
  para um servidor com `authType: "BearerToken"`
- **THEN** a API responde com HTTP 200, `authType: "CustomHeader"` e
  `authHeaderName: "X-API-TOKEN"`, e a credencial persistida passa a ser a
  nova

#### Scenario: Trocar de CustomHeader para BearerToken com credencial nova limpa o nome do header
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com `authType:
  "BearerToken"` e uma credencial nova, para um servidor com `authType:
  "CustomHeader"`
- **THEN** a API responde com HTTP 200, `authType: "BearerToken"` e
  `authHeaderName: null`

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

#### Scenario: Transicionar de CustomHeader para sem autenticação limpa credencial e nome do header
- **WHEN** um cliente envia `PUT /mcp-servers/{id}` com `authType: "None"`
  para um servidor que tinha `authType: "CustomHeader"`, nome de header e
  credencial persistidos
- **THEN** a API atualiza o registro, a credencial anteriormente persistida
  deixa de existir, e `authHeaderName` passa a ser nulo

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

### Requirement: Descoberta de tools de um servidor MCP cadastrado
O sistema SHALL permitir, via `apps/api`, consultar a lista atual de tools
oferecidas por um `McpServer` cadastrado, executando um handshake real do
protocolo MCP (`tools/list`) contra a URL, tipo de autenticação e credencial
persistidos do servidor. O resultado SHALL refletir o estado atual do
servidor no momento da chamada, sem nenhum catálogo de tools persistido ou
cacheado em `apps/api`.

#### Scenario: Descoberta de tools com handshake bem-sucedido
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` cadastrado, acessível, cujo servidor MCP responde ao
  `tools/list`
- **THEN** a API responde com HTTP 200 e a lista de tools atualmente
  oferecidas pelo servidor (nome e descrição de cada uma)

#### Scenario: Descoberta de tools de servidor MCP inexistente retorna 404
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um `id` que
  não corresponde a nenhum `McpServer` cadastrado
- **THEN** a API responde com HTTP 404

#### Scenario: Descoberta de tools com host inalcançável
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` cuja URL persistida não responde (host inalcançável, conexão
  recusada ou timeout)
- **THEN** a API responde com um resultado indicando falha, com um motivo
  que identifica problema de conectividade, sem expor nenhuma lista de
  tools

#### Scenario: Descoberta de tools com credencial rejeitada
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` cujo servidor MCP rejeita a credencial persistida (HTTP 401 ou
  403 durante o handshake)
- **THEN** a API responde com um resultado indicando falha, com um motivo
  que identifica rejeição de credencial, sem expor nenhuma lista de tools

#### Scenario: Descoberta de tools de servidor MCP inativo é permitida
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um
  `McpServer` com `isActive: false`
- **THEN** a API executa o handshake normalmente e responde com a lista de
  tools (ou o motivo de falha, conforme os cenários acima), sem bloquear a
  operação por causa do estado inativo

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

### Requirement: Formato de fio do tipo de autenticação e do nome do header
O tipo de autenticação SHALL trafegar no JSON de `apps/api` e ser gravado na
coluna `AuthType` de `mcp_servers` como o nome do membro em string,
exatamente `"None"`, `"BearerToken"` ou `"CustomHeader"`, nunca como
ordinal. O nome do header SHALL trafegar no JSON como a propriedade
`authHeaderName` (string ou `null`) e ser gravado na coluna
`AuthHeaderName`, exatamente como foi informado.

#### Scenario: O tipo novo vai para o fio e para o banco como string
- **WHEN** um servidor é cadastrado com `authType: "CustomHeader"` e
  `authHeaderName: "X-API-TOKEN"`
- **THEN** o texto bruto da resposta contém `"authType":"CustomHeader"` e
  `"authHeaderName":"X-API-TOKEN"`, e a coluna `AuthType` da linha gravada
  contém a string `CustomHeader`

### Requirement: Validação do tipo de autenticação por nome de membro
`apps/api` SHALL aceitar em `authType` apenas o nome de um tipo definido
(`None`, `BearerToken` ou `CustomHeader`), sem diferenciar maiúsculas de
minúsculas, no cadastro, na atualização e no teste de configuração não
salva. Valor numérico, combinação separada por vírgula ou nome não definido
SHALL ser recusado com HTTP 400 no campo `authType`. A mensagem de erro SHALL
listar os valores aceitos a partir dos tipos definidos, não de um texto fixo.

#### Scenario: authType numérico é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType: "2"`
- **THEN** a API responde com HTTP 400 no campo `authType` e não cria
  nenhum registro

#### Scenario: authType que é combinação de nomes é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "None,BearerToken"`
- **THEN** a API responde com HTTP 400 no campo `authType` e não cria
  nenhum registro

#### Scenario: A mensagem de valores aceitos inclui o tipo novo
- **WHEN** um cliente envia `POST /mcp-servers` com `authType: "Basic"`
- **THEN** a API responde com HTTP 400, e a mensagem do campo `authType`
  cita `None`, `BearerToken` e `CustomHeader`

### Requirement: Validação do nome do header customizado
Quando o tipo de autenticação for `CustomHeader`, `apps/api` SHALL validar o
nome do header no cadastro, na atualização e no teste de configuração não
salva, **antes** de qualquer conexão ao servidor MCP, e SHALL recusar com
HTTP 400 no campo `authHeaderName` um nome que:

- não seja um token HTTP válido (RFC 9110, seção 5.6.2: um ou mais
  caracteres `tchar`, sem espaço, dois-pontos ou caractere de controle);
- o .NET recuse como header de requisição (por exemplo, `Content-Type` e os
  demais headers de conteúdo);
- coincida, sem diferenciar maiúsculas de minúsculas, com um header que o
  SDK MCP ou o próprio HTTP controlam: qualquer nome com prefixo `Mcp-`,
  `Accept`, `Last-Event-ID`, `Host`, `Connection`, `Keep-Alive`,
  `Proxy-Connection`, `Transfer-Encoding`, `TE`, `Trailer`, `Upgrade` e
  `Expect`.

`Authorization` SHALL ser aceito como nome de header customizado. A resposta
de erro SHALL NOT conter o valor da credencial em nenhum campo.

#### Scenario: Nome de header com caractere fora de token é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, `authHeaderName: "X API"` e uma credencial
- **THEN** a API responde com HTTP 400 no campo `authHeaderName` e não cria
  nenhum registro

#### Scenario: Header de conteúdo é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, `authHeaderName: "Content-Type"` e uma credencial
- **THEN** a API responde com HTTP 400 no campo `authHeaderName` e não cria
  nenhum registro

#### Scenario: Header reservado do protocolo MCP é rejeitado sem diferenciar caixa
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, `authHeaderName: "mcp-session-id"` e uma credencial
- **THEN** a API responde com HTTP 400 no campo `authHeaderName` e não cria
  nenhum registro

#### Scenario: Header Host é rejeitado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, `authHeaderName: "Host"` e uma credencial
- **THEN** a API responde com HTTP 400 no campo `authHeaderName` e não cria
  nenhum registro

#### Scenario: Authorization é aceito como header customizado
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, `authHeaderName: "Authorization"` e a credencial
  `Token abc`
- **THEN** a API cria o registro com HTTP 201 e `authHeaderName:
  "Authorization"`

#### Scenario: Nome inválido no teste não salvo responde 400 sem o segredo e sem conectar
- **WHEN** um cliente envia `POST /mcp-servers/test` com `authType:
  "CustomHeader"`, `authHeaderName: "Content-Type"` e uma credencial
- **THEN** a API responde com HTTP 400 no campo `authHeaderName`, o texto
  bruto da resposta não contém o valor da credencial, e nenhuma requisição é
  feita à URL informada

### Requirement: Validação do valor do token do header customizado
Quando o tipo de autenticação for `CustomHeader`, `apps/api` SHALL recusar
com HTTP 400 no campo `credential`, no cadastro, na atualização que informa
credencial nova e no teste de configuração não salva, uma credencial com
algum caractere fora do ASCII visível ou espaço (`0x20` a `0x7E`), ou com
espaço no início ou no fim. No teste de configuração não salva com
`CustomHeader`, credencial ausente ou vazia SHALL também ser recusada com
HTTP 400 no campo `credential`, sem nenhuma conexão à URL informada. A
mensagem de erro SHALL NOT conter o valor da credencial.

#### Scenario: Credencial com quebra de linha é rejeitada
- **WHEN** um cliente envia `POST /mcp-servers` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e uma credencial que
  contém `\r\n`
- **THEN** a API responde com HTTP 400 no campo `credential`, não cria
  nenhum registro, e o texto bruto da resposta não contém a credencial

#### Scenario: Credencial com caractere não ASCII é rejeitada
- **WHEN** um cliente envia `POST /mcp-servers/test` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e uma credencial com `ç`
- **THEN** a API responde com HTTP 400 no campo `credential`, sem conectar à
  URL informada

#### Scenario: Teste não salvo com CustomHeader sem credencial é rejeitado sem conectar
- **WHEN** um cliente envia `POST /mcp-servers/test` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e sem credencial (ou com
  credencial vazia)
- **THEN** a API responde com HTTP 400 no campo `credential`, e nenhuma
  requisição é feita à URL informada

### Requirement: Envio da credencial conforme o tipo de autenticação nas conexões de apps/api
`apps/api` SHALL enviar a credencial conforme o tipo de autenticação em toda
conexão a um servidor MCP (teste de configuração não salva, re-teste de
servidor salvo e descoberta de tools): em `BearerToken`, no header
`Authorization` com o valor `Bearer <credencial>`; em `CustomHeader`, no
header de nome `authHeaderName` com a credencial como valor, sem nenhum
prefixo e sem enviar `Authorization`; em `None`, sem nenhum dos dois.

Quando a conexão parte de um registro salvo (re-teste, descoberta de tools e
validação do vínculo de agente) e o tipo é `CustomHeader`, `apps/api` SHALL
recusar, antes de qualquer requisição à URL, um nome de header persistido
ausente ou que o .NET recuse como header de requisição. O resultado SHALL ser
uma falha com motivo `Unknown`, cuja mensagem cita o nome do header e SHALL
NOT conter o valor da credencial.

#### Scenario: Teste não salvo com header customizado envia o token no header configurado
- **WHEN** um cliente envia `POST /mcp-servers/test` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e a credencial `abc`,
  para um servidor MCP que só aceita requisições com `X-API-TOKEN: abc`
- **THEN** a API responde com HTTP 200 e um resultado indicando sucesso, e
  as requisições recebidas pelo servidor não têm header `Authorization`

#### Scenario: Header customizado recusado pelo servidor é credencial rejeitada
- **WHEN** um cliente envia `POST /mcp-servers/test` com `authType:
  "CustomHeader"`, `authHeaderName: "X-API-TOKEN"` e uma credencial que o
  servidor MCP recusa com HTTP 401
- **THEN** a API responde com HTTP 200 e um resultado indicando falha, com o
  motivo de credencial rejeitada

#### Scenario: Descoberta de tools de servidor salvo com header customizado
- **WHEN** um cliente envia `GET /mcp-servers/{id}/tools` para um servidor
  salvo com `authType: "CustomHeader"`, cujo servidor MCP só aceita o header
  configurado com a credencial persistida
- **THEN** a API responde com HTTP 200 e a lista de tools do servidor

#### Scenario: Nome de header recusado em servidor salvo não vaza o segredo
- **WHEN** um servidor tem, gravado por SQL direto, `AuthType`
  `CustomHeader`, `AuthHeaderName` `Content-Type` e uma credencial cifrada que
  decifra para um valor conhecido, e um cliente envia `GET
  /mcp-servers/{id}/tools` e `POST /mcp-servers/{id}/test` para ele
- **THEN** as duas respostas indicam falha com motivo `Unknown`, nenhuma
  requisição é feita à URL do servidor, e o texto bruto de nenhuma das duas
  respostas contém a credencial

#### Scenario: BearerToken continua enviando Authorization Bearer
- **WHEN** um cliente envia `POST /mcp-servers/test` com `authType:
  "BearerToken"` e a credencial `abc`, para um servidor MCP que só aceita
  `Authorization: Bearer abc`
- **THEN** a API responde com HTTP 200 e um resultado indicando sucesso

