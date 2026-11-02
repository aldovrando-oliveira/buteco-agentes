using Buteco.Api.Users.Passwords;

namespace Buteco.Api.Tests.Users.Passwords;

/// <summary>
/// "Política de senha" (spec <c>user-account</c>; design.md da change rotas-conta-usuarios, D3).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.1: com o limite
/// superior em <c>&lt;</c> no lugar de <c>&lt;=</c>, <see cref="TheLimitsAreAccepted"/> reprova
/// no caso dos 128.
/// </para>
/// </summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData(11)]
    [InlineData(129)]
    public void OutsideTheLimits_IsRefused(int length)
    {
        Assert.False(PasswordPolicy.IsSatisfiedBy(new string('a', length)));
    }

    [Theory]
    [InlineData(12)]
    [InlineData(128)]
    public void TheLimitsAreAccepted(int length)
    {
        Assert.True(PasswordPolicy.IsSatisfiedBy(new string('a', length)));
    }

    [Fact]
    public void SpacesCount_IncludingAtTheEnds()
    {
        // 12 com os espaços das pontas; sem eles seriam 10.
        Assert.True(PasswordPolicy.IsSatisfiedBy(" senha-curt "));
        Assert.True(PasswordPolicy.IsSatisfiedBy(new string(' ', 12)));
    }

    [Fact]
    public void NoCompositionRule()
    {
        Assert.True(PasswordPolicy.IsSatisfiedBy("aaaaaaaaaaaa"));
    }

    [Fact]
    public void Missing_IsRefused()
    {
        Assert.False(PasswordPolicy.IsSatisfiedBy(null));
    }
}
