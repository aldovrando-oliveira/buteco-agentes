using Buteco.Api.Identity.Storage;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Linhas de <c>openiddict_tokens</c> e <c>openiddict_authorizations</c> gravadas direto
/// por SQL, para os testes da limpeza que precisam de volume (design.md da change
/// limpeza-tokens-openiddict, D5 e D6), e uma "instância" do <c>apps/api</c> só com o que
/// o <see cref="IdentityTokenPruner"/> usa. As datas saem do <c>now()</c> do banco, o
/// mesmo relógio do "vencido" do critério.
/// </summary>
public static class IdentityTokenSeed
{
    /// <summary>
    /// <paramref name="dead"/> refresh tokens resgatados há 30 dias, que a limpeza remove,
    /// e <paramref name="live"/> válidos de agora, que ela preserva. Sem autorização.
    /// </summary>
    public static async Task DeadAndLiveAsync(string connectionString, int dead, int live)
    {
        await ExecuteAsync(connectionString, $"""
            INSERT INTO openiddict_tokens ("Id", "CreationDate", "ExpirationDate", "Status", "Subject", "Type")
            SELECT 'morto-' || g, now() - interval '30 days', now() - interval '16 days', 'redeemed', 'usuario-' || (g % 50), 'refresh_token'
            FROM generate_series(1, {dead}) AS g;

            INSERT INTO openiddict_tokens ("Id", "CreationDate", "ExpirationDate", "Status", "Subject", "Type")
            SELECT 'vivo-' || g, now(), now() + interval '14 days', 'valid', 'usuario-' || (g % 50), 'refresh_token'
            FROM generate_series(1, {live}) AS g;
            """);
    }

    public static async Task<long> TokenCountAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM openiddict_tokens", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task MigrateAsync(string connectionString)
    {
        await using var dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(connectionString).Options);
        await dbContext.Database.MigrateAsync();
    }

    /// <summary>
    /// O pruner de uma instância: provedor de serviços próprio, banco comum, e nada em
    /// comum com as outras além do lock do Postgres.
    /// </summary>
    public static IdentityTokenPruner Instance(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(connectionString));
        services.AddOpenIddict().AddCore(core => core.UseEntityFrameworkCore().UseDbContext<AppDbContext>());
        var provider = services.BuildServiceProvider();

        return new IdentityTokenPruner(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
    }
}
