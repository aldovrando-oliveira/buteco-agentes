using Buteco.Inbox.Channels.Commands.ActivateChannel;
using Buteco.Inbox.Channels.Commands.CreateChannel;
using Buteco.Inbox.Channels.Commands.DeactivateChannel;
using Buteco.Inbox.Channels.Commands.UpdateChannel;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Queries.GetChannelById;
using Buteco.Inbox.Channels.Queries.ListChannels;
using Buteco.Inbox.Channels.Requests;
using Buteco.Inbox.Channels.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Inbox.Channels.Endpoints;

public static class ChannelEndpoints
{
    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/channels");

        group.MapPost("/", CreateChannelAsync);
        group.MapGet("/", ListChannelsAsync);
        group.MapGet("/{id:guid}", GetChannelByIdAsync);
        group.MapPut("/{id:guid}", UpdateChannelAsync);
        group.MapPost("/{id:guid}/activate", ActivateChannelAsync);
        group.MapPost("/{id:guid}/deactivate", DeactivateChannelAsync);

        return app;
    }

    private static async Task<Results<Created<ChannelResponse>, ValidationProblem, ProblemHttpResult>> CreateChannelAsync(
        CreateChannelRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateShape(request.Name, request.ChannelType, request.Credential, request.AgentId, out var channelType);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new CreateChannelCommand(channelType, request.Name!, request.Credential!, request.AgentId!.Value);
        var result = await mediator.Send(command, cancellationToken);

        return result.Outcome switch
        {
            CreateChannelOutcome.Success => TypedResults.Created($"/channels/{result.Channel!.Id}", result.Channel),
            CreateChannelOutcome.AgentNotFound => TypedResults.ValidationProblem(BuildAgentNotFoundErrors()),
            _ => TypedResults.Problem(AgentValidationFailedDetail, statusCode: StatusCodes.Status502BadGateway),
        };
    }

    private static async Task<Ok<IReadOnlyList<ChannelResponse>>> ListChannelsAsync(
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var channels = await mediator.Send(new ListChannelsQuery(), cancellationToken);

        return TypedResults.Ok(channels);
    }

    private static async Task<Results<Ok<ChannelResponse>, NotFound>> GetChannelByIdAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var channel = await mediator.Send(new GetChannelByIdQuery(id), cancellationToken);

        return channel is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(channel);
    }

    private static async Task<Results<Ok<ChannelResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateChannelAsync(
        Guid id,
        UpdateChannelRequest request,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var shapeErrors = ValidateUpdateShape(request.Name, request.AgentId);
        if (shapeErrors is not null)
        {
            return TypedResults.ValidationProblem(shapeErrors);
        }

        var command = new UpdateChannelCommand(id, request.Name!, request.Credential, request.AgentId!.Value);
        var result = await mediator.Send(command, cancellationToken);

        return result.Outcome switch
        {
            UpdateChannelOutcome.Success => TypedResults.Ok(result.Channel!),
            UpdateChannelOutcome.NotFound => TypedResults.NotFound(),
            UpdateChannelOutcome.AgentNotFound => TypedResults.ValidationProblem(BuildAgentNotFoundErrors()),
            _ => TypedResults.Problem(AgentValidationFailedDetail, statusCode: StatusCodes.Status502BadGateway),
        };
    }

    private static async Task<Results<Ok<ChannelResponse>, NotFound>> ActivateChannelAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new ActivateChannelCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<ChannelResponse>, NotFound>> DeactivateChannelAsync(
        Guid id,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var response = await mediator.Send(new DeactivateChannelCommand(id), cancellationToken);

        return response is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(response);
    }

    private static Dictionary<string, string[]>? ValidateShape(string? name, string? channelTypeRaw, string? credential, Guid? agentId, out ChannelType channelType)
    {
        var errors = new Dictionary<string, string[]>();
        channelType = default;

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["O nome do canal é obrigatório."];
        }

        if (string.IsNullOrWhiteSpace(channelTypeRaw) || !Enum.TryParse(channelTypeRaw, ignoreCase: true, out channelType))
        {
            errors["channelType"] = ["O tipo de canal informado é inválido. Valores aceitos: WhatsApp, Telegram."];
        }

        if (string.IsNullOrWhiteSpace(credential))
        {
            errors["credential"] = ["As credenciais do canal são obrigatórias."];
        }

        if (agentId is null || agentId == Guid.Empty)
        {
            errors["agentId"] = ["O identificador do agente responsável pelo canal é obrigatório."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static Dictionary<string, string[]>? ValidateUpdateShape(string? name, Guid? agentId)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors["name"] = ["O nome do canal é obrigatório."];
        }

        if (agentId is null || agentId == Guid.Empty)
        {
            errors["agentId"] = ["O identificador do agente responsável pelo canal é obrigatório."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static Dictionary<string, string[]> BuildAgentNotFoundErrors() =>
        new() { ["agentId"] = ["O agente informado não foi encontrado em apps/api."] };

    private const string AgentValidationFailedDetail =
        "Não foi possível validar o agente informado junto a apps/api (indisponível ou respondeu com erro).";
}
