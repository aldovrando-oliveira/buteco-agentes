using System.Net;
using System.Text.Json;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Waha;

namespace Buteco.Inbox.Tests;

public class WahaOutboundMessageSenderTests
{
    private static string CredentialJson(string serviceUrl = "http://waha.test", string sessionName = "default", string authToken = "s3cr3t-key") =>
        JsonSerializer.Serialize(new WahaCredential(serviceUrl, sessionName, authToken));

    [Fact]
    public async Task SendAsync_PostsSendTextWithExpectedUrlHeaderAndBody()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sender = new WahaOutboundMessageSender(new SingleClientHttpClientFactory(new HttpClient(handler)));

        var message = new OutboundMessage(
            Guid.NewGuid(),
            CredentialJson(serviceUrl: "http://waha.test", sessionName: "minha-sessao", authToken: "chave-secreta"),
            "5511999999999@c.us",
            "Olá! Sua solicitação foi concluída.");

        await sender.SendAsync(message, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://waha.test/api/sendText", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("chave-secreta", Assert.Single(handler.LastRequest.Headers.GetValues("X-Api-Key")));

        var body = await JsonSerializer.DeserializeAsync<JsonElement>(await handler.LastRequest.Content!.ReadAsStreamAsync());
        Assert.Equal("minha-sessao", body.GetProperty("session").GetString());
        Assert.Equal("5511999999999@c.us", body.GetProperty("chatId").GetString());
        Assert.Equal("Olá! Sua solicitação foi concluída.", body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAsync_ChatIdIsContactExternalIdWithoutTransformation()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sender = new WahaOutboundMessageSender(new SingleClientHttpClientFactory(new HttpClient(handler)));

        // Grupo (@g.us), não indivíduo (@c.us) — prova que o sender não
        // tenta reconstruir/adivinhar o sufixo, só repassa o valor
        // recebido (design.md, Decision 5/7).
        var message = new OutboundMessage(Guid.NewGuid(), CredentialJson(), "12345-67890@g.us", "Resposta para o grupo");

        await sender.SendAsync(message, CancellationToken.None);

        var body = await JsonSerializer.DeserializeAsync<JsonElement>(await handler.LastRequest!.Content!.ReadAsStreamAsync());
        Assert.Equal("12345-67890@g.us", body.GetProperty("chatId").GetString());
    }

    [Fact]
    public async Task SendAsync_NonSuccessResponse_ThrowsHttpRequestException()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sender = new WahaOutboundMessageSender(new SingleClientHttpClientFactory(new HttpClient(handler)));

        var message = new OutboundMessage(Guid.NewGuid(), CredentialJson(), "5511999999999@c.us", "Texto");

        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(message, CancellationToken.None));
    }

    private sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class CapturingHttpMessageHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }
}
