using Buteco.Api.Agents.Entities;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.ExternalApplications;

/// <summary>
/// Os agentes liberados para cada aplicação externa e, no agente, as aplicações com acesso (design.md da
/// change agentes-por-aplicacao, D5; spec <c>external-application-agents</c>).
///
/// <list type="bullet">
/// <item><b>Sob <c>/applications/{id}/agents</c>, só o Admin com o papel gravado</b>
/// (<see cref="CurrentUser.FindAdminAsync"/>), como o resto de <c>/applications</c>: liberar um agente é
/// dar alcance a uma credencial permanente, e o rebaixado perde isso na requisição seguinte. Sem senha
/// (Q10): liberar não gera credencial, como editar na #186;</item>
/// <item><b>no agente, duas rotas, e não uma com resposta por papel</b> (Q8): a contagem para os três
/// papéis e a lista só para o Admin. Cada uma é uma linha da <c>RoleRoutes</c> e da matriz da spec, e o
/// guarda da matriz cobre as duas; uma rota só, que escondesse a lista pelo papel dentro do handler,
/// passaria nele mesmo vazando a lista para o Leitor.</item>
/// </list>
/// </summary>
public static class ExternalApplicationAgentEndpoints
{
    public sealed record GrantAgentRequest(Guid? AgenteId, List<string?>? Operacoes);

    public sealed record UpdateAgentOperationsRequest(List<string?>? Operacoes);

    private const string PrimaryKeyName = "PK_external_application_agents";

    public static IEndpointRouteBuilder MapExternalApplicationAgentEndpoints(this IEndpointRouteBuilder app)
    {
        var applications = app.MapGroup(ExternalApplicationEndpoints.GroupPath);
        applications.MapGet("/{id:guid}/agents", ListAsync);
        applications.MapPost("/{id:guid}/agents", GrantAsync);
        applications.MapPut("/{id:guid}/agents/{agentId:guid}", UpdateAsync);
        applications.MapDelete("/{id:guid}/agents/{agentId:guid}", RemoveAsync);

        var agents = app.MapGroup("/agents");
        agents.MapGet("/{id:guid}/applications", ApplicationsOfAgentAsync);
        agents.MapGet("/{id:guid}/applications/count", CountForAgentAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid id, HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (!await dbContext.ExternalApplications.AnyAsync(application => application.Id == id, cancellationToken))
        {
            return ExternalApplicationFields.NotFound();
        }

        return TypedResults.Ok(await ItemsAsync(dbContext, id, agentId: null, cancellationToken));
    }

    private static async Task<IResult> GrantAsync(
        Guid id, GrantAgentRequest request, HttpContext httpContext, AppDbContext dbContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (!await dbContext.ExternalApplications.AnyAsync(application => application.Id == id, cancellationToken))
        {
            return ExternalApplicationFields.NotFound();
        }

        if (ExternalApplicationOperations.Validate(request.Operacoes, out var operations) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (request.AgenteId is not { } agentId || !await dbContext.Agents.AnyAsync(agent => agent.Id == agentId, cancellationToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["agenteId"] = [ExternalApplicationOperations.AgentNotFoundMessage],
            });
        }

        if (await dbContext.ExternalApplicationAgents.AnyAsync(link => link.ApplicationId == id && link.AgentId == agentId, cancellationToken))
        {
            return AlreadyGranted();
        }

        dbContext.ExternalApplicationAgents.Add(ExternalApplicationAgent.Create(id, agentId, operations, timeProvider.GetUtcNow()));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
            postgres.ConstraintName == PrimaryKeyName)
        {
            // Dois Admins liberando o mesmo agente ao mesmo tempo: a chave decide, e o segundo vê o 409,
            // sem gravar por cima do primeiro.
            return AlreadyGranted();
        }

        var item = (await ItemsAsync(dbContext, id, agentId, cancellationToken)).Single();
        return TypedResults.Created($"{ExternalApplicationEndpoints.GroupPath}/{id}/agents/{agentId}", item);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        Guid agentId,
        UpdateAgentOperationsRequest request,
        HttpContext httpContext,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (!await dbContext.ExternalApplications.AnyAsync(application => application.Id == id, cancellationToken))
        {
            return ExternalApplicationFields.NotFound();
        }

        if (ExternalApplicationOperations.Validate(request.Operacoes, out var operations) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (await dbContext.ExternalApplicationAgents.FindAsync([id, agentId], cancellationToken) is not { } link)
        {
            return NotGranted();
        }

        link.Replace(operations, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ItemsAsync(dbContext, id, agentId, cancellationToken)).Single());
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, Guid agentId, HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (!await dbContext.ExternalApplications.AnyAsync(application => application.Id == id, cancellationToken))
        {
            return ExternalApplicationFields.NotFound();
        }

        var removed = await dbContext.ExternalApplicationAgents
            .Where(link => link.ApplicationId == id && link.AgentId == agentId)
            .ExecuteDeleteAsync(cancellationToken);
        return removed == 0 ? NotGranted() : TypedResults.NoContent();
    }

