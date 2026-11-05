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
    /// ser ≥ a maior duração entre access e refresh token + intervalo da verificação,
    /// porque a troca efetiva acontece no primeiro tique depois de <c>signs_from</c>; o boot
    /// afirma isso (<c>ValidateIdentityIssuerConfiguration</c>).
    ///
    /// <para>
    /// <b>Quem manda é o refresh</b> (design.md da change usuarios-locais-login-emissor,
    /// D3.7): o OpenIddict valida o refresh, o código e o <c>id_token_hint</c> com as mesmas
    /// chaves de <c>SigningCredentials</c> (<c>OpenIddictServerConfiguration.cs</c> l.
    /// 556–558, <c>OpenIddictServerHandlers.Protection.cs</c> l. 189 e 476). Com 24 h, quem
    /// ficasse um dia sem usar o painel na troca de chave perderia a sessão antes dos 14
    /// dias. 15 dias cobrem os 14 d de <see cref="IdentityTokenLifetimes.RefreshToken"/> + 1 h. A janela gravada nas chaves já existentes é
    /// estendida pela migration <c>ExtendIdentityKeyRetention</c>.
    /// </para>
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(15);

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

    public static bool IsInRing(IdentityKey key, DateTimeOffset now) => InRing(key.ValidatesUntil, now);

    public static bool SignsAt(IdentityKey key, DateTimeOffset now) => Signs(key.SignsFrom, key.SignsUntil, now);

    // Uma conta só para o anel e para o GET /identity (leitura-identidade-emissor, D3): as
    // sobrecargas acima e a Describe abaixo passam por aqui.
    private static bool InRing(DateTimeOffset validatesUntil, DateTimeOffset now) => validatesUntil > now;

    private static bool Signs(DateTimeOffset signsFrom, DateTimeOffset signsUntil, DateTimeOffset now) =>
        signsFrom <= now && now < signsUntil;

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

    /// <summary>
    /// O estado de cada chave de assinatura que ainda valida, para o <c>GET /identity</c>
    /// (design.md da change leitura-identidade-emissor, D3), no instante dado:
    /// <list type="bullet">
    /// <item><see cref="IdentityKeyState.InUse"/>: assina agora e é a de maior
    /// <c>SignsFrom</c> entre as que assinam, a mesma escolha do
    /// <see cref="IdentityKeyRingSnapshot.Build"/>;</item>
    /// <item><see cref="IdentityKeyState.Next"/>: começa a assinar depois de agora;</item>
    /// <item><see cref="IdentityKeyState.Retiring"/>: não assina mais (ou assina sem ser a
    /// escolhida) e ainda valida.</item>
    /// </list>
    /// A que não valida mais fica fora, como fica fora do anel. A ordem é a do quadro
    /// Identidade: em uso, próxima, aposentando, e dentro do mesmo estado o
    /// <c>SignsFrom</c> mais novo primeiro.
    /// </summary>
    public static IReadOnlyList<IdentityKeyDescription> Describe(IReadOnlyCollection<IdentityKeyWindowsView> keys, DateTimeOffset now)
    {
        var inRing = keys.Where(key => InRing(key.ValidatesUntil, now)).ToList();
        var inUseKid = inRing
            .Where(key => Signs(key.SignsFrom, key.SignsUntil, now))
            .OrderByDescending(key => key.SignsFrom)
            .Select(key => key.Kid)
            .FirstOrDefault();

        return inRing
            .Select(key => new IdentityKeyDescription(key, StateOf(key, now, inUseKid)))
            .OrderBy(description => description.State)
            .ThenByDescending(description => description.Key.SignsFrom)
            .ToList();
    }

    private static IdentityKeyState StateOf(IdentityKeyWindowsView key, DateTimeOffset now, string? inUseKid)
    {
        if (key.Kid == inUseKid)
        {
            return IdentityKeyState.InUse;
        }

        return key.SignsFrom > now ? IdentityKeyState.Next : IdentityKeyState.Retiring;
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

/// <summary>
/// Uma chave de assinatura como o <c>GET /identity</c> a lê (design.md da change
/// leitura-identidade-emissor, D4): identificador, criação e as três janelas. Não tem
/// propriedade de material, de propósito: nada que a serialize pode vazar a chave.
/// </summary>
public sealed record IdentityKeyWindowsView(
    string Kid,
    DateTimeOffset CreatedAt,
    DateTimeOffset SignsFrom,
    DateTimeOffset SignsUntil,
    DateTimeOffset ValidatesUntil);

/// <summary>Estado de uma chave de assinatura, na ordem em que o quadro Identidade as lista.</summary>
public enum IdentityKeyState
{
    InUse,
    Next,
    Retiring,
}

public sealed record IdentityKeyDescription(IdentityKeyWindowsView Key, IdentityKeyState State);
