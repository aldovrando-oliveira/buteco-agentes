using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.KnowledgeSync;

/// <summary>
/// Gravações de <c>/sync</c> que falham porque a base foi excluída entre a leitura e
/// o <c>SaveChanges</c> (design.md da change exclusao-base-conhecimento, D6). A falha
/// tem três formas, e todas levam à mesma pergunta — a base ainda existe? — antes de
/// decidir a resposta: sem ela, 404, que a #105 lê como fim do ciclo daquela base.
/// </summary>
public static class KnowledgeBaseWriteFailures
{
    /// <summary>
    /// FK composta documento → base, como o Postgres a guarda: o EF trunca o nome em 63
    /// caracteres com <c>~</c>. Lido do banco migrado (<c>pg_constraint</c>), não do
    /// modelo.
    /// </summary>
    public const string DocumentBaseForeignKey = "FK_knowledge_documents_knowledge_bases_KnowledgeBaseId_Knowled~";

    /// <summary>FK evento → base, lida do banco migrado.</summary>
    public const string EventBaseForeignKey = "FK_knowledge_document_events_knowledge_bases_KnowledgeBaseId";

    /// <summary>
    /// A gravação pode ter falhado por a base ter sumido: violação de uma das duas FKs
    /// para a base (inclusão de documento ou de evento), <c>UPDATE</c>/<c>DELETE</c> que
    /// não achou a linha (<see cref="DbUpdateConcurrencyException"/>), ou impasse. Só a
    /// violação DESTAS duas FKs entra; qualquer outra sobe.
    /// </summary>
    public static bool MayBeDeletedBase(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException ||
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: DocumentBaseForeignKey or EventBaseForeignKey,
        } ||
        IsDeadlock(exception);

    public static bool IsDeadlock(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected };

    /// <summary>Relê a existência da base, depois de limpar o que a tentativa deixou no change tracker.</summary>
    public static async Task<bool> KnowledgeBaseIsGoneAsync(
        this AppDbContext dbContext, Guid knowledgeBaseId, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        return await dbContext.LookupSyncedKnowledgeBaseAsync(knowledgeBaseId, cancellationToken) == SyncedKnowledgeBaseLookup.NotFound;
    }
}
