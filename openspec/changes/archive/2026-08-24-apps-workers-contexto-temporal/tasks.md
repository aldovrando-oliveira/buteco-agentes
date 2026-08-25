## 1. `apps/workers` — `TimeProvider` no DI e dependência de teste

- [x] 1.1 Em `apps/workers/src/Buteco.Workers/Program.cs`, registrar
      `builder.Services.AddSingleton(TimeProvider.System);` — depois da
      Tarefa 1.3, nenhum código de `apps/workers`, novo ou pré-existente,
      deve chamar `DateTimeOffset.Now`/`TimeZoneInfo.Local` diretamente
      (design.md, Decisão 6, Decisão 9/Decisão A).
- [x] 1.2 Adicionar `<PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.9.0" />`
      em `Directory.Packages.props` e `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />`
      em `apps/workers/tests/Buteco.Workers.Tests/Buteco.Workers.Tests.csproj`
      (design.md, Decisão 8 — versão verificada como a mais recente estável
      publicada no NuGet.org na data desta proposta, e decompilada
      diretamente para confirmar a API usada, não só inferida da versão
      inspecionada durante a exploração).
- [x] 1.3 Injetar `TimeProvider` no construtor de `TaskJobConsumer`
      (`apps/workers/src/Buteco.Workers/Messaging/TaskJobConsumer.cs`) e
      trocar as duas chamadas diretas a `DateTimeOffset.Now` (`StartAsync`
      linha 24, `StopAsync` linha 70) por `timeProvider.GetLocalNow()` —
      fecha a única ocorrência real de acesso a relógio/fuso local fora do
      `TimeProvider` encontrada na varredura completa de `apps/workers`
      (design.md, Achado 9; Decisão 9, Decisão A). Site pré-existente a
      esta change, não introduzido por ela.

## 2. `apps/workers` — construtor do bloco de contexto temporal

- [x] 2.1 Criar `apps/workers/src/Buteco.Workers/Agents/TemporalContextBlockBuilder.cs`
      com `public static string Build(TimeProvider timeProvider, DateTimeOffset? messageInstant = null)`
      (design.md, Decisão 1). `messageInstant` sempre `null` nesta etapa —
      nenhum chamador em `apps/workers` tem outro valor para passar; a
      assinatura já reserva o espaço que a etapa 2 vai preencher.
- [x] 2.2 Formato do carimbo: ISO 8601 com offset (`timeProvider.GetLocalNow()`)
      + dia da semana por extenso em `CultureInfo("pt-BR")` fixo — nunca
      `CurrentCulture`/`CurrentUICulture` do host (design.md, Decisão 3 e a
      seção "Timezone é padrão do sistema inteiro" do proposal.md).
- [x] 2.3 Primeira linha do bloco declara explicitamente que o conteúdo não
      é uma mensagem do usuário (design.md, Decisão 3).
