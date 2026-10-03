using System.Net;
using Buteco.Api.Auth;
using Buteco.Api.Tests.Support;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Tests;

/// <summary>
/// O token que o <c>apps/api</c> assina para chamar o <c>apps/connectors</c>
/// (design.md da change criacao-base-sincronizada, D4): <c>sub</c> igual a
/// <c>service:api</c>, 5 minutos de validade, um token novo por requisição. Sem
/// contêiner.
/// </summary>
public class ServiceTokenDelegatingHandlerTests
{
    private static readonly TokenService Tokens = new(Microsoft.Extensions.Options.Options.Create(new TokenSigningOptions
    {
        TokenSigningKey = TestAuthentication.TokenSigningKey,
    }));

    [Fact]
    public async Task Send_AttachesBearerTokenWithServiceApiSubjectAndFiveMinuteLifetime()
    {
        var inner = new CapturingHandler();
        using var invoker = new HttpMessageInvoker(new ServiceTokenDelegatingHandler(Tokens) { InnerHandler = inner });

        var before = DateTimeOffset.UtcNow;
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/x"), CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        var authorization = Assert.Single(inner.Authorizations);
        Assert.NotNull(authorization);
        Assert.Equal("Bearer", authorization!.Scheme);

        var validation = Tokens.Validate(authorization.Parameter!);
        Assert.True(validation.IsValid);
        Assert.Equal("service:api", validation.Subject);
        Assert.Equal("service:api", ServiceTokenDelegatingHandler.ApiSubject);

        // O TokenService usa DateTimeOffset.UtcNow e grava exp em segundos: a
        // expiração cai na janela [antes + 5 min, depois + 5 min], com o
        // truncamento para segundo.
        var exp = ReadExp(authorization.Parameter!);
        Assert.InRange(exp, before.AddMinutes(5).ToUnixTimeSeconds() - 1, after.AddMinutes(5).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task Send_TwoRequests_EachGetsItsOwnIssuedToken()
    {
        var inner = new CapturingHandler();
        var issuer = new CountingTokenService(Tokens);
        using var invoker = new HttpMessageInvoker(new ServiceTokenDelegatingHandler(issuer) { InnerHandler = inner });

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/a"), CancellationToken.None);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://connectors.test/b"), CancellationToken.None);

        Assert.Equal(2, issuer.Issued);
        Assert.Equal(2, inner.Authorizations.Count);
        Assert.All(inner.Authorizations, header => Assert.Equal("service:api", Tokens.Validate(header!.Parameter!).Subject));
    }

    private static long ReadExp(string token)
    {
        var payload = token.Split('.')[0].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var json = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
        return json.RootElement.GetProperty("Exp").GetInt64();
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

    private sealed class CountingTokenService(ITokenService inner) : ITokenService
    {
        public int Issued { get; private set; }

        public (string Token, DateTimeOffset ExpiresAt) Issue(string subject, TimeSpan lifetime)
        {
            Issued++;
            return inner.Issue(subject, lifetime);
        }

        public TokenValidationResult Validate(string token) => inner.Validate(token);
    }
}
