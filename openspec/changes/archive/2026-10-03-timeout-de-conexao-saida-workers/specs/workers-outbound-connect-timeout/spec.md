## ADDED Requirements

### Requirement: Chamada ao provedor de LLM desiste de conectar em prazo limitado

`apps/workers` SHALL limitar a 5 segundos, por tentativa, o tempo para
estabelecer a conexão (TCP e TLS) com o provedor de LLM, nos três provedores
suportados (`openai`, `anthropic`, `gemini`). Esgotado o prazo, a tentativa SHALL
falhar com o erro de conexão, em vez de esperar o timeout total da requisição ou
o do sistema operacional. A falha SHALL seguir o caminho que a falha de LLM já
tem: a task termina em `failed`. O prazo SHALL valer para a chamada da execução
do agente e para a de resumo do histórico, que usam o mesmo cliente.

#### Scenario: Provedor OpenAI cuja conexão não se estabelece
- **WHEN** um agente com provedor `openai` executa e o endpoint configurado aceita
  o TCP mas nunca completa o TLS
- **THEN** cada tentativa da chamada ao LLM falha ao fim do prazo de conexão, com
  o erro de timeout de conexão
- **AND** a chamada inteira, com as retentativas do SDK, termina antes de 30
  segundos, que é o limite de espera pelo lock de contexto da mensagem seguinte

#### Scenario: Provedor Anthropic cuja conexão não se estabelece
- **WHEN** um agente com provedor `anthropic` executa e o endpoint não completa a
  conexão
- **THEN** a chamada falha ao fim do prazo de conexão, com o erro de timeout de
  conexão, sem esperar o timeout por tentativa do SDK

#### Scenario: Provedor Gemini cuja conexão não se estabelece
- **WHEN** um agente com provedor `gemini` executa e o endpoint não completa a
  conexão
- **THEN** a chamada falha ao fim do prazo de conexão, com o erro de timeout de
  conexão, sem esperar os 100 segundos do timeout total

#### Scenario: Conexão que se estabelece dentro do prazo não é afetada
- **WHEN** a conexão com o provedor se estabelece em menos de 5 segundos
- **THEN** a requisição segue normalmente, e o limite de tempo de resposta
  continua sendo o que cada SDK já aplicava

### Requirement: Geração de embedding desiste de conectar em prazo limitado

`apps/workers` SHALL limitar a 5 segundos, por tentativa, o tempo para
estabelecer a conexão com o provedor de embedding. Vale para a consulta da tool
de conhecimento, executada dentro do lock de contexto, e para a indexação. A
falha SHALL seguir o caminho que cada uma já tem: a tool de conhecimento degrada
sem derrubar a execução, e a indexação registra a tentativa como falha.

#### Scenario: Endpoint de embedding cuja conexão não se estabelece
- **WHEN** o endpoint de embedding configurado aceita o TCP mas nunca completa o
  TLS
- **THEN** cada tentativa falha ao fim do prazo de conexão, com o erro de timeout
  de conexão
- **AND** a geração inteira, com as retentativas do SDK, termina antes de 30
  segundos

### Requirement: Conexão com servidor MCP desiste em prazo limitado

`apps/workers` SHALL limitar a 5 segundos o tempo para estabelecer cada conexão
com um `McpServer` vinculado durante a resolução do conjunto de tools. A falha
SHALL seguir a degradação por servidor que já existe: o servidor fica de fora do
conjunto daquela execução e a task continua.

#### Scenario: Servidor MCP cuja conexão não se estabelece
- **WHEN** um agente tem um `McpServer` vinculado e ativo cujo endereço aceita o
  TCP mas nunca completa o TLS
- **THEN** a resolução de tools desiste desse servidor antes do
  `InitializationTimeout` de 60 segundos do cliente MCP, com o erro de timeout de
  conexão
- **AND** o servidor fica de fora do conjunto de tools da execução, e a task
  continua com os demais

#### Scenario: O prazo vale para o registro usado em produção
- **WHEN** o `HttpClient` do MCP é obtido do registro de DI que o processo de
  `apps/workers` usa no startup
- **THEN** ele aplica o prazo de conexão de 5 segundos
