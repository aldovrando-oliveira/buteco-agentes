using System.Diagnostics;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using Buteco.Api.Tests.Support;
using Npgsql;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Uma instância por ciclo de limpeza (spec <c>issuer-token-storage</c>; design.md da
/// change limpeza-tokens-openiddict, D5), no molde do <c>IdentityKeyRingConcurrencyTests</c>:
/// cada pruner é o de uma instância, e o que elas têm em comum é o banco e o lock.
///
/// <para>
/// Verificado contra o defeito (convenção 15): com a chave da limpeza trocada pela da
/// rotação no <c>IdentityTokenPruner</c>, o caso do lock da rotação preso devolve
/// <c>Skipped</c> e reprova (registro no tasks.md, 3.7).
/// </para>
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class IdentityTokenPruneConcurrencyTests(MigrationPostgresFixture postgres)
{
    // Mais que um lote de 1.000, para os ciclos concorrentes se cruzarem entre lotes.
    private const int Dead = 2_500;
    private const int Live = 500;

    [Fact]
    public async Task FourConcurrentCycles_RemoveEachDeadRowOnce_WithoutErrors()
    {
        var connectionString = await SeededDatabaseAsync();
        var pruners = Enumerable.Range(0, 4).Select(_ => IdentityTokenSeed.Instance(connectionString)).ToList();

        var stopwatch = Stopwatch.StartNew();
        var results = await Task.WhenAll(pruners.Select(pruner => pruner.RunCycleAsync(CancellationToken.None)));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"os quatro ciclos levaram {stopwatch.Elapsed}");
        Assert.Equal(Dead, results.Sum(result => result.Tokens));
        Assert.Equal(Live, await IdentityTokenSeed.TokenCountAsync(connectionString));
    }

    [Fact]
    public async Task WithThePruneLockHeld_TheCycleSkips_AndRemovesOnceItIsReleased()
    {
        var connectionString = await SeededDatabaseAsync();
        var pruner = IdentityTokenSeed.Instance(connectionString);

        await using (var holder = await HoldLockAsync(connectionString, IdentityTokenPrunePolicy.AdvisoryLockKey))
        {
            Assert.Equal(IdentityTokenPruneResult.Skipped, await pruner.RunCycleAsync(CancellationToken.None));
            Assert.Equal(Dead + Live, await IdentityTokenSeed.TokenCountAsync(connectionString));
        }

        Assert.Equal(IdentityTokenPruneResult.Pruned(tokens: Dead, authorizations: 0), await pruner.RunCycleAsync(CancellationToken.None));
        Assert.Equal(Live, await IdentityTokenSeed.TokenCountAsync(connectionString));
    }

    [Fact]
    public async Task WithTheRotationLockHeld_TheCycleStillRuns()
    {
        var connectionString = await SeededDatabaseAsync();
        var pruner = IdentityTokenSeed.Instance(connectionString);

        await using var holder = await HoldLockAsync(connectionString, IdentityKeyRotationPolicy.AdvisoryLockKey);

        Assert.Equal(IdentityTokenPruneResult.Pruned(tokens: Dead, authorizations: 0), await pruner.RunCycleAsync(CancellationToken.None));
    }

    private async Task<string> SeededDatabaseAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await IdentityTokenSeed.MigrateAsync(connectionString);
        await IdentityTokenSeed.DeadAndLiveAsync(connectionString, Dead, Live);
        return connectionString;
    }

    /// <summary>O lock preso por outra transação, até o descarte devolvido.</summary>
    private static async Task<LockHolder> HoldLockAsync(string connectionString, long key)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({key})", connection, transaction))
        {
            await command.ExecuteNonQueryAsync();
        }

        return new LockHolder(connection, transaction);
    }

    private sealed class LockHolder(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}
