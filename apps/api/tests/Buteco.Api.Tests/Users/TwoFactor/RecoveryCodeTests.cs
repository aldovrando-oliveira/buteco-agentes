using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// Os códigos de recuperação, sem banco (spec <c>two-factor-totp</c>, "Códigos de
/// recuperação de uso único, guardados só em hash"; design.md da change
/// verificacao-2-etapas-totp, D6). O uso único é do banco e fica nos testes de integração.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.5: com a
/// normalização sem as trocas de Crockford, <see cref="CodeTypedAnotherWay_IsTheSameCode"/>
/// reprova.
/// </para>
/// </summary>
public class RecoveryCodeTests
{
    [Fact]
    public void Generated_IsFourGroupsOfFour_FromTheCrockfordAlphabet()
    {
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$", RecoveryCode.Generate());
    }

    [Fact]
    public void ASet_IsTenDistinctCodes()
    {
        var set = RecoveryCode.GenerateSet();

        Assert.Equal(10, set.Count);
        Assert.Equal(10, set.Distinct().Count());
    }

    [Fact]
    public void CodeTypedAnotherWay_IsTheSameCode()
    {
        // Minúsculas, sem hífens, com espaços, a letra O no lugar do zero e o I no lugar do um.
        Assert.Equal("0123ABCD4567EFGH", RecoveryCode.Normalize("o i23 abcd4567efgh"));
        Assert.Equal(RecoveryCode.Hash("0123ABCD4567EFGH"), RecoveryCode.Hash(RecoveryCode.Normalize("0123-ABCD-4567-EFGH")!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("287082")]
    [InlineData("0123-ABCD-4567-EFG")]
    [InlineData("0123-ABCD-4567-EFGHJ")]
    [InlineData("0123-ABCD-4567-EFGU")]
    public void SomethingElse_IsNotARecoveryCode(string? input)
    {
        Assert.Null(RecoveryCode.Normalize(input));
    }

    [Fact]
    public void TheHash_IsSha256_NotTheCode()
    {
        var hash = RecoveryCode.Hash("0123ABCD4567EFGH");

        Assert.Equal(32, hash.Length);
        Assert.DoesNotContain("0123ABCD4567EFGH", Convert.ToHexString(hash));
    }
}
