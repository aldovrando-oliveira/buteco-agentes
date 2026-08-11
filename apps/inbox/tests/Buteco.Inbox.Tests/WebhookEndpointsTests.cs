using System.Net;
using System.Text;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Testing;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Inbox.Tests;

public class WebhookEndpointsTests(InboxFactoryFixture factory) : IClassFixture<InboxFactoryFixture>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ReceiveAsync_UnknownChannelId_ReturnsNotFound()
    {
        var response = await PostAsync($"/webhooks/{Guid.NewGuid()}", "{}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReceiveAsync_ChannelTypeWithoutRegisteredHandler_ReturnsBadRequest()
    {
        // Canal inserido direto no banco, com um ChannelType sem nenhum
        // adapter registrado — não alcançável via POST /channels (que
        // rejeitaria esse channelType), só pra provar o caminho defensivo
        // de WebhookEndpoints.ReceiveAsync (design.md, Decision 1).
        var channelId = await CreateChannelAsync("channel-type-sem-adapter");

        var response = await PostAsync($"/webhooks/{channelId}", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReceiveAsync_RegisteredChannelType_DispatchesToHandlerOfThatType()
    {
        var channelId = await CreateChannelAsync("test-channel");
        var handler = (TestInboundWebhookHandler)factory.Services.GetRequiredKeyedService<IInboundWebhookHandler>("test-channel");

        var response = await PostAsync($"/webhooks/{channelId}", "{}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(channelId, handler.HandledChannelIds);
    }

    private Task<HttpResponseMessage> PostAsync(string path, string json) =>
        _client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<Guid> CreateChannelAsync(string channelType)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var channel = new Channel(channelType, $"Canal {Guid.NewGuid()}", "irrelevante-nesta-fatia", Guid.NewGuid());
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel.Id;
    }
}
