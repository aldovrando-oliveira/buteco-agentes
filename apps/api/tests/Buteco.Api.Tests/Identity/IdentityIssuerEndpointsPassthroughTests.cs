using Buteco.Api.Auth;
using Buteco.Api.Identity;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A checagem de passthrough do <c>ValidateIdentityIssuerEndpoints</c> (spec
/// <c>route-authentication</c>, "Passthrough sem rota derruba o boot"; design.md da change
/// usuarios-locais-login-emissor, D3.6), sobre um <c>WebApplication</c> mínimo com o registro
/// real do emissor e só as rotas que cada caso mapeia. Sem banco: a checagem não consulta.
///
/// <para>
/// Verificado contra o defeito (convenção 15): sem o ramo do passthrough na checagem, os
/// três casos de recusa reprovam (registro no tasks.md, 6.2).
/// </para>
/// </summary>
public class IdentityIssuerEndpointsPassthroughTests
{
    private static WebApplication Build(Action<WebApplication> map)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PublicUrl:BaseUrl"] = ApiFactoryFixture.PublicBaseUrl });
        builder.Services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql("Host=localhost"));
        builder.Services.AddIdentityIssuer(builder.Configuration);

        var app = builder.Build();
        map(app);
        return app;
    }

    private static void Anonymous(WebApplication app, string path, AnonymousRouteReason reason) =>
        app.MapGet(path, () => Results.Ok()).AllowAnonymous().WithMetadata(new AnonymousRouteClassification(reason));

    [Fact]
    public void WithoutTheAuthorizeRoute_Fails_NamingIt()
    {
        var app = Build(app => Anonymous(app, IdentityIssuerEndpoints.EndSession, AnonymousRouteReason.SessionEnd));

        var exception = Assert.Throws<InvalidOperationException>(app.ValidateIdentityIssuerEndpoints);

        Assert.Contains("'/connect/authorize' é endpoint em passthrough e não tem rota mapeada", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTheRouteNotAnonymous_Fails()
    {
        var app = Build(app =>
        {
            app.MapGet(IdentityIssuerEndpoints.Authorization, () => Results.Ok());
            Anonymous(app, IdentityIssuerEndpoints.EndSession, AnonymousRouteReason.SessionEnd);
        });

        var exception = Assert.Throws<InvalidOperationException>(app.ValidateIdentityIssuerEndpoints);

        Assert.Contains("'/connect/authorize' é endpoint em passthrough", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithTheRouteClassifiedWithAnotherReason_Fails()
    {
        var app = Build(app =>
        {
            Anonymous(app, IdentityIssuerEndpoints.Authorization, AnonymousRouteReason.AuthEntryPoint);
            Anonymous(app, IdentityIssuerEndpoints.EndSession, AnonymousRouteReason.AuthEntryPoint);
        });

        var exception = Assert.Throws<InvalidOperationException>(app.ValidateIdentityIssuerEndpoints);

        Assert.Contains("'/connect/logout' é endpoint em passthrough e não tem rota mapeada de mesmo caminho, anônima e classificada como SessionEnd", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithBothRoutesAnonymousAndClassified_Passes()
    {
        var app = Build(app =>
        {
            Anonymous(app, IdentityIssuerEndpoints.Authorization, AnonymousRouteReason.AuthEntryPoint);
            Anonymous(app, IdentityIssuerEndpoints.EndSession, AnonymousRouteReason.SessionEnd);
        });

        app.ValidateIdentityIssuerEndpoints();
    }

    [Fact]
    public void ARouteOnANonPassthroughEndpoint_StillFails()
    {
        var app = Build(app =>
        {
            Anonymous(app, IdentityIssuerEndpoints.Authorization, AnonymousRouteReason.AuthEntryPoint);
            Anonymous(app, IdentityIssuerEndpoints.EndSession, AnonymousRouteReason.SessionEnd);
            Anonymous(app, IdentityIssuerEndpoints.Token, AnonymousRouteReason.AuthEntryPoint);
        });

        var exception = Assert.Throws<InvalidOperationException>(app.ValidateIdentityIssuerEndpoints);

        Assert.Contains("'/connect/token' é endpoint do servidor OIDC e também rota mapeada do app", exception.Message, StringComparison.Ordinal);
    }
}
