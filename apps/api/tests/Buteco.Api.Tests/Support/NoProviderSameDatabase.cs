using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O <c>apps/api</c> "depois do reinício sem o provedor" (change conectar-falha-minha-conta, #281, design D4): um host
/// sem a seção <c>Identity:FederatedProviders</c>, no MESMO banco de outro host e, por isso, com o mesmo anel de
/// chaves (o anel mora no banco, e a chave que o cifra vem da mesma configuração de teste). É a forma que a sonda
/// da tarefa 0 provou no pod: o state token gravado pelo host com o provedor decifra neste.
///
/// <para>Não sobe contêiner nem o apaga: o dono do banco é o host de origem. O log do emissor é capturado.</para>
/// </summary>
public sealed class NoProviderSameDatabaseHost(ApiFactoryFixture source) : LocalUsersFixture
{
    public IdentityIssuerLogCapture Logs { get; } = new();

    protected override string? SharedDatabaseConnectionString => source.PostgresConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    /// <summary>Sobe o host: a migração (já aplicada pelo de origem) e o anel carregado.</summary>
    public Task StartAsync() => ((IAsyncLifetime)this).InitializeAsync();
}

/// <summary>
/// O host com o provedor (o início e a autorização no <see cref="FakeGoogle"/>) e, sob demanda, o mesmo
/// <c>apps/api</c> reiniciado sem o provedor (<see cref="WithoutProviderAsync"/>), para a volta.
/// </summary>
public class RestartedWithoutProviderFixture : FederatedLoginFixture
{
    private NoProviderSameDatabaseHost? _withoutProvider;
    private readonly SemaphoreSlim _starting = new(1, 1);

    public async Task<NoProviderSameDatabaseHost> WithoutProviderAsync()
    {
        await _starting.WaitAsync();
        try
        {
            if (_withoutProvider is null)
            {
                var host = new NoProviderSameDatabaseHost(this);
                await host.StartAsync();
                _withoutProvider = host;
            }

            return _withoutProvider;
        }
        finally
        {
            _starting.Release();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (_withoutProvider is not null)
        {
            await _withoutProvider.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
