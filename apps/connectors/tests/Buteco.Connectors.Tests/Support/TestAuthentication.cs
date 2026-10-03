using System.Net.Http.Headers;
using Buteco.Connectors.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Connectors.Tests.Support;

// Mesmo valor literal das fixtures de apps/api e apps/inbox; não compartilhado em
// código (projetos de teste separados).
public static class TestAuthentication
{
    public const string TokenSigningKey = "test-signing-key-shared-between-api-and-inbox-fixtures";

    public static string IssueToken(IServiceProvider services, string subject, TimeSpan? lifetime = null)
    {
        var tokenService = services.GetRequiredService<ITokenService>();
        var (token, _) = tokenService.Issue(subject, lifetime ?? TimeSpan.FromMinutes(30));
        return token;
    }

    public static HttpClient CreateClientAs(ConnectorsFactory factory, string subject)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", IssueToken(factory.Services, subject));
        return client;
    }
}
