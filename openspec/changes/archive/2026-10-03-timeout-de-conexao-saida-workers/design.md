## Context

A #46 nasceu da execução real de 22/09/2026: uma chamada ao Gemini presa 100 s
num IPv6 com rota e sem conectividade segurou o lock de contexto, e a mensagem
seguinte da conversa morreu esperando por ele (`ContextLock`, 30,3 s). O .NET não
cai para IPv4 enquanto espera, e nenhuma chamada de saída de `apps/workers` tem
timeout de conexão.

Tudo abaixo foi medido em 03/10/2026 sobre `a7960d9`, numa máquina macOS, a menos
que diga "lido". As saídas estão em `~/buteco-runs/46/`; o console de medição não
entra no repositório.

### Inventário das chamadas de saída

Varredura com critério estreito (`HttpClient|AddHttpClient|IHttpClientFactory|HttpMessageHandler|SocketsHttpHandler`)
e largo (construtores de SDK, `new Uri(`, `SendAsync(`, `PostAsync`, `Endpoint =`) em
`apps/workers/src`, linhas abertas uma a uma. O largo acrescentou os três
construtores de SDK, que o estreito só pegava por comentário. Os demais itens do
largo são comentários ou nomes de tool. **Seis pontos criam cliente HTTP de saída:**

| # | caminho | onde | quem cria o handler hoje | ponto de injeção (decompilado) |
|---|---|---|---|---|
| 1 | chat OpenAI | `Agents/ChatClientResolver.cs:143` | `HttpClientPipelineTransport.Shared` do `System.ClientModel` 1.14.0: um `HttpClient` **estático** sobre `HttpClientHandler { AllowAutoRedirect = false }`, `Timeout` infinito | `OpenAIClientOptions.Transport` |
| 2 | embedding OpenAI | `Knowledge/Embedding/EmbeddingGeneratorResolver.cs:83` (chamado por `KnowledgeToolSetResolver.cs:147`, dentro do lock, e por `KnowledgeIndexingService.cs:123`) | o mesmo `Shared` | o mesmo |
| 3 | chat Anthropic | `Agents/ChatClientResolver.cs:158` | o SDK `Anthropic` 12.39.0: `new HttpClient(new HttpClientHandler { AutomaticDecompression = Anthropic.Core.DecompressionMethods.Available })`, `Timeout` infinito, um por `AnthropicClient`. `Available` é um campo do SDK: **`GZip`** se o runtime descomprime gzip, `None` caso contrário (`Anthropic.Core/DecompressionMethods.cs`, decompilado; no .NET 10, `GZip`) | propriedade `HttpClient` (init) |
| 4 | chat Gemini | `Agents/ChatClientResolver.cs:170` | o SDK `Google.GenAI` 1.15.0: `new HttpClient()`, `Timeout` default de 100 s, um por `Client` | `ClientOptions.HttpClientFactory` |
| 5 | MCP | `Mcp/McpTransportFactory.cs:40` | `IHttpClientFactory`, cliente nomeado registrado em `Program.cs:54-58` com `SocketsHttpHandler { PooledConnectionLifetime = 30 s }` | o handler registrado |
| 6 | push notification | `Notifications/PushNotificationSender.cs:42` | `IHttpClientFactory`, cliente nomeado em `Program.cs:73-74`, handler default, `Timeout` 5 s | o handler registrado |

O resumo do histórico usa o mesmo cliente do item 1, 3 ou 4
(`AgentExecutionService.cs:428-450`). A delegação entre agentes não faz HTTP:
publica no RabbitMQ.

### Reprodução, código atual

Destino `10.255.255.1`. Antes de rodar, conferido na rede atual que o SYN fica sem
resposta (15 s sem resposta nas portas 80, 443 e 1001; rota padrão por
`192.168.100.1`, `en0`, sem VPN). Sem timeout da aplicação, o connect do macOS
desiste em **75,0 s**. As tentativas foram contadas pelos `RequestStart` do
EventSource `System.Net.Http`, com uma porta por caminho. Saída:
`repro-1836.txt`.

