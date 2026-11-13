using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;
using Buteco.Api.Users.TwoFactor;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// A confirmação de quem chama nas rotas que transformam um access token roubado (15 min) em acesso
/// permanente: criar o convite, gerar novo link do convite, gerar o link de redefinição, promover a Admin,
/// criar a aplicação externa, gerar segredo e conectar um provedor federado (design.md da change
/// convites-redefinicao-senha, D1, P10, decisão do dono de 02/11/2026; e da change
/// provedores-federados-google, D8, Q1).
///
/// <list type="bullet">
/// <item><b>quem tem senha</b> confirma com a senha (<c>senha</c>), como o <c>setup</c> das 2 etapas. O
/// código não substitui a senha de quem a tem: sem a senha certa, <c>401</c> "Senha inválida.";</item>
/// <item><b>quem não tem senha</b> (só entra por provedor federado) confirma com o código do app
/// autenticador (<c>codigo</c>), com o uso único do passo; o código de recuperação não vale aqui.
/// Recusado, <c>401</c> "Código inválido."; segredo que não decifra, <c>500</c>.</item>
/// </list>
///
/// <para>
/// Toda recusa conta no bloqueio do e-mail <b>de quem chama</b>; o acerto não zera a contagem. O
/// <c>423</c> do bloqueio vem antes, por <see cref="LoginThrottleResponses.LockedAsync"/>, sem conferir nada.
/// </para>
/// </summary>
public static class CallerPassword
{
    public const string InvalidMessage = "Senha inválida.";

    public const string InvalidCodeMessage = "Código inválido.";

    public const string UnreadableSecretMessage =
        "Não foi possível conferir o código do app autenticador. Fale com o administrador.";

    public static async Task<IResult?> RejectAsync(
        User caller,
        string? password,
        string? code,
        PasswordHasher hasher,
        TwoFactorVerifier verifier,
        LoginThrottleStore throttle,
        CancellationToken cancellationToken)
    {
        if (caller.PasswordHash is not null)
        {
            if (!string.IsNullOrEmpty(password) &&
                hasher.Verify(password, caller.PasswordHash, caller.PasswordHashIsBootstrapLegacy) != PasswordVerificationResult.Failed)
            {
                return null;
            }

            await throttle.RecordFailureAsync(caller.NormalizedEmail, cancellationToken);
            return TypedResults.Problem(title: InvalidMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        switch (await verifier.VerifyAppCodeAsync(caller, code, cancellationToken))
        {
            case TwoFactorCheck.Accepted:
                return null;
            case TwoFactorCheck.SecretUnreadable:
                return TypedResults.Problem(title: UnreadableSecretMessage, statusCode: StatusCodes.Status500InternalServerError);
        }

        await throttle.RecordFailureAsync(caller.NormalizedEmail, cancellationToken);
        return TypedResults.Problem(title: InvalidCodeMessage, statusCode: StatusCodes.Status401Unauthorized);
    }
}
