using System.Security.Cryptography;
using Buteco.Api.Identity;
using Buteco.Api.Identity.Keys;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// O guarda do D5 (design.md da change emissor-oidc-api): com a anterior ainda em
/// retenção, a ativa e a próxima já publicada, quem assina é a ativa, escolhida pelo
/// comparador do próprio OpenIddict 7.7.2 sobre o embrulho X.509 do anel.
///
/// <para>
/// As três entram em ordem <b>invertida</b>: o snapshot as ordena por <c>kid</c>, e os
/// <c>kid</c>s aqui põem a anterior primeiro e a ativa por último. Entre
/// <c>RsaSecurityKey</c> puras o comparador devolve <c>0</c> (F3), e quem assinaria seria
/// a primeira, a anterior. Verificado contra esse defeito (convenção 15): trocar o
/// embrulho por <c>RsaSecurityKey</c> faz este teste reprovar (registro no tasks.md, 3.6).
/// </para>
/// </summary>
public class IdentityKeyRingSigningOrderTests
{
    private const string PreviousKid = "a-anterior";
    private const string NextKid = "b-proxima";
    private const string ActiveKid = "c-ativa";

    private static readonly IdentityKeyMaterialCipher Cipher = new(Microsoft.Extensions.Options.Options.Create(
        new IdentityIssuerOptions { SigningKeyEncryptionKey = "dGVzdC1pZGVudGl0eS1rZXktMzItYnl0ZXMtLS0tLS0=" }));

    [Fact]
    public void TwoValidKeysAndAFutureOne_InInvertedOrder_TheNewestValidSigns()
    {
        var now = DateTimeOffset.UtcNow;
        var keys = new[]
        {
            // Parou de assinar há 1 h, valida por mais 23 h.
            SigningKey(PreviousKid, signsFrom: now.AddDays(-90).AddHours(-1), signsUntil: now.AddHours(-1)),
            // Publicada, começa a assinar em 5 dias.
            SigningKey(NextKid, signsFrom: now.AddDays(5), signsUntil: now.AddDays(95)),
            // Assina desde a saída da anterior.
            SigningKey(ActiveKid, signsFrom: now.AddHours(-1), signsUntil: now.AddDays(5)),
            EncryptionKey(now),
        };

        var snapshot = IdentityKeyRingSnapshot.Build(keys, now, Cipher);
        Assert.Equal([PreviousKid, NextKid, ActiveKid], snapshot.SigningKeys.Select(key => key.KeyId));

        var options = BuildOpenIddictOptions(snapshot);

        Assert.Equal(ActiveKid, options.SigningCredentials[0].Key.KeyId);
        Assert.Equal(ActiveKid, snapshot.SigningKid);
        Assert.Equal(
            new[] { PreviousKid, NextKid, ActiveKid }.Order(),
            options.SigningCredentials.Select(credentials => credentials.Key.KeyId).Order());
    }

    private static OpenIddictServerOptions BuildOpenIddictOptions(IdentityKeyRingSnapshot snapshot)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOpenIddict().AddServer(server =>
        {
            server.SetIssuer(new Uri("https://emissor.test/"));
            server.SetTokenEndpointUris("connect/token");
            server.AllowClientCredentialsFlow();

            // Sem banco: o modo degradado exige um validador próprio do pedido de token,
            // que aqui nunca roda (só as opções são montadas).
            server.EnableDegradedMode();
            server.AddEventHandler<OpenIddictServerEvents.ValidateTokenRequestContext>(handler =>
                handler.UseInlineHandler(_ => default));
            server.Configure(options => IdentityKeyRingOptionsConfigurer.Apply(options, snapshot));
        });

        // O PostConfigure do OpenIddict ordena as credenciais aqui; a primeira é a que assina (F3).
        return services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
    }

    private static IdentityKey SigningKey(string kid, DateTimeOffset signsFrom, DateTimeOffset signsUntil)
    {
        using var rsa = RSA.Create(2048);
        return new IdentityKey
        {
            Kid = kid,
            Usage = IdentityKeyUsage.Signing,
            Algorithm = "RS256",
            ProtectedMaterial = Cipher.Protect(rsa.ExportPkcs8PrivateKey()),
            CreatedAt = signsFrom,
            SignsFrom = signsFrom,
            SignsUntil = signsUntil,
            ValidatesUntil = signsUntil + IdentityKeyRotationPolicy.Retention,
        };
    }

    private static IdentityKey EncryptionKey(DateTimeOffset now) => new()
    {
        Kid = "z-cifra",
        Usage = IdentityKeyUsage.Encryption,
        Algorithm = "A256KW",
        ProtectedMaterial = Cipher.Protect(RandomNumberGenerator.GetBytes(32)),
        CreatedAt = now,
        SignsFrom = now,
        SignsUntil = DateTimeOffset.MaxValue,
        ValidatesUntil = DateTimeOffset.MaxValue,
    };
}
