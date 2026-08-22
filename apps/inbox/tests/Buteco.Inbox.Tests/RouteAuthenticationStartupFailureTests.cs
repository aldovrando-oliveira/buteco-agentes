using Buteco.Inbox.Auth;
using Microsoft.AspNetCore.Builder;

namespace Buteco.Inbox.Tests;

// Mesmo estilo de ChannelAdapterRegistrationExtensionsTests.cs e do par
// equivalente em apps/api — unitário, direto na extensão, sem subir o
// host completo da aplicação. Cobre o par "com item"/"sem item" da
// checagem de integridade (design.md, Decision 4).
public class RouteAuthenticationStartupFailureTests
{
    [Fact]
    public void ValidateRouteAuthenticationClassification_AllRoutesClassified_DoesNotThrow()
    {
        var app = WebApplication.CreateBuilder().Build();
        app.MapGet("/health", () => "ok")
            .AllowAnonymous()
            .WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.HealthProbe));
        app.MapGet("/channels", () => "ok");

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
            () => app.ValidateRouteAuthenticationClassification("/health", "/webhooks/{channelId:guid}"));

        Assert.Contains("/webhooks/{channelId:guid}", exception.Message);
    }
}
