using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Knowledge.Indexing;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Buteco.Api.Tests;

/// <summary>
/// A publicação de indexação com confirmação do broker, contra o RabbitMQ real (design.md
/// da change indexacao-sem-job-orfao, D5 e R5). <c>partial</c> desta classe porque a
/// fixture dela é a única do <c>apps/api</c> com RabbitMQ e com o publisher real de
/// indexação: nenhuma fonte de contêiner nova.
/// </summary>
public partial class A2ATaskLifecycleTests
{
    [Fact]
    public async Task DocumentCreated_ReachesTheIndexingQueue_ThroughTheConfirmedPublisher_AndTheRequestIsRemoved()
    {
        var baseResponse = await _client.PostAsJsonAsync(
            "/knowledge-bases", new CreateKnowledgeBaseRequest("Base com broker real", "Descrição."));
        baseResponse.EnsureSuccessStatusCode();
        var knowledgeBase = (await baseResponse.Content.ReadFromJsonAsync<KnowledgeBaseResponse>())!;

        var documentResponse = await _client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBase.Id}/documents",
            new CreateKnowledgeDocumentRequest("Doc com broker real", "markdown", "# Broker real\n\nTexto.\n"));
        documentResponse.EnsureSuccessStatusCode();
        var document = (await documentResponse.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;

        // A fixture não substitui o publisher de indexação: é o RabbitMqKnowledgeIndexingJobPublisher
        // com confirmação, e o pedido só sai da tabela depois do ack.
        Assert.IsType<RabbitMqKnowledgeIndexingJobPublisher>(fixture.Services.GetRequiredService<IKnowledgeIndexingJobPublisher>());

        var message = await ReadIndexingMessageForAsync(document.Id);
        Assert.NotNull(message);
        Assert.Equal(1, message!.ContentRevision);
        Assert.Equal(1, message.Attempt);

        using var scope = fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.KnowledgeIndexingRequests.AnyAsync(request => request.KnowledgeDocumentId == document.Id));
    }

    private async Task<KnowledgeIndexingJobMessage?> ReadIndexingMessageForAsync(Guid documentId)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMq.Hostname,
            Port = fixture.RabbitMq.GetMappedPublicPort(5672),
            UserName = "buteco",
            Password = "buteco_test_password",
        };

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await channel.BasicGetAsync(KnowledgeIndexingQueues.Main, autoAck: true);
            if (result is null)
            {
                await Task.Delay(200);
                continue;
            }

            var message = JsonSerializer.Deserialize<KnowledgeIndexingJobMessage>(result.Body.Span);
            if (message?.KnowledgeDocumentId == documentId)
            {
                return message;
            }
        }

        return null;
    }
}
