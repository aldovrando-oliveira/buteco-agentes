using Buteco.Inbox.Auth;
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
        app.MapPost("/webhooks/{channelId:guid}", ReceiveAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.ExternalUnauthenticated));

        return app;
    }

    private static async Task<Results<StatusCodeHttpResult, NotFound, BadRequest>> ReceiveAsync(
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

        // TypedResults.StatusCode, não TypedResults.Ok() fixo — um handler
        // pode ter definido um status diferente de 200 na própria resposta
        // antes de retornar (ex. TelegramInboundWebhookHandler rejeitando
        // com 401 quando o secret_token diverge ou o canal está inativo;
        // inbox-adapter-telegram, design.md, Decision 8). TypedResults.Ok()
        // sobrescreveria isso incondicionalmente para 200. HttpResponse.StatusCode
        // já é 200 por padrão quando nenhum handler o altera (WAHA,
        // test-channel), então o comportamento existente não muda.
        return TypedResults.StatusCode(request.HttpContext.Response.StatusCode);
    }
}
