## Context

O diagnóstico está fechado por duas rodadas de exploração em 22/09/2026, com
reprodução contra o Gemini real. O `proposal.md` carrega a exceção capturada e o
mecanismo; este documento decide **como** corrigir, e registra o que a
verificação precisa provar.

**Estado atual, com arquivo e linha:**

| peça | onde | o que faz hoje |
|---|---|---|
| composição da compactação | `Agents/AgentExecutionService.cs:392-412` | `CompactionProvider(SummarizationCompactionStrategy(CompactionCallChatClient(chatClient), CompactionTriggers.TurnsExceed(10)))`, **sem** `loggerFactory` |
| marcação de finalidade | `ExecutionMetrics/CompactionCallChatClient.cs:41-46` | abre `MarkPurpose(Compaction)` e delega ao client compartilhado; **não toca nas mensagens** |
| medida e log da requisição | `Agents/LlmCallDurationChatClient.cs:70-154` | `try/catch/finally`; o `finally` escreve **uma** linha ("Chamada ao LLM concluída") e grava a linha filha |
| status HTTP da falha | `ExecutionMetrics/ExecutionMetricsScope.cs:148-153` | casa `HttpRequestException { StatusCode: { } }` e `ClientResultException { Status: > 0 }` |
| nível de log | `apps/{api,inbox,workers}/src/*/appsettings.json` | `Default: Information`; **nenhum** `appsettings.Production.json` existe |
| checagens de startup | `Knowledge/Indexing/EmbeddingBatchSizeValidation.cs:50+`, `EmbeddingIndexConsistencyValidation.cs:44+` | só lançam; **nenhuma linha de sucesso** |

**Restrições que amarram o desenho:**

- **O client é compartilhado por `(provider, model)` durante a vida do
  processo** (`ChatClientResolver`, change `fix-vazamento-httpclient-chat`).
  Nada que esta change acrescente pode ser dono dele nem descartá-lo.
- **`Microsoft.Agents.AI.Compaction` é `[Experimental("MAAI001")]`** e a
  estratégia é `sealed`: não há ponto de extensão dentro dela. O que dá para
  mudar é **o que entregamos ao client que ela chama**.
- **A exceção do resumo é engolida pela estratégia por desenho**, e o guarda
  `SummarizationCallFailure_DoesNotPreventUserTurnFromCompleting` prende isso.
  Continua certo não derrubar o turno do usuário — o que falta é a falha
  **aparecer**.

### Reverificação — o que mudou desde a exploração

- A `metricas-execucao-coleta` foi **arquivada** em 22/09/2026
  (`openspec/changes/archive/2026-09-22-metricas-execucao-coleta/`). O escopo 3
  desta change corrige um defeito dela: o registro arquivado é corrigido com a
  causa (convenção 9).
- O `02` já registra, do mesmo dia, o achado de **falta de timeout de conexão nas
  chamadas HTTP de saída** — o IPv6 com rota e sem conectividade que fez a
  primeira corrida do harness estourar 100 s. É item próprio, **fora desta
  change**, e não se confunde com o `400` do Gemini: aquele espera o timeout
  total, este volta em 256–281 ms.

## Goals / Non-Goals

**Goals:**

- A compactação volta a reduzir o histórico **em todos os provedores
  suportados**, sem depender de o pacote mudar.
- A falha de resumo deixa de ser invisível — para a causa conhecida **e** para
  as que ainda não conhecemos.
- Uma falha de provedor Gemini grava o status HTTP que o SDK já tinha.
- O nível de log de produção deixa de morar no ambiente do servidor.
- Log e tabela passam a casar por campo, não por coincidência de duração.

**Non-Goals:**

- Não mexer na estratégia de compactação do pacote nem trocar a versão dele.
- Não tratar grupos `ToolCall` de forma própria (medido: não precisa).
- Não investigar a anomalia dos turnos 1 e 2.
- Não acrescentar coluna nem contagem de mensagens em `provider_calls`.
- Não corrigir o timeout de conexão das chamadas HTTP de saída — item próprio do
  `02`, do mesmo dia.

## Decisions

### D1 — A correção mora no `CompactionCallChatClient`, e é de payload

