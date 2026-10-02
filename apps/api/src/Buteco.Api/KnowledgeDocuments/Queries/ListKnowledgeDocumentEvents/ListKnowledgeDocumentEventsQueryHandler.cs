using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeDocuments.Responses;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocumentEvents;

/// <summary>
/// Devolve <c>null</c> quando a base não existe (vira 404 no endpoint); página
/// vazia com <c>nextCursor</c> nulo quando a base existe e não tem evento.
/// </summary>
public sealed class ListKnowledgeDocumentEventsQueryHandler(AppDbContext dbContext)
    : IQueryHandler<ListKnowledgeDocumentEventsQuery, KnowledgeDocumentEventPageResponse?>
{
    /// <summary>
    /// Fixo, sem parâmetro de cliente (D7): "Carregar mais" não tem cenário real
    /// para outro tamanho.
    /// </summary>
    public const int PageSize = 50;

    public async ValueTask<KnowledgeDocumentEventPageResponse?> Handle(
        ListKnowledgeDocumentEventsQuery query, CancellationToken cancellationToken)
    {
        // Base inativa é lida normalmente: desativar impede o uso pelo agente,
        // não a leitura da manutenção.
        var knowledgeBaseExists = await dbContext.KnowledgeBases
            .AnyAsync(knowledgeBase => knowledgeBase.Id == query.KnowledgeBaseId, cancellationToken);

        if (!knowledgeBaseExists)
        {
            return null;
        }

        // Filtro SEMPRE pela base da rota, com ou sem cursor (D8): o cursor não
        // carrega a base, então nunca abre a porta para outra.
        var events = dbContext.KnowledgeDocumentEvents
            .AsNoTracking()
            .Where(documentEvent => documentEvent.KnowledgeBaseId == query.KnowledgeBaseId);

        if (query.After is { } after)
        {
            // Comparação de row value no SQL — (OccurredAt, Id) < (@at, @id) —,
            // verificada no SQL gerado pela 10.0.3 do provider (D7, tarefa 1.1).
            // A ordem é a mesma do ORDER BY abaixo, pelo mesmo operador de uuid
            // do banco: a página seguinte começa estritamente depois do último
            // item entregue, e evento novo no topo não desloca nada.
            events = events.Where(documentEvent => EF.Functions.LessThan(
                ValueTuple.Create(documentEvent.OccurredAt, documentEvent.Id),
                ValueTuple.Create(after.OccurredAt, after.Id)));
        }

        // Mais recente primeiro, com desempate por Id (api-response-ordering):
        // OccurredAt não é único. Busca UMA linha além da página só para saber
        // se há próxima — o cliente nunca pede uma página vazia para descobrir
        // que acabou.
        var rows = await events
            .OrderByDescending(documentEvent => documentEvent.OccurredAt)
            .ThenByDescending(documentEvent => documentEvent.Id)
            .Take(PageSize + 1)
            .Select(documentEvent => new KnowledgeDocumentEventResponse(
                documentEvent.Id,
                documentEvent.DocumentId,
                documentEvent.DocumentTitle,
                documentEvent.Type,
                documentEvent.ContentChanged,
                documentEvent.TitleChanged,
                documentEvent.Author,
                documentEvent.OccurredAt))
            .ToListAsync(cancellationToken);

        var hasNextPage = rows.Count > PageSize;
        var items = hasNextPage ? rows[..PageSize] : rows;

        // O cursor sai da última linha LIDA DO BANCO, nunca de um valor em
        // memória: o timestamptz tem resolução de microssegundo.
        var nextCursor = hasNextPage
            ? new KnowledgeDocumentEventCursor(items[^1].OccurredAt, items[^1].Id).Encode()
            : null;

        return new KnowledgeDocumentEventPageResponse(items, nextCursor);
    }
}
