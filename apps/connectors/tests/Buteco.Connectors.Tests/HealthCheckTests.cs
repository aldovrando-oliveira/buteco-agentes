using System.Net;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

public class HealthCheckTests
{
    [Fact]
    public async Task Health_SemToken_Responde200()
    {
        await using var factory = new ConnectorsFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
