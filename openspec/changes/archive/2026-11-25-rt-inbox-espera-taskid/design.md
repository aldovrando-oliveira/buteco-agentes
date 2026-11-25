## Context

### O mecanismo

O registro de despacho do `apps/inbox` (`PendingDispatch`) tem três momentos:

1. **Nasce no recebimento.** `ReceiveMessageAsync` cria o registro e grava (`InboundMessageOrchestrator.cs:58-63`) antes de voltar. Todos os casos do `rt-inbox` aguardam essa chamada antes de esperar qualquer coisa.
2. **Ganha o `taskId`** quando o `SendMessage` responde `Submitted` (`DebounceSweepService.cs:338`).
3. **Some** quando o push fecha o despacho (`DispatchOutcomeProcessor.CloseAsync`), ou quando a reconciliação o resolve pelo `GetTask`.

O `taskId` no registro existe só entre o 2 e o 3. Os casos esperam por ele com `PollUntilAsync`, a cada 100 ms e com três buscas por consulta. Com o push livre e o LLM mockado sem atraso, o intervalo entre o 2 e o 3 cabe dentro de uma consulta.

### Medidas

Todas de 24/11/2026, sobre `4e44dd3`, uma rodada por vez sob o `suites.lock`. As saídas estão em `~/.cache/buteco-agents/296-investigacao-4e44dd3/`.

| medida | resultado |
|---|---|
| janela do `taskId` da sentinela, com sondas (`eventos/`) | 46, 140 e 127 ms; com sondas, 0 reprovações em 2 rodadas, contra 4 em 4 sem elas: as sondas mudam o tempo |
| **A**: a branch da #296, com só a espera da sentinela a cada 5 ms (`tempo/A-*`) | 4 válidas, 4 aprovadas 16/16, CPU mínima de 39 a 62 |
| **B**: a `main` com `Task.Delay` de 4 e 8 ms logo antes da conferência da chave no `EnqueueingAgentHandler` (`tempo/B*-*`) | 16 tentativas, 2 válidas (aprovadas), 14 inconclusivas por CPU abaixo de 100; nas 14, caem só os casos que esperam o `taskId` com o push livre. **Indício, não medido pela regra** |
| a `main` sem atraso (`serie/baseline-*`) | 4 aprovadas, CPU mínima de 25 a 79 |

Quedas no B, por caso, nas tentativas de 4 ms e de 8 ms:

- `TerminalTask(chatFails: True)`: 1 e 8;
- `Source3`, a sentinela: 6 e 6;
- `PushNotificationReceived…`: 1 e 6;
- `TaskCarriesChannelType…`: 0 e 6;
- `TheInboxTaskHasNoOwner`: 2 e 5;
- `Question2`: 1 e 1.

Nunca caíram os `Source2_*`, a conversa principal do `Source3` e a `SessionSerialization`.

### O universo, enumerado antes de filtrar (convenção 25)

- **Critério largo:** toda consulta ao registro de despacho nos 16 casos do projeto.
- **Critério estreito:** quem chama `WaitForTaskIdAsync`. Dá 7 casos, todos do `OrphanDispatch`.
- **O largo achou 5 a mais:** os 4 do `RoundTripTests`, via `GetPendingDispatchTaskIdAsync`, e a `SessionSerialization`, via `FindDispatchesAsync`.
  - Dois deles caíram no B: `TheInboxTaskHasNoOwner` e `TaskCarriesChannelType…`.
- **Fora:**
  - a espera pela ausência do registro, que é estado final;
  - `ServiceToken.S2`, que só espera a ausência;
  - `S1`, `ADivergentSecret`, `IssuerToken` e `Role`, que não consultam o registro.

