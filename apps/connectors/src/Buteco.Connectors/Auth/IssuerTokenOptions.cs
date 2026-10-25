namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/IssuerTokenOptions.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

// Issuer público do emissor do apps/api (change validacao-jwks-inbox-connectors, D3).
// Vazio desliga a validação do token do emissor (D7). No compose de produção vem da
// mesma variável que vira PublicUrl__BaseUrl do apps/api (PUBLIC_DOMAIN).
public sealed class IssuerTokenOptions
{
    public const string SectionName = "Identity";

    public string? Issuer { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Issuer);

    // Mesma normalização do issuer no apps/api (barra final): o iss do token é
    // comparado por igualdade ordinal.
    public string NormalizedIssuer => Issuer!.Trim().TrimEnd('/') + "/";
}
