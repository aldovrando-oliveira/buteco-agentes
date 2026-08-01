using Buteco.Api.A2A;
using Buteco.Api.Agents.Commands.CreateAgent;
using Buteco.Api.Infrastructure;
using Buteco.Api.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace Buteco.Api.Tests;

public class CreateAgentCommandHandlerTests
{
    private const string Provider = "openai";
    private const string Model = "gpt-5.6-sol";

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static ProviderCatalogService CreateProviderCatalogService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ChatClient:ApiKey"] = "changeme" })
            .Build();

        return new ProviderCatalogService(configuration);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsAgentAndRegistersA2AServer()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var registryMock = new Mock<IAgentA2AServerRegistry>();
        var handler = new CreateAgentCommandHandler(dbContext, registryMock.Object, CreateProviderCatalogService());

        var command = new CreateAgentCommand("Atendente", "Você é um atendente simpático.", Provider, Model);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(ProviderValidationOutcome.Valid, result.Validation);
        var response = result.Agent!;
        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(command.Name, response.Name);
        Assert.Equal(command.Instructions, response.Instructions);

        var persisted = await dbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == response.Id);
        Assert.Equal(command.Name, persisted.Name);
        Assert.Equal(command.Instructions, persisted.Instructions);
        Assert.Equal(Provider, persisted.Provider);
        Assert.Equal(Model, persisted.Model);

        registryMock.Verify(registry => registry.Register(response.Id), Times.Once);
    }

    [Fact]
    public async Task Handle_ProviderNotConfigured_DoesNotPersistOrRegister()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var registryMock = new Mock<IAgentA2AServerRegistry>();
        var handler = new CreateAgentCommandHandler(dbContext, registryMock.Object, CreateProviderCatalogService());

        var command = new CreateAgentCommand("Atendente", "Você é um atendente simpático.", "anthropic", "claude-opus-5");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(ProviderValidationOutcome.ProviderNotConfigured, result.Validation);
        Assert.Null(result.Agent);
        Assert.False(await dbContext.Agents.AnyAsync());
        registryMock.Verify(registry => registry.Register(It.IsAny<Guid>()), Times.Never);
    }
}
