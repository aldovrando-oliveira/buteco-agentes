## Why

Dois defeitos reais encontrados em produção no round-trip de push
notification recebido por `apps/inbox` (capability
`inbox-message-orchestration`), ambos em
`PushNotificationEndpoints.ReceiveAsync`:

1. **Resposta nunca extraída** — `ExtractResponseText` lia
   `task.Status.Message?.Parts`, mas `AgentExecutionService.ExecuteAsync`
   (`apps/workers`) nunca preenche `Status.Message` no caminho de sucesso:
   grava o texto da resposta via `TaskUpdater.AddArtifactAsync` e chama
   `CompleteAsync()` sem mensagem final. `responseText` era sempre `null`,
   então a entrega ao canal de origem nunca acontecia, embora a `Message`
   de entrada ainda fosse marcada `Completed` (dispatch bem-sucedido sem
   nenhuma resposta visível ou entregue) — reproduzido com screenshot do
   inbox mostrando status "Concluído" sem nenhuma mensagem de saída.
   Note-se que a spec de `a2a-push-notifications` (Requirement "Webhook
   disparado ao final do processamento pelo worker") já descrevia
   corretamente o payload como contendo `artifacts` — o defeito era só de
   implementação em `apps/inbox`, não uma spec errada.

2. **Persistência perdida sob timeout do chamador** — `ReceiveAsync`
   propaga o `CancellationToken` da própria requisição (ligado a
   `HttpContext.RequestAborted`) por toda a função, incluindo a entrega ao
   canal e o `SaveChangesAsync` final. `PushNotificationSender`
   (`apps/workers`) usa, por decisão deliberada (`a2a-push-notifications`,
   Decision 3), um `HttpClient` com timeout fixo de 5 segundos e nenhum
   retry. Se o round-trip dentro de `ReceiveAsync` (consultar
   `PendingDispatch`, decifrar credencial, chamar a API do canal — ex.
   Telegram —, persistir) ultrapassar esses 5 segundos, o cliente aborta a
   conexão; isso propaga como cancelamento do lado do servidor mesmo
   depois que a entrega ao canal já tiver sido concluída com sucesso.
   Resultado: a resposta chega no canal (Telegram), mas nem a `Message` de
   saída, nem a atualização de `DispatchStatus` para `Completed`, nem a
   remoção do `PendingDispatch` são persistidas — a mensagem de entrada
   fica presa em "Processando" (`Dispatching`) para sempre. Reproduzido com
   screenshot mostrando duas mensagens presas em "Processando" enquanto
   uma delas já tinha sido respondida no Telegram.

Ambos os defeitos já foram corrigidos diretamente no código (ver Tasks);
esta change documenta a correção retroativamente e fecha o gap de spec
do item 2 — o item 1 não precisa de mudança de spec, só o defeito de
implementação foi corrigido.

## What Changes

- `PushNotificationEndpoints.ExtractResponseText` passa a ler
  `task.Artifacts?.LastOrDefault()?.Parts`, mesmo padrão de leitura já
  usado por `AgentDelegationToolSetResolver` e pelos testes de
  `apps/workers`.
- `PushNotificationEndpoints.ReceiveAsync` passa a processar a entrega ao
  canal, a atualização de `DispatchStatus` e o `SaveChangesAsync` final
  com `CancellationToken.None`, em vez do token da requisição — a busca
  inicial do `PendingDispatch` (antes de qualquer efeito colateral)
  continua usando o token da requisição, sem mudança de comportamento
  aí.
- Teste de fixture (`PushNotificationEndpointsTests.BuildAgentTask`)
  corrigido para construir o `AgentTask` no formato real produzido por
  `AgentExecutionService` (`Artifacts`, não `Status.Message`) — o formato
  antigo do fixture era exatamente por que os testes existentes não
  pegaram o defeito 1.

## Capabilities

### Modified Capabilities
- `inbox-message-orchestration`: o endpoint receptor de push notification
  passa a garantir que a entrega ao canal e a persistência local do
  resultado completam mesmo que o chamador (o webhook de
  `apps/workers`, com timeout curto e sem retry) já tenha desistido de
  esperar a resposta.

### No spec change
- `inbox-message-history` e `a2a-push-notifications`: nenhuma mudança de
  spec — o comportamento descrito já estava correto; o defeito 1 era
  puramente de implementação em `apps/inbox`, não uma lacuna de spec.

## Impact

- Só `apps/inbox` (`PushNotificationEndpoints.cs` e seu arquivo de
  testes). Nenhuma migração de banco, nenhuma mudança de contrato HTTP
  externo, nenhuma mudança em `apps/workers` (o timeout de 5s de
  `PushNotificationSender`/Decision 3 permanece como está — a correção é
  só do lado receptor, tornando-o resiliente a esse timeout em vez de
  alterá-lo).
- Suíte de testes de `apps/inbox` validada com Testcontainers apontando
  para o socket da API do Podman (sem Docker Desktop disponível neste
  ambiente): 155/155 testes passando, incluindo os 9 de
  `PushNotificationEndpointsTests` (ver Tasks, seção 3, para os detalhes
  de configuração).
