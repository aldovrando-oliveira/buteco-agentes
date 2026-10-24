using System.Net;
using System.Net.Http.Json;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Sem url pública, o emissor fica desligado e o app sobe igual (spec <c>oidc-issuer</c>;
/// design.md da change emissor-oidc-api, D8).
/// </summary>
public class IdentityIssuerDisabledTests(IdentityIssuerDisabledTests.WithoutPublicUrlWithLogs factory)
    : IClassFixture<IdentityIssuerDisabledTests.WithoutPublicUrlWithLogs>
{
    public sealed class WithoutPublicUrlWithLogs : ApiFactoryFixture
    {
        public IdentityIssuerLogCapture Logs { get; } = new();

        protected override string? ConfiguredPublicBaseUrl => string.Empty;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        }
    }

    [Fact]
    public async Task Discovery_IsNotServed()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Boot_WarnsThatTheIssuerIsOff()
    {
        _ = factory.Services;

        Assert.Contains(factory.Logs.Lines, line =>
            line.Level == LogLevel.Warning &&
            line.Message.StartsWith("Emissor OIDC desligado: PublicUrl:BaseUrl não configurada.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Login_StillWorks()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            username = TestAuthentication.KnownOperatorUsername,
            password = TestAuthentication.KnownOperatorPassword,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
