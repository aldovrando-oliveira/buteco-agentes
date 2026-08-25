## Context

`AgentExecutionService.ExecuteAsync`
(`apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs:171-192`) é o
único ponto de `apps/workers` que monta o `ChatClientAgentOptions` enviado ao
LLM:

```csharp
var aiAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = agent.Name,
    ChatOptions = new ChatOptions { Instructions = agent.Instructions, Tools = toolSet.Tools.Concat(delegationTools).ToList() },
    ChatHistoryProvider = new InMemoryChatHistoryProvider(...),
    AIContextProviders = new AIContextProvider[] { new CompactionProvider(...) },
});
```

Hoje `agent.Instructions` é usado como veio do banco, sem nenhuma âncora
temporal — o modelo não tem como saber que dia é "hoje" nem se sua resposta
está chegando com atraso.

**Investigação feita em duas rodadas de `/opsx:explore` antes deste
design.md**, decompilando `Microsoft.Agents.AI` 1.15.0 e
`Microsoft.Agents.AI.Abstractions` 1.15.0 com `ilspycmd`, e testando `TZ`
empiricamente em container Linux real (`mcr.microsoft.com/dotnet/sdk:10.0`
via `podman`) — nada assumido de memória de treinamento.

**Achado 1 — ponto de montagem é único.** Confirmado por leitura completa de
`AgentExecutionService.cs`: não há segundo caminho que monte
`ChatClientAgentOptions`/`Instructions` em `apps/workers`.

**Achado 2 — os três eixos do risco "carimbo congelado" estão fechados:**
- `ChatClientAgent` é construído por task, dentro de `ExecuteAsync`
  (linha 171) — não há cache de instância entre execuções.
  `AgentExecutionService` é singleton no DI (`Program.cs:57`), mas não
  guarda nenhum `ChatClientAgent`/`Instructions` como estado.
- `CompactionProvider.InvokingCoreAsync` (decompilado,
  `CompactionProvider.cs:121-195`) opera só sobre
  `context.AIContext.Messages` — monta seu `CompactionMessageIndex`
  exclusivamente a partir de mensagens, roda a estratégia de compactação só
  sobre esse índice, e devolve `Instructions = context.AIContext.Instructions`
  (linha 190) **sem tocar no valor**. `Instructions` nunca entra na janela
  que o resumo compacta.
- A sessão persistida por `contextId` não tem como carregar `Instructions`
  de volta. Confirmado decompilando toda a cadeia:
  `AgentSession` (abstrata, `AgentSession.cs:56-67`) expõe só `StateBag`;
  `ChatClientAgentSession : AgentSession` (`ChatClientAgentSession.cs:80-85`,
  `[JsonConstructor]`) adiciona só `ConversationId` — os dois únicos campos
  que entram no JSON serializado (`SerializeSessionCoreAsync`/
  `DeserializeSessionCoreAsync`, `ChatClientAgent.cs:969-979`). Dentro do
  `StateBag`: `InMemoryChatHistoryProvider.State` guarda só `Messages`
  (`InMemoryChatHistoryProvider.cs:30-37`); `CompactionProvider.State` guarda
  só `MessageGroups`. `Instructions` vive exclusivamente em
  `ChatClientAgent._agentOptions.ChatOptions.Instructions`
  (`ChatClientAgent.cs:695-711`), clonado uma vez no construtor
  (`_agentOptions = options?.Clone()`, linha 801) — **é separação
  arquitetural do framework entre sessão (histórico + estado de providers) e
  configuração do agente (Instructions, tools, nome), não omissão da versão
  atual do pacote.**

  O teste que prova isso empiricamente já existe, parcialmente:
  `ConversationHistoryTests.cs:182-229`
  (`AgentInstructionsUpdatedBetweenMessages_SecondCallUsesNewInstructions`)
  já processa duas tasks no mesmo `contextId`, atualiza
  `Agent.Instructions` entre elas, e confirma via `Callback` no
  `Mock<IChatClient>` que a segunda chamada usa o valor novo — mesmo
  harness (`WorkerInfrastructureFixture`, Testcontainers Postgres+RabbitMQ,
  dois `IHost` descartados entre si) que esta change estende para o
  carimbo temporal (ver Decisão 7).

**Achado 3 — delegação reaproveita o mesmo ponto de graça.** A task
delegada é publicada na mesma fila que o `TaskJobConsumer` já consome
(`AgentDelegationToolSetResolver.cs:127` →
`taskJobPublisher.PublishAsync(new TaskJobMessage(...))` →
`TaskJobConsumer.ExecuteAsync:51` → `AgentExecutionService.ExecuteAsync`),
então monta seu próprio bloco com seu próprio instante de processamento,
sem nenhuma propagação extra — a task delegada tem sua própria "hora" real,
o que é correto para esta etapa.

**Achado 4 — `TimeProvider` cobre relógio e fuso**, confirmado decompilando
`Microsoft.Extensions.TimeProvider.Testing` 9.4.0 (versão arbitrária usada
só para inspecionar se a API existia nesta primeira rodada — a versão
efetivamente referenciada pela change, `10.9.0`, foi decompilada à parte e
confirmada em separado; ver Decisão 8):
`FakeTimeProvider._localTimeZone`/`LocalTimeZone`/`SetLocalTimeZone(TimeZoneInfo)`
(`FakeTimeProvider.cs:19,51,180`) — permite fixar relógio E fuso no teste,
sem que o montador do bloco chame `DateTimeOffset.Now`/`TimeZoneInfo.Local`
diretamente. `TimeProvider` não está registrado no DI em nenhum lugar hoje
— nem em `apps/workers/Program.cs`, nem em
`ConversationHistoryTests.BuildHost`.

**Achado 5 — comportamento real de `TZ` no .NET 10, testado em container
Linux (`mcr.microsoft.com/dotnet/sdk:10.0`, nove casos):**

