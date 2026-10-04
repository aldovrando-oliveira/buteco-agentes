**Issue:** #47

## Why

Uma `PendingDispatch` pode ficar em `Dispatching` para sempre. A conversa fica sem
resposta e sem aviso, as mensagens aparecem como "Processando" no painel, e nada
no sistema varre esse estado. O único leitor de linha em `Dispatching` é o
endpoint de push (`PushNotificationEndpoints.cs:61`): se o push não chega, ou chega
na hora errada, ninguém mais olha para a linha.

Reproduzido em 03/10/2026 sobre `cb74315`, por teste, nos três apps reais. São
**quatro** fontes, e não duas:

| # | fonte | como foi reproduzida | `TaskId` na linha |
|---|---|---|---|
| 1 | o inbox para, ou lança, entre o commit do claim e a gravação do `TaskId` | parada normal do host com o `SendMessage` em voo; exceção que não é de transporte depois do claim | **nulo** |
| 2 | o push falha (5 s, sem retentativa) | round-trip, webhook falhando | preenchido |
| 3 | o worker para entre gravar o estado terminal e enviar o push; a reentrega cai na guarda terminal e não reenvia | round-trip, parada do worker com o push em voo, confirmado pelo log da guarda | preenchido |
| 4 | **nova:** o push chega **antes** de o `TaskId` estar gravado; o endpoint responde 401 e o push não volta | `SendMessage` cuja task termina antes da resposta voltar ao inbox | preenchido |

A fonte 1 não depende de instância morrer: um deploy do inbox com `SendMessage` em
voo chega ao mesmo estado, e é o caso mais caro. O `SendMessage` já chegou à
`apps/api`, a task roda, o push chega com `TaskId` desconhecido (401), e a resposta
do agente se perde.

**Os "7 de 7 `Failed`" de 22/09 não são uma quinta fonte.** O caminho de falha do
worker envia o push (medido: task `Failed`, push 200, linha removida). No dev, o
push ia para o túnel do inbox, que resolvia IPv6 sem conectividade, e todas as
falhas são de antes do contorno do IPv6. É a fonte 2.

E há uma segunda metade, que o título da issue já nomeia ("sem aviso"): **nenhum
desfecho de falha avisa a conversa hoje**, nem pelo caminho normal. Task `Failed`
chega pelo push sem texto, nada é entregue, e as mensagens de entrada são
marcadas como `Completed` (`PushNotificationEndpoints.cs:94`). Rejeição síncrona,
rejeição de protocolo e esgotamento de tentativas também terminam em silêncio.

**Posição:** 8, depois da #46 e antes da `replicas-de-worker`, fixada pelo dono.

## What Changes

- **`apps/inbox`, a parada não interrompe mais o envio já reivindicado.** O trecho
  entre o claim e a gravação do `TaskId` (consulta do canal, `SendMessage`,
  resposta) roda com um prazo próprio de 8 s, e não com o token de parada. A parada
  espera esse trecho terminar. Medido com o `SendMessage` levando 1 s: a parada
  passou de 6 ms (`TaskId` nulo) para ~1.009 ms, 3 de 3, com o `TaskId` gravado.
  Isso tira a parada normal da fonte 1.
- **`apps/inbox`, reconciliação de `Dispatching` (serviço periódico novo).** A cada
  ciclo, para cada linha em `Dispatching`:
  - **com `TaskId`:** consulta o estado da task em `apps/api` (`GetTask` pelo
    cliente A2A que o inbox já usa). Task terminal há mais tempo que a carência
    é resolvida **pelo mesmo processamento do push**. Sem carimbo terminal na task,
    a carência conta da primeira vez que a reconciliação a viu terminal. Task
    não-terminal, task não encontrada ou consulta que falha ficam como estão. A
    idade da linha não entra: quem decide é o estado terminal;
  - **sem `TaskId`, com a última mensagem mais velha que um limite:** encerrada
    como perda, igual ao esgotamento de tentativas (log de erro, entradas
    `Failed`, aviso à conversa, linha removida). Não é redisparada: se o
    `SendMessage` já tinha chegado à `apps/api`, o agente responderia duas vezes.
    Depois da mudança acima, sobra para crash do processo e exceção inesperada.
    **Resíduo nomeado:** se o `SendMessage` tinha chegado, o agente respondeu, a
    resposta se perde e o contato recebe o aviso de falha.
- **Processamento do desfecho extraído** do endpoint de push para um componente
  usado pelo endpoint e pela reconciliação. A reconciliação não ganha semântica
  própria.
- **Aviso de falha à conversa.** Todo desfecho em que nenhuma resposta virá passa a
  enviar ao canal de origem o texto fixo *«Não consegui responder agora. Pode tentar
  de novo em instantes?»*, pelo mesmo caminho de entrega da
  resposta, e persistido como mensagem de saída com o mesmo status de entrega.
  Os desfechos são: task terminal não-`Completed` (pelo push ou pela
  reconciliação), rejeição síncrona (inclusive por agente inativo, decisão do dono),
  rejeição de protocolo, esgotamento de tentativas e linha sem `TaskId` encerrada. Task `Completed` sem texto continua
  sem mensagem.
- **Status das entradas de task `Failed` passa a `Failed`.** Hoje fica `Completed`
  sem nenhum guarda que o prenda. Alinha com o que `inbox-message-history` já diz
  de `Failed` ("nenhuma resposta virá").
