using Buteco.Api.Users.Login;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests.Users;

/// <summary>
/// As regras do bloqueio, sem banco (spec <c>local-users</c>, "Bloqueio progressivo por
/// e-mail"; design.md da change usuarios-locais-login-emissor, D4). Um caso por cenário.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.3:
/// <list type="bullet">
/// <item>com a reincidência exigindo 5 falhas de novo, o caso
/// <see cref="AfterAnExpiredLock_OneFailureLocksForDouble"/> reprova;</item>
/// <item>com o <see cref="LoginThrottlePolicy.Effective"/> ignorando as 24 h (só a limpeza
/// zeraria), o caso <see cref="TwentyFourHoursWithoutFailure_StartOver_EvenIfTheRowIsStillThere"/>
/// reprova.</item>
/// </list>
/// </para>
/// </summary>
public class LoginThrottlePolicyTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    private LoginThrottleState Fail(LoginThrottleState state) => LoginThrottlePolicy.RecordFailure(state, _clock.GetUtcNow());

    private TimeSpan? Remaining(LoginThrottleState state) => LoginThrottlePolicy.LockRemaining(state, _clock.GetUtcNow());

    private LoginThrottleState FailTimes(LoginThrottleState state, int times)
    {
        for (var i = 0; i < times; i++)
        {
            state = Fail(state);
        }

        return state;
    }

    [Fact]
    public void FourFailures_DoNotLock_TheFifthLocksForFiveMinutes()
    {
        var state = FailTimes(LoginThrottleState.Empty, 4);
        Assert.Null(Remaining(state));

        state = Fail(state);
        Assert.Equal(TimeSpan.FromMinutes(5), Remaining(state));
    }

    [Fact]
    public void TheLockEnds_AfterFiveMinutes()
    {
        var state = FailTimes(LoginThrottleState.Empty, 5);

        _clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        Assert.NotNull(Remaining(state));

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(Remaining(state));
    }

    [Fact]
    public void AfterAnExpiredLock_OneFailureLocksForDouble()
    {
        var state = FailTimes(LoginThrottleState.Empty, 5);
        _clock.Advance(TimeSpan.FromMinutes(5));

        state = Fail(state);

        Assert.Equal(TimeSpan.FromMinutes(10), Remaining(state));
    }

    [Fact]
    public void Repeated_TheLocksDouble_UpToSixtyMinutes()
    {
        var state = FailTimes(LoginThrottleState.Empty, 5);
        var durations = new List<TimeSpan>();

        for (var i = 0; i < 5; i++)
        {
            _clock.Advance(Remaining(state)!.Value);
            state = Fail(state);
            durations.Add(Remaining(state)!.Value);
        }

        Assert.Equal(
            new[] { 10, 20, 40, 60, 60 }.Select(minutes => TimeSpan.FromMinutes(minutes)),
            durations);
    }

    [Fact]
    public void TwentyFourHoursWithoutFailure_StartOver_EvenIfTheRowIsStillThere()
    {
        // Um bloqueio de 10 min, e a linha continua com LastLockMinutes = 10.
        var state = FailTimes(LoginThrottleState.Empty, 5);
        _clock.Advance(TimeSpan.FromMinutes(5));
        state = Fail(state);
        Assert.Equal(10, state.LastLockMinutes);

        _clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));

        state = FailTimes(state, 4);
        Assert.Null(Remaining(state));

        state = Fail(state);
        Assert.Equal(TimeSpan.FromMinutes(5), Remaining(state));
    }

    [Fact]
    public void WithinTwentyFourHours_TheEscalationIsKept()
    {
        var state = FailTimes(LoginThrottleState.Empty, 5);
        _clock.Advance(TimeSpan.FromHours(23));

        state = Fail(state);

        Assert.Equal(TimeSpan.FromMinutes(10), Remaining(state));
    }

    [Fact]
    public void FailuresSpreadOverMoreThanADay_DoNotAccumulate()
    {
        var state = FailTimes(LoginThrottleState.Empty, 4);
        _clock.Advance(TimeSpan.FromHours(25));

        state = Fail(state);

        Assert.Equal(1, state.ConsecutiveFailures);
        Assert.Null(Remaining(state));
    }

    [Fact]
    public void FailureLimits_AreThePolicyConstants()
    {
        Assert.Equal(5, LoginThrottlePolicy.FailuresBeforeFirstLock);
        Assert.Equal(TimeSpan.FromMinutes(5), LoginThrottlePolicy.FirstLock);
        Assert.Equal(TimeSpan.FromMinutes(60), LoginThrottlePolicy.MaxLock);
        Assert.Equal(TimeSpan.FromHours(24), LoginThrottlePolicy.ResetAfter);
    }
}