| `TZ` | `TimeZoneInfo.Local.Id` resolvido | Offset |
|---|---|---|
| (ausente) | `UTC` | `+00:00` |
| `America/Sao_Paulo` (válido) | `America/Sao_Paulo` | `-03:00` |
| `America/Sao_Paolo` (typo) | `UTC` — **silencioso, sem exceção** | `+00:00` |
| `bogus_garbage_value` | `UTC` — silencioso | `+00:00` |
| `` (vazio) | `UTC` | `+00:00` |
| `UTC` (deliberado) | `UTC` | `+00:00` |
| `Etc/UTC` (deliberado) | `Etc/UTC` | `+00:00` |
| `Brazil/East` (alias válido da tz database) | `Brazil/East` — **round-trip idêntico** | `-03:00` |
| `:America/Sao_Paulo` (prefixo POSIX `:`) | `America/Sao_Paulo` — **`:` removido do `Id` resolvido** | `-03:00` |

`TimeProvider.System.LocalTimeZone.Id` espelhou `TimeZoneInfo.Local.Id` nos
nove casos. Checar só a presença de `TZ` deixaria passar um valor
presente-porém-inválido, que cai em UTC do mesmo jeito silencioso que a
ausência — ver Decisão 4.

Os dois últimos casos foram acrescentados depois de revisão desta proposta,
por dois motivos diferentes: `Brazil/East` prova, com um alias real (é o
fuso ambiente desta própria máquina de desenvolvimento, achado ao rodar a
sonda também fora de container — ver Achado 7), que nem todo alias quebra a
comparação estrita; `:America/Sao_Paulo` prova o caso que de fato quebra —
a sintaxe POSIX de prefixo `:` resolve o fuso corretamente, mas o `.NET`
descarta o `:` ao montar o `Id`, então o valor declarado (`:America/Sao_Paulo`)
nunca bate com o valor resolvido (`America/Sao_Paulo`), mesmo com a
configuração correta. Ver Decisão 4.

**Achado 6 — não existe nenhuma checagem de startup em `apps/workers`
hoje.** `Program.cs` (lido por completo) vai direto de `builder.Services
.Add...` para `host.Build()`/`host.Run()`. O molde a seguir é de
`apps/inbox`: `app.ValidateRouteAuthenticationClassification(...)`
(`apps/inbox/src/Buteco.Inbox/Program.cs:143`) — extensão chamada
depois de `Build()`, antes de `Run()`, `throw new
InvalidOperationException` síncrono, sem bypass. É comportamento de boot do
processo, não transição de estado de task A2A — pertence à capability
`workers-scaffold` (que já tem o requisito "Worker Service mínimo" cobrindo
"o processo sobe... registrando log de início do host"), não a
`a2a-task-lifecycle`. Ver Decisão 4 e specs/workers-scaffold/spec.md desta
change.

**Achado 7 — `TimeZoneInfo.Local`/`TimeProvider.System.LocalTimeZone` são
cacheados por processo, mas `TimeZoneInfo.ClearCachedData()` força
reavaliação real e ao vivo, testado dentro de um único processo em duas
plataformas diferentes** (container Linux via `podman`, e nativamente nesta
máquina macOS — a mesma que executa `dotnet test` localmente para este
repo): `Environment.SetEnvironmentVariable("TZ", novoValor)` sozinho não
muda nada (o cache antigo persiste); `TimeZoneInfo.ClearCachedData()` logo
em seguida força os dois a reler o ambiente e resolver o valor novo — três
trocas sucessivas dentro do mesmo processo (`America/Sao_Paulo` →
`Asia/Tokyo` → valor inválido → `UTC`) confirmadas corretas nas duas
plataformas, não é acaso de uma única leitura. `TimeProvider.System
.LocalTimeZone` não tem cache próprio independente — acompanha
`TimeZoneInfo.Local` em tempo real nos dois ambientes testados. Ver Decisão
9 (mecanismo de teste da checagem de boot).

**Achado 8 — o fallback de `TZ` ausente é dependente de plataforma, mas a
checagem de boot não depende de qual fallback ocorre.** No container Linux
puro do Achado 5, `TZ` ausente resolve para `UTC`. Rodando a mesma sonda
nativamente nesta máquina macOS, sem `TZ` definida, `TimeZoneInfo.Local.Id`
resolveu para `Brazil/East` — o fuso configurado no próprio sistema
operacional, não `UTC`. Ou seja: fora de um container Linux sem
`/etc/localtime`, "ausente" não cai necessariamente em UTC. Isso não
enfraquece a Decisão 4: a checagem compara o valor DECLARADO (`TZ`, que
fica `null` quando ausente) contra o valor RESOLVIDO — `null` nunca é igual
a nenhum `Id` de fuso válido, então a checagem falha corretamente
independentemente de qual fuso o SO usou como fallback. O que muda por
plataforma é só a causa raiz que um teste local (fora de container)
consegue reproduzir — ver Decisão 9.

**Achado 9 — varredura de `apps/workers` (produção e testes) por acesso
direto a relógio/fuso local, pedida na revisão para verificar (não só
afirmar) o invariante de isolamento da Decisão 9.** Padrões buscados:
`DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now`, `TimeZoneInfo
.Local`, `TimeZoneInfo.ConvertTime*`, `.ToLocalTime()`,
`CurrentCulture`/`CurrentUICulture`. **Resultado: não veio vazio.**

```
apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs:24:
    logger.LogInformation("Worker starting at: {time}", DateTimeOffset.Now);
apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs:70:
    logger.LogInformation("Worker stopping at: {time}", DateTimeOffset.Now);
```

Nenhuma outra ocorrência de nenhum dos padrões buscados em todo
`apps/workers` (produção e testes) — só estas duas, ambas pré-existentes a
esta change (`StartAsync`/`StopAsync` de `TaskJobConsumer`, o
`BackgroundService` que consome a fila de tasks).

