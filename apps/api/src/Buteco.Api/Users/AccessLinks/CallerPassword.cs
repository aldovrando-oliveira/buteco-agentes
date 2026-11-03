using Buteco.Api.Users.Login;
using Buteco.Api.Users.Passwords;

namespace Buteco.Api.Users.AccessLinks;

/// <summary>
/// A senha de quem chama nas rotas que transformam um access token roubado (15 min) em acesso
/// permanente: criar o convite, gerar novo link do convite e gerar o link de redefinição
/// (design.md da change convites-redefinicao-senha, D1, P10, decisão do dono de 02/11/2026). Como
/// o <c>setup</c> das 2 etapas: senha ausente ou errada → <c>401</c>, contada no bloqueio do
/// e-mail <b>de quem chama</b>; o acerto não zera a contagem. O <c>423</c> do bloqueio vem antes,
/// por <see cref="LoginThrottleResponses.LockedAsync"/>, sem conferir nada.
/// </summary>
public static class CallerPassword
{
    public const string InvalidMessage = "Senha inválida.";

    public static async Task<IResult?> RejectAsync(
        User caller, string? password, PasswordHasher hasher, LoginThrottleStore throttle, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(password) &&
            hasher.Verify(password, caller.PasswordHash, caller.PasswordHashIsBootstrapLegacy) != PasswordVerificationResult.Failed)
        {
            return null;
        }

        await throttle.RecordFailureAsync(caller.NormalizedEmail, cancellationToken);
        return TypedResults.Problem(title: InvalidMessage, statusCode: StatusCodes.Status401Unauthorized);
    }
}
