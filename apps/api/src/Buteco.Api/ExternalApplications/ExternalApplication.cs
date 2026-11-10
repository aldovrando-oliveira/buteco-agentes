namespace Buteco.Api.ExternalApplications;

/// <summary>
/// A aplicação externa (design.md da change aplicacoes-externas-credencial, D3; spec
/// <c>external-applications</c>): um sistema de fora do Buteco que obtém token no emissor por
/// <c>client_credentials</c>. Ela tem uma linha própria aqui e um cliente no OpenIddict com o
/// mesmo <see cref="ClientId"/>; os segredos ficam em <see cref="ExternalApplicationSecret"/>, fora do
/// <c>ClientSecret</c> do OpenIddict, que guarda um segredo só por cliente (D4).
///
/// <para>
/// <b>Desativada, nunca excluída.</b> A aplicação é a autora das tasks e das métricas que a #187 vai
/// registrar, e a lista continua mostrando a desativada, que pode ser reativada.
/// </para>
/// </summary>
public sealed class ExternalApplication
{
    public Guid Id { get; private set; }

    /// <summary>Gerado (<see cref="ExternalApplicationClientId"/>), único e imutável.</summary>
    public string ClientId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>O nome em maiúsculas, para a unicidade sem diferenciar maiúscula, pelo índice único.</summary>
    public string NormalizedName { get; private set; } = null!;

    public string? Description { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    /// <summary>O último token emitido para um segredo desta aplicação (D6).</summary>
    public DateTimeOffset? LastUsedAt { get; private set; }

    public bool IsActive => DeactivatedAt is null;

    private ExternalApplication()
    {
    }

    public static ExternalApplication Create(string clientId, string name, string? description, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ClientId = clientId,
        Name = name,
        NormalizedName = Normalize(name),
        Description = description,
        CreatedAt = now,
    };

    public static string Normalize(string name) => name.ToUpperInvariant();

    public void Rename(string name, string? description)
    {
        Name = name;
        NormalizedName = Normalize(name);
        Description = description;
    }

    public void Deactivate(DateTimeOffset now) => DeactivatedAt ??= now;

    public void Activate() => DeactivatedAt = null;
}
