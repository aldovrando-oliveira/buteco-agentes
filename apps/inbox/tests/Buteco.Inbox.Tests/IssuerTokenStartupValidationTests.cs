using System.Net;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Buteco.Inbox.Tests;

/// <summary>
/// Checagens de boot da validação do token do emissor (spec <c>issuer-token-validation</c>;
/// design.md da change validacao-jwks-inbox-connectors, D7; convenção 8).
/// </summary>
public class IssuerTokenStartupValidationTests(PostgresOnlyFixture postgres) : IClassFixture<PostgresOnlyFixture>
{
    [Fact]
    public async Task WithoutIssuer_ValidationIsOff_IssuerTokenIsUnauthorized_HmacWorks_AndBootWarns()
    {
        var issuer = new TestIssuer();
        await using var factory = new IssuerTokenFactory(postgres, issuer, configuration: new() { ["Identity:Issuer"] = "" });

        var issued = await factory.CreateClientWithToken(issuer.Issue()).GetAsync("/channels");
        var hmac = await factory.CreateClientWithToken(factory.OperatorHmacToken()).GetAsync("/channels");

        Assert.Equal(HttpStatusCode.Unauthorized, issued.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hmac.StatusCode);
        Assert.Contains(factory.Logs.ToList(), entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("Identity:Issuer", StringComparison.Ordinal));
        Assert.Empty(issuer.Requests);
    }

    [Fact]
    public async Task WithIssuer_BootLogsTheCheckedConfiguration()
    {
        await using var factory = new IssuerTokenFactory(postgres, new TestIssuer());
        _ = factory.Server;

        Assert.Contains(factory.Logs.ToList(), entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains(TestIssuer.Issuer, StringComparison.Ordinal)
            && entry.Message.Contains(IssuerTokenAudience.Value, StringComparison.Ordinal));
    }

    /// <summary>
    /// Prova o <b>registro</b> da chamada no <c>Program.cs</c>: a aplicação real não sobe
    /// com o issuer sem esquema. Sem Postgres: a checagem roda antes de qualquer acesso ao banco.
    /// </summary>
    [Theory]
    [InlineData("issuer.exemplo.test")]
    [InlineData("https://issuer.exemplo.test/?tenant=1")]
    [InlineData("https://issuer.exemplo.test/#x")]
    [InlineData("ftp://issuer.exemplo.test/")]
    public void InvalidIssuer_FailsTheRealApplicationStartup(string configuredIssuer)
    {
        using var factory = RealApplication(new() { ["Identity:Issuer"] = configuredIssuer });

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Identity:Issuer", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void ClientPointingToUnknownSubject_FailsNamingClientAndSubject()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => IssuerTokenStartupValidation.ValidateIssuerClientSubjects(
            new Dictionary<string, string> { ["buteco-painel"] = "operator", ["cliente-x"] = "service:inexistente" },
            [SubjectAuthorization.OperatorSubject]));

        Assert.Contains("cliente-x", exception.Message, StringComparison.Ordinal);
        Assert.Contains("service:inexistente", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("buteco-painel", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RealClientTable_PassesAgainstTheRealSubjects()
    {
        var exception = Record.Exception(() => IssuerTokenStartupValidation.ValidateIssuerClientSubjects(
            IssuerClientSubjects.Table, [SubjectAuthorization.OperatorSubject]));

        Assert.Null(exception);
        Assert.Equal(SubjectAuthorization.OperatorSubject, IssuerClientSubjects.Table[IssuerClientSubjects.PanelClientId]);
    }

    private static WebApplicationFactory<Program> RealApplication(Dictionary<string, string?> overrides) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseIssuer(overrides["Identity:Issuer"]!).ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inbox:CredentialEncryptionKey"] = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=",
                ["Api:BaseUrl"] = TestIssuer.InternalBaseUrl,
                ["PublicUrl:BaseUrl"] = "http://apps-inbox.test",
            }).AddInMemoryCollection(TestAuthentication.ConfigOverrides).AddInMemoryCollection(overrides)));

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Flatten(exception.InnerException)}";
}
