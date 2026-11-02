namespace Buteco.Api.Users.TwoFactor;

/// <summary>
/// O URI que o app autenticador lê pelo QR (D8, decisão do dono P7): o issuer fixo
/// "Buteco Agentes" e a conta = e-mail, os dois codificados no rótulo e no parâmetro
/// <c>issuer</c>. O QR é do painel (#183).
/// </summary>
public static class OtpAuthUri
{
    public const string Issuer = "Buteco Agentes";

    public static string Build(string email, ReadOnlySpan<byte> secret)
    {
        var issuer = Uri.EscapeDataString(Issuer);
        return $"otpauth://totp/{issuer}:{Uri.EscapeDataString(email)}" +
               $"?secret={Base32.Encode(secret)}&issuer={issuer}&algorithm=SHA1&digits={Totp.Digits}&period={Totp.StepSeconds}";
    }
}
