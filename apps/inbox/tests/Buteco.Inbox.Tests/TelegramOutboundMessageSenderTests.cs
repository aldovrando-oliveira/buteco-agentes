using System.Net;
using System.Text.Json;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Telegram;

namespace Buteco.Inbox.Tests;

public class TelegramOutboundMessageSenderTests
{
    private static string CredentialJson(string botToken = "123456:ABC-DEF") =>
        JsonSerializer.Serialize(new TelegramCredential(botToken));

    [Fact]
    public async Task SendAsync_PostsSendMessageWithExpectedUrlAndBody()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sender = new TelegramOutboundMessageSender(new SingleClientHttpClientFactory(new HttpClient(handler)));

        var message = new OutboundMessage(
            Guid.NewGuid(),
            CredentialJson(botToken: "123456:chave-secreta"),
            "987654321",
            "Olá! Sua solicitação foi concluída.");

        await sender.SendAsync(message, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.telegram.org/bot123456:chave-secreta/sendMessage", handler.LastRequest.RequestUri!.ToString());

        var body = await JsonSerializer.DeserializeAsync<JsonElement>(await handler.LastRequest.Content!.ReadAsStreamAsync());
        Assert.Equal("987654321", body.GetProperty("chat_id").GetString());
        Assert.Equal("Olá! Sua solicitação foi concluída.", body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAsync_ChatIdIsContactExternalIdWithoutTransformation()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sender = new TelegramOutboundMessageSender(new SingleClientHttpClientFactory(new HttpClient(handler)));

        var message = new OutboundMessage(Guid.NewGuid(), CredentialJson(), "-100123456789", "Resposta para o grupo");

        await sender.SendAsync(message, CancellationToken.None);

        var body = await JsonSerializer.DeserializeAsync<JsonElement>(await handler.LastRequest!.Content!.ReadAsStreamAsync());
        Assert.Equal("-100123456789", body.GetProperty("chat_id").GetString());
    }

    [Fact]
    public async Task SendAsync_NonSuccessResponse_ThrowsHttpRequestException()
    {
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sender = new TelegramOutboundMessageSender(new SingleClientHttpClientFactory(new HttpClient(handler)));

        var message = new OutboundMessage(Guid.NewGuid(), CredentialJson(), "987654321", "Texto");

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
