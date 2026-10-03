using Buteco.Api.Infrastructure;
using Buteco.Api.KnowledgeBases.Entities;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeSync.Connectors;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.KnowledgeBases.Commands.CreateKnowledgeBase;

/// <summary>
/// Cadastro de base. A <c>Synced</c> segue a ordem da D6 do design.md da change
/// criacao-base-sincronizada, e nenhum passo antes da gravação grava nada:
/// pasta em uso pela consulta (D5) → <c>apps/connectors</c> não configurado (D1) →
/// validação da pasta (D2, D3) → gravação com nome e URL da resposta (D7), e a
/// violação do índice da pasta vira o mesmo "pasta em uso" (D5).
/// </summary>
public sealed class CreateKnowledgeBaseCommandHandler(
    AppDbContext dbContext,
    IConnectorsFolderClient connectorsFolderClient,
    ILogger<CreateKnowledgeBaseCommandHandler> logger)
    : ICommandHandler<CreateKnowledgeBaseCommand, CreateKnowledgeBaseResult>
{
    /// <summary>Nome do índice único parcial de <c>(SyncProvider, SyncFolderId)</c> (D5 da #102).</summary>
    private const string FolderIndexName = "IX_knowledge_bases_SyncProvider_SyncFolderId";

    /// <summary>
    /// Evento do log que registra a corrida perdida no índice da pasta (D5). Público para
    /// o teste de corrida contar quantas vezes o caminho de <c>UniqueViolation</c> rodou.
    /// </summary>
    public static readonly EventId ConcurrentSyncedCreateRejectedEvent = new(1024, "ConcurrentSyncedKnowledgeBaseCreateRejected");

    public async ValueTask<CreateKnowledgeBaseResult> Handle(CreateKnowledgeBaseCommand command, CancellationToken cancellationToken)
    {
        if (command.Provider is null)
        {
            var manual = new KnowledgeBase(command.Name, command.Description);
            dbContext.KnowledgeBases.Add(manual);
            await dbContext.SaveChangesAsync(cancellationToken);

            return CreateKnowledgeBaseResult.Created(KnowledgeBaseResponse.FromEntity(manual));
        }

        var provider = command.Provider;
        var folderId = command.FolderId!;

        // Antes da chamada externa: é fato do banco do apps/api, responde 409 mesmo com o
        // apps/connectors fora do ar, e poupa uma chamada ao provedor (D5).
        var owner = await FindFolderOwnerAsync(provider, folderId, cancellationToken);
        if (owner is not null)
        {
            return CreateKnowledgeBaseResult.FolderInUse(owner.Value.Id, owner.Value.Name);
        }

        var validation = await connectorsFolderClient.DescribeFolderAsync(provider, folderId, cancellationToken);
        if (!validation.Succeeded)
        {
            return CreateKnowledgeBaseResult.ValidationFailed(validation);
        }

        // Nome e URL SEMPRE da resposta do apps/connectors (D7); o id é o pedido, que o
        // cliente já conferiu contra o devolvido.
        var folder = validation.Folder!;
        var knowledgeBase = KnowledgeBase.CreateSynced(
            command.Name, command.Description, provider, folderId, folder.Name, folder.WebUrl);
        dbContext.KnowledgeBases.Add(knowledgeBase);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsFolderUniqueViolation(exception))
        {
            // Outro cadastro da mesma pasta passou pela consulta junto com este e gravou
            // primeiro (D5). Detach da base Added e UMA releitura: o Postgres só entrega a
            // violação ao perdedor depois do commit do vencedor, então ele já está gravado.
            // É o idioma de UpsertSyncedDocumentCommandHandler. O log é a evidência de que
            // a corrida aconteceu, e é o que o teste conta.
            logger.LogInformation(
                ConcurrentSyncedCreateRejectedEvent,
                "Cadastro concorrente de base sincronizada: a pasta do provedor {Provider} foi gravada por outra requisição; respondido como pasta em uso.",
                provider);
            dbContext.Entry(knowledgeBase).State = EntityState.Detached;

            var winner = await FindFolderOwnerAsync(provider, folderId, cancellationToken)
                ?? throw new InvalidOperationException(
                    "Violação de unicidade da pasta sem base vencedora na releitura.", exception);

            return CreateKnowledgeBaseResult.FolderInUse(winner.Id, winner.Name);
        }

        return CreateKnowledgeBaseResult.Created(KnowledgeBaseResponse.FromEntity(knowledgeBase));
    }

    // Comparação como veio (collation determinística do banco): 'AbC' e 'abc' são pastas
    // diferentes (D5 da #102). Ativa ou inativa, a pasta é da base.
    private async Task<(Guid Id, string Name)?> FindFolderOwnerAsync(
        string provider, string folderId, CancellationToken cancellationToken)
    {
        var owner = await dbContext.KnowledgeBases.AsNoTracking()
            .Where(knowledgeBase => knowledgeBase.ContentMode == KnowledgeBaseContentMode.Synced &&
                                    knowledgeBase.SyncProvider == provider &&
                                    knowledgeBase.SyncFolderId == folderId)
            .Select(knowledgeBase => new { knowledgeBase.Id, knowledgeBase.Name })
            .FirstOrDefaultAsync(cancellationToken);

        return owner is null ? null : (owner.Id, owner.Name);
    }

    private static bool IsFolderUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        postgres.ConstraintName == FolderIndexName;
}