**Decisão:** o `CompactionCallChatClient` passa a inspecionar a lista de
mensagens que recebe e, **quando a última for de assistente**, acrescentar uma
mensagem final de usuário antes de delegar. As mensagens do histórico não são
alteradas — nem papel, nem conteúdo, nem ordem.

**Por que aqui.** É a única costura nossa no caminho da compactação: já existe,
já é construída por nós (`AgentExecutionService.cs:408`), já embrulha
exatamente a chamada de resumo e nada mais. Qualquer outro ponto ou é do pacote
(`sealed`), ou é o client compartilhado, que também serve os turnos — e o turno
**não** pode ganhar mensagem nenhuma.

**Condicional, e não incondicional.** Acrescentar sempre é mais simples de ler,
e está errado: quando a porção resumida termina em mensagem de usuário, a
requisição já é válida, e a mensagem extra entraria sem necessidade. O custo de
ler a última mensagem é uma comparação de papel.

**Alternativas recusadas:**

| alternativa | por que não |
|---|---|
| trocar o papel da última mensagem para `user` | falsifica a autoria dentro do prompt de resumo: o modelo passaria a ler a própria resposta como fala do usuário |
| descartar o último grupo de assistente da requisição | perde conteúdo do resumo e quebra a atomicidade do grupo; e o grupo continuaria marcado como excluído pela estratégia, virando buraco no histórico |
| client de resumo dedicado a outro provedor | contraria a Decisão 4 da `apps-workers-resumo-historico-conversa` (reaproveitar o client do agente), acrescenta credencial e custo de outro provedor, e deixa o defeito de pé para quem usar Gemini no turno |
| esperar correção do pacote | o defeito é do **nosso** payload com aquele provedor; nada indica que o pacote considere isso bug dele, e o piloto está no regime ruim hoje |
| distinguir o provedor e aplicar só no Gemini | a checagem por provedor espalha conhecimento de provedor por mais um ponto; e a mensagem final de usuário é válida nos três. Se um dia doer, a condição existe e é de uma linha |

### D2 — O texto da mensagem final: afirmação de fronteira, não segundo comando

**A instrução que a estratégia já manda como `system`** (decompilada de
`SummarizationCompactionStrategy.DefaultSummarizationPrompt`, 1.15.0) é:

```
You are a conversation summarizer. Produce a concise summary of the conversation
that preserves:

- Key facts, decisions, and user preferences
- Important context needed for future turns
- Tool call outcomes and their significance

Omit pleasantries and redundant exchanges. Be factual and brief.
```

**Decisão:** a mensagem acrescentada **não repete o comando de resumir**. Ela
declara o fim do trecho:

```
End of the conversation to summarize.
```

**Por que não "Resuma a conversa acima".** Dois comandos de resumo — um no
`system`, outro no turno final do usuário — competem: o segundo é mais recente e
mais específico em posição, e passa a valer como se refinasse o primeiro, sem
carregar nenhum dos quatro critérios que o `system` lista. O resumo piora
exatamente nos itens que o prompt do pacote existe para preservar. Uma
afirmação de fronteira ocupa a posição de turno de usuário — que é tudo o que o
Gemini exige — sem disputar a instrução.

**Por que em inglês.** O prompt de sistema que ela acompanha é em inglês, do
pacote. Misturar idioma dentro do mesmo prompt é variável a mais no resultado,
e o idioma do resumo produzido segue o da conversa, não o da instrução.

**O que fica registrado no código, ao lado do texto:** que ele é lido pelo
modelo, que o `system` do pacote é quem comanda, e que trocá-lo muda o resumo —
não é string de formatação.

### D3 — `loggerFactory` no `CompactionProvider`, por construtor, e por que isso não custa os 14 harness

**Decisão:** `AgentExecutionService` passa a receber `ILoggerFactory` no
construtor e a repassá-lo ao `CompactionProvider`.

