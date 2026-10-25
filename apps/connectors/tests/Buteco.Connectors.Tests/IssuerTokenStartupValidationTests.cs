using System.Net;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Checagens de boot da validação do token do emissor (spec <c>issuer-token-validation</c>;
/// design.md da change validacao-jwks-inbox-connectors, D7; convenção 8). Aqui o
/// <c>Api:BaseUrl</c> é opcional, então o issuer ligado sem ele também derruba o boot.
/// </summary>
public class IssuerTokenStartupValidationTests
{
    [Fact]
    public async Task WithoutIssuer_ValidationIsOff_IssuerTokenIsUnauthorized_HmacWorks_AndBootWarns()
    {
        var issuer = new TestIssuer();
        await using var factory = new ConnectorsFactory();

        var issued = await factory.CreateClientWithToken(issuer.Issue()).GetAsync("/connectors/providers");
        var hmac = await factory.CreateClientWithToken(TestAuthentication.IssueToken(factory.Services, "operator")).GetAsync("/connectors/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, issued.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hmac.StatusCode);
        Assert.Contains(factory.Logs.Lines, line =>
            line.StartsWith("Warning", StringComparison.Ordinal) && line.Contains("Identity:Issuer", StringComparison.Ordinal));
        Assert.Empty(issuer.Requests);
    }

    [Fact]
    public async Task WithIssuer_BootLogsTheCheckedConfiguration()
    {
        await using var factory = new ConnectorsFactory(issuer: new TestIssuer());
        _ = factory.Server;

        Assert.Contains(factory.Logs.Lines, line =>
            line.StartsWith("Information", StringComparison.Ordinal)
            && line.Contains(TestIssuer.Issuer, StringComparison.Ordinal)
            && line.Contains(IssuerTokenAudience.Value, StringComparison.Ordinal));
    }

    /// <summary>
    /// Prova o <b>registro</b> da chamada no <c>Program.cs</c>: a aplicação real não sobe
    /// com issuer inválido.
    /// </summary>
    [Theory]
    [InlineData("issuer.exemplo.test")]
    [InlineData("https://issuer.exemplo.test/?tenant=1")]
    [InlineData("https://issuer.exemplo.test/#x")]
    [InlineData("ftp://issuer.exemplo.test/")]
    public void InvalidIssuer_FailsTheRealApplicationStartup(string configuredIssuer)
    {
        using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?>
        {
            ["Identity:Issuer"] = configuredIssuer,
            ["Api:BaseUrl"] = TestIssuer.InternalBaseUrl,
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Identity:Issuer", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void IssuerWithoutApiBaseUrl_FailsTheRealApplicationStartup_NamingBothKeys()
    {
        using var factory = new ConnectorsFactory(extraConfiguration: new Dictionary<string, string?>
        {
            ["Identity:Issuer"] = TestIssuer.Issuer,
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("Identity:Issuer", Flatten(exception), StringComparison.Ordinal);
        Assert.Contains("Api:BaseUrl", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void ClientPointingToUnknownSubject_FailsNamingClientAndSubject()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => IssuerTokenStartupValidation.ValidateIssuerClientSubjects(
            new Dictionary<string, string> { ["buteco-painel"] = "operator", ["cliente-x"] = "service:inexistente" },
            [.. ConnectorsSubjectAuthorizationHandler.SubjectRoutes.Keys]));

        Assert.Contains("cliente-x", exception.Message, StringComparison.Ordinal);
        Assert.Contains("service:inexistente", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("buteco-painel", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RealClientTable_PassesAgainstTheRealSubjectRoutes()
    {
        var exception = Record.Exception(() => IssuerTokenStartupValidation.ValidateIssuerClientSubjects(
            IssuerClientSubjects.Table, [.. ConnectorsSubjectAuthorizationHandler.SubjectRoutes.Keys]));

        Assert.Null(exception);
        Assert.Equal(ConnectorsSubjectAuthorizationHandler.OperatorSubject, IssuerClientSubjects.Table[IssuerClientSubjects.PanelClientId]);
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null ? exception.Message : $"{exception.Message} | {Flatten(exception.InnerException)}";
}
