using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Buteco.Api.Tests.Providers;

/// <summary>
/// R1 de ponta a ponta (design.md da change modelos-curados-por-provedor, D3; spec
/// <c>a2a-task-lifecycle</c>, "Agente que executava antes do deploy continua aceitando
/// tarefas"): o banco é migrado até a migration ANTERIOR à da lista curada, um agente é gravado
/// por SQL com um par fora dos nove do catálogo estático (a edição manual), e só então a
/// migration nova roda, como no <c>migrator</c> do deploy. O <c>SendMessage</c> para ele é
/// aceito e publica o job.
/// </summary>
public class AgentBeforeCurationMigrationTests(AgentBeforeCurationMigrationTests.Fixture factory) : IClassFixture<AgentBeforeCurationMigrationTests.Fixture>
{
    private const string PreviousMigrationId = "20261113033350_AddUserFederatedIdentities";

    public sealed class Fixture : ApiFactoryFixture
    {
        public FakeTaskJobPublisher TaskJobPublisher { get; } = new();

        protected override bool MigratesOnInitialize => false;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ITaskJobPublisher>();
                services.AddSingleton<ITaskJobPublisher>(TaskJobPublisher);
            });
        }
    }

    [Fact]
    public async Task AnAgentWithAPairOutsideTheNine_RecordedBeforeTheMigration_IsAcceptedAfterIt()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseButecoAgentsNpgsql(factory.PostgresConnectionString).Options;
        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.GetService<IMigrator>().MigrateAsync(PreviousMigrationId, CancellationToken.None);
        }

        var agentId = Guid.NewGuid();
        await using (var dbContext = new AppDbContext(options))
        {
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO agents ("Id", "Name", "Instructions", "IsActive", "Provider", "Model", "CreatedAt", "UpdatedAt")
                VALUES ({agentId}, 'Agente de antes', 'Instruções.', true, 'openai', 'modelo-editado-por-sql', now(), now())
                """);
        }

        await factory.MigrateAsync();
        await factory.Services.GetRequiredService<IdentityKeyRing>().EnsureLoadedAsync(CancellationToken.None);

        var inbox = TestAuthentication.InboxServiceClient(factory);
        var payload = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "SendMessage",
            @params = new { message = new { role = "ROLE_USER", parts = new[] { new { text = "Olá" } }, messageId = Guid.NewGuid().ToString("N") } },
        };
        var response = await inbox.PostAsJsonAsync($"/agents/{agentId}/a2a", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var task = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("task");
        Assert.Equal("TASK_STATE_SUBMITTED", task.GetProperty("status").GetProperty("state").GetString());
        var taskId = task.GetProperty("id").GetString();
        Assert.Contains(factory.TaskJobPublisher.PublishedMessages, message => message.TaskId == taskId);
    }
}
