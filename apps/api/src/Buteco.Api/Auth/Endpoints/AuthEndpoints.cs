using Buteco.Api.Auth.Requests;
using Buteco.Api.Auth.Responses;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Auth.Endpoints;

public static class AuthEndpoints
{
    // Mesma mensagem genérica para usuário ou senha incorretos, sem
    // indicar qual campo errou — mesmo padrão de
    // PushNotificationEndpoints.ReceiveAsync (apps/inbox), que não
    // distingue "task desconhecida" de "token divergente" (design.md,
    // Decision 5).
    private const string InvalidCredentialsMessage = "Usuário ou senha inválidos.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint));

        return app;
    }

    private static Results<Ok<LoginResponse>, ProblemHttpResult> LoginAsync(
        LoginRequest request,
        IOptions<OperatorCredentialOptions> credentialOptions,
        IOptions<TokenSigningOptions> tokenOptions,
        ITokenService tokenService)
    {
        var credential = credentialOptions.Value;

        var credentialsMatch =
            !string.IsNullOrEmpty(request.Username) &&
            !string.IsNullOrEmpty(request.Password) &&
            request.Username == credential.OperatorUsername &&
            OperatorPasswordHasher.Verify(request.Password, credential.OperatorPasswordHash);

        if (!credentialsMatch)
        {
            return TypedResults.Problem(title: InvalidCredentialsMessage, statusCode: StatusCodes.Status401Unauthorized);
        }

        var (token, expiresAt) = tokenService.Issue("operator", tokenOptions.Value.OperatorTokenLifetime);
        return TypedResults.Ok(new LoginResponse(token, expiresAt));
    }
}
