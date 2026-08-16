namespace Buteco.Inbox.Channels.Adapters;

// Resultado tipado, não exceção (design.md, Decision 2) — mesmo padrão de
// AgentReferenceValidationResult/ChannelConfigValidationResult: falha de
// rede/credencial rejeitada é um caminho esperado, não excepcional.
// UpdatedCredential carrega a credencial em texto plano com qualquer dado
// gerado pelo provisionamento (ex. secret_token) já embutido — só o
// provisionador conhece o shape específico do adapter.
public sealed record ChannelWebhookProvisioningResult(
    bool Success,
    string? UpdatedCredential,
    string? ErrorMessage)
{
    public static ChannelWebhookProvisioningResult Succeeded(string updatedCredential) =>
        new(true, updatedCredential, null);

    public static ChannelWebhookProvisioningResult Failed(string errorMessage) =>
        new(false, null, errorMessage);
}
