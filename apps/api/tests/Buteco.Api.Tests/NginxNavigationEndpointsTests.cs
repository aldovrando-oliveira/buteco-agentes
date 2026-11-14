using Buteco.Api.Identity;
using Buteco.Api.Identity.Federated;
using Buteco.Api.Tests.Support;
using Buteco.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;

namespace Buteco.Api.Tests;

/// <summary>
/// Todo caminho que o navegador abre no apps/api por navegação de topo tem um <c>location =</c> próprio no nginx do
/// stack, encaminhando ao apps/api e sem o desvio de navegação (spec <c>server-deployment</c>, "Nginx do stack roteia
/// por prefixo"; change nginx-retorno-federado, #285, design D3). Pelo bloco de regex do prefixo, com o desvio, o
/// navegador recebe o shell do SPA: foi o que a homologação da M3 viu no retorno do Google, com o "404 Not Found" do
/// React Router. A lista sai do código: o retorno e as URIs do cliente OIDC composto, num host com um provedor
/// configurado (sem provedor, o cliente nem é registrado), e os endpoints do emissor em passthrough, que o painel
/// abre por navegação (o authorize e o logout).
/// </summary>
public class NginxNavigationEndpointsTests(FederatedLoginFixture fixture) : IClassFixture<FederatedLoginFixture>
{
    [Fact]
    public void EveryTopLevelNavigationPath_HasItsOwnExactBlock_ToTheApi_WithoutTheDeviation()
    {
        var client = fixture.Services.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue;
        var clientPaths = client.RedirectionEndpointUris
            .Concat(client.PostLogoutRedirectionEndpointUris)
            .Select(PathOf)
            .ToList();
        Assert.True(clientPaths.Count > 0, "o cliente OIDC do host não tem nenhuma URI de retorno: o guarda não teria o que conferir.");

        var paths = clientPaths
            .Append(FederatedLogin.CallbackPath)
            .Concat(IdentityIssuerEndpoints.Expected.Where(endpoint => endpoint.Value.Passthrough).Select(endpoint => endpoint.Key));

        var problems = NginxRouting.FindNavigationProblems(paths, NginxRouting.ParseFile(), upstream: "api");

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // O OpenIddict guarda as URIs relativas sem a barra do começo.
    private static string PathOf(Uri uri) => uri.IsAbsoluteUri ? uri.AbsolutePath : "/" + uri.OriginalString.TrimStart('/');
}