| # | caso (arquivo:linha da espera) | janela | uso do `taskId` depois |
|---|---|---|---|
| 1 | `OrphanDispatch.Source2_PushFails_TaskCompleted` (:43) | push recusado; fecha pela reconciliação, ≥ ~1 s | `GetTaskState` = COMPLETED |
| 2 | `OrphanDispatch.Question2_WorkerFailurePath` (:69) | push livre | `GetTaskState` = FAILED |
| 3 | `OrphanDispatch.Source3`, conversa principal (:94) | push retido até o reinício | `GetTaskState` antes do reinício e no fim |
| 4 | `OrphanDispatch.Source3`, sentinela (:112) | push livre | nenhum |
| 5 | `OrphanDispatch.Source2_PushFails_TaskFailed` (:130) | push recusado, ≥ ~1 s | `GetTaskState` = FAILED |
| 6 | `OrphanDispatch.TerminalTask(chatFails: False)` (:154) | push livre | `GetRawStatus` |
| 7 | `OrphanDispatch.TerminalTask(chatFails: True)` (:154) | push livre, o mais curto | `GetRawStatus` |
| 8 | `RoundTrip.PushNotificationReceivedWithCorrectPayload` (:56) | push livre | `GetTask`, estado e artefato |
| 9 | `RoundTrip.TaskCarriesMessageInstant…` (:121) | push livre | `messageInstant` bruto |
| 10 | `RoundTrip.TheInboxTaskHasNoOwner` (:170) | push livre | estado e `a2a_tasks.ApplicationId` por `TaskId` |
| 11 | `RoundTrip.TaskCarriesChannelTypeAndContactExternalId…` (:217) | push livre | `channelType` e `contactExternalId` brutos |
| 12 | `SessionSerialization.SecondMessage…` (:65 e :80) | LLM de 8 s | o 1º para esperar `WORKING` antes de subir a 2ª instância; o 2º pelo id diferente; os estados finais |

## Goals / Non-Goals

**Goals:**

- Nenhum caso do `rt-inbox` condiciona o progresso ao `taskId` no registro de despacho.
- Cada caso espera primeiro uma condição positiva e durável e só depois afirma a ausência do registro.
- Os guardas G1, G2 e G3 reprovam contra o defeito que nomeiam (convenção 15).
- O critério para fechar a #176 fica escrito (D6).

**Non-Goals:**

- Nenhum código de produção. O `taskId` transitório é o contrato do inbox, não um defeito.
- O `RoundTripFixture` e o N = 1.
- O erro de canal do RabbitMQ ao reiniciar o worker (19/11/2026). Ele fica na #176.
- Reduzir o intervalo de consulta. É a alternativa (b), recusada na D1.

## Decisions

### D0. Antes da (c): nada apaga a linha de `a2a_tasks` nesse fluxo (tarefa 0.1, com parada)

As decisões D2 e D4 leem o `taskId` de `a2a_tasks` porque a linha nasce no `Submit` (`PostgresTaskStore.SaveTaskAsync`, `apps/api`) e não some.

Nenhum código do repositório chama `PostgresTaskStore.DeleteTaskAsync` do `apps/api` nem a do `apps/workers`; o `grep` de 24/11/2026 achou só as definições. Falta conferir que o SDK A2A (`A2AServer`, `TaskUpdater`, `AgentEventQueue`) não a chama durante um despacho, nem no reinício do worker do caso 3.

**Se a tarefa 0.1 achar uma chamada nesse fluxo, a change para e volta ao dono**, antes de fixar a (c) e a D2.

### D1. Condição positiva e durável primeiro, a ausência depois (casos 1, 2 e 4–11)

Cada caso troca a espera do `taskId` no registro por duas esperas, nesta ordem:

1. **a condição positiva e durável:**
   - casos 1, 2 e 5–11: a task do agente do caso em estado final em `a2a_tasks`, pelo mesmo `IsTerminal()` do SDK que a reconciliação usa (`DispatchReconciliationService.cs:176`). Com mais de uma task do agente, o caso reprova com a lista (D2);
   - caso 4, a sentinela: a resposta entregue (D3);
2. **a ausência do registro de despacho**, como hoje, com o mesmo prazo.

Depois disso, o caso lê o `taskId` de `a2a_tasks` (D2) e segue com as mesmas asserções.

**Por que a ordem importa, e o que ela protege hoje** (decisão do dono, 24/11/2026):

- sem a condição positiva, "o registro não existe" também vale antes de ele ser criado, e o caso seguiria com a task ainda nem criada;
- **mas no código de hoje o registro nasce dentro do `ReceiveMessageAsync` que o caso aguarda** (Context, item 1), então a ausência só vale depois do fechamento;
- a ordem protege contra uma criação que deixe de ser síncrona, como um recebimento enfileirado. Daí o desenho do G2 (D5).

