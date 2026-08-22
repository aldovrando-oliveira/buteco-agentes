using Buteco.Api.Auth;
using Microsoft.AspNetCore.Builder;

namespace Buteco.Api.Tests;

// Mesmo estilo de ChannelAdapterRegistrationExtensionsTests.cs
// (apps/inbox) — unitário, direto na extensão, sem subir o host completo
// da aplicação. Cobre o par "com item"/"sem item" da checagem de
// integridade (design.md, Decision 4): boot sobe quando tudo está
// classificado, boot falha nos dois sentidos quando não está.
public class RouteAuthenticationStartupFailureTests
{
    [Fact]
    public void ValidateRouteAuthenticationClassification_AllRoutesClassified_DoesNotThrow()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/health", () => "ok")
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.HealthProbe));
        app.MapGet("/agents", () => "ok");

        var exception = Record.Exception(() => app.ValidateRouteAuthenticationClassification("/health"));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateRouteAuthenticationClassification_AnonymousRouteWithoutReason_Throws()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/health", () => "ok").AllowAnonymous();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.ValidateRouteAuthenticationClassification("/health"));

        Assert.Contains("/health", exception.Message);
    }

    [Fact]
    public void ValidateRouteAuthenticationClassification_ExpectedAnonymousRouteNotMapped_Throws()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/health", () => "ok")
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.HealthProbe));

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.ValidateRouteAuthenticationClassification("/health", "/auth/login"));

        Assert.Contains("/auth/login", exception.Message);
    }
}
