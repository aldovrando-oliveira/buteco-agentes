using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A migration de dados <c>ExtendIdentityKeyRetention</c> (design.md da change
/// usuarios-locais-login-emissor, D6): estende para <c>signs_until + 15 dias</c> a janela
/// das chaves de assinatura gravadas com a retenção de 24 h, e não toca a chave de cifra nem
/// a de assinatura cuja janela já é igual ou maior.
///
/// <para>
/// Verificado contra o defeito (convenção 15): sem o <c>UPDATE</c> da migration, o caso
/// reprova na chave de 24 h (registro no tasks.md, 6.9).
/// </para>
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class ExtendIdentityKeyRetentionMigrationTests(MigrationPostgresFixture postgres)
{
    private const string PreviousMigrationId = "20261031144517_AddLocalUsers";

    [Fact]
    public async Task ExtendsOnlyTheSigningKeysBelowFifteenDays()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(await postgres.CreateDatabaseAsync())
            .Options;

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigrationId, CancellationToken.None);
        }

        var signsUntil = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var encryptionCreated = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO identity_keys (kid, usage, algorithm, protected_material, created_at, signs_from, signs_until, validates_until) VALUES
                ('assina-24h', 'Signing', 'RS256', '\x00', {encryptionCreated}, {signsUntil.AddDays(-90)}, {signsUntil}, {signsUntil.AddHours(24)}),
                ('assina-15d', 'Signing', 'RS256', '\x00', {encryptionCreated}, {signsUntil.AddDays(-90)}, {signsUntil}, {signsUntil.AddDays(15)}),
                ('assina-20d', 'Signing', 'RS256', '\x00', {encryptionCreated}, {signsUntil.AddDays(-90)}, {signsUntil}, {signsUntil.AddDays(20)}),
                ('cifra', 'Encryption', 'A256KW', '\x00', {encryptionCreated}, {encryptionCreated}, 'infinity', 'infinity')
                """);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(null!, CancellationToken.None);
        }

        await using (var dbContext = new AppDbContext(options))
        {
            var windows = await dbContext.IdentityKeys.AsNoTracking().ToDictionaryAsync(key => key.Kid, key => key.ValidatesUntil);

            Assert.Equal(signsUntil.AddDays(15), windows["assina-24h"]);
            Assert.Equal(signsUntil.AddDays(15), windows["assina-15d"]);
            Assert.Equal(signsUntil.AddDays(20), windows["assina-20d"]);
            Assert.Equal(DateTimeOffset.MaxValue, windows["cifra"]);
        }
    }
}