**Alternativas recusadas:**

- **(b) só reduzir o intervalo de consulta:** ainda pode perder a janela.
  - Nada garante um mínimo para ela, porque quem a fecha é o worker mais o push, em outros processos.
  - Uma consulta são três buscas num escopo novo, e com a CPU estrangulada pode levar mais que a janela.
  - Na change-1 da #296 o worker pegou o job 2 ms antes de o inbox gravar o `taskId`.
  - `Task.Delay` tem resolução de ~15 ms no Windows.
  - O A mostra que a perda diminui, não que some.
- **(d) reter o push em todos os casos:** muda o caminho que eles exercitam.

### D2. Ler o `taskId` de `a2a_tasks` pelo agente do caso, e a segunda task, caso a caso

Cada caso cria o próprio agente (`CreateAgentAsync`). No `Source3`, a conversa principal e a sentinela têm agentes distintos. Isso permite ler as tasks por `AgentId`. Antes de escrever `Assert.Single`, foi conferido de onde poderia nascer uma segunda task do mesmo agente:

| fonte | onde | cria task? |
|---|---|---|
| reconciliação | `DispatchReconciliationService`: só `GetTaskAsync` (:160) e `FailAsync` (:247), "sem redisparo (D7)" (:238) | **não** |
| reentrega do job (reinício do worker, caso 3) | a mensagem devolvida à fila carrega o mesmo `TaskId` (`TaskJobMessage`, `EnqueueingAgentHandler`) | **não**, é a mesma linha |
| continuação de conversa | só o caso 12 manda duas mensagens ao mesmo agente | **sim**, só no 12 |
| reintentativa de transporte do `SendMessage` | `DebounceSweepService.cs:221` e `IsTransportFailure` (:396): `HttpRequestException` ou o timeout do cliente A2A, de 5 s (`apps/inbox/Program.cs:97-98`) | **pode, em todos os casos**: se o api grava a task no `Submit` e a resposta passa de 5 s, o inbox reenvia e nasce outra no mesmo contexto. Nenhum caso a provoca. O handler mediu ~25 ms. Com a CPU estrangulada, não medido |

Resultado por caso:

| # | segunda task possível por | critério |
|---|---|---|
| 1, 2, 5 | reintentativa | `Assert.Single`, com a lista |
| 3 | reintentativa (a reentrega usa a mesma linha) | `Assert.Single`, com a lista, na leitura da D4 |
| 4 | não lê task | — |
| 6–11 | reintentativa | `Assert.Single`, com a lista |
| 12 | continuação **e** reintentativa | o do caso 12: a 2ª é a linha com `TaskId` diferente da 1ª (D4) |

**O critério: `Assert.Single`, com a lista das tasks do agente na mensagem** (decisão do dono, 24/11/2026, questão aberta 1).

- Nos casos de uma mensagem, nenhum dado durável distingue duas tasks do mesmo pedido: só o registro transitório sabe qual o inbox guardou. Escolher uma seria afirmar sem ter lido.
- Por isso o caso afirma que **há uma só**. Se houver mais de uma, reprova com o id e o estado de cada task do agente na mensagem.
- Uma segunda task é um achado, e não ruído do teste: o caminho da reintentativa no código é a #314, fora desta change (convenção 23).
- No caso 12, que tem duas tasks por desenho, vale o critério da D4.
- **Recusada:** "cada task do agente em estado final", com a abandonada só registrada na saída. Ela aprovaria com uma segunda task que ninguém olhou.

### D3. A sentinela: a resposta entregue, sem o `taskId`

A sentinela (caso 4, `:112`) existe para provar que o consumo serializado já reentregou a mensagem devolvida no reinício. Para isso ela precisa ter passado pelo worker. A espera do `taskId` sai, e entram:

1. a condição positiva: uma mensagem de saída da conversa da sentinela com `DeliveryStatus.Sent`. É durável, seja a resposta ou o aviso de falha. O canal dela cifra a credencial de verdade (`CreateChannelAsync`), então a entrega grava `Sent`;
2. a asserção: o conteúdo é `RoundTripFixture.MockedAgentReplyText`;
3. a ausência do registro (`:113`), como hoje.

