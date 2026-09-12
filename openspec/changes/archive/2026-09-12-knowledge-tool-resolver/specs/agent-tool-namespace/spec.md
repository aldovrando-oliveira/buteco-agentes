## MODIFIED Requirements

### Requirement: Nomes únicos no conjunto final de tools do agente

`apps/workers` SHALL garantir que o conjunto de tools entregue ao LLM de uma
execução — a união das tools MCP resolvidas, das tools de delegação resolvidas e
das tools de conhecimento resolvidas — não contém dois nomes iguais,
independentemente de as duas tools colidentes virem do mesmo conjunto de origem
ou de conjuntos diferentes.

Uma colisão SHALL ser resolvida **renomeando** a tool que perde, nunca
descartando-a: as tools permanecem no conjunto e permanecem chamáveis de forma
independente.

#### Scenario: Colisão entre uma tool MCP e uma tool de delegação
- **WHEN** um agente tem um `McpServer` vinculado cuja tool resolve para um
  nome idêntico ao de uma tool de delegação resolvida para o mesmo agente
- **THEN** o conjunto entregue ao LLM contém as duas tools, com nomes
  distintos, e cada uma roteia para o seu destino real (servidor MCP e agente
  Target, respectivamente)

#### Scenario: Colisão entre duas tools MCP de servidores diferentes
- **WHEN** um agente tem dois `McpServer`s vinculados e ativos cujos nomes
  sanitizados coincidem, cada um oferecendo (e permitindo via `AllowedTools`)
  uma tool de mesmo nome
- **THEN** o conjunto entregue ao LLM contém as duas tools, com nomes
  distintos, e cada uma roteia para o servidor MCP correto

#### Scenario: Colisão entre duas tools de conhecimento de bases diferentes
- **WHEN** um agente tem duas `KnowledgeBase` vinculadas e ativas cujos nomes
  produzem o mesmo nome de tool
- **THEN** o conjunto entregue ao LLM contém as duas tools, com nomes distintos,
  e cada uma busca na sua própria base

#### Scenario: Colisão entre uma tool MCP e uma tool de conhecimento
- **WHEN** a tool de uma `KnowledgeBase` vinculada resolve para um nome idêntico
  ao de uma tool MCP resolvida para o mesmo agente
- **THEN** o conjunto entregue ao LLM contém as duas tools, com nomes distintos,
  e cada uma roteia para o seu destino real (servidor MCP e base de
  conhecimento, respectivamente)

#### Scenario: Conjunto sem nenhuma colisão preserva todos os nomes
- **WHEN** as tools MCP, de delegação e de conhecimento de um agente resolvem
  para nomes todos distintos entre si
- **THEN** nenhum nome é alterado — cada tool mantém exatamente o nome que o
  seu resolvedor produziu

### Requirement: Precedência declarada entre os conjuntos de origem

Na resolução de uma colisão, a precedência entre os conjuntos de origem SHALL
ser, da mais forte para a mais fraca: **tools MCP**, depois **tools de
delegação**, depois **tools de conhecimento**. A tool do conjunto mais forte
mantém o nome pretendido e a do conjunto mais fraco é a renomeada. Dentro de um
mesmo conjunto de origem, a primeira tool na ordem de resolução SHALL manter o
nome pretendido.

Esta precedência SHALL ser propriedade declarada do ponto que une os conjuntos,
não consequência da ordem em que os conjuntos são concatenados.

#### Scenario: MCP mantém o nome, delegação é renomeada
- **WHEN** uma tool MCP e uma tool de delegação do mesmo agente resolvem para
  o mesmo nome
- **THEN** a tool MCP é exposta com o nome pretendido e a tool de delegação é
  exposta com um nome derivado do seu por sufixo de dedupe

#### Scenario: Conhecimento é o renomeado contra qualquer um dos outros dois
- **WHEN** uma tool de conhecimento resolve para o mesmo nome de uma tool MCP ou
  de uma tool de delegação do mesmo agente
- **THEN** a tool MCP ou de delegação é exposta com o nome pretendido e a tool
  de conhecimento é exposta com um nome derivado do seu por sufixo de dedupe

### Requirement: Renomeação por colisão é observável no log

`apps/workers` SHALL registrar um aviso a cada renomeação por colisão,
identificando o agente, o nome pretendido, o nome final atribuído e o conjunto
de origem de cada uma das duas tools envolvidas — o operador precisa conseguir
entender por que a tool que cadastrou não é a que o agente chamou.

O conjunto de origem registrado SHALL distinguir os **três** conjuntos: MCP,
delegação e conhecimento.

O aviso SHALL NOT derrubar a execução: a task segue normalmente com o conjunto
renomeado (convenção 4).

#### Scenario: Colisão renomeada aparece no log com as duas origens
- **WHEN** uma tool de delegação é renomeada por colidir com uma tool MCP
- **THEN** um aviso é registrado contendo o `AgentId`, o nome pretendido, o
  nome final e a indicação de que a origem que manteve o nome foi MCP e a
  renomeada foi delegação

#### Scenario: Colisão envolvendo conhecimento nomeia esse conjunto no aviso
- **WHEN** uma tool de conhecimento é renomeada por colidir com uma tool de
  outro conjunto
- **THEN** o aviso registrado identifica a origem renomeada como conhecimento

#### Scenario: Conjunto sem colisão não registra aviso
- **WHEN** o conjunto final de tools de um agente não tem nenhuma colisão
- **THEN** nenhum aviso de renomeação é registrado

### Requirement: Conjunto de nomes estável entre execuções

`apps/workers` SHALL produzir exatamente o mesmo conjunto de nomes de tool
para o mesmo agente e o mesmo cadastro — mesmos vínculos `AgentMcpServer` com
as mesmas `AllowedTools`, os mesmos `AgentDelegation`, e os mesmos
`AgentKnowledgeBase` com as mesmas bases ativas —, incluindo quais tools foram
renomeadas e para quais nomes.

#### Scenario: Duas execuções do mesmo cadastro com colisão produzem os mesmos nomes
- **WHEN** um agente cujo cadastro contém uma colisão de nome é resolvido em
  duas execuções diferentes, sem nenhuma mudança no cadastro
- **THEN** os nomes das tools resolvidas são idênticos nas duas execuções, e a
  mesma tool é a renomeada nas duas

#### Scenario: Ordem de resolução das tools de conhecimento é estável
- **WHEN** um agente com duas ou mais bases de conhecimento vinculadas e ativas
  é resolvido em duas execuções diferentes, sem mudança no cadastro
- **THEN** as tools de conhecimento aparecem na mesma ordem nas duas execuções,
  independentemente da ordem em que o banco devolveria as linhas
