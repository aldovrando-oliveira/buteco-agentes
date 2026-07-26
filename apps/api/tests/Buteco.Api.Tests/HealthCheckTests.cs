using System.Net;
using Buteco.Api.Tests.Support;

namespace Buteco.Api.Tests;

public class HealthCheckTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    [Fact]
    public async Task Health_Endpoint_Returns_Ok()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
