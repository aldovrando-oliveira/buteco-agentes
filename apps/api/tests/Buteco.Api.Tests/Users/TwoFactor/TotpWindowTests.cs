using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// A janela de um passo para cada lado (spec <c>two-factor-totp</c>, "Janela de um passo e
/// recusa do código já usado"; design.md da change verificacao-2-etapas-totp, D3). O reuso
/// é do banco e fica nos testes de integração.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.2: com a janela de
/// ±2, <see cref="TwoStepsAway_AreRefused"/> reprova.
/// </para>
/// </summary>
public class TotpWindowTests
{
    private static readonly byte[] Secret = Totp.NewSecret();
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 15, TimeSpan.Zero);
    private static readonly long Current = Totp.StepAt(Now);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void NeighbouringSteps_AreAccepted_AndTheMatchedStepComesBack(int offset)
    {
        var code = Totp.Compute(Secret, Current + offset);

        Assert.Equal(Current + offset, Totp.Match(Secret, code, Now));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public void TwoStepsAway_AreRefused(int offset)
    {
        var code = Totp.Compute(Secret, Current + offset);

        Assert.Null(Totp.Match(Secret, code, Now));
    }
}
