using Buteco.Inbox.Channels.Responses;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Channels.Queries.GetChannelById;

public sealed class GetChannelByIdQueryHandler(AppDbContext dbContext, IOptions<PublicUrlOptions> publicUrlOptions)
    : IQueryHandler<GetChannelByIdQuery, ChannelResponse?>
{
    public async ValueTask<ChannelResponse?> Handle(GetChannelByIdQuery query, CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(channel => channel.Id == query.Id, cancellationToken);

        return channel is null ? null : ChannelResponse.FromEntity(channel, publicUrlOptions.Value.BaseUrl);
    }
}
