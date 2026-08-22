namespace Buteco.Api.Auth;

public sealed class TokenSigningOptions
{
    public const string SectionName = "Auth";

    public string TokenSigningKey { get; set; } = "";

    // Default vive no tipo, não no appsettings.json — a chave
    // Auth:OperatorTokenLifetime pode ficar ausente de qualquer
    // appsettings/env sem tratamento especial em TokenService
    // (design.md, Decision 1).
    public TimeSpan OperatorTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);
}
