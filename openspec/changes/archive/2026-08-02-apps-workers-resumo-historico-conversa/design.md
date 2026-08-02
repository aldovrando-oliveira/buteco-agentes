## Context

`AgentExecutionService.ExecuteAsync` (`apps/workers/src/Buteco.Workers/Agents/AgentExecutionService.cs`)
já monta o `ChatClientAgent` com histórico de conversa (change
`apps-workers-historico-conversa`, arquivada em
`openspec/changes/archive/2026-08-01-apps-workers-historico-conversa/`):

```csharp
var chatClient = chatClientResolver.Resolve(agent.Provider!, agent.Model!);
var aiAgent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
{
    Name = agent.Name,
    ChatOptions = new ChatOptions { Instructions = agent.Instructions },
    ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
    {
        ChatReducer = new RecentMessageChatReducer(MaxHistoryMessages), // 20 hoje — revisado por esta change, ver Decisão 9
    }),
});
```

O histórico é recuperado da última task `completed` do mesmo `contextId`
(`AgentTask.Metadata["conversationSession"]`, uma `AgentSession` nativa
serializada, guardada como string JSON escapada via `ConversationSessionCodec`
— Decisões 1 e 10 daquela change) e limitado por truncamento simples: acima
de `MaxHistoryMessages` mensagens não-system, o `RecentMessageChatReducer`
próprio (reimplementado contra a interface estável `IChatReducer` — ver
adiante) descarta as mais antigas sem aviso.

Truncar por contagem de mensagens contém o crescimento do histórico, mas
perde contexto de conversas longas de uma vez. O objetivo desta change é
resumir a porção que excede um limiar de interações em vez de descartá-la,
mantendo o mesmo mecanismo de persistência já existente.

**Investigação feita antes de qualquer decisão abaixo** (via `/opsx:explore`,
decompilando os pacotes reais instalados com `ilspycmd` — não assumido de
memória de treinamento, mesmo método já usado na change anterior):

**Hipótese inicial avaliada e não escolhida**: usar `SummarizingChatReducer`
(`Microsoft.Extensions.AI` 10.6.0, já instalado) como substituto direto do
`ChatReducer` de `InMemoryChatHistoryProviderOptions`. Confirmado via
decompilação que esse tipo existe, com assinatura
`SummarizingChatReducer(IChatClient chatClient, int targetCount, int?
threshold)`, mas:
- É `[Experimental("MEAI001")]` — mesmo diagnóstico que a change anterior já
  rejeitou explicitamente para o `MessageCountingChatReducer` (Decisão 4
  daquele design.md), escolhendo reimplementar contra `IChatReducer` em vez
  de depender do tipo concreto experimental.
- `ReduceAsync` não tem nenhum tratamento de exceção. Confirmado
  decompilando `Microsoft.Agents.AI.InMemoryChatHistoryProvider.ReduceMessagesAsync`
  (`state.Messages = (await reducer.ReduceAsync(...)).ToList()`): se
  `ReduceAsync` lança, a atribuição nunca ocorre (o estado em memória não
  corrompe), mas a exceção propaga por `ProvideChatHistoryAsync` →
  `ChatClientAgent.RunAsync` → o `catch` genérico de
  `AgentExecutionService.ExecuteAsync`, levando a **task inteira** a
  `failed` — incompatível com o requisito de degradação graciosa desta
  change (uma falha de resumo não pode custar o turno inteiro do usuário).
  Usar esse tipo exigiria escrever um `IChatReducer` decorator próprio só
  para engolir essa exceção.
- Achado adicional, não confirmável com certeza só por decompilação: o
  mecanismo de "resumo incremental" desse tipo marca uma `ChatMessage` com
  `AdditionalProperties["__summary__"]`, mas essa mensagem específica
  (`unsummarizedMessages[indexOfFirstMessageToKeep - 1]`) é a mesma que o
  próprio `ReduceAsync` exclui do resultado retornado
  (`Skip(indexOfFirstMessageToKeep)`) — o marcador provavelmente não
  sobrevive ao ciclo persistir/reler a sessão entre turnos, exigindo
  confirmação com teste dedicado se esse tipo fosse escolhido.

