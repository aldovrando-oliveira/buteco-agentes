using Buteco.Api.Identity;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Duas instâncias verificando a rotação ao mesmo tempo contra um banco sem chave criam
/// uma chave de cada uso (spec <c>identity-signing-keys</c>; design.md da change
/// emissor-oidc-api, D7). Cada anel aqui é o de uma instância: processo próprio, banco
/// comum, e nada em comum além do lock do Postgres.
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class IdentityKeyRingConcurrencyTests(MigrationPostgresFixture postgres)
{
    [Fact]
    public async Task TwoConcurrentChecks_AgainstAnEmptyTable_CreateOneKeyOfEachUsage()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using (var dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options))
        {
            await dbContext.Database.MigrateAsync();
        }

        var rings = Enumerable.Range(0, 4).Select(_ => Instance(connectionString)).ToList();

        await Task.WhenAll(rings.Select(ring => ring.RefreshAsync(CancellationToken.None)));

        await using var check = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options);
        var keys = await check.IdentityKeys.AsNoTracking().ToListAsync();
        Assert.Single(keys, key => key.Usage == IdentityKeyUsage.Signing);
        Assert.Single(keys, key => key.Usage == IdentityKeyUsage.Encryption);
        Assert.Single(rings.Select(ring => ring.Snapshot!.SigningKid).Distinct());
    }

    private static IdentityKeyRing Instance(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(connectionString));
        var provider = services.BuildServiceProvider();

        var cipher = new IdentityKeyMaterialCipher(Microsoft.Extensions.Options.Options.Create(
            new IdentityIssuerOptions { SigningKeyEncryptionKey = TestAuthentication.IdentitySigningKeyEncryptionKey }));

        return new IdentityKeyRing(
            provider.GetRequiredService<IServiceScopeFactory>(),
            cipher,
            new IdentityKeyFactory(cipher),
            TimeProvider.System,
            NullLogger<IdentityKeyRing>.Instance);
    }
}
