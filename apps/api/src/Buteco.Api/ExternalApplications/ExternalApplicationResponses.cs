namespace Buteco.Api.ExternalApplications;

// As respostas de /applications (spec external-applications). Nenhuma traz o hash; o texto do segredo
// só aparece em CreatedSecret, nas duas rotas que o criam.

public sealed record ApplicationSummary(
    Guid Id,
    string Nome,
    string? Descricao,
    string ClientId,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? DesativadaEm,
    DateTimeOffset? UltimoUso,
    IReadOnlyList<ApplicationAgentChip> Agentes)
{
    /// <summary>
    /// <paramref name="agents"/>: os agentes liberados para a aplicação, pelo nome (agentes-por-aplicacao,
    /// Q11), o chip "Agentes liberados" da lista.
    /// </summary>
    public static ApplicationSummary From(ExternalApplication application, IReadOnlyList<ApplicationAgentChip> agents) => new(
        application.Id,
        application.Name,
        application.Description,
        application.ClientId,
        application.IsActive,
        application.CreatedAt,
        application.DeactivatedAt,
        application.LastUsedAt,
        agents);
}

public sealed record SecretSummary(
    Guid Id,
    string Sufixo,
    string Estado,
    DateTimeOffset CriadoEm,
    DateTimeOffset ValidoAte,
    DateTimeOffset? RevogadoEm,
    DateTimeOffset? UltimoUso);

public sealed record ApplicationDetail(
    Guid Id,
    string Nome,
    string? Descricao,
    string ClientId,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? DesativadaEm,
    DateTimeOffset? UltimoUso,
    string EndpointToken,
    IReadOnlyList<SecretSummary> Segredos)
{
    /// <summary>
    /// Os segredos do mais novo para o mais antigo, com o estado derivado agora: "substituído" é ter
    /// um segredo mais novo na mesma aplicação (<see cref="ExternalApplicationSecret.StateAt"/>).
    /// </summary>
    public static ApplicationDetail From(
        ExternalApplication application, IReadOnlyList<ExternalApplicationSecret> secrets, string endpointToken, DateTimeOffset now)
    {
        var ordered = secrets.OrderByDescending(secret => secret.CreatedAt).ThenByDescending(secret => secret.Id).ToList();
        var summaries = ordered.Select((secret, index) => new SecretSummary(
            secret.Id,
            secret.Suffix,
            ExternalApplicationSecretStateText.WriteWire(secret.StateAt(now, hasNewer: index > 0)),
            secret.CreatedAt,
            secret.ExpiresAt,
            secret.RevokedAt,
            secret.LastUsedAt)).ToList();

        return new ApplicationDetail(
            application.Id,
            application.Name,
            application.Description,
            application.ClientId,
            application.IsActive,
            application.CreatedAt,
            application.DeactivatedAt,
            application.LastUsedAt,
            endpointToken,
            summaries);
    }
}

public sealed record CreatedSecret(Guid Id, string Valor, string Sufixo, DateTimeOffset CriadoEm, DateTimeOffset ValidoAte)
{
    public static CreatedSecret From(ExternalApplicationSecret secret, string value) =>
        new(secret.Id, value, secret.Suffix, secret.CreatedAt, secret.ExpiresAt);
}

public sealed record CreatedApplication(
    Guid Id,
    string Nome,
    string? Descricao,
    string ClientId,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? UltimoUso,
    string EndpointToken,
    CreatedSecret Segredo);
