## Why

`PushNotificationConfigCodec.Encode`
(`apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs:16-17`)
serializa sem `A2AJsonUtilities.DefaultOptions` — o mesmo defeito de
forma que `ConversationSessionCodec` tinha antes de
`crossapp-session-codec-encoder`, já registrado como item em aberto em
`02-HISTORICO_E_STATUS.md` desde aquela change.

A diferença que decide o escopo desta change: lá o defeito era benigno
(o valor era reescrito por `Serialize(task, A2AJsonUtilities.DefaultOptions)`
dentro de `SaveTaskAsync` antes de tocar o banco). Aqui não — o valor
gravado em `Metadata["pushNotificationConfig"]` é um `JsonElement` já
materializado, e a re-serialização do `AgentTask` inteiro não reaplica
naming policy sobre um `JsonElement` (só sobre objetos .NET serializados
a fresco). O casing errado (`Id`/`Url`/`Authentication`/`Token` em vez
de `id`/`url`/`authentication`/`token`) e os nulls explícitos de campos
ausentes **estão gravados em `a2a_tasks` desde 2026-08-08** e saem sem
filtragem para qualquer cliente do protocolo A2A que chame
`GetTask`/`ListTasks` (`GET /agents/{id}/a2a`,
`RoutingA2ARequestHandler.cs:47-56`).

Uma rodada de `/opsx:explore` já fechou as sete perguntas em aberto por
leitura de código, decompilação do `A2A.dll`, reprodução empírica do
path de produção e consulta à spec A2A na fonte — nenhum dos três
gatilhos de escalonamento (migração de leitura, consumidor externo real,
formato correto diferente do assumido) disparou. Corrigir agora porque
o defeito já está confirmado, não potencial, e cresce em superfície a
cada task terminal nova gravada sem a correção.

## What Changes

- Corrige `PushNotificationConfigCodec.Encode` para serializar com
  `A2AJsonUtilities.DefaultOptions`, alinhando-o ao resto do pipeline A2A
  (`PostgresTaskStore` dos dois apps, `PushNotificationSender`,
  `ConversationSessionCodec` já corrigido).
- Corrige as duas ocorrências em
  `PushNotificationEndToEndTests.cs` (`:67` e `:117`) que hoje afirmam
  `pushConfigElement.GetProperty("Url")` — escritas contra o
  comportamento observado (o bug), não contra o contrato — para afirmar
  `"url"`, com asserção negativa cobrindo a ausência dos campos opcionais
  omitidos gravados como `null`. Estende a mesma cobertura, positiva e
  negativa, aos campos aninhados (`token`,
  `authentication.scheme`/`authentication.credentials`) nos dois testes
  que já variam esses campos.
- Formaliza como cenário testável, em `a2a-push-notifications`, que o
  `pushNotificationConfig` persistido em `Metadata` segue o formato de
  fio da spec A2A (camelCase, opcionais omitidos).
- Sem migração de dados das linhas já gravadas em `a2a_tasks` — decisão
  explícita, com gatilho, não ausência de trabalho (ver design.md D4).
- Registra o fechamento do item em aberto em `02-HISTORICO_E_STATUS.md`
  (aberto por `crossapp-session-codec-encoder`) e registra o mecanismo
  do `JsonElement` que sobrevive à re-serialização em
  `01-ARQUITETURA_E_CONVENCOES.md` (D6 do design.md).

## Capabilities

### New Capabilities

(nenhuma)

### Modified Capabilities

- `a2a-push-notifications`: a requirement "Push notification config
  persistido pelo worker" ganha um cenário novo — o `pushNotificationConfig`
  gravado em `Metadata` segue o formato de fio do protocolo A2A
  (camelCase, sem campos opcionais ausentes serializados como `null`).

## Impact

- `apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs`
  — correção do encoder.
- `apps/workers/tests/Buteco.Workers.Tests/Notifications/PushNotificationEndToEndTests.cs`
  — expectativa corrigida nas duas ocorrências, com asserção negativa
  nova, e cobertura estendida a `token`/`authentication` nos dois testes
  que já variam esses campos.
- `openspec/specs/a2a-push-notifications/spec.md` — novo cenário na
  requirement existente.
- `02-HISTORICO_E_STATUS.md` — fecha o item em aberto registrado por
  `crossapp-session-codec-encoder`, com os números levantados no
  explore (37/37 linhas de dev no formato errado) e a decisão de não
  migrar, com gatilho.
- `01-ARQUITETURA_E_CONVENCOES.md` — registra o mecanismo do `JsonElement`
  sobrevivendo à re-serialização do `AgentTask`, na seção "AgentCard /
  protocolo A2A" (design.md D6).
- Nenhuma migração de dados em `a2a_tasks` (design.md D4).
- Nenhuma mudança de comportamento de push notification (retry, timeout
  de 5s, fire-and-forget) — segue como está.
