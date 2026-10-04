using System.Net;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Tests;

/// <summary>
/// O token que o <c>apps/connectors</c> envia ao <c>apps/api</c> (design.md da change
/// ciclo-de-sincronizacao, D1): novo a cada requisição, <c>sub</c> igual a
/// <c>service:connectors</c>, validade fixa de 5 minutos.
/// </summary>
public class ServiceTokenDelegatingHandlerTests
{
    [Fact]
    public async Task EveryRequest_CarriesAFreshServiceConnectorsToken()
    {
        var tokenService = new TokenService(Microsoft.Extensions.Options.Options.Create(
            new TokenSigningOptions { TokenSigningKey = TestAuthentication.TokenSigningKey }));
        var spy = new SpyTokenService(tokenService);
        var inner = new CapturingHandler();
        using var client = new HttpClient(new ServiceTokenDelegatingHandler(spy) { InnerHandler = inner })
        {
            BaseAddress = new Uri("http://api.test"),
        };

        await client.GetAsync("/sync/knowledge-bases");
        await client.GetAsync("/sync/knowledge-bases");

        Assert.Equal(2, inner.Authorizations.Count);
        Assert.All(inner.Authorizations, header =>
        {
            Assert.Equal("Bearer", header!.Scheme);
            var validation = tokenService.Validate(header.Parameter!);
            Assert.True(validation.IsValid);
            Assert.Equal("service:connectors", validation.Subject);
        });
        Assert.Equal([("service:connectors", TimeSpan.FromMinutes(5)), ("service:connectors", TimeSpan.FromMinutes(5))], spy.Issued);
    }

    private sealed class SpyTokenService(ITokenService inner) : ITokenService
    {
        public List<(string Subject, TimeSpan Lifetime)> Issued { get; } = [];

        public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, TimeSpan lifetime)
        {
            Issued.Add((subject, lifetime));
            return inner.Issue(subject, lifetime);
        }

        public TokenValidationResult Validate(string token) => inner.Validate(token);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<System.Net.Http.Headers.AuthenticationHeaderValue?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
