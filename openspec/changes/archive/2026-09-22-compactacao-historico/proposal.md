## Why

A compactação do histórico da conversa **nunca funciona com Gemini**, e o piloto
é Gemini: **toda conversa que passa de dez turnos está nesse regime**. A chamada
de resumo é recusada com `400`, a estratégia do pacote engole a exceção, e o
turno segue com o histórico cru — que cresce **~300 a 500 tokens por turno** e
não para. As duas conversas do piloto chegaram a 5.405 e 14.520 tokens de
entrada. Seis chamadas de compactação, seis falhas.

A exceção, reproduzida contra o Gemini real em **22/09/2026, 21:15,
`America/Sao_Paulo`** (Darwin 24.6.0, `Microsoft.Agents.AI` 1.15.0,
`Google.GenAI` 1.15.0, `gemini-3.6-flash`, **chave de dev por variável de
ambiente, não a do piloto**):

```
tipo=Google.GenAI.ClientError   statusDeclarado=400   baseStatus=null
mensagem: "Requests ending with a model turn are not supported."
duração: 256 ms (T11) e 281 ms (T12)
```

**Falha sempre, e não às vezes.** O payload da compactação termina sempre em
turno de modelo, por construção: o `TurnIndex` é copiado para os grupos de
assistente (`CompactionMessageIndex.cs:205,211`) e `IncludedTurnCount` conta
índices distintos entre os grupos não excluídos (`:43-45`), então excluir só a
mensagem de usuário de um turno não derruba a contagem — o laço precisa excluir
o grupo de assistente também, e para logo depois dele. As duas capturas
confirmam: `[s,u,a]` no T11 e `[s,u,a,u,a]` no T12. **O defeito é do par (nosso
payload, Gemini)**, não da compactação: OpenAI e Anthropic aceitam mensagem
final de assistente.

**E a falha é invisível por construção**, não por nível de log: a estratégia
captura e engole a exceção
(`SummarizationCompactionStrategy.CompactCoreAsync:162-175`) e avisa num
`NullLoggerFactory`, porque o `CompactionProvider` é construído sem
`loggerFactory` (`AgentExecutionService.cs:408`). Este diagnóstico custou duas
rodadas de exploração, um harness e uma chave de dev — a próxima falha de
resumo, de qualquer natureza, seria igualmente invisível.

## Escopos desta change

**Cinco, declarados separadamente.** O 1 é o defeito de produto; os outros
quatro são o que o diagnóstico mostrou faltar para que o próximo custe menos.

| escopo | o que é | por que está aqui |
|---|---|---|
| **1** | a requisição de compactação deixa de terminar em turno de modelo | é o defeito: sem isso, a compactação não funciona com Gemini |
| **2** | a falha de resumo passa a aparecer em log | a falha foi invisível **por construção**; o escopo 1 conserta o caso conhecido, este conserta a classe |
| **3** | `HttpStatusOf` passa a ler o status das exceções do SDK do Gemini | **defeito da `metricas-execucao-coleta`** (convenção 9): o `400` estava disponível o tempo todo e foi descartado |
| **4** | nível de log de produção, versionado, e linha própria para as checagens de startup silenciosas | o nível do piloto foi ajustado **no ambiente**, fora do repositório — duas fontes do mesmo valor divergem |
| **5** | a linha do cliente de LLM carrega `TaskId` e finalidade, e separa sucesso de falha | casar log com tabela dependeu de as durações serem únicas; a próxima vez pode não dar |

## What Changes

Tudo em `apps/workers`, exceto a conferência de configuração de log, que toca
também `apps/api` e `apps/inbox`. **Nenhuma migração, nenhuma rota, nenhuma
tela.**

- **`CompactionCallChatClient` passa a garantir que a requisição de resumo não
  termine em mensagem de assistente**, acrescentando uma mensagem final de
  usuário quando a última for de assistente. A costura já é nossa — hoje o tipo
  só marca a finalidade num `AsyncLocal` — e a correção não depende de o pacote
  mudar. O texto dessa mensagem é decisão do `design.md`: ele entra no prompt do
  resumo, ao lado da instrução `system` que a estratégia já manda, e dois
  comandos de resumo conflitantes produzem resumo pior que um.
- **`CompactionProvider` passa a receber `loggerFactory`**, e o
  `AgentExecutionService` passa a receber `ILoggerFactory` por construtor — a
  dependência que `Host.CreateApplicationBuilder` registra sempre, então os 14
  harness de teste que registram o serviço não mudam (a conferir por
  compilação e pela suíte).
