## Context

`apps/workers` degrada com o tempo de processo: ~44 descritores de arquivo
vazados por mensagem, sem retorno, até o worker parar de responder. A cadeia
causal está no `proposal.md`. Este documento registra **as conferências feitas
antes de decidir** (convenção 6) e depois as decisões que elas sustentam.

**Classificação da change (convenção 18):** entrega **código**. Não há decisão
de produto pendente; o único ponto que parecia decisão — rotação de chave — foi
resolvido pela conferência, e o resultado está na Decisão 5.

### Conferências, com o que cada uma mediu

Todas foram feitas contra a árvore atual e contra os assemblies realmente
resolvidos pelo projeto, não de memória.

**C1 — `IChatClient` é `IDisposable`, e o contrato pede reuso.**
`Microsoft.Extensions.AI.Abstractions` **10.8.3** (versão resolvida, conferida em
`obj/project.assets.json`; a 10.8.1 do `Directory.Packages.props` é o piso, não o
resolvido): `public interface IChatClient : IDisposable`. A documentação XML do
tipo diz, textual:

> *It is expected that all implementations of `IChatClient` support being used
> by multiple requests concurrently.*
> *Instances must not be disposed of while the instance is still in use.*

O desenho da biblioteca sempre foi reuso de instância. Construir por mensagem é
que é o desvio.

**C2 — Gemini: `new HttpClient()` por instância de client.** `Google.GenAI`
1.15.0 decompilado. **O tipo citado no relatório de origem, `BaseApiClient`, não
existe nesta versão** — o tipo real é `Google.GenAI.ApiClient`, e o mecanismo
descrito está correto nele:

```csharp
private static HttpClient CreateHttpClient(HttpOptions httpOptions, ClientOptions? clientOptions = null)
{
    HttpClient httpClient = null;
    if (clientOptions != null) { httpClient = clientOptions.HttpClientFactory?.Invoke(); }
    if (httpClient == null)
    {
        httpClient = new HttpClient();
        if (httpOptions.Timeout.HasValue) { httpClient.Timeout = ...; }
    }
    return httpClient;
}
```

`ChatClientResolver.BuildGemini` chama `new Client(apiKey:)` **sem
`ClientOptions`**, então `HttpClientFactory` é nulo e cada client ganha um
`HttpClient` próprio, criado sob demanda e guardado em campo. `ApiClient` é
`IDisposable`/`IAsyncDisposable` e só descarta esse `HttpClient` no `Dispose`.
Também é aqui que **os 100 s do stack trace se explicam**: `httpOptions.Timeout`
é nulo, nenhum `Timeout` é atribuído, e vale o default do `HttpClient`.

**C3 — Anthropic: mesmo efeito, por outro caminho.** `Anthropic` 12.39.0. O
construtor padrão de `Anthropic.Core.ClientOptions` faz
`new HttpClient(new HttpClientHandler { AutomaticDecompression = ... }) { Timeout = Timeout.InfiniteTimeSpan }`.
`new AnthropicClient { ApiKey = ... }` cria um `ClientOptions` novo, logo um
`HttpClient` novo. **Correção de detalhe:** aqui o `Timeout` é infinito, não 100 s
— o SDK gerencia prazo por requisição. Os 100 s do sintoma são do caminho do
Gemini (C2), não deste.

**C4 — OpenAI não vaza, e é isso que explica a assimetria observada.**
`System.ClientModel` 1.14.0: `HttpClientPipelineTransport` tem
`private static readonly HttpClient _sharedDefaultClient` e
`public static HttpClientPipelineTransport Shared { get; }`. Um `HttpClient` por
processo. Por isso a indexação rodou o dia inteiro sem sintoma (ela é OpenAI-only,
ver C8) enquanto o chat degradava.

