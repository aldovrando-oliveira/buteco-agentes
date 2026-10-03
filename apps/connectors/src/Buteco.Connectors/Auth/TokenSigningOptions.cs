namespace Buteco.Connectors.Auth;

public sealed class TokenSigningOptions
{
    public const string SectionName = "Auth";

    // Mesmo valor de apps/api e apps/inbox. Ausente ou vazia, o boot falha
    // (Program.cs).
    public string TokenSigningKey { get; set; } = "";
}
