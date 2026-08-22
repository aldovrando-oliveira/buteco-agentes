namespace Buteco.Inbox.Auth;

public sealed class TokenSigningOptions
{
    public const string SectionName = "Auth";

    // Mesmo valor de apps/api — segredo compartilhado via configuração,
    // sem chamada de rede entre os dois processos para validar token
    // (design.md, Decision 1).
    public string TokenSigningKey { get; set; } = "";
}
