using Buteco.Inbox.Agents;
using Buteco.Inbox.Channels.Adapters;
using Buteco.Inbox.Channels.Adapters.Testing;
using Buteco.Inbox.Channels.Commands.CreateChannel;
using Buteco.Inbox.Channels.Security;
using Buteco.Inbox.Infrastructure;
using Buteco.Inbox.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;

namespace Buteco.Inbox.Tests;

public class CreateChannelCommandHandlerTests
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

    // Mock<IChannelAdapterRegistry> delegando para o próprio adapter de
    // teste registrado em Program.cs (design.md, Decision 6) — prova que
    // o handler chama o validador resolvido pelo registry, sem depender
    // de DI keyed real no teste unitário (design.md, Decision 5).
    private static IChannelAdapterRegistry CreateAdapterRegistry()
    {
        var registryMock = new Mock<IChannelAdapterRegistry>();
        registryMock.Setup(r => r.IsRegistered(ChannelType)).Returns(true);
        registryMock.Setup(r => r.GetConfigValidator(ChannelType)).Returns(new TestChannelConfigValidator());
        return registryMock.Object;
    }

    [Fact]
    public async Task Handle_AgentFound_PersistsChannelWithEncryptedCredentials()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var validatorMock = new Mock<IAgentReferenceValidator>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentReferenceValidationResult.Found);
        var cipher = CreateCipher();
        var handler = new CreateChannelCommandHandler(dbContext, cipher, validatorMock.Object, CreateAdapterRegistry(), CreatePublicUrlOptions());
        var agentId = Guid.NewGuid();

        var command = new CreateChannelCommand(ChannelType, "Canal de Suporte", "token-secreto", agentId);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(CreateChannelOutcome.Success, result.Outcome);
        var response = result.Channel!;
        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(agentId, response.AgentId);
        Assert.True(response.IsActive);

        var persisted = await dbContext.Channels.AsNoTracking().SingleAsync(channel => channel.Id == response.Id);
        Assert.Equal("token-secreto", cipher.Decrypt(persisted.EncryptedCredentials));
    }

    [Fact]
    public async Task Handle_AgentFound_ComputesWebhookUrlFromChannelIdAndPublicUrl()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var validatorMock = new Mock<IAgentReferenceValidator>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentReferenceValidationResult.Found);
        var handler = new CreateChannelCommandHandler(dbContext, CreateCipher(), validatorMock.Object, CreateAdapterRegistry(), CreatePublicUrlOptions());

        var command = new CreateChannelCommand(ChannelType, "Canal Com Webhook", "token-secreto", Guid.NewGuid());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(CreateChannelOutcome.Success, result.Outcome);
        Assert.Equal($"{PublicUrlBaseUrl}/webhooks/{result.Channel!.Id}", result.Channel!.WebhookUrl);
    }

    [Fact]
    public async Task Handle_AgentNotFound_DoesNotPersist()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var validatorMock = new Mock<IAgentReferenceValidator>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentReferenceValidationResult.NotFound);
        var handler = new CreateChannelCommandHandler(dbContext, CreateCipher(), validatorMock.Object, CreateAdapterRegistry(), CreatePublicUrlOptions());

        var command = new CreateChannelCommand(ChannelType, "Canal de Suporte", "token-secreto", Guid.NewGuid());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(CreateChannelOutcome.AgentNotFound, result.Outcome);
        Assert.Null(result.Channel);
        Assert.False(await dbContext.Channels.AnyAsync());
    }

    [Fact]
    public async Task Handle_AgentValidationCommunicationFailure_DoesNotPersist()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var validatorMock = new Mock<IAgentReferenceValidator>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentReferenceValidationResult.CommunicationFailure);
        var handler = new CreateChannelCommandHandler(dbContext, CreateCipher(), validatorMock.Object, CreateAdapterRegistry(), CreatePublicUrlOptions());

        var command = new CreateChannelCommand(ChannelType, "Canal de Vendas", "token-secreto", Guid.NewGuid());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(CreateChannelOutcome.AgentValidationFailed, result.Outcome);
        Assert.Null(result.Channel);
        Assert.False(await dbContext.Channels.AnyAsync());
    }

    [Fact]
    public async Task Handle_AgentFoundButInactive_StillPersistsChannel()
    {
        // isActive do agente não é verificado pelo validador — canal
        // vinculado a agente inativo é permitido de propósito (design.md,
        // Decision 7). O mock devolve "Found" independente de isActive,
        // exatamente como AgentReferenceValidator faz na integração real.
        await using var dbContext = CreateInMemoryDbContext();
        var validatorMock = new Mock<IAgentReferenceValidator>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentReferenceValidationResult.Found);
        var handler = new CreateChannelCommandHandler(dbContext, CreateCipher(), validatorMock.Object, CreateAdapterRegistry(), CreatePublicUrlOptions());

        var command = new CreateChannelCommand(ChannelType, "Canal Com Agente Inativo", "token-secreto", Guid.NewGuid());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(CreateChannelOutcome.Success, result.Outcome);
        Assert.True(await dbContext.Channels.AnyAsync(channel => channel.Id == result.Channel!.Id));
    }

    [Fact]
    public async Task Handle_CredentialRejectedByAdapterValidator_DoesNotPersist()
    {
        // Prova que o handler chama o validador registrado para o
        // ChannelType certo antes de cifrar (design.md, Decision 2) — não
        // uma validação hardcoded no handler.
        await using var dbContext = CreateInMemoryDbContext();
        var validatorMock = new Mock<IAgentReferenceValidator>();
        validatorMock.Setup(v => v.ValidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentReferenceValidationResult.Found);
        var handler = new CreateChannelCommandHandler(dbContext, CreateCipher(), validatorMock.Object, CreateAdapterRegistry(), CreatePublicUrlOptions());

        var command = new CreateChannelCommand(ChannelType, "Canal Com Credencial Inválida", TestChannelConfigValidator.RejectedCredential, Guid.NewGuid());
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(CreateChannelOutcome.InvalidCredential, result.Outcome);
        Assert.Null(result.Channel);
        Assert.NotNull(result.ValidationErrors);
        Assert.Contains("credential", result.ValidationErrors!.Keys);
        Assert.False(await dbContext.Channels.AnyAsync());
    }
}