**O contraste com a D2 da `metricas-execucao-coleta` é deliberado.** Lá,
injetar um coletor por construtor foi recusado porque custaria os 14 harness de
teste que registram o serviço, e **quebraria em runtime, não em compilação**.
Aqui a dependência é `ILoggerFactory`: os 14 harness constroem o host com
`Host.CreateApplicationBuilder()`, que registra logging — e o serviço **já**
depende de `ILogger<AgentExecutionService>`, que sai do mesmo registro. Nenhum
harness muda. **É por compilação e pela suíte que se confere**, não por leitura:
se algum harness montar `ServiceCollection` cru, falha na resolução.

**O que a linha passa a dizer** (medido na exploração, com `loggerFactory`
injetado no harness):

```
warn: Microsoft.Agents.AI.Compaction.SummarizationCompactionStrategy[1621922599]
      Summarization failed for 2 groups; restoring excluded groups and
      continuing without compaction. Error: <mensagem do provedor>
```

**O que ainda fica escondido, e é achado a registrar, não a corrigir aqui.**
`ChatClientAgent` é construído em `AgentExecutionService.cs:374` sem
`loggerFactory` **e sem `services`**. Decompilado (1.15.0): o `_logger` do
próprio agente sai do parâmetro `loggerFactory`, mas o middleware empilhado por
`WithDefaultAgentMiddleware` — inclusive `FunctionInvokingChatClient`, que loga
invocação de tool — resolve `ILoggerFactory` do **`services`**, que é outro
parâmetro. Passar só `loggerFactory` acende as linhas do agente e **não** as do
middleware. Esta change passa o `loggerFactory` ao `CompactionProvider`, que é o
que o defeito exige; ligar o log do middleware é decisão de operação (volume),
com gatilho próprio — vai para Open Questions.

### D4 — `HttpStatusOf` casa o tipo derivado antes da base

**Decisão:** acrescentar `Google.GenAI.ClientError` **e** `ServerError` ao
`switch`, **antes** do caso de `HttpRequestException`.

**A ordem não é estilo, é correção.** Os dois derivam de `HttpRequestException`
e declaram `public new int StatusCode`, deixando nula a da base (decompilado,
`Google.GenAI` 1.15.0). Um `switch` de padrões casa o **primeiro** braço
compatível: com `HttpRequestException` na frente, o braço do tipo derivado nunca
é alcançado — e o resultado é o de hoje, `HttpStatus` nulo.

**`ServerError` entra junto**, embora o piloto só tenha produzido `ClientError`.
São a mesma forma pelo mesmo motivo, e deixar metade corrigida é pior que não
corrigir: a próxima consulta acharia que 5xx não acontece.

**`apps/workers` já referencia `Google.GenAI`** (`ChatClientResolver.cs:5`), e o
tipo é público. Nenhuma dependência nova.

**Por que não resolver por reflexão, genericamente.** Ler "qualquer propriedade
chamada `StatusCode` declarada no tipo" pegaria SDKs futuros sem mudar código —
e é exatamente o tipo de acoplamento por nome que a D12 da
`metricas-execucao-coleta` recusou para o motivo da falha. Tipo conhecido,
braço explícito, e o nulo continua dizendo "não sei".

### D5 — Nível de log de produção em `appsettings.Production.json`, um por app

**Decisão:** `appsettings.Production.json` em `apps/workers`, `apps/api` e
`apps/inbox`, cada um com
`Microsoft.EntityFrameworkCore.Database.Command: Warning`. **Não** em
`docker-compose.prod.yml`.

**Por que o arquivo e não o compose.** O compose já carrega o que é de
ambiente — credencial, host, porta. Nível de log é do app: quem lê o
`appsettings` do app vê o que ele faz em produção sem abrir o compose, e o
arquivo acompanha o app se ele for rodado fora daquele compose. O compose
continua podendo sobrepor, e é isso que o torna um lugar ruim para o valor
**canônico**.

**Confirmado, não suposto:** o `Dockerfile` de cada app faz `dotnet publish -o
/app/publish` e copia o diretório inteiro — `appsettings*.json` vai junto, como
conteúdo padrão do SDK. E nenhum dos três define `ASPNETCORE_ENVIRONMENT`/
`DOTNET_ENVIRONMENT`, então o ambiente resolvido é `Production` por default do
host genérico — exatamente o que o log do piloto mostrou
(*"Hosting environment: Production"*).