**Corrigido no apply (24/11/2026, convenção 9):** a primeira forma esperava diretamente a resposta. Com a sentinela recusada, isso virava um tempo esgotado de 20 s, sujeito à regra da CPU. Esperar qualquer saída entregue e afirmar o conteúdo faz a recusa reprovar na asserção, com o aviso de falha na mensagem (G4).

**Sem a condição 1**, uma sentinela recusada sem task (o `FailAsync` da resposta não `Submitted`) também fecharia o registro, e o caso seguiria sem a garantia de ordem. A asserção nova é acréscimo de cobertura.

### D4. A (c) nos casos 3 e 12: consultar `a2a_tasks` até a linha aparecer

- **Caso 3, a conversa principal.**
  - O push está retido (`HoldUntilCancelled`), e o caso precisa do `taskId` antes do reinício.
  - Consulta as tasks do agente até existir uma.
  - Espera o `PushGate.Entered`, como hoje.
  - Afirma `TASK_STATE_COMPLETED` pela API, como hoje.
  - Com mais de uma task do agente, reprova com a lista (id e estado), pelo critério da D2.
- **Caso 12.**
  - **A 1ª task:** consulta até existir exatamente uma linha do agente. A `msg2` ainda não foi enviada.
  - **A 2ª:** depois da `msg2`, consulta até existir uma linha com `TaskId` diferente da 1ª.
  - **O que muda e o que não muda:** a espera do `WORKING`, a subida da segunda instância e as asserções finais continuam iguais. Muda só de onde vem o id.
  - Uma reintentativa da `msg1`, antes do 1º id, deixaria duas linhas. O caso reprova com a lista, e não escolhe uma.

A linha nasce no `Submit`, antes até de o inbox gravar o `taskId`, e não some (D0). Por isso esta leitura não tem janela.

**Corrigido no apply (24/11/2026, convenção 9), pelo G3 e pelo G2.** A primeira forma da D4 lia a task assim que existia uma linha do agente. O G3 mostrou que isso escolhe a task errada: a tentativa abandonada nasce primeiro, em `Submitted`, e a reintentativa só depois. No caso 3, o `Single` viu uma linha só, a abandonada, e o caso reprovou com `TASK_STATE_SUBMITTED` em vez da lista. As duas leituras passaram a esperar uma linha que a abandonada não satisfaz:

- **caso 3:** lê as tasks **depois** do `PushGate.Entered`. O push só é chamado depois da gravação terminal, então a task que terminou já existe, e a abandonada também, se houver;
- **caso 12:** espera uma task do agente **fora de `Submitted`** (`Working` ou estado final), a 1ª e depois a 2ª. A abandonada nasce antes da que trabalha, então o `Single` vê as duas.

O G2 verde mostrou outro ponto: a espera da resposta da sentinela (D3) usava o `GetMessagesAsync`, que faz `SingleAsync` no contato e lança quando ele ainda não existe. A consulta passou a devolver falso sem contato. Nenhuma asserção mudou.

### D5. Os guardas

**A validade das pontas vermelhas (decisão do dono, 24/11/2026).** A reprovação de G2, G3 e G4 está na asserção e na mensagem esperada (`Single` com a lista, a resposta trocada pelo aviso de falha), não em tempo esgotado. Por isso conta como determinística, com qualquer CPU, e a mensagem de cada caso fica registrada. Reprovação por tempo esgotado continua sujeita à regra da CPU. O G1 é por tempo e seguiu a regra.

Todos seguem a convenção 15: só contam depois de reprovar contra o defeito que nomeiam. Rodam pelo `guarda-309.sh`, cópia do `guarda-296.sh`, com o mesmo lock. A mutação toca só o trecho nomeado. A árvore volta pela cópia, com o `sha256` conferido.

