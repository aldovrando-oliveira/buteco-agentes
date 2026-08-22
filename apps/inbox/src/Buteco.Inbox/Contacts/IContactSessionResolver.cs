using Buteco.Inbox.Contacts.Entities;

namespace Buteco.Inbox.Contacts;

public interface IContactSessionResolver
{
    // contactMetadata é obrigatório (não opcional/nullable) — assinatura
    // explícita sobre "nenhum metadado" ser uma escolha ativa de quem
    // chama, não um default inofensivo (inbox-adapter-waha, design.md,
    // Decision 8). Usado só quando um novo Contact é criado; ignorado ao
    // reaproveitar um Contact já existente. displayName, ao contrário, é
    // nullable e usado em toda chamada, criação ou reaproveitamento —
    // nulo não apaga um valor já persistido (inbox-mensagens-persistidas,
    // design.md, Decisão 9).
    Task<Session> FindOrCreateSessionAsync(
        Guid channelId,
        string externalId,
        IReadOnlyDictionary<string, string> contactMetadata,
        string? displayName,
        CancellationToken cancellationToken);
}
