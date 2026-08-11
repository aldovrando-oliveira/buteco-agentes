using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Inbox.Channels.Webhooks.Endpoints;

// Rota genérica única, despachando por ChannelType já persistido no Channel
// resolvido por channelId — não uma rota por tipo de adapter (design.md,
// Decision 1, "Alternativa descartada": rota específica por ChannelType).
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/{channelId:guid}", ReceiveAsync);

        return app;
    }

    private static async Task<Results<Ok, NotFound, BadRequest>> ReceiveAsync(
        Guid channelId,
        HttpRequest request,
        AppDbContext dbContext,
        IChannelAdapterRegistry adapterRegistry,
        CancellationToken cancellationToken)
    {
        var channel = await dbContext.Channels.FindAsync([channelId], cancellationToken);
        if (channel is null)
        {
            return TypedResults.NotFound();
        }

        var handler = adapterRegistry.GetInboundWebhookHandler(channel.ChannelType);
        if (handler is null)
        {
            return TypedResults.BadRequest();
        }

        await handler.HandleAsync(channelId, request, cancellationToken);

        return TypedResults.Ok();
    }
}
