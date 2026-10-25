using System.Net;
using System.Security.Cryptography;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Persistência das chaves (spec <c>identity-signing-keys</c>; design.md da change
/// emissor-oidc-api, D4 e D6). "Reiniciar" aqui é subir um segundo host contra o mesmo
/// banco, com um anel novo em memória.
/// </summary>
public class IdentityKeyPersistenceTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    [Fact]
    public async Task FirstRequest_LeavesExactlyOneKeyOfEachUsage_AndTheJwksPublishesTheSigningOne()
    {
        var jwks = new JsonWebKeySet(await factory.CreateClient().GetStringAsync("/.well-known/jwks"));

        var keys = await KeysAsync();
        var signing = Assert.Single(keys, key => key.Usage == IdentityKeyUsage.Signing);
        Assert.Single(keys, key => key.Usage == IdentityKeyUsage.Encryption);
        Assert.Equal([signing.Kid], jwks.Keys.Select(key => key.Kid));
    }

    [Fact]
    public async Task Restart_AgainstTheSameDatabase_KeepsTheKid_AndCreatesNoRow()
    {
        var firstKid = Assert.Single(new JsonWebKeySet(await factory.CreateClient().GetStringAsync("/.well-known/jwks")).Keys).Kid;
        var rowsBefore = (await KeysAsync()).Select(key => key.Kid).Order().ToList();

        await using var restarted = factory.WithWebHostBuilder(_ => { });
        var secondKid = Assert.Single(new JsonWebKeySet(await restarted.CreateClient().GetStringAsync("/.well-known/jwks")).Keys).Kid;

        Assert.Equal(firstKid, secondKid);
        Assert.Equal(rowsBefore, (await KeysAsync()).Select(key => key.Kid).Order().ToList());
    }

    [Fact]
    public async Task StoredMaterial_DecryptsToTheKeyPublishedInTheJwks()
    {
        var published = Assert.Single(new JsonWebKeySet(await factory.CreateClient().GetStringAsync("/.well-known/jwks")).Keys);
        var row = (await KeysAsync()).Single(key => key.Kid == published.Kid);

        var cipher = factory.Services.GetRequiredService<IdentityKeyMaterialCipher>();
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(cipher.Unprotect(row.ProtectedMaterial), out _);

        Assert.Equal(published.N, Base64UrlEncoder.Encode(rsa.ExportParameters(false).Modulus));
    }

    [Fact]
    public async Task AnotherEncryptionKey_LeavesTheIssuerAt503_AndTheRowsUntouched()
    {
        _ = await factory.CreateClient().GetAsync("/health");
        var rowsBefore = (await KeysAsync()).Select(key => (key.Kid, key.ProtectedMaterial.Length)).Order().ToList();

        await using var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:SigningKeyEncryptionKey"] = "b3V0cmEtY2hhdmUtZGUtMzItYnl0ZXMtLS0tLS0tLS0=",
            })));

        var response = await misconfigured.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("5", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal(rowsBefore, (await KeysAsync()).Select(key => (key.Kid, key.ProtectedMaterial.Length)).Order().ToList());
    }

    private async Task<List<IdentityKey>> KeysAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdentityKeys.AsNoTracking().ToListAsync();
    }
}