`DateTimeOffset.Now` lê `TimeZoneInfo.Local` internamente. `TaskJobConsumer`
é registrado (`AddHostedService<TaskJobConsumer>()`) e seu
`StartAsync`/`StopAsync` são executados por pelo menos oito arquivos de
teste de integração já existentes (confirmado por busca):
`WorkerTests.cs`, `TaskJobConsumerTests.cs`,
`AgentDelegationConcurrencyTests.cs`, `ConversationHistoryTests.cs`,
`HistorySummarizationTests.cs`, `AgentDelegationExecutionTests.cs`,
`Mcp/McpToolExecutionEndToEndTests.cs`,
`Notifications/PushNotificationEndToEndTests.cs`. Ou seja: o invariante que
a Decisão 9 pretendia declarar ("nenhum outro teste depende de fuso real")
**não é verdadeiro hoje**, antes mesmo desta change — é um site
pré-existente, não introduzido por ela.

**O que está e o que não está provado:**
- Provado: nenhum teste hoje ASSERE sobre o valor desses dois logs — são
  `LogInformation` informativos, o texto exato do carimbo não é lido por
  nenhuma asserção encontrada. Uma leitura "errada" momentânea de
  `TimeZoneInfo.Local` nesses dois pontos não faria nenhuma asserção
  existente falhar.
- Não provado, e não investigado além do grep: se `TimeZoneInfo
  .ClearCachedData()` chamado por uma thread (o teste de boot de `TZ`,
  Seção 7 de tasks.md) enquanto outra thread está no meio de resolver
  `DateTimeOffset.Now` (`TaskJobConsumer.StartAsync`/`StopAsync` de um
  teste de integração rodando em paralelo, Seção 6 ou qualquer um dos
  outros arquivos listados acima) tem algum efeito observável além do
  valor logado — é uma pergunta sobre concorrência interna do BCL que este
  achado não responde, e não deveria ser respondida por suposição.

**Resolução (Decisão 9, Decisões A e B)**: `TaskJobConsumer` passa a
receber `TimeProvider` no construtor e usar `GetLocalNow()` no lugar das
duas chamadas de `DateTimeOffset.Now` (Tarefa 1.3) — fecha a ocorrência na
origem. Nenhum isolamento adicional de coleção do xUnit foi adotado além
disso; o raciocínio completo, incluindo a alternativa considerada e
rejeitada, está na Decisão 9.

**Achado 10 — o escopo do Achado 9 estava declarado errado em duas
citações posteriores (Decisão B, Tarefa 7.6 de tasks.md), e uma regressão
real provou isso, não uma revisão de texto.** O Achado 9 em si é preciso
sobre o próprio escopo — sua primeira linha diz "varredura de
`apps/workers`". Mas a Decisão B e a Tarefa 7.6, ao resumir esse achado,
generalizaram para "varredura de código do repo"/"varredura completa do
código do repo" — escopo que a varredura nunca teve.

A prova não foi encontrada revisando o texto: apareceu como regressão real
durante a validação estendida da Tarefa 8.2 (rodada contra o monorepo
inteiro, além do que a tarefa pedia). `tests/InboxOrchestratorRoundTrip
.Tests/Support/RoundTripFixture.cs` — um projeto de teste na raiz do repo,
fora de `apps/workers` — monta seu próprio host mínimo de `apps/workers`
(`BuildWorkersHost`, registrando `AgentExecutionService`/`TaskJobConsumer`
do mesmo jeito que os harnesses de `apps/workers/tests`) e não estava
entre os arquivos varridos pelo Achado 9. Ele falhou por falta do mesmo
registro de `TimeProvider` que a Decisão 6 já exigia — corrigido na hora,
mesmo padrão da Decisão A, registrando `TimeProvider.System` nesse host
também.

**Varredura estendida, feita depois dessa correção**, aplicando os mesmos
padrões de busca do Achado 9 (`DateTime.Now`, `DateTime.Today`,
`DateTimeOffset.Now`, `TimeZoneInfo.Local`, `TimeZoneInfo.ConvertTime*`,
`.ToLocalTime()`, `CurrentCulture`/`CurrentUICulture`) ao diretório
`tests/` na raiz do repo, que hospeda os dois projetos de teste cruzados
entre apps (`CrossAppTaskStoreCompatibility.Tests`,
`InboxOrchestratorRoundTrip.Tests`): **resultado vazio** — nenhuma
ocorrência, nem antes nem depois da correção acima (a correção foi sobre
`TimeProvider` ausente do DI, não sobre acesso direto a relógio/fuso
nesse arquivo). `CrossAppTaskStoreCompatibility.Tests` não referencia
`AgentExecutionService`/`TaskJobConsumer` em nenhum ponto — nunca esteve
em risco por este eixo.

**Correção de escopo, não de mérito**: em nenhum lugar deste documento
"varredura completa" deveria ter sido lida como "de todo o repositório" —
era, e continua sendo, "de `apps/workers`" (Achado 9) mais, agora, "de
`tests/` na raiz" (este achado). Código de produção de `apps/api` e
`apps/inbox` nunca foi varrido por este eixo e não precisa ser — nenhum
dos dois monta host de `apps/workers` nem depende do `TimeProvider`
injetado nele. As referências à Decisão B e à Tarefa 7.6 (tasks.md) foram
corrigidas para o escopo real: `apps/workers` + `tests/` na raiz, não "o
repo".

## Goals / Non-Goals

**Goals:**
- Montar um bloco de contexto temporal a cada execução de task, concatenado
  a `Instructions`, nunca persistido, nunca parte do histórico de conversa.
- Bloco com dois espaços — instante de PROCESSAMENTO (sempre presente) e
  instante da MENSAGEM (opcional, ausente nesta etapa) — e regra de
  precedência explícita em linguagem natural entre os dois.
- Dia da semana por extenso e defasagem entre os dois instantes calculados
  em código, nunca delegados ao modelo.
- `TimeProvider` como único ponto de acesso a relógio/fuso — determinismo
  total em teste via `FakeTimeProvider`.
- Checagem de boot que falha se o fuso resolvido não corresponder ao que
  `TZ` declara (não só se `TZ` está ausente).
- Cobertura de teste unitário puro para o construtor do bloco (sem
  Testcontainers), extensão do harness de integração já existente
  (`WorkerInfrastructureFixture`) para o cenário de sessão/carimbo e para a
  entrega ao `IChatClient`, e classe própria com host mínimo — sem
  reaproveitar esse harness — para o boot de `TZ` (Decisão 9).

