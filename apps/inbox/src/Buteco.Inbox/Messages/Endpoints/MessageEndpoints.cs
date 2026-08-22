using Buteco.Inbox.Messages.Queries.GetSessionMessages;
using Buteco.Inbox.Messages.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Inbox.Messages.Endpoints;

public static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sessions/{sessionId:guid}/messages", GetSessionMessagesAsync);

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<MessageResponse>>, NotFound>> GetSessionMessagesAsync(
        Guid sessionId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var messages = await mediator.Send(new GetSessionMessagesQuery(sessionId), cancellationToken);

        return messages is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(messages);
    }
}
