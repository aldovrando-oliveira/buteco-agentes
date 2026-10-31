using Buteco.Api.Users;

namespace Buteco.Api.Tests.Users;

/// <summary>O identificador de login (spec <c>local-users</c>; design.md da change usuarios-locais-login-emissor, D4).</summary>
public class EmailNormalizationTests
{
    [Theory]
    [InlineData("Maria@Empresa.com", "maria@empresa.com")]
    [InlineData("  maria@empresa.com  ", "maria@empresa.com")]
    [InlineData("\tMARIA@EMPRESA.COM\n", "maria@empresa.com")]
    public void Normalize_TrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, EmailNormalization.Normalize(input));
    }

    [Fact]
    public void User_KeepsTheTypedEmail_AndTheNormalizedOne()
    {
        var user = User.Create("  Maria@Empresa.com ", "Maria", passwordHash: null, DateTimeOffset.UtcNow);

        Assert.Equal("Maria@Empresa.com", user.Email);
        Assert.Equal("maria@empresa.com", user.NormalizedEmail);
    }
}
