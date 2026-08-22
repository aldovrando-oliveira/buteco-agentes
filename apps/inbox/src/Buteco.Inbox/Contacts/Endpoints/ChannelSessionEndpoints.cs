using Buteco.Inbox.Contacts.Queries.GetChannelSessions;
using Buteco.Inbox.Contacts.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Inbox.Contacts.Endpoints;

public static class ChannelSessionEndpoints
{
    public static IEndpointRouteBuilder MapChannelSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/channels/{channelId:guid}/sessions", GetChannelSessionsAsync);

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<ChannelSessionResponse>>, NotFound>> GetChannelSessionsAsync(
        Guid channelId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var sessions = await mediator.Send(new GetChannelSessionsQuery(channelId), cancellationToken);

        return sessions is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(sessions);
    }
}
