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

    // GetWebhookProvisioner não é configurado por padrão — Moq devolve
    // null, mesmo comportamento de um ChannelType sem provisionador
    // registrado (inbox-adapter-telegram, design.md, Decision 1).
    private static Mock<IChannelAdapterRegistry> CreateAdapterRegistryMock(Mock<IChannelWebhookProvisioner>? provisionerMock = null)
    {
        var registryMock = new Mock<IChannelAdapterRegistry>();
        registryMock.Setup(r => r.GetConfigValidator(ChannelType)).Returns(new TestChannelConfigValidator());
        if (provisionerMock is not null)
        {
            registryMock.Setup(r => r.GetWebhookProvisioner(ChannelType)).Returns(provisionerMock.Object);
        }

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

    // Quarto contrato, opcional (inbox-adapter-telegram, design.md,
    // Decision 6) — os quatro testes abaixo cobrem o reprovisionamento na
    // troca de credencial.
    [Fact]
    public async Task Handle_WithoutCredential_DoesNotInvokeWebhookProvisioner()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var cipher = CreateCipher();
        var agentId = Guid.NewGuid();
        var channel = await SeedChannelAsync(dbContext, cipher, agentId);

        var provisionerMock = new Mock<IChannelWebhookProvisioner>();
        var agentValidatorMock = new Mock<IAgentReferenceValidator>();
        var adapterRegistryMock = CreateAdapterRegistryMock(provisionerMock);
        var handler = new UpdateChannelCommandHandler(dbContext, cipher, agentValidatorMock.Object, adapterRegistryMock.Object, CreatePublicUrlOptions());

        var command = new UpdateChannelCommand(channel.Id, "Canal Renomeado", null, agentId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(UpdateChannelOutcome.Success, result.Outcome);
        adapterRegistryMock.Verify(r => r.GetWebhookProvisioner(It.IsAny<string>()), Times.Never);
        provisionerMock.Verify(p => p.ProvisionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_CredentialChangedWithProvisioner_InvokesProvisionAsyncWithNewSecretEachTime()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var cipher = CreateCipher();
        var agentId = Guid.NewGuid();
        var channel = await SeedChannelAsync(dbContext, cipher, agentId, credential: "credencial-velha");

        var secretsSeen = new List<string>();
        var provisionerMock = new Mock<IChannelWebhookProvisioner>();
        provisionerMock
            .Setup(p => p.ProvisionAsync(channel.Id, It.IsAny<string>(), $"{PublicUrlBaseUrl}/webhooks/{channel.Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, CancellationToken _) =>
            {
                var secret = Guid.NewGuid().ToString("N");
                secretsSeen.Add(secret);
                return ChannelWebhookProvisioningResult.Succeeded($"credencial-provisionada-{secret}");
            });

        var agentValidatorMock = new Mock<IAgentReferenceValidator>();
        var handler = new UpdateChannelCommandHandler(dbContext, cipher, agentValidatorMock.Object, CreateAdapterRegistryMock(provisionerMock).Object, CreatePublicUrlOptions());

        var firstResult = await handler.Handle(new UpdateChannelCommand(channel.Id, "Canal Renomeado", "credencial-nova-1", agentId), CancellationToken.None);
        var secondResult = await handler.Handle(new UpdateChannelCommand(channel.Id, "Canal Renomeado", "credencial-nova-2", agentId), CancellationToken.None);

        Assert.Equal(UpdateChannelOutcome.Success, firstResult.Outcome);
        Assert.Equal(UpdateChannelOutcome.Success, secondResult.Outcome);
        Assert.Equal(2, secretsSeen.Count);
        Assert.NotEqual(secretsSeen[0], secretsSeen[1]);

        var persisted = await dbContext.Channels.AsNoTracking().SingleAsync(c => c.Id == channel.Id);
        Assert.Equal($"credencial-provisionada-{secretsSeen[1]}", cipher.Decrypt(persisted.EncryptedCredentials));
    }

    [Fact]
    public async Task Handle_ReprovisioningFails_KeepsPreviouslyPersistedCredential()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var cipher = CreateCipher();
        var agentId = Guid.NewGuid();
        var channel = await SeedChannelAsync(dbContext, cipher, agentId, credential: "credencial-velha");

        var provisionerMock = new Mock<IChannelWebhookProvisioner>();
        provisionerMock
            .Setup(p => p.ProvisionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChannelWebhookProvisioningResult.Failed("token inválido"));

        var agentValidatorMock = new Mock<IAgentReferenceValidator>();
        var handler = new UpdateChannelCommandHandler(dbContext, cipher, agentValidatorMock.Object, CreateAdapterRegistryMock(provisionerMock).Object, CreatePublicUrlOptions());

        var command = new UpdateChannelCommand(channel.Id, "Canal Renomeado", "credencial-nova", agentId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(UpdateChannelOutcome.ProvisioningFailed, result.Outcome);
        Assert.Null(result.Channel);
        Assert.Equal("token inválido", result.ProvisioningError);

        var persisted = await dbContext.Channels.AsNoTracking().SingleAsync(c => c.Id == channel.Id);
        Assert.Equal("credencial-velha", cipher.Decrypt(persisted.EncryptedCredentials));
    }
}
