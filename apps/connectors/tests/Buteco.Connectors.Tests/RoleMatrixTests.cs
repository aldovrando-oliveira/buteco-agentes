using System.Net;
using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;

namespace Buteco.Connectors.Tests;

/// <summary>
/// A matriz de papéis do <c>apps/connectors</c> (spec <c>route-roles</c> e <c>connectors-api</c>;
/// design.md da change papeis-no-token, D1, D2 e D11): a tabela de produção igual à matriz da spec,
/// escrita aqui à parte (lida da própria <see cref="RoleRoutes"/>, o guarda seria circular; medido na
/// 3.3 do tasks.md), e o comportamento de cada rota por papel. O Leitor recebe <c>403</c> nas três
/// rotas do painel, sem disparar ciclo de sincronização (critério (a) da #184, aqui com o emissor de
/// teste; com o emissor real, em <c>tests/ApiConnectorsRoundTrip.Tests</c>).
/// </summary>
public class RoleMatrixTests : IAsyncLifetime
{
    private static readonly Dictionary<(string Method, string Pattern), string> Expected = new()
    {
        [("GET", "/connectors/providers")] = "A O",
        [("GET", "/connectors/providers/{providerKey}/folders")] = "A O",
        [("POST", "/connectors/knowledge-bases/{knowledgeBaseId:guid}/sync")] = "A O",
    };

    private static readonly (string Role, string Letter)[] Roles =
        [(PanelRoles.Admin, "A"), (PanelRoles.Operator, "O"), (PanelRoles.Reader, "L")];

    private readonly ConnectorsFactory _factory = new();

    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var (method, pattern) in Expected.Keys)
        {
            foreach (var (role, _) in Roles)
            {
                data.Add(method, pattern, role);
            }
        }

        return data;
    }

    [Fact]
    public void TheProductionTable_IsTheExpectedMatrix()
    {
        Assert.Equal(Expected.Keys.OrderBy(key => key.ToString()), RoleRoutes.Table.Keys.OrderBy(key => key.ToString()));
        foreach (var (route, roles) in RoleRoutes.Table)
        {
            foreach (var (role, letter) in Roles)
            {
                Assert.True(
                    roles.Contains(role) == Expected[route].Split(' ').Contains(letter),
                    $"{route.Method} {route.Pattern}: o papel {role} diverge da matriz da spec");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachRoute_ForEachRole_FollowsTheMatrix(string method, string pattern, string role)
    {
        await _factory.WaitForIssuerKeysAsync();
        var client = _factory.CreateClientWithToken(_factory.Issuer.Issue(role: role));
        var path = pattern
            .Replace("{providerKey}", FakeConnector.Key, StringComparison.Ordinal)
            .Replace("{knowledgeBaseId:guid}", Guid.NewGuid().ToString(), StringComparison.Ordinal);

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
        var allowed = Expected[(method, pattern)].Split(' ').Contains(Roles.Single(entry => entry.Role == role).Letter);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        if (allowed)
        {
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task TheReader_IsForbiddenOnThePanelRoutes_AndNoCycleStarts()
    {
        await _factory.WaitForIssuerKeysAsync();
        var knowledgeBase = _factory.SyncApi.AddBase();
        var client = _factory.CreateClientWithToken(_factory.Issuer.Issue(role: PanelRoles.Reader));

        var providers = await client.GetAsync("/connectors/providers");
        var folders = await client.GetAsync($"/connectors/providers/{FakeConnector.Key}/folders");
        var sync = await client.PostAsync($"/connectors/knowledge-bases/{knowledgeBase.Id}/sync", null);

        Assert.Equal(HttpStatusCode.Forbidden, providers.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, folders.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sync.StatusCode);
        Assert.Empty(_factory.SyncApi.Requests);
    }

    [Fact]
    public void AnEntryWithoutRoles_IsAProblem()
    {
        var problems = ConnectorsSubjectRouteValidation.FindRoleProblems(
            new Dictionary<(string Method, string Pattern), IReadOnlyList<string>> { [("GET", "/connectors/providers")] = [] });

        Assert.Equal("a tabela de papéis lista GET '/connectors/providers' sem nenhum papel", Assert.Single(problems));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();
}
