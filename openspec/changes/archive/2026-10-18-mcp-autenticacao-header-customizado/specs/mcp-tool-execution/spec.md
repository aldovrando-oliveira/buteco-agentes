## MODIFIED Requirements

### Requirement: Degradação por servidor MCP inacessível durante a resolução de tools

`apps/workers` SHALL excluir, quando a conexão com um `McpServer` vinculado
e ativo falhar durante a resolução do conjunto de tools de uma execução
(host inalcançável, timeout, handshake falho, ou credencial persistida que
não pode ser decifrada com a chave de criptografia atualmente configurada),
esse servidor do conjunto de tools daquela execução, e continuar o
processamento da task normalmente com as tools dos demais servidores
vinculados — a task SHALL NOT falhar por causa desse servidor específico.

A mesma exclusão por servidor SHALL valer, **sem nenhuma tentativa de
conexão** àquele servidor, quando a linha persistida não puder ser usada
para montar a autenticação:

- o `AuthType` gravado não é um tipo que esta versão de `apps/workers`
  conhece (por exemplo, gravado por um `apps/api` mais novo durante um
  deploy fora de ordem ou depois de um rollback só de `apps/workers`). A
  leitura das demais linhas SHALL continuar funcionando;
- o tipo é `CustomHeader` e o nome do header persistido está ausente ou é um
  nome que o .NET recusa como header de requisição.

Em nenhum desses casos o valor da credencial, decifrado ou cifrado, SHALL
aparecer no log.

#### Scenario: Um McpServer inalcançável não derruba a task
- **WHEN** um agente tem dois `McpServer`s vinculados e ativos, e um deles
  está inalcançável (host não responde) no momento da execução da task
- **THEN** a task é processada normalmente até `completed`, oferecendo ao
  LLM apenas as tools do servidor que respondeu, sem nenhum erro visível
  relacionado ao servidor inacessível no resultado da task

#### Scenario: Todos os McpServers vinculados falham e a task ainda assim processa
- **WHEN** todos os `McpServer`s vinculados e ativos de um agente estão
  inacessíveis no momento da execução da task
- **THEN** a task é processada normalmente até `completed`, sem nenhuma tool
  MCP disponível ao LLM, e sem falhar a task por causa disso

#### Scenario: Falha ao decifrar a credencial persistida de um McpServer degrada apenas aquele servidor
- **WHEN** a credencial persistida de um `McpServer` vinculado não pode ser
  decifrada com a chave de criptografia atualmente configurada em
  `apps/workers`
- **THEN** esse servidor é excluído do conjunto de tools daquela execução, e
  a task continua normalmente com as tools dos demais servidores vinculados

#### Scenario: AuthType desconhecido degrada apenas aquele servidor
- **WHEN** um agente tem dois `McpServer`s vinculados e ativos, e a coluna
  `AuthType` de um deles contém um valor que esta versão de `apps/workers`
  não conhece
- **THEN** a resolução de tools não lança exceção, esse servidor é excluído
  do conjunto sem nenhuma requisição HTTP a ele, as tools do outro servidor
  continuam disponíveis, e a task é processada até `completed`

#### Scenario: Nome de header recusado no banco degrada o servidor sem gravar o segredo no log
- **WHEN** um `McpServer` vinculado e ativo tem `AuthType` `CustomHeader`,
  um nome de header persistido que o .NET recusa como header de requisição
  (por exemplo, `Content-Type`) e uma credencial que decifra para um valor
  conhecido
- **THEN** esse servidor é excluído do conjunto sem nenhuma requisição HTTP
  a ele, e nenhuma linha de log produzida durante a resolução contém o valor
  decifrado da credencial

## ADDED Requirements

### Requirement: Autenticação da conexão MCP na execução

`apps/workers` SHALL enviar a credencial conforme o `AuthType` persistido em
toda requisição HTTP que faz a um `McpServer` durante uma execução (sonda
`server/discover`, `initialize`, notificações, `tools/list`, `tools/call` e o
encerramento de sessão): em `BearerToken`, no header
`Authorization` com o valor `Bearer <credencial>`; em `CustomHeader`, no
header cujo nome é o `AuthHeaderName` persistido, com a credencial como
valor, sem nenhum prefixo e sem enviar `Authorization`; em `None`, sem
nenhum dos dois.

#### Scenario: Execução com header customizado chama a tool com o token no header configurado
- **WHEN** um agente tem vinculado um `McpServer` com `AuthType`
  `CustomHeader`, `AuthHeaderName` `X-API-TOKEN` e credencial `abc`, cujo
  servidor só aceita requisições com `X-API-TOKEN: abc`, e o LLM decide
  chamar uma tool desse servidor
- **THEN** a tool é chamada de verdade, o resultado dela chega à resposta
  final da task, e nenhuma requisição recebida pelo servidor tem header
  `Authorization`

#### Scenario: Execução com BearerToken continua enviando Authorization Bearer
- **WHEN** um agente tem vinculado um `McpServer` com `AuthType`
  `BearerToken` e credencial `abc`, cujo servidor só aceita
  `Authorization: Bearer abc`
- **THEN** a resolução inclui as tools desse servidor no conjunto da
  execução

#### Scenario: Linha gravada pelo apps/api é lida e usada pelo apps/workers
- **WHEN** `apps/api` grava, pelo próprio contexto e com a própria cifra, um
  `McpServer` com `AuthType` `CustomHeader`, `AuthHeaderName` `X-API-TOKEN`
  e uma credencial, e `apps/workers` resolve as tools de um agente vinculado
  a ele, com a mesma chave de criptografia
- **THEN** `apps/workers` lê o `AuthType` como `CustomHeader`, e o servidor
  recebe o header `X-API-TOKEN` com a credencial original

#### Scenario: Todo tipo gravado pelo apps/api é conhecido pelo apps/workers
- **WHEN** `apps/api` grava, pelo próprio contexto, um `McpServer` para cada
  valor do seu `McpServerAuthType`, e `apps/workers` lê cada linha pelo
  próprio contexto
- **THEN** cada linha é lida com o `AuthType` de mesmo nome que foi gravado,
  e nenhuma é tratada como tipo desconhecido
