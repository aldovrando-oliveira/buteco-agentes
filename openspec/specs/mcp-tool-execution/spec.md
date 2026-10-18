# mcp-tool-execution Specification

## Purpose

TBD - defined by change apps-workers-execucao-mcp. Update Purpose after archive.
## Requirements
### Requirement: Descoberta e filtragem de tools MCP por execução

Ao processar uma task, `apps/workers` SHALL, para cada `McpServer` vinculado
ao agente com `IsActive: true`, conectar ao servidor e obter a lista de
tools atualmente oferecida (`tools/list`), e oferecer ao LLM somente a
interseção entre essa lista e o `AllowedTools` persistido para aquele
vínculo. Nenhum catálogo de tools SHALL ser cacheado ou persistido em
`apps/workers` entre execuções — a descoberta é feita ao vivo a cada task.

#### Scenario: Tool permitida e oferecida pelo servidor entra no conjunto
- **WHEN** um agente tem um `McpServer` vinculado e ativo, com uma tool no
  `AllowedTools` que o servidor efetivamente oferece em `tools/list`
- **THEN** essa tool está disponível para o LLM chamar durante o
  processamento da task

#### Scenario: Tool não incluída em AllowedTools não entra no conjunto
- **WHEN** um `McpServer` vinculado oferece uma tool via `tools/list` que não
  está presente no `AllowedTools` daquele vínculo
- **THEN** essa tool não está disponível para o LLM chamar durante o
  processamento da task

#### Scenario: Tool em AllowedTools que o servidor não oferece mais é excluída sem erro
- **WHEN** o `AllowedTools` de um vínculo inclui o nome de uma tool que o
  `tools/list` do servidor correspondente não retorna mais (drift desde o
  momento em que o vínculo foi configurado)
- **THEN** o processamento da task continua normalmente, sem erro, e essa
  tool simplesmente não está disponível para o LLM chamar

#### Scenario: AllowedTools vazio resulta em nenhuma tool daquele servidor
- **WHEN** um `McpServer` está vinculado a um agente com `AllowedTools: []`
- **THEN** nenhuma tool daquele servidor é disponibilizada ao LLM durante o
  processamento da task, sem erro

#### Scenario: McpServer inativo é excluído do conjunto sem tentativa de conexão
- **WHEN** um agente tem um `McpServer` vinculado com `IsActive: false`
- **THEN** nenhuma tool daquele servidor é disponibilizada ao LLM, e
  `apps/workers` não tenta conectar a esse servidor durante o processamento
  da task

#### Scenario: Agente sem nenhum McpServer vinculado processa a task normalmente
- **WHEN** um agente sem nenhum `McpServer` vinculado processa uma task
- **THEN** a task é processada normalmente, sem nenhuma tool MCP oferecida ao
  LLM, e sem nenhuma tentativa de conexão MCP

### Requirement: Round-trip real de chamada de tool MCP

`apps/workers` SHALL rotear, quando o LLM decidir chamar uma tool MCP
disponível durante o processamento de uma task, a chamada para o servidor
MCP real correspondente, aguardar o resultado, e devolvê-lo ao LLM para que
a conversa continue até a task atingir o estado `completed`. O resultado
final da task SHALL refletir o resultado da chamada de tool quando a
resposta do LLM depender dela.

#### Scenario: Chamada de tool bem-sucedida afeta o resultado final da task
- **WHEN** o LLM decide chamar uma tool MCP disponível durante o
  processamento de uma task, e o servidor MCP correspondente responde com
  sucesso
- **THEN** o resultado da chamada é devolvido ao LLM, a conversa continua, e
  a task atinge o estado `completed` com uma resposta que reflete o
  resultado da tool

#### Scenario: Mesma tool pode ser chamada mais de uma vez no mesmo turno
- **WHEN** o LLM decide chamar a mesma tool MCP mais de uma vez durante o
  processamento da mesma task
- **THEN** cada chamada é roteada para o servidor MCP real e respondida
  individualmente, sem que `apps/workers` precise reabrir uma conexão nova a
  cada chamada

### Requirement: Distinção de tools com nomes iguais entre servidores diferentes

`apps/workers` SHALL disponibilizar, de forma distinguível, tools com o
mesmo nome oferecidas por `McpServer`s diferentes vinculados ao mesmo
agente (quando ambas sobrevivem ao filtro de `AllowedTools`), sem que uma
sobrescreva ou oculte a outra no conjunto oferecido ao LLM.

A garantia SHALL valer também quando são os **nomes dos servidores** que
colidem — seja porque sanitizam para a mesma cadeia (dois `McpServer.Name` que
diferem apenas em caracteres fora de `[a-zA-Z0-9_-]`), seja porque compartilham
o mesmo prefixo depois da truncagem no limite de 64 caracteres. O prefixo
`{servidor}__{tool}` SHALL NOT ser o único mecanismo que sustenta esta
distinção.

#### Scenario: Duas tools de mesmo nome em servidores diferentes coexistem no conjunto
- **WHEN** um agente tem dois `McpServer`s vinculados e ativos, cada um
  oferecendo (e permitindo via `AllowedTools`) uma tool de nome idêntico
- **THEN** o conjunto de tools oferecido ao LLM contém as duas tools, cada
  uma identificável e chamável de forma independente, roteando para o
  servidor MCP correto quando chamada

#### Scenario: Dois McpServer cujos nomes sanitizam para a mesma cadeia
- **WHEN** um agente tem dois `McpServer`s vinculados e ativos chamados
  `"Zendesk MCP"` e `"Zendesk.MCP"`, ambos oferecendo (e permitindo via
  `AllowedTools`) uma tool chamada `search`
- **THEN** o conjunto de tools oferecido ao LLM contém as duas tools com nomes
  distintos, cada uma roteando para o seu servidor MCP

#### Scenario: Dois McpServer cujos nomes só diferem depois do caractere 64
- **WHEN** um agente tem dois `McpServer`s vinculados e ativos cujos nomes
  compartilham os primeiros 64 caracteres e diferem depois disso, ambos
  oferecendo a mesma tool
- **THEN** o conjunto de tools oferecido ao LLM contém as duas tools com nomes
  distintos, cada uma com no máximo 64 caracteres, cada uma roteando para o
  seu servidor MCP

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

### Requirement: Ordem determinística da resolução de tools MCP

`apps/workers` SHALL ordenar de forma explícita e estável a consulta que lista
os vínculos `AgentMcpServer` de um agente durante a resolução, para que a ordem
em que os servidores entram no conjunto — e, por consequência, qual tool mantém o nome
pretendido numa colisão — não dependa do plano de execução escolhido pelo banco.

#### Scenario: Servidores entram no conjunto na mesma ordem em execuções repetidas
- **WHEN** o mesmo agente, com o mesmo conjunto de `McpServer`s vinculados e
  ativos, tem suas tools resolvidas em execuções repetidas
- **THEN** as tools aparecem no conjunto resolvido na mesma ordem em todas as
  execuções

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

