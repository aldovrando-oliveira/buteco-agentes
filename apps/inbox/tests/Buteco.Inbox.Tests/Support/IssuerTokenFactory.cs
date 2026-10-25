using System.Net.Http.Headers;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// O <c>apps/inbox</c> real com a validação do token do emissor ligada contra um
/// <see cref="TestIssuer"/> (change validacao-jwks-inbox-connectors, D9): <c>Identity:Issuer</c>
/// e <c>Api:BaseUrl</c> do emissor de teste, o cliente <c>IssuerKeys</c> com o handler
/// dele, e um <see cref="IssuerKeysSchedule"/> escolhido pelo teste. Uma instância por
/// teste, porque o estado das chaves é do processo; o Postgres é o da classe.
/// <para>
/// O <see cref="ITokenService"/> é embrulhado num contador, para os testes afirmarem que
/// um JWT nunca passa pelo HMAC.
/// </para>
/// </summary>
public sealed class IssuerTokenFactory(
    PostgresOnlyFixture postgres,
    TestIssuer issuer,
    IssuerKeysSchedule? schedule = null,
    Dictionary<string, string?>? configuration = null) : WebApplicationFactory<Program>
{
    public List<CapturedLogEntry> Logs { get; } = [];

    public CountingTokenService TokenService => (CountingTokenService)Services.GetRequiredService<ITokenService>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inbox:CredentialEncryptionKey"] = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=",
                ["Api:BaseUrl"] = TestIssuer.InternalBaseUrl,
            });
            config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
            if (configuration is not null)
            {
                config.AddInMemoryCollection(configuration);
            }
        });

        builder.UseIssuer(configuration?.GetValueOrDefault("Identity:Issuer") ?? TestIssuer.Issuer);
        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(Logs, "Buteco.Inbox.Auth")));

        builder.ConfigureServices(services =>
        {
            var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbContextDescriptor is not null)
            {
                services.Remove(dbContextDescriptor);
            }

            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(postgres.Postgres.GetConnectionString()));

            services.Replace(ServiceDescriptor.Singleton(schedule ?? new IssuerKeysSchedule()));
            services.AddHttpClient(IssuerKeysRetriever.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(issuer.CreateHandler);

            services.Replace(ServiceDescriptor.Singleton<ITokenService>(provider =>
                new CountingTokenService(new TokenService(provider.GetRequiredService<IOptions<TokenSigningOptions>>()))));
        });
    }

    public HttpClient CreateClientWithToken(string? token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public string OperatorHmacToken() => Services.GetRequiredService<ITokenService>().Issue("operator", TimeSpan.FromMinutes(30)).Token;

    /// <summary>Espera o serviço em segundo plano obter as chaves.</summary>
    public async Task WaitForKeysAsync()
    {
        _ = Server;
        var state = Services.GetRequiredService<IssuerKeysState>();
        await Poll.UntilAsync(() => state.Obtained, "as chaves do emissor não foram obtidas");
    }
}

public sealed class CountingTokenService(ITokenService inner) : ITokenService
{
    private int _validateCalls;

    public int ValidateCalls => Volatile.Read(ref _validateCalls);

    public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, TimeSpan lifetime) => inner.Issue(subject, lifetime);

    public TokenValidationResult Validate(string token)
    {
        Interlocked.Increment(ref _validateCalls);
        return inner.Validate(token);
    }
}

public static class Poll
{
    public static async Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(25);
        }
    }

    public static async Task UntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(25);
        }
    }
}
