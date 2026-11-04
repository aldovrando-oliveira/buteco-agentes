using System.Net;
using Buteco.Connectors.Sync;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Buteco.Connectors.Tests;

/// <summary>
/// A fábrica com o emissor de teste ligado pelo pedido de token (design.md da change
/// fixtures-token-emissor, D3; spec <c>connectors-scaffold</c>, "Fábrica padrão sem
/// agendamento do ciclo"). O emissor exige o <c>Api:BaseUrl</c>, que liga o agendador do
/// ciclo; sem o teste pedir o endereço, o agendador sai do DI, e nenhum pedido chega aos
/// duplos do <c>apps/api</c> depois do boot.
/// </summary>
public class ConnectorsFactoryIssuerTests
{
    [Fact]
    public async Task IssuerOn_WithoutApiBaseUrlRequested_NothingReachesTheApiFakesAfterBoot()
    {
        await using var factory = new ConnectorsFactory();
        var client = TestAuthentication.CreateIssuerClientAs(factory, "operator");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/connectors/providers")).StatusCode);

        // A rodada do agendador sai logo depois do boot: um pedido de token chegaria aqui.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (factory.TokenEndpoint.Requests == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.Equal(0, factory.TokenEndpoint.Requests);
        Assert.Empty(factory.SyncApi.Requests);
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), service => service is SyncSchedulerService);
    }

    [Fact]
    public async Task IssuerOn_WithApiBaseUrlRequested_KeepsTheScheduler()
    {
        await using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?> { ["Api:BaseUrl"] = "http://api.test" });
        _ = TestAuthentication.CreateIssuerClientAs(factory, "operator");

        Assert.Contains(factory.Services.GetServices<IHostedService>(), service => service is SyncSchedulerService);
    }
}
