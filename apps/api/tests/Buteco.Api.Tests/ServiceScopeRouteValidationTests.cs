using Buteco.Api.Auth;
using Microsoft.AspNetCore.Builder;

namespace Buteco.Api.Tests;

/// <summary>
/// A checagem de boot da lista de rotas dos subjects de serviço (design.md da
/// change catalogo-base-sincronizada, D6; convenção 8). A extensão direto, sem
/// host, no molde de <see cref="RouteAuthenticationStartupFailureTests"/>. A
/// composição real é afirmada em <c>ServiceScopeAuthorizationTests</c>, que já
/// paga a fixture com o host construído.
/// </summary>
public class ServiceScopeRouteValidationTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> Routes =
        new Dictionary<string, IReadOnlyList<(string Method, string Pattern)>>
        {
            ["service:teste"] = [("GET", "/sync/knowledge-bases"), ("PUT", "/sync/knowledge-bases/{id:guid}/documents")],
        };

    [Fact]
    public void ValidateServiceScopeRoutes_AllRoutesMapped_DoesNotThrow()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/sync/knowledge-bases", () => "ok");
        app.MapPut("/sync/knowledge-bases/{id:guid}/documents", () => "ok");

        var exception = Record.Exception(() => app.ValidateServiceScopeRoutes(Routes));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateServiceScopeRoutes_RouteNotMapped_ThrowsNamingSubjectMethodAndPattern()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/sync/knowledge-bases", () => "ok");

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateServiceScopeRoutes(Routes));

        Assert.Contains("service:teste", exception.Message);
        Assert.Contains("PUT", exception.Message);
        Assert.Contains("/sync/knowledge-bases/{id:guid}/documents", exception.Message);
    }

    [Fact]
    public void ValidateServiceScopeRoutes_PatternMappedWithAnotherMethod_Throws()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/sync/knowledge-bases", () => "ok");
        app.MapGet("/sync/knowledge-bases/{id:guid}/documents", () => "ok");

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateServiceScopeRoutes(Routes));

        Assert.Contains("PUT", exception.Message);
    }
}