    /// <summary>Só o Admin com o papel gravado: os nomes das aplicações são da matriz da #184 só para ele.</summary>
    private static async Task<IResult> ApplicationsOfAgentAsync(
        Guid id, HttpContext httpContext, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (await CurrentUser.FindAdminAsync(httpContext.User, dbContext, cancellationToken) is null)
        {
            return CurrentUser.Forbidden();
        }

        if (!await dbContext.Agents.AnyAsync(agent => agent.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var rows = await dbContext.ExternalApplicationAgents
            .AsNoTracking()
            .Where(link => link.AgentId == id)
            .Join(dbContext.ExternalApplications, link => link.ApplicationId, application => application.Id, (link, application) => new
            {
                application.Id,
                application.Name,
                application.NormalizedName,
                application.DeactivatedAt,
                application.LastUsedAt,
                link.Operations,
            })
            .OrderBy(row => row.DeactivatedAt != null)
            .ThenBy(row => row.NormalizedName)
            .ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(rows
            .Select(row => new AgentApplicationItem(row.Id, row.Name, row.DeactivatedAt is null, Ordered(row.Operations), row.LastUsedAt))
            .ToList());
    }

    /// <summary>
    /// Os três papéis: só números, sem nome nem id de aplicação (Q9). É o "Usado por N aplicações externas
    /// ativas (M no total)" do card para o Operador e o Leitor.
    /// </summary>
    private static async Task<IResult> CountForAgentAsync(Guid id, AppDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.Agents.AnyAsync(agent => agent.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var states = await dbContext.ExternalApplicationAgents
            .AsNoTracking()
            .Where(link => link.AgentId == id)
            .Join(dbContext.ExternalApplications, link => link.ApplicationId, application => application.Id, (_, application) => application.DeactivatedAt == null)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AgentApplicationCount(states.Count, states.Count(active => active)));
    }

    /// <summary>Os agentes liberados para a aplicação (ou só um deles), no formato da listagem.</summary>
    private static async Task<List<ApplicationAgentItem>> ItemsAsync(
        AppDbContext dbContext, Guid applicationId, Guid? agentId, CancellationToken cancellationToken)
    {
        var rows = await dbContext.ExternalApplicationAgents
            .AsNoTracking()
            .Where(link => link.ApplicationId == applicationId && (agentId == null || link.AgentId == agentId))
            .Join(dbContext.Agents, link => link.AgentId, agent => agent.Id, (link, agent) => new { Link = link, Agent = agent })
            .OrderBy(row => row.Agent.Name)
            .ThenBy(row => row.Agent.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(row => ApplicationAgentItem.From(row.Link, row.Agent)).ToList();
    }

    internal static IReadOnlyList<string> Ordered(IEnumerable<string> operations) =>
        ExternalApplicationOperations.All.Where(operations.Contains).ToList();

    private static IResult AlreadyGranted() =>
        TypedResults.Problem(title: ExternalApplicationOperations.AlreadyGrantedMessage, statusCode: StatusCodes.Status409Conflict);

    private static IResult NotGranted() =>
        TypedResults.Problem(title: ExternalApplicationOperations.NotGrantedMessage, statusCode: StatusCodes.Status404NotFound);
}

public sealed record ApplicationAgentItem(
    Guid AgenteId,
    string Nome,
    string? Descricao,
    bool Ativo,
    IReadOnlyList<string> Operacoes,
    DateTimeOffset LiberadoEm)
{
    public static ApplicationAgentItem From(ExternalApplicationAgent link, Agent agent) => new(
        agent.Id,
        agent.Name,
        agent.Description,
        agent.IsActive,
        ExternalApplicationAgentEndpoints.Ordered(link.Operations),
        link.GrantedAt);
}

public sealed record AgentApplicationItem(Guid Id, string Nome, bool Ativa, IReadOnlyList<string> Operacoes, DateTimeOffset? UltimoUso);

public sealed record AgentApplicationCount(int Total, int Ativas);

/// <summary>Um agente liberado na lista de aplicações (Q11): o chip "Agentes liberados" da tela.</summary>
public sealed record ApplicationAgentChip(Guid Id, string Nome);