- **`ExecutionMetricsScope.HttpStatusOf` passa a ler `Google.GenAI.ClientError`
  e `ServerError`**, que derivam de `HttpRequestException` e declaram
  `public new int StatusCode`, deixando nula a propriedade da base. Correção de
  um `switch`, sem migração. **Com ela, este diagnóstico inteiro teria saído de
  uma consulta.**
- **`Microsoft.EntityFrameworkCore.Database.Command` em `Warning` em produção**,
  em arquivo versionado, com a configuração duplicada do ambiente do piloto
  removida. **O `Default` não sobe** — esconderia a linha de início do detector
  de tasks não-terminais, a única prova de que ele está registrado.
- **`ValidateEmbeddingBatchSize` e `ValidateEmbeddingIndexConsistency` passam a
  registrar uma linha de sucesso.** A segunda só aparecia no log pela consulta
  que o EF imprimia, e o item acima remove essa consulta — sem linha própria,
  ela passaria a rodar invisível. Sucesso indistinguível de ausência é a mesma
  forma que o detector resolveu com a linha de início.
- **`LlmCallDurationChatClient` passa a escrever linhas distintas para sucesso e
  falha**, cada uma com `TaskId` e finalidade (`Turn` × `Compaction`), e a de
  falha com o tipo da exceção e o status HTTP quando houver. Hoje o `finally`
  escreve a mesma linha para os dois casos.
- **Decisão sobre o `HttpClient`, registrada:** fica em `Warning`, porque a
  linha de falha acima passa a carregar status e tipo. **As duas andam juntas** —
  se o escopo 5 sair, o `HttpClient` volta para `Information`.

## Capabilities

### New Capabilities

Nenhuma. A change corrige comportamento de capabilities existentes.

### Modified Capabilities

- `a2a-task-lifecycle`: o resumo do histórico passa a ser montado de forma que
  **todos** os provedores suportados aceitem — a requisição de resumo nunca
  termina em mensagem de assistente —, e a falha de resumo passa a ser
  registrada em log em vez de silenciosa. A linha de duração da requisição ao
  provedor passa a distinguir sucesso de falha e a identificar a task e a
  finalidade da chamada (requisitos acrescentados e um modificado).
- `agent-execution-metrics`: `HttpStatus` passa a ser gravado também quando a
  exceção do SDK declara o status em propriedade própria que oculta a da base
  (requisito modificado).
- `server-deployment`: o nível de log de produção passa a viver em arquivo
  versionado, com fonte única (requisito acrescentado).
- `workers-scaffold`: as checagens de startup que hoje passam em silêncio
  passam a registrar uma linha (requisito acrescentado).

## Impact

- **`apps/workers`** (produção): `ExecutionMetrics/CompactionCallChatClient.cs`,
  `ExecutionMetrics/ExecutionMetricsScope.cs`,
  `Agents/AgentExecutionService.cs`, `Agents/LlmCallDurationChatClient.cs`,
  `Knowledge/Indexing/EmbeddingBatchSizeValidation.cs`,
  `Knowledge/Indexing/EmbeddingIndexConsistencyValidation.cs`, e
  `appsettings.Production.json` (novo).
- **`apps/workers`** (teste): guardas novos em
  `ExecutionMetrics/`, `Agents/LlmCallDurationChatClientTests.cs` e
  `HistorySummarizationTests.cs`.
- **`apps/api` e `apps/inbox`:** só `appsettings.Production.json`, pelo mesmo
  motivo (os dois usam EF). **Nenhum código.**
- **`apps/frontend`, `libs/`:** nada.
- **Banco:** nada. Nenhuma migração, nenhuma coluna.
- **Operação:** a configuração de log do ambiente do piloto é **removida** no
  deploy — se ficar, duas fontes do mesmo valor divergem. Nenhuma variável de
  ambiente nova.
- **Dependência externa:** nenhuma versão de pacote muda. A correção do escopo 1
  é nossa justamente para não depender de `Microsoft.Agents.AI` mudar.

### Non-Goals explícitos

- **Não investigar a anomalia dos turnos 1 e 2** — item aberto, com cinco
  candidatos já eliminados por consulta e por reprodução.
- Não acrescentar coluna de categoria de exceção em `provider_calls` — item
  aberto, a reavaliar **depois** do escopo 3, que pode torná-la desnecessária.
- Não acrescentar contagem de mensagens por requisição em `provider_calls` —
  item aberto.
- Não tratar grupos `ToolCall` de forma própria: medido em 40 turnos que, com o
  resumo funcionando, eles são excluídos e resumidos como unidade e o patamar se
  segura.
- Não mexer em número de instâncias de `apps/workers`, na varredura de
  `PendingDispatch`, nem nas etapas seguintes da linha de métricas.
- Não corrigir a `libgssapi_krb5` da imagem.