- **Endpoint de push que perde a corrida para a reconciliação** responde 200 com
  log `Warning`, não 500 com erro.
- **Guardas por fonte**, cada um reprovando no código atual e passando com a
  correção: as quatro fontes (a parada com `SendMessage` em voo à parte), o aviso
  em cada desfecho, o carimbo nulo, o não-reenvio por duas instâncias e o endpoint
  que perde a corrida.

**Fora de escopo, explícito:**

- timeout e handler das chamadas de saída de `apps/workers` (acabaram de mudar na
  #46). A retentativa do push no worker foi considerada e **não** entra: a
  reconciliação cobre a fonte 2 sem um segundo mecanismo;
- `TaskJobConsumer` e o caminho de parada (#49). A fonte 3 é coberta do lado do
  inbox, sem tocá-los;
- o lock de contexto (#132, #134, #139), `KnowledgeIndexingConsumer` (#125) e a
  reabertura da métrica (#126);
- **task que nunca termina** (`Submitted` nunca consumida, `Working` presa): a
  linha fica em `Dispatching`, como hoje. É a população da
  `workers-nonterminal-task-detection`, que só observa e não age;
- texto do aviso configurável (por ambiente ou por canal): não há cenário real de
  outro valor (convenção 2);
- **buscar a task pelo `messageId`** para recuperar a linha sem `TaskId` em vez de
  avisar falha: por rota nova na `apps/api`, ou listando o contexto com histórico
  pelo protocolo (ver D7). Fica para quando o resíduo da fonte 1 aparecer;
- **as linhas que já existem** não ganham regra no código. A limpeza é manual, na
  janela do deploy (ver `design.md`): no dev, as 13 linhas de 22/08 a 22/09
  entregariam 2 respostas de seis semanas atrás e 9 avisos atrasados.

## Capabilities

### New Capabilities

Nenhuma. A reconciliação fecha o ciclo de disparo que `inbox-message-orchestration`
já define.

### Modified Capabilities

- `inbox-message-orchestration`: dois requisitos novos (reconciliação de disparo
  cuja task já terminou; encerramento de disparo sem `TaskId` além do limite), um
  requisito novo de aviso de falha, e "Conteúdo bufferizado é removido ao final do
  ciclo de disparo" passa a listar a reconciliação e o encerramento por idade
  entre os desfechos.
- `inbox-message-history`: "Estado de dispatch é espelhado nas mensagens de
  entrada agrupadas" passa a cobrir a task terminal não-`Completed` e os dois
  desfechos da reconciliação como falha; "Mensagem de saída persistida ao entregar
  resposta ao canal" passa a cobrir o aviso.
- `inbox-channel-adapter-plugin`: "Entrega da resposta do agente ao canal de
  origem" passa a cobrir a reconciliação como origem do desfecho, e o cenário "sem
  mensagem de resposta não invoca nenhum sender" fica restrito à task
  `Completed`.

## Impact

- **Código (só `apps/inbox`):** `Orchestration/PushNotifications/Endpoints/PushNotificationEndpoints.cs`,
  `Orchestration/DebounceSweepService.cs`, `Orchestration/Entities/PendingDispatch.cs`,
  `Program.cs`, e três arquivos novos: o serviço de reconciliação, o processamento
  de desfecho e as opções dela. **Uma migration**, `AddReconciliationClaimedAt`
  (coluna anulável `ReconciliationClaimedAt`, acrescentada na implementação, ver D4
  no `design.md`). O deploy segue o runbook de redeploy com migration
  (`stop inbox` → `migrator` → `up`), e o rollback mantém a coluna (Migration Plan
  do `design.md`). Nada em `apps/api`: o
  `service:inbox` já pode `POST /agents/{id}/a2a` (`ServiceScopeAuthorizationHandler.cs:39`),
  e o `GetTask` não filtra por agente (`PostgresTaskStore.cs:15-17`). Nada em
  `apps/workers`, `apps/frontend` nem `libs/`.
- **Documentação**, só quando o dono pedir (grupo 7 do `tasks.md`):
  `DispatchReconciliation__*` em `docs/configuration.md` e o ciclo de vida do
  buffer de debounce em `docs/architecture.md`.
- **Testes:** `apps/inbox` (parada com envio em voo, reconciliação, aviso,
  encerramento por idade, push precoce, endpoint que perde a corrida) e `tests/InboxOrchestratorRoundTrip.Tests` (fontes 2 e 3 com os três
  apps reais). A fixture do round-trip ganha controles de push, de falha do LLM e
  de reinício do worker, neutros por padrão.
- **Comportamento observável:**
  - a conversa órfã passa a receber a resposta, ou o aviso, alguns minutos depois
    (carência + intervalo);
  - a parada do inbox pode levar até 8 s a mais quando há envio em voo, dentro dos
    10 s padrão do Compose (`docker-compose.prod.yml` não define
    `stop_grace_period`);
  - o contato passa a receber aviso em todo desfecho de falha, inclusive nos que já
    existiam;
  - o painel mostra "Falha no processamento" para task `Failed` em vez de
    "Concluído";
  - a `apps/api` passa a receber um `GetTask` por linha em `Dispatching` por ciclo.
