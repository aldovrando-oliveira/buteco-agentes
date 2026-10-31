using Buteco.Api.Identity.Keys;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Host com <c>Auth:BootstrapAdminEmail</c> configurado (ou vazio) e o aquecimento a cada
/// 200 ms: o host sobe antes da migração, e o bootstrap conclui no ciclo seguinte a ela.
/// O hash é o PBKDF2 do operador de teste (<see cref="TestAuthentication"/>).
/// </summary>
public class UserBootstrapFixture : ApiFactoryFixture
{
    public const string BootstrapEmail = "admin@exemplo.test";

    public IdentityIssuerLogCapture Logs { get; } = new();

    protected virtual string ConfiguredBootstrapEmail => BootstrapEmail;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdminEmail"] = ConfiguredBootstrapEmail,
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityKeyRotationSchedule>();
            services.AddSingleton(new IdentityKeyRotationSchedule { WarmupRetryInterval = TimeSpan.FromMilliseconds(200) });
        });
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    /// <summary>Espera uma condição do aquecimento, com prazo.</summary>
    public static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("A condição do aquecimento não se cumpriu no prazo.");
            }

            await Task.Delay(100);
        }
    }
}

public sealed class UserBootstrapWithoutEmailFixture : UserBootstrapFixture
{
    protected override string ConfiguredBootstrapEmail => "";
}
