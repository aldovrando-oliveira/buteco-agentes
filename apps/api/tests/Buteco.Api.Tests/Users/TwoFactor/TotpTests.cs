using System.Text;
using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O código pelo RFC 6238 (spec <c>two-factor-totp</c>, "Código TOTP pelo RFC 6238";
/// design.md da change verificacao-2-etapas-totp, D3 e D4). Os vetores são os SHA-1 do
/// Apêndice B, com a semente ASCII <c>12345678901234567890</c>, em 6 dígitos: o mesmo
/// inteiro truncado módulo 10⁶, porque 10⁶ divide os 10⁸ da tabela.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.1: com o contador em
/// little-endian, com a truncagem sem <c>&amp; 0x7f</c>, e com o código sem zeros à esquerda,
/// <see cref="RfcAppendixBVectors"/> reprova.
/// </para>
/// </summary>
public class TotpTests
{
    private static readonly byte[] RfcSeed = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void RfcAppendixBVectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, Totp.Compute(RfcSeed, step));
    }

    [Fact]
    public void CodeWithASpace_IsTheSameCode()
    {
        Assert.Equal("287082", Totp.Normalize("287 082"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("28708")]
    [InlineData("2870821")]
    [InlineData("28708a")]
    public void SomethingThatIsNotSixDigits_IsNotACode(string? input)
    {
        Assert.Null(Totp.Normalize(input));
    }

    [Fact]
    public void NewSecret_HasTheSizeOfTheHmacOutput()
    {
        Assert.Equal(20, Totp.NewSecret().Length);
    }
}
