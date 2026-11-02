using System.Globalization;

namespace Buteco.Api.Users.Login;

/// <summary>
/// O <c>423</c> com <c>Retry-After</c> das rotas autenticadas que conferem senha ou código da
/// própria pessoa (as 2 etapas e a troca de senha): durante o bloqueio do e-mail, nada é
/// conferido, como no login.
/// </summary>
public static class LoginThrottleResponses
{
    public static async Task<IResult?> LockedAsync(HttpContext httpContext, LoginThrottleStore throttle, User user, CancellationToken cancellationToken)
    {
        if (await throttle.GetLockRemainingAsync(user.NormalizedEmail, cancellationToken) is not { } remaining)
        {
            return null;
        }

        httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(remaining.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return TypedResults.Problem(title: SessionEndpoints.LockedMessage, statusCode: StatusCodes.Status423Locked);
    }
}
