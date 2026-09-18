## Why

"Mensagens processadas" era **um** card no desenho do painel de inventário
(`frontend-inventario-catalogos`, arquivada em 2026-09-17, que o deixou fora com
o que ele exigia). Ele se dividiu em três métricas de atividade distintas, e
**mensagens recebidas é a primeira a sair do papel** — pelo mesmo motivo que
`inbox-sessoes-por-periodo` (arquivada em 2026-09-17) foi a primeira antes dela:
é a que não depende de nenhum dado que ainda não existe.

Hoje **nenhuma rota do `apps/inbox` conta mensagens por intervalo**. A única
leitura de mensagens existente — `GET /sessions/{sessionId:guid}/messages`
(`MessageEndpoints.cs:12`) — devolve a lista completa de **uma** sessão, exige o
`sessionId`, e não tem parâmetro de data. Para o número que o card quer, ela
obrigaria o cliente a varrer todas as sessões e somar.

O consumo final é **um inteiro**, não uma lista. Modelar como listagem e deixar o
cliente contar repetiria o erro que as duas propostas anteriores já recusaram:
trazer payload inteiro para exibir um inteiro.

Esta change é **só backend**, pela convenção 1 (catálogo → vínculo → execução →
UI): a tela que consome este número é change futura, sequenciada depois.

## What Changes

- **Rota nova, só leitura**: `GET /messages/summary?from=…&to=…` no `apps/inbox`,
  devolvendo `{ "inboundCount": N }` — a contagem de mensagens de **entrada**
  (`Direction = Inbound`) cujo `OccurredAt` cai dentro do intervalo, **inclusivo
  nos dois limites**.

- **`inboundCount`, não `receivedCount`.** É a convenção 13 aplicada ao contrato,
  e o argumento é diferente do de `startedCount`: "recebida" tem **ambiguidade de
  ponto de vista** — do sistema, uma mensagem recebida é a que o contato mandou;
  do contato, é a que o agente mandou. `inboundCount` herda o vocabulário de
  `MessageDirection.Inbound`, que é definido em relação ao sistema e não admite
  a inversão. "Mensagens recebidas" fica como **rótulo de tela**, quando ela
  existir — nunca como nome de campo.

- **Uma definição temporal só, sem tabela de descarte.** Ao contrário de sessão
  — que tinha três instantes concorrentes e precisou de uma tabela para eliminar
  dois —, `Message` tem **um** campo temporal, e ele é write-once: `OccurredAt` é
  atribuído em `CreateInbound` (`Message.cs:56`) e `CreateOutbound` (`:77`) e em
  nenhum outro lugar; o único mutador público da entidade é
  `UpdateDispatchStatus()`, que não o toca. Não há definição concorrente a
  descartar, então esta change **não repete** o formato de D3 da anterior.

- **Um gatilho novo, que nenhuma decisão anterior previa.** `OccurredAt` é o
  instante de **recebimento pelo servidor** (`DateTimeOffset.UtcNow` capturado no
  adapter — Telegram `:105`, Waha `:64`), não o instante que o provedor registrou
  no evento. É isso que torna a contagem estável — nenhum provedor consegue
  inserir mensagem "no passado" —, mas é propriedade **acidental**: vem de dois
  `UtcNow` espalhados em adapters, não de decisão escrita. O gatilho fica no
  `design.md` (D5).

- **`outboundCount` é Non-Goal explícito, com motivo.** Outbound é persistida em
  **dois** estados (`Sent` e `Failed` — `PushNotificationEndpoints.cs:163` e
  `:182`), e *"uma entrega que falhou conta como enviada?"* é pergunta sem
  resposta hoje. Incluir o campo agora obrigaria a respondê-la de passagem, que é
  o pior momento. Adiar não custa contrato: a resposta é **objeto**, não inteiro
  nu, exatamente para admitir o segundo campo sem quebra (D4 da change anterior).

- **A contagem é de mensagens distintas recebidas, não de entregas de webhook.**
  Há dedup real — `IX_messages_SessionId_ExternalId` único parcial
  `WHERE Direction='Inbound' AND ExternalId IS NOT NULL`, com
  `DbUpdateException` de violação única tratada como duplicata
  (`InboundMessageOrchestrator.cs:47,67`). Webhook reentregue conta **uma vez**.
  Isso é diferença de comportamento a **afirmar na spec**, não só implementação a
  testar.

- **Sem teto de intervalo e sem índice novo** — mesmo gatilho de volume real de
  D8/D9 da change anterior, mas **a razão do índice é outra e não se copia**:
  `sessions` não tinha índice nenhum em `StartedAt`; `messages` **tem**
  `IX_messages_SessionId_OccurredAt`, composto e com `SessionId` como coluna
  líder. O índice existe e **não serve** a esta consulta.

- **Herdado sem redecidir**, da change anterior: `from`/`to` obrigatórios e
  explícitos, limites inclusivos, binding manual (`string?` +
  `AssumeUniversal | AdjustToUniversal`), `ValidationProblem` como forma **única**
  de corpo para os três casos de erro, e `COUNT` agregado no banco.

Nenhuma quebra de contrato: nada existente muda de forma ou de comportamento.

## Capabilities

### New Capabilities

- `inbox-message-period-summary`: contagem agregada de mensagens de entrada do
  `apps/inbox` por intervalo de datas, servida como recurso próprio de leitura —
  os limites do intervalo, sua validação, qual instante decide a inclusão, e o
  fato de a unidade contada ser a mensagem distinta e não a entrega de webhook.

  **Por que capability nova, e não requisitos em `inbox-message-history`:** aquela
  capability descreve a leitura do **histórico de uma sessão** — lista ordenada,
  recorte por `sessionId`, prévia de última mensagem. Nenhum requisito dela é
  sobre agregação sem recorte. A separação é a mesma que já existe entre
  `inbox-message-orchestration` (o mecanismo que grava) e `inbox-message-history`
  (a leitura sobre ele), e o nome segue o vizinho direto
  `inbox-session-period-summary`, trocando só o domínio — as duas capabilities
  respondem perguntas diferentes sobre o mesmo intervalo.

### Modified Capabilities

Nenhuma. Nenhum requisito existente muda.

## Impact

- **Apps afetados: só `apps/inbox`.** Nenhum arquivo em `apps/api`,
  `apps/workers` ou `apps/frontend`.
- **Código**: um endpoint novo (`Messages/Endpoints/`), uma query e um handler
  mediator (`Messages/Queries/`), um record de resposta (`Messages/Responses/`),
  e o `Map*` correspondente em `Program.cs`. Nenhum arquivo existente muda de
  comportamento.
- **Banco**: **sem migração**. Nenhuma coluna, nenhum índice — `OccurredAt` já
  existe e já é persistido (`AppDbContext.cs:148`).
- **Rota**: `/messages` é prefixo de nível superior **novo** — `MessageEndpoints`
  hoje só registra `/sessions/{sessionId:guid}/messages`, aninhado sob
  `/sessions`. Inaugurar o namespace não muda nada na classificação de
  autenticação, mas precisa estar **dito** (design.md, D2).
- **Autenticação**: nenhuma exceção a declarar. `Program.cs` classifica toda rota
  como autenticada por padrão e a lista de exceções é só para as anônimas; a rota
  nova não a toca, e o startup reprova se alguém a esquecer.
- **Contrato**: rota nova, aditiva. Nenhum consumidor existente é afetado.
- **Consumidor**: nenhum nesta change. A tela é sequenciada depois (convenção 1).
