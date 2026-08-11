using Buteco.Inbox.Agents;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Channels.Commands.UpdateChannel;

public sealed class UpdateChannelCommandHandler(
    AppDbContext dbContext,
    IChannelCredentialCipher credentialCipher,
    IAgentReferenceValidator agentReferenceValidator,
    IChannelAdapterRegistry adapterRegistry,
    IOptions<PublicUrlOptions> publicUrlOptions) : ICommandHandler<UpdateChannelCommand, UpdateChannelResult>
{
    public async ValueTask<UpdateChannelResult> Handle(UpdateChannelCommand command, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels
            .FirstOrDefaultAsync(channel => channel.Id == command.Id, cancellationToken);

        if (channel is null)
        {
            return UpdateChannelResult.NotFound();
        }

        // Revalida AgentId só quando ele muda em relação ao persistido —
        // evita uma chamada de rede desnecessária quando o cliente reenvia
        // o mesmo AgentId já validado no cadastro/atualização anterior.
        // isActive do agente não é verificado — canal vinculado a agente
        // inativo é permitido de propósito (design.md, Decision 7).
        if (command.AgentId != channel.AgentId)
        {
            var validation = await agentReferenceValidator.ValidateAsync(command.AgentId, cancellationToken);
            switch (validation)
            {
                case AgentReferenceValidationResult.NotFound:
                    return UpdateChannelResult.AgentNotFound();
                case AgentReferenceValidationResult.CommunicationFailure:
                    return UpdateChannelResult.AgentValidationFailed();
            }
        }

        channel.UpdateDetails(command.Name, command.AgentId);

        if (!string.IsNullOrWhiteSpace(command.Credential))
        {
            // ChannelType não é atualizável — o validador resolvido é
            // sempre o do channel.ChannelType já persistido (design.md,
            // Decision 2).
            var configValidation = adapterRegistry.GetConfigValidator(channel.ChannelType).Validate(command.Credential);
            if (!configValidation.IsValid)
            {
                return UpdateChannelResult.InvalidCredential(configValidation.Errors);
            }

            channel.SetEncryptedCredentials(credentialCipher.Encrypt(command.Credential));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return UpdateChannelResult.Success(ChannelResponse.FromEntity(channel, publicUrlOptions.Value.BaseUrl));
    }
}
