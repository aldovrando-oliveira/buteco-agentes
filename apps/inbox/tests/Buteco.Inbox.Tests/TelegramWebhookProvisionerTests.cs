using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Telegram;

namespace Buteco.Inbox.Tests;

public class TelegramWebhookProvisionerTests
{
    private static string CredentialJson(string botToken = "123456:ABC-DEF") =>
        JsonSerializer.Serialize(new TelegramCredential(botToken));

    [Fact]
    public async Task ProvisionAsync_PostsSetWebhookWithExpectedUrlAndNonEmptySecret()
    {
        var handler = new JsonRespondingHttpMessageHandler(HttpStatusCode.OK, new { ok = true });
        var provisioner = new TelegramWebhookProvisioner(new SingleClientHttpClientFactory(handler));

        await provisioner.ProvisionAsync(
            Guid.NewGuid(), CredentialJson(botToken: "123456:chave-secreta"), "https://inbox.exemplo.com/webhooks/abc", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://api.telegram.org/bot123456:chave-secreta/setWebhook", handler.LastRequest.RequestUri!.ToString());

        var body = await JsonSerializer.DeserializeAsync<JsonElement>(await handler.LastRequest.Content!.ReadAsStreamAsync());
        Assert.Equal("https://inbox.exemplo.com/webhooks/abc", body.GetProperty("url").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("secret_token").GetString()));
    }

    [Fact]
    public async Task ProvisionAsync_SuccessfulResponse_ReturnsCredentialWithWebhookSecretFilled()
    {
        var handler = new JsonRespondingHttpMessageHandler(HttpStatusCode.OK, new { ok = true });
        var provisioner = new TelegramWebhookProvisioner(new SingleClientHttpClientFactory(handler));

        var result = await provisioner.ProvisionAsync(
            Guid.NewGuid(), CredentialJson(), "https://inbox.exemplo.com/webhooks/abc", CancellationToken.None);

        Assert.True(result.Success);
        var updated = JsonSerializer.Deserialize<TelegramCredential>(result.UpdatedCredential!)!;
        Assert.False(string.IsNullOrWhiteSpace(updated.WebhookSecret));
        Assert.Equal("123456:ABC-DEF", updated.BotToken);
    }

    [Fact]
    public async Task ProvisionAsync_ApiRespondsOkFalse_ReturnsFailedWithDescriptionWithoutThrowing()
    {
        var handler = new JsonRespondingHttpMessageHandler(HttpStatusCode.OK, new { ok = false, description = "Bad Request: token inválido" });
        var provisioner = new TelegramWebhookProvisioner(new SingleClientHttpClientFactory(handler));

        var result = await provisioner.ProvisionAsync(
            Guid.NewGuid(), CredentialJson(), "https://inbox.exemplo.com/webhooks/abc", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.UpdatedCredential);
        Assert.Equal("Bad Request: token inválido", result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionAsync_NonSuccessHttpStatus_ReturnsFailedWithoutThrowing()
    {
        var handler = new JsonRespondingHttpMessageHandler(HttpStatusCode.Unauthorized, new { ok = false, description = "Unauthorized" });
        var provisioner = new TelegramWebhookProvisioner(new SingleClientHttpClientFactory(handler));

        var result = await provisioner.ProvisionAsync(
            Guid.NewGuid(), CredentialJson(), "https://inbox.exemplo.com/webhooks/abc", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Unauthorized", result.ErrorMessage);
    }

    [Fact]
    public async Task ProvisionAsync_TwoCallsInSequence_GenerateDifferentSecrets()
    {
        var handler = new JsonRespondingHttpMessageHandler(HttpStatusCode.OK, new { ok = true });
        var provisioner = new TelegramWebhookProvisioner(new SingleClientHttpClientFactory(handler));

        var first = await provisioner.ProvisionAsync(Guid.NewGuid(), CredentialJson(), "https://inbox.exemplo.com/webhooks/abc", CancellationToken.None);
        var second = await provisioner.ProvisionAsync(Guid.NewGuid(), CredentialJson(), "https://inbox.exemplo.com/webhooks/abc", CancellationToken.None);

        var firstSecret = JsonSerializer.Deserialize<TelegramCredential>(first.UpdatedCredential!)!.WebhookSecret;
        var secondSecret = JsonSerializer.Deserialize<TelegramCredential>(second.UpdatedCredential!)!.WebhookSecret;
        Assert.NotEqual(firstSecret, secondSecret);
    }

    // TelegramWebhookProvisioner.ProvisionAsync abre o HttpClient com
    // `using`, assumindo o contrato real de IHttpClientFactory.CreateClient()
    // (handler pooled, seguro de descartar a cada chamada). Um fake que
    // devolvesse sempre a MESMA instância de HttpClient quebraria essa
    // suposição na segunda chamada (ObjectDisposedException) — por isso
    // este fake devolve um HttpClient novo por chamada, todos compartilhando
    // o mesmo HttpMessageHandler (disposeHandler: false), mesmo padrão do
    // pool real.
    private sealed class SingleClientHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class JsonRespondingHttpMessageHandler(HttpStatusCode statusCode, object responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(responseBody) };
            return Task.FromResult(response);
        }
    }
}
