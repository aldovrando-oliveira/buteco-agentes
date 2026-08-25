## 1. Correção (apps/workers)

- [x] 1.1 [apps/workers] Em `PushNotificationConfigCodec.Encode`
      (`apps/workers/src/Buteco.Workers/Agents/PushNotificationConfigCodec.cs`),
      passar `A2AJsonUtilities.DefaultOptions` para
      `JsonSerializer.SerializeToElement`, alinhando com o resto do
      pipeline A2A (D1 do design.md).
- [x] 1.2 [apps/workers] Atualizar o comentário XML de
      `PushNotificationConfigCodec.Encode` registrando que a codificação
      precisa usar as mesmas opções do resto do pipeline A2A — mesmo
      espírito do comentário de `ConversationSessionCodec`.

## 2. Teste E2E (apps/workers)

- [x] 2.1 [apps/workers] Em
      `PushNotificationEndToEndTests.cs:67`, trocar
      `pushConfigElement.GetProperty("Url")` por
      `pushConfigElement.GetProperty("url")`, e acrescentar a asserção
      negativa: `pushConfigElement.TryGetProperty("Url", out _)` deve ser
      `false` (D5 do design.md).
- [x] 2.2 [apps/workers] Repetir a mesma correção e asserção negativa em
      `PushNotificationEndToEndTests.cs:117` (segunda ocorrência,
      cenário `TaskFailedWithPushConfig_...`).
- [x] 2.3 [apps/workers] Nas duas ocorrências, acrescentar asserção
      negativa confirmando que campos opcionais não fornecidos no
      cenário (`Id`, `Authentication`) não aparecem como propriedade no
      `pushConfigElement` — nem em PascalCase nem em camelCase.
- [x] 2.4 [apps/workers] Adicionar um comentário junto das asserções de
      `:67`/`:117` explicando que o formato (`url` minúsculo, opcionais
      omitidos) é contrato da spec A2A, não coincidência — apontando
      para esta change, no mesmo espírito do comentário em
      `AssertTasksMatch` (`crossapp-session-codec-encoder`, Risco 3) —
      para não ser "consertado" de volta ao ver um teste vermelho no
      futuro (Risco correspondente do design.md).
- [x] 2.5 [apps/workers] Em `AuthenticationPresent_ResultsInAuthorizationHeader`
      (usa `Authentication = new AuthenticationInfo { Scheme = "Bearer",
      Credentials = "auth-credential" }`), acrescentar
      `DeserializeTask`/leitura de `pushConfigElement` como nas Tarefas
      2.1-2.3, e afirmar: `pushConfigElement.GetProperty("authentication").GetProperty("scheme")`
      == `"Bearer"` e `.GetProperty("credentials")` == `"auth-credential"`
      (camelCase, aninhado); mais a negativa — ausência de
      `Authentication`/`Scheme`/`Credentials` em PascalCase em qualquer
      nível (D5 do design.md — o aninhamento é a parte mais propensa a
      regredir, porque depende da naming policy se aplicar em
      profundidade).
- [x] 2.6 [apps/workers] Em `TokenPresent_ResultsInNotificationTokenHeader`
      (usa `Token = "webhook-token"`), a mesma checagem para
      `pushConfigElement.GetProperty("token")` == `"webhook-token"`
      (camelCase) e a negativa — ausência de `Token` em PascalCase.
- [x] 2.7 [apps/workers] Rodar
      `dotnet test apps/workers/tests/Buteco.Workers.Tests` (inclui
      `PushNotificationEndToEndTests`) e confirmar verde após 1.1-2.6.
      Confirmado: suíte completa 92/92 verde (rodada contra Postgres/
      RabbitMQ reais via Testcontainers, socket do `podman machine`).

## 3. Especificação