**Non-Goals:**
- **Instante da mensagem atravessando a delegação.** Na etapa 2, sem
  propagação, o agente Source resolveria "amanhã" contra um dia e o Target
  contra outro — dois agentes na mesma conversa divergindo sobre a mesma
  palavra do cliente, a falha que esta linha existe para evitar. Fica
  registrado agora, não descoberto na etapa 2: `TaskJobMessage`
  (`apps/workers/src/Buteco.Workers/Messaging/TaskJobMessage.cs`) é um
  `record` posicional fechado (`TaskId`, `AgentId`, `ContextId`,
  `PushNotificationConfig?`), sem bag extensível — a etapa 2 vai precisar
  alterar esse contrato (seguro, por ser JSON nomeado, com precedente de
  campo opcional) e tocar `AgentDelegationToolSetResolver
  .DelegateToTargetAsync`/`CreateDelegatedTaskAsync`
  (`AgentDelegationToolSetResolver.cs:75-176`) em dois pontos.
- **Risco "modelo escolhe a âncora errada entre instante de processamento e
  instante da mensagem, apesar da regra de precedência" — não existe
  nesta etapa, nasce na etapa 2.** `messageInstant` é sempre `null` aqui
  (Decisão 1), então não há duas âncoras para o modelo escolher entre si —
  ver Risks/Trade-offs para o risco que de fato existe nesta etapa (modelo
  errar a única âncora fornecida). Gatilho para a etapa 2: assim que
  `messageInstant` passar a ter valor real, este risco entra em vigor e
  precisa da mesma exigência de verificação manual documentada que a
  convenção 10 já pede aqui para o caso de âncora única.
- Metadata A2A, contexto de canal, dados de contato (nome, telefone) —
  etapa 2 (`inbox-contexto-canal-metadata`).
- Timestamp por mensagem individual dentro do buffer de debounce de
  `apps/inbox`.
- Timezone por agente — descartado por decisão explícita, não adiado. Não
  entra na lista de itens em aberto, não gera etapa de UI.
- Idioma do dia da semana configurável — mesma natureza de D5, fixo em
  pt-BR.
- Contexto como argumento estruturado de tool call MCP — linha própria.
- Histórico de sessões anteriores do mesmo contato no contexto.
- Validação de não-vazio em `Agent.Instructions` em `apps/api` — ausência
  confirmada (sem `Validator`/checagem de `NotEmpty` encontrada em
  `CreateAgentCommandHandler`/`UpdateAgentCommandHandler`); fica como item
  em aberto (ver Open Questions), não como escopo desta change.
- Qualquer alteração em `apps/inbox`, `apps/api` ou `apps/frontend`.

## Decisions

### 1. Bloco montado por uma função pura nova, não inline em `AgentExecutionService`

Novo tipo estático, `TemporalContextBlockBuilder`
(`apps/workers/src/Buteco.Workers/Agents/TemporalContextBlockBuilder.cs`),
com uma única entrada pública:

```csharp
public static string Build(TimeProvider timeProvider, DateTimeOffset? messageInstant = null)
```

Motivo de ser uma função pura, separada de `AgentExecutionService`: o par de
testes exigido pela convenção 5 (com/sem instante de mensagem × defasagem
acima/abaixo do limiar) precisa rodar sem Testcontainers, sem RabbitMQ, sem
`IChatClient` mockado — só chamando a função com um `FakeTimeProvider` e um
`DateTimeOffset?` sintético. Se a montagem ficasse inline em `ExecuteAsync`,
todo teste do bloco exigiria o pipeline inteiro do worker.

`messageInstant` já existe na assinatura nesta etapa, sempre `null` em
produção (nenhum chamador de `apps/workers` tem outro valor para passar) —
é o espaço preparado que o proposal.md descreve. A etapa 2 passa a chamar
com o valor real, sem mudar a assinatura.

`AgentExecutionService.ExecuteAsync` (linha 174) passa a ser:

```csharp
ChatOptions = new ChatOptions
{
    Instructions = $"{agent.Instructions}\n\n{TemporalContextBlockBuilder.Build(timeProvider)}",
    Tools = toolSet.Tools.Concat(delegationTools).ToList(),
},
```

(`timeProvider` injetado no construtor de `AgentExecutionService` — ver
Decisão 6.)

### 2. Ordem da concatenação: `Agent.Instructions` primeiro, bloco depois

`Agent.Instructions` do operador costuma ser prosa longa (personalidade,
regras de negócio, tom); a âncora temporal precisa ficar próxima do fim do
texto de sistema, não soterrada no meio ou no início de um bloco maior.
Separador: linha em branco dupla (`\n\n`), não um marcador estrutural
(`---`, XML) — o bloco já se declara como não fazendo parte da conversa via
sua própria primeira linha (ver Decisão 3), então não precisa de delimitador
extra.

**Caso degenerado — `Agent.Instructions` vazia**: a concatenação não pode
deixar `\n\n` órfão no início do texto final. `TemporalContextBlockBuilder`
não decide isso sozinho (ele só produz o texto do bloco); quem concatena
(`AgentExecutionService`) precisa tratar o caso — ex.:

```csharp
var instructions = string.IsNullOrWhiteSpace(agent.Instructions)
    ? temporalBlock
    : $"{agent.Instructions}\n\n{temporalBlock}";
```

É par de teste explícito (convenção 5): com e sem `Agent.Instructions`
vazia, nenhum dos dois produz separador órfão. A ausência de validação de
não-vazio em `apps/api` (Non-Goals) faz esse caso alcançável na prática, não
só teórico.

**Achado na implementação (convenção 9):** a Tarefa 5.6 exige esse par de
teste na Seção 5 — sem Testcontainers. Com a concatenação inline em
`AgentExecutionService.ExecuteAsync`, não haveria como testá-la sem subir o
pipeline inteiro do worker. Correção: `TemporalContextBlockBuilder` ganhou
um segundo método estático, `Concatenate(string? agentInstructions, string
temporalContextBlock)`, com exatamente a lógica acima — ainda chamado a
partir do ponto de montagem (`AgentExecutionService`, que continua sendo
quem decide chamar), só que como função pura testável isoladamente, mesmo
espírito da Decisão 1 para `Build`. Não muda o comportamento nem a ordem
descritos acima, só onde o código mora.

