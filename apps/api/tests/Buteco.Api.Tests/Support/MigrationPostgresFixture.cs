using Npgsql;
using Testcontainers.PostgreSql;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Um contêiner Postgres para as classes de teste de migração, compartilhado
/// pela <see cref="MigrationPostgresCollection"/> (design.md da change
/// historico-documentos-base, D12). Cada teste cria o próprio banco com
/// <see cref="CreateDatabaseAsync"/>, e assim estados de migração diferentes —
/// um banco parado na migração anterior, outro na última — convivem sem ordem
/// entre testes.
///
/// <para>
/// É fixture de <b>collection</b>, e não <c>IAsyncLifetime</c> na classe de teste,
/// por um motivo medido: o xUnit cria uma instância da classe por teste, então o
/// <c>IAsyncLifetime</c> da classe sobe um contêiner por <b>teste</b> — sete para
/// os sete casos de <c>RejectionMetricsMigrationTests</c> (#110). A fixture de
/// collection é criada uma vez para todas as classes dela.
/// </para>
///
/// <para>
/// A imagem vai como literal: não há constante para ela nas fixtures de
/// <c>apps/api</c>, e criá-la é escopo da #110.
/// </para>
/// </summary>
public sealed class MigrationPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg18")
        .WithDatabase("buteco_agents_migrations_test")
        .WithUsername("buteco")
        .WithPassword("buteco_test_password")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    /// <summary>
    /// Cria um banco vazio de nome único e devolve a connection string dele. Nada
    /// é migrado aqui: cada teste decide até onde migrar.
    /// </summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = $"migration_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\";", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = databaseName }.ConnectionString;
    }
}
