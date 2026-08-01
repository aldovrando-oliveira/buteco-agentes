using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Workers.Agents;

/// <summary>
/// Serializa o processamento de mensagens do mesmo (agentId, contextId) entre
/// instâncias concorrentes do worker via <c>pg_advisory_lock</c> — ver
/// design.md da change apps-workers-historico-conversa, Decisão 7. Mantém uma
/// conexão Postgres dedicada aberta enquanto o lock estiver em posse, à parte
/// dos <see cref="Microsoft.Extensions.DependencyInjection.IServiceScope"/>/
/// <see cref="AppDbContext"/> de vida curta usados pelas demais operações de
/// store — sem isso, o lock (de sessão, não de transação) seria liberado
/// junto com a conexão de cada operação individual, antes do fim da seção
/// crítica. Se o processo morrer sem chamar <see cref="DisposeAsync"/>, o
/// Postgres libera o lock automaticamente ao encerrar essa conexão.
/// </summary>
public sealed class ConversationContextLock : IAsyncDisposable
{
    private readonly IServiceScope _scope;
    private readonly AppDbContext _dbContext;
    private readonly Guid _agentId;
    private readonly string _contextId;
    private bool _released;

    private ConversationContextLock(IServiceScope scope, AppDbContext dbContext, Guid agentId, string contextId)
    {
        _scope = scope;
        _dbContext = dbContext;
        _agentId = agentId;
        _contextId = contextId;
    }

    public static async Task<ConversationContextLock> AcquireAsync(
        IServiceScopeFactory scopeFactory,
        Guid agentId,
        string contextId,
        CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_lock(hashtext({agentId.ToString()}), hashtext({contextId}))",
            cancellationToken);

        return new ConversationContextLock(scope, dbContext, agentId, contextId);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_released)
        {
            _released = true;

            // Libera com CancellationToken.None de propósito: o unlock deve
            // ser tentado mesmo que o token da execução original já tenha
            // sido cancelado.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_unlock(hashtext({_agentId.ToString()}), hashtext({_contextId}))");
            await _dbContext.Database.CloseConnectionAsync();
        }

        await _dbContext.DisposeAsync();
        _scope.Dispose();
    }
}
