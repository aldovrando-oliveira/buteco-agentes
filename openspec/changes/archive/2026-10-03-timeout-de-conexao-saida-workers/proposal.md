**Issue:** #46

## Why

Nenhuma chamada HTTP de saída de `apps/workers` tem timeout de **conexão**. Se o
SYN fica sem resposta ou o TLS não completa, a chamada espera o que vier primeiro
entre o timeout total do caminho e o timeout de SYN do sistema operacional, vezes
as retentativas do SDK. Enquanto espera, ela segura o lock de contexto, e a
mensagem seguinte da mesma conversa desiste em 30 s (`CommandTimeout`). Em
22/09/2026, no ambiente de dev, essa cadeia derrubou uma mensagem (1 das 3 falhas
`ContextLock` medidas na #46).

Reproduzido em 03/10/2026 sobre `a7960d9`, contra `10.255.255.1` (SYN sem
resposta, medido na rede da máquina antes de rodar), com os resolvedores reais:

| caminho | espera até falhar (macOS) | tentativas |
|---|---|---|
| chat OpenAI | **300,3 s** | 4 |
| embedding OpenAI | **300,0 s** | 4 |
| chat Gemini | **75,5 s** | 1 |
| MCP | **60,1 s** | sonda + initialize |
| chat Anthropic | **30,3 s** | 1 |
| push notification | 5,1 s | 1 |

No macOS o SYN desiste em 75,0 s (medido). Em Linux, onde produção roda, o
default do kernel é maior (~127 s, lido, não medido), então o limite passa a ser o
`NetworkTimeout`/`HttpClient.Timeout` de 100 s. É o "100 s no Gemini" observado
na #46.

**Posição:** 7, antes da `replicas-de-worker` (fixada pelo dono). A série que
aquela change vai ler pode estar contaminada por esperas de rede.

## What Changes

- **`apps/workers`, timeout de conexão de 5 s** em todo caminho que hoje espera
  conexão sem limite: chat OpenAI, embedding OpenAI, chat Anthropic, chat Gemini e
  MCP. O valor é constante, com o motivo escrito ao lado (convenção 2: não há
  cenário de alguém precisar de outro valor). O timeout cobre TCP **e** TLS
  (medido). Com ele, **cada chamada** inalcançável segura o lock por ~20 s (OpenAI,
  4 tentativas), ~15 s (MCP) e ~5 s (Anthropic e Gemini), medido em simulação. O
  limite é por chamada, não por execução: uma execução com duas ou mais chamadas
  inalcançáveis passa dos 30 s (ver Risks no `design.md`).
- **O que a correção não faz:** com IPv6 com rota e sem conectividade (o caso de
  22/09), a tentativa por IPv6 consome os 5 s e falha, e o .NET não cai para IPv4
  dentro do prazo. A chamada continua falhando, só mais rápido. A correção impede
  a cascata no lock, não faz a chamada funcionar. O contorno
  `DOTNET_SYSTEM_NET_DISABLEIPV6=1` continua necessário onde ele existe hoje.
- **Os três SDKs de LLM recebem o handler pelos pontos de injeção que cada um
  expõe** (verificados por decompilação): `OpenAIClientOptions.Transport`, a
  propriedade `HttpClient` do `AnthropicClient` e `ClientOptions.HttpClientFactory`
  do `Google.GenAI`. O handler é **um por processo** para cada SDK, nunca por
  execução. O que cada handler default configurava hoje é preservado (descompressão
  `GZip` no Anthropic, redirecionamento desligado no OpenAI, `Timeout` de 100 s no
  Gemini).
- **O registro do `HttpClient` do MCP sai do `Program.cs` para um método** chamado
  pelo `Program.cs` e pelo guarda, para que o guarda exercite o registro de
  produção e não uma cópia.
- **Guardas por caminho**, com um servidor em loopback que aceita o TCP e nunca
  responde ao TLS, sem depender da rede da máquina. Reprovam no código atual e
  passam com a correção.

**Fora de escopo, explícito:**

- **push notification:** o timeout total de 5 s já limita a conexão (5,1 s
  medido), e um timeout de conexão de 5 s não mudaria nada. A retentativa e a
  reconciliação de `PendingDispatch` são da **#47**;
- **resposta travada depois de conectar:** é outro limite (o de resposta), com
  outra decisão. Vai para a **#134** (OpenAI segura até 400 s nesse caso);
- **a corrida da aquisição do lock** na borda do `CommandTimeout`: **#132**;
- `TaskJobConsumer` e o caminho de parada (acabaram de mudar na #49),
  `KnowledgeIndexingConsumer` (#125) e a reabertura da métrica (#126);
- **a investigação do lock** (esperas de 83,6 s e 103,6 s, fila de 44,6 s): vai para
  uma issue `tipo: investigação` aberta antes do archive. A #46 fecha pelo conserto.

## Capabilities

### New Capabilities

- `workers-outbound-connect-timeout`: toda chamada HTTP de saída de
  `apps/workers` que pode segurar o lock de contexto (LLM dos três provedores,
  embedding, MCP) desiste de estabelecer conexão num prazo limitado, e a falha
  segue o caminho de falha que cada uma já tem.

### Modified Capabilities

Nenhuma. O comportamento de falha de cada caminho não muda: a task termina em
`failed` na falha de LLM, a tool de conhecimento degrada, e o servidor MCP é
excluído do conjunto (requisito "Degradação por servidor MCP inacessível" de
`mcp-tool-execution`, que já cita timeout). Muda só **quando** a falha chega.

## Impact

- **Código (só `apps/workers`):** `Agents/ChatClientResolver.cs`,
  `Knowledge/Embedding/EmbeddingGeneratorResolver.cs`, `Mcp/McpTransportFactory.cs`,
  `Program.cs`, e um arquivo novo com os handlers e a constante. Os comentários de
  `EmbeddingGeneratorResolver`, `KnowledgeToolSetResolver` e
  `MeasuredEmbeddingGenerator` que citam `HttpClientPipelineTransport.Shared` como
  garantia de "um `HttpClient` por processo" são reescritos sobre o transporte
  novo, que mantém a mesma propriedade. Nada em `libs/` e nenhum outro app.
- **Testes:** uma classe nova, sem contêiner, fora da `WorkerHostCollection` (a
  régua de contenção não muda, convenção 22). Os casos de Anthropic e Gemini ficam
  numa collection sem paralelismo, porque apontam o SDK por variável de ambiente
  do processo.
- **Comportamento observável:** conexão que não se estabelece em 5 s falha em vez
  de esperar. Conexão legítima mais lenta que 5 s (TCP + TLS) também passa a
  falhar. Não há dado de tempo de conexão em `provider_calls`/`embedding_calls`
  para medir isso; ver Risks no `design.md`.
