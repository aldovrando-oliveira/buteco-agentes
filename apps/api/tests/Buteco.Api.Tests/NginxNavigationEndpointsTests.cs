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
/// React Router. A lista sai do código: o retorno e as URIs do cliente OIDC composto, e os endpoints do emissor em
/// passthrough, que o painel abre por navegação (o authorize e o logout).
///
/// <para>
/// <b>Os dois hosts</b> (change conectar-falha-minha-conta, #281, D0, P3): desde a #281 o cliente OIDC é registrado
/// também sem provedor, e o retorno é navegação de topo real nos dois. O caso roda num host com provedor
/// (<see cref="NginxNavigationEndpointsTests"/>, a classe e o nome de antes) e num sem nenhum
/// (<see cref="NginxNavigationEndpointsWithoutProviderTests"/>), com as mesmas duas asserções. A mutação da 3.6 da
/// #285 ("a lista do código vazia") era trocar o host pelo sem provedor; com o cliente sempre registrado essa forma
/// deixou de pegar (medido na sonda da tarefa 0), e a mutação passou a ser na fonte da lista.
/// </para>
/// </summary>
public abstract class NginxNavigationEndpointsTestsBase(ApiFactoryFixture fixture)
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

/// <summary>O host com um provedor configurado.</summary>
public class NginxNavigationEndpointsTests(FederatedLoginFixture fixture)
    : NginxNavigationEndpointsTestsBase(fixture), IClassFixture<FederatedLoginFixture>;

/// <summary>O host sem nenhum provedor: o cliente OIDC sem registração, com o retorno (#281).</summary>
public class NginxNavigationEndpointsWithoutProviderTests(LocalUsersFixture fixture)
    : NginxNavigationEndpointsTestsBase(fixture), IClassFixture<LocalUsersFixture>;
