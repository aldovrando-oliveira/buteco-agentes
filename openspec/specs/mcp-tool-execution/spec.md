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
