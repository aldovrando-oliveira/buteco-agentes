using Buteco.Inbox.Agents;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Mediator;

namespace Buteco.Inbox.Channels.Commands.CreateChannel;

public sealed class CreateChannelCommandHandler(
    AppDbContext dbContext,
    IChannelCredentialCipher credentialCipher,
    IAgentReferenceValidator agentReferenceValidator) : ICommandHandler<CreateChannelCommand, CreateChannelResult>
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

        var encryptedCredentials = credentialCipher.Encrypt(command.Credential);
        var channel = new Channel(command.ChannelType, command.Name, encryptedCredentials, command.AgentId);

        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateChannelResult.Success(ChannelResponse.FromEntity(channel));
    }
}
