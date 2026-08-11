using Buteco.Inbox.Contacts.Entities;

namespace Buteco.Inbox.Contacts;

public interface IContactSessionResolver
{
    // contactMetadata é obrigatório (não opcional/nullable) — assinatura
    // explícita sobre "nenhum metadado" ser uma escolha ativa de quem
    // chama, não um default inofensivo (inbox-adapter-waha, design.md,
    // Decision 8). Usado só quando um novo Contact é criado; ignorado ao
    // reaproveitar um Contact já existente.
    Task<Session> FindOrCreateSessionAsync(
        Guid channelId,
        string externalId,
        IReadOnlyDictionary<string, string> contactMetadata,
        CancellationToken cancellationToken);
}
