using Buteco.Inbox.Contacts.Queries.GetContactSessions;
using Buteco.Inbox.Contacts.Queries.ListContacts;
using Buteco.Inbox.Contacts.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Inbox.Contacts.Endpoints;

public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/contacts");

        group.MapGet("/", ListContactsAsync);
        group.MapGet("/{id:guid}/sessions", GetContactSessionsAsync);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ContactResponse>>> ListContactsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var contacts = await mediator.Send(new ListContactsQuery(), cancellationToken);

        return TypedResults.Ok(contacts);
    }

    private static async Task<Results<Ok<IReadOnlyList<SessionResponse>>, NotFound>> GetContactSessionsAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var sessions = await mediator.Send(new GetContactSessionsQuery(id), cancellationToken);

        return sessions is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(sessions);
    }
}
