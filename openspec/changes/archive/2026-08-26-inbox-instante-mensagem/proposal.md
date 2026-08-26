## Why

A etapa 1 (`apps-workers-contexto-temporal`, arquivada) deu ao agente o
instante de **processamento**, mas deixou o instante da **mensagem**
sempre `null` — nenhum chamador tinha como fornecê-lo. Sob represamento de
fila (ou qualquer defasagem entre "cliente escreveu" e "worker processou"),
uma expressão relativa como "amanhã" resolve contra o instante errado:
mensagem escrita no dia 21 dizendo "amanhã", processada no dia 22, resolve
hoje para o dia 23 em vez do dia 22. Esta change fecha esse gap — é o
ponto em que o caso de uso que originou toda a linha de contexto temporal
se resolve de verdade.

Uma rodada de `/opsx:explore` (`inbox-contexto-canal-metadata`) já
investigou o transporte com evidência verificada (decompilação do pacote
`A2A`, rastreamento do pipeline em `apps/api`, leitura dos adapters e do
resolver de delegação) e a revisão decidiu dividir o que a exploração
cobria em duas changes por perfil de risco oposto: esta leva só o
**instante da mensagem** (plumbing com um cuidado de formato conhecido); o
**contexto de canal** (`DisplayName`, `ExternalId`) fica para
`inbox-contexto-canal`, subsequente, porque coloca texto livre do usuário
final na janela de contexto do modelo pela primeira vez, sem mitigação que
elimine o risco de injeção de prompt — não cabe nesta change.

## What Changes

- `apps/inbox` passa a incluir o instante de recebimento da última
  mensagem do buffer de debounce (`PendingDispatch.LastMessageAt`, já
  disponível no ponto de disparo) como um valor escalar (string ISO 8601
  com offset) em `Message.Metadata` ao montar o `SendMessage` disparado
  contra `apps/api` — usando `A2AJsonUtilities.DefaultOptions` na
  serialização, para não repetir o defeito de formato de fio já corrigido
  duas vezes nesta linha de trabalho (`ConversationSessionCodec`,
  `PushNotificationConfigCodec`).
- `apps/workers` passa a ler essa chave da última mensagem de usuário no
  histórico da task (já em mãos em `AgentExecutionService`, sem mudança
  em `TaskJobMessage`) e passá-la para
  `TemporalContextBlockBuilder.Build(timeProvider, messageInstant)` — a
  regra de precedência da etapa 1 (que já existe e já é testada) passa a
  ter, pela primeira vez em produção, um instante de mensagem real para
  resolver contra, incluindo a linha de defasagem entre os dois instantes.
- Ausência da chave (metadata ausente, chave ausente, ou valor presente
  mas não parseável) é caminho normal, não erro — colapsa para o
  comportamento já existente da etapa 1 (resolve contra o instante de
  processamento), sem falhar a task.
- O instante da mensagem passa a ser propagado para tasks criadas por
  delegação entre agentes (`AgentDelegationToolSetResolver`), para que
  Source e Target resolvam expressões relativas contra o mesmo instante —
  sem isso, os dois podem chegar a datas diferentes para a mesma palavra
  do cliente, a falha que esta linha de trabalho existe para evitar.

## Capabilities

### New Capabilities

(nenhuma — esta change estende capabilities já existentes)

### Modified Capabilities

- `inbox-message-orchestration`: o disparo do debounce contra `apps/api`
  passa a incluir o instante da última mensagem do buffer em
  `Message.Metadata`, além do que já é enviado hoje (`AgentId`,
  `ContextId`, `pushNotificationConfig`).
- `a2a-task-lifecycle`: `apps/api`, ao receber `SendMessage`, passa a
  garantir explicitamente que qualquer valor em `Message.Metadata`
  sobrevive à persistência sem alteração de formato (comportamento já
  existente, agora promovido a requisito com contraparte de teste); e o
  worker passa a extrair o instante da mensagem de `Message.Metadata`
  (quando presente) e repassá-lo ao construtor do bloco de contexto
  temporal, mudando a regra de precedência de sempre resolver contra o
  instante de processamento para resolver contra o instante da mensagem
  quando disponível.
- `agent-delegation-execution`: a criação de task delegada passa a
  propagar o instante da mensagem original para o Target, para que a
  resolução de expressões relativas não divirja entre Source e Target na
  mesma cadeia de delegação.

## Impact

- **`apps/inbox`**: `DebounceSweepService.BuildSendMessageRequest`
  (monta `Message.Metadata`); nenhuma migração de banco (usa coluna já
  existente, `PendingDispatch.LastMessageAt`).
- **`apps/workers`**: `AgentExecutionService`
  (`ExtractLatestUserText`/`GetTaskWithRetryAsync`, ponto de montagem das
  `Instructions`); `AgentDelegationToolSetResolver`
  (`DelegateToTargetAsync`, `BuildDelegationTool`,
  `IAgentDelegationToolSetResolver.ResolveAsync` — assinatura de
  interface muda). `TemporalContextBlockBuilder` não muda (seu parâmetro
  `messageInstant` já existe desde a etapa 1).
- **`apps/api`**: nenhuma mudança de código — já persiste
  `Message.Metadata` em `a2a_tasks` sem alteração, e `TaskJobMessage`
  (RabbitMQ) não precisa de campo novo.
- **Sem mudança de schema/migração** em nenhum dos três apps.
- **Contrato de protocolo A2A**: novo campo em `Message.Metadata` é
  aditivo — um cliente A2A externo que não enviar a chave não quebra
  nada, colapsa para o comportamento da etapa 1.
