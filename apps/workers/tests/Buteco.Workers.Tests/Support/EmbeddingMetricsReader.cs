using Buteco.Workers.EmbeddingMetrics.Entities;
using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Workers.Tests.Support;

/// <summary>
/// Leitura das duas tabelas de métrica de embedding, para os guardas da change
/// <c>metricas-embedding-coleta</c> — quatro classes de teste leem as mesmas
/// tabelas, por isso o helper é compartilhado. Mesmo papel de
/// <see cref="ExecutionMetricsReader"/>, e o mesmo motivo de existir.
/// </summary>
/// <remarks>
/// <para>
/// <b>Os guardas de INDEXAÇÃO não esperam.</b> A escrita acontece dentro do
/// <c>IndexAsync</c> que o teste <c>await</c>a — quando ele retorna, as linhas já
/// estão no banco. É diferente da etapa 1, onde o fechamento corre depois do
/// estado terminal da task e o teste precisa de sondagem.
/// </para>
///
/// <para>
/// <b>Os guardas de BUSCA esperam</b>, e por isso
/// <see cref="WaitForSearchCallsAsync"/> existe: a linha da busca é acumulada no
/// <c>ExecutionMetricsScope</c> e gravada no <b>fechamento da execução</b>, que
/// roda depois do estado terminal da task. Ler assim que a task fica
/// <c>Completed</c> corre contra o fechamento e reprova de forma intermitente —
/// é a mesma armadilha que <see cref="ExecutionMetricsReader"/> documenta.
/// </para>
/// </remarks>
public static class EmbeddingMetricsReader
{
    public static async Task<IReadOnlyList<EmbeddingCall>> CallsForAttemptAsync(string connectionString, Guid attemptId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await dbContext.EmbeddingCalls.AsNoTracking()
            .Where(row => row.KnowledgeIndexingAttemptId == attemptId)
            .ToListAsync();
    }

    public static async Task<IReadOnlyList<EmbeddingCall>> CallsForDocumentAsync(string connectionString, Guid documentId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await (
            from call in dbContext.EmbeddingCalls.AsNoTracking()
            join attempt in dbContext.KnowledgeIndexingAttempts.AsNoTracking()
                on call.KnowledgeIndexingAttemptId equals attempt.Id
            where attempt.KnowledgeDocumentId == documentId
            orderby attempt.Attempt
            select call).ToListAsync();
    }

    public static async Task<IReadOnlyList<EmbeddingCall>> CallsForTaskAsync(string connectionString, string taskId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await dbContext.EmbeddingCalls.AsNoTracking()
            .Where(row => row.TaskId == taskId)
            .ToListAsync();
    }

    /// <summary>
    /// Sonda até a execução gravar ao menos <paramref name="expected"/> linhas de
    /// busca. Ancorada no dado persistido, e não num <c>Task.Delay</c> fixo.
    /// </summary>
    public static async Task<IReadOnlyList<EmbeddingCall>> WaitForSearchCallsAsync(
        string connectionString, string taskId, int expected = 1, int maxAttempts = 75)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var calls = await CallsForTaskAsync(connectionString, taskId);
            if (calls.Count >= expected)
            {
                return calls;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"A execução da task '{taskId}' não gravou {expected} linha(s) de embedding de busca a tempo.");
    }

    public static async Task<IReadOnlyList<KnowledgeIndexingAttempt>> AttemptsAsync(string connectionString, Guid documentId)
    {
        await using var dbContext = CreateDbContext(connectionString);
        return await dbContext.KnowledgeIndexingAttempts.AsNoTracking()
            .Where(row => row.KnowledgeDocumentId == documentId)
            .OrderBy(row => row.Attempt)
            .ToListAsync();
    }

    public static async Task<KnowledgeIndexingAttempt> SingleAttemptAsync(string connectionString, Guid documentId)
    {
        var attempts = await AttemptsAsync(connectionString, documentId);
        return Assert.Single(attempts);
    }

    private static AppDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options);
}