**C5 — A chave `(provider, model)` é suficiente, e o motivo tem que ficar
escrito junto dela.** A entidade `Agent` tem `Provider` e `Model` e **nenhum
campo de credencial**. As credenciais vêm de `ChatClientOptions`,
`AnthropicOptions` e `GeminiOptions`, ligadas por
`builder.Configuration.GetSection(...)` em `Program.cs:18-20`, e em execução real
chegam por variável de ambiente (`appsettings.json` não contém chave nenhuma).
Ou seja: **a credencial é por processo, não por agente**, e dois agentes com o
mesmo `(provider, model)` usam necessariamente a mesma credencial. No dia em que
credencial por agente existir, a chave do cache tem que passar a incluí-la — sem
este registro, isso passa despercebido.

**C6 — `IOptions<T>`, não `IOptionsMonitor<T>`.** `ChatClientResolver` recebe
`IOptions<ChatClientOptions>`, `IOptions<AnthropicOptions>` e
`IOptions<GeminiOptions>`. Uma varredura por `IOptionsMonitor` em `apps/` só
acha os dois `OperatorTokenAuthenticationHandler`, onde a assinatura é imposta
pelo framework de autenticação. Decide a Decisão 5.

**C7 — Onde o cache vive: o resolver já é singleton.**
`Program.cs:34` registra `AddSingleton<IChatClientResolver, ChatClientResolver>()`,
e o resolver injeta **apenas** `IOptions<T>`, que também é singleton. Não há
dependência escopada, então não existe aqui o defeito de DI "singleton que
depende de scoped". O cache mora dentro de `ChatClientResolver`, sem componente
novo e sem mudança de tempo de vida (convenção 2).

**C8 — `EmbeddingGeneratorResolver` tem a mesma forma e não vaza.** Constrói por
chamada e nunca descarta, exatamente como o resolver de chat — mas só suporta
`openai` (`anthropic`/`gemini` lançam por ausência de capacidade), e o caminho
OpenAI usa o transporte estático de C4. Conferido para que a simetria não vire
"correção" sem defeito.

**C9 — Nada descarta o `IChatClient` hoje, e por quê.**
`Microsoft.Agents.AI.ChatClientAgent` é `public sealed class ChatClientAgent : AIAgent`,
e `AIAgent` **não** é `IDisposable` — o `DisposeAsync` que aparece no assembly é
de máquina de estado de iterador assíncrono, não do agente.
`AgentExecutionService` também não usa `using` no `aiAgent`. Então o
compartilhamento é seguro hoje. **Mas** `DelegatingChatClient.Dispose(bool)` faz
`InnerClient.Dispose()` — descarte em cascata — o que transforma um `using`
acrescentado no futuro num defeito de segunda mensagem. É o risco R1.

**C10 — A posição do wrapper na cadeia decide o que ele mede.** O construtor de
`ChatClientAgent` faz
`ChatClient = chatClient.WithDefaultAgentMiddleware(options, services)`, e essa
extensão monta um `ChatClientBuilder` **sobre** o client recebido, acrescentando
`FunctionInvokingChatClient` (entre outros) **por fora**. Consequência: um
wrapper construído dentro do resolver fica na camada **mais interna**, e mede
cada requisição HTTP ao provedor separadamente, **sem** o tempo de execução das
tools. Essa cadeia é remontada a cada mensagem (um `ChatClientAgent` novo por
execução) e nunca descartada — mais uma razão para o wrapper de duração ser
construído **uma vez, com o client cacheado**, e não por mensagem.

**C11 — `LoggingChatClient` do próprio `Microsoft.Extensions.AI` não serve.**
Decompilado (10.6.0, resolvido transitivamente): registra
`LogInvoked`/`LogCompleted`/`LogInvocationFailed`/`LogInvocationCanceled` e, em
`Trace`, o conteúdo serializado das mensagens — **nenhum `Stopwatch`, nenhuma
medida de tempo**. Reusá-lo não entregaria o instrumento, e o modo `Trace` é
justamente o que a própria documentação do tipo diz para nunca ligar em produção.

