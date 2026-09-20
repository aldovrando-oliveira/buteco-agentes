using System.Text.Json;
using global::A2A;
using Buteco.Workers.A2A;
using Buteco.Workers.Diagnostics;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaskStatus = A2A.TaskStatus;

namespace Buteco.Workers.Tests.Diagnostics;

/// <summary>
/// Guardas da varredura de tasks não-terminais envelhecidas.
/// </summary>
/// <remarks>
/// <b>Contra `HEAD` estes testes não existem — não compilam.</b> Marcá-los como
/// "vermelhos da convenção 15" seria erro: o peso daquela convenção nesta change
/// é carregado pelos guardas de `AgentDelegationConcurrencyTests`, que compilam
/// contra `HEAD` (só observam log) e reprovam pela chave ausente. Aqui o que se
/// prende é semântica de componente novo, mais o par "sem item" que a convenção
/// 5 pede — e esse par tem uma armadilha própria, ver T3.
///
/// <para>
/// Usa <see cref="TimeProvider.System"/> e semeia linhas com
/// <c>status_timestamp</c> no passado, em vez de usar relógio falso: o
/// <c>StatusTimestamp</c> das linhas vem do banco real, e misturar relógio falso
/// com instantes reais gravados é a forma mais fácil de escrever um guarda que
/// passa por coincidência.
/// </para>
///
/// <para>
/// <b>14ª classe do <see cref="WorkerHostCollection"/></b> — o critério de
/// leitura da suíte foi recalibrado na própria change que a acrescentou
/// (convenção 22), e não deixado para a seguinte descobrir.
/// </para>
/// </remarks>
[Collection(WorkerHostCollection.Name)]
public class NonTerminalTaskDetectorTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task Detect_WithSubmittedTaskOlderThanWindow_CountsItAsSubmitted()
    {
        await ClearTasksAsync();
        var agentId = await SeedAgentAsync();
        var taskId = await SeedTaskAsync(agentId, TaskState.Submitted, age: Window + TimeSpan.FromMinutes(1));

        var report = await DetectAsync();

        Assert.Equal(1, report.CountFor(nameof(TaskState.Submitted)));
        Assert.Equal(0, report.CountFor(nameof(TaskState.Working)));
        Assert.Equal(taskId, report.OldestTaskId);
        Assert.Equal(Window, report.Window);
    }

    [Fact]
    public async Task Detect_WithWorkingTaskOlderThanWindow_CountsItAsWorkingInASeparateBucket()
    {
        await ClearTasksAsync();
        var agentId = await SeedAgentAsync();
        await SeedTaskAsync(agentId, TaskState.Working, age: Window + TimeSpan.FromMinutes(1));

        var report = await DetectAsync();

        Assert.Equal(1, report.CountFor(nameof(TaskState.Working)));
        Assert.Equal(0, report.CountFor(nameof(TaskState.Submitted)));
    }

    /// <summary>
    /// T3 — o par "sem item" da convenção 5, escrito para NÃO reprovar por
    /// vacuidade.
    /// </summary>
    /// <remarks>
    /// Um guarda de "nada a reportar" que rodasse sobre store <b>vazio</b>
    /// ficaria verde com e sem a implementação, porque nunca chegaria à
    /// comparação — é a quinta forma da convenção 15, já paga nesta base pela
    /// checagem de consistência de índice de embedding. Por isso o cenário
    /// semeia linhas de verdade (terminais antigas <b>e</b> não-terminais
    /// recentes) e <b>afirma a precondição</b> de store povoado antes de afirmar
    /// a ausência de achado.
    /// </remarks>
    [Fact]
    public async Task Detect_WithPopulatedStoreButNothingAged_ReportsNoFindings()
    {
        await ClearTasksAsync();
        var agentId = await SeedAgentAsync();
        await SeedTaskAsync(agentId, TaskState.Completed, age: TimeSpan.FromDays(3));
        await SeedTaskAsync(agentId, TaskState.Failed, age: TimeSpan.FromDays(2));
        await SeedTaskAsync(agentId, TaskState.Submitted, age: TimeSpan.FromSeconds(5));
        await SeedTaskAsync(agentId, TaskState.Working, age: TimeSpan.FromSeconds(5));

        await using var dbContext = CreateDbContext();
        var seeded = await dbContext.A2ATasks.CountAsync();
        Assert.Equal(4, seeded);

        var report = await DetectAsync();

        Assert.False(report.HasFindings);
        Assert.Null(report.OldestTaskId);
        Assert.Null(report.OldestAge);
    }

    [Fact]
    public async Task Detect_WithTerminalTaskFarOlderThanWindow_NeverReportsIt()
    {
        await ClearTasksAsync();
        var agentId = await SeedAgentAsync();
        await SeedTaskAsync(agentId, TaskState.Completed, age: TimeSpan.FromDays(30));
        await SeedTaskAsync(agentId, TaskState.Failed, age: TimeSpan.FromDays(30));
        await SeedTaskAsync(agentId, TaskState.Rejected, age: TimeSpan.FromDays(30));
        await SeedTaskAsync(agentId, TaskState.Canceled, age: TimeSpan.FromDays(30));

        var report = await DetectAsync();

        Assert.False(report.HasFindings);
    }

    /// <summary>
    /// T5 — a janela não é valor próprio: é o timeout de delegação, lido em
    /// runtime. Mudar o timeout move o corte, e é isso que impede a referência
    /// de envelhecer separada do estado que a mediu (convenção 22).
    /// </summary>
    [Fact]
    public async Task Detect_WindowFollowsTheConfiguredDelegationTimeout()
    {
        await ClearTasksAsync();
        var agentId = await SeedAgentAsync();
        await SeedTaskAsync(agentId, TaskState.Submitted, age: TimeSpan.FromSeconds(60));

        var withLongWindow = await DetectAsync(window: TimeSpan.FromSeconds(120));
        var withShortWindow = await DetectAsync(window: TimeSpan.FromSeconds(30));

        Assert.False(withLongWindow.HasFindings);
        Assert.Equal(1, withShortWindow.CountFor(nameof(TaskState.Submitted)));
        Assert.Equal(TimeSpan.FromSeconds(30), withShortWindow.Window);
    }

    /// <summary>
    /// T6 — a negativa de D3, com teste próprio para não sumir dentro de um
    /// positivo. O relatório e a emissão descrevem a observação (estado, idade,
    /// janela) e NÃO afirmam que a task está travada: um turno com várias
    /// chamadas de tool de delegação ultrapassa a janela legitimamente, e o
    /// sistema não distingue os dois casos.
    /// </summary>
    [Fact]
    public async Task Sweep_WithAgedTask_DescribesTheObservationWithoutClaimingTheTaskIsStuck()
    {
        await ClearTasksAsync();
        var agentId = await SeedAgentAsync();
        await SeedTaskAsync(agentId, TaskState.Submitted, age: Window + TimeSpan.FromMinutes(5));

        var logs = new List<string>();
        using var host = BuildHost(logs, sweepInterval: TimeSpan.FromMilliseconds(200));
        await host.StartAsync();

        string finding;
        try
        {
            finding = await PollUntilLoggedAsync(logs, "Tasks não-terminais além da janela");
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Contains(nameof(TaskState.Submitted), finding);
        Assert.Contains(Window.ToString(), finding);
        foreach (var verdict in (string[])["travada", "travadas", "presa", "presas", "defeito", "defeituosa"])
        {
            Assert.DoesNotContain(verdict, finding, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// T7 — sobre o host composto: a varredura tica e se anuncia. Sem este
    /// registro, "nenhum achado" e "a varredura não está rodando" produziriam o
    /// mesmo silêncio, e ambiguidade num instrumento de diagnóstico é o defeito
    /// que ele existe para não ter.
    /// </summary>
    [Fact]
    public async Task Sweep_OnStartup_AnnouncesItselfWithWindowAndInterval()
    {
        await ClearTasksAsync();

        var logs = new List<string>();
        using var host = BuildHost(logs, sweepInterval: TimeSpan.FromMilliseconds(200));
        await host.StartAsync();

        string announcement;
        try
        {
            announcement = await PollUntilLoggedAsync(logs, "Varredura de tasks não-terminais ativa");
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Contains(Window.ToString(), announcement);
        Assert.Contains(TimeSpan.FromMilliseconds(200).ToString(), announcement);
    }

    /// <summary>
    /// T8 — convenção 4. A consulta é a chamada que mais realisticamente falha,
    /// e é ela que o cenário derruba (banco inalcançável): a falha é registrada,
    /// o processo continua de pé e o ciclo seguinte acontece.
    /// </summary>
    /// <remarks>
    /// Afirma <b>consequência</b>, não implementação — e a asserção que decide é
    /// a do <b>segundo</b> ciclo: um <c>catch</c> que engolisse a exceção e
    /// deixasse o laço morrer produziria exatamente um log de erro e nenhum
    /// depois.
    /// </remarks>
    [Fact]
    public async Task Sweep_WithFailingQuery_LogsAndKeepsCycling()
    {
        var logs = new List<string>();
        using var host = BuildHost(
            logs,
            sweepInterval: TimeSpan.FromMilliseconds(200),
            // Porta sem ninguém escutando: a consulta falha, o resto do host
            // sobe normalmente.
            connectionString: "Host=127.0.0.1;Port=1;Database=inexistente;Username=nobody;Password=nobody;Timeout=1;Command Timeout=1");
        await host.StartAsync();

        try
        {
            await PollUntilAsync(() => CountLogged(logs, "Falha ao varrer tasks não-terminais") >= 2);
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.True(CountLogged(logs, "Falha ao varrer tasks não-terminais") >= 2);
    }

    /// <summary>
    /// Esvazia <c>a2a_tasks</c> antes de cada cenário.
    /// </summary>
    /// <remarks>
    /// <b>Não é higiene genérica: é consequência direta do desenho do detector.</b>
    /// Ele lê a tabela <b>inteira</b>, de propósito (a leitura é global — é o que
    /// faz a série valer para decidir capacidade), então um guarda que afirme
    /// contagem global tem que <b>possuir</b> a tabela. Sem isso os cenários
    /// desta classe se contaminam entre si — observado, não previsto: a primeira
    /// rodada deu `Expected: 1, Actual: 2` porque o cenário anterior tinha
    /// semeado uma linha envelhecida que continuava lá.
    ///
    /// <para>
    /// Seguro porque os testes de uma classe correm em sequência (uma coleção
    /// por classe no xUnit) e a classe inteira está serializada com as outras
    /// pelo <see cref="WorkerHostCollection"/>; e os containers são próprios
    /// desta classe, então nada de fora escreve aqui.
    /// </para>
    /// </remarks>
    private async Task ClearTasksAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM a2a_tasks");
    }

    private async Task<NonTerminalTaskReport> DetectAsync(TimeSpan? window = null)
    {
        await using var dbContext = CreateDbContext();
        var detector = new NonTerminalTaskDetector(TimeProvider.System);
        return await detector.DetectAsync(dbContext, window ?? Window, CancellationToken.None);
    }

    private IHost BuildHost(List<string> logs, TimeSpan sweepInterval, string? connectionString = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["ConnectionStrings:Postgres"] = connectionString ?? fixture.Postgres.GetConnectionString();
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Logging.AddProvider(new CapturingLoggerProvider(logs, typeof(NonTerminalTaskDetectorService).FullName!));

        builder.Services.Configure<AgentDelegationToolOptions>(options => options.Timeout = Window);
        builder.Services.Configure<TaskDiagnosticsOptions>(options => options.SweepInterval = sweepInterval);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<NonTerminalTaskDetector>();
        builder.Services.AddHostedService<NonTerminalTaskDetectorService>();

        return builder.Build();
    }

    private async Task<Guid> SeedAgentAsync()
    {
        var agentId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();

        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {$"Agente {agentId:N}"}, {"Instruções de teste."}, {true}, {"openai"}, {"gpt-5.6-sol"}, {now}, {now})
             """);

        return agentId;
    }

    private async Task<string> SeedTaskAsync(Guid agentId, TaskState state, TimeSpan age)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var contextId = Guid.NewGuid().ToString("N");
        var statusTimestamp = DateTimeOffset.UtcNow - age;

        await using var dbContext = CreateDbContext();

        var agentTask = new AgentTask
        {
            Id = taskId,
            ContextId = contextId,
            Status = new TaskStatus { State = state, Timestamp = statusTimestamp },
        };

        var payload = JsonSerializer.Serialize(agentTask, A2AJsonUtilities.DefaultOptions);
        dbContext.A2ATasks.Add(new A2ATaskRecord(taskId, agentId, contextId, state.ToString(), statusTimestamp, payload));
        await dbContext.SaveChangesAsync();

        return taskId;
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private static int CountLogged(List<string> logs, string fragment)
    {
        lock (logs)
        {
            return logs.Count(message => message.Contains(fragment, StringComparison.Ordinal));
        }
    }

    private static async Task<string> PollUntilLoggedAsync(List<string> logs, string fragment, int maxAttempts = 50)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            lock (logs)
            {
                var message = logs.Find(candidate => candidate.Contains(fragment, StringComparison.Ordinal));
                if (message is not null)
                {
                    return message;
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Nenhum registro contendo '{fragment}' foi emitido a tempo.");
    }

    private static async Task PollUntilAsync(Func<bool> condition, int maxAttempts = 50)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("A condição esperada não foi satisfeita a tempo.");
    }

    // Quarta cópia deste mecanismo na suíte — as outras em
    // TimeZoneStartupValidationTests, TemporalContextMessageInstantTests e
    // AgentDelegationConcurrencyTests. Aqui basta o texto formatado (os guardas
    // afirmam a presença e a ausência de frases da emissão, não campos
    // estruturados), então não reusa a variante estruturada daquela classe.
    // O gatilho de extração para Support/ está registrado como item aberto.
    private sealed class CapturingLoggerProvider(List<string> messages, params string[] categoryPrefixes) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) =>
            Array.Exists(categoryPrefixes, prefix => categoryName.StartsWith(prefix, StringComparison.Ordinal))
                ? new CapturingLogger(messages)
                : NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