**O `Default` não sobe para `Warning`.** Escondia a linha de início do detector
de tasks não-terminais (`NonTerminalTaskDetectorService.cs:106`), que é a única
prova de que ele está registrado — e essa linha existe precisamente porque
silêncio ambíguo num instrumento de diagnóstico é o defeito que ele existe para
não ter.

**A configuração do ambiente do piloto é removida no deploy.** Duas fontes do
mesmo valor divergem, e a que está fora do repositório é a que ninguém revisa.

### D6 — As checagens de startup ganham linha de sucesso

**Decisão:** `ValidateEmbeddingBatchSize` e `ValidateEmbeddingIndexConsistency`
passam a logar uma linha em `Information` quando passam, com o valor conferido.

**Por que agora, e não como faxina.** A segunda só se manifestava no log pela
consulta que o EF Core imprimia. **A D5 silencia essa consulta** — sem linha
própria, esta change tornaria uma checagem de boot invisível. É consequência
direta do escopo 4, não item solto.

**A forma é a que esta base já usa:** a linha de início do detector, com o mesmo
argumento escrito ao lado (sucesso indistinguível de ausência).

### D7 — Sucesso e falha em linhas distintas, com `TaskId` e finalidade

**Decisão:** `LlmCallDurationChatClient` passa a escrever duas linhas
diferentes, decididas pelo resultado que o `finally` já conhece:

- sucesso: provedor, modelo, streaming, duração, `TaskId`, finalidade;
- falha: os mesmos campos, mais **tipo** da exceção e status HTTP quando houver.

**A finalidade e a task saem do escopo ambiente, não de parâmetro** — pelo mesmo
motivo que a linha filha das métricas sai dali (D2 da `metricas-execucao-coleta`):
o client é compartilhado por `(provider, model)` e não sabe de quem é a chamada.
`ExecutionMetricsScope` já guarda os dois; hoje a finalidade é `private`, e passa
a ter leitor público. **Fora de execução, os dois campos saem vazios e a linha
continua saindo** — o client é usado fora de execução nos testes dele.

**O status na linha é o mesmo valor que vai para a tabela**, depois da D4 — o que
mantém log e tabela dizendo a mesma coisa, que é o ponto do escopo.

**Consequência declarada:** com esta linha, o `HttpClient` pode ficar em
`Warning` sem perder o status da resposta. **As duas decisões andam juntas** — se
o D7 sair da change, o `HttpClient` volta para `Information`.

### D8 — O guarda da costura afirma o nosso payload, não a aceitação do provedor

O guarda do escopo 1 roda em CI **sem chave de provedor**: ele afirma que a
requisição de compactação nunca sai terminando em mensagem de assistente. Isso é
uma propriedade **do nosso payload**.

**Que o Gemini aceita a requisição assim é evidência de execução real, não do
guarda:** 22/09/2026, 21:15, `America/Sao_Paulo`, Darwin 24.6.0,
`Microsoft.Agents.AI` 1.15.0, `Google.GenAI` 1.15.0, `gemini-3.6-flash`, chave de
**dev** por variável de ambiente. Sem este parágrafo, daqui a seis meses alguém
lê o guarda e supõe que ele prova o fim a fim. **Não prova**, e a ponte entre os
dois é esta medição — convenções 13 e 22.

### D9 — Guardas vermelhos, e onde cada um mora

| guarda | onde | o que prende | vermelho contra `HEAD` por |
|---|---|---|---|
| requisição de compactação não termina em assistente | `ExecutionMetrics/CompactionCallChatClientTests.cs` (novo) | D1 | hoje o wrapper delega a lista intacta |
| a mensagem acrescentada é de usuário, e o histórico não é alterado | idem | D1/D2 | idem |
| lista que já termina em usuário passa intacta | idem | D1 (condicional) | hoje passa intacta — **este fica verde**, e é registrado como guarda de não-regressão |
| patamar: conversa longa com tool, entrada não cresce monotonicamente | `HistorySummarizationTests` | escopo 1 fim a fim | com um duplo que recusa turno final de modelo, hoje a entrada cresce |
| falha de resumo aparece em log | `HistorySummarizationTests` | D3 | hoje o aviso vai para `NullLogger` |
| `ClientError` com `new StatusCode` grava `HttpStatus` | `ExecutionMetrics/ExecutionMetricsScopeTests` | D4 | hoje grava nulo |
| linha de falha distinta da de sucesso, com task e finalidade | `Agents/LlmCallDurationChatClientTests` | D7 | hoje é a mesma linha |
| checagens de startup logam ao passar | `Knowledge/…ValidationTests` | D6 | hoje não logam |

