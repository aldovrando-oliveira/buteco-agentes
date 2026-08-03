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
        var shapeErrors = ValidateShape(request.Name, request.Url, request.AuthType, out var authType);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        if (authType != McpServerAuthType.None && string.IsNullOrWhiteSpace(request.Credential))
        {
            return TypedResults.ValidationProblem(BuildMissingCredentialErrors());
        }

        var command = new CreateMcpServerCommand(request.Name!, request.Description ?? string.Empty, request.Url!, authType, request.Credential);
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
        var shapeErrors = ValidateShape(request.Name, request.Url, request.AuthType, out var authType);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new UpdateMcpServerCommand(id, request.Name!, request.Description ?? string.Empty, request.Url!, authType, request.Credential);
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
        var shapeErrors = ValidateTestShape(request.Url, request.AuthType, out var authType);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new TestUnsavedMcpServerConnectionCommand(request.Url!, authType, request.Credential);
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

    private static Dictionary<string, string[]>? ValidateTestShape(string? url, string? authTypeRaw, out McpServerAuthType authType)
    {
        var errors = new Dictionary<string, string[]>();
        authType = McpServerAuthType.None;

        if (string.IsNullOrWhiteSpace(url))
        {
            errors["url"] = ["A URL do servidor MCP é obrigatória."];
        }

        if (string.IsNullOrWhiteSpace(authTypeRaw) || !Enum.TryParse(authTypeRaw, ignoreCase: true, out authType))
        {
            errors["authType"] = ["O tipo de autenticação informado é inválido. Valores aceitos: None, BearerToken."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static Dictionary<string, string[]>? ValidateShape(string? name, string? url, string? authTypeRaw, out McpServerAuthType authType)
    {
        var errors = new Dictionary<string, string[]>();
        authType = McpServerAuthType.None;

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["O nome do servidor MCP é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            errors["url"] = ["A URL do servidor MCP é obrigatória."];
        }

        if (string.IsNullOrWhiteSpace(authTypeRaw) || !Enum.TryParse(authTypeRaw, ignoreCase: true, out authType))
        {
            errors["authType"] = ["O tipo de autenticação informado é inválido. Valores aceitos: None, BearerToken."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static Dictionary<string, string[]> BuildMissingCredentialErrors() =>
        new() { ["credential"] = ["A credencial é obrigatória para o tipo de autenticação informado."] };
}
