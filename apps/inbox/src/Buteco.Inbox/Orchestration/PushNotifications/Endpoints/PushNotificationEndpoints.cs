using A2A;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Orchestration.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Inbox.Orchestration.PushNotifications.Endpoints;

// Recebe a AgentTask completa quando uma task disparada por
// DebounceSweepService atinge um estado terminal (design.md, Decisão 6).
// Nome do header idêntico ao já usado por PushNotificationSender
// (apps/workers) — não um valor novo.
public static class PushNotificationEndpoints
{
    public const string RoutePattern = "/internal/push-notifications";

    public const string TokenHeaderName = "X-A2A-Notification-Token";

    public static IEndpointRouteBuilder MapPushNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(RoutePattern, ReceiveAsync);

        return app;
    }

    private static async Task<Results<Ok, UnauthorizedHttpResult>> ReceiveAsync(
        AgentTask task,
        HttpRequest request,
        AppDbContext dbContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // PushNotificationEndpoints é estática — não pode ser argumento de
        // tipo de ILogger<T>, daí ILoggerFactory + nome explícito.
        var logger = loggerFactory.CreateLogger(typeof(PushNotificationEndpoints).FullName!);

        var token = request.Headers[TokenHeaderName].FirstOrDefault();

        var pendingDispatch = await dbContext.PendingDispatches
            .FirstOrDefaultAsync(dispatch => dispatch.TaskId == task.Id, cancellationToken);

        if (pendingDispatch is null || token is null || pendingDispatch.ExpectedToken != token)
        {
            // Sem distinguir "task desconhecida" de "token divergente" na
            // resposta — qualquer chamador externo que tente postar um
            // "task completed" falso recebe o mesmo 401 (design.md,
            // Decisão 6).
            return TypedResults.Unauthorized();
        }

        logger.LogInformation(
            "Round-trip A2A concluído para a task {TaskId}, sessão {SessionId}, estado {State}",
            task.Id,
            pendingDispatch.SessionId,
            task.Status.State);

        dbContext.Remove(pendingDispatch);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok();
    }
}