**A medida do guarda de patamar conta TODO o conteúdo, não só texto.** Na
exploração, somar apenas `ChatMessage.Text` deixou `FunctionCallContent` e
`FunctionResultContent` de fora, e os dois cenários — compactação funcionando e
compactação falhando — deram **185–189 tokens idênticos**. Com a contagem
corrigida, a diferença apareceu: patamar em ~3.570 contra crescimento monótono
até 4.082 em 40 turnos. **Isso vai escrito ao lado do guarda**: um teste que meça
só texto não distingue os dois comportamentos.

## Árvore de pastas

Só o que esta change toca. `(novo)` marca arquivo que não existe hoje.

```
apps/
  api/src/Buteco.Api/
    appsettings.Production.json                                    (novo)
  inbox/src/Buteco.Inbox/
    appsettings.Production.json                                    (novo)
  workers/
    src/Buteco.Workers/
      appsettings.Production.json                                  (novo)
      Agents/
        AgentExecutionService.cs           (ILoggerFactory por construtor; repassa ao CompactionProvider)
        LlmCallDurationChatClient.cs       (sucesso × falha, TaskId e finalidade)
      ExecutionMetrics/
        CompactionCallChatClient.cs        (mensagem final de usuário quando a última for de assistente)
        ExecutionMetricsScope.cs           (HttpStatusOf lê ClientError/ServerError; leitor público da finalidade)
      Knowledge/Indexing/
        EmbeddingBatchSizeValidation.cs    (linha de sucesso)
        EmbeddingIndexConsistencyValidation.cs (linha de sucesso)
    tests/Buteco.Workers.Tests/
      ExecutionMetrics/
        CompactionCallChatClientTests.cs                           (novo)
        ExecutionMetricsScopeTests.cs                              (casos de status)
      Agents/
        LlmCallDurationChatClientTests.cs                          (linhas distintas)
      Knowledge/
        EmbeddingStartupValidationLogTests.cs                      (novo)
      HistorySummarizationTests.cs                                 (patamar e log de falha)
```

Nada em `libs/`, nada em `apps/frontend`, nenhuma migração.

## Projeção (convenção 18) — décima segunda medição

Feita **depois** de fechar a verificação e **antes** de código. O fechamento só
compara.

**Blast radius de assinatura:** uma. `AgentExecutionService` ganha um parâmetro
de construtor (`ILoggerFactory`) — resolvido por DI nos 14 harness, que **não**
mudam. `ExecutionMetricsScope` ganha um membro público de leitura. Nenhuma
assinatura pública existente muda. **Confere-se compilando todos os `.csproj`** —
não há `.sln` na raiz.

**Unidades públicas** (a régua acertou quatro vezes seguidas — mantida):

| unidade | quantas |
|---|---|
| tipo novo em produção | **0** |
| tipo novo em teste | 2 (`CompactionCallChatClientTests`, `EmbeddingStartupValidationLogTests`) |
| membro público novo em tipo existente | **1** (leitor da finalidade corrente) |
| assinatura existente alterada | **1** (construtor de `AgentExecutionService`) |
| arquivo de configuração novo | 3 (`appsettings.Production.json`) |

**Arquivos, criados e modificados separados, em pares com o teste.**
Modificado se mede com `git diff -w` — sem isso, reindentação inflou um arquivo
de 86 para 514 linhas numa medição anterior da série.

| | arquivos | linhas projetadas |
|---|---|---|
| criados (produção) | 3 (configuração) | ~30 |
| modificados (produção, à mão) | 6 | ~190 |
| criados (teste) | 2 | ~230 |
| modificados (teste) | 3 | ~190 |
| registro (`02`, `CHANGELOG`) | 2 | ~120 |
| **total à mão, ex-`openspec/`** | **16** | **~760** (faixa 650–900) |

