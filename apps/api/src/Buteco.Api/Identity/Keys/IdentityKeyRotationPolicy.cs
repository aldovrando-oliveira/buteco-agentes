namespace Buteco.Api.Identity.Keys;

/// <summary>
/// As constantes e as janelas da rotação (design.md da change emissor-oidc-api, D7).
/// Constantes no código, e não opção (convenção 2): nenhum cenário pede outro valor.
/// </summary>
public static class IdentityKeyRotationPolicy
{
    /// <summary>Quanto tempo cada chave de assinatura assina.</summary>
    public static readonly TimeSpan RotationInterval = TimeSpan.FromDays(90);

    /// <summary>
    /// Quanto antes de assinar a próxima chave aparece no JWKS. A #208 fixa o cache do
    /// JWKS abaixo disso.
    /// </summary>
    public static readonly TimeSpan PrePublication = TimeSpan.FromDays(7);

    /// <summary>
    /// Quanto tempo a chave que parou de assinar continua publicada e validando. Precisa
    /// ser ≥ duração do access token + intervalo da verificação, porque a troca efetiva
    /// acontece no primeiro tique depois de <c>signs_from</c>; o boot afirma isso
    /// (<c>ValidateIdentityIssuerConfiguration</c>).
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>Intervalo da verificação de rotação.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Chave do <c>pg_advisory_xact_lock</c> que serializa a criação de chaves entre
    /// instâncias. São os bytes ASCII de <c>BUTECOK1</c> lidos como inteiro big-endian.
    ///
    /// <para>
    /// Forma de <b>uma chave <c>bigint</c></b>, de propósito: o Postgres mantém espaços
    /// separados para a forma de 64 bits e a de dois <c>int4</c> ("these two key spaces
    /// do not overlap", documentação do PostgreSQL 18, §9.28.10), e o
    /// <c>apps/workers</c> já pega lock no mesmo banco na forma de dois <c>int4</c>
    /// (<c>ConversationContextLock</c>, <c>hashtext(agentId), hashtext(contextId)</c>).
    /// Assim não há colisão por construção, sem depender de valor de <c>hashtext</c>.
    /// </para>
    /// </summary>
    public const long AdvisoryLockKey = 4779819235982396209;

    /// <summary>Janelas da primeira chave de assinatura, ou da que substitui uma vencida.</summary>
    public static IdentityKeyWindows FirstSigningKey(DateTimeOffset now) => SigningKeyStartingAt(now);

    /// <summary>A próxima chave começa a assinar exatamente quando a ativa para.</summary>
    public static IdentityKeyWindows NextSigningKey(IdentityKey active) => SigningKeyStartingAt(active.SignsUntil);

    /// <summary>
    /// A chave de cifra não roda nesta change (Non-Goals): vale do instante em que nasce
    /// em diante, sem fim.
    /// </summary>
    public static IdentityKeyWindows EncryptionKey(DateTimeOffset now) =>
        new(now, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue);

    public static bool IsInRing(IdentityKey key, DateTimeOffset now) => key.ValidatesUntil > now;

    public static bool SignsAt(IdentityKey key, DateTimeOffset now) => key.SignsFrom <= now && now < key.SignsUntil;

    /// <summary>
    /// O que falta criar para o conjunto de chaves gravadas, no instante dado:
    /// <list type="bullet">
    /// <item>nenhuma chave de assinatura cobre o agora → uma que começa agora;</item>
    /// <item>a que cobre o agora para em menos de <see cref="PrePublication"/> e não há
    /// próxima → a próxima, começando no fim da ativa;</item>
    /// <item>nenhuma chave de cifra → uma.</item>
    /// </list>
    /// </summary>
    public static IdentityKeyPlan Plan(IReadOnlyCollection<IdentityKey> keys, DateTimeOffset now)
    {
        var signing = keys.Where(key => key.Usage == IdentityKeyUsage.Signing).ToList();
        var current = signing
            .Where(key => SignsAt(key, now))
            .OrderByDescending(key => key.SignsFrom)
            .FirstOrDefault();

        IdentityKeyWindows? newSigningKey = null;
        if (current is null)
        {
            newSigningKey = FirstSigningKey(now);
        }
        else if (current.SignsUntil - now < PrePublication && !signing.Any(key => key.SignsFrom > now))
        {
            newSigningKey = NextSigningKey(current);
        }

        var needsEncryptionKey = !keys.Any(key => key.Usage == IdentityKeyUsage.Encryption && IsInRing(key, now));

        return new IdentityKeyPlan(newSigningKey, needsEncryptionKey);
    }

    private static IdentityKeyWindows SigningKeyStartingAt(DateTimeOffset signsFrom)
    {
        var signsUntil = signsFrom + RotationInterval;
        return new IdentityKeyWindows(signsFrom, signsUntil, signsUntil + Retention);
    }
}

public sealed record IdentityKeyWindows(DateTimeOffset SignsFrom, DateTimeOffset SignsUntil, DateTimeOffset ValidatesUntil);

public sealed record IdentityKeyPlan(IdentityKeyWindows? NewSigningKey, bool NeedsEncryptionKey)
{
    public bool IsEmpty => NewSigningKey is null && !NeedsEncryptionKey;
}
