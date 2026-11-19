using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using OpenIddict.Server;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O que o host responde, por nome (change conectar-falha-minha-conta, #281, D0, P2): as rotas mapeadas, com o
/// método e o padrão, e os endpoints do servidor OIDC, que o OpenIddict atende sem rota mapeada. Os do cliente OIDC
/// vão à parte (<see cref="ClientPaths"/>), porque o cliente passou a existir também sem provedor.
/// </summary>
public static class HostEndpointList
{
    public static IReadOnlyList<string> RoutesAndServerEndpoints(IServiceProvider services)
    {
        var routes = services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Order(StringComparer.Ordinal).ToList();
                return $"{(methods is { Count: > 0 } ? string.Join(",", methods) : "*")} {endpoint.RoutePattern.RawText}";
            });

        var server = services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var serverPaths = typeof(OpenIddictServerOptions).GetProperties()
            .Where(property => property.Name.EndsWith("EndpointUris", StringComparison.Ordinal) && property.PropertyType == typeof(List<Uri>))
            .SelectMany(property => ((List<Uri>)property.GetValue(server)!).Select(uri => $"oidc-servidor {property.Name} {PathOf(uri)}"));

        return [.. routes.Concat(serverPaths).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> ClientPaths(IServiceProvider services)
    {
        var client = services.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue;
        return [.. client.RedirectionEndpointUris.Concat(client.PostLogoutRedirectionEndpointUris).Select(PathOf).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    // O OpenIddict guarda as URIs relativas sem a barra do começo.
    private static string PathOf(Uri uri) => uri.IsAbsoluteUri ? uri.AbsolutePath : "/" + uri.OriginalString.TrimStart('/');
}
