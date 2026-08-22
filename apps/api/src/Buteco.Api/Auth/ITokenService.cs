namespace Buteco.Api.Auth;

public interface ITokenService
{
    (string Token, DateTimeOffset ExpiresAt) Issue(string subject, TimeSpan lifetime);

    TokenValidationResult Validate(string token);
}
