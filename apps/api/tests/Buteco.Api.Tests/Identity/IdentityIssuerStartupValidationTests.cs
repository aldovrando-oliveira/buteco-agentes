using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Server;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Checagens de boot do emissor (convenção 8; design.md da change emissor-oidc-api, D8),
/// sempre sobre a <b>composição real</b> do <c>Program.cs</c>. Sem contêiner: todas
/// falham antes de qualquer acesso ao banco.
///
/// <para>
/// O último caso é o guarda da nova forma da convenção 8. Verificado contra o defeito
/// (convenção 15): sem a chamada de <c>ValidateIdentityIssuerEndpoints()</c> no
/// <c>Program.cs</c>, ele reprova (registro no tasks.md, 5.9).
/// </para>
/// <para>
/// O caso do refresh token de 15 dias é o guarda da desigualdade da limpeza (change
/// limpeza-tokens-openiddict, D3). Verificado contra o defeito: sem a checagem no
/// <c>ValidateIdentityIssuerConfiguration</c>, ele reprova (registro no tasks.md
/// daquela change, 4.2).
/// </para>
/// </summary>
public class IdentityIssuerStartupValidationTests
{
    [Fact]
    public void RealComposition_WithPublicUrlWithoutScheme_FailsNamingTheSetting()
    {
        using var factory = new Factory(new() { ["PublicUrl:BaseUrl"] = "api.exemplo.test" });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains("PublicUrl:BaseUrl inválido", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void RealComposition_WithoutTheEncryptionKey_FailsNamingTheSetting()
    {
        using var factory = new Factory(new()
        {
            ["PublicUrl:BaseUrl"] = ApiFactoryFixture.PublicBaseUrl,
            ["Identity:SigningKeyEncryptionKey"] = "",
        });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains("Identity:SigningKeyEncryptionKey não configurado", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void RealComposition_WithIntrospectionEndpoint_FailsToStart()
    {
        using var factory = new Factory(
            new() { ["PublicUrl:BaseUrl"] = ApiFactoryFixture.PublicBaseUrl },
            services => services.Configure<OpenIddictServerOptions>(options =>
                options.IntrospectionEndpointUris.Add(new Uri("connect/introspect", UriKind.Relative))));

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        Assert.Contains("endpoint introspection '/connect/introspect'", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public void RealComposition_WithRefreshTokensOutlivingThePruneMinimumAge_FailsNamingBothDurations()
    {
        using var factory = new Factory(
            new() { ["PublicUrl:BaseUrl"] = ApiFactoryFixture.PublicBaseUrl },
            services => services.Configure<OpenIddictServerOptions>(options => options.RefreshTokenLifetime = TimeSpan.FromDays(15)));

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.NotNull(exception);
        var message = Flatten(exception);
        Assert.Contains("Idade mínima da limpeza das entradas de token (14.00:00:00)", message, StringComparison.Ordinal);
        Assert.Contains("refresh token (15.00:00:00)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RealComposition_WithTheDefaultRefreshTokenLifetime_PassesThePruneCheck()
    {
        // A composição real, sem tocar na duração (14 dias, o padrão). Sem banco, o boot
        // pode falhar depois por outro motivo; o que se afirma é que não é por esta checagem.
        using var factory = new Factory(new() { ["PublicUrl:BaseUrl"] = ApiFactoryFixture.PublicBaseUrl });

        var exception = Record.Exception(() => _ = factory.Services);

        Assert.DoesNotContain("Idade mínima da limpeza", exception is null ? "" : Flatten(exception), StringComparison.Ordinal);
    }

    private sealed class Factory(Dictionary<string, string?> settings, Action<IServiceCollection>? services = null)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Por UseSetting, que chega ao registro dos serviços (ver ApiFactoryFixture).
            builder.UseSetting("PublicUrl:BaseUrl", settings["PublicUrl:BaseUrl"]);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(TestAuthentication.ConfigOverrides);
                config.AddInMemoryCollection(settings);
            });

            if (services is not null)
            {
                builder.ConfigureServices(services);
            }
        }
    }

    private static string Flatten(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} {Flatten(exception.InnerException)}";
}