**C12 — O caminho de erro do resolver é exercitado hoje por um teste real.**
`TaskJobConsumerTests.Consumer_WhenAgentProviderNotConfiguredInWorkerEnvironment_TaskEndsFailed`
usa o `ChatClientResolver` **real** (`BuildHostWithRealResolver`), de propósito,
com `AnthropicOptions`/`GeminiOptions` nunca configurados. Isso fixa um requisito
para o cache: **falha não pode ser memorizada** (Decisão 2).

**C13 — A propriedade antiga nunca teve guarda, e os mocks da suíte já modelavam
a propriedade nova.** Conferido antes de reescrever a docstring, para não deletar
um guarda invertido por engano. Varredura de `Assert.Same`/`Assert.NotSame`/
`ReferenceEquals` em `apps/workers/tests/`: **uma única ocorrência**, em
`AgentDelegationConcurrencyTests:105`, sobre um `Task` e sem relação com isto.
Os cinco testes de `ChatClientResolverTests` afirmam **tipo construído** (três) e
**lançamento** (dois) — nenhum afirma identidade, quantidade de construções ou
ausência de reuso. Não há guarda invertido a reescrever.

E o detalhe que vai além da ausência: **todos os `Mock<IChatClientResolver>` da
base fazem `.Setup(r => r.Resolve(...)).Returns(chatClient)`** — uma instância
fixa devolvida em toda chamada —, nos oito sítios de `apps/workers/tests/` e
também em `tests/InboxOrchestratorRoundTrip.Tests/Support/RoundTripFixture.cs:242`.
Ou seja, **a suíte inteira vinha exercitando a semântica cacheada** enquanto a
produção construía por mensagem. Não é só que a propriedade antiga não tinha
guarda: o arranjo de teste padrão da casa a contradizia, e ninguém leu isso como
divergência. É a explicação de por que o custo (um pool de conexões por mensagem)
atravessou desde `backend-multi-provedor-llm` sem ser notado.

**C14 — A afirmação falsa não são três: são quatro, e a quarta está em
`apps/api`.** `apps/api/src/Buteco.Api/McpServers/Connectivity/IMcpConnectionTester.cs:5-11`
cita o resolver de `apps/workers` como precedente: *"mesmo espírito de
`IChatClientResolver` em apps/workers — construído por chamada, sem cache entre
chamadas"*. É a forma mais cara da convenção 6: uma afirmação sobre o código de
**outro app**, escrita de memória, que decai em silêncio quando aquele app muda —
e que `scripts/check-docs.py` não alcança, porque nada ali está errado
sintaticamente. Ver Decisão 6.

## Goals / Non-Goals

**Goals:**

- Eliminar o vazamento de pool de conexões por mensagem em `apps/workers`.
- Deixar a duração de cada requisição ao provedor de LLM visível em log.
- Deixar escrito, junto do código, por que `(provider, model)` basta como chave
  e o que exigiria mudá-la.
- Corrigir as quatro afirmações hoje falsas no código sobre construção por
  chamada — três em `apps/workers`, uma em `apps/api` (C14).

**Non-Goals:**

- **Evicção, TTL ou invalidação do cache.** Não há cenário: a cardinalidade é
  limitada pelos pares `(provider, model)` realmente cadastrados, cada entrada
  custa um pool de conexões, e nada torna uma entrada obsoleta enquanto o
  processo vive. Inventar política aqui é convenção 2. Ver Decisão 3.
- **Timeout e retry da chamada ao LLM** — change própria, ver `proposal.md`.
- Qualquer mudança em `apps/api`, `apps/inbox` ou `apps/frontend`.
- Tornar `EmbeddingGeneratorResolver` cacheado (C8: não vaza).
- Métrica/telemetria estruturada (OpenTelemetry). O `Non-Goal` explícito de
  observabilidade do `docker-compose.prod.yml` continua valendo; o que entra
  aqui é uma linha de log, não um pipeline de métrica.

## Decisions

### Decisão 1 — Cachear o `IChatClient` por `(provider, model)` dentro do resolver

`ChatClientResolver` ganha um dicionário de instâncias vivas, chaveado por
`(provider, model)`, e devolve sempre a mesma instância para a mesma chave.
Nenhuma instância é descartada enquanto o processo vive — é exatamente o que o
contrato do tipo pede (C1).

