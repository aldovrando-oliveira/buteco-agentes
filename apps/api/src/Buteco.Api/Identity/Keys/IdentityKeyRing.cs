using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Identity.Keys;

/// <summary>
/// As chaves vigentes do emissor, decifradas, em memória (design.md da change
/// emissor-oidc-api, D5, D6 e D7). É a única peça que lê e escreve
/// <c>identity_keys</c>; o configurer das opções do OpenIddict só lê o snapshot daqui.
///
/// <para>
/// <b>Por que não carregar no boot:</b> as fixtures de teste sobem o host antes de
/// migrar, e o <c>apps/api</c> sobe hoje sem banco. Quem carrega é o aquecimento do
/// <see cref="IdentityKeyRotationService"/>, logo depois do start, e o
/// <see cref="IdentityKeyRingGate"/>, na requisição que chegar antes dele. Os dois
/// passam por <see cref="EnsureLoadedAsync"/>, sob o mesmo semáforo.
/// </para>
///
/// <para>
/// <b>Toda carga passa pelo lock</b> <see cref="IdentityKeyRotationPolicy.AdvisoryLockKey"/>,
/// porque toda carga pode criar chave: a primeira de cada uso, ou a próxima de assinatura.
/// Duas instâncias contra o mesmo banco criam uma chave só.
/// </para>
/// </summary>
public sealed class IdentityKeyRing(
    IServiceScopeFactory scopeFactory,
    IdentityKeyMaterialCipher cipher,
    IdentityKeyFactory factory,
    TimeProvider timeProvider,
    ILogger<IdentityKeyRing> logger)
{
    public static readonly EventId KeyCreatedEvent = new(1180, "IdentityKeyCreated");
    public static readonly EventId RingChangedEvent = new(1181, "IdentityKeyRingChanged");

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private volatile IdentityKeyRingSnapshot? _snapshot;
    private CancellationTokenSource _changeSource = new();
    private int _loadCount;

    public bool IsLoaded => _snapshot is not null;

    /// <summary>O snapshot corrente, ou <c>null</c> antes da primeira carga.</summary>
    public IdentityKeyRingSnapshot? Snapshot => _snapshot;

    /// <summary>
    /// Quantas cargas do banco concluíram neste processo. Existe para o teste que afirma
    /// que uma requisição não carregou nada depois do aquecimento (tarefa 5.7).
    /// </summary>
    public int LoadCount => Volatile.Read(ref _loadCount);

    /// <summary>
    /// Disparado quando o snapshot muda (conjunto de <c>kid</c>s ou quem assina): o
    /// <c>IOptionsMonitor</c> do OpenIddict reconstrói as opções na próxima requisição.
    /// </summary>
    public IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _changeSource).Token);

    /// <summary>
    /// Carrega se ainda não carregou. Falha não fica guardada: a próxima chamada tenta de
    /// novo (D6).
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (IsLoaded)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsLoaded)
            {
                await LoadUnderLockAsync(cancellationToken);
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>A verificação de rotação (D7): cria o que faltar e recarrega.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            await LoadUnderLockAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task LoadUnderLockAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now;
        List<IdentityKey> keys;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            // Forma bigint, espaço de chave separado do (int, int) do apps/workers (D7).
            await dbContext.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock({IdentityKeyRotationPolicy.AdvisoryLockKey})",
                cancellationToken);

            // O "agora" só depois do lock: quem esperou o lock precisa enxergar a chave
            // que o anterior acabou de criar como vigente, e não como futura. Lido antes,
            // ele é anterior ao signs_from dela, e o plano criaria outra (medido pelo
            // IdentityKeyRingConcurrencyTests, com quatro anéis).
            now = timeProvider.GetUtcNow();
            keys = await dbContext.IdentityKeys.AsNoTracking().ToListAsync(cancellationToken);

            var plan = IdentityKeyRotationPolicy.Plan(keys, now);
            if (!plan.IsEmpty)
            {
                var created = new List<IdentityKey>();
                if (plan.NewSigningKey is { } windows)
                {
                    created.Add(factory.CreateSigningKey(windows, now));
                }

                if (plan.NeedsEncryptionKey)
                {
                    created.Add(factory.CreateEncryptionKey(IdentityKeyRotationPolicy.EncryptionKey(now), now));
                }

                dbContext.IdentityKeys.AddRange(created);
                await dbContext.SaveChangesAsync(cancellationToken);

                foreach (var key in created)
                {
                    logger.LogInformation(
                        KeyCreatedEvent,
                        "Chave do emissor criada: kid {Kid}, uso {Usage}, assina de {SignsFrom:O} a {SignsUntil:O}.",
                        key.Kid,
                        key.Usage,
                        key.SignsFrom,
                        key.SignsUntil);
                }

                keys.AddRange(created);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        var snapshot = IdentityKeyRingSnapshot.Build(keys, now, cipher);
        Interlocked.Increment(ref _loadCount);

        if (_snapshot?.Fingerprint == snapshot.Fingerprint)
        {
            return;
        }

        _snapshot = snapshot;
        logger.LogInformation(
            RingChangedEvent,
            "Anel de chaves do emissor carregado: assina {SigningKid}, publica {Kids}.",
            snapshot.SigningKid,
            string.Join(", ", snapshot.SigningKeys.Select(key => key.KeyId)));

        var previous = Interlocked.Exchange(ref _changeSource, new CancellationTokenSource());
        previous.Cancel();
        previous.Dispose();
    }
}

