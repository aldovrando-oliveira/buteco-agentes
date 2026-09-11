using Buteco.Workers.AgentDelegations;
using Buteco.Workers.Infrastructure;
using Buteco.Workers.Messaging;
using Buteco.Workers.Options;
using Buteco.Workers.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change apps-workers-delegacao-execucao, Decision 9: slug
/// determinístico e dedupe por sufixo numérico entre Targets com
/// <c>Agent.Name</c> colidente, mesmo espírito dos testes de slug/dedupe já
/// existentes para <c>AgentSkillMapper.Slugify</c> em <c>apps/api</c>.
/// Testes de <see cref="AgentDelegationToolSetResolver.ResolveAsync"/> aqui
/// não exercitam RabbitMQ/AgentExecutionService — só a montagem da lista de
/// tools, então usam um container de DI mínimo em vez do host completo.
/// </summary>
public class DelegationToolNameSlugifierTests(WorkerInfrastructureFixture fixture) : IClassFixture<WorkerInfrastructureFixture>
{
    [Theory]
    [InlineData("Atendimento", "atendimento")]
    [InlineData("Consulta CEP", "consulta-cep")]
    [InlineData("Suporte    Técnico!!", "suporte-tecnico")]
    [InlineData("---", "agent")]
    public void Slugify_ProducesExpectedSlug(string name, string expectedSlug)
    {
        Assert.Equal(expectedSlug, DelegationToolNameSlugifier.Slugify(name));
    }

    [Fact]
    public async Task ResolveAsync_TwoTargetsWithCollidingName_ProducesTheSameBaseName()
    {
        var sourceId = Guid.NewGuid();
        var targetAId = Guid.NewGuid();
        var targetBId = Guid.NewGuid();

        await SeedAgentAsync(sourceId, "Atendente Geral");
        await SeedAgentAsync(targetAId, "Atendimento");
        await SeedAgentAsync(targetBId, "Atendimento");
        await SeedAgentDelegationAsync(sourceId, targetAId);
        await SeedAgentDelegationAsync(sourceId, targetBId);

        var resolver = BuildResolver();
        await using var dbContext = CreateDbContext();
        var sourceAgent = await dbContext.Agents.AsNoTracking().FirstAsync(a => a.Id == sourceId);

        var tools = await resolver.ResolveAsync(dbContext, sourceAgent, Guid.NewGuid().ToString("N"), currentDepth: 0, messageInstant: null, CancellationToken.None);

        // Mudança de dono, não de comportamento: o dedupe por sufixo saiu deste
        // resolvedor e virou global (change dedupe-global-nome-de-tool,
        // Decisão 3), porque dentro dele a unicidade nunca cobria colisão com
        // uma tool MCP — e o sufixo local estourava os 64 caracteres. Aqui fica
        // o contrato do resolvedor: nome-base determinístico por Target. A
        // unicidade que o requisito "Nome estável e sem colisão para a tool de
        // delegação" promete é verificada no seam de produção, em
        // AgentToolNamespaceTests.
        Assert.Equal(2, tools.Count);
        var names = tools.Select(t => t.Name).ToList();
        Assert.All(names, name => Assert.Equal("delegate_to_atendimento", name));
    }

    [Fact]
    public async Task ResolveAsync_SameDelegationSet_ProducesSameToolNames_AcrossExecutions()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        await SeedAgentAsync(sourceId, "Atendente Geral");
        await SeedAgentAsync(targetId, "Financeiro");
        await SeedAgentDelegationAsync(sourceId, targetId);

        var resolver = BuildResolver();
        await using var dbContext = CreateDbContext();
        var sourceAgent = await dbContext.Agents.AsNoTracking().FirstAsync(a => a.Id == sourceId);
        var contextId = Guid.NewGuid().ToString("N");

        var first = await resolver.ResolveAsync(dbContext, sourceAgent, contextId, currentDepth: 0, messageInstant: null, CancellationToken.None);
        var second = await resolver.ResolveAsync(dbContext, sourceAgent, contextId, currentDepth: 0, messageInstant: null, CancellationToken.None);

        Assert.Equal(first.Select(t => t.Name), second.Select(t => t.Name));
    }

    [Fact]
    public async Task ResolveAsync_SourceWithoutAnyDelegation_ReturnsEmptyList()
    {
        var sourceId = Guid.NewGuid();
        await SeedAgentAsync(sourceId, "Atendente Solo");

        var resolver = BuildResolver();
        await using var dbContext = CreateDbContext();
        var sourceAgent = await dbContext.Agents.AsNoTracking().FirstAsync(a => a.Id == sourceId);

        var tools = await resolver.ResolveAsync(dbContext, sourceAgent, Guid.NewGuid().ToString("N"), currentDepth: 0, messageInstant: null, CancellationToken.None);

        Assert.Empty(tools);
    }

    private AgentDelegationToolSetResolver BuildResolver()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()));
        var provider = services.BuildServiceProvider();

        return new AgentDelegationToolSetResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Mock.Of<ITaskJobPublisher>(),
            Microsoft.Extensions.Options.Options.Create(new AgentDelegationToolOptions()),
            NullLogger<AgentDelegationToolSetResolver>.Instance);
    }

    private AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(fixture.Postgres.GetConnectionString()).Options);

    private async Task SeedAgentAsync(Guid agentId, string name, string provider = "openai", string model = "gpt-5.6-sol")
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
             VALUES ({agentId}, {name}, {"Instruções de teste."}, {true}, {provider}, {model}, {now}, {now})
             """);
    }

    private async Task SeedAgentDelegationAsync(Guid sourceAgentId, Guid targetAgentId)
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO agent_delegations ("SourceAgentId", "TargetAgentId")
             VALUES ({sourceAgentId}, {targetAgentId})
             """);
    }
}
