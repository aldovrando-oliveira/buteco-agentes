using A2A;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Infrastructure;
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
        app.MapPost(RoutePattern, ReceiveAsync)
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.PreExistingAuthMechanism));

        return app;
    }

    private static async Task<Results<Ok, UnauthorizedHttpResult>> ReceiveAsync(
        AgentTask task,
        HttpRequest request,
        AppDbContext dbContext,
        DispatchOutcomeProcessor outcomeProcessor,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // PushNotificationEndpoints é estática — não pode ser argumento de
        // tipo de ILogger<T>, daí ILoggerFactory + nome explícito.
        var logger = loggerFactory.CreateLogger(typeof(PushNotificationEndpoints).FullName!);

        var token = request.Headers[TokenHeaderName].FirstOrDefault();

        // Só usada para a leitura inicial — daqui em diante o processamento
        // roda com CancellationToken.None, não com o token da requisição
        // (ligado a HttpContext.RequestAborted). PushNotificationSender
        // (apps/workers) usa um HttpClient com Timeout curto e fixo de 5s
        // (Program.cs, design.md, Decision 3) e nunca espera nem reage à
        // resposta deste endpoint — se o round-trip (chamar o canal +
        // persistir) ultrapassar esses 5s, o cliente aborta a conexão, o que
        // cancelaria SaveChangesAsync mesmo já tendo enviado a resposta ao
        // canal (ex. Telegram), perdendo a persistência sem perder o envio.
        // Ver incidente: resposta chegou no Telegram, mas a Message/PendingDispatch
        // nunca saiu de "Dispatching" no inbox.
        var pendingDispatch = await dbContext.PendingDispatches
            .FirstOrDefaultAsync(dispatch => dispatch.TaskId == task.Id, cancellationToken);

        if (pendingDispatch is null || token is null || pendingDispatch.ExpectedToken != token)
        {
            // Sem distinguir "task desconhecida" de "token divergente" na
            // resposta — qualquer chamador externo que tente postar um
            // "task completed" falso recebe o mesmo 401 (design.md,
            // Decisão 6). Inclui o push que chega depois de a reconciliação
            // reivindicar a linha, que troca o token (#47, design.md D4).
            return TypedResults.Unauthorized();
        }

        logger.LogInformation(
            "Round-trip A2A concluído para a task {TaskId}, sessão {SessionId}, estado {State}",
            task.Id,
            pendingDispatch.SessionId,
            task.Status.State);

        // O desfecho — resposta, nada, ou aviso de falha — é o mesmo da
        // reconciliação, decidido num lugar só (#47, design.md D5). Se a
        // reconciliação ganhar a corrida pela linha antes da gravação final, o
        // processador devolve AlreadyResolved e loga Warning; a resposta continua
        // 200, porque o desfecho foi processado e o worker não reage a ela (D4).
        await outcomeProcessor.CompleteFromTaskAsync(pendingDispatch, task, CancellationToken.None);

        return TypedResults.Ok();
    }
}