- **G1, o atraso de 8 ms**, numa cópia descartável de cada ponta. É a mutação do experimento B: `Task.Delay(8)` logo antes da conferência da chave no `EnqueueingAgentHandler`.
  - **Vermelho:** a ponta da baseline com o atraso reprova os casos expostos, na linha da espera do `taskId`.
  - **Verde:** a ponta da change com o atraso passa 16/16.
  - Os dois lados precisam de rodada válida: a reprovação só conta com a CPU em 100 do início ao fim.
  - Para aumentar a chance, a ponta vermelha roda as duas classes expostas sozinhas pelo `teste-309.sh`, que é mais curto.
  - **Como o G1 foi estabelecido (apply, 24/11/2026, convenção 9): pelo atraso, sem o plano B.** A ponta vermelha (a cópia `296-exp-b`, a baseline `4e44dd3` com `INVEST_DELAY_MS=8`; as duas classes expostas pelo `teste-309.sh`) teve 5 tentativas inconclusivas, com CPU mínima de 58 a 90, e a 6ª **reprovada válida**, com a CPU em 100 nas 10 amostras. Nessa rodada caíram o `Source3` em `OrphanDispatchRoundTripTests.cs:112` e o `PushNotificationReceivedWithCorrectPayload` em `RoundTripTests.cs:55`, as duas esperas do `taskId` (`TimeoutException`). Nas 6 tentativas, todo caso que caiu na espera do `taskId` está entre os expostos (2, 4, 7, 8, 10 e 11). A 3ª teve também o `Source3` caindo em `:101`, com `AlreadyClosedException` (`CHANNEL_ERROR - expected 'channel.open'`) no `RestartWorkersAsync`: é a outra causa da #176 (D6), fora da espera do `taskId`, e não conta para o G1.
  - **Plano B, já autorizado pelo dono (24/11/2026):** se a ponta vermelha não tiver reprovação válida em 8 tentativas, não há parada. O guarda passa a usar uma mutação determinística numa cópia da baseline: a espera do `taskId` só começa a consultar depois de a resposta do caso estar entregue. Os casos expostos têm de reprovar na linha da espera do `taskId`. Como a reprovação não depende do tempo da máquina, ela conta com qualquer CPU. **Só se nem assim reprovar, a change para.** O que estabeleceu o G1, se o atraso ou o plano B, e por quê, fica registrado nesta seção no apply (convenção 9).
- **G2, a condição positiva.**
  - Tirar só a condição positiva hoje **não** reprova, porque o registro nasce síncrono (D1).
  - O guarda faz duas mutações numa cópia: (i) o recebimento passa a criar o registro depois de voltar, com o `ReceiveMessageAsync` adiando a gravação em 300 ms; (ii) os casos perdem a condição positiva.
  - **Vermelho:** com (i)+(ii), a ausência vale de imediato, não há task em estado final, e o caso reprova na leitura de `a2a_tasks`.
  - **Verde:** com (i) só, os casos passam.
  - Este desenho mexe em produção, só numa cópia descartável, aceito pelo dono em 24/11/2026.
  - **O G2 não guarda a sentinela (caso 4), medido no apply.** O `Source3` sozinho, com (i) e (ii), passa. Sem a condição positiva, a ausência do registro da sentinela vale de imediato, mas a espera de 15 s da reconciliação da conversa principal (`ReconciliationTimeout`, `:115`) cobre a reentrega do mesmo jeito. O que a condição positiva da sentinela guarda é outra coisa: que ela passou pelo worker. Esse é o G4.
- **G3, a segunda task.**
  - Numa cópia, o timeout do cliente A2A do inbox da fixture cai para 50 ms, e o handler ganha 100 ms de atraso, o que força a reintentativa.
  - **Esperado: todos os casos que leem task reprovam com a lista das tasks do agente**, com id e estado de cada uma: 1–3 e 5–12. A sentinela (caso 4) não lê task.

- **G4, a sentinela que passou pelo worker (D3; decisão do dono, 24/11/2026).** Numa cópia, a sentinela fala com um agente que o api não conhece. O api recusa o `SendMessage` com `A2AException` (`RoutingA2ARequestHandler.cs:53`), e o inbox faz o `FailAsync` e entrega o aviso de falha. O registro fecha sem task e sem worker.
  - **Vermelho**, com a condição positiva da D3: o `Source3` reprova na asserção da resposta, com o aviso de falha no lugar.
  - **Contraprova**, só com a ausência do registro: o `Source3` passa. É a forma que a sentinela teria sem a D3.

### D6. O critério para fechar a #176 (proposta, depois do merge)