### 3. Formato do bloco: ISO 8601 com offset + dia da semana por extenso, regra de precedência em linguagem natural, marcado como fora da conversa

Estrutura (exemplo com os dois instantes presentes, para ilustrar a forma —
nesta etapa `messageInstant` é sempre ausente):

```
[Contexto temporal — não é uma mensagem do usuário, não responda a ele diretamente]
Instante de processamento (agora, relógio do sistema): 2026-08-22T14:32:07-03:00 (sábado)
Instante da mensagem (quando o usuário enviou): 2026-08-21T23:58:40-03:00 (sexta-feira)
[Defasagem entre os dois: 1 dia e 33 minutos — informe o usuário que a resposta está atrasada, se isso for relevante para o pedido.]

Regra: toda expressão de tempo relativa no pedido do usuário ("amanhã",
"sexta que vem", "daqui a uma hora") se resolve contra o instante da
mensagem acima, não contra o instante de processamento. O instante de
processamento só serve para você avaliar se sua resposta está chegando
atrasada.
```

Sem `messageInstant` (o caso real desta etapa), colapsa para:

```
[Contexto temporal — não é uma mensagem do usuário, não responda a ele diretamente]
Instante atual (relógio do sistema): 2026-08-22T14:32:07-03:00 (sábado)

Regra: toda expressão de tempo relativa no pedido do usuário ("amanhã",
"sexta que vem", "daqui a uma hora") se resolve contra o instante acima.
```

Decisões de forma:
- Primeira linha declara explicitamente que o bloco não é conteúdo do
  usuário — mitiga (parcialmente, não elimina) o risco de um agente tratar
  o bloco como parte do pedido a responder.
- Dia da semana e defasagem (quando aplicável) são texto literal produzido
  em código (`CultureInfo("pt-BR")` fixo — nunca `CurrentCulture`), nunca
  aritmética que o modelo precise fazer.
- A linha de defasagem só aparece quando os dois instantes existem E a
  diferença ultrapassa o limiar (Decisão 5) — nesta etapa nunca aparece,
  porque `messageInstant` é sempre `null`, mas o código e o teste do par
  acima/abaixo do limiar entram nesta change (convenção 5, e é o mecanismo
  que a etapa 2 vai acionar sem precisar de mudança nesta função).
- A frase de precedência é texto fixo, não gerado dinamicamente — o
  motivo (mensagem do dia 21 dita "amanhã", processada no dia 22, quer dizer
  dia 22) está no proposal.md; aqui só a forma.

O texto exato (wording em português, nomes de linha) é ajustável na
implementação sem redesenho — o que é fixo por esta decisão é a estrutura
(marcação de não-conversa, ordem processamento→mensagem→defasagem→regra) e
que dia da semana/defasagem nunca são deixados para o modelo calcular.

### 4. Checagem de boot de `TZ`: comparar contra o RESULTADO resolvido, não contra a presença da variável

Confirmado empiricamente (Achado 5): `TZ` presente-porém-inválido cai em
UTC do mesmo jeito silencioso que `TZ` ausente. Checar só
`Environment.GetEnvironmentVariable("TZ") is not null` deixaria passar
exatamente o caso que a checagem existe para pegar.

**Regra**: comparar o valor de `TZ` contra `TimeProvider.LocalTimeZone.Id`
resolvido pela instância **registrada no container** (não
`TimeProvider.System` direto — ver Decisão 6); falha o boot se não
baterem. Sem caso especial para UTC: `TZ=UTC` resolve `Id="UTC"` (bate) e
`TZ=Etc/UTC` resolve `Id="Etc/UTC"` (bate) — um deploy que deliberadamente
quer UTC passa sem exceção especial no código, e um `TZ` ausente/vazio/typo
(que também resolve para `Id="UTC"`, mas não bate com o valor declarado)
falha.

```csharp
public static void ValidateTimeZoneConfiguration(this IHost host)
{
    var timeProvider = host.Services.GetRequiredService<TimeProvider>();
    var declaredTz = Environment.GetEnvironmentVariable("TZ");
    var resolvedId = timeProvider.LocalTimeZone.Id;

    if (declaredTz != resolvedId)
    {
        throw new InvalidOperationException(
            $"Fuso horário não configurado corretamente: TZ='{declaredTz}' " +
            $"não corresponde ao fuso resolvido '{resolvedId}'. " +
            "Defina TZ com o nome IANA canônico do fuso desejado.");
    }

    var logger = host.Services.GetRequiredService<ILogger<Program>>(); // ou logger estático equivalente
    logger.LogInformation(
        "Fuso horário do sistema: {TimeZoneId}, offset atual: {Offset}",
        resolvedId, timeProvider.GetLocalNow().Offset);
}
```

Chamada em `Program.cs`, depois de `var host = builder.Build();`, antes de
`host.Run();` — mesmo molde de
`ValidateRouteAuthenticationClassification` (`apps/inbox/Program.cs:143`),
mas em `IHost` (Worker Service), não `WebApplication`. É a primeira
checagem de startup de `apps/workers`.

**Sem o prefixo POSIX `:`, obrigatório no contrato de deploy**: o Achado 5
mostrou, com evidência de container real, que um alias da tz database
(`Brazil/East`) faz round-trip idêntico — não é aliases em geral que
quebram a comparação estrita. O caso que de fato quebra, também verificado,
é a sintaxe POSIX de prefixo `:` (`:America/Sao_Paulo`): resolve o fuso
certo, mas o `.NET` descarta o `:` ao montar `TimeZoneInfo.Local.Id`, então
o valor declarado nunca bate com o resolvido mesmo estando correto. A
decisão é manter a comparação estrita e declarar no contrato/manifesto de
deploy de `apps/workers` (Tarefa 4.4) que `TZ` usa o nome IANA da tz
database sem o prefixo `:` — regra escrita aqui e lá, não implícita, e
apoiada em caso real, não hipotético.

