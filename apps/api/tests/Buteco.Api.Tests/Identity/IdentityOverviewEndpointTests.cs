using System.Net;
using System.Text.Json;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// <c>GET /identity</c> num banco recém-subido (spec <c>identity-issuer-overview</c>; design.md
/// da change leitura-identidade-emissor, D1, D2, D6 e D7): acesso, endereços iguais aos do
/// discovery, durações iguais às do OpenIddict, uma chave em uso, nenhum material e os nomes
/// do fio. Os três estados ficam no <see cref="IdentityOverviewKeyStatesTests"/>, que grava
/// chaves a mais e por isso tem host próprio.
/// </summary>
public class IdentityOverviewEndpointTests(IdentityOverviewEndpointTests.Fixture factory)
    : IClassFixture<IdentityOverviewEndpointTests.Fixture>
{
    public sealed class Fixture : LocalUsersFixture;

    private const string Issuer = ApiFactoryFixture.PublicBaseUrl + "/";

    private Task<HttpResponseMessage> GetAsync(string? bearer) =>
        TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, "/identity", bearer);

    // Acesso (D7).

    [Fact]
    public async Task ActiveUser_Is200()
    {
        var user = await SignedInUser.CreateAsync(factory, "identidade-ativo");

        Assert.Equal(HttpStatusCode.OK, (await GetAsync(user.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task WithoutToken_Is401()
    {
        await SignedInUser.CreateAsync(factory, "identidade-anel");

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(null)).StatusCode);
    }

    [Theory]
    [InlineData(ServiceScopeAuthorizationHandler.InboxSubject)]
    [InlineData(ServiceScopeAuthorizationHandler.ConnectorsSubject)]
    public async Task ServiceToken_Is403(string subject)
    {
        await SignedInUser.CreateAsync(factory, "identidade-servico");

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(TestIssuerTokens.ForSubject(factory.Services, subject))).StatusCode);
    }

    [Fact]
    public async Task PanelTokenWithoutUser_Is403_FromCurrentUser()
    {
        await SignedInUser.CreateAsync(factory, "identidade-sem-usuario");

        var response = await GetAsync(TestIssuerTokens.Panel(factory.Services));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CurrentUser.ForbiddenMessage, await TwoFactorTestClient.TitleAsync(response));
    }

    [Fact]
    public async Task DeactivatedUser_WithTheTokenFromBefore_Is403()
    {
        var user = await SignedInUser.CreateAsync(factory, "identidade-desativado");
        await LocalUserSeed.DeactivateAsync(factory.Services, user.User.Id);

        var response = await GetAsync(user.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(CurrentUser.ForbiddenMessage, await TwoFactorTestClient.TitleAsync(response));
    }

    // Endereços (D2).

    [Fact]
    public async Task Addresses_AreTheOnesTheDiscoveryAnnounces()
    {
        var emissor = (await BodyAsync()).GetProperty("emissor");
        using var discovery = JsonDocument.Parse(await SignedInUser.Anonymous(factory).GetStringAsync("/.well-known/openid-configuration"));
        var root = discovery.RootElement;

        Assert.Equal(root.GetProperty("issuer").GetString(), emissor.GetProperty("issuer").GetString());
        Assert.Equal(root.GetProperty("issuer").GetString() + ".well-known/openid-configuration", emissor.GetProperty("discovery").GetString());
        Assert.Equal(root.GetProperty("jwks_uri").GetString(), emissor.GetProperty("jwks").GetString());
        Assert.Equal(root.GetProperty("token_endpoint").GetString(), emissor.GetProperty("token").GetString());
        Assert.Equal(Issuer, emissor.GetProperty("issuer").GetString());
    }

    [Fact]
    public async Task AnotherHost_DoesNotChangeTheAddresses()
    {
        var user = await SignedInUser.CreateAsync(factory, "identidade-host");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/identity");
        request.Headers.Authorization = new("Bearer", user.AccessToken);
        request.Headers.Host = "outro-host.interno:8080";

        var response = await SignedInUser.Anonymous(factory).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("outro-host", text);
        var emissor = JsonDocument.Parse(text).RootElement.GetProperty("emissor");
        foreach (var name in new[] { "issuer", "discovery", "jwks", "token" })
        {
            Assert.StartsWith(Issuer, emissor.GetProperty(name).GetString(), StringComparison.Ordinal);
        }
    }

    // Durações (D6, guarda do dono): a resposta contra o que o OpenIddict e a rotação de fato usam.

    [Fact]
    public async Task TokenDurations_AreTheOpenIddictOptionsOfTheHost()
    {
        var emissor = (await BodyAsync()).GetProperty("emissor");
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;

        Assert.Equal((long)options.AccessTokenLifetime!.Value.TotalSeconds, emissor.GetProperty("validadeDoTokenSegundos").GetInt64());
        Assert.Equal((long)options.RefreshTokenLifetime!.Value.TotalSeconds, emissor.GetProperty("sessaoSemAtividadeSegundos").GetInt64());
    }

    [Fact]
    public async Task RotationDurations_AreTheWindowsTheRotationWrote()
    {
        var chaves = (await BodyAsync()).GetProperty("chavesDeAssinatura");
        var inUse = chaves.GetProperty("chaves").EnumerateArray().Single(key => key.GetProperty("estado").GetString() == "em-uso");

        Assert.Equal(
            (long)(inUse.GetProperty("assinaAte").GetDateTimeOffset() - inUse.GetProperty("assinaDesde").GetDateTimeOffset()).TotalSeconds,
            chaves.GetProperty("intervaloDeTrocaSegundos").GetInt64());
        Assert.Equal(
            (long)(inUse.GetProperty("saiDaPublicacaoEm").GetDateTimeOffset() - inUse.GetProperty("assinaAte").GetDateTimeOffset()).TotalSeconds,
            chaves.GetProperty("retencaoSegundos").GetInt64());
    }

    [Fact]
    public async Task CheckInterval_IsTheTimerOfTheRotation()
    {
        var chaves = (await BodyAsync()).GetProperty("chavesDeAssinatura");
        var schedule = factory.Services.GetRequiredService<IdentityKeyRotationSchedule>();

        Assert.Equal((long)schedule.CheckInterval.TotalSeconds, chaves.GetProperty("verificacaoSegundos").GetInt64());
    }

    // Chave (D3, D4).

    [Fact]
    public async Task FreshDatabase_HasOneKeyInUse_WithTheStoredCreation()
    {
        var keys = (await BodyAsync()).GetProperty("chavesDeAssinatura").GetProperty("chaves").EnumerateArray().ToList();
        var stored = await StoredSigningKeysAsync(factory.Services);

        var key = Assert.Single(keys);
        var row = Assert.Single(stored);
        Assert.Equal("em-uso", key.GetProperty("estado").GetString());
        Assert.Equal(row.Kid, key.GetProperty("kid").GetString());
        Assert.Equal(row.CreatedAt, key.GetProperty("criadaEm").GetDateTimeOffset());
    }

    [Fact]
    public async Task ListSigningKeys_DoesNotSelectTheMaterialColumn()
    {
        await SignedInUser.CreateAsync(factory, "identidade-sql");
        var ring = factory.Services.GetRequiredService<IdentityKeyRing>();

        var commands = await factory.SqlCapture.CaptureAsync(() => ring.ListSigningKeysAsync(CancellationToken.None));

        var query = Assert.Single(commands, command => command.Contains("identity_keys", StringComparison.Ordinal));
        Assert.Contains("signs_from", query, StringComparison.Ordinal);
        Assert.DoesNotContain("protected_material", query, StringComparison.Ordinal);
    }

    // Nomes do fio (D2).

    [Fact]
    public async Task WireNames_AreExactlyTheContract()
    {
        var body = await BodyAsync();

        Assert.Equal(["chavesDeAssinatura", "emissor"], Names(body));
        Assert.Equal(
            ["discovery", "issuer", "jwks", "sessaoSemAtividadeSegundos", "token", "validadeDoTokenSegundos"],
            Names(body.GetProperty("emissor")));
        Assert.Equal(
            ["chaves", "intervaloDeTrocaSegundos", "prePublicacaoSegundos", "retencaoSegundos", "verificacaoSegundos"],
            Names(body.GetProperty("chavesDeAssinatura")));
        Assert.All(
            body.GetProperty("chavesDeAssinatura").GetProperty("chaves").EnumerateArray(),
            key => Assert.Equal(KeyPropertyNames, Names(key)));
    }

    internal static readonly string[] KeyPropertyNames = ["assinaAte", "assinaDesde", "criadaEm", "estado", "kid", "saiDaPublicacaoEm"];

    internal static readonly string[] KeyMaterialNames = ["d", "p", "q", "dp", "dq", "qi", "n", "e", "k", "x5c"];

    internal static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    internal static IEnumerable<string> AllNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => AllNames(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(AllNames),
        _ => [],
    };

    internal static async Task<List<IdentityKey>> StoredSigningKeysAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdentityKeys.AsNoTracking()
            .Where(key => key.Usage == IdentityKeyUsage.Signing)
            .ToListAsync();
    }

    private async Task<JsonElement> BodyAsync()
    {
        var user = await SignedInUser.CreateAsync(factory, "identidade-leitura");
        var response = await GetAsync(user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await TwoFactorTestClient.JsonAsync(response);
    }
}