/// <summary>As credenciais que o configurer entrega ao OpenIddict.</summary>
public sealed record IdentityKeyRingSnapshot(
    IReadOnlyList<X509SecurityKey> SigningKeys,
    IReadOnlyList<SymmetricSecurityKey> EncryptionKeys,
    string SigningKid,
    string Fingerprint)
{
    /// <summary>
    /// Decifra as chaves que ainda validam no instante dado e monta as credenciais. Lança
    /// se alguma não decifrar, sem substituí-la (D4).
    /// </summary>
    public static IdentityKeyRingSnapshot Build(IReadOnlyCollection<IdentityKey> keys, DateTimeOffset now, IdentityKeyMaterialCipher cipher)
    {
        var inRing = keys.Where(key => IdentityKeyRotationPolicy.IsInRing(key, now)).OrderBy(key => key.Kid, StringComparer.Ordinal).ToList();

        var signing = inRing.Where(key => key.Usage == IdentityKeyUsage.Signing).Select(key => ToSigningKey(key, cipher)).ToList();
        var encryption = inRing.Where(key => key.Usage == IdentityKeyUsage.Encryption).Select(key => ToEncryptionKey(key, cipher)).ToList();

        var signingKid = inRing
            .Where(key => key.Usage == IdentityKeyUsage.Signing && IdentityKeyRotationPolicy.SignsAt(key, now))
            .OrderByDescending(key => key.SignsFrom)
            .Select(key => key.Kid)
            .First();

        var fingerprint = $"{signingKid}|{string.Join(',', inRing.Select(key => key.Kid))}";
        return new IdentityKeyRingSnapshot(signing, encryption, signingKid, fingerprint);
    }

    /// <summary>
    /// Embrulha a chave RSA num X.509 auto-assinado, só em memória (D5): com
    /// <c>NotBefore = signs_from</c> e <c>NotAfter = validates_until</c>, o comparador
    /// do OpenIddict nunca prefere a próxima chave e, entre as válidas, prefere a mais
    /// nova. Sem isso, entre duas <c>RsaSecurityKey</c> a ordem fica com o <c>Sort</c>.
    /// </summary>
    private static X509SecurityKey ToSigningKey(IdentityKey key, IdentityKeyMaterialCipher cipher)
    {
        var rsa = RSA.Create();
        var pkcs8 = Decrypt(key, cipher);
        try
        {
            rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        var request = new CertificateRequest($"CN=buteco-emissor-{key.Kid}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(key.SignsFrom, key.ValidatesUntil);
        return new X509SecurityKey(certificate, key.Kid);
    }

    private static SymmetricSecurityKey ToEncryptionKey(IdentityKey key, IdentityKeyMaterialCipher cipher) =>
        new(Decrypt(key, cipher)) { KeyId = key.Kid };

    private static byte[] Decrypt(IdentityKey key, IdentityKeyMaterialCipher cipher)
    {
        try
        {
            return cipher.Unprotect(key.ProtectedMaterial);
        }
        catch (CryptographicException exception)
        {
            // Não substitui a chave (D4): gerar outra em silêncio invalidaria o que já
            // foi assinado e esconderia o erro de configuração.
            throw new InvalidOperationException(
                $"A chave do emissor '{key.Kid}' ({key.Usage}) não decifra com Identity:SigningKeyEncryptionKey. A chave de cifra configurada não é a que gravou as chaves; o emissor fica indisponível até a configuração ser corrigida.",
                exception);
        }
    }
}