O `Source3` reprova por dois motivos registrados:

- o da janela, que é esta change;
- o erro de canal do RabbitMQ ao reiniciar o worker, que apareceu uma vez, na baseline de 19/11/2026.

**Critério:** 15 rodadas completas do `rt-inbox` na `main` depois do merge, em pelo menos 2 dias distintos, com 0 reprovações do `Source3` de qualquer tipo.

- Rodada aprovada conta com qualquer CPU, pela regra assimétrica.
- A rodada de baseline de outras changes sobre a `main` conta, então o critério não exige rodadas só para ele.

**Por que 15:**

- na baseline da #296 sobre `c9733ce`, o `Source3` reprovou 2 vezes em 8, cerca de 25%;
- com 25%, a chance de 15 aprovações seguidas sem a correção é 0,75¹⁵ ≈ 1,3%;
- com 10%, seria ≈ 21%. Portanto 15 rodadas separam "corrigido" de "intermitente comum", e não excluem uma causa rara;
- o erro do RabbitMQ apareceu uma vez e tem taxa desconhecida. Se reaparecer, a #176 fica aberta com ele como causa restante, e o dono decide se vira issue própria.

O comentário na #176 com este critério só sai com o ok do dono.

### Árvore de pastas

```
tests/InboxOrchestratorRoundTrip.Tests/
├── OrphanDispatchRoundTripTests.cs        (casos 1–7: D1, D2, D3, D4; sai o WaitForTaskIdAsync)
├── RoundTripTests.cs                      (casos 8–11: D1, D2; sai o GetPendingDispatchTaskIdAsync)
├── SessionSerializationRoundTripTests.cs  (caso 12: D4)
├── Support/
│   ├── RoundTripFixture.cs                (intocado)
│   └── AgentTasks.cs                      (novo, se as três classes compartilharem a leitura de a2a_tasks por agente;
│                                           senão, um helper privado por classe, como os de hoje)
├── IssuerTokenRoundTripTests.cs           (intocado)
├── RoleRoundTripTests.cs                  (intocado)
└── ServiceTokenRoundTripTests.cs          (intocado)
openspec/changes/rt-inbox-espera-taskid/   (esta change)
```

Nada em `libs/`, em `apps/*/src` nem no compose.

## Risks / Trade-offs

- **[A ponta vermelha do G1 não consegue rodada válida]** O B teve 0 reprovações válidas em 16 tentativas, com a máquina estrangulada. → Rodar só as classes expostas, pelo `teste-309.sh`. Em 8 tentativas sem reprovação válida, usar o plano B da D5, a mutação determinística, sem relaxar a regra.
- **[A leitura de `a2a_tasks` amarra o teste ao schema do api]** → Já é o padrão do caso 10, `ApiAppDbContext.A2ATasks`. O `rt-crossapp` guarda o schema.
- **[O `Assert.Single` da D2 reprova numa reintentativa legítima do inbox]** → É o comportamento desejado: a segunda task é o achado da #314, e a mensagem traz a lista para quem for investigar. O G3 mostra que a lista aparece.
- **[A asserção nova da sentinela reprova onde hoje passa]** Uma sentinela recusada sem task fechava o registro e o caso seguia. → É cobertura que faltava, registrada como acréscimo (lista de casos, `tasks.md`).
- **[O caso 12 pode não ser exercitado pelo G3]** A 2ª passada não chegou a criar a task da `msg1`. Na 3ª, o G3 passou a 1 s de timeout e 2 s de atraso depois do `Submit`, com até 2 tentativas. Se o caso 12 continuar sem uma 2ª task, ele fica registrado como não exercitado (tasks.md, 2.3).
- **[O tempo dos casos]** As esperas novas são do mesmo tamanho das de hoje. → A duração do `rt-inbox` entra no registro por rodada.

## Migration Plan

Só testes: não há migração nem rollback de dado. O rollback é reverter o PR.

## Open Questions

Nenhuma. As duas foram decididas pelo dono em 24/11/2026:

1. **O critério da segunda task:** `Assert.Single`, com a lista das tasks do agente (id e estado) na mensagem (D2);
2. **O G2:** aceito como desenhado, com a mutação de produção só numa cópia descartável (D5).
