using System.Text.Json;
using Buteco.Api.ExternalApplications;
using Buteco.Api.Infrastructure;
using global::A2A;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.A2A;

/// <summary>
/// A checagem da aplicação externa na rota <c>POST /agents/{id}/a2a</c>, antes do SDK do A2A (design.md
/// da change agentes-por-aplicacao, D1 e D2; spec <c>a2a-task-lifecycle</c>, "A rota A2A da aplicação
/// externa passa pelo vínculo antes do JSON-RPC"). Só o subject <c>application</c> passa por aqui; o
/// <c>service:inbox</c> segue direto, como antes.
///
/// <list type="number">
/// <item><b>O vínculo, com a aplicação ativa</b> (uma consulta, sem ler <c>agents</c>): senão <c>404</c>
/// vazio. Agente inexistente, agente sem vínculo e aplicação desativada dão a mesma resposta, byte a
/// byte: a aplicação não descobre se o agente existe (D2.3), e a desativada perde a A2A já na
/// requisição seguinte, mesmo com o token de 5 min ainda no prazo (D2.4, Q6).</item>
/// <item><b>O método e as operações</b>: <c>SendMessage</c> exige <c>enviar-mensagem</c>, e mais
/// <c>notificacao-push</c> se traz <c>configuration.pushNotificationConfig</c>; <c>GetTask</c> exige
/// <c>consultar-task</c>. Qualquer outro método, inclusive <c>CancelTask</c> (Q1, #264) e
/// <c>ListTasks</c>, que listaria tasks de todo mundo: <c>403</c> vazio.</item>
/// </list>
///
/// <para>
/// <b>Sem parser próprio</b> (decisão do dono, Q2). O corpo é lido com os mesmos tipos e as mesmas opções
/// que o <c>A2AJsonRpcProcessor</c> usa (<see cref="JsonRpcRequest"/>, <see cref="SendMessageRequest"/>,
/// <see cref="A2AJsonUtilities.DefaultOptions"/>), e volta ao início para o SDK. Assim o método e a
/// configuração que este filtro autoriza são os que o SDK executa: com <c>method</c> duplicado, por
/// exemplo, os dois leem o último (o conversor do SDK usa <c>JsonElement.TryGetProperty</c>). Um parser à
/// parte, que lesse o primeiro, autorizaria um <c>GetTask</c> e deixaria o SDK rodar um <c>ListTasks</c>.
/// </para>
///
/// <para>
/// Nome que não é método A2A (inclusive com a caixa trocada) já falha no parse do SDK com
/// <c>-32601</c>: aqui é <c>403</c>, como todo método fora das operações. Outro corpo ilegível (JSON
/// inválido, sem <c>method</c>) segue para o SDK, que o lê pelo mesmo código e responde o erro JSON-RPC de
/// sempre; nada passa sem método.
/// </para>
/// </summary>
public sealed class ApplicationA2AFilter(IServiceScopeFactory scopeFactory) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (!ApplicationCaller.IsApplication(httpContext.User))
        {
            return await next(context);
        }

        var operations = await GrantedOperationsAsync(httpContext);
        if (operations is null)
        {
            return Results.StatusCode(StatusCodes.Status404NotFound);
        }

        if (!await AllowsAsync(httpContext.Request, operations))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }

    /// <summary>
    /// As operações do vínculo da aplicação com o agente da rota, com a aplicação ativa; <c>null</c> para
    /// qualquer outro caso, inclusive o id ilegível e o agente que não existe (que não tem vínculo).
    /// </summary>
    private async Task<IReadOnlyList<string>?> GrantedOperationsAsync(HttpContext httpContext)
    {
        if (ApplicationCaller.IdOf(httpContext.User) is not { } applicationId ||
            httpContext.Request.RouteValues["id"] is not string idText ||
            !Guid.TryParse(idText, out var agentId))
        {
            return null;
        }

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await dbContext.ExternalApplicationAgents
            .AsNoTracking()
            .Where(link => link.ApplicationId == applicationId && link.AgentId == agentId)
            .Join(
                dbContext.ExternalApplications.Where(application => application.DeactivatedAt == null),
                link => link.ApplicationId,
                application => application.Id,
                (link, _) => link.Operations)
            .FirstOrDefaultAsync(httpContext.RequestAborted);
    }

    private static async Task<bool> AllowsAsync(HttpRequest request, IReadOnlyList<string> operations)
    {
        request.EnableBuffering();
        try
        {
            JsonRpcRequest? rpcRequest;
            try
            {
                rpcRequest = (JsonRpcRequest?)await JsonSerializer.DeserializeAsync(
                    request.Body, A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(JsonRpcRequest)), request.HttpContext.RequestAborted);
            }
            catch (A2AException exception) when (exception.ErrorCode == A2AErrorCode.MethodNotFound)
            {
                return false;
            }
            catch (Exception exception) when (exception is A2AException or JsonException)
            {
                // Ilegível: o SDK lê o mesmo corpo pelo mesmo código e responde o erro de parse.
                return true;
            }

            if (rpcRequest is null)
            {
                return true;
            }

            return rpcRequest.Method switch
            {
                A2AMethods.SendMessage => operations.Contains(ExternalApplicationOperations.SendMessage) &&
                    (!CarriesPushConfig(rpcRequest.Params) || operations.Contains(ExternalApplicationOperations.PushNotification)),
                A2AMethods.GetTask => operations.Contains(ExternalApplicationOperations.GetTask),
                _ => false,
            };
        }
        finally
        {
            request.Body.Position = 0;
        }
    }

    /// <summary>
    /// Se o <c>SendMessage</c>, lido como o SDK o lê, traz configuração de push. Params que o SDK não
    /// consegue ler não criam task, e por isso não levam push a lugar nenhum.
    /// </summary>
    private static bool CarriesPushConfig(JsonElement? parameters)
    {
        if (parameters is not { } value)
        {
            return false;
        }

        try
        {
            var sendMessage = (SendMessageRequest?)value.Deserialize(A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(SendMessageRequest)));
            return sendMessage?.Configuration?.PushNotificationConfig is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
