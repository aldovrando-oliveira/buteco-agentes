using System.Text;
using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>Os vetores do RFC 4648 §10, sem o padding (D8).</summary>
public class Base32Tests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Rfc4648Vectors_WithoutPadding(string input, string expected)
    {
        Assert.Equal(expected, Base32.Encode(Encoding.ASCII.GetBytes(input)));
    }

    [Fact]
    public void TwentyBytes_AreThirtyTwoCharacters()
    {
        Assert.Equal(32, Base32.Encode(Totp.NewSecret()).Length);
    }
}
