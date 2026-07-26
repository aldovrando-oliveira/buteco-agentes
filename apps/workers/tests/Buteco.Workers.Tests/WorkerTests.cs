using Buteco.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Buteco.Workers.Tests;

public class WorkerTests
{
    [Fact]
    public async Task Host_Starts_And_Stops_Without_Errors()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddHostedService<Worker>())
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }
}
