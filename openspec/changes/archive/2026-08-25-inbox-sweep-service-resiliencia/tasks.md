## 1. Implementação (apps/inbox)

- [x] 1.1 Em `DebounceSweepService.ProcessDueDispatchesAsync`, envolver a
      consulta que produz `candidateIds` (o bloco `using (var scope =
      scopeFactory.CreateScope()) { ... ToListAsync(...) }`) em um
      `try/catch (Exception ex)` — nível de ciclo (Decisão 3 do
      design.md): logar via `ILogger<DebounceSweepService>` em nível
      `Error` com a exceção original, identificando que é falha na
      consulta de candidatos, e retornar sem processar nenhum candidato
      neste ciclo (o próximo tick do `PeriodicTimer` consulta de novo).
- [x] 1.2 No `foreach` de candidatos, envolver cada chamada a `await
      TryDispatchAsync(candidateId, cancellationToken)` em um
      `try/catch (Exception ex)` próprio — nível de candidato (Decisão 3
      do design.md): logar em nível `Error` com a exceção original e o
      `candidateId` afetado, e continuar para o próximo candidato do
      `foreach` — nenhum candidato problemático bloqueia os demais no
      mesmo ciclo.
- [x] 1.3 Em ambos os `catch` (1.1 e 1.2), excluir cancelamento genuíno
      do processo: não tratar como erro uma `OperationCanceledException`
      quando `cancellationToken.IsCancellationRequested` for verdadeiro
      (mesmo idioma de `IsTransportFailure`, que já faz essa distinção
      para `TaskCanceledException`) — deixar essa exceção propagar
      normalmente.
- [x] 1.4 Conferir que nenhum outro comportamento de
      `ProcessDueDispatchesAsync`/`TryDispatchAsync` muda — os dois
      `catch` novos são estritamente aditivos ao redor do código
      existente, não uma reestruturação do método.

## 2. Testes (apps/inbox)

- [x] 2.1 Criar um `ILoggerProvider`/`ILogger` de captura para uso em
      teste, mesmo padrão já usado em
      `apps/workers/tests/Buteco.Workers.Tests/TimeZoneStartupValidationTests.cs`
      (`CapturingLoggerProvider`/`CapturingLogger`) — duplicado
      localmente em `apps/inbox/tests` (convenção 7: helper de teste
      pequeno, duplicado entre projetos de teste em vez de compartilhado).
      Registrar em `OrchestrationFactoryFixture`
      (`builder.Logging.AddProvider(...)` dentro de `ConfigureWebHost`),
      expondo a lista de mensagens capturadas para os testes lerem.
- [x] 2.2 Criar um `DbCommandInterceptor` de teste (ex.:
      `ThrowOnceOnCommandTextInterceptor`) que, quando armado com um
      fragmento de texto (ex.: `"pending_dispatches"`), lança uma
      exceção na primeira execução de comando cujo `CommandText`
      contenha esse fragmento e depois se desarma automaticamente
      (não afeta comandos seguintes, incluindo os do próprio teste ou de
      outros testes da classe). Registrar via
      `.AddDbContext<AppDbContext>(options => options.UseNpgsql(...)
      .AddInterceptors(...))` em `OrchestrationFactoryFixture`, expondo a
      instância (com o método de armar) para os testes.
- [x] 2.3 Novo teste: armar o interceptor da Tarefa 2.2 para a próxima
      consulta de candidatos, criar um candidato elegível e aguardar
      (`PollUntil`) o log capturado (Tarefa 2.1) registrar uma entrada de
      nível `Error` contendo a exceção forçada
      (`Requirement: Falha de infraestrutura durante a consulta de
      candidatos...`, primeiro Scenario, e `Requirement: Falha capturada
      (...) nunca é silenciosa`, Scenario de consulta).
- [x] 2.4 No mesmo teste da Tarefa 2.3, depois que o interceptor já
      disparou (e se desarmou) uma vez, confirmar via `PollUntil` que o
      **mesmo candidato** (ou um candidato novo criado depois) é
      consultado e disparado normalmente no ciclo seguinte — prova de
      que a consulta volta a funcionar sem reinício do processo
      (`Requirement: Falha de infraestrutura durante a consulta de
      candidatos...`, segundo Scenario).