A chave carrega, **no código, junto da declaração**, o motivo de C5: a credencial
é por processo, não por agente. Se um dia existir credencial por agente, a chave
muda.

**Alternativas recusadas:**

- **`ClientOptions.HttpClientFactory` (Gemini) + equivalente no Anthropic.**
  Tapa o vazamento provedor a provedor, exige achar e manter o ponto de extensão
  de cada SDK (o do Anthropic tem forma diferente da do Gemini — C2 × C3), e não
  elimina a criação de um client por mensagem, só a do transporte. Recusada por
  custo recorrente por provedor novo, contra uma correção única no ponto que os
  três compartilham.
- **Manter a construção por mensagem e acrescentar `using`.** Fecha o vazamento
  de descritor e **joga fora o pool de conexões a cada mensagem** — toda mensagem
  volta a pagar handshake TCP e TLS, e o `HttpClient` descartado ainda deixa
  sockets em `TIME_WAIT`. É a pior das três: corrige o sintoma medido e piora a
  latência que ninguém está medindo.

### Decisão 2 — Escrita sob lock, leitura livre; falha nunca é memorizada

Leitura pelo caminho rápido de um `ConcurrentDictionary`; se ausente, um lock, e
dentro dele a checagem de novo e a construção.

Duas propriedades que a forma precisa garantir, e que é fácil perder:

- **Construção única.** `GetOrAdd` com fábrica pode executar a fábrica mais de
  uma vez sob concorrência e descartar o perdedor — e o perdedor aqui é
  justamente um client com pool próprio, ou seja, um vazamento menor pela porta
  dos fundos. O lock elimina a janela.
- **Falha não fica memorizada.** A construção lança `InvalidOperationException`
  para provedor desconhecido ou credencial ausente (C12). Com o lock, a exceção
  propaga e **nada é gravado** no dicionário, então a resolução seguinte tenta de
  novo. `Lazy<IChatClient>` foi recusada exatamente por aqui: em
  `ExecutionAndPublication` ela **memoriza a exceção** e a re-lança para sempre,
  transformando uma configuração corrigida em falha permanente até reiniciar.

O custo do lock é um por mensagem, na casa dos microssegundos, contra uma chamada
de rede a um LLM. Não há trade-off real a discutir.

**Os dois motivos de recusa vão para o comentário do código, não só para este
documento.** `Lazy<T>` e `GetOrAdd` são as duas escolhas óbvias para "cache
concorrente em C#", as duas parecem mais simples que um lock explícito, e as duas
estão erradas por motivo **específico deste caso** — não por preferência de
estilo. Escritos só aqui, ficam num artefato que é arquivado; escritos no código,
ficam onde a revisão que proporia a "simplificação" acontece. O comentário nomeia
o efeito, não a preferência: `Lazy` memoriza a exceção e transformaria credencial
ausente em falha permanente do processo (com `TaskJobConsumerTests` exercitando
exatamente esse caminho — C12); `GetOrAdd` pode executar a fábrica duas vezes e
descartar o perdedor, que aqui não é um objeto barato e sim um pool de conexões —
o próprio defeito que esta change corrige, reintroduzido em escala menor.

### Decisão 3 — Dicionário que nunca desaloja, e é isso que fecha o defeito

Sem evicção, sem TTL. Não é omissão: **se houver evicção, o item evictado precisa
ser descartado**, e um `IMemoryCache` (ou qualquer coisa com evicção) que remova
a entrada sem `Dispose` faz o vazamento voltar — mais devagar, e por isso mais
caro de diagnosticar que o original. Como não existe cenário que exija evicção
(Non-Goals), a escolha é não ter, e o registro existe para que ninguém
"melhore" o cache com uma política que reabre o defeito.

### Decisão 4 — Log de duração num `DelegatingChatClient` construído junto do client cacheado

