## 1. Correção (apps/workers)

- [x] 1.1 [apps/workers] Em `ConversationSessionCodec.Encode`
      (`apps/workers/src/Buteco.Workers/Agents/ConversationSessionCodec.cs`),
      passar `A2AJsonUtilities.DefaultOptions` para
      `JsonSerializer.SerializeToElement`, alinhando com o resto do
      pipeline A2A (D1 do design.md).
- [x] 1.2 [apps/workers] Atualizar o comentário XML de
      `ConversationSessionCodec.Encode` para registrar, junto da Decisão
      10 já citada, que o encoder precisa ser o mesmo do resto do
      pipeline A2A — não só a codificação em string escalar.

## 2. Teste de compatibilidade cross-app (tests/)

- [x] 2.1 [tests/CrossAppTaskStoreCompatibility.Tests] Rodar
      `dotnet test tests/CrossAppTaskStoreCompatibility.Tests` contra
      Postgres real (Testcontainers) e confirmar 2/2 verde após a
      correção 1.1 — sem alterar `BuildSampleTask`,
      `BuildSampleConversationSession` nem `AssertTasksMatch`.
- [x] 2.2 [tests/CrossAppTaskStoreCompatibility.Tests] Adicionar um
      comentário nas linhas 158-160 de
      `PostgresTaskStoreCompatibilityTests.cs` (a asserção
      `Assert.Equal(...GetRawText()...)`) explicando que a comparação
      textual é deliberada — detecta divergência de
      `JsonSerializerOptions` entre os dois `PostgresTaskStore` — e não
      deve ser relaxada para comparação semântica; apontar para esta
      change como referência (Risco 3 do design.md).

## 3. Especificação

- [x] 3.1 [openspec] Confirmar que o novo cenário "Metadata da task
      persistida pelo worker é lida de volta byte-identicamente pelo
      PostgresTaskStore da API", em
      `specs/a2a-task-lifecycle/spec.md` desta change, está coberto de
      fato pelos testes 2.1 (não precisa de teste novo — o cenário já é
      o que `PostgresTaskStoreCompatibilityTests` verifica).

## 4. Registro (apps/api e apps/workers não afetados; documentação viva)

- [x] 4.1 [docs] Em `02-HISTORICO_E_STATUS.md`, atualizar a entrada de
      `tests/CrossAppTaskStoreCompatibility.Tests` na baseline nomeada:
      remover da lista de pré-existentes, e registrar o diagnóstico
      (bug de encoder em `ConversationSessionCodec.Encode`, não
      divergência entre apps), o bisect (`1470b72`, 2026-08-01, nasceu
      quebrada no commit que introduziu o cenário), e a janela de 24
      dias / 11 commits em que as outras seis asserções de
      `AssertTasksMatch` continuaram passando sem que isso desse
      qualquer sinal — teste vermelho não distingue asserção nova de
      regressão real.
- [x] 4.2 [docs] Na mesma atualização de `02-HISTORICO_E_STATUS.md`,
      registrar o achado do `PushNotificationConfigCodec` (casing
      `Url`/`Token` em vez de `url`/`token`, nulls explícitos, em
      produção desde 2026-08-08) como divergência conhecida fora do
      escopo desta change — **exposição externa confirmada, não
      potencial** (`GetTask`/`ListTasks` via `GET /agents/{id}/a2a`
      devolvem `Metadata` sem filtragem a qualquer cliente do protocolo
      A2A; ver Risco correspondente no `design.md`). Registrar que isso
      eleva a prioridade da change subsequente, para não depender só da
      memória desta sessão até ela ser proposta.
- [x] 4.3 [docs] Em `01-ARQUITETURA_E_CONVENCOES.md`, acrescentar um
      terceiro exemplo à convenção 12 ("Contrato entre apps inclui o
      formato de fio, não só os campos"): opções de serialização
      (encoder de escaping, naming policy, tratamento de null) fazem
      parte do formato de fio tanto quanto a representação de um campo
      específico — dois sites que serializam o mesmo tipo com opções
      diferentes produzem payloads estruturalmente diferentes mesmo com
      os campos certos (D5 do design.md).

## 5. Verificação final

- [x] 5.1 [apps/workers] Rodar a suíte completa de
      `apps/workers/tests/Buteco.Workers.Tests` (inclui
      `ConversationSessionCodecTests` e `PushNotificationEndToEndTests`)
      para confirmar que a mudança em 1.1 não afeta nenhum teste
      existente.
- [x] 5.2 [tests/CrossAppTaskStoreCompatibility.Tests] Não é uma
      verificação a refazer durante o apply: a evidência de que nenhuma
      linha em `a2a_tasks` precisa de reformatação já está capturada em
      D3 do `design.md`, contra o commit pré-`apply` (`f668be5`) — o
      único lugar onde "linha gravada pelo código antigo, relida" pode
      existir (um Postgres novo do Testcontainers, rodado depois da
      correção em 2.1, só teria linhas já escritas pelo código
      corrigido, e não provaria nada sobre o formato anterior). Só
      confirmar, ao revisar D3 durante o apply, que a evidência citada
      ainda bate com o diff aplicado em 1.1 — sem repetir a captura.