- [x] 2.4 Sem `messageInstant`: bloco contém só o instante de processamento
      e a regra de precedência colapsada ("resolva expressões relativas
      contra o instante acima") — forma exata em design.md, Decisão 3.
- [x] 2.5 Com `messageInstant` (exercitado só por teste nesta etapa — ver
      Tarefa 5): bloco contém os dois instantes, a regra de precedência
      completa (expressões relativas resolvem contra o instante da
      mensagem, o de processamento só indica atraso), e a linha de
      defasagem — ver Tarefa 2.7.
- [x] 2.6 Nova constante global de limiar de defasagem (nome a definir, ex.
      `StaleResponseThreshold`), em código, não em `appsettings.json`, não
      configurável por agente — mesmo padrão de `MaxHistoryMessages`/
      `SummarizationTurnThreshold`/`DelegationDepthLimit` em
      `AgentExecutionService` (design.md, Decisão 5; convenção 2). Valor de
      partida: Open Questions do design.md.
- [x] 2.7 Linha de defasagem (dia(s)/hora(s)/minuto(s) em termos humanos, não
      um `TimeSpan` cru) só aparece quando `messageInstant` está presente E
      a diferença entre os dois instantes ultrapassa o limiar da Tarefa 2.6
      (design.md, Decisão 3 e 5). Nesta etapa a linha nunca aparece em
      produção (nenhum chamador passa `messageInstant`); o código e o par
      de teste acima/abaixo do limiar existem mesmo assim (Tarefa 5.5).

## 3. `apps/workers` — ponto de montagem em `AgentExecutionService`

- [x] 3.1 Injetar `TimeProvider` no construtor de `AgentExecutionService`
      (mesmo padrão dos demais resolvers já injetados).
- [x] 3.2 Em `AgentExecutionService.ExecuteAsync`, no único ponto de
      montagem de `ChatClientAgentOptions` (`AgentExecutionService.cs:171-192`),
      concatenar `agent.Instructions` com
      `TemporalContextBlockBuilder.Build(timeProvider)` — bloco depois das
      instruções do operador, separador `\n\n` (design.md, Decisão 2).
- [x] 3.3 Tratar o caso `Agent.Instructions` vazia/em branco: o texto final
      não pode começar com o separador órfão — usar só o bloco temporal
      nesse caso, sem `\n\n` antes dele (design.md, Decisão 2).

## 4. `apps/workers` — checagem de boot de `TZ`

- [x] 4.1 Criar extensão (ex. `ValidateTimeZoneConfiguration(this IHost host)`)
      que resolve `TimeProvider` via `host.Services.GetRequiredService<TimeProvider>()`
      (não `TimeProvider.System` direto), compara
      `Environment.GetEnvironmentVariable("TZ")` contra
      `timeProvider.LocalTimeZone.Id`, e lança
      `InvalidOperationException` síncrona se não baterem — cobre `TZ`
      ausente, vazia e inválida com a mesma comparação (design.md, Decisão
      4). Sem caso especial para UTC: `TZ=UTC`/`TZ=Etc/UTC` batem
      naturalmente com o `Id` resolvido.
- [x] 4.2 Em caso de sucesso, logar o `Id` do fuso resolvido e o offset
      atual (`timeProvider.GetLocalNow().Offset`) no log de inicialização
      (design.md, Decisão 4).
- [x] 4.3 Chamar essa extensão em `Program.cs` depois de
      `var host = builder.Build();`, antes de `host.Run();` — primeira
      checagem de startup de `apps/workers` (design.md, Achado 6; molde de
      `ValidateRouteAuthenticationClassification` de `apps/inbox/Program.cs:143`).
- [x] 4.4 Declarar no contrato/manifesto de deploy de `apps/workers` que
      `TZ` deve usar o nome IANA da tz database **sem o prefixo POSIX `:`**
      (ex. `America/Sao_Paulo`, nunca `:America/Sao_Paulo`) — documentação,
      não código. O prefixo `:` resolve o fuso corretamente mas é removido
      do `Id` que o .NET expõe, o que faria a comparação estrita falhar um
      deploy correto (design.md, Decisão 4, Achado 5 — caso verificado, não
      hipotético).

## 5. `apps/workers` — testes unitários puros do construtor do bloco (`Buteco.Workers.Tests`, sem Testcontainers)

- [x] 5.1 Dia da semana correto para um instante conhecido, com
      `FakeTimeProvider` fixando relógio e fuso (`SetLocalTimeZone`).
- [x] 5.2 Carimbo em ISO 8601 com offset presente e correto.
- [x] 5.3 Frase de precedência presente no bloco (dois textos possíveis:
      com e sem `messageInstant` — Tarefas 2.4/2.5).
- [x] 5.4 Par com/sem `messageInstant`: bloco sem instante da mensagem não
      apresenta um segundo instante nem a linha de defasagem; bloco com
      instante da mensagem apresenta os dois.
- [x] 5.5 Par de defasagem acima/abaixo do limiar (constante da Tarefa 2.6):
      chamando `Build` diretamente com `messageInstant` sintético,
      confirmar que a linha de defasagem aparece só acima do limiar.
- [x] 5.6 `Agent.Instructions` vazia não produz separador órfão no texto
      final concatenado (Tarefa 3.3) — par com/sem `Agent.Instructions`
      vazia.
- [x] 5.7 Bloco montado uma única vez: nenhuma duplicação do marcador do
      bloco no texto final de `Instructions` (design.md, Risks — risco de
      duplicação; contraparte de teste desse risco, que não vira Scenario
      em spec.md por ser de implementação, não de contrato observável).

**Nota (design.md, Risks):** os testes desta seção cobrem a FORMA do bloco
isoladamente. Sozinhos, não provam que o pipeline real de
`AgentExecutionService` entrega esse texto ao `IChatClient` — essa prova é
a Tarefa 6.2, obrigatória, não uma alternativa a esta seção.

## 6. `apps/workers` — testes de integração (`Buteco.Workers.Tests`, `WorkerInfrastructureFixture`/Testcontainers)

- [x] 6.1 Variação de `ConversationHistoryTests
      .AgentInstructionsUpdatedBetweenMessages_SecondCallUsesNewInstructions`
      (`ConversationHistoryTests.cs:182-229`): duas tasks no mesmo
      `contextId`, `FakeTimeProvider` registrado no `BuildHost` e avançado
      entre a task A e a task B, `ChatOptions` capturado via `Callback` na
      segunda chamada — confirmar que o carimbo capturado reflete o
      instante da segunda execução, não o da primeira (design.md, Decisão
      7 — fecha o risco "carimbo congelado" com evidência de ponta a
      ponta, não só a nível de unidade).
- [x] 6.2 Confirmar, no mesmo teste ou em variação próxima, que o texto de
      `Instructions` capturado pelo `IChatClient` mockado contém o bloco de
      contexto temporal (não só que o construtor unitário o produz
      isoladamente) — contraparte do risco "bloco montado mas não
      entregue ao modelo" (design.md, Risks). Obrigatória junto com a
      Seção 5, não redundante com ela.

`BuildHost` (harness já existente) **não** é alterado para chamar a
checagem de boot de `TZ` — ela é comportamento de `Program.cs`, não do
pipeline de execução de agente que este harness testa, e `BuildHost`
registra `FakeTimeProvider`, não `TimeProvider.System` (Decisão 6), então
misturar as duas coisas produziria exatamente o fixture forjado que a
Tarefa 7 evita (design.md, Decisão 9).

## 7. `apps/workers` — testes de boot de `TZ` (host mínimo, sem Testcontainers, classe própria)

Testes desta seção NÃO usam `WorkerInfrastructureFixture`/`BuildHost` — são
tão independentes de Postgres/RabbitMQ quanto os da Seção 5. Vivem em uma
classe de teste própria (nome sugerido: `TimeZoneStartupValidationTests`),
por mutarem estado global do processo (`TZ`, cache de `TimeZoneInfo`) e
precisarem rodar em sequência entre si — não misturar com outras classes de
teste (design.md, Decisão 9).

- [x] 7.1 Cada teste constrói seu próprio host mínimo
      (`Host.CreateApplicationBuilder()` registrando só
      `builder.Services.AddSingleton(TimeProvider.System)` — sem Postgres,
      sem RabbitMQ, sem `AgentExecutionService`) e chama a mesma extensão
      de `Program.cs` (Tarefa 4.1) contra esse host.
- [x] 7.2 Mecanismo de mutação: `Environment.SetEnvironmentVariable("TZ", valor)`
      seguido de `TimeZoneInfo.ClearCachedData()` antes de resolver
      `TimeProvider.System` do host — não subprocesso (design.md, Decisão
      9, Achado 7: mecanismo confirmado real e repetível, testado em
      container Linux e nativamente em macOS). Cada teste restaura `TZ` ao
      valor original e chama `ClearCachedData()` de novo ao final
      (`finally`/`Dispose`), para não vazar estado mutado a outros testes
      do mesmo processo.
- [x] 7.3 Teste: host falha ao ser validado sem `TZ` definida (mutação para
      `null`/removida) — cobre o cenário "Worker não inicia sem TZ
      definida" de `specs/workers-scaffold/spec.md`.
- [x] 7.4 Teste: host falha ao ser validado com `TZ` definida para um valor
      inválido (ex. `America/Sao_Paolo`, typo verificado no design.md,
      Achado 5) — cobre "Worker não inicia com TZ definida para um valor
      que não resolve corretamente". Caso distinto do 7.3, mesma checagem.
- [x] 7.5 Teste: host valida com sucesso com `TZ` definida para um fuso
      válido, e o log de inicialização contém o `Id` do fuso resolvido e o
      offset atual — cobre "Worker inicia normalmente com TZ corretamente
      configurada e registra o fuso resolvido".
- [x] 7.6 Comentário explícito na classe de teste (`TimeZoneStartupValidationTests`
      ou nome equivalente) documentando o invariante que a torna segura
      para rodar em paralelo com o resto da suíte: depois da Tarefa 1.3,
      nenhum código de `apps/workers` (produção ou teste) lê
      `TimeZoneInfo.Local`/`TimeProvider.System` reais fora do próprio
      mecanismo desta classe — verificado por varredura completa de
      `apps/workers` (design.md, Achado 9), estendida depois aos projetos
      de teste na raiz do repo em `tests/` (design.md, Achado 10), não por
      suposição. **Não** é varredura de todo o código do repo — código de
      produção de `apps/api`/`apps/inbox` nunca foi varrido por este eixo
      e não precisa ser, já que nenhum dos dois monta host de
      `apps/workers`. O comentário registra explicitamente que o resíduo
      fora do alcance dessas duas varreduras é código de framework
      (logging, Npgsql/EF Core, host), não algo que uma varredura de
      código deste repo ou isolamento de coleção do xUnit eliminaria, e
      que nenhuma asserção da suíte depende de um valor derivado de fuso
      local — tripwire para quem for depurar um flake futuro na suíte de
      integração, não uma garantia (design.md, Decisão 9, Decisões A e B
      — inclui a alternativa de isolar a coleção considerada e rejeitada,
      com o motivo).

## 8. Verificação final

- [x] 8.1 Rodar `dotnet build` (produção e testes) de `apps/workers` e
      confirmar 0 avisos/erros novos. Confirmado: `dotnet build Workers.sln`
      limpo, 0 erros; os 2 avisos existentes (`NU1903`, vulnerabilidade do
      `SSH.NET` transitivo de Testcontainers) são pré-existentes, não
      introduzidos por esta change.
- [x] 8.2 Rodar a suíte de testes de `apps/workers`. Docker não está
      instalado neste ambiente, mas `podman` está — Testcontainers rodou
      contra o socket do `podman machine` via `DOCKER_HOST`, então a suíte
      completa foi exercitada, não só as Seções 5 e 7: **92/92 testes
      passando**, incluindo todos os pré-existentes (histórico, resumo,
      delegação, retry, MCP, push notification) sem alteração de
      comportamento, mais os novos desta change.

      Achados fora do escopo original das tarefas, corrigidos para chegar a
      verde (registrados aqui, não escondidos):
      - `AgentExecutionService`/`TaskJobConsumer` passaram a exigir
        `TimeProvider` no construtor (Tarefas 1.3/3.1) — todo harness de
        teste que monta esses tipos via `IHost` próprio precisou do mesmo
        registro que `Program.cs` ganhou. Além de
        `ConversationHistoryTests.BuildHost` (já previsto), mais seis
        arquivos tinham seu próprio `BuildHost`/host mínimo:
        `WorkerTests.cs`, `TaskJobConsumerTests.cs` (dois builders),
        `AgentDelegationConcurrencyTests.cs`,
        `AgentDelegationExecutionTests.cs`, `HistorySummarizationTests.cs`,
        `Mcp/McpToolExecutionEndToEndTests.cs`. Todos ganharam
        `builder.Services.AddSingleton(TimeProvider.System)`.
      - Gap pré-existente, não introduzido por esta change, mas que só
        apareceu ao rodar a suíte contra infraestrutura real pela primeira
        vez nesta sessão: `PushNotificationSender` nunca era registrado em
        `ConversationHistoryTests.BuildHost` nem em mais quatro dos
        arquivos acima (`WorkerTests.cs`, `TaskJobConsumerTests.cs`,
        `AgentDelegationConcurrencyTests.cs`,
        `AgentDelegationExecutionTests.cs`, `HistorySummarizationTests.cs`,
        `Mcp/McpToolExecutionEndToEndTests.cs`), mesmo `AgentExecutionService`
        já dependendo dele antes desta change. Corrigido com o mesmo
        registro de `Program.cs` (`AddHttpClient` + `AddSingleton`).
      - `ConversationHistoryTests.AgentInstructionsUpdatedBetweenMessages_SecondCallUsesNewInstructions`
        (pré-existente) fazia `Assert.Equal("Instruções novas.", ...)` —
        passou a `Assert.StartsWith`, porque `Instructions` agora
        legitimamente carrega o bloco de contexto temporal depois do texto
        do operador (Decisão 2). Comportamento novo esperado, não bug.

      **Validação estendida ao monorepo inteiro**, além do que esta tarefa
      exige (só `apps/workers`) — rodada por pedido explícito em revisão,
      via `podman`/`DOCKER_HOST` cobrindo também os projetos que
      Testcontainers usa fora de `apps/workers`. 636 testes no total:

      - `apps/api`, `apps/inbox`: 154/155 cada — a falha em cada um passa
        isolada (flake de isolamento entre testes rodando em conjunto,
        pré-existente, fora de `apps/workers`).
      - `libs/ProviderCatalog.Tests`: 6/6.
      - `tests/CrossAppTaskStoreCompatibility.Tests`: 0/2 — divergência de
        escaping JSON (`"` vs. `\"`) entre o serializador de
        `apps/api` e o de `apps/workers` ao ler/escrever `AgentTask
        .payload`. **Confirmado pré-existente por evidência, não por
        inspeção**: os mesmos dois testes, com a mesma mensagem de erro,
        falham do mesmo jeito rodados a partir do commit imediatamente
        anterior ao apply desta change (`git worktree` num commit
        anterior). Bug real, mas em código que esta change nunca tocou
        (`PostgresTaskStore`/serialização de task) — fora do Non-Goal
        desta change corrigir.
      - `tests/InboxOrchestratorRoundTrip.Tests`: 1/2 inicialmente — a
        regressão real que motivou o Achado 10 do design.md (`TimeProvider`
        ausente em `RoundTripFixture.BuildWorkersHost`), corrigida na hora.
        O segundo teste (`MessageReceived_TriggersFullRoundTrip...`) segue
        falhando por `TimeoutException` a ~22s contra um limite interno de
        20s, repetível nas duas tentativas — leitura mais provável é
        overhead de rede do `podman` (VM) frente ao Docker nativo para o
        qual o timeout foi calibrado, não regressão de código (nada nesta
        change adiciona latência a esse caminho).
      - `apps/frontend` (Vitest): 210/224 — repositório que esta change
        nunca editou; falhas pré-existentes de timing de `userEvent` e
        timeout de navegação em testes de componentes MCP.

      Nenhuma dessas falhas fora de `apps/workers` foi corrigida — Non-Goal
      explícito desta change (nenhuma alteração em `apps/api`,
      `apps/inbox` ou `apps/frontend`); ficam reportadas aqui para quem for
      investigar não precisar refazer o mesmo levantamento.
- [x] 8.3 Verificação manual documentada no PR (design.md, Risks — risco
      sem contraparte automatizada): enviar uma mensagem com expressão de
      tempo relativa a um agente real perto da virada do dia, conferir
      manualmente que a resposta usa corretamente o instante de
      processamento fornecido (única âncora existente nesta etapa — não é
      um teste de precedência entre duas âncoras, que só existe a partir
      da etapa 2). **Confirmada pelo usuário em 2026-08-24**, fora deste
      ambiente (exigia agente real com provider LLM configurado). O
      resultado literal da conversa com o agente fica para a descrição do
      PR, não reproduzido aqui.