**Alternativa considerada**: checar só presença de `TZ`
(`Environment.GetEnvironmentVariable("TZ") is not null`). Rejeitada —
Achado 5 mostra que isso passa com um valor inválido que ainda assim cai em
UTC silenciosamente; é o "fixture forjado que passa com o comportamento
certo e com o errado" que a convenção 11 existe para evitar.

**Capability desta checagem**: é comportamento de boot do processo, não
transição de task A2A — o requisito correspondente em spec.md vive em
`workers-scaffold` (`MODIFIED` do requisito "Worker Service mínimo", que já
cobre "o processo sobe... registrando log de início"), não em
`a2a-task-lifecycle`. Ver Achado 6.

### 5. Limiar de defasagem: constante em código, não configuração

Mesmo padrão de `MaxHistoryMessages`/`SummarizationTurnThreshold`/
`DelegationDepthLimit` em `AgentExecutionService` — constante global, não
por agente, não em `appsettings.json` (convenção 2). Valor exato é Open
Question (ponto de partida sugerido: alguns minutos — a defasagem normal
entre processamento e mensagem é de segundos; um valor na casa de minutos
evita o agente se desculpar por atraso insignificante, que é bug de
produto). Nesta etapa a linha nunca é exercida em produção (sem
`messageInstant` real), mas o par de teste acima/abaixo do limiar entra
aqui, chamando `TemporalContextBlockBuilder.Build` diretamente com um
`messageInstant` sintético.

### 6. `TimeProvider` registrado no DI — produção e teste

`Program.cs` ganha `builder.Services.AddSingleton(TimeProvider.System);`.
`AgentExecutionService` passa a receber `TimeProvider` no construtor
(primary constructor, mesmo padrão dos outros resolvers já injetados) e
repassa para `TemporalContextBlockBuilder.Build`. O escopo dessa regra não
é só código novo desta change: depois da Decisão A (Decisão 9), nenhum
código de `apps/workers` — novo ou pré-existente, produção ou teste — chama
`DateTimeOffset.Now`/`TimeZoneInfo.Local` diretamente. Os dois sites
pré-existentes que ainda faziam isso (`TaskJobConsumer.StartAsync`/
`StopAsync`) foram achados pela varredura completa do Achado 9 e fechados
pela Decisão A, não deixados de fora por estarem fora do código novo desta
change. Vale também para a checagem de boot (Decisão 4, que resolve
`TimeProvider` do container, não `TimeProvider.System` direto, para validar
exatamente a instância que a aplicação vai usar).

Testes: `ConversationHistoryTests.BuildHost` (harness já existente) passa a
registrar um `FakeTimeProvider` no lugar do `AddSingleton(TimeProvider
.System)` de produção — mesmo padrão já usado ali para
`IChatClientResolver`/`IMcpToolSetResolver` (`Mock`/`Null*` substituindo o
registro de produção).

### 7. Teste de carimbo por sessão: variação direta de `ConversationHistoryTests.cs`, mesmo harness

O teste que fecha o risco "carimbo congelado" (Achado 2, terceiro eixo) não
precisa de infraestrutura nova: é uma variação de
`AgentInstructionsUpdatedBetweenMessages_SecondCallUsesNewInstructions`
(`ConversationHistoryTests.cs:182-229`) — duas tasks no mesmo `contextId`,
`FakeTimeProvider` avançado entre a task A e a task B (em vez de `UPDATE
agents SET Instructions`), `ChatOptions` capturado via `Callback` na
segunda chamada, e asserção de que o carimbo capturado é o novo, não o da
primeira execução. Mesmo arquivo, mesmo `WorkerInfrastructureFixture`
(Testcontainers Postgres+RabbitMQ), sem host adicional.

### 8. Dependência de teste nova: `Microsoft.Extensions.TimeProvider.Testing` 10.9.0

Verificado no feed do NuGet.org (`https://api.nuget.org/v3-flatcontainer/
microsoft.extensions.timeprovider.testing/index.json`) que `10.9.0` é a
versão estável mais recente publicada na data desta proposta. A
decompilação original de exploração usou `9.4.0` (arbitrária, só para
inspecionar a existência da API); depois de identificar `10.9.0` como a
versão a referenciar, `10.9.0` foi restaurada e decompilada de novo, à
parte — `FakeTimeProvider.cs` daquele pacote confirma
`SetLocalTimeZone(TimeZoneInfo)`/`LocalTimeZone` idênticos aos de `9.4.0`,
mesma assinatura, mesmo comportamento. Não é "sem indicação de mudança
breaking" (convenção 6 não aceita isso) — é confirmação direta da versão
que de fato vai ser referenciada, não só da versão usada para explorar a
API pela primeira vez. `proposal.md`, este documento e `tasks.md` referem
todos `10.9.0` — nenhuma menção a `9.4.0` fora deste parágrafo, que existe
só para registrar que a versão inicialmente inspecionada e a versão
referenciada são diferentes e ambas foram verificadas. Entra em
`Directory.Packages.props` (`<PackageVersion Include="Microsoft.Extensions
.TimeProvider.Testing" Version="10.9.0" />`) e como `<PackageReference>` em
`Buteco.Workers.Tests.csproj` — projeto de teste, não de produção.

### 9. Mecanismo do teste da checagem de boot: mutação em processo (`SetEnvironmentVariable` + `ClearCachedData`), não subprocesso — e nunca via `BuildHost`

**Por que não dá para testar isso com o harness existente.** `BuildHost`
(`ConversationHistoryTests.cs`) registra `FakeTimeProvider` no lugar de
`TimeProvider.System` (Decisão 6) — necessário para os testes que já
existem ali (ex. Decisão 7, que precisa fixar relógio/fuso
deterministicamente). Se a checagem de boot resolvesse o `TimeProvider` do
mesmo container de teste, o teste estaria comparando um valor de `TZ` que
ele mesmo define contra um `FakeTimeProvider.LocalTimeZone` que ele mesmo
também define — os dois lados escolhidos pelo teste, o "fixture forjado"
que passaria com o comportamento certo e com o errado, exatamente o defeito
que este item de revisão apontou. **Decisão: os testes de boot NÃO
reaproveitam `BuildHost`**, e `BuildHost` continua exatamente como está,
sem nunca chamar a checagem — ela é comportamento de `Program.cs`, não do
pipeline de execução de agente que `BuildHost` existe para testar. Os
testes de boot constroem seu próprio host mínimo
(`Host.CreateApplicationBuilder()` registrando só `TimeProvider.System` —
sem Postgres, sem RabbitMQ, sem `AgentExecutionService`) e chamam a mesma
extensão que `Program.cs` chama.

