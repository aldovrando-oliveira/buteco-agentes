using Buteco.Inbox.Agents;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Mediator;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Channels.Commands.CreateChannel;

public sealed class CreateChannelCommandHandler(
    AppDbContext dbContext,
    IChannelCredentialCipher credentialCipher,
    IAgentReferenceValidator agentReferenceValidator,
    IChannelAdapterRegistry adapterRegistry,
    IOptions<PublicUrlOptions> publicUrlOptions) : ICommandHandler<CreateChannelCommand, CreateChannelResult>
{
    public async ValueTask<CreateChannelResult> Handle(CreateChannelCommand command, CancellationToken cancellationToken)
    {
        // isActive do agente não é verificado aqui — canal vinculado a
        // agente inativo é permitido de propósito (design.md, Decision 7).
        var validation = await agentReferenceValidator.ValidateAsync(command.AgentId, cancellationToken);
        switch (validation)
        {
            case AgentReferenceValidationResult.NotFound:
                return CreateChannelResult.AgentNotFound();
            case AgentReferenceValidationResult.CommunicationFailure:
                return CreateChannelResult.AgentValidationFailed();
        }

        // Validador resolvido pelo adapter registrado para o ChannelType,
        // chamado sobre o texto plano antes de cifrar (design.md, Decision 2).
        var configValidation = adapterRegistry.GetConfigValidator(command.ChannelType).Validate(command.Credential);
        if (!configValidation.IsValid)
        {
            return CreateChannelResult.InvalidCredential(configValidation.Errors);
        }

        // Channel construído em memória (Id já populado por Guid.NewGuid()
        // no construtor) antes de qualquer Add/SaveChangesAsync — nada foi
        // persistido ainda quando o provisionamento abaixo roda, então uma
        // falha do provisionador simplesmente retorna, sem precisar de
        // rollback/transação (inbox-adapter-telegram, design.md, Decision 4).
        var encryptedCredentials = credentialCipher.Encrypt(command.Credential);
        var channel = new Channel(command.ChannelType, command.Name, encryptedCredentials, command.AgentId);

        // Quarto contrato, opcional — null para adapters sem provisionamento
        // automático (ex. "waha"), presente para "telegram"
        // (inbox-adapter-telegram, design.md, Decision 1/4).
        var provisioner = adapterRegistry.GetWebhookProvisioner(command.ChannelType);
        if (provisioner is not null)
        {
            var webhookUrl = ChannelResponse.BuildWebhookUrl(publicUrlOptions.Value.BaseUrl, channel.Id);
            var provisioning = await provisioner.ProvisionAsync(channel.Id, command.Credential, webhookUrl, cancellationToken);
            if (!provisioning.Success)
            {
                return CreateChannelResult.ProvisioningFailed(provisioning.ErrorMessage!);
            }

            channel.SetEncryptedCredentials(credentialCipher.Encrypt(provisioning.UpdatedCredential!));
        }

        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateChannelResult.Success(ChannelResponse.FromEntity(channel, publicUrlOptions.Value.BaseUrl));
    }
}