**Comentário é produto — em produção E em teste.** Os registros classificados
antes de multiplicar, com os três tipos de custo da série (evidência numérica
~14; contrafactual ~12 a ~31; medição com regime colado, o mais caro):

| registro | tipo | custo |
|---|---|---|
| por que a correção é de payload e mora no wrapper (as quatro recusadas) | contrafactual | ~31 |
| por que condicional e não incondicional | contrafactual | ~12 |
| o texto da mensagem: por que não é segundo comando, por que em inglês | contrafactual | ~31 |
| por que `ILoggerFactory` por construtor não custa os 14 harness | contrafactual | ~31 |
| por que o derivado antes da base no `switch` | contrafactual | ~31 |
| por que `appsettings.Production.json` e não compose | contrafactual | ~31 |
| por que o `Default` não sobe | evidência com linha (detector `:106`) | ~14 |
| por que a checagem de índice ganha linha agora | evidência | ~14 |
| `HttpClient` em `Warning` anda junto com a linha de falha | evidência | ~14 |
| **a reprodução contra o Gemini, com regime colado** (D8) | **medição** | **~26** |
| **em teste:** por que medir todo o conteúdo e não só texto | medição (cita os 185–189) | ~20 |
| **em teste:** por que o duplo recusa turno final de modelo | contrafactual | ~12 |

~267 de registro contra ~380 de lógica em produção: **~0,7:1** — abaixo das
changes de puro registro (2,3:1 e 3,15:1) e abaixo da última (1:1), porque aqui
a lógica é pouca e o que pesa é a evidência. **Uma medição com regime colado em
produção** — a primeira da série a ter.

**Cenários, contados pelos estados observáveis das deltas de spec — e a unidade
de entrega do xUnit é o CASO, não o método:**

| classe | casos novos |
|---|---|
| `CompactionCallChatClientTests` | **4** (termina em assistente; termina em usuário; histórico intacto; finalidade continua marcada) |
| `ExecutionMetricsScopeTests` | 1 `[Theory]` de 3 casos (`ClientError`, `ServerError`, exceção sem status) = **3** |
| `LlmCallDurationChatClientTests` | **4** (sucesso com task e finalidade; falha com tipo e status; finalidade de compactação; fora de execução) |
| `EmbeddingStartupValidationLogTests` | **3** (lote passa e loga; índice passa e loga; falha continua falhando) |
| `HistorySummarizationTests` | **2** (patamar com provedor que recusa turno final de modelo; falha de resumo em log) |
| **`apps/workers`** | **+16** |

**Os 16 são CASOS, não métodos** — é o número que a suíte devolve, e é com ele
que a 7.4 compara. São **14 métodos**: 13 `[Fact]` mais **uma** `[Theory]`
(`ExecutionMetricsScopeTests`, os três status), que sozinha rende **3** casos.
Nenhuma outra `[Theory]` está prevista; se o apply acrescentar uma, a projeção
muda de número **e** a diferença é explicada, não reprojetada. A décima primeira
medição errou exatamente aqui: duas `[Theory]` renderam 6 casos onde a projeção
tinha contado 2.

**Suíte, com o regime colado.** A baseline é **a medir na tarefa 1.1**, na
worktree limpa, e não herdada de leitura: o último número registrado para
`apps/workers` é a projeção **319/319** da `metricas-execucao-coleta`, e durante
o apply dela a mesma suíte chegou a dar 317/319 antes do escopo 2 — número de
suíte nunca sai de memória. `apps/api` e `apps/inbox` **não têm código tocado**;
rodam mesmo assim, porque ganham `appsettings.Production.json` e a régua é a
suíte inteira. Projeção: `apps/workers` **baseline + 16**, **mesmo número de
classes** na `WorkerHostCollection`; `apps/api` e `apps/inbox` **inalteradas**.
O `+16` é em casos, pela mesma unidade do parágrafo acima.

## Risks / Trade-offs

- **A mensagem acrescentada entra no prompt do resumo.** → O texto é afirmação
  de fronteira, não comando (D2), e o registro ao lado dele diz que trocá-lo muda
  o resumo. O guarda de patamar mede tamanho, não qualidade — a qualidade do
  resumo continua sem medida automática, como já estava.
