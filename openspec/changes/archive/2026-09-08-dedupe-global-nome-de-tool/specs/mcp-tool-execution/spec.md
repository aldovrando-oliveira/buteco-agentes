## MODIFIED Requirements

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

## ADDED Requirements

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
