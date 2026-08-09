using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Channels.Queries.ListChannels;

public sealed class ListChannelsQueryHandler(AppDbContext dbContext) : IQueryHandler<ListChannelsQuery, IReadOnlyList<ChannelResponse>>
{
    public async ValueTask<IReadOnlyList<ChannelResponse>> Handle(ListChannelsQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.Channels
            .AsNoTracking()
            .OrderBy(channel => channel.CreatedAt)
            .Select(channel => ChannelResponse.FromEntity(channel))
            .ToListAsync(cancellationToken);
    }
}
