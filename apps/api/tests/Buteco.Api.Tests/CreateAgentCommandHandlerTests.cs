using Buteco.Api.A2A;
using Buteco.Api.Agents.Commands.CreateAgent;
using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Buteco.Api.Tests;

public class CreateAgentCommandHandlerTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsAgentAndRegistersA2AServer()
    {
        await using var dbContext = CreateInMemoryDbContext();
        var registryMock = new Mock<IAgentA2AServerRegistry>();
        var handler = new CreateAgentCommandHandler(dbContext, registryMock.Object);

        var command = new CreateAgentCommand("Atendente", "Você é um atendente simpático.");

        var response = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(command.Name, response.Name);
        Assert.Equal(command.Instructions, response.Instructions);

        var persisted = await dbContext.Agents.AsNoTracking().SingleAsync(agent => agent.Id == response.Id);
        Assert.Equal(command.Name, persisted.Name);
        Assert.Equal(command.Instructions, persisted.Instructions);

        registryMock.Verify(registry => registry.Register(response.Id), Times.Once);
    }
}