**Achado principal, não previsto pela hipótese inicial**: existe um
namespace inteiro, `Microsoft.Agents.AI.Compaction`, já presente no mesmo
pacote `Microsoft.Agents.AI` 1.15.0 já referenciado por `apps/workers` (sem
bump de versão necessário) — um pipeline de compactação de histórico
propósito-construído, com uma estratégia de resumo dedicada
(`SummarizationCompactionStrategy`) que resolve, nativamente, os dois
problemas acima. Ver Decisão 1.

**Segunda rodada de investigação, feita antes de `/opsx:apply`**: decompilar
o pacote confirma que `ChatHistoryProvider` e `AIContextProviders` são
propriedades configuráveis separadamente em `ChatClientAgentOptions`, mas
isso não prova que operam sobre dados independentes nem que a ordem entre
eles é livre de conflito. Decompilando o pipeline real de execução
(`ChatClientAgent.PrepareSessionAndMessagesAsync` e
`CompactionMessageIndex.Update`, ambos em `Microsoft.Agents.AI` 1.15.0),
confirmou-se que **não são independentes**: `RecentMessageChatReducer`
(plugado em `ChatHistoryProvider`) roda antes de `CompactionProvider`
(plugado em `AIContextProviders`) em toda execução, e o truncamento
destrutivo do primeiro invalida o bookkeeping incremental do segundo assim
que a conversa ultrapassa `MaxHistoryMessages`. Ver Decisão 9 — a conclusão
mudou o valor de `MaxHistoryMessages` proposto (não o mecanismo em si).

## Goals / Non-Goals

**Goals:**
- Resumir (não truncar) a porção do histórico de conversa que ultrapassa um
  limiar global de interações, antes de cada chamada ao LLM.
- Resumo incremental: cada novo cruzamento do limiar soma o resumo anterior
  aos turnos novos desde então, nunca resume a conversa inteira do zero.
- Reaproveitar o `IChatClient` já resolvido por `IChatClientResolver` para o
  agente — sem client dedicado para a chamada de resumo.
- Falha na chamada de resumo não impede o turno do usuário de ser
  processado — degrada graciosamente para o histórico não resumido daquele
  turno.
- Cobrir o comportamento com teste de integração (`IChatClient` mockado,
  mesmo padrão já estabelecido, sem exigir Testcontainers para os cenários
  que não precisam de Postgres real).

**Non-Goals:**
- Limiar configurável por agente — nasce como constante global, igual a
  `MaxHistoryMessages` hoje.
- UI para visualizar ou editar o resumo.
- Qualquer mudança em `apps/api`.
- Modelo de resumo dedicado/separado do modelo do agente.
- Mecanismo de resetar o resumo manualmente.
- Revisar ou substituir o mecanismo `RecentMessageChatReducer`/
  `InMemoryChatHistoryProvider` — a classe e a lógica continuam exatamente
  como estão, limitando o que é persistido no Postgres. O **valor** da
  constante `MaxHistoryMessages` é revisado nesta change (Decisão 9) para
  parar de competir com `CompactionProvider` — isso não é uma mudança de
  mecanismo, é o ajuste mínimo necessário para os dois coexistirem sem
  conflito.

## Decisions

### 1. `CompactionProvider` + `SummarizationCompactionStrategy` nativos (`Microsoft.Agents.AI.Compaction`), não `SummarizingChatReducer` nem mecanismo manual

`AIContextProviders` (lista) ganha um `CompactionProvider` novo, construído
com `SummarizationCompactionStrategy(chatClient, CompactionTriggers.TurnsExceed(SummarizationTurnThreshold))`
— reaproveitando a mesma variável `chatClient` já resolvida por
`chatClientResolver.Resolve(agent.Provider!, agent.Model!)` para o
`ChatClientAgent` (ver Decisão 4). Confirmado via decompilação completa de
`SummarizationCompactionStrategy.CompactCoreAsync`:

```csharp
try
{
    val = await ChatClient.GetResponseAsync(list, null, cancellationToken);
}
catch (Exception ex) when (!(ex is OperationCanceledException))
{
    // desfaz a exclusão dos grupos (IsExcluded = false / ExcludeReason = null)
    // loga via logger.LogSummarizationFailed
    return false; // nenhuma exceção propaga
}
```

Isso resolve a degradação graciosa (Goals, terceiro item) **sem nenhum
código nosso** — não é um comportamento que implementamos, é confirmado por
decompilação do pacote já instalado.

Confirmado também, decompilando `ChatClientAgentOptions` por completo, que
`ChatHistoryProvider` (usado hoje pelo `InMemoryChatHistoryProvider`) e
`AIContextProviders` são propriedades **separadas** — adicionar o
`CompactionProvider` não substitui nem conflita com o que já existe.
`RecentMessageChatReducer` continua rodando com a mesma lógica de hoje,
limitando o que é persistido no Postgres (non-goal explícito: não alterar o
mecanismo) — só o valor do teto muda, e por um motivo concreto descoberto
na investigação: ver Decisão 9. O `CompactionProvider` novo só resume o que
é efetivamente enviado ao LLM em cada chamada.

`CompactionProvider` mantém seu próprio estado incremental
(`State.MessageGroups`, uma lista de `CompactionMessageGroup`) dentro do
mesmo `AgentSession.StateBag` que `InMemoryChatHistoryProvider` já usa (via
`ProviderSessionState<State>`, mesmo padrão, chave própria — o nome do tipo
da estratégia por padrão). A cada invocação, se já existir estado
persistido, carrega os grupos existentes e só faz merge incremental das
mensagens novas — não resume do zero a cada gatilho (ver Decisão 3).

**Custo aceito**: `SummarizationCompactionStrategy`, `CompactionProvider`,
`CompactionTriggers` e todo o namespace `Compaction` são
`[Experimental("MAAI001")]` — diagnóstico diferente do `MEAI001` da change
anterior, mesmo tipo de risco (a API pode mudar ou ser removida numa
atualização futura do pacote sem aviso de breaking change). Diferente da
change anterior — onde reimplementar `MessageCountingChatReducer` contra
`IChatReducer` levou ~35 linhas (`RecentMessageChatReducer`) porque truncar
por contagem é lógica simples — reimplementar este pipeline à mão para
evitar `MAAI001` exigiria reconstruir bookkeeping incremental de grupos de
mensagens, proteção de mensagens de sistema/tool-calls, e orquestração de
resumo com fallback em falha: uma superfície bem maior, para uma lógica
inerentemente mais complexa. O risco de mudança futura sem aviso é aceito
aqui porque o custo de evitá-lo (reimplementar tudo isso à mão, sujeito a
bugs sutis que o pacote já tem testado) supera o benefício, ao contrário do
caso simples da change anterior.

**Alternativas consideradas**:
- **`SummarizingChatReducer` (`Microsoft.Extensions.AI`) plugado em
  `InMemoryChatHistoryProviderOptions.ChatReducer`, com um `IChatReducer`
  decorator próprio só para engolir exceção da chamada de resumo**. Mais
  próximo do padrão já usado por `RecentMessageChatReducer` (troca só o
  reducer), mas exige escrever à mão exatamente a degradação graciosa que o
  `SummarizationCompactionStrategy` já tem pronta e testada — código extra
  para replicar um comportamento que o pacote já oferece nativamente, numa
  parte crítica (tratamento de falha) onde um bug de implementação própria
  seria pior que depender do tipo experimental. Rejeitada.