| caminho | espera | tentativas | exceção |
|---|---|---|---|
| chat OpenAI | **300,3 s** | 4 (0,3 / 75,4 / 150,4 / 225,4 s) | `AggregateException` "Retry failed after 4 tries" ⊃ `SocketException` |
| embedding OpenAI | **300,0 s** | 4 | idem |
| chat Gemini | **75,5 s** | 1 | `HttpRequestException` ⊃ `SocketException` |
| MCP | **60,1 s** | 2 (sonda `server/discover` + `initialize`) | `TimeoutException` "Initialization timed out" |
| chat Anthropic | **30,3 s** | 1 | `TaskCanceledException` (timeout do próprio SDK) |
| push | 5,1 s | 1 | engolida pelo sender (degradação graciosa) |

Os dois `RequestStart` do Anthropic no mesmo instante são uma tentativa só: o
listener mudo da rodada seguinte aceitou **uma** conexão TCP. O evento sai duas
vezes porque o SDK envia por um `HttpMessageInvoker` próprio que embrulha o
`HttpClient`.

Em Linux, onde produção roda, o SYN desiste mais tarde (~127 s com o
`tcp_syn_retries` default, **lido, não medido**). O limite passa então a ser o
`NetworkTimeout`/`HttpClient.Timeout` de 100 s: 4 × 100 s no OpenAI, 100 s no
Gemini.

**De onde vêm os 30 s do Anthropic** (decompilado, confirmado pela medição): o
worker chama sem streaming (`AgentExecutionService.cs:462`, `RunAsync`), e
`AsIChatClient` não define `MaxOutputTokens`. O SDK usa 1024 e calcula o timeout
por tentativa como `clamp(30·1024/1000, 30, 600)` = 30 s. Cancelamento por timeout
**não** é retentado: `ShouldRetry` aceita só `IOException`/`AnthropicIOException`.

### Duas propriedades do `ConnectTimeout` que decidem o desenho

Medidas com um `SocketsHttpHandler { ConnectTimeout = 3 s }` (`repro-tls-*.txt`):

1. **Cobre o TLS.** Um listener em loopback que aceita o TCP e nunca responde ao
   ClientHello é cortado em **3,07 s**; o SYN pendurado, em 3,01 s. É o que permite
   guardas sem depender da rede da máquina (D5).
