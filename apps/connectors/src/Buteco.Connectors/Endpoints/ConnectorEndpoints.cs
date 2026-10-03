using Buteco.Connectors.Connectors;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Connectors.Endpoints;

/// <summary>
/// As três rotas do <c>apps/connectors</c> (design.md, D9). Quem chama cada uma está na
/// tabela de <see cref="Auth.ConnectorsSubjectAuthorizationHandler"/>.
/// </summary>
public static class ConnectorEndpoints
{
    public static IEndpointRouteBuilder MapConnectorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/connectors");
        group.MapGet("/providers", ListProviders);
        group.MapGet("/providers/{providerKey}/folders", BrowseAsync);
        group.MapGet("/providers/{providerKey}/folder", DescribeAsync);
        return app;
    }

    private static Ok<ProviderResponse[]> ListProviders(IEnumerable<ConnectorAccount> accounts) =>
        TypedResults.Ok(accounts
            .OrderBy(account => account.Key, StringComparer.Ordinal)
            .Select(account => new ProviderResponse(account.Key, account.Email))
            .ToArray());

    private static async Task<Results<Ok<FolderEntryResponse[]>, ProblemHttpResult>> BrowseAsync(
        string providerKey,
        string? parentId,
        IServiceProvider services,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var navigator = services.GetKeyedService<IFolderNavigator>(providerKey);
        if (navigator is null)
        {
            return Failure(new ConnectorFailure(ConnectorCodes.ProviderNotConfigured));
        }

        try
        {
            var entries = await navigator.BrowseAsync(string.IsNullOrWhiteSpace(parentId) ? null : parentId, cancellationToken);
            return TypedResults.Ok(entries
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(FolderEntryResponse.From)
                .ToArray());
        }
        catch (ConnectorFailure failure)
        {
            Log(loggerFactory, providerKey, "navegação", failure);
            return Failure(failure);
        }
    }

    private static async Task<Results<Ok<FolderDescriptionResponse>, ProblemHttpResult>> DescribeAsync(
        string providerKey,
        string? id,
        IServiceProvider services,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TypedResults.Problem(title: "Informe o id da pasta.", statusCode: StatusCodes.Status400BadRequest);
        }

        var navigator = services.GetKeyedService<IFolderNavigator>(providerKey);
        if (navigator is null)
        {
            return Failure(new ConnectorFailure(ConnectorCodes.ProviderNotConfigured));
        }

        try
        {
            var folder = await navigator.DescribeFolderAsync(id, cancellationToken);
            return TypedResults.Ok(new FolderDescriptionResponse(folder.Id, folder.Name, folder.WebUrl));
        }
        catch (ConnectorFailure failure)
        {
            Log(loggerFactory, providerKey, "descrição de pasta", failure);
            return Failure(failure);
        }
    }

    // Status por natureza, código como informação (D9). 403 é só da autorização do
    // subject, para não repetir a ambiguidade do Google, onde quatro situações voltam 403.
    internal static int StatusFor(string code) => code switch
    {
        ConnectorCodes.ProviderNotConfigured => StatusCodes.Status404NotFound,
        ConnectorCodes.AccessDenied or ConnectorCodes.NotAFolder or ConnectorCodes.FolderTrashed
            => StatusCodes.Status422UnprocessableEntity,
        ConnectorCodes.RateLimited or ConnectorCodes.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status502BadGateway,
    };

    // O texto exibido é do frontend; o título aqui é só para quem lê a resposta crua.
    private static string TitleFor(string code) => code switch
    {
        ConnectorCodes.ProviderNotConfigured => "Provedor não configurado.",
        ConnectorCodes.AccessDenied => "A conta do provedor não tem acesso à pasta.",
        ConnectorCodes.NotAFolder => "O item informado não é uma pasta.",
        ConnectorCodes.FolderTrashed => "A pasta está na lixeira.",
        ConnectorCodes.RateLimited => "Cota do provedor excedida.",
        ConnectorCodes.ProviderUnavailable => "Provedor indisponível.",
        _ => "Falha do provedor.",
    };

    private static ProblemHttpResult Failure(ConnectorFailure failure) =>
        TypedResults.Problem(
            title: TitleFor(failure.Code),
            detail: failure.Detail,
            statusCode: StatusFor(failure.Code),
            extensions: new Dictionary<string, object?> { ["code"] = failure.Code });

    private static void Log(ILoggerFactory loggerFactory, string providerKey, string operation, ConnectorFailure failure) =>
        loggerFactory.CreateLogger(typeof(ConnectorEndpoints)).LogWarning(
            "Conector {ProviderKey} falhou na {Operation} com {Code}.", providerKey, operation, failure.Code);
}
