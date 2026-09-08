## ADDED Requirements

### Requirement: Nomes únicos no conjunto final de tools do agente

`apps/workers` SHALL garantir que o conjunto de tools entregue ao LLM de uma
execução — a união das tools MCP resolvidas com as tools de delegação
resolvidas — não contém dois nomes iguais, independentemente de as duas tools
colidentes virem do mesmo conjunto de origem ou de conjuntos diferentes.

Uma colisão SHALL ser resolvida **renomeando** a tool que perde, nunca
descartando-a: as duas tools permanecem no conjunto e permanecem chamáveis de
forma independente.

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

#### Scenario: Conjunto sem nenhuma colisão preserva todos os nomes
- **WHEN** as tools MCP e de delegação de um agente resolvem para nomes todos
  distintos entre si
- **THEN** nenhum nome é alterado — cada tool mantém exatamente o nome que o
  seu resolvedor produziu

### Requirement: Precedência declarada entre os conjuntos de origem

Na resolução de uma colisão, a tool das tools MCP SHALL manter o nome
pretendido e a tool de delegação SHALL ser a renomeada. Dentro de um mesmo
conjunto de origem, a primeira tool na ordem de resolução SHALL manter o nome
pretendido.

Esta precedência SHALL ser propriedade declarada do ponto que une os
conjuntos, não consequência da ordem em que os dois conjuntos são
concatenados.

#### Scenario: MCP mantém o nome, delegação é renomeada
- **WHEN** uma tool MCP e uma tool de delegação do mesmo agente resolvem para
  o mesmo nome
- **THEN** a tool MCP é exposta com o nome pretendido e a tool de delegação é
  exposta com um nome derivado do seu por sufixo de dedupe

### Requirement: Nome de tool dentro do limite verificado de 64 caracteres

Todo nome exposto ao LLM SHALL ter no máximo 64 caracteres e conter apenas
`[a-zA-Z0-9_-]`.

Os dois valores têm fonte primária, e é a superfície de API que os fixa:
`FunctionObject.name` da especificação OpenAPI publicada pelo OpenAI, na
superfície **Chat Completions** — a que `apps/workers` usa, via
`ChatClientResolver.BuildOpenAi` → `GetChatClient(model).AsIChatClient()` —,
declara *"Must be a-z, A-Z, 0-9, or contain underscores and dashes, with a
maximum length of 64"*. O Gemini declara 128 no seu documento de descoberta
oficial, um superconjunto deste conjunto de caracteres. O limite do Anthropic
**não é declarado** em nenhuma fonte primária consultada, então este requisito
SHALL NOT ser justificado como "o mínimo entre os três provedores": está
verificado contra dois, e a lacuna do terceiro é registrada como risco próprio
no `design.md` (R8).

A garantia SHALL valer **depois** do sufixo de dedupe, não apenas antes: um
sufixo aplicado a um nome já no limite SHALL encurtar a base para caber, nunca
ultrapassar o limite.

#### Scenario: Sufixo de dedupe aplicado a um nome já no limite de 64
- **WHEN** duas tools do mesmo agente colidem num nome que já ocupa exatamente
  64 caracteres
- **THEN** as duas tools recebem nomes distintos e ambos os nomes têm no
  máximo 64 caracteres

#### Scenario: Nome curto não é alterado pelo limite
- **WHEN** as tools de um agente resolvem para nomes bem abaixo de 64
  caracteres, sem colisão
- **THEN** nenhum nome é truncado nem recebe sufixo

### Requirement: Caractere inicial do nome é restrição própria do repositório

`apps/workers` SHALL garantir que todo nome exposto ao LLM comece por letra
ASCII ou `_`, prefixando `_` quando o nome derivado não satisfizer isso.

Esta restrição SHALL ser declarada como **escolha do repositório**, não como
exigência de provedor: nenhuma das fontes primárias consultadas a exige — o
schema do OpenAI para Chat Completions aceita `[a-zA-Z0-9_-]` em qualquer
posição, inclusive dígito inicial, e o documento de descoberta do Gemini não
declara regra de posição. O motivo de mantê-la é não alterar nomes que hoje já
são expostos com o `_` prefixado; removê-la mudaria o nome de tools em uso sem
nenhum ganho verificado.

#### Scenario: Nome derivado que começa com dígito recebe `_` na frente
- **WHEN** a composição do nome de uma tool produz uma cadeia que começa com um
  dígito
- **THEN** o nome exposto ao LLM é essa cadeia prefixada por `_`

#### Scenario: Nome derivado que já começa com letra não é alterado
- **WHEN** a composição do nome de uma tool produz uma cadeia que já começa com
  uma letra ASCII
- **THEN** o nome exposto ao LLM é exatamente essa cadeia, sem prefixo

### Requirement: Colisão de nome é sensível a caixa

`apps/workers` SHALL considerar dois nomes colidentes apenas quando forem
iguais caractere a caractere (comparação ordinal, sensível a caixa) — o mesmo
critério que o cliente de invocação de função usa para resolver uma chamada de
volta para a tool. Dois nomes que diferem apenas na caixa SHALL NOT ser
tratados como colisão, e portanto SHALL NOT ser renomeados.

#### Scenario: Duas tools cujos nomes diferem só na caixa não são renomeadas
- **WHEN** o conjunto final de tools de um agente contém uma tool chamada
  `Search` e outra chamada `search`
- **THEN** as duas permanecem com exatamente esses nomes, nenhuma recebe sufixo
  de dedupe, e nenhum aviso de renomeação é registrado

#### Scenario: Duas tools com o nome idêntico são renomeadas
- **WHEN** o conjunto final de tools de um agente contém duas tools chamadas
  `search`, idênticas caractere a caractere
- **THEN** uma delas é renomeada com sufixo de dedupe

### Requirement: Renomeação por colisão é observável no log

`apps/workers` SHALL registrar um aviso a cada renomeação por colisão,
identificando o agente, o nome pretendido, o nome final atribuído e o conjunto
de origem de cada uma das duas tools envolvidas — o operador precisa conseguir
entender por que a tool que cadastrou não é a que o agente chamou.

O aviso SHALL NOT derrubar a execução: a task segue normalmente com o conjunto
renomeado (convenção 4).

#### Scenario: Colisão renomeada aparece no log com as duas origens
- **WHEN** uma tool de delegação é renomeada por colidir com uma tool MCP
- **THEN** um aviso é registrado contendo o `AgentId`, o nome pretendido, o
  nome final e a indicação de que a origem que manteve o nome foi MCP e a
  renomeada foi delegação

#### Scenario: Conjunto sem colisão não registra aviso
- **WHEN** o conjunto final de tools de um agente não tem nenhuma colisão
- **THEN** nenhum aviso de renomeação é registrado

### Requirement: Conjunto de nomes estável entre execuções

`apps/workers` SHALL produzir exatamente o mesmo conjunto de nomes de tool
para o mesmo agente e o mesmo cadastro — mesmos vínculos `AgentMcpServer` com
as mesmas `AllowedTools`, e os mesmos `AgentDelegation` —, incluindo quais
tools foram renomeadas e para quais nomes.

#### Scenario: Duas execuções do mesmo cadastro com colisão produzem os mesmos nomes
- **WHEN** um agente cujo cadastro contém uma colisão de nome é resolvido em
  duas execuções diferentes, sem nenhuma mudança no cadastro
- **THEN** os nomes das tools resolvidas são idênticos nas duas execuções, e a
  mesma tool é a renomeada nas duas