- [x] 2.5 Novo teste: usar `factory.A2AClientFactory.Handler` (mesmo
      mecanismo já usado pelos testes existentes de rejeição/falha de
      transporte) para lançar, apenas para a sessão A, uma exceção que
      NÃO seja `A2AException` nem uma das cobertas por
      `IsTransportFailure` (ex.: `InvalidOperationException` simples).
      Criar também uma sessão B elegível no mesmo ciclo. Assere, via
      `PollUntil`, que (a) o log capturado registra a falha de A com o
      identificador do candidato, e (b) `SendMessage` é disparado
      normalmente para B **no mesmo ciclo**, sem esperar por uma nova
      varredura (`Requirement: Falha ao processar um candidato
      específico não bloqueia os demais candidatos do mesmo ciclo`, e o
      Scenario correspondente de `Requirement: Falha capturada (...)
      nunca é silenciosa`).
- [x] 2.6 Confirmar (rodando a suíte já existente) que os testes atuais
      de `DebounceSweepServiceTests.cs` — caminho feliz,
      `A2AException`, falha de transporte com retry — continuam
      passando sem alteração, provando que os dois `catch` novos não
      interferem no tratamento já existente desses casos.

## 3. Registro (parte da change)

- [x] 3.1 Corrigir a entrada da baseline de testes em
      `02-HISTORICO_E_STATUS.md` (seção da change
      `apps-workers-contexto-temporal`, subseção "Baseline nomeada"): o
      mecanismo registrado para a flake de `apps/inbox` ("corrida de
      disposal do `WebApplicationFactory` entre classes de teste em
      paralelo") está errado. Substituir pela causa real
      (`DebounceSweepService` + `HostOptions.BackgroundServiceExceptionBehavior
      .StopHost` sob contenção de recursos de Testcontainers concorrentes),
      registrar a taxa medida (2/10 execuções para a
      `ObjectDisposedException`; 3/10 para o flake separado de
      `InboundMessageOrchestratorTests`/`ContactSessionResolver`, já em
      aberto e sem relação com este defeito).
- [x] 3.2 Atualizar a seção "Próximo passo"/"Itens em aberto" de
      `02-HISTORICO_E_STATUS.md`: registrar a correção pendente de
      `InboxFactoryFixture` (Non-Goals do design.md desta change — mesma
      correção que `OrchestrationFactoryFixture` já tem, migrar sem
      acessar `Services` antes) como change própria subsequente, com
      gatilho "antes ou junto da próxima change que toque
      `apps/inbox/tests`".
- [x] 3.3 Acrescentar à convenção 4 de `01-ARQUITETURA_E_CONVENCOES.md`
      uma linha explícita (decisão já tomada em design.md, Decisão 2):
      todo `BackgroundService` deste monorepo deve capturar suas
      próprias falhas recuperáveis por unidade de trabalho (mesmo nível
      de `TaskJobConsumer`/`DebounceSweepService` após esta change), não
      depender de `HostOptions.BackgroundServiceExceptionBehavior` —
      `Ignore` não reinicia o serviço após a primeira exceção (fica
      "vivo mas morto"), e é política de host, não por dependência.
- [x] 3.4 Registrar em "Itens em aberto" de `02-HISTORICO_E_STATUS.md` o
      achado da Decisão 4 do design.md: `TaskJobConsumer`
      (`apps/workers`) tem a sequência de setup inicial
      (`CreateConnectionAsync`/`CreateChannelAsync`/`QueueDeclareAsync`/
      `BasicQosAsync`/`BasicConsumeAsync`) desprotegida — falha do
      RabbitMQ nesse ponto do boot derruba o host, via o mesmo default
      `BackgroundServiceExceptionBehavior.StopHost`. Diferente do
      defeito desta change (risco de boot, não de operação recorrente).
      Gatilho: antes de qualquer mudança futura em `TaskJobConsumer`, ou
      se `apps/workers` passar a subir em ambiente onde o RabbitMQ pode
      não estar pronto no boot.

## 4. Verificação

- [x] 4.1 (apps/inbox) Repetir a mesma metodologia de reprodução usada na
      exploração anterior — execuções sucessivas da suíte completa de
      `apps/inbox` com log detalhado — e confirmar que o evento
      `BackgroundService failed` / `HostOptions.BackgroundServiceExceptionBehavior
      is configured to StopHost` não aparece mais nos logs, e que a
      `ObjectDisposedException` desaparece da suíte. Se qualquer um dos
      dois persistir, registrar como achado novo (Migration Plan do
      design.md) — não descartar como resíduo esperado.
- [x] 4.2 (monorepo) Rodar a suíte completa (`apps/api`, `apps/workers`,
      `apps/inbox`, `tests/` na raiz) e confirmar, por nome de teste
      (não por contagem), que nenhuma regressão foi introduzida nas
      falhas pré-existentes já registradas em `02-HISTORICO_E_STATUS.md`.
