namespace Buteco.Api.Auth.Responses;

public record LoginResponse(string Token, DateTimeOffset ExpiresAt);
