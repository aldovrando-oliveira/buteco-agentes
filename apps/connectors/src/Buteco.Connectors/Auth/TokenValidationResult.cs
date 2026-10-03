namespace Buteco.Connectors.Auth;

public readonly record struct TokenValidationResult(bool IsValid, string? Subject)
{
    public static TokenValidationResult Invalid { get; } = new(false, null);
}