- **Mecanismo 100% manual, sem nenhum tipo experimental** (mesma filosofia
  do `RecentMessageChatReducer`: reimplementar contra interface/mecanismo
  estável). Mais consistente com o precedente da change anterior de evitar
  toda superfície experimental, mas exigiria implementar à mão: gatilho por
  contagem de turnos, construção do prompt de resumo, chamada ao LLM com
  tratamento de falha, e a lógica incremental (resumir(resumo_anterior +
  mensagens novas) em vez de do zero) — a parte mais complexa e propensa a
  bug de toda a fatia, sem nenhum código do framework fazendo o trabalho
  pesado. Rejeitada: para uma lógica desse tamanho, a chance de introduzir
  um bug sutil (ex. no ponto de corte entre resumido/preservado, ou na
  extração do texto de resposta do LLM) supera o risco de uma mudança futura
  sem aviso em `MAAI001`.

### 2. Persistência do resumo: nenhum mecanismo novo — reaproveita o `AgentSession.StateBag`/`Metadata["conversationSession"]` já existente

Como `CompactionProvider` persiste seu próprio estado dentro do mesmo
`AgentSession.StateBag` que `InMemoryChatHistoryProvider` já usa, e a change
anterior já serializa o `AgentSession` inteiro (`SerializeSessionAsync`) para
`AgentTask.Metadata["conversationSession"]` via `ConversationSessionCodec`
(Decisão 1 daquela change), o estado do resumo cai de graça no mesmo
mecanismo — nenhuma tabela, coluna ou codec novo. O `ConversationSessionCodec`
guarda o blob inteiro como string JSON escapada (Decisão 10 daquela change,
motivada por um bug real de reordenação de propriedades em `jsonb`) — como
essa proteção é sobre o blob inteiro, não sobre um tipo específico dentro
dele, cobre automaticamente qualquer conteúdo novo que passe a fazer parte
da sessão, incluindo o estado do `CompactionProvider`.

### 3. Resumo incremental: nativo, via bookkeeping de grupos do `CompactionProvider` — não resumarização do zero a cada gatilho

Confirmado decompilando `CompactionProvider.InvokingCoreAsync`: se já existe
estado persistido (`state.MessageGroups.Count > 0`), o provider reconstrói o
`CompactionMessageIndex` a partir dos grupos salvos e só faz merge das
mensagens novas desde a última execução (`messageIndex.Update(messageList)`)
— grupos já marcados como resumidos (`IsExcluded = true`) permanecem assim,
sem reprocessamento. `SummarizationCompactionStrategy.CompactCoreAsync` só
envia ao `IChatClient` os grupos ainda não excluídos que ultrapassam
`MinimumPreservedGroups` — nunca a conversa inteira. Satisfaz o requisito
"resumir(resumo_anterior + mensagens novas)" sem nenhum código nosso para
rastrear isso.

### 4. Chamada de resumo reaproveita o `IChatClientResolver` já existente — mesma instância, sem client dedicado

`SummarizationCompactionStrategy` recebe, no construtor, a mesma variável
`chatClient` já obtida por `chatClientResolver.Resolve(agent.Provider!,
agent.Model!)` em `AgentExecutionService.ExecuteAsync` — o mesmo
provider/model já resolvido para a conversa principal do agente, sem nenhuma
resolução adicional nem cliente separado para a chamada de resumo.

### 5. Degradação graciosa: nativa, confirmada por decompilação — não implementação própria

Ver Decisão 1: `SummarizationCompactionStrategy.CompactCoreAsync` já engole
qualquer exceção da chamada de resumo (exceto `OperationCanceledException`),
desfaz a exclusão dos grupos que seriam resumidos, loga e retorna `false` —
o turno do usuário segue com o histórico não resumido daquele momento, e o
resumo é tentado de novo no próximo gatilho (o estado persistido não foi
alterado, então o mesmo conjunto de grupos antigos continua elegível). Não é
necessário nenhum `try`/`catch` nosso em `AgentExecutionService` para esse
caso específico.

### 6. Trade-off aceito: uma chamada extra ao LLM por gatilho cruzado, em troca de conter o crescimento ilimitado do histórico enviado por turno