**Mecanismo para provar resolução real de `TZ` dentro desse host mínimo**:
`Environment.SetEnvironmentVariable("TZ", valor)` seguido de
`TimeZoneInfo.ClearCachedData()` antes de resolver `TimeProvider.System` do
host — comportamento confirmado real e repetível no Achado 7, em duas
plataformas (container Linux e macOS nativo, a própria máquina onde
`dotnet test` roda localmente para este repo). Não é um artifício do teste:
é o mesmo mecanismo que faz `TimeProvider.System` refletir o `TZ` do
processo em produção — só que em produção o processo nasce com o valor
certo, e aqui o teste o muda meio da execução para observar a checagem
reagir aos dois lados (bate/não bate).

**Alternativa considerada e rejeitada**: subprocesso dedicado (publicar o
worker, ou um executável mínimo, e rodar via `Process.Start` com `TZ`
setada no ambiente do processo filho). Mais pesado (build/publish extra,
parsing de stdout/exit code, sem nenhum precedente de teste via subprocesso
neste repo) e não compra nada em correção que o Achado 7 já não tenha
provado — `ClearCachedData()` demonstrou ser confiável e repetível dentro
de um único processo, em ambas as plataformas testadas.

**Isolamento entre os testes de boot e o resto da suíte**: os três casos de
teste de boot (Tarefas 7.3-7.5 de tasks.md: sem `TZ`, `TZ` inválida, `TZ`
válida — Seção 7 inteira, fora da seção de Testcontainers) mutam estado
global do processo (`TZ`, cache de `TimeZoneInfo`). Ficam em uma classe de
teste própria (xUnit já roda testes de uma mesma classe em sequência entre
si por padrão, evitando que os três se atropelem) e cada teste restaura
`TZ` ao valor original e chama `ClearCachedData()` de novo ao final
(`finally`/`Dispose`), para não vazar estado mutado para qualquer teste que
rode depois no mesmo processo.

O Achado 9 achou uma ocorrência real que ameaçava esse isolamento —
`TaskJobConsumer.StartAsync`/`StopAsync` lendo `DateTimeOffset.Now`
diretamente, exercitados por pelo menos oito arquivos de teste de
integração desta suíte. Duas decisões fecham isso, nenhuma delas silenciosa:

**Decisão A — `TaskJobConsumer` passa a receber `TimeProvider` no
construtor** (mesmo padrão dos demais componentes desta change — Decisão
6) **e troca as duas chamadas de `DateTimeOffset.Now` por
`timeProvider.GetLocalNow()`** (Tarefa 1.3). Fecha a ocorrência real que o
Achado 9 encontrou, na origem, em vez de trabalhar em volta dela.

**Decisão B — nenhum isolamento adicional de coleção do xUnit, nenhum
projeto de teste novo.** Depois da Decisão A, a classe de teste de boot
(Seção 7) e a `TZ` que ela muta continuam correndo em paralelo com o resto
da suíte de integração, sem serialização especial entre coleções. Raciocínio:
- Depois da Decisão A, nenhum código de `apps/workers` nem dos projetos de
  teste na raiz do repo (produção ou teste, fora do próprio mecanismo de
  teste da Seção 7) lê relógio ou fuso local fora do `TimeProvider`
  injetado — verificado pelos Achados 9 e 10, que juntos não acharam
  nenhuma outra ocorrência além das três já corrigidas (duas em
  `TaskJobConsumer`, uma em `RoundTripFixture`).
- O que resta é leitura de fuso local por código de FRAMEWORK (provider de
  logging, Npgsql/EF Core, o próprio host) — fora do alcance de qualquer
  varredura de código deste repo (framework não é código deste repo), e
  que nenhuma forma de isolamento de coleção do xUnit eliminaria por
  completo, já que esse código roda de qualquer jeito enquanto o processo
  de teste está de pé. Não é a lacuna que os Achados 9/10 relataram; é o
  resíduo que sobra depois de fechá-la — categoria diferente, sem
  contraparte de código deste repo para corrigir.
- A janela de mutação é estreita: três testes, cada um restaurando `TZ` e
  chamando `ClearCachedData()` de novo em `finally`/`Dispose`. Nenhuma
  asserção da suíte lê um valor derivado de fuso local (provado nos
  Achados 9/10) — não há o que quebrar por uma leitura concorrente
  momentânea.
- **Alternativa considerada e rejeitada**: colocar
  `TimeZoneStartupValidationTests` na mesma coleção xUnit dos testes de
  integração, serializando-os contra ela sem precisar de projeto de teste
  novo. Resolveria a concorrência sem custo de infraestrutura, mas amarraria
  os testes de boot ao ciclo de vida do `WorkerInfrastructureFixture`
  (Testcontainers) — e a Tarefa 8.2 depende explicitamente de a Seção 7
  rodar sem Docker disponível. Custo maior que o benefício, rejeitada.
- **Tripwire, não garantia**: se aparecer flake intermitente na suíte de
  integração de `apps/workers` ou nos projetos de teste na raiz do repo
  (`tests/`) depois desta change, os Achados 9/10 e este parágrafo são o
  primeiro lugar a olhar — não uma garantia de que não pode
  acontecer, um atalho para quem for depurar encontrar a causa numa
  leitura, em vez de caçar (mesmo espírito do incidente de
  `InboundMessageOrchestrator` citado na revisão que levantou este ponto).

