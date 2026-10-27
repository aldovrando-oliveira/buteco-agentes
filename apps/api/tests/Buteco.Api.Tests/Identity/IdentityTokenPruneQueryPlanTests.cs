using System.Data.Common;
using Buteco.Api.Identity.Storage;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Xunit.Abstractions;

namespace Buteco.Api.Tests.Identity;

/// <summary>Pulado sem <c>BUTECO_MEDICAO=1</c>: medição, fora da suíte padrão.</summary>
public sealed class MeasurementFactAttribute : FactAttribute
{
    public MeasurementFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BUTECO_MEDICAO") != "1")
        {
            Skip = "Medição: defina BUTECO_MEDICAO=1.";
        }
    }
}

/// <summary>
/// O plano e o tempo das quatro consultas que dependem dos índices de
/// <c>openiddict_tokens</c> e <c>openiddict_authorizations</c>, com 150.000 tokens (design.md
/// da change limpeza-tokens-openiddict, D6): um lote do <c>PruneAsync</c> de tokens, o de
/// autorizações, a revogação por autorização e a revogação por subject.
///
/// <para>
/// O SQL medido é o que os managers do OpenIddict emitem de verdade: um interceptor troca
/// cada comando por <c>EXPLAIN (ANALYZE, BUFFERS)</c> do mesmo texto, com os mesmos
/// parâmetros, numa transação desfeita, e devolve 0 linhas afetadas (o que encerra o laço
/// de lotes do <c>PruneAsync</c> no primeiro). Acima de 100 ms em qualquer uma, a decisão
/// de índice volta ao dono (tarefa 3.6).
/// </para>
/// </summary>
[Trait("Category", "Medicao")]
[Collection(MigrationPostgresCollection.Name)]
public class IdentityTokenPruneQueryPlanTests(MigrationPostgresFixture postgres, ITestOutputHelper output)
{
    private const int Tokens = 150_000;
    private const int Authorizations = 1_500;

    [MeasurementFact]
    public async Task FourQueries_With150kTokens_StayUnder100Ms()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await IdentityTokenSeed.MigrateAsync(connectionString);

        // Autorizações ad hoc de 50 usuários, um terço revogadas; tokens de 0 a 30 dias,
        // access, refresh e id intercalados, um terço de cada status. Metade passa dos 14
        // dias. ANALYZE no fim, como o autovacuum deixaria em produção.
        await IdentityTokenSeed.ExecuteAsync(connectionString, $"""
            INSERT INTO openiddict_authorizations ("Id", "CreationDate", "Status", "Subject", "Type")
            SELECT 'auth-' || g, now() - (g % 30) * interval '1 day',
                   CASE WHEN g % 3 = 0 THEN 'revoked' ELSE 'valid' END, 'usuario-' || (g % 50), 'ad-hoc'
            FROM generate_series(1, {Authorizations}) AS g;

            INSERT INTO openiddict_tokens ("Id", "AuthorizationId", "CreationDate", "ExpirationDate", "Status", "Subject", "Type")
            SELECT 'tkn-' || g, 'auth-' || (g % {Authorizations} + 1),
                   now() - (g % 720) * interval '1 hour',
                   now() - (g % 720) * interval '1 hour' + CASE WHEN g % 3 = 1 THEN interval '14 days' ELSE interval '15 minutes' END,
                   CASE g % 3 WHEN 0 THEN 'valid' WHEN 1 THEN 'redeemed' ELSE 'revoked' END,
                   'usuario-' || ((g % {Authorizations} + 1) % 50),
                   CASE g % 3 WHEN 0 THEN 'access_token' WHEN 1 THEN 'refresh_token' ELSE 'id_token' END
            FROM generate_series(1, {Tokens}) AS g;

            ANALYZE openiddict_tokens;
            ANALYZE openiddict_authorizations;
            """);

        var explain = new ExplainInterceptor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(connectionString).AddInterceptors(explain));
        services.AddOpenIddict().AddCore(core => core.UseEntityFrameworkCore().UseDbContext<AppDbContext>());
        await using var provider = services.BuildServiceProvider();

        var threshold = DateTimeOffset.UtcNow - IdentityTokenPrunePolicy.MinimumAge;
        var times = new List<(string Name, double Milliseconds)>();
        await Measure("PruneAsync de tokens (um lote)", provider, (sp, ct) => sp.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold, ct));
        await Measure("PruneAsync de autorizações (um lote)", provider, (sp, ct) => sp.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold, ct));
        await Measure("revogação por autorização", provider, (sp, ct) => sp.GetRequiredService<IOpenIddictTokenManager>().RevokeByAuthorizationIdAsync("auth-7", ct));
        await Measure("revogação por subject", provider, (sp, ct) => sp.GetRequiredService<IOpenIddictTokenManager>().RevokeBySubjectAsync("usuario-7", ct));

        foreach (var (name, milliseconds) in times)
        {
            output.WriteLine($"{name}: {milliseconds} ms");
        }

        Assert.All(times, time => Assert.True(time.Milliseconds < 100, $"{time.Name}: {time.Milliseconds} ms, acima de 100 ms"));

        async Task Measure(string name, IServiceProvider root, Func<IServiceProvider, CancellationToken, ValueTask<long>> call)
        {
            explain.Plans.Clear();
            await using (var scope = root.CreateAsyncScope())
            {
                await call(scope.ServiceProvider, CancellationToken.None);
            }

            var (sql, plan) = Assert.Single(explain.Plans);
            output.WriteLine($"== {name}");
            output.WriteLine(sql);
            output.WriteLine(string.Join('\n', plan));

            var execution = plan.Single(line => line.StartsWith("Execution Time:", StringComparison.Ordinal));
            var milliseconds = double.Parse(
                execution["Execution Time:".Length..].Replace("ms", "").Trim(),
                System.Globalization.CultureInfo.InvariantCulture);
            times.Add((name, milliseconds));
        }
    }

    private sealed class ExplainInterceptor : DbCommandInterceptor
    {
        public List<(string Sql, List<string> Plan)> Plans { get; } = [];

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var original = command.CommandText;
            await RunAsync(command.Connection!, "BEGIN", cancellationToken);

            var plan = new List<string>();
            command.CommandText = "EXPLAIN (ANALYZE, BUFFERS) " + original;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    plan.Add(reader.GetString(0));
                }
            }

            command.CommandText = original;
            await RunAsync(command.Connection!, "ROLLBACK", cancellationToken);

            Plans.Add((original, plan));
            return InterceptionResult<int>.SuppressWithResult(0);
        }

        private static async Task RunAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