Classe nova, fina, em `apps/workers`: `LlmCallDurationChatClient : DelegatingChatClient`,
que cronometra `GetResponseAsync`/`GetStreamingResponseAsync`, registra duração
com `provider` e `model`, e **registra também no caminho de falha antes de
propagar** — uma chamada que estoura por espera de pool é precisamente o caso que
não pode ficar sem medida.

O wrapper é criado **uma vez por entrada do cache**, envolvendo o client do SDK.
Duas razões medidas:

- **Posição.** C10: como `ChatClientAgent` empilha o seu middleware por fora, o
  wrapper fica na camada mais interna e mede a requisição ao provedor, não o
  turno do agente com execução de tools dentro. É a medida que teria separado
  "esperando conexão do pool" de "esperando o modelo".
- **Ciclo de vida.** Se o wrapper fosse construído por mensagem e descartado,
  `DelegatingChatClient.Dispose()` descartaria o client cacheado em cascata (C9).
  Construído uma vez e guardado, o problema não existe.

**Alternativa recusada: cronometrar `aiAgent.RunAsync` em `AgentExecutionService`.**
Custa menos linhas e mede a coisa errada — `RunAsync` é o turno inteiro do
agente, com N requisições ao LLM e a execução das tools MCP misturadas numa
medida só. Contra este defeito, um número que sobe sem dizer qual parte subiu é
o que o diagnóstico já tinha.

### Decisão 5 — Rotação de chave de provedor exige reinício do processo, registrado com gatilho

`IOptions<T>` (C6) resolve o valor uma vez; e as credenciais chegam por variável
de ambiente, que um processo em execução não vê mudar de qualquer forma. Logo:
**a rotação já exigia reinício antes desta change** — o cache não introduz esse
custo, não o piora, e não há invalidação a inventar (convenção 2).

O que o cache acrescenta é uma **segunda** razão, que só importa no futuro: se
alguém trocar `IOptions<T>` por `IOptionsMonitor<T>` para fazer configuração
fluir a quente, o client cacheado continuará com a credencial antiga e a troca
não terá efeito. Fica escrito, com gatilho: **no dia em que
`IOptionsMonitor<T>` entrar para qualquer uma das três `Options` de provedor, o
cache tem que ser invalidado na mudança — e o client removido, descartado.**

O registro vai para `docs/configuration.md` em vez de ficar só no código, porque
"precisa reiniciar" descoberto durante uma rotação de emergência é o pior momento
possível para descobrir.

### Decisão 6 — Corrigir as quatro afirmações falsas no código, na mesma passada

A construção por chamada está afirmada em **quatro** lugares que esta change
torna mentira — três em `apps/workers` e um em `apps/api` (C14):

- `IChatClientResolver.cs:6-9` — *"construído por chamada (sem cache entre
  execuções…)"*;
- `ChatClientResolver.cs:12-20` — *"Fábrica de `IChatClient` por provedor"*;
- `Program.cs:73-75` — *"`IChatClientResolver` constrói o `IChatClient` por
  chamada (sem cache entre execuções), então ser singleton aqui não implica
  reaproveitar nenhuma instância"* — esta é a mais perigosa, porque o `então`
  vira um raciocínio inválido sobre DI.

- `apps/api/.../McpServers/Connectivity/IMcpConnectionTester.cs:5-11` — *"mesmo
  espírito de `IChatClientResolver` em apps/workers — construído por chamada, sem
  cache entre chamadas"*.

