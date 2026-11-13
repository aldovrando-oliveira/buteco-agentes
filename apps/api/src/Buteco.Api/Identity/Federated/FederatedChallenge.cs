using Microsoft.AspNetCore.Authentication;
using OpenIddict.Client;
using OpenIddict.Client.AspNetCore;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// O início do fluxo com o provedor (design.md da change provedores-federados-google, D1, passo 2):
/// o desafio do cliente OIDC para a registração da chave, com as propriedades do Buteco no state token.
/// O OpenIddict responde o desafio com <c>302</c> para o provedor e grava o cookie do fluxo; aqui o
/// <c>302</c> vira <c>200 { url }</c>, porque o painel começa o fluxo por <c>fetch</c> (JSON e
/// <c>Origin</c>) e só depois navega. O <c>Set-Cookie</c> do fluxo fica na mesma resposta.
/// </summary>
public static class FederatedChallenge
{
    public sealed record StartResponse(string Url);

    public const string UnreachableMessage = "Não foi possível falar com o provedor. Tente de novo em instantes.";

    public static async Task<IResult> BeginAsync(HttpContext httpContext, FederatedProvider provider, IDictionary<string, string?> items)
    {
        // O desafio precisa do discovery do provedor. Buscado antes, com prazo próprio (D15, APPLY-11):
        // fora do alcance, 503 com problem+json em até FederatedLogin.ProviderTimeout, e não o 400 em
        // texto do OpenIddict depois das tentativas do cliente. Buscado uma vez, ele fica no cache da
        // registração, e o desafio seguinte não volta à rede.
        if (!await ProviderReachableAsync(httpContext, provider))
        {
            return TypedResults.Problem(title: UnreachableMessage, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var properties = new AuthenticationProperties(new Dictionary<string, string?>(items)
        {
            [OpenIddictClientAspNetCoreConstants.Properties.RegistrationId] = provider.Key,
            [FederatedLogin.Properties.ProviderKey] = provider.Key,
        });

        await httpContext.ChallengeAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme, properties);

        var location = httpContext.Response.Headers.Location.ToString();
        if (httpContext.Response.StatusCode != StatusCodes.Status302Found || string.IsNullOrEmpty(location))
        {
            throw new InvalidOperationException(
                $"O desafio do cliente OIDC para o provedor '{provider.Key}' não respondeu com o redirecionamento ao provedor.");
        }

        httpContext.Response.Headers.Remove("Location");
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        return TypedResults.Ok(new StartResponse(location));
    }

    private static async Task<bool> ProviderReachableAsync(HttpContext httpContext, FederatedProvider provider)
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(FederatedLoginEndpoints.LoggerCategory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted);
        timeout.CancelAfter(FederatedLogin.ProviderTimeout);
        try
        {
            var registration = await httpContext.RequestServices.GetRequiredService<OpenIddictClientService>()
                .GetClientRegistrationByIdAsync(provider.Key, timeout.Token);
            if (registration.ConfigurationManager is { } manager)
            {
                await manager.GetConfigurationAsync(timeout.Token).WaitAsync(timeout.Token);
            }

            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && !httpContext.RequestAborted.IsCancellationRequested)
        {
            FederatedLoginEndpoints.LogFailed(logger, provider.Key, $"provedor_inalcancavel ({exception.GetType().Name})");
            return false;
        }
    }
}
