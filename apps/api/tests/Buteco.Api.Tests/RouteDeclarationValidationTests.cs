using Buteco.Api.Auth;
using Buteco.Api.Tests.Support;
using Buteco.Api.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests;

/// <summary>
/// "Toda rota autenticada declara papel ou subject de serviço, conferido no boot" (spec
/// <c>route-roles</c>; design.md da change papeis-no-token, D2; convenção 8). A extensão direto,
/// sem host, e a composição real pela fixture, que é o único caso que pega a chamada removida do
/// <c>Program.cs</c> junto com uma entrada a menos (convenção 8, segunda forma).
/// </summary>
public class RouteDeclarationValidationTests(ApiFactoryFixture factory) : IClassFixture<ApiFactoryFixture>
{
    // Sem nenhum serviço, mas com a chave da aplicação externa, que a checagem exige desde a change
    // aplicacoes-externas-credencial (D1): sem ela, todo caso ganharia um problema que não é o dele.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> NoServices =
        new Dictionary<string, IReadOnlyList<(string Method, string Pattern)>>
        {
            [ServiceScopeAuthorizationHandler.ApplicationSubject] = [],
        };

    private static IReadOnlyDictionary<(string Method, string Pattern), IReadOnlyList<UserRole>> Roles(
        params ((string Method, string Pattern) Route, UserRole[] Roles)[] entries) =>
        entries.ToDictionary(entry => entry.Route, entry => (IReadOnlyList<UserRole>)entry.Roles);

    [Fact]
    public void AnAuthenticatedRouteWithoutDeclaration_Throws_NamingMethodAndPattern()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/coisas", () => "ok");
        app.MapPost("/coisas", () => "ok");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            app.ValidateRouteDeclarations(Roles((("GET", "/coisas"), [UserRole.Reader])), NoServices));

        Assert.Contains("POST '/coisas'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("GET '/coisas'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARoleEntryWithoutRoute_Throws_NamingTheEntry()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/coisas", () => "ok");

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateRouteDeclarations(
            Roles((("GET", "/coisas"), [UserRole.Reader]), (("GET", "/coisas-renomeadas"), [UserRole.Reader])), NoServices));

        Assert.Contains("GET '/coisas-renomeadas'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyRoleList_Throws_NamingTheEntry()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/coisas", () => "ok");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            app.ValidateRouteDeclarations(Roles((("GET", "/coisas"), [])), NoServices));

        Assert.Contains("GET '/coisas' sem nenhum papel", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AServiceRoute_CountsAsDeclared_AndAnAnonymousRouteStaysOutOfBothSenses()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/sync/coisas", () => "ok");
        app.MapGet("/aberta", () => "ok").AllowAnonymous();
        var services = new Dictionary<string, IReadOnlyList<(string Method, string Pattern)>>
        {
            ["service:teste"] = [("GET", "/sync/coisas")],
            [ServiceScopeAuthorizationHandler.ApplicationSubject] = [],
        };

        var exception = Record.Exception(() => app.ValidateRouteDeclarations(Roles(), services));

        Assert.Null(exception);
    }

    // "Lista vazia de subject de serviço" (spec route-roles; 4.8 do tasks.md da change
    // aplicacoes-externas-credencial). Uma lista esvaziada por engano deixaria o serviço com 403 em tudo.
    [Fact]
    public void AnEmptyServiceRouteList_Throws_NamingTheSubject()
    {
        var app = WebApplication.CreateBuilder().Build();
        var services = new Dictionary<string, IReadOnlyList<(string Method, string Pattern)>>
        {
            [ServiceScopeAuthorizationHandler.ConnectorsSubject] = [],
            [ServiceScopeAuthorizationHandler.ApplicationSubject] = [],
        };

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateRouteDeclarations(Roles(), services));

        Assert.Contains("'service:connectors' tem a lista de rotas vazia", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'application' tem a lista", exception.Message, StringComparison.Ordinal);
    }

    // "A lista vazia de application passa, por exceção" (spec route-roles): a exceção temporária até a
    // #187, que a retira junto com a abertura da rota A2A.
    [Fact]
    public void TheApplicationSubject_MayHaveAnEmptyList_UntilIssue187()
    {
        Assert.Empty(ServiceScopeAuthorizationHandler.ServiceRoutes[ServiceScopeAuthorizationHandler.ApplicationSubject]);
        var app = WebApplication.CreateBuilder().Build();

        var exception = Record.Exception(() => app.ValidateRouteDeclarations(Roles(), NoServices));

        Assert.Null(exception);
    }

    // "Sem a entrada application" (spec route-roles).
    [Fact]
    public void WithoutTheApplicationSubject_Throws_NamingIt()
    {
        var app = WebApplication.CreateBuilder().Build();
        var services = new Dictionary<string, IReadOnlyList<(string Method, string Pattern)>>();

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateRouteDeclarations(Roles(), services));

        Assert.Contains("o subject 'application'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RealComposition_EveryAuthenticatedRouteIsDeclared()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        Assert.NotEmpty(RoleRoutes.Table);
        Assert.Empty(RouteDeclarationValidation.FindProblems(endpoints, RoleRoutes.Table, ServiceScopeAuthorizationHandler.ServiceRoutes));
    }
}
