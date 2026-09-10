using Buteco.Api.Infrastructure;
using Buteco.Api.McpServers.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.McpServers.Queries.ListMcpServers;

public sealed class ListMcpServersQueryHandler(AppDbContext dbContext) : IQueryHandler<ListMcpServersQuery, IReadOnlyList<McpServerResponse>>
{
    public async ValueTask<IReadOnlyList<McpServerResponse>> Handle(ListMcpServersQuery query, CancellationToken cancellationToken)
    {
        return await dbContext.McpServers
            .AsNoTracking()
            // Desempate estável: CreatedAt não é único — é atribuído no
            // construtor da entidade e dois registros podem compartilhar o
            // instante —, então ordenar só por ele deixa a ordem entre
            // empatados a cargo do plano do Postgres (api-response-ordering).
            .OrderBy(mcpServer => mcpServer.CreatedAt)
            .ThenBy(mcpServer => mcpServer.Id)
            .Select(mcpServer => McpServerResponse.FromEntity(mcpServer))
            .ToListAsync(cancellationToken);
    }
}