**Limitação registrada, não escondida (Achado 8)**: estes testes verificam
o contrato geral da checagem (declarado ≠ resolvido → falha) de forma
portável, rodando em qualquer SO que execute a suíte. O cenário específico
"`TZ` ausente cai em UTC em silêncio" do Achado 5 só foi verificado contra
o alvo real de deploy (container Linux, via `podman`) — não é
re-derivado pelo teste em processo, que pode rodar num SO cujo fallback
ambiente não seja UTC (Achado 8). Os dois achados juntos cobrem o
necessário: Achado 5 prova o comportamento em produção; a Seção 7 de
tasks.md prova que a checagem pega a divergência em qualquer SO onde a
suíte rodar.

## Risks / Trade-offs

- **[Risco] Carimbo congelado entre mensagens da mesma sessão** → fechado
  arquiteturalmente (Achado 2) e coberto por teste (Decisão 7): duas tasks
  no mesmo `contextId` com `FakeTimeProvider` avançado, asserção sobre o
  `ChatOptions` capturado na segunda.
- **[Risco] Bloco montado mas não entregue ao modelo** (mesmo perfil do bug
  histórico de `ExtractResponseText` — campo preenchido num lugar que
  ninguém lê) → mitigação: os testes unitários da Seção 5 de tasks.md
  (`Build(...)` chamado isoladamente) cobrem a FORMA do bloco — dia da
  semana, ISO 8601, precedência, pares com/sem instante e acima/abaixo do
  limiar — e nunca são, sozinhos, a prova de que o pipeline real o entrega.
  Essa prova é a Tarefa 6.2: asserção contra o `ChatOptions` capturado no
  `Mock<IChatClient>.GetResponseAsync` do teste de integração, o que de
  fato chega ao client. As duas coisas são exigidas, não uma no lugar da
  outra — cortar a Tarefa 6.2 por parecer redundante com a Seção 5
  reabriria exatamente este risco.
- **[Risco] `TZ` ausente ou inválida → bloco em fuso não declarado, em
  silêncio** → mitigação: checagem de boot da Decisão 4, comparando
  resultado resolvido contra o valor declarado, não só presença. Teste
  (Tarefa 7) cobre os dois casos (ausente e presente-porém-inválida)
  separadamente, no host mínimo da Decisão 9. Achado 5 (container Linux,
  alvo real de deploy) mostra os dois casos caindo no mesmo sintoma (`UTC`
  silencioso); Achado 8 mostra que o fallback exato varia por plataforma
  fora de um container Linux puro — o que não muda é que a checagem falha
  nos dois casos, em qualquer plataforma, porque compara contra o valor
  declarado, não contra um fallback específico esperado.
- **[Risco] Duplicação do bloco se a montagem for chamada mais de uma vez
  no mesmo fluxo** → mitigação: `TemporalContextBlockBuilder.Build` é
  chamado uma única vez, no único ponto de montagem
  (`AgentExecutionService.cs:171-192`, Achado 1); teste (Tarefa 5.7) assere
  ocorrência única do marcador do bloco (`[Contexto temporal`) no texto
  final de `Instructions`. **Por que não vira Scenario em spec.md**: é
  risco de implementação (chamar uma função pura duas vezes no mesmo
  fluxo), não um contrato observável do protocolo A2A — nenhum
  comportamento visível de fora do processo distingue "montado uma vez" de
  "montado duas vezes produzindo o mesmo texto por coincidência"; o que a
  convenção 10 pede aqui é a contraparte de teste (que existe, Tarefa 5.7),
  não necessariamente um Scenario, quando o risco não é de fato uma
  interação observável do sistema com o protocolo.
- **[Risco] Modelo ignora ou interpreta mal o instante de processamento
  fornecido** (única âncora que existe nesta etapa — `messageInstant` é
  sempre `null`, então não há duas âncoras para o modelo escolher entre
  si, e a regra de precedência entre elas não é exercida em produção
  aqui) → **não testável de forma determinística** (comportamento de LLM,
  não de código). Mitigação: primeira linha do bloco marcando-o como fora
  da conversa (Decisão 3); verificação manual documentada no PR contra um
  cenário real de dia virado (mensagem enviada perto da meia-noite,
  processada no dia seguinte), conferindo que a resposta usa o instante de
  processamento fornecido corretamente — não que ela escolhe entre duas
  âncoras, o que só passa a existir na etapa 2 (ver Non-Goals). Listado
  aqui para não ficar como risco não coberto sem justificativa — a
  convenção 10 exige a contraparte ou a justificativa explícita de por que
  não existe, nunca o risco em silêncio.
- **[Trade-off] Comparação estrita de `TZ` exige nome canônico da tz
  database** (Decisão 4) — aceito e documentado no contrato de deploy, não
  escondido. Um operador que configurar um alias reconhecido pelo SO mas
  não idêntico ao `Id` canônico (raro, mas possível) veria o boot falhar
  com uma mensagem clara apontando os dois valores — não um comportamento
  incorreto silencioso.

## Migration Plan

Sem migration de banco — o bloco nunca é persistido, existe só no momento
da chamada ao LLM. Deploy normal de `apps/workers`; nenhuma coordenação com
`apps/api`/`apps/inbox` (nenhum dos dois muda). Único requisito operacional
novo: `TZ` precisa estar definida, com o nome IANA da tz database sem o
prefixo POSIX `:` (Decisão 4, Achado 5), no ambiente de execução de
`apps/workers` (dev local via `docker-compose.yml`, qualquer ambiente de
deploy) — sem isso, o processo não sobe (Decisão 4).
Rollback: reverter o deploy de `apps/workers`; nenhum estado persistido
precisa ser desfeito.

## Open Questions

- Valor exato do limiar de defasagem (Decisão 5) — ponto de partida
  sugerido: alguns minutos; ajustar depois de observar comportamento real
  na etapa 2 (é quando a linha passa a aparecer de fato). Constante isolada,
  fácil de mudar sem redesenho.
- Wording exato do bloco em português (Decisão 3) — a estrutura é fixa, o
  texto literal (nomes de linha, frase de precedência) é ajustável na
  implementação.
- Validação de não-vazio para `Agent.Instructions` em `apps/api`
  (Non-Goals) — ausência confirmada nesta exploração; não é escopo desta
  change, mas fica registrada como lacuna pré-existente encontrada, não
  introduzida por ela.