**A quarta é corrigida, e a correção não é atualizar a citação — é removê-la.**
Trocar "sem cache" por "com cache por `(provider, model)`" deixaria em `apps/api`
uma afirmação sobre o ciclo de vida de um componente de `apps/workers`, que é
precisamente a forma que decaiu. A metade útil da docstring (*"Interface existe
para permitir substituição em teste"*) é verdadeira sobre o próprio
`IMcpConnectionTester` e fica; a citação cruzada sai.

**Isto amplia o escopo declarado no `proposal.md`, e a ampliação é nomeada:**
esta change passa a tocar um arquivo de `apps/api`. A edição é de **comentário,
zero linhas de comportamento, zero `ProjectReference`** — não cria nem sugere
acoplamento entre apps, e o isolamento estrito continua intacto. A alternativa
seria deixar na árvore uma frase que esta change sabe ser falsa, que é o defeito
que a tarefa de documentação por artefato existe para impedir.

O `design.md` arquivado da change `backend-multi-provedor-llm` **não** é editado:
change arquivada é registro histórico, e a Decision 7 de lá já nomeou o gatilho
("se perfilamento mostrar necessidade") que esta change dispara. O elo é feito
aqui, apontando para lá.

## Risks / Trade-offs

**R1 — `using` acrescentado no futuro sobre o agente ou a cadeia descarta o
client compartilhado, e toda mensagem seguinte daquele `(provider, model)` falha
com `ObjectDisposedException`.** O sintoma seria "funciona a primeira vez depois
do boot", que é caro de ler.
→ **Contraparte verificável:** cenário *"Instância reutilizada continua utilizável
depois de uma execução completa"* na delta spec, com teste que processa **duas**
tasks em sequência contra o mesmo host e afirma que a segunda chega a estado
terminal. Mais o comentário no sítio de resolução (`AgentExecutionService:171`)
dizendo que a instância é compartilhada e não deve ser descartada — ao lado do
`await using` do `toolSet` em `:178`, que é o que hoje sugere a simetria errada.

**R2 — Concorrência real sobre uma instância compartilhada.** Duas mensagens do
mesmo `(provider, model)` passam a usar o mesmo `IChatClient` ao mesmo tempo; e o
`CompactionProvider` (`AgentExecutionService:234-236`) usa deliberadamente o
**mesmo** client para a chamada de resumo dentro da mesma execução.
→ **Contraparte:** C1 — a documentação do tipo declara que implementações são
esperadas suportar uso concorrente por múltiplas requisições. É garantia do
contrato da biblioteca, verificada na versão resolvida, não suposição. O que o
mesmo parágrafo ressalva é mutação de **argumentos** compartilhados (`ChatOptions`),
e aqui cada execução monta o seu `ChatOptions` novo dentro de
`ChatClientAgentOptions`, por mensagem — nada é compartilhado entre execuções
além do client.

**R3 — Falha memorizada.** Um cache implementado com `Lazy<T>` ou
`GetOrAdd` sobre fábrica que lança transformaria "credencial faltando" em falha
permanente, ou gravaria entrada inválida.
→ **Contraparte:** cenário *"Provider sem credencial configurada volta a falhar em
toda resolução"*, que resolve **duas vezes** e afirma as duas falhas. O teste
existente (`Resolve_ProviderWithoutApiKeyConfigured_Throws`) resolve **uma** vez e
passaria com o defeito presente.

**R4 — Uma entrada de cache retém um pool de conexões até o processo morrer.**
É a troca deliberada: N pools fixos (N = pares realmente usados) em vez de um por
mensagem. Com os provedores atuais e um catálogo curado de modelos, N é pequeno.
→ **Contraparte:** não é testável como cenário — é propriedade de
dimensionamento. Fica registrada aqui, e o log de duração (Decisão 4) é o que
tornaria visível qualquer degradação por saturação de um pool compartilhado, que
é o modo de falha oposto ao corrigido.

**R5 — O guarda de identidade pode passar verde no componente errado.** Um teste
de execução de agente que afirme reuso estaria afirmando no lugar errado: quase
todos os testes de `AgentExecutionService` usam `Mock<IChatClientResolver>` e são
cegos a isto por construção.
→ **Contraparte:** os guardas de identidade e de distinção ficam em
`ChatClientResolverTests`, que exercita o componente que a correção toca; e a
tarefa de reintrodução do defeito (convenção 15) verifica explicitamente **quais**
testes reprovam, exigindo que nenhum teste de execução de agente reprove junto.

## Migration Plan

Não há migração: sem mudança de schema, de contrato de fio, de configuração ou de
API. O efeito aparece ao reiniciar o processo de `apps/workers`.

**Rollback:** reverter o commit. Não há estado persistido novo, nada a desfazer.

**Verificação em desenvolvimento, e ela é o motivo da change:** contar descritores
do processo (`lsof -p <pid> | wc -l`) antes e depois de processar uma sequência de
mensagens com um agente Gemini ou Anthropic. O baseline medido é ~44 por mensagem,
sem retorno; depois da correção a contagem tem que estabilizar. **Isto vale para o
par que vaza (Gemini/Anthropic) — com OpenAI a medição não mostra nada nem antes
nem depois (C4), e rodá-la só com OpenAI produziria um "corrigido" vazio.**

## Open Questions

Nenhuma. As duas que a proposta trazia em aberto foram fechadas por conferência:
onde o cache vive (C7 — o resolver, que já é singleton) e o que fazer sobre
rotação de chave (C6 → Decisão 5). Timeout e retry não são pergunta aberta desta
change: são escopo de outra, declarado no `proposal.md`.

## Tamanho projetado por componente

Projetado **depois** de as conferências fecharem (convenção 18), contando
**criados e modificados separadamente**, e os modificados **em pares**
(produção + o teste dele).

**Criados — 2 arquivos / ~150 linhas**

| Arquivo (`apps/workers`) | Linhas |
| --- | --- |
| `src/.../Agents/LlmCallDurationChatClient.cs` | ~55 |
| `tests/.../Agents/LlmCallDurationChatClientTests.cs` | ~95 |

O teste do wrapper precisa de **arranjo próprio** (um `IChatClient` falso que
atrasa e um que lança), e pela régua refinada da convenção 18 cenário com arranjo
próprio custa 25-40 linhas, não 19-21 — daí as ~95 para quatro cenários.

**Modificados — 6 arquivos / ~185 linhas**

| Arquivo (`apps/workers`) | Linhas | O que domina |
| --- | --- | --- |
| `src/.../Agents/ChatClientResolver.cs` | ~+40/-6 | cache, lock, composição do wrapper |
| `src/.../Agents/IChatClientResolver.cs` | ~+10/-5 | docstring falsa (Decisão 6) |
| `src/.../Program.cs` | ~+7/-4 | comentário falso em `:73-75` |
| `src/.../Agents/AgentExecutionService.cs` | ~+8 | só comentário no sítio `:171` (R1) |
| `tests/.../Agents/ChatClientResolverTests.cs` | ~+85 | 4 guardas novos |
| `tests/.../TaskJobConsumerTests.cs` | ~+45 | guarda de duas tasks em sequência (R1) |

E um em `apps/api`, achado por C14 **depois** da primeira redação desta projeção:

| Arquivo (`apps/api`) | Linhas | O que domina |
| --- | --- | --- |
| `src/.../McpServers/Connectivity/IMcpConnectionTester.cs` | ~+3/-3 | citação cruzada falsa (Decisão 6) |

**Total projetado: 9 arquivos / ~340 linhas de código.**

Registrando a direção pela convenção 18: a projeção **subiu** entre a primeira
redação e esta, e a causa é estrutural, não um fator a somar em projeções
futuras. O componente novo não apareceu por mudança de escopo — apareceu porque a
varredura de afirmações falsas foi feita **no repositório inteiro** e não só no
app que a change corrige. A régua reutilizável: *quando uma change invalida uma
propriedade documentada, o blast radius da documentação não respeita a fronteira
de app, porque a citação cruzada em comentário não deixa rastro que o compilador
ou `check-docs.py` enxerguem.*

A âncora é **decomposta**, nunca o headline de commit: esta change não cria
operação CQRS nenhuma e não cria migração, então os dois maiores distorcedores
já medidos nesta base (contagem dirigida por operação CQRS; `.Designer.cs`
carregando o snapshot inteiro do modelo) não se aplicam. O custo aqui é dominado
por **teste** — ~225 das ~335 linhas —, que é o perfil esperado de uma correção
de defeito com guarda, e não por produção.

Os artefatos OpenSpec desta change (`proposal.md`, `design.md`, `specs/`,
`tasks.md`) **não** entram nesse número, pela mesma convenção.

### Medido na implementação (convenção 9 — corrigido aqui, não só no resumo)

**Três divergências entre este desenho e o que a implementação exigiu.** Estão
corrigidas acima? Não — ficam registradas aqui, porque nenhuma muda uma decisão;
todas acrescentam componente que o desenho não previu:

1. **`ChatClientResolver` ganhou um quarto parâmetro, `ILoggerFactory`.** O
   desenho descrevia o wrapper composto dentro do resolver (Decisão 4) sem notar
   que o resolver passaria a precisar fabricar o logger dele. Mudança de
   assinatura de construtor, que arrastou o helper `CreateResolver` dos testes.

2. **`LlmCallDurationChatClient` precisou expor `InnerChatClient` público.**
   `DelegatingChatClient.InnerClient` é `protected`, e os três testes existentes
   de "qual tipo de client é construído para qual provedor" liam
   `chatClient.GetType().Name` — que depois do wrapper devolve a mesma resposta
   para os três provedores. Sem o acessor, a asserção teria que ser abandonada
   ou trocada por outra coisa. O desenho tratou o wrapper como transparente para
   os testes existentes, e ele não é.

3. **O guarda de R1 exigiu um arquivo novo, `DisposalTrackingChatClient.cs`.**
   A projeção contou esse guarda como ~45 linhas dentro de `TaskJobConsumerTests`.
   Na implementação ficou claro que um `Mock<IChatClient>` **não serve**: o
   `Dispose()` de um mock é inócuo, então o guarda passaria verde com o descarte
   em cascata presente — exatamente a primeira forma da convenção 15. Foi
   preciso um falso real que registre o descarte e passe a lançar depois dele.
   Componente descoberto na implementação, não na verificação.

**Tamanho real, decomposto — e a projeção acertou na metade que importa.**

| | Projetado | Real |
| --- | --- | --- |
| Criados (código) | 2 / ~150 | **3 / 303** |
| Modificados (código), linhas `+` | 7 / ~191 | **7 / 316** |
| Documentação (`docs/`, `CHANGELOG`) | não projetada | 3 / 57 |
| **Total de código** | **9 / ~340** | **10 / 619** |

Ler isso como "a projeção errou 1,8x" produziria um fator de correção inventado
— o erro exato que esta convenção existe para não repetir. Decomposto por
**natureza da linha**, medido:

| | Comentário | Código | Vazia |
| --- | --- | --- | --- |
| Modificados (linhas `+`) | **187 (59%)** | 103 (33%) | 26 |
| Criados | 80 (26%) | 185 (61%) | 38 |
| **Total** | **267 (43%)** | **288 (47%)** | 64 |

**As 288 linhas de código real ficam abaixo das ~340 projetadas.** A projeção de
*código* acertou; o que ela não contou foi **comentário**, e em change desta
natureza o comentário domina os modificados com 59%.

**A causa é estrutural e reutilizável, não um fator a somar.** Nesta change o
comentário **é parte do entregável**, não ornamento: metade das tarefas do
`tasks.md` (3.1b, 3.2, 3.3, 6.1-6.5) pedem explicitamente que uma razão fique
escrita no ponto onde a decisão vive, porque o `design.md` é arquivado e o
comentário é onde a revisão que desfaria a decisão acontece. A régua para a
próxima projeção: **numa correção de defeito cujo entregável inclui a razão
registrada no sítio da decisão, projetar comentário separado de código nos
arquivos modificados** — a razão ocupa ~1,8x o código que ela protege. Uma
change que só acrescenta funcionalidade não tem esse perfil.

E um gatilho menor, também medido: **a projeção não contou linhas de
documentação** (`docs/`, `CHANGELOG`), embora o `tasks.md` tivesse uma tarefa por
artefato. Foram 57 linhas em 3 arquivos — pequeno, mas sistematicamente ausente
da projeção, e portanto sistematicamente subestimado.
