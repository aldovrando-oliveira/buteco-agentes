using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Channels.Queries.ListChannels;

public sealed class ListChannelsQueryHandler(AppDbContext dbContext, IOptions<PublicUrlOptions> publicUrlOptions)
    : IQueryHandler<ListChannelsQuery, IReadOnlyList<ChannelResponse>>
{
    public async ValueTask<IReadOnlyList<ChannelResponse>> Handle(ListChannelsQuery query, CancellationToken cancellationToken)
    {
        var publicUrlBaseUrl = publicUrlOptions.Value.BaseUrl;

        return await dbContext.Channels
            .AsNoTracking()
            .OrderBy(channel => channel.CreatedAt)
            .Select(channel => ChannelResponse.FromEntity(channel, publicUrlBaseUrl))
            .ToListAsync(cancellationToken);
    }
}
