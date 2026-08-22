using Buteco.Inbox.Messages.Entities;

namespace Buteco.Inbox.Contacts.Responses;

public sealed record ChannelSessionResponse(
    Guid SessionId,
    Guid ContactId,
    string ContactExternalId,
    string? ContactDisplayName,
    DateTimeOffset LastActivityAt,
    MessagePreviewResponse? LastMessage);

// Prévia da última Message (entrada ou saída) da sessão — sem ela a etapa 3
// faria N+1 para montar a lista (inbox-mensagens-persistidas, design.md,
// Decisão 10).
public sealed record MessagePreviewResponse(MessageDirection Direction, string Content, DateTimeOffset OccurredAt);