O histórico enviado ao LLM já é limitado em tamanho pelo
`RecentMessageChatReducer` (Decisão 4 da change anterior — o mecanismo não
muda aqui, só o valor do teto, ver Decisão 9), mas cresce de novo a cada
turno até o próximo truncamento, e o truncamento descarta contexto. O resumo troca isso por: sempre que o número
de interações desde o último resumo (ou desde o início da conversa)
ultrapassa `SummarizationTurnThreshold`, uma chamada adicional ao LLM
resume a porção mais antiga — custo extra concentrado nesses gatilhos, não
em todo turno, em troca de manter contexto condensado em vez de descartado.
Aceito e documentado, não escondido.

### 7. Gatilho por `CompactionTriggers.TurnsExceed`, não `MessagesExceed` — nova constante global `SummarizationTurnThreshold`

`Microsoft.Agents.AI.Compaction.CompactionTriggers` (decompilado por
completo) oferece `MessagesExceed(int)`, `TurnsExceed(int)` (conta turnos de
usuário reais, via `CompactionMessageGroup.TurnIndex`), `TokensExceed`/
`TokensBelow`, `GroupsExceed`, `HasToolCalls()`, e `All`/`Any` para compor.
Decisão: usar `TurnsExceed(SummarizationTurnThreshold)` — mapeia
diretamente para "depois de N interações", a unidade em que o requisito foi
formulado, mesmo sendo uma unidade diferente de `MaxHistoryMessages`
(mensagens) usado pelo `RecentMessageChatReducer` existente — os dois
mecanismos são independentes (Non-Goals) e cada um usa a unidade mais
natural para o seu próprio propósito.

**Alternativa considerada**: `MessagesExceed`, para manter a mesma unidade
(contagem de mensagens) do `MaxHistoryMessages` já existente. Rejeitada:
"interação"/turno é a unidade em que o requisito de produto foi formulado, e
`TurnsExceed` já conta turnos de usuário de verdade (não uma aproximação por
divisão de mensagens por 2) — usar `MessagesExceed` só para uniformizar a
unidade com um mecanismo ortogonal não traria benefício real.

### 8. Risco aceito, não bloqueante: `[Experimental("MAAI001")]` pode mudar ou ser removido sem aviso de breaking change

Mesmo tipo de risco já aceito para outras dependências do Microsoft Agent
Framework neste repo (ex. `AgentSession`/`SerializeSessionAsync` em si, na
change anterior), mas numa superfície de código nova maior — o namespace
`Compaction` inteiro (`CompactionStrategy`, `CompactionMessageIndex`,
`CompactionMessageGroup`, `CompactionProvider`, `CompactionTriggers`), não
só um tipo isolado. Ver Risks/Trade-offs para a mitigação.

### 9. `MaxHistoryMessages` precisa virar um teto de segurança bem mais alto — não pode competir com `SummarizationTurnThreshold`

**Investigação feita antes de `/opsx:apply`**, por pedido explícito, porque
a Decisão 1 só tinha confirmado que `ChatHistoryProvider` e
`AIContextProviders` são propriedades separadas em `ChatClientAgentOptions`
— o que prova que são configuráveis independentemente, não que operam sobre
dados independentes nem em uma ordem livre de conflito.

**Ordem real, confirmada decompilando `ChatClientAgent.PrepareSessionAndMessagesAsync`**:

```csharp
// dentro de PrepareSessionAndMessagesAsync:
enumerable = await LoadChatHistoryAsync(typedSession, enumerable, chatOptions, ct);
// ... só depois:
foreach (AIContextProvider item2 in aIContextProviders)
{
    val2 = await item2.InvokingAsync(new InvokingContext(this, typedSession, val2), ct);
}
```

`LoadChatHistoryAsync` (que chama `ChatHistoryProvider.InvokingAsync` →
`InMemoryChatHistoryProvider.ProvideChatHistoryAsync` →
`RecentMessageChatReducer`) roda **antes** do loop de `AIContextProviders`
(`CompactionProvider`), em toda execução. `CompactionProvider` só recebe o
que sobrou depois do truncamento — não uma visão independente do histórico
completo.

**Por que isso quebra o propósito da change, não só reduz sua eficácia —
confirmado decompilando `CompactionMessageIndex.Update`**:

