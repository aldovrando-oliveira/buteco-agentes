namespace Buteco.Api.ExternalApplications;

/// <summary>
/// Um segredo de aplicação externa (design.md da change aplicacoes-externas-credencial, D4; spec
/// <c>external-applications</c>). Só o SHA-256 do texto e o sufixo de 4 caracteres ficam gravados;
/// o texto aparece uma vez, na resposta que o cria (<see cref="ExternalApplicationSecretValue"/>).
///
/// <para>
/// <b>O estado não é gravado</b>, é derivado na leitura (<see cref="StateAt"/>): gravar "substituído"
/// ou "expirado" exigiria um processo para virar a coluna no instante certo, e o emissor confere
/// <see cref="IsAliveAt"/> pelas mesmas datas, sem essa coluna.
/// </para>
/// </summary>
public sealed class ExternalApplicationSecret
{
    /// <summary>A validade de um segredo novo: 1 ano. Constante, não opção (convenção 2; Q12).</summary>
    public static readonly TimeSpan Validity = TimeSpan.FromDays(365);

    /// <summary>
    /// Quanto os segredos vivos anteriores ainda valem depois de gerar um novo, para a aplicação trocar
    /// sem parar. Constante, não opção (convenção 2; Q12): se um caso real pedir outro valor, vira opção.
    /// </summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromDays(7);

    /// <summary>Segredos vivos por aplicação (Q8): gerar o quarto responde <c>409</c>.</summary>
    public const int MaxAlive = 3;

    public Guid Id { get; private set; }

    public Guid ApplicationId { get; private set; }

    /// <summary>SHA-256 do texto (<see cref="ExternalApplicationSecretValue.Hash"/>), com índice único.</summary>
    public byte[] Hash { get; private set; } = null!;

    /// <summary>Os 4 últimos caracteres do texto, para a lista (<c>btc_sk_••••a91f</c>).</summary>
    public string Suffix { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    private ExternalApplicationSecret()
    {
    }

    public static ExternalApplicationSecret Create(Guid applicationId, string value, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ApplicationId = applicationId,
        Hash = ExternalApplicationSecretValue.Hash(value),
        Suffix = ExternalApplicationSecretValue.Suffix(value),
        CreatedAt = now,
        ExpiresAt = now + Validity,
    };

    /// <summary>Não revogado e não vencido: o que o emissor aceita.</summary>
    public bool IsAliveAt(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>
    /// A sobreposição (D4): o segredo vivo passa a valer até no máximo <paramref name="now"/> +
    /// <see cref="Overlap"/>. O <c>min</c> é o que impede a sobreposição de ESTENDER um segredo que
    /// já venceria antes; sem ele, gerar um segredo novo daria mais 7 dias a um segredo prestes a
    /// vencer.
    /// </summary>
    public void Supersede(DateTimeOffset now)
    {
        var limit = now + Overlap;
        if (ExpiresAt > limit)
        {
            ExpiresAt = limit;
        }
    }

    /// <summary>Revogar o já revogado não muda a data da primeira revogação.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>
    /// O estado, na ordem da spec: revogado, depois expirado, depois substituído (existe segredo mais
    /// novo da mesma aplicação), depois ativo. Função pura, para os quatro casos sem banco.
    /// </summary>
    public ExternalApplicationSecretState StateAt(DateTimeOffset now, bool hasNewer) =>
        RevokedAt is not null ? ExternalApplicationSecretState.Revoked
        : ExpiresAt <= now ? ExternalApplicationSecretState.Expired
        : hasNewer ? ExternalApplicationSecretState.Superseded
        : ExternalApplicationSecretState.Active;
}

public enum ExternalApplicationSecretState
{
    Active,
    Superseded,
    Expired,
    Revoked,
}

/// <summary>O estado no JSON da resposta, em português, como o papel (<c>UserRoleText.WriteWire</c>).</summary>
public static class ExternalApplicationSecretStateText
{
    public static string WriteWire(ExternalApplicationSecretState state) => state switch
    {
        ExternalApplicationSecretState.Active => "ativo",
        ExternalApplicationSecretState.Superseded => "substituido",
        ExternalApplicationSecretState.Expired => "expirado",
        ExternalApplicationSecretState.Revoked => "revogado",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
