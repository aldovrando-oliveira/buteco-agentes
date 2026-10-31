using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.Users.Login;

/// <summary>
/// Leitura e escrita de <c>identity_login_throttle</c> (design.md da change
/// usuarios-locais-login-emissor, D4). A falha é registrada sob <c>SELECT … FOR UPDATE</c>
/// na linha do e-mail: duas falhas simultâneas do mesmo e-mail contam duas, e não uma.
/// </summary>
public sealed class LoginThrottleStore(AppDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<TimeSpan?> GetLockRemainingAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var row = await dbContext.LoginThrottles.AsNoTracking()
            .SingleOrDefaultAsync(throttle => throttle.NormalizedEmail == normalizedEmail, cancellationToken);

        return row is null ? null : LoginThrottlePolicy.LockRemaining(row.ToState(), timeProvider.GetUtcNow());
    }

    public async Task RecordFailureAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO identity_login_throttle (normalized_email, consecutive_failures, last_lock_minutes, last_failure_at) VALUES ({normalizedEmail}, 0, 0, {now}) ON CONFLICT (normalized_email) DO NOTHING",
            cancellationToken);

        // ToList, e não Single: o EF comporia o FOR UPDATE numa subconsulta com LIMIT.
        var row = (await dbContext.LoginThrottles
            .FromSqlInterpolated($"SELECT * FROM identity_login_throttle WHERE normalized_email = {normalizedEmail} FOR UPDATE")
            .ToListAsync(cancellationToken)).Single();

        // A linha recém-inserida não tem falha: o "agora" de last_failure_at é só para
        // satisfazer o NOT NULL, e o estado de partida é vazio.
        var state = row.ConsecutiveFailures == 0 && row.LastLockMinutes == 0 ? LoginThrottleState.Empty : row.ToState();
        row.Apply(LoginThrottlePolicy.RecordFailure(state, now));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task ResetAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.LoginThrottles.Where(throttle => throttle.NormalizedEmail == normalizedEmail)
            .ExecuteDeleteAsync(cancellationToken);
}