```csharp
internal void Update(IList<ChatMessage> allMessages)
{
    // procura _lastProcessedMessage (a mensagem mais recente vista da
    // última vez) dentro de allMessages, contando a partir do fim
    ...
    if (num < 0 || num + 1 < RawMessageCount)
    {
        Groups.Clear();               // descarta TODO o bookkeeping acumulado
        _currentTurn = 0;
        AppendFromMessages(allMessages, 0);   // reconstrói do zero
    }
    else
    {
        AppendFromMessages(allMessages, num + 1);  // caminho incremental normal
    }
}
```

`RawMessageCount` (`CompactionMessageIndex`) é uma contagem cumulativa de
todas as mensagens que o `CompactionProvider` já indexou desde o início da
conversa — inclui grupos excluídos (resumidos ou truncados), que **nunca
são fisicamente removidos** do índice (confirmado decompilando
`CompactionMessageGroup`/`TruncationCompactionStrategy`: excluir só marca
`IsExcluded = true`, o conteúdo continua ocupando a lista/o estado
persistido). Esse contador só cresce.

Assim que `RecentMessageChatReducer` começa a truncar de verdade — ou seja,
assim que a conversa ultrapassa `MaxHistoryMessages` mensagens brutas — a
janela que chega ao `CompactionProvider` fica presa em, no máximo,
`MaxHistoryMessages` mensagens para sempre, enquanto `RawMessageCount`
continua crescendo a cada turno. A partir desse ponto, `num + 1 <
RawMessageCount` fica **permanentemente verdadeiro**: todo turno seguinte
aciona `Groups.Clear()`, descartando resumo e bookkeeping acumulados antes
de qualquer resumo conseguir se estabilizar. Não é um caso raro — é o
estado estacionário garantido a partir do momento em que o truncamento
passa a agir de verdade.

Com os valores originalmente propostos (`MaxHistoryMessages = 20` ≈
`SummarizationTurnThreshold = 10` turnos, assumindo ~2 mensagens por turno
sem tool calls), essa colisão começa quase no primeiro gatilho de resumo
possível — o mecanismo de resumo nunca chega a estabilizar para uma
conversa que continua crescendo, exatamente o cenário que esta change
existe para resolver. Confirma, com evidência de decompilação, a suspeita
levantada antes do `/opsx:apply`.

**Nenhum valor finito de `MaxHistoryMessages` elimina a colisão por
completo** — para uma conversa que cresce indefinidamente, o momento em que
o total de mensagens brutas ultrapassa `MaxHistoryMessages` sempre chega
eventualmente, e a partir daí o `Groups.Clear()` recorrente é permanente
pelo resto daquela conversa. O que muda com um valor maior é **até onde**
a conversa consegue crescer usando resumo real (incremental, estável) antes
de reverter para truncamento puro.

**Decisão**: `MaxHistoryMessages` deixa de ser um limiar próximo de
`SummarizationTurnThreshold` e passa a ser um teto de segurança bem mais
alto — ponto de partida sugerido: **200** (~10x o equivalente em mensagens
de `SummarizationTurnThreshold = 10` turnos), para que
`RecentMessageChatReducer` funcione como rede de segurança contra
crescimento patológico, não como um limiar concorrente dentro da faixa de
operação normal do `CompactionProvider`. Dentro dessa faixa (até ~200
mensagens brutas, dezenas de gatilhos de resumo), o resumo acumula estado
de forma estável, conforme a Decisão 3 pretende. Só uma conversa
excepcionalmente longa (mais de ~200 mensagens brutas — muito além do que
`SummarizationTurnThreshold = 10` e `MinimumPreservedGroups = 8` precisam
para operar) reabriria a colisão descrita acima.

**Risco residual aceito, não escondido**: se uma conversa realmente
ultrapassar o novo teto, o comportamento degrada de volta para o
truncamento puro de hoje (perda de contexto antigo, sem erro, sem crash) —
não é o "nunca perder contexto" que esta change busca, mas também não é
regressão: é exatamente o comportamento pré-existente antes desta change,
como último recurso. Ver Risks/Trade-offs.

