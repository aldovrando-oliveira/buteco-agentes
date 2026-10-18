using Buteco.Api.McpServers.Commands.ActivateMcpServer;
using Buteco.Api.McpServers.Commands.CreateMcpServer;
using Buteco.Api.McpServers.Commands.DeactivateMcpServer;
using Buteco.Api.McpServers.Commands.TestSavedMcpServerConnection;
using Buteco.Api.McpServers.Commands.TestUnsavedMcpServerConnection;
using Buteco.Api.McpServers.Commands.UpdateMcpServer;
using Buteco.Api.McpServers.Connectivity;
using Buteco.Api.McpServers.Entities;
using Buteco.Api.McpServers.Queries.GetMcpServerById;
using Buteco.Api.McpServers.Queries.ListMcpServers;
using Buteco.Api.McpServers.Queries.ListMcpServerTools;
using Buteco.Api.McpServers.Requests;
using Buteco.Api.McpServers.Responses;
using Buteco.Api.McpServers.Validation;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Api.McpServers.Endpoints;

public static class McpServerEndpoints
{
    public static IEndpointRouteBuilder MapMcpServerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/mcp-servers");

        group.MapPost("/", CreateMcpServerAsync);
        group.MapGet("/", ListMcpServersAsync);
        group.MapGet("/{id:guid}", GetMcpServerByIdAsync);
        group.MapGet("/{id:guid}/tools", ListMcpServerToolsAsync);
        group.MapPut("/{id:guid}", UpdateMcpServerAsync);
        group.MapPost("/{id:guid}/activate", ActivateMcpServerAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateMcpServerAsync);
        group.MapPost("/test", TestUnsavedMcpServerConnectionAsync);
        group.MapPost("/{id:guid}/test", TestSavedMcpServerConnectionAsync);

