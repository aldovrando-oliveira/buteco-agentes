namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/ServiceClientOptions.cs — sem libs/ (design.md
// da change servicos-token-emissor, D3, e remover-hmac-token-servico, D10). O que difere de
// propósito é o client_id.

/// <summary>
/// A credencial do <c>apps/connectors</c> no emissor do <c>apps/api</c> (design.md da change
/// servicos-token-emissor, D5). O segredo é o mesmo valor de
/// <c>Identity:ServiceClients:Connectors:Secret</c> no <c>apps/api</c>. O <c>client_id</c> é
/// constante, e não configuração (convenção 2).
/// </summary>
public sealed class ServiceClientOptions
{
    public const string SectionName = "ServiceClient";

    public const string SecretConfigurationName = "ServiceClient:Secret";

    public const string ClientId = "buteco-servico-connectors";

    public string? Secret { get; set; }
}
