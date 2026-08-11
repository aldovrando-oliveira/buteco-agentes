using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Channels.Commands.DeactivateChannel;

public sealed class DeactivateChannelCommandHandler(AppDbContext dbContext, IOptions<PublicUrlOptions> publicUrlOptions)
    : ICommandHandler<DeactivateChannelCommand, ChannelResponse?>
{
    public async ValueTask<ChannelResponse?> Handle(DeactivateChannelCommand command, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels
            .FirstOrDefaultAsync(channel => channel.Id == command.Id, cancellationToken);

        if (channel is null)
        {
            return null;
        }

        channel.Deactivate();
        await dbContext.SaveChangesAsync(cancellationToken);

        return ChannelResponse.FromEntity(channel, publicUrlOptions.Value.BaseUrl);
    }
}
