using System.Text.Json.Serialization;

namespace Buteco.Api.KnowledgeSync.Responses;

public sealed record UpsertSyncedDocumentResponse(Guid DocumentId, SyncedDocumentUpsertOutcome Outcome);

/// <summary>
/// O que o upsert fez (D8, D3). <see cref="Unchanged"/> é o marcador novo com
/// título e texto iguais: só o marcador foi gravado, sem evento e sem indexação.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SyncedDocumentUpsertOutcome>))]
public enum SyncedDocumentUpsertOutcome
{
    Created,
    Updated,
    Unchanged,
}