**Alternativas consideradas**:
- **Remover `RecentMessageChatReducer`/`ChatReducer` de
  `InMemoryChatHistoryProviderOptions` por completo**, deixando só o
  `CompactionProvider` bounding o histórico. Eliminaria a colisão de raiz,
  mas reabriria o problema que a change anterior corrigiu explicitamente
  (Decisão 4 daquele design.md: "o limite também contém o tamanho do que
  fica persistido no Postgres") — como grupos excluídos do
  `CompactionProvider` também nunca são removidos fisicamente do estado
  persistido, `Metadata["conversationSession"]` cresceria sem limite para
  conversas muito longas, só que agora via `CompactionProvider.State`
  em vez de `InMemoryChatHistoryProvider.State`. Rejeitada: trocaria um
  problema por outro, violando um non-goal explícito desta change (não
  alterar o mecanismo que bound a persistência) sem necessidade.
- **Manter os dois limiares próximos, aceitando a colisão como está**.
  Rejeitada — decompilação confirma que isso não é "eficácia reduzida", é
  "o mecanismo de resumo nunca funciona de forma estável", o oposto do
  propósito da change.
- **Encadear `SummarizationCompactionStrategy` +
  `TruncationCompactionStrategy` via `PipelineCompactionStrategy`, tudo
  dentro do `CompactionProvider`, eliminando `RecentMessageChatReducer`**.
  Resolveria a colisão de ordem (as duas estratégias operam no mesmo
  `CompactionMessageIndex`, sem a barreira entre `ChatHistoryProvider` e
  `AIContextProviders`), mas decompilando `TruncationCompactionStrategy`
  confirma que ela também só marca `IsExcluded = true` — não remove
  fisicamente conteúdo do índice/estado persistido. Não resolveria o
  crescimento ilimitado do que é persistido, só reorganizaria onde ele
  acontece. Fica registrada como evolução possível (Open Questions) se o
  crescimento do blob de sessão se mostrar um problema real na prática —
  fora do escopo desta fatia, que já resolve o objetivo principal (resumir
  em vez de descartar, dentro de uma faixa de operação generosa) sem essa
  reestruturação adicional.

## Risks / Trade-offs

- **[Risco] `Microsoft.Agents.AI.Compaction` é `[Experimental("MAAI001")]`
  e pode mudar de forma incompatível numa atualização futura de
  `Microsoft.Agents.AI`, sem aviso de breaking change** → mitigação: a
  superfície usada por esta change fica isolada dentro de
  `AgentExecutionService` (um único ponto de construção); um bump de pacote
  que quebre essa API exigiria ajustar só esse ponto, com os testes desta
  change (mockados) detectando a quebra de comportamento antes de produção.
  Nenhuma migration de dados depende da forma interna de
  `CompactionMessageGroup` — o que é persistido é o blob de sessão opaco já
  existente (Decisão 2), então mesmo uma mudança de formato interno do
  pacote seria coberta pelo mesmo fallback "sem sessão anterior" que a
  change anterior já trata para blobs incompatíveis.
- **[Risco] Resumo gerado pelo LLM perde detalhes que os turnos brutos
  tinham** → aceito: é a troca intencional desta change (custo/contexto por
  custo/latência). `MinimumPreservedGroups` (padrão do pacote: 8) mantém os
  turnos mais recentes sempre crus, não resumidos — só a porção mais antiga
  é condensada.
- **[Trade-off] Uma chamada extra ao LLM por gatilho cruzado** — ver
  Decisão 6, aceito e documentado.
- **[Risco] Falha silenciosa de resumo repetida indefinidamente** (ex.
  provider do agente permanentemente indisponível) → mitigação parcial: cada
  falha é logada (`logger.LogSummarizationFailed`, nativo do pacote); como o
  estado dos grupos não avança quando a chamada falha, o próximo turno tenta
  resumir o mesmo conjunto de novo — sem acumular tentativas nem parar de
  tentar, mas também sem alertar operacionalmente além do log. Aceitável
  para esta fatia (non-goal: nenhuma UI/alerta novo); considerar
  observabilidade dedicada como evolução futura se isso se mostrar um
  problema real.
- **[Risco] Conversas que ultrapassam o novo teto de `MaxHistoryMessages`
  (~200 mensagens brutas) perdem a garantia de "resumir em vez de
  descartar"** (Decisão 9) → mitigação: degrada para o truncamento puro que
  já existe hoje (sem erro, sem crash, sem regressão em relação ao
  comportamento pré-change) — só deixa de resumir a partir desse ponto. Não
  bloqueante para esta fatia porque o teto é generoso o bastante (~10x
  `SummarizationTurnThreshold`) para cobrir a esmagadora maioria das
  conversas reais; monitorar se conversas excepcionalmente longas se
  tornarem comuns o suficiente para justificar a evolução nativa descrita
  na Decisão 9 (`PipelineCompactionStrategy` substituindo
  `RecentMessageChatReducer` por completo).

## Migration Plan

Sem migration de banco — o estado do `CompactionProvider` vive dentro do
mesmo `AgentSession.StateBag`/`Metadata["conversationSession"]` já existente
(Decisão 2). Deploy normal de `apps/workers`; nenhuma coordenação especial
com `apps/api` (que continua nunca lendo nem escrevendo esse campo). Sessões
serializadas antes deste deploy simplesmente não têm estado de
`CompactionProvider` — tratadas como "nenhum grupo resumido ainda", mesmo
comportamento de uma conversa nova a partir do ponto de vista da
compactação, sem erro. Rollback: reverter o deploy de `apps/workers`; a
versão anterior do código ignora as chaves extras que passariam a existir no
`StateBag` de sessões serializadas pela versão nova, sem quebrar.

## Open Questions

- Valor exato de `SummarizationTurnThreshold` (a definir na implementação,
  ponto de partida sugerido: 10 interações) — ajustar depois de observar
  custo/latência real; é uma constante isolada, fácil de mudar sem
  redesenho, mesmo padrão do `MaxHistoryMessages` existente.
- `MinimumPreservedGroups` do `SummarizationCompactionStrategy` (padrão do
  pacote: 8) não é reconfigurado explicitamente por esta change — usar o
  padrão do pacote a menos que a implementação identifique necessidade de
  um valor diferente. **Relação obrigatória com `SummarizationTurnThreshold`
  (Decisão 1/7)**: `SummarizationTurnThreshold` precisa ser maior que
  `MinimumPreservedGroups`, senão não existe conteúdo elegível para resumir
  no primeiro gatilho — `SummarizationCompactionStrategy.CompactCoreAsync`
  só age sobre grupos além dos `MinimumPreservedGroups` mais recentes
  (Decisão 1). Os valores sugeridos (10 e 8) já satisfazem essa relação; ao
  ajustar qualquer um dos dois no futuro, preservar `SummarizationTurnThreshold
  > MinimumPreservedGroups`.
- Valor exato do novo teto de `MaxHistoryMessages` (Decisão 9, ponto de
  partida sugerido: 200) — mesma natureza de ajuste futuro que os outros
  dois valores acima: fácil de mudar sem redesenho, só precisa continuar
  generoso o bastante em relação a `SummarizationTurnThreshold`/
  `MinimumPreservedGroups` para que `RecentMessageChatReducer` funcione como
  rede de segurança, não como limiar concorrente (ver Decisão 9).
- Se conversas excepcionalmente longas (além do novo teto de
  `MaxHistoryMessages`) se tornarem comuns o suficiente na prática, a
  evolução nativa é substituir `RecentMessageChatReducer` por um
  `PipelineCompactionStrategy` (`SummarizationCompactionStrategy` +
  `TruncationCompactionStrategy`) inteiramente dentro do `CompactionProvider`
  — alternativa já avaliada e registrada como rejeitada por ora na Decisão
  9, não porque seja tecnicamente inviável, mas porque não resolve sozinha
  o crescimento do que é persistido (grupos excluídos também não são
  removidos fisicamente) e adiciona complexidade sem necessidade comprovada
  ainda.
