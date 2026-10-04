using Buteco.Api.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.KnowledgeBases.Commands.DeleteKnowledgeBase;

/// <summary>
/// Exclusão real de base de conhecimento — a exceção à regra "catálogo só se
/// desativa" (design.md da change exclusao-base-conhecimento, D1). Só base inativa
/// (D2), e tudo que é dela vai junto, numa transação (D3):
///
/// <list type="number">
/// <item><c>FOR UPDATE</c> na linha da base, e a conferência de <c>IsActive</c> sob o
/// bloqueio — uma ativação concorrente espera, ou a exclusão a vê e recusa;</item>
/// <item>os documentos, apagados aqui (os fragmentos vão pela cascata documento →
/// fragmento);</item>
/// <item>a base (os eventos de histórico e os vínculos com agentes vão pelas cascatas
/// base → evento e base → vínculo).</item>
/// </list>
///
/// <para>
/// O bloqueio vem ANTES de apagar os documentos: uma inclusão faz <c>FOR KEY SHARE</c>
/// na base, que conflita com o <c>FOR UPDATE</c>. Sem ele, um upsert que commitasse
/// entre o passo 2 e o 3 faria o <c>Restrict</c> da FK documento → base recusar o
/// passo 3. A FK continua <c>Restrict</c> de propósito: é a rede para qualquer outro
/// caminho que esqueça os documentos (D3).
/// </para>
/// </summary>
public sealed class DeleteKnowledgeBaseCommandHandler(
    AppDbContext dbContext,
    ILogger<DeleteKnowledgeBaseCommandHandler> logger)
    : ICommandHandler<DeleteKnowledgeBaseCommand, DeleteKnowledgeBaseResult>
{
    /// <summary>
    /// A única pegada de uma ação irreversível depois que o histórico vai junto
    /// (D11). Segue 1021, 1022 e 1024.
    /// </summary>
    public static readonly EventId KnowledgeBaseDeletedEvent = new(1025, "KnowledgeBaseDeleted");

    public async ValueTask<DeleteKnowledgeBaseResult> Handle(DeleteKnowledgeBaseCommand command, CancellationToken cancellationToken)
    {
        // A ordem medida dos comandos não produz impasse com as escritas de /sync (D6);
        // o caminho existe para que um impasse, se acontecer, nunca vire 500. Repete a
        // transação UMA vez.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                return await DeleteOnceAsync(command.Id, cancellationToken);
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        return DeleteKnowledgeBaseResult.ConcurrentWrite;
    }

    private async Task<DeleteKnowledgeBaseResult> DeleteOnceAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Sem composição sobre o FromSql: o EF emite o SQL como está, com o FOR UPDATE
        // no nível de fora.
        var locked = await dbContext.KnowledgeBases
            .FromSql($"""SELECT * FROM knowledge_bases WHERE "Id" = {id} FOR UPDATE""")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (locked.Count == 0)
        {
            return DeleteKnowledgeBaseResult.NotFound;
        }

        var knowledgeBase = locked[0];
        if (knowledgeBase.IsActive)
        {
            return DeleteKnowledgeBaseResult.Active;
        }

        var documents = await dbContext.KnowledgeDocuments
            .Where(document => document.KnowledgeBaseId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.KnowledgeBases
            .Where(candidate => candidate.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            KnowledgeBaseDeletedEvent,
            "Base de conhecimento {KnowledgeBaseId} ({ContentMode}) excluída com {DocumentCount} documento(s).",
            id,
            knowledgeBase.ContentMode,
            documents);

        return DeleteKnowledgeBaseResult.Deleted;
    }

    private static bool IsDeadlock(Exception exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } ||
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected };
}