- [x] 3.1 [openspec] Confirmar que o cenário novo "Metadata persistida
      segue o formato de fio da spec A2A", em
      `specs/a2a-push-notifications/spec.md` desta change — que afirma o
      formato dos quatro campos de `PushNotificationConfig`
      (`url`, `token`, `authentication`, `id`) — está coberto de fato
      pelos testes 2.1-2.6, incluindo o aninhamento de `authentication`
      (2.5) e `token` (2.6), não só `url`. Não precisa de teste novo além
      desses. Confirmado: `url` coberto por 2.1/2.2, `token` por 2.6,
      `authentication.scheme`/`authentication.credentials` por 2.5; `id`
      não tem teste de presença (nunca é fornecido pelo cliente em nenhum
      cenário A2A deste repo hoje), mas sua ausência-quando-não-fornecido
      é coberta pela asserção negativa em todas as quatro ocorrências
      (2.1-2.2-2.5-2.6).

## 4. Registro (apps/api e apps/inbox não afetados; documentação viva)

- [x] 4.1 [docs] Em `02-HISTORICO_E_STATUS.md`, fechar o item em aberto
      "`PushNotificationConfigCodec.Encode` fora do contrato de
      serialização A2A, com exposição externa confirmada" (buscar pelo
      título — o número de linha muda a cada sessão de trabalho neste
      arquivo) — mover para a seção de baseline resolvida (ou remover da
      lista de itens em aberto, seguindo o padrão usado para
      `ConversationSessionCodec`), registrando: a correção aplicada, os
      números levantados no explore (Postgres de dev: 175 tasks totais,
      37 com a chave, 37/37 no formato errado antes da correção), a
      decisão de não migrar as linhas existentes com o gatilho (D4 do
      design.md), e a limitação sobre consumidores externos não
      descartáveis por leitura de código (D3 do design.md).
- [x] 4.2 [docs] Na seção "AgentCard / protocolo A2A" de
      `01-ARQUITETURA_E_CONVENCOES.md`, acrescentar uma frase registrando
      o mecanismo: `JsonSerializer.Serialize` sobre um `JsonElement` já
      materializado dentro de `AgentTask.Metadata` não reaplica naming
      policy nem `DefaultIgnoreCondition` na re-serialização do
      `AgentTask` inteiro (em `PostgresTaskStore.SaveTaskAsync`) — só
      objetos .NET serializados a fresco passam por essa etapa. Um valor
      fora do contrato só é corrigido por essa re-serialização se nunca
      foi materializado como `JsonElement` antes dela (caso
      `ConversationSessionCodec`, string escalar); se já foi (caso
      `PushNotificationConfigCodec`, objeto), o defeito chega ao disco
      (D6 do design.md).

## 5. Verificação final

- [x] 5.1 [apps/workers] Confirmar, ao revisar o diff aplicado em 1.1,
      que ele bate com a evidência empírica citada no design.md (Context
      e D1) — a correção deve ser exatamente passar
      `A2AJsonUtilities.DefaultOptions` para `SerializeToElement`, sem
      nenhuma outra mudança de comportamento. Confirmado: diff é
      exatamente essa troca (mais o comentário XML de 1.2, sem lógica
      nova).
- [x] 5.2 [tests/, dev local] Não é uma migração a executar durante o
      apply (D4 do design.md é decisão de não migrar). Rodar a query de
      contagem citada no design.md contra o Postgres de produção antes
      do apply, e registrar o resultado em uma das duas formas — não
      deixar o checkbox marcado sem registrar qual: (a) os números reais
      de produção (total/com a chave/formato errado/formato correto),
      substituindo a nota "só verificou dev" do design.md; ou (b), se não
      houver acesso ao Postgres de produção neste momento, registrar
      explicitamente "não executado por falta de acesso ao Postgres de
      produção" no design.md, para a próxima sessão saber que esse dado
      ainda falta. Resultado: (b) — sem acesso ao Postgres de produção
      nesta sessão de apply; registrado no design.md (Context).
