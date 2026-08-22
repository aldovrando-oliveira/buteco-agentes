using System.Text;
using System.Text.Json;
using Buteco.Inbox.Channels.Adapters.Telegram;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Messages.Entities;
using Buteco.Inbox.Orchestration;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Buteco.Inbox.Tests;

public class TelegramInboundWebhookHandlerTests
{
    private const string CorrectSecret = "secret-correto";

    private static IChannelCredentialCipher CreateCipher() =>
        new AesGcmChannelCredentialCipher(Microsoft.Extensions.Options.Options.Create(
            new InboxCryptoOptions { CredentialEncryptionKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=" }));

    private static ServiceProvider BuildProvider(Mock<IInboundMessageOrchestrator> orchestratorMock)
    {
        // Nome do banco capturado antes do registro, não gerado dentro do
        // lambda — AddDbContext reavalia o lambda a cada resolução de
        // escopo; gerar o Guid ali dentro daria um banco em memória novo e
        // vazio a cada escopo (SeedChannelAsync e o escopo aberto por
        // TelegramInboundWebhookHandler nunca veriam os mesmos dados).
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddSingleton(orchestratorMock.Object);
        return services.BuildServiceProvider();
    }

    private static async Task<Channel> SeedChannelAsync(
        ServiceProvider provider, IChannelCredentialCipher cipher, bool isActive = true, string? webhookSecret = CorrectSecret)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var credential = JsonSerializer.Serialize(new TelegramCredential("123456:ABC-DEF", webhookSecret));
        var channel = new Channel("telegram", "Canal Telegram", cipher.Encrypt(credential), Guid.NewGuid());
        if (!isActive)
        {
            channel.Deactivate();
        }

        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel;
    }

    private static HttpRequest BuildRequest(string json, string? secretToken)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        if (secretToken is not null)
        {
            context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"] = secretToken;
        }

        return context.Request;
    }

    private const string MessageUpdateJson = """
        {
          "update_id": 123456789,
          "message": {
            "message_id": 42,
            "date": 1700000000,
            "chat": { "id": 987654321, "type": "private" },
            "from": { "id": 111, "is_bot": false, "first_name": "Ana", "username": "ana_silva" },
            "text": "Olá, preciso de ajuda"
          }
        }
        """;

    private static void AssertOrchestratorNeverCalled(Mock<IInboundMessageOrchestrator> orchestratorMock) =>
        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageContentType>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task HandleAsync_UnknownChannelId_Returns404AndDoesNotInvokeOrchestrator()
    {
        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), CreateCipher());

