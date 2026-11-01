namespace Buteco.Api.Auth;

public sealed class TokenSigningOptions
{
    public const string SectionName = "Auth";

    public string TokenSigningKey { get; set; } = "";
}
