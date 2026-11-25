extern alias ApiAssembly;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ApiAppDbContext = ApiAssembly::Buteco.Api.Infrastructure.AppDbContext;

namespace InboxOrchestratorRoundTrip.Tests.Support;

/// <summary>
/// As tasks de um agente lidas de <c>a2a_tasks</c> do <c>apps/api</c> (change rt-inbox-espera-taskid,
/// D1, D2 e D4). É o dado durável que substitui o <c>taskId</c> do registro de despacho do
/// <c>apps/inbox</c>: esse só existe entre a gravação pelo inbox e o fechamento pelo push, e a
/// task pode terminar dentro de um único intervalo de consulta. A linha daqui nasce no
/// <c>Submit</c> e nada a apaga no fluxo de um despacho (D0).
///
/// <para>
/// Cada caso cria o próprio agente, então as tasks dele são as do caso. Uma segunda task do
/// mesmo pedido (a reintentativa de transporte do <c>SendMessage</c>, #314) não é escolhida
/// nem ignorada: <see cref="Single"/> reprova com a lista (D2).
/// </para>
/// </summary>
internal static class AgentTasks
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public sealed record Row(string TaskId, string State)
    {
        /// <summary>O mesmo <c>IsTerminal()</c> do SDK que a reconciliação do inbox usa.</summary>
        public bool IsTerminal =>
            Enum.TryParse<global::A2A.TaskState>(State, out var state) && global::A2A.TaskStateExtensions.IsTerminal(state);
    }

    public static async Task<IReadOnlyList<Row>> ListAsync(RoundTripFixture fixture, Guid agentId)
    {
        using var scope = fixture.ApiFactory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApiAppDbContext>();

        return await dbContext.A2ATasks.AsNoTracking()
            .Where(task => task.AgentId == agentId)
            .OrderBy(task => task.TaskId)
            .Select(task => new Row(task.TaskId, task.State))
            .ToListAsync();
    }

    /// <summary>
    /// Consulta as tasks do agente até <paramref name="isDone"/>. As linhas não somem, então a
    /// condição não tem janela: vista uma vez, continua verdadeira.
    /// </summary>
    public static async Task<IReadOnlyList<Row>> WaitUntilAsync(
        RoundTripFixture fixture, Guid agentId, Func<IReadOnlyList<Row>, bool> isDone, TimeSpan timeout, string condition)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var rows = await ListAsync(fixture, agentId);
            if (isDone(rows))
            {
                return rows;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Condição não satisfeita a tempo: {condition}. Tasks do agente {agentId}: {Describe(rows)}.");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>A condição positiva da D1: ao menos uma task do agente em estado final.</summary>
    public static Task<IReadOnlyList<Row>> WaitForTerminalAsync(RoundTripFixture fixture, Guid agentId, TimeSpan timeout) =>
        WaitUntilAsync(fixture, agentId, rows => rows.Any(row => row.IsTerminal), timeout, "uma task do agente em estado final");

    /// <summary>
    /// A task única do agente. Com zero ou mais de uma, reprova com o id e o estado de cada task
    /// do agente na mensagem (D2).
    /// </summary>
    public static Row Single(IReadOnlyList<Row> rows, Guid agentId)
    {
        Assert.True(rows.Count == 1, $"Esperava uma só task do agente {agentId}; há {rows.Count}: {Describe(rows)}.");
        return rows[0];
    }

    /// <summary><see cref="Single"/> sobre as tasks lidas agora.</summary>
    public static async Task<string> SingleTaskIdAsync(RoundTripFixture fixture, Guid agentId) =>
        Single(await ListAsync(fixture, agentId), agentId).TaskId;

    public static string Describe(IEnumerable<Row> rows)
    {
        var list = rows.Select(row => $"{row.TaskId} ({row.State})").ToList();
        return list.Count == 0 ? "nenhuma" : string.Join(", ", list);
    }
}
