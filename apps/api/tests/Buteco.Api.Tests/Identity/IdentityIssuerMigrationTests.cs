using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A forma das cinco tabelas do emissor OIDC (design.md da change emissor-oidc-api, D3),
/// no molde de <see cref="RejectionMetricsMigrationTests"/>. Como lá, não há espelho em
/// <c>apps/workers</c>: ninguém lá lê tabela de identidade.
/// </summary>
[Collection(MigrationPostgresCollection.Name)]
public class IdentityIssuerMigrationTests(MigrationPostgresFixture postgres) : IAsyncLifetime
{
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseButecoAgentsNpgsql(_connectionString)
            .Options;

        await using var dbContext = new AppDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    [Theory]
    [InlineData("identity_keys")]
    [InlineData("openiddict_applications")]
    [InlineData("openiddict_authorizations")]
    [InlineData("openiddict_scopes")]
    [InlineData("openiddict_tokens")]
    public async Task Migration_CreatesTheTable_WithTheSnakeCaseName(string table)
    {
        var tables = await ScalarAsync<long>(
            "select count(*) from information_schema.tables where table_schema = 'public' " +
            $"and table_name = '{table}';");

        Assert.Equal(1, tables);
    }

    [Theory]
    [InlineData("kid", "text", "NO")]
    [InlineData("usage", "text", "NO")]
    [InlineData("algorithm", "text", "NO")]
    [InlineData("protected_material", "bytea", "NO")]
    [InlineData("created_at", "timestamp with time zone", "NO")]
    [InlineData("signs_from", "timestamp with time zone", "NO")]
    [InlineData("signs_until", "timestamp with time zone", "NO")]
    [InlineData("validates_until", "timestamp with time zone", "NO")]
    public async Task IdentityKeys_Columns_HaveTheExpectedTypeAndNullability(string column, string expectedType, string expectedNullable)
    {
        var actual = await ScalarAsync<string>(
            "select data_type || '|' || is_nullable from information_schema.columns " +
            $"where table_name = 'identity_keys' and column_name = '{column}';");

        Assert.Equal($"{expectedType}|{expectedNullable}", actual);
    }

    [Fact]
    public async Task IdentityKeys_HasTheLoadIndex_OnUsageAndValidatesUntil()
    {
        var definition = await ScalarAsync<string>(
            "select indexdef from pg_indexes where tablename = 'identity_keys' " +
            "and indexname = 'IX_identity_keys_usage_validates_until';");

        Assert.Contains("(usage, validates_until)", definition);
    }

    /// <summary>
    /// O guarda do "sem seed" (D2): nenhuma migration cadastra cliente, escopo ou chave.
    /// O primeiro cliente real é da #186.
    /// </summary>
    [Theory]
    [InlineData("openiddict_applications")]
    [InlineData("openiddict_scopes")]
    [InlineData("identity_keys")]
    public async Task FreshlyMigratedDatabase_HasNoRows(string table)
    {
        Assert.Equal(0, await ScalarAsync<long>($"select count(*) from {table};"));
    }
}
