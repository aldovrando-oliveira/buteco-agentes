using Buteco.Inbox.Channels.Responses;

namespace Buteco.Inbox.Channels.Commands.CreateChannel;

public enum CreateChannelOutcome
{
    Success,
    AgentNotFound,
    AgentValidationFailed,
    InvalidCredential,
    ProvisioningFailed,
}

/// <summary>
/// Sinal neutro devolvido pelo handler — sem nenhuma referência a
/// <c>Microsoft.AspNetCore.Http.HttpResults</c> (mesmo padrão de
/// <c>UpdateMcpServerResult</c>, apps/api). <see cref="CreateChannelOutcome.AgentNotFound"/>
/// e <see cref="CreateChannelOutcome.AgentValidationFailed"/> distinguem os
/// dois motivos de falha da validação de <c>AgentId</c> contra apps/api
/// (design.md, Decision 4). <see cref="CreateChannelOutcome.InvalidCredential"/>
/// carrega os erros reportados pelo <c>IChannelConfigValidator</c> do
/// adapter (design.md, Decision 2) em <see cref="ValidationErrors"/>.
/// <see cref="CreateChannelOutcome.ProvisioningFailed"/> carrega a mensagem
/// reportada pelo <c>IChannelWebhookProvisioner</c> do adapter
/// (inbox-adapter-telegram, design.md, Decision 5) em
/// <see cref="ProvisioningError"/>.
/// </summary>
public sealed record CreateChannelResult(
    ChannelResponse? Channel,
    CreateChannelOutcome Outcome,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? ProvisioningError = null)
{
    public static CreateChannelResult Success(ChannelResponse channel) => new(channel, CreateChannelOutcome.Success);

    public static CreateChannelResult AgentNotFound() => new(null, CreateChannelOutcome.AgentNotFound);

    public static CreateChannelResult AgentValidationFailed() => new(null, CreateChannelOutcome.AgentValidationFailed);

    public static CreateChannelResult InvalidCredential(IReadOnlyDictionary<string, string[]> errors) =>
        new(null, CreateChannelOutcome.InvalidCredential, errors);

    public static CreateChannelResult ProvisioningFailed(string errorMessage) =>
        new(null, CreateChannelOutcome.ProvisioningFailed, ProvisioningError: errorMessage);
}
