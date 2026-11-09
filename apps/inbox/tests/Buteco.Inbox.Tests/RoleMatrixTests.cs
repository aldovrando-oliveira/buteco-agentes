using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Buteco.Inbox.Auth;
using Buteco.Inbox.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

/// <summary>
/// A matriz de papéis do <c>apps/inbox</c> (spec <c>route-roles</c>; design.md da change
/// papeis-no-token, D1, D2 e D11): a tabela de produção igual à matriz da spec, escrita aqui à
/// parte (lida da própria <see cref="RoleRoutes"/>, o guarda seria circular; medido na 3.3 do
/// tasks.md), o comportamento de cada rota por papel, e a checagem de boot nos três sentidos.
/// O <c>403</c> da autorização sai com o corpo vazio; a resposta da própria rota (um id que não
/// existe), com corpo ou outro status.
/// </summary>
public partial class RoleMatrixTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private static readonly Dictionary<(string Method, string Pattern), string> Expected = new()
    {
        [("GET", "/channels/")] = "A O L",
        [("GET", "/channels/{id:guid}")] = "A O L",
        [("POST", "/channels/")] = "A O",
        [("PUT", "/channels/{id:guid}")] = "A O",
        [("POST", "/channels/{id:guid}/activate")] = "A O",
        [("POST", "/channels/{id:guid}/deactivate")] = "A O",
        [("GET", "/channels/{channelId:guid}/sessions")] = "A O",
        [("GET", "/sessions/{sessionId:guid}/messages")] = "A O",
        [("GET", "/contacts/")] = "A O",
        [("GET", "/contacts/{id:guid}/sessions")] = "A O",
        [("GET", "/sessions/summary")] = "A O L",
        [("GET", "/messages/summary")] = "A O L",
    };

    private static readonly (string Role, string Letter)[] Roles =
        [(PanelRoles.Admin, "A"), (PanelRoles.Operator, "O"), (PanelRoles.Reader, "L")];

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
        Assert.NotEmpty(Expected);
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
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Issuer.Issue(role: role));
        var request = new HttpRequestMessage(new HttpMethod(method), RouteParameter().Replace(pattern, _ => Guid.NewGuid().ToString()));
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await client.SendAsync(request);
        var denied = response.StatusCode == HttpStatusCode.Forbidden && (await response.Content.ReadAsByteArrayAsync()).Length == 0;
        var letter = Roles.Single(entry => entry.Role == role).Letter;

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(
            denied != Expected[(method, pattern)].Split(' ').Contains(letter),
            $"{method} {pattern} ({role}): {(int)response.StatusCode}, {(denied ? "negado" : "atendido")} pela autorização");
    }

    [Fact]
    public void RealComposition_EveryAuthenticatedRouteIsDeclared()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        Assert.Empty(RouteDeclarationValidation.FindProblems(endpoints, RoleRoutes.Table));
    }

    [Fact]
    public void AnAuthenticatedRouteWithoutDeclaration_Throws_NamingMethodAndPattern()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/coisas", () => "ok");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            app.ValidateRouteDeclarations(new Dictionary<(string Method, string Pattern), IReadOnlyList<string>>()));

        Assert.Contains("GET '/coisas'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWithoutRoute_OrWithoutRoles_Throws_NamingTheEntry()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/coisas", () => "ok");

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateRouteDeclarations(
            new Dictionary<(string Method, string Pattern), IReadOnlyList<string>>
            {
                [("GET", "/coisas")] = [],
                [("GET", "/coisas-renomeadas")] = [PanelRoles.Reader],
            }));

        Assert.Contains("GET '/coisas' sem nenhum papel", exception.Message, StringComparison.Ordinal);
        Assert.Contains("GET '/coisas-renomeadas', que não corresponde", exception.Message, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
