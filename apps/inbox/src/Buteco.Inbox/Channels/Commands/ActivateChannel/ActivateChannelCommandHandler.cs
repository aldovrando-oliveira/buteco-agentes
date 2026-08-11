using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Channels.Commands.ActivateChannel;

public sealed class ActivateChannelCommandHandler(AppDbContext dbContext, IOptions<PublicUrlOptions> publicUrlOptions)
    : ICommandHandler<ActivateChannelCommand, ChannelResponse?>
{
    public async ValueTask<ChannelResponse?> Handle(ActivateChannelCommand command, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels
            .FirstOrDefaultAsync(channel => channel.Id == command.Id, cancellationToken);

        if (channel is null)
        {
            return null;
        }

        channel.Activate();
        await dbContext.SaveChangesAsync(cancellationToken);

        return ChannelResponse.FromEntity(channel, publicUrlOptions.Value.BaseUrl);
    }
}