/// <summary>
/// Os três estados e a ausência de material, com chaves gravadas ao lado da que a rotação criou
/// (spec <c>identity-issuer-overview</c>; design.md da change leitura-identidade-emissor, D3, D4 e
/// D8). As chaves a mais saem da <see cref="IdentityKeyFactory"/> do host, com material real
/// cifrado, para o teste do material procurar bytes de verdade. O relógio da fixture não anda,
/// então nenhum tique de rotação recarrega o anel no meio.
/// </summary>
public class IdentityOverviewKeyStatesTests(IdentityOverviewKeyStatesTests.Fixture factory)
    : IClassFixture<IdentityOverviewKeyStatesTests.Fixture>, IAsyncLifetime
{
    public sealed class Fixture : LocalUsersFixture
    {
        public IdentityKey? Next { get; set; }

        public IdentityKey? Retiring { get; set; }

        public IdentityKey? Expired { get; set; }
    }

    private SignedInUser _user = null!;

    public async Task InitializeAsync()
    {
        _user = await SignedInUser.CreateAsync(factory, "identidade-estados");
        if (factory.Next is not null)
        {
            return;
        }

        var keyFactory = factory.Services.GetRequiredService<IdentityKeyFactory>();
        var now = factory.Clock.GetUtcNow();
        var active = Assert.Single(await IdentityOverviewEndpointTests.StoredSigningKeysAsync(factory.Services));

        factory.Next = keyFactory.CreateSigningKey(IdentityKeyRotationPolicy.NextSigningKey(active), now);
        factory.Retiring = keyFactory.CreateSigningKey(new IdentityKeyWindows(now.AddDays(-100), now.AddDays(-10), now.AddDays(5)), now.AddDays(-107));
        factory.Expired = keyFactory.CreateSigningKey(new IdentityKeyWindows(now.AddDays(-200), now.AddDays(-110), now.AddDays(-95)), now.AddDays(-207));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.IdentityKeys.AddRange(factory.Next, factory.Retiring, factory.Expired);
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> BodyTextAsync()
    {
        var response = await TwoFactorTestClient.SendAsync(SignedInUser.Anonymous(factory), HttpMethod.Get, "/identity", _user.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task InUseNextAndRetiring_InTheOrderOfTheBoard_WithTheStoredDates()
    {
        var keys = JsonDocument.Parse(await BodyTextAsync()).RootElement
            .GetProperty("chavesDeAssinatura").GetProperty("chaves").EnumerateArray().ToList();
        var stored = (await IdentityOverviewEndpointTests.StoredSigningKeysAsync(factory.Services)).ToDictionary(key => key.Kid);
        var activeKid = stored.Keys.Single(kid => kid != factory.Next!.Kid && kid != factory.Retiring!.Kid && kid != factory.Expired!.Kid);

        Assert.Equal(
            [(activeKid, "em-uso"), (factory.Next!.Kid, "proxima"), (factory.Retiring!.Kid, "aposentando")],
            keys.Select(key => (key.GetProperty("kid").GetString()!, key.GetProperty("estado").GetString()!)));
        foreach (var key in keys)
        {
            var row = stored[key.GetProperty("kid").GetString()!];
            Assert.Equal(row.CreatedAt, key.GetProperty("criadaEm").GetDateTimeOffset());
            Assert.Equal(row.SignsFrom, key.GetProperty("assinaDesde").GetDateTimeOffset());
            Assert.Equal(row.SignsUntil, key.GetProperty("assinaAte").GetDateTimeOffset());
            Assert.Equal(row.ValidatesUntil, key.GetProperty("saiDaPublicacaoEm").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task EncryptionKeyAndExpiredKey_StayOut()
    {
        var text = await BodyTextAsync();

        using var scope = factory.Services.CreateScope();
        var encryptionKids = await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdentityKeys.AsNoTracking()
            .Where(key => key.Usage == IdentityKeyUsage.Encryption).Select(key => key.Kid).ToListAsync();
        Assert.NotEmpty(encryptionKids);
        Assert.All(encryptionKids, kid => Assert.DoesNotContain(kid, text));
        Assert.DoesNotContain(factory.Expired!.Kid, text);
    }

    [Fact]
    public async Task NoKeyParameterName_AndExactlyTheSixKeyProperties()
    {
        var text = await BodyTextAsync();
        var root = JsonDocument.Parse(text).RootElement;

        var names = IdentityOverviewEndpointTests.AllNames(root).ToHashSet(StringComparer.Ordinal);
        Assert.All(IdentityOverviewEndpointTests.KeyMaterialNames, name => Assert.DoesNotContain(name, names));
        Assert.All(
            root.GetProperty("chavesDeAssinatura").GetProperty("chaves").EnumerateArray(),
            key => Assert.Equal(IdentityOverviewEndpointTests.KeyPropertyNames, IdentityOverviewEndpointTests.Names(key)));
    }

    [Fact]
    public async Task NoEncryptedMaterialOfAnyRow_InTheResponse()
    {
        var text = await BodyTextAsync();

        using var scope = factory.Services.CreateScope();
        var materials = await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdentityKeys.AsNoTracking()
            .Select(key => key.ProtectedMaterial).ToListAsync();
        Assert.True(materials.Count >= 4, $"esperava a ativa, a de cifra e as três gravadas; vieram {materials.Count}");
        foreach (var material in materials)
        {
            Assert.DoesNotContain(Convert.ToBase64String(material), text);
            Assert.DoesNotContain(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(material), text);
            Assert.DoesNotContain(Convert.ToHexString(material), text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