2. **Lança `TaskCanceledException`** (⊃ `TimeoutException` "A connection could not
   be established within the configured ConnectTimeout"), **não**
   `HttpRequestException`. É o que torna D4 necessário.

### Simulação da correção

Os mesmos SDKs, com o handler injetado pelos pontos da tabela, contra
`10.255.255.1` (`repro-fixed-*.txt`):

| caminho | C = 5 s | C = 10 s | tentativas |
|---|---|---|---|
| OpenAI | **20,2 s** | 40,1 s | 4: a retentativa do `System.ClientModel` repete a falha de conexão |
| Anthropic | **5,2 s** | 10,1 s | 1: o SDK não repete `TaskCanceledException` |
| Gemini | **5,3 s** | 10,3 s | 1 |
| MCP | **15,1 s** | 20,1 s | sonda + initialize; a tentativa de conexão pendente da sonda sobrevive ao cancelamento dela |

O embedding não foi simulado à parte. Usa o mesmo transporte e a mesma política
de retentativa do chat OpenAI, então vale o número do OpenAI, **por leitura**. O
guarda de embedding mede.

## Goals / Non-Goals

**Goals:**

- Toda chamada de saída que pode segurar o lock de contexto desiste de conectar
  em 5 s por tentativa.
- A falha por timeout de conexão segue o caminho de falha que cada chamada já tem.
- Os guardas exercitam o registro e os handlers de produção, não cópias.

**Non-Goals:**

- Timeout de **resposta** depois de conectar (#134).
- Push notification (D6) e a reconciliação de `PendingDispatch` (#47).
- Mudar retentativas de SDK. Elas entram no cálculo do valor, mas não são
  alteradas.
- `PooledConnectionLifetime` nos handlers de LLM: hoje é infinito nos três, e
  continua. O efeito sobre troca de DNS já existe e não muda aqui.
- A investigação do lock: a corrida está na #132, e as esperas de 83,6 s e
  103,6 s vão para a issue `tipo: investigação`.
- **Fazer a chamada funcionar no caso IPv6 de 22/09.** Com IPv6 com rota e sem
  conectividade, a tentativa por IPv6 consome os 5 s e falha, e o .NET não cai
  para IPv4 dentro do prazo. A chamada continua falhando, só mais rápido. A
  correção impede a **cascata no lock** (a mensagem seguinte deixa de morrer
  esperando), não o erro da chamada. O contorno `DOTNET_SYSTEM_NET_DISABLEIPV6=1`
  continua necessário onde ele existe hoje.

## Decisions

### D1 — 5 s, constante, com o motivo ao lado

**Escolha:** `ConnectTimeout = 5 s` em todos os caminhos cobertos, numa constante
com comentário. Sem opção de configuração.

**Por quê:**

- **O lock limita por cima.** A mensagem seguinte desiste do lock em 30 s
  (`CommandTimeout`, `ConversationContextLock.cs:29-45`). O OpenAI repete a falha
  de conexão 4 vezes, então o tempo segurando é ~4C. **C = 10 s deu 40,1 s**
  (medido): a próxima mensagem continuaria morrendo. **C = 5 s deu 20,2 s.**
- **A conexão legítima limita por baixo.** O prazo cobre TCP **e** TLS (medido). O
  RTO inicial do SYN é 1 s, com retransmissões em ~1 s e ~3 s (RFC 6298 e o
  default do Linux, **lidos**). 5 s tolera dois SYNs perdidos mais um handshake TLS
  de ~2 s. Não há dado de tempo de conexão em `provider_calls`/`embedding_calls`
  (só `DurationMs` total), então este lado é argumento, não medição. Ver Risks.
- **Constante, não configuração (convenção 2):** não há cenário de alguém
  precisar de outro valor. O único candidato seria um gateway lento, e esse caso
  pede investigar o gateway, não afrouxar o timeout que protege o lock.

**Tempo segurando o lock por chamada inalcançável, com C = 5 s.** O limite é
**por chamada**, não por execução: uma execução com duas ou mais chamadas
inalcançáveis soma os tempos (ver Risks).

| caminho | antes, macOS (medido) | antes, Linux (lido) | depois (simulado) |
|---|---|---|---|
| chat OpenAI | 300,3 s | ~400 s (4 × 100 s) | **20,2 s** |
| embedding OpenAI (tool de conhecimento) | 300,0 s | ~400 s | **~20 s** (por leitura, mesmo transporte) |
| chat Anthropic | 30,3 s | 30 s | **5,2 s** |
| chat Gemini | 75,5 s | 100 s | **5,3 s** |
| MCP | 60,1 s | 60 s | **15,1 s** |

**Alternativas:**

- **10 s:** recusada pela medição (40,1 s no OpenAI).
- **5 s com menos retentativas no `System.ClientModel`:** daria folga para subir C,
  mas muda o comportamento de retentativa contra falhas transitórias legítimas.
  Fora do escopo e sem medição que a sustente.
- **Configurável:** recusada pela convenção 2.

### D2 — Um handler por SDK, por processo

**Escolha:** um arquivo novo, `apps/workers/src/Buteco.Workers/Http/OutboundConnectTimeout.cs`,
com a constante e três handlers estáticos, um por SDK, cada um preservando o que o
default daquele SDK configurava:

- **OpenAI:** um `HttpClientPipelineTransport` estático sobre um `HttpClient`
  estático, com `SocketsHttpHandler { ConnectTimeout, AllowAutoRedirect = false }`
  e `Timeout` infinito. Chat **e** embedding passam
  `OpenAIClientOptions.Transport = OutboundConnectTimeout.OpenAiTransport`;
- **Anthropic:** um `SocketsHttpHandler { ConnectTimeout, AutomaticDecompression = DecompressionMethods.GZip }`
  estático. É o valor que o `Available` do SDK resolve no .NET 10. O `None` do
  fallback só ocorre num runtime sem gzip, que este processo não usa. Cada `AnthropicClient` recebe `new HttpClient(handler, disposeHandler: false) { Timeout = Infinite }`;
- **Gemini:** um `SocketsHttpHandler { ConnectTimeout }` estático.
  `HttpClientFactory = () => new HttpClient(handler, disposeHandler: false)`, com o
  `Timeout` default de 100 s preservado.

**Por quê:**

- **Nunca handler por execução:** foi o vazamento de ~44 descritores por mensagem
  que obrigou o cache de `ChatClientResolver`. Handler estático é um pool por SDK
  por processo, menos que hoje no Anthropic e no Gemini (um por par
  `(provider, model)`).
- **O embedding constrói `OpenAIClient` por chamada**
  (`EmbeddingGeneratorResolver.cs:44-68`, `KnowledgeToolSetResolver.cs:135-143`), e
  isso só é seguro porque o transporte é um por processo. O transporte estático
  novo **mantém** essa propriedade. Os três comentários que citam
  `HttpClientPipelineTransport.Shared` como a garantia são reescritos para citar o
  transporte novo. Senão eles passam a afirmar algo falso (convenção 6).
- **`disposeHandler: false`:** `AnthropicClient` descarta o próprio `HttpClient`
  (`ClientOptions.DisposeHttpResources`, decompilado). Com o handler compartilhado,
  um descarte não pode derrubar os outros clientes. Hoje o cache nunca descarta,
  mas a proteção custa um argumento.
- **Preservar o default de cada SDK:** a mudança é o timeout de conexão, não a
  política de redirecionamento ou de descompressão.

**Alternativas:**

- **Um handler só para os três SDKs:** recusada, porque os defaults divergem
  (redirecionamento, descompressão), e unificar mudaria comportamento sem motivo.
- **`IHttpClientFactory` para os SDKs:** o `System.ClientModel` precisa de um
  transporte construído uma vez, e o resolvedor de embedding constrói cliente por
  chamada. Passar a fábrica por esses caminhos é mais fiação para o mesmo efeito.

### D3 — O registro do MCP vira um método, chamado pelo `Program.cs` e pelo guarda

**Escolha:** `McpTransportFactory.AddHttpClient(IServiceCollection)`, estático, com
o registro que hoje está em `Program.cs:54-58` mais o `ConnectTimeout`. O
`Program.cs` passa a chamá-lo. O guarda do MCP também.

**Por quê:** os testes atuais **copiam** o registro (`McpToolExecutionEndToEndTests.cs:253`
monta o próprio handler). Um guarda escrito do mesmo jeito testaria a cópia, e o
`Program.cs` poderia perder o timeout com o guarda verde. Com dois consumidores
reais, a convenção 2 não se opõe.

**O que não muda:** as cópias do registro do push nos testes de host. O push não
é tocado (D6), e trocar aquelas cópias é refatoração de suíte sem relação com o
defeito.

### D4 — Os dois `catch` do `McpToolSetResolver` separam parada de timeout

**Escolha:** `McpToolSetResolver.cs:78` e `:114` trocam
`when (exception is not OperationCanceledException)` por
`when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)`,
a mesma regra de `AgentExecutionService.IsShutdownCancellation`
(`AgentExecutionService.cs:530-531`).

**Por quê, medido:** com o timeout de conexão, a exceção que sai do
`McpClient.CreateAsync` é `TaskCanceledException` (simulação, `repro-fixed-*.txt`).
O filtro atual a deixa escapar: o servidor inalcançável derrubaria a **task**, em
vez de ser excluído do conjunto. É regressão do requisito "Degradação por servidor
MCP inacessível" de `mcp-tool-execution`, introduzida pela própria correção. O
`:114` (`tools/list`) entra porque, com `PooledConnectionLifetime` de 30 s, ele
também pode abrir conexão nova.

**Os outros filtros conferidos, e por que ficam:**

- `KnowledgeToolSetResolver.cs:199` e `KnowledgeIndexingService.cs:155`: o
  embedding OpenAI chega como `AggregateException` do `ClientRetryPolicy` (medido
  na reprodução e na simulação do chat, mesmo pipeline). O guarda de embedding
  afirma esse tipo, para que a dependência fique presa;
- `AgentExecutionService.cs:286` e `:492`: já usam `IsShutdownCancellation`, e
  Anthropic e Gemini, que chegam como `TaskCanceledException`, caem no `failed`
  como hoje.

### D5 — Guardas com listener mudo em loopback

**Escolha:** um apoio de teste que abre um `TcpListener` em loopback, porta
efêmera, aceita o TCP e nunca escreve. Os clientes apontam para
`https://127.0.0.1:<porta>`. Pela propriedade 1, isso exercita o `ConnectTimeout`
sem depender da rede.

| guarda | onde | afirma | reprova hoje porque |
|---|---|---|---|
| chat OpenAI | classe nova `OutboundConnectTimeoutTests` | falha em < 30 s com `TimeoutException` de `ConnectTimeout` na cadeia | espera 4 × 100 s |
| embedding OpenAI | idem | idem, e a exceção do topo **não** é `OperationCanceledException` (D4) | espera 4 × 100 s |
| chat Anthropic | idem, collection sem paralelismo | falha em < 10 s com `ConnectTimeout` na cadeia | espera 30 s |
| chat Gemini | idem, collection sem paralelismo | falha em < 10 s com `ConnectTimeout` na cadeia | espera 100 s |
| MCP | `McpToolSetResolverTests`, registro de produção (D3) | servidor excluído, nenhuma exceção, em < 30 s | espera 60 s; e, com o timeout sem D4, lança |

- **Anthropic e Gemini** só aceitam endpoint por variável de ambiente do processo
  (`ANTHROPIC_BASE_URL`, `GOOGLE_GEMINI_BASE_URL`), porque as `Options` não têm
  `BaseUrl`, e acrescentar uma só para teste seria configuração sem cenário. Daí a
  collection com `DisableParallelization`, que restaura as variáveis no fim.
- **Quando cada SDK lê a variável, e por que o cliente medido nasce no guarda e
  morre com ele** (conferido no código):
  - **o cache do `ChatClientResolver` é por instância:** `_clients` é
    `private readonly Dictionary` (`ChatClientResolver.cs:58`), sem nada
    `static` na classe. Cada guarda constrói o **seu** resolvedor. O cliente
    apontado para o listener vive só nesse cache e vai embora com o resolvedor no
    fim do teste. Nenhum outro teste o alcança;
  - **Anthropic lê na primeira requisição, uma vez por cliente:**
    `ClientOptions._baseUrl` é um `Lazy<string>` sobre `ANTHROPIC_BASE_URL`
    (`Anthropic.Core/ClientOptions.cs:452`), avaliado no primeiro acesso a
    `BaseUrl`, que acontece ao montar a primeira requisição. O guarda define a
    variável **antes** do `Resolve` e só a restaura **depois** de a chamada
    terminar, porque restaurar entre a construção e a requisição faria o cliente ir
    à API real;
  - **Gemini lê na construção:** `Client` chama `inferBaseUrl` no construtor
    (`Google.GenAI/Client.cs:110-131`). O campo estático `geminiBaseUrl` só muda por
    `Client.setDefaultBaseUrl`, que nem o worker nem o guarda chamam. A variável
    precisa estar definida no `Resolve`;
  - **sem paralelismo, a janela não vaza:** enquanto a variável aponta para o
    listener, nenhum outro teste roda e constrói cliente Anthropic ou Gemini. Os
    que constroem (`ChatClientResolverTests`) não fazem requisição, então nem um
    `Lazy` avaliado depois os levaria ao listener. A restauração fica num
    `finally`.
- **As duas pernas (convenção 15):** cada guarda é rodado contra o código atual
  (vermelho) e com a correção (verde). O MCP tem uma terceira perna: timeout
  **sem** D4, que tem de reprovar por exceção, não por tempo.
- **Divergência da implementação (convenção 9):** os limites dos guardas de OpenAI
  e MCP nasceram em 25 s e 20 s, colados no medido (20,3 s e 15,7 s). Numa rodada
  com a VM disputada pela linha principal, o de embedding reprovou aos 25,6 s com a
  correção presente. Passaram a **30 s, o limite do requisito** (o lock), que é o
  que o guarda protege. A `TimeoutException` do `ConnectTimeout` na cadeia continua
  sendo o que prova a causa. As duas pernas foram refeitas com os limites novos.
- **Custo:** os dois guardas OpenAI levam ~20 s cada no verde. Rodam em paralelo
  com a suíte. Os de Anthropic e Gemini, sem paralelismo, ~10 s no total.

### D6 — Push notification não é tocado

O `HttpClient.Timeout` de 5 s já limita a conexão (5,1 s medido), e um
`ConnectTimeout` de 5 s não muda nada. O brief permite tocar só o de conexão, e
aqui ele seria inerte. A retentativa e a reconciliação são da #47.

### Árvore

```
apps/workers/
  src/Buteco.Workers/
    Http/
      OutboundConnectTimeout.cs            (novo: constante + 3 handlers, D1/D2)
    Agents/ChatClientResolver.cs           (injeta os handlers nos 3 SDKs)
    Knowledge/Embedding/EmbeddingGeneratorResolver.cs  (Transport; comentário)
    Knowledge/Execution/KnowledgeToolSetResolver.cs    (só comentário)
    EmbeddingMetrics/MeasuredEmbeddingGenerator.cs     (só comentário)
    Mcp/McpTransportFactory.cs             (AddHttpClient, D3)
    Mcp/McpToolSetResolver.cs              (filtros, D4)
    Program.cs                             (chama McpTransportFactory.AddHttpClient)
  tests/Buteco.Workers.Tests/
    Http/OutboundConnectTimeoutTests.cs    (novo: OpenAI, embedding, Anthropic, Gemini)
    Support/SilentTlsListener.cs           (novo)
    Mcp/McpToolSetResolverTests.cs         (guarda do MCP)
```

Nada em `libs/`.

## Risks / Trade-offs

- **[Conexão legítima mais lenta que 5 s passa a falhar]** → Não há dado de tempo
  de conexão em produção. O endpoint OpenAI de produção vem de `OPENAI_BASE_URL`
  (`docker-compose.prod.yml:98`, `:155`), e a documentação se refere a ele como
  gateway (`docs/configuration.md:152`). A latência de conexão dele não foi
  medida. **Contraparte verificável (convenção 10):**
  o erro de timeout de conexão é distinguível no log (`TimeoutException` "A
  connection could not be established within the configured ConnectTimeout").
  Uma ocorrência com o provedor saudável é o gatilho para revisar o valor.
- **[Execução com duas ou mais chamadas inalcançáveis passa dos 30 s]** → O limite
  do D1 é por chamada. Qualquer execução que encontre duas chamadas inalcançáveis
  soma os tempos e passa dos 30 s do lock. Exemplos: o gateway cai no meio da
  execução, e o embedding da tool de conhecimento (~20 s) mais o chat seguinte
  (~20 s) dão ~40 s; ou um MCP inalcançável na resolução (15,1 s) mais o chat
  OpenAI (20,2 s) dão ~35 s. Antes, uma única chamada já passava de 60 s, e duas
  passavam de 300 s. Fica registrado, sem correção nesta change: limitar a
  execução inteira exigiria um prazo por execução ou mexer na retentativa (D1,
  alternativas).
- **[Comentários que afirmam "um `HttpClient` por processo" ficam falsos se o
  transporte deixar de ser estático]** → Reescritos (D2), e o comentário do
  `OutboundConnectTimeout` diz quem depende do estático.
- **[O `Program.cs` volta a registrar o MCP à mão e o guarda não vê]** → O guarda
  prende o método, não a chamada no `Program.cs`. Mitigação: o registro inline
  some do `Program.cs`, e a chamada fica sozinha. Risco aceito.
- **[Variáveis de ambiente nos guardas de Anthropic e Gemini]** → Collection sem
  paralelismo, com restauração. Nenhum outro teste faz chamada de rede real a
  esses SDKs (`ChatClientResolverTests` só constrói).

## Migration Plan

Sem migração de dados nem de configuração. Rollback é reverter o commit.

## Open Questions

Nenhuma de produto. O valor tem gatilho de revisão (Risks).