- **O guarda da costura não prova o fim a fim** (D8). → A ponte é a medição com
  regime colado, escrita no código do guarda e aqui.
- **Um provedor futuro pode recusar outra coisa.** → A correção é de forma, não
  de provedor; a linha de aviso do D3 é o que faz a próxima recusa aparecer no
  primeiro dia em vez do terceiro.
- **`appsettings.Production.json` silencia a consulta do EF em produção.** →
  Compensado pelo D6 nas duas checagens que dependiam dela. Se outra coisa
  dependia da mesma consulta, some junto — a tarefa de conferência procura por
  isso antes.
- **O log do middleware do agente continua apagado** (D3). → Registrado como
  achado com gatilho, não corrigido aqui: ligá-lo é decisão de volume de log.
- **A suíte de `apps/workers` exige Podman com `DOCKER_HOST` e Ryuk desligado
  nesta máquina.** → Está no regime da tarefa de verificação; sem isso a suíte
  de integração falha inteira e o número não vale.

## Migration Plan

Sem migração de banco e sem mudança de contrato. A ordem é a dos guardas
vermelhos (convenção 15): guarda → correção → guarda verde, escopo a escopo.

**No deploy:**

1. Subir a imagem com os três `appsettings.Production.json`.
2. **Remover a configuração de log do ambiente do piloto** — é o passo que não
   pode ficar para depois: **variável de ambiente vence `appsettings`** na
   precedência do `ConfigurationBuilder`, então enquanto ela existir o valor
   versionado não é o efetivo, e a spec afirmaria algo que não vale no único
   ambiente que importa. **Ausência de variável não deixa rastro** — sem a
   conferência do passo 3, a única prova de que a fonte única funcionou seria
   alguém lembrar de ter apagado.
3. Conferir no log do boot, no par que a tarefa 5.4 já faz localmente: o log de
   comando do EF **não aparece**, e a linha de início do detector **continua
   aparecendo**, com janela e intervalo — é ela que prova que o `Default` não
   subiu junto. Registrar com o regime colado (data, hora, fuso, instância).
   **Este passo é pós-deploy e NÃO fecha a change:** fechar significa "o valor
   está versionado e os guardas passam", nunca "o piloto está lendo o valor
   versionado". Vai para "Itens em aberto" do `02` (tarefa 8.2), com gatilho — o
   primeiro deploy com esta change — e posição — a janela desse deploy. Mesmo
   tratamento que a verificação de campo da `indexacao-lote-de-fragmentos`
   recebeu.
4. Confirmar o efeito no dado, não no log: numa conversa que passe de dez turnos,
   `provider_calls` com `Purpose = Compaction` passa a ter `Failed = false`, e a
   entrada do turno seguinte para de crescer. **Registrar a data e o fuso** da
   primeira conversa medida nesse regime — a série de métricas se divide ali.

**Rollback:** reverter a imagem. Nada a desfazer no banco. A configuração de log
do ambiente pode ser recolocada, se for preciso, sem depender do código.

## Open Questions

1. **Ligar o log do middleware do agente (`services` no `ChatClientAgent`)?**
   Acenderia as linhas de invocação de tool do `FunctionInvokingChatClient`.
   **Gatilho:** a próxima investigação de tool que dependa de saber qual foi
   chamada e com quê. **Posição:** depois desta change, junto da decisão de
   volume de log — não antes de haver um caso que peça.
2. **Qual o texto que o operador vê quando o resumo falha.** Hoje a linha é do
   pacote, em inglês, com o `EventId` dele. Envolvê-la numa linha nossa, em
   pt-BR, custa um wrapper de `ILoggerFactory`. **Gatilho:** a primeira vez que
   alguém de operação precisar agir por essa linha. **Posição:** só se acontecer.
3. **Coluna de categoria de exceção em `provider_calls`.** Item aberto da linha
   de métricas. **Gatilho:** reavaliar **depois** do escopo 3 — com o status do
   Gemini gravado, ela pode ter perdido a razão de existir. **Posição:** linha de
   métricas, junto da contagem de mensagens por requisição.
