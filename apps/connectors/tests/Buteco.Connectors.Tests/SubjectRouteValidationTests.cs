using Buteco.Connectors.Auth;
using Buteco.Connectors.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Connectors.Tests;

// connectors-api, "Tabela de subjects conferida no boot nos dois sentidos".
public class SubjectRouteValidationTests
{
    [Fact]
    public void TabelaCasaComAsRotas_NaoReprova()
    {
        var app = AppWith(endpoints =>
        {
            endpoints.MapGet("/connectors/providers", () => "ok");
            endpoints.MapGet("/connectors/providers/{providerKey}/folder", (string providerKey) => "ok");
        });

        var exception = Record.Exception(() => app.ValidateConnectorsSubjectRoutes(Table(
            ("operator", "GET", "/connectors/providers"),
            ("service:api", "GET", "/connectors/providers/{providerKey}/folder"))));

        Assert.Null(exception);
    }

    [Fact]
    public void EntradaDaTabelaSemRota_ReprovaNomeandoSubjectMetodoEPadrao()
    {
        var app = AppWith(endpoints => endpoints.MapGet("/connectors/providers", () => "ok"));

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateConnectorsSubjectRoutes(Table(
            ("operator", "GET", "/connectors/providers"),
            ("operator", "GET", "/connectors/providers/{providerKey}/pastas"))));

        Assert.Contains("operator", exception.Message);
        Assert.Contains("GET", exception.Message);
        Assert.Contains("/connectors/providers/{providerKey}/pastas", exception.Message);
    }

    [Fact]
    public void RotaAutenticadaSemDono_ReprovaNomeandoOPadrao()
    {
        var app = AppWith(endpoints =>
        {
            endpoints.MapGet("/connectors/providers", () => "ok");
            endpoints.MapGet("/connectors/esquecida", () => "ok");
        });

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateConnectorsSubjectRoutes(Table(
            ("operator", "GET", "/connectors/providers"))));

        Assert.Contains("/connectors/esquecida", exception.Message);
    }

    [Fact]
    public void RotaAnonimaFicaForaDosDoisSentidos()
    {
        var app = AppWith(endpoints =>
        {
            endpoints.MapGet("/connectors/providers", () => "ok");
            endpoints.MapGet("/health", () => "ok").AllowAnonymous();
        });

        var exception = Record.Exception(() => app.ValidateConnectorsSubjectRoutes(Table(
            ("operator", "GET", "/connectors/providers"))));

        Assert.Null(exception);
    }

    [Fact]
    public void MetodoDiferenteNaoConta()
    {
        var app = AppWith(endpoints => endpoints.MapPost("/connectors/providers", () => "ok"));

        var exception = Assert.Throws<InvalidOperationException>(() => app.ValidateConnectorsSubjectRoutes(Table(
            ("operator", "GET", "/connectors/providers"))));

        Assert.Contains("GET", exception.Message);
    }

    [Fact]
    public async Task ComposicaoReal_PassaSemDivergencia()
    {
        await using var factory = new ConnectorsFactory(withFakeConnector: false);
        _ = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var problems = ConnectorsSubjectRouteValidation.FindProblems(endpoints, ConnectorsSubjectAuthorizationHandler.SubjectRoutes);

        Assert.Empty(problems);
        Assert.Equal(3, ConnectorsSubjectAuthorizationHandler.SubjectRoutes.Values.Sum(routes => routes.Count));
    }

    private static WebApplication AppWith(Action<WebApplication> map)
    {
        var app = WebApplication.CreateBuilder().Build();
        map(app);
        return app;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<(string Method, string Pattern)>> Table(
        params (string Subject, string Method, string Pattern)[] entries) =>
        entries
            .GroupBy(entry => entry.Subject)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<(string Method, string Pattern)>)group.Select(entry => (entry.Method, entry.Pattern)).ToList());
}
