using System.Net.Http.Headers;
using Buteco.Connectors.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Connectors.Tests.Support;

public static class TestAuthentication
{
    // O segredo do apps/connectors no emissor (servicos-token-emissor, D5), obrigatório no
    // boot com Api:BaseUrl. Gerado com `openssl rand -base64 32`; distinto do de
    // desenvolvimento.
    public const string ServiceClientSecret = "0YQDdIaMNUNTwbz8s2jahrj6AX8Q5MksdcZudVwEa0g=";

    /// <summary>
    /// Cliente com o token do emissor de teste da fábrica, do cliente que a tabela de clientes
    /// mapeia para <paramref name="subject"/> (fixtures-token-emissor, D3). Espera a primeira
    /// obtenção das chaves.
    /// </summary>
    public static HttpClient CreateIssuerClientAs(ConnectorsFactory factory, string subject)
    {
        var token = IssuerTokenFor(factory, subject);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// O token do emissor de teste para <paramref name="subject"/>. Subject que nenhum cliente
    /// produz vira um <c>client_id</c> igual ao texto dele, fora da tabela: o token autentica sem
    /// subject e recebe <c>403</c> pelo mesmo handler (fixtures-token-emissor, Q1, decisão do dono).
    /// </summary>
    public static string IssuerTokenFor(ConnectorsFactory factory, string subject)
    {
        Task.Run(factory.WaitForIssuerKeysAsync).GetAwaiter().GetResult();
        var clientId = IssuerClientSubjects.Table.FirstOrDefault(entry => entry.Value == subject).Key ?? subject;
        return factory.Issuer.Issue(clientId: clientId, subject: clientId);
    }
}