        var request = BuildRequest(MessageUpdateJson, CorrectSecret);
        await handler.HandleAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, request.HttpContext.Response.StatusCode);
        AssertOrchestratorNeverCalled(orchestratorMock);
    }

    [Fact]
    public async Task HandleAsync_InactiveChannel_Returns401EvenWithCorrectSecretAndDoesNotInvokeOrchestrator()
    {
        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher, isActive: false);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(MessageUpdateJson, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, request.HttpContext.Response.StatusCode);
        AssertOrchestratorNeverCalled(orchestratorMock);
    }

    [Fact]
    public async Task HandleAsync_MissingSecretHeader_Returns401AndDoesNotInvokeOrchestrator()
    {
        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(MessageUpdateJson, secretToken: null);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, request.HttpContext.Response.StatusCode);
        AssertOrchestratorNeverCalled(orchestratorMock);
    }

    [Fact]
    public async Task HandleAsync_SecretHeaderDivergesFromPersisted_Returns401AndDoesNotInvokeOrchestrator()
    {
        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(MessageUpdateJson, secretToken: "secret-errado");
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, request.HttpContext.Response.StatusCode);
        AssertOrchestratorNeverCalled(orchestratorMock);
    }

    [Fact]
    public async Task HandleAsync_CorrectSecretHeader_ProcessesNormally()
    {
        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(MessageUpdateJson, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, request.HttpContext.Response.StatusCode);
        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            channel.Id, "987654321", "Olá, preciso de ajuda", MessageContentType.Text, "42", "ana_silva", It.IsAny<DateTimeOffset>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UpdateWithoutMessageField_IsIgnoredWithoutInvokingOrchestrator()
    {
        const string json = """
            {
              "update_id": 123456789,
              "callback_query": { "id": "abc", "data": "algum-callback" }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(json, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        AssertOrchestratorNeverCalled(orchestratorMock);
    }

    [Fact]
    public async Task HandleAsync_MessageWithoutTextOrMedia_IsIgnoredWithoutInvokingOrchestrator()
    {
        const string json = """
            {
              "update_id": 123456789,
              "message": {
                "message_id": 42,
                "date": 1700000000,
                "chat": { "id": 987654321, "type": "private" },
                "from": { "id": 111, "is_bot": false, "first_name": "Ana" }
              }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(json, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        AssertOrchestratorNeverCalled(orchestratorMock);
    }

    [Fact]
    public async Task HandleAsync_PhotoWithoutCaption_InvokesOrchestratorWithMarkerAndImageContentType()
    {
        const string json = """
            {
              "update_id": 123456789,
              "message": {
                "message_id": 43,
                "date": 1700000000,
                "chat": { "id": 987654321, "type": "private" },
                "from": { "id": 111, "is_bot": false, "first_name": "Ana" },
                "photo": [{ "file_id": "abc", "width": 90, "height": 90 }]
              }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(json, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            channel.Id, "987654321", "[mídia: image]", MessageContentType.Image, "43", "Ana", It.IsAny<DateTimeOffset>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DocumentWithCaption_InvokesOrchestratorWithCaptionAndDocumentContentType()
    {
        const string json = """
            {
              "update_id": 123456789,
              "message": {
                "message_id": 44,
                "date": 1700000000,
                "chat": { "id": 987654321, "type": "private" },
                "from": { "id": 111, "is_bot": false, "first_name": "Ana" },
                "caption": "Segue o comprovante",
                "document": { "file_id": "doc123", "file_name": "comprovante.pdf" }
              }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(json, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            channel.Id, "987654321", "Segue o comprovante", MessageContentType.Document, "44", "Ana", It.IsAny<DateTimeOffset>(),
            It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExternalIdIsExactlyChatIdConvertedToString()
    {
        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(MessageUpdateJson, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            It.IsAny<Guid>(), "987654321", It.IsAny<string>(), It.IsAny<MessageContentType>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyDictionary<string, string>>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MetadataIsExactlyUsernameAndFirstNameFromFixture()
    {
        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(MessageUpdateJson, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageContentType>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<DateTimeOffset>(),
            It.Is<IReadOnlyDictionary<string, string>>(metadata =>
                metadata.Count == 2 && metadata["username"] == "ana_silva" && metadata["firstName"] == "Ana"),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_MetadataOmitsAbsentFields()
    {
        const string json = """
            {
              "update_id": 123456789,
              "message": {
                "message_id": 42,
                "date": 1700000000,
                "chat": { "id": 987654321, "type": "private" },
                "from": { "id": 111, "is_bot": false, "first_name": "Ana" },
                "text": "Sem username"
              }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(json, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageContentType>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<DateTimeOffset>(),
            It.Is<IReadOnlyDictionary<string, string>>(metadata =>
                metadata.Count == 1 && metadata["firstName"] == "Ana" && !metadata.ContainsKey("username")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_DisplayNameFallsBackToFirstNameWhenNoUsername()
    {
        const string json = """
            {
              "update_id": 123456789,
              "message": {
                "message_id": 45,
                "date": 1700000000,
                "chat": { "id": 987654321, "type": "private" },
                "from": { "id": 111, "is_bot": false, "first_name": "Ana" },
                "text": "Sem username"
              }
            }
            """;

        var orchestratorMock = new Mock<IInboundMessageOrchestrator>();
        var provider = BuildProvider(orchestratorMock);
        var cipher = CreateCipher();
        var channel = await SeedChannelAsync(provider, cipher);
        var handler = new TelegramInboundWebhookHandler(provider.GetRequiredService<IServiceScopeFactory>(), cipher);

        var request = BuildRequest(json, CorrectSecret);
        await handler.HandleAsync(channel.Id, request, CancellationToken.None);

        orchestratorMock.Verify(o => o.ReceiveMessageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageContentType>(), It.IsAny<string>(),
            "Ana", It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
