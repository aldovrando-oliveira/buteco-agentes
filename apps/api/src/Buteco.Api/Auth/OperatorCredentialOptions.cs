namespace Buteco.Api.Auth;

public sealed class OperatorCredentialOptions
{
    public const string SectionName = "Auth";

    public string OperatorUsername { get; set; } = "";

    // Formato composto "{iterations}.{saltBase64}.{hashBase64}" (design.md,
    // Decision 5) — gerado offline, nunca em código de produção.
    public string OperatorPasswordHash { get; set; } = "";
}
