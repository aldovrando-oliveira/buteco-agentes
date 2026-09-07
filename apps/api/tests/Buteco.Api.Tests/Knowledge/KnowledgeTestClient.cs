using System.Net.Http.Json;
using Buteco.Api.KnowledgeBases.Requests;
using Buteco.Api.KnowledgeBases.Responses;
using Buteco.Api.KnowledgeDocuments.Requests;
using Buteco.Api.KnowledgeDocuments.Responses;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Atalhos de arranjo compartilhados pelas classes de teste de conhecimento.
/// Só monta pré-condição — nenhuma asserção mora aqui.
/// </summary>
internal static class KnowledgeTestClient
{
    public const string SampleMarkdown = "# Política de trocas\n\nTrocas em até 30 dias.\n";

    public static async Task<KnowledgeBaseResponse> CreateBaseAsync(
        this HttpClient client, string name = "Base", string description = "Descrição da base.")
    {
        var response = await client.PostAsJsonAsync("/knowledge-bases", new CreateKnowledgeBaseRequest(name, description));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeBaseResponse>())!;
    }

    public static async Task<KnowledgeDocumentResponse> CreateDocumentAsync(
        this HttpClient client, Guid knowledgeBaseId, string title = "Documento", string? content = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/knowledge-bases/{knowledgeBaseId}/documents",
            new CreateKnowledgeDocumentRequest(title, "markdown", content ?? SampleMarkdown));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeDocumentResponse>())!;
    }
}
