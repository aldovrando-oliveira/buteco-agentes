using Buteco.Inbox.Agents;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Testing;
using Buteco.Inbox.Channels.Commands.UpdateChannel;
using Buteco.Inbox.Channels.Entities;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Buteco.Inbox.Tests;

public class UpdateChannelCommandHandlerTests
{
    private const string ChannelType = "test-channel";
    private const string PublicUrlBaseUrl = "http://localhost:5027";

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static IChannelCredentialCipher CreateCipher() =>
        new AesGcmChannelCredentialCipher(Microsoft.Extensions.Options.Options.Create(
            new InboxCryptoOptions { CredentialEncryptionKey = "NtxqjqnKG3sqy52PFRh/SGk573bsE9TrDtKOsDiR8uc=" }));

    private static IOptions<PublicUrlOptions> CreatePublicUrlOptions() =>
        Microsoft.Extensions.Options.Options.Create(new PublicUrlOptions { BaseUrl = PublicUrlBaseUrl });

    private static Mock<IChannelAdapterRegistry> CreateAdapterRegistryMock()
    {
        var registryMock = new Mock<IChannelAdapterRegistry>();
        registryMock.Setup(r => r.GetConfigValidator(ChannelType)).Returns(new TestChannelConfigValidator());
        return registryMock;
    }

    private static async Task<Channel> SeedChannelAsync(AppDbContext dbContext, IChannelCredentialCipher cipher, Guid agentId, string credential = "credencial-original")
    {
        var channel = new Channel(ChannelType, "Canal Original", cipher.Encrypt(credential), agentId);
        dbContext.Channels.Add(channel);
        await dbContext.SaveChangesAsync();
        return channel;
    }

    [Fact]
    public async Task Handle_WithoutCredential_DoesNotCallValidator()
    {
        // Prova que a validação do adapter só roda quando Credential é
        // informado — mesma condição já usada para decidir se
        // recriptografa (design.md, Decision 2).
        await using var dbContext = CreateInMemoryDbContext();
        var cipher = CreateCipher();
        var agentId = Guid.NewGuid();
        var channel = await SeedChannelAsync(dbContext, cipher, agentId);

        var agentValidatorMock = new Mock<IAgentReferenceValidator>();
        var adapterRegistryMock = CreateAdapterRegistryMock();
        var handler = new UpdateChannelCommandHandler(dbContext, cipher, agentValidatorMock.Object, adapterRegistryMock.Object, CreatePublicUrlOptions());

        var command = new UpdateChannelCommand(channel.Id, "Canal Renomeado", null, agentId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(UpdateChannelOutcome.Success, result.Outcome);
        adapterRegistryMock.Verify(r => r.GetConfigValidator(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithCredentialRejectedByAdapterValidator_DoesNotUpdateCredential()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var cipher = CreateCipher();
        var agentId = Guid.NewGuid();
        var channel = await SeedChannelAsync(dbContext, cipher, agentId, credential: "credencial-original");

        var agentValidatorMock = new Mock<IAgentReferenceValidator>();
        var handler = new UpdateChannelCommandHandler(dbContext, cipher, agentValidatorMock.Object, CreateAdapterRegistryMock().Object, CreatePublicUrlOptions());

        var command = new UpdateChannelCommand(channel.Id, "Canal Renomeado", TestChannelConfigValidator.RejectedCredential, agentId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(UpdateChannelOutcome.InvalidCredential, result.Outcome);
        Assert.Null(result.Channel);
        Assert.NotNull(result.ValidationErrors);

        var persisted = await dbContext.Channels.AsNoTracking().SingleAsync(c => c.Id == channel.Id);
        Assert.Equal("credencial-original", cipher.Decrypt(persisted.EncryptedCredentials));
    }

    [Fact]
    public async Task Handle_WithValidNewCredential_ReplacesCredential()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var cipher = CreateCipher();
        var agentId = Guid.NewGuid();
        var channel = await SeedChannelAsync(dbContext, cipher, agentId, credential: "credencial-velha");

        var agentValidatorMock = new Mock<IAgentReferenceValidator>();
        var handler = new UpdateChannelCommandHandler(dbContext, cipher, agentValidatorMock.Object, CreateAdapterRegistryMock().Object, CreatePublicUrlOptions());

        var command = new UpdateChannelCommand(channel.Id, "Canal Renomeado", "credencial-nova", agentId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(UpdateChannelOutcome.Success, result.Outcome);
        var persisted = await dbContext.Channels.AsNoTracking().SingleAsync(c => c.Id == channel.Id);
        Assert.Equal("credencial-nova", cipher.Decrypt(persisted.EncryptedCredentials));
    }
}
