using System.Text;
using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Tests.Users.TwoFactor;

/// <summary>
/// O URI do app autenticador (spec <c>two-factor-totp</c>, "Cadastro em dois tempos";
/// design.md da change verificacao-2-etapas-totp, D8).
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.4: com o e-mail sem
/// codificar, <see cref="EmailWithAPlus_IsEncodedInTheLabel"/> reprova.
/// </para>
/// </summary>
public class OtpAuthUriTests
{
    private static readonly byte[] Secret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Fact]
    public void Uri_HasTheIssuer_TheAccount_AndTheFourParameters()
    {
        Assert.Equal(
            "otpauth://totp/Buteco%20Agentes:ana%40exemplo.test" +
            "?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=Buteco%20Agentes&algorithm=SHA1&digits=6&period=30",
            OtpAuthUri.Build("ana@exemplo.test", Secret));
    }

    [Fact]
    public void EmailWithAPlus_IsEncodedInTheLabel()
    {
        var uri = OtpAuthUri.Build("ana+painel@exemplo.test", Secret);

        Assert.StartsWith("otpauth://totp/Buteco%20Agentes:ana%2Bpainel%40exemplo.test?", uri);
    }
}