        return app;
    }

    private static async Task<Results<Created<McpServerResponse>, ValidationProblem>> CreateMcpServerAsync(
        CreateMcpServerRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Name, request.Url, request.AuthType, request.AuthHeaderName, request.Credential, out var authType);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        if (authType != McpServerAuthType.None && string.IsNullOrWhiteSpace(request.Credential))
        {
            return TypedResults.ValidationProblem(BuildMissingCredentialErrors());
        }

        var command = new CreateMcpServerCommand(request.Name!, request.Description ?? string.Empty, request.Url!, authType, request.Credential, request.AuthHeaderName);
        var result = await mediator.Send(command, cancellationToken);

        return TypedResults.Created($"/mcp-servers/{result.Id}", result);
    }

    private static async Task<Ok<IReadOnlyList<McpServerResponse>>> ListMcpServersAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var mcpServers = await mediator.Send(new ListMcpServersQuery(), cancellationToken);

        return TypedResults.Ok(mcpServers);
    }

    private static async Task<Results<Ok<McpServerResponse>, NotFound>> GetMcpServerByIdAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var mcpServer = await mediator.Send(new GetMcpServerByIdQuery(id), cancellationToken);

        return mcpServer is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(mcpServer);
    }

    private static async Task<Results<Ok<McpServerToolsResponse>, NotFound>> ListMcpServerToolsAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new ListMcpServerToolsQuery(id), cancellationToken);

        return result.Found
            ? TypedResults.Ok(result.Result!)
            : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<McpServerResponse>, NotFound, ValidationProblem>> UpdateMcpServerAsync(
        Guid id,
        UpdateMcpServerRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Name, request.Url, request.AuthType, request.AuthHeaderName, request.Credential, out var authType);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new UpdateMcpServerCommand(id, request.Name!, request.Description ?? string.Empty, request.Url!, authType, request.Credential, request.AuthHeaderName);
        var result = await mediator.Send(command, cancellationToken);

        if (!result.Found)
        {
            return TypedResults.NotFound();
        }

        if (result.MissingCredential)
        {
            return TypedResults.ValidationProblem(BuildMissingCredentialErrors());
        }

        return TypedResults.Ok(result.McpServer!);
    }

    private static async Task<Results<Ok<McpServerResponse>, NotFound>> ActivateMcpServerAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new ActivateMcpServerCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<McpServerResponse>, NotFound>> DeactivateMcpServerAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new DeactivateMcpServerCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<McpConnectionTestResult>, ValidationProblem>> TestUnsavedMcpServerConnectionAsync(
        TestMcpServerConfigRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateTestShape(request.Url, request.AuthType, request.AuthHeaderName, request.Credential, out var authType);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new TestUnsavedMcpServerConnectionCommand(request.Url!, authType, request.Credential, request.AuthHeaderName);
        var result = await mediator.Send(command, cancellationToken);

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<McpConnectionTestResult>, NotFound>> TestSavedMcpServerConnectionAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new TestSavedMcpServerConnectionCommand(id), cancellationToken);

        return result.Found
            ? TypedResults.Ok(result.Result!)
            : TypedResults.NotFound();
    }

    // Teste de configuração não salva: além do que o cadastro valida, com
    // CustomHeader a credencial é OBRIGATÓRIA aqui (D3 da change
    // mcp-autenticacao-header-customizado). Sem ela o header sairia vazio, e um
    // "credencial rejeitada" seria verdade sobre um teste que não testou nada. O
    // BearerToken não muda: exigir credencial nele é contrato de tipo existente.
    // Tudo isso responde 400 ANTES de qualquer conexão — um nome que o .NET
    // recusa, se chegasse ao SDK, viraria exceção com o segredo na mensagem (V3).
    private static Dictionary<string, string[]>? ValidateTestShape(string? url, string? authTypeRaw, string? authHeaderName, string? credential, out McpServerAuthType authType)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(url))
        {
            errors["url"] = ["A URL do servidor MCP é obrigatória."];
        }

        ValidateAuthentication(authTypeRaw, authHeaderName, credential, errors, out authType);

        if (authType == McpServerAuthType.CustomHeader && string.IsNullOrWhiteSpace(credential) && !errors.ContainsKey("credential"))
        {
            errors["credential"] = ["A credencial é obrigatória para testar o tipo de autenticação CustomHeader."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static Dictionary<string, string[]>? ValidateShape(string? name, string? url, string? authTypeRaw, string? authHeaderName, string? credential, out McpServerAuthType authType)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["O nome do servidor MCP é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            errors["url"] = ["A URL do servidor MCP é obrigatória."];
        }

        ValidateAuthentication(authTypeRaw, authHeaderName, credential, errors, out authType);

        return errors.Count > 0 ? errors : null;
    }

    // authType por nome de membro (D9), e, só no CustomHeader, o nome do header
    // e o valor do token (D3). authHeaderName com outro tipo é ignorado (o
    // McpServer o grava nulo), não recusado. Credencial em branco não é checada
    // aqui: no cadastro a ausência vira o 400 de credencial obrigatória, e na
    // atualização significa "manter a atual".
    private static void ValidateAuthentication(string? authTypeRaw, string? authHeaderName, string? credential, Dictionary<string, string[]> errors, out McpServerAuthType authType)
    {
        if (!McpAuthHeaderRules.TryParseAuthType(authTypeRaw, out authType))
        {
            errors["authType"] = [McpAuthHeaderRules.InvalidAuthTypeMessage];
            return;
        }

        if (authType != McpServerAuthType.CustomHeader)
        {
            return;
        }

        if (McpAuthHeaderRules.ValidateHeaderName(authHeaderName) is { } headerNameError)
        {
            errors["authHeaderName"] = [headerNameError];
        }

        if (!string.IsNullOrWhiteSpace(credential) && McpAuthHeaderRules.ValidateTokenValue(credential) is { } credentialError)
        {
            errors["credential"] = [credentialError];
        }
    }

    private static Dictionary<string, string[]> BuildMissingCredentialErrors() =>
        new() { ["credential"] = ["A credencial é obrigatória para o tipo de autenticação informado."] };
}
