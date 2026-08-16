## 1. Fix de concorrência no append (apps/inbox)

- [x] 1.1 (apps/inbox) Em `Orchestration/InboundMessageOrchestrator.cs`,
      extrair uma função auxiliar privada assíncrona (ex.
      `AppendMessageWithRetryAsync(PendingDispatch pendingDispatch, string
      text, DateTimeOffset receivedAt, Guid sessionId, CancellationToken)`)
      que encapsula `AppendMessage` + `SaveChangesAsync` protegidos por um
      loop de retry para `DbUpdateConcurrencyException` (design.md,
      Decisão 1): a cada exceção capturada, chamar
      `dbContext.Entry(pendingDispatch).ReloadAsync(cancellationToken)`;
      se `pendingDispatch.Status != PendingDispatchStatus.Pending` após o
      reload (linha reivindicada pelo `DebounceSweepService` ou removida
      entre a leitura original e o reload), retornar um sinal indicando
      "não há mais Pending para reaproveitar" (ex. `bool` de retorno ou
      `null`) sem lançar; caso contrário, reaplicar `AppendMessage(text,
      receivedAt)` e tentar `SaveChangesAsync` de novo. Limitar a
      `MaxAppendRetries = 10` tentativas (design.md, Decisão 2); ao
      esgotar, propagar a última `DbUpdateConcurrencyException`.

      **Correção durante a implementação (ver design.md, Decisão 1 —
      "Correção durante a implementação")**: a versão com `ReloadAsync`
      causava perda silenciosa de mensagem (~80% de falha em 15
      execuções do teste da tarefa 2.1, sem nenhuma exceção) devido a um
      aliasing entre o snapshot "original" e o valor "current" do change
      tracker para a coleção owned/JSON `Messages`. Implementado com
      detach + rebusca via `FindPendingAsync` no lugar de `ReloadAsync` —
      15/15 execuções passaram de forma determinística após a troca.
- [x] 1.2 (apps/inbox) Substituir o caminho principal de
      `ReceiveMessageAsync` (hoje linhas 23-28, `pendingDispatch is not
      null` → `AppendMessage` + `SaveChangesAsync` direto) para usar a
      função auxiliar de 1.1 em vez do `SaveChangesAsync` sem retry. Se a
      função sinalizar "não há mais Pending" (linha reivindicada/removida
      entre a leitura original e a tentativa de append), cair no mesmo
      caminho de criação de uma nova `PendingDispatch` já usado quando
      `FindPendingAsync` retorna null (design.md, Decisão 1, passo 2) —
      não duplicar a lógica de criação.
- [x] 1.3 (apps/inbox) Substituir o bloco
      `catch (DbUpdateException exception) when (IsUniqueViolation(exception))`
      (hoje linhas 38-51) para usar a mesma função auxiliar de 1.1 em vez
      do `SaveChangesAsync` direto após o `FindPendingAsync` de
      recuperação — hoje esse `SaveChangesAsync` é exatamente o ponto que
      lança `DbUpdateConcurrencyException` sem tratamento (linha 50,
      confirmado por 5 execuções isoladas do teste, ver design.md,
      Diagnóstico). Aplicar o mesmo tratamento de "não há mais Pending"
      da tarefa 1.2 se o reload mostrar `Status != Pending`.
- [x] 1.4 (apps/inbox) Revisar se a extração das tarefas 1.1-1.3 elimina
      a duplicação de código de append entre os dois caminhos (principal
      e pós-unique-violation) sem alterar nenhum outro comportamento do
      método (criação de nova `PendingDispatch`, exceção de
      `InvalidOperationException` quando `FindPendingAsync` de
      recuperação não encontra nada mesmo sem `Status` divergente, etc.).

## 2. Testes (apps/inbox)

- [x] 2.1 (apps/inbox) Adicionar um teste dedicado ao caminho direto
      (linhas 23-28 hoje) em `InboundMessageOrchestratorTests.cs` — o
      caminho que o design.md identifica como afetado pelo mesmo bug mas
      que `ReceiveMessageAsync_ConcurrentCallsSameSession_ResolveToSinglePendingDispatch`
      não exercita, porque esse teste parte de "nenhuma `PendingDispatch`
      existe" e todas as chamadas competem na criação (só cobre o
      caminho pós-violação-de-unicidade da tarefa 1.3). O novo teste
      deve: (a) pré-criar uma `PendingDispatch` `Pending` para uma
      `Session`, fora de qualquer corrida (ex. uma chamada síncrona a
      `ReceiveAsync` com a primeira mensagem, aguardada antes de
      prosseguir); (b) disparar N chamadas concorrentes de
      `ReceiveMessageAsync` para essa mesma `Session` via `Task.WhenAll`
      (mesmo N = 8 do teste existente, para exercitar a mesma pressão de
      concorrência) — todas encontrando a `PendingDispatch` já existente
      na primeira leitura de `FindPendingAsync`, sem passar pela
      violação de unicidade; (c) assert: `Task.WhenAll` completa sem
      exceção não tratada; continua existindo uma única `PendingDispatch`
      Pending para a Session; a contagem final de mensagens no buffer é
      exatamente 1 (da pré-criação) + N (das chamadas concorrentes) —
      nenhuma mensagem perdida.
- [x] 2.2 (apps/inbox) Rodar os dois testes isolados de concorrência
      (`dotnet test --filter "FullyQualifiedName~ReceiveMessageAsync_ConcurrentCallsSameSession|FullyQualifiedName~<nome do teste novo de 2.1>"`,
      via Testcontainers usando o socket do podman-machine-default como
      backend — `DOCKER_HOST=unix:///var/folders/8k/7jq1v1sj51x10mrs9bnhx_j80000gn/T/podman/podman-machine-default-api.sock`
      e `TESTCONTAINERS_RYUK_DISABLED=true`, já que Docker não está
      instalado neste ambiente) pelo menos 5 vezes consecutivas cada após
      o fix e confirmar que ambos passam de forma determinística em
      todas as execuções — são testes de concorrência, uma única passada
      não é confirmação suficiente para nenhum dos dois. Rodado 15x
      consecutivas (acima do mínimo de 5) após a correção de 1.1
      (detach + rebusca em vez de `ReloadAsync`): 15/15 passando.
- [x] 2.3 (apps/inbox) Rodar a suíte completa de `apps/inbox`
      (`dotnet test`, mesmas variáveis de ambiente de 2.2) e confirmar
      122/122 testes passando (121 já existentes + o novo teste de 2.1),
      não 121/122 nem 120/121. Confirmado: 122/122.
- [x] 2.4 (apps/inbox) Confirmar que nenhum teste existente que exercite
      o caminho de criação de `PendingDispatch` (ex.
      `ReceiveMessageAsync_FirstMessage_CreatesPendingDispatchWithSingleMessage`,
      `ReceiveMessageAsync_SecondMessageBeforeDispatch_AppendsToSamePendingDispatch`)
      regrediu com a extração da função auxiliar de 1.1. Confirmado —
      ambos passam dentro do 122/122 de 2.3.
