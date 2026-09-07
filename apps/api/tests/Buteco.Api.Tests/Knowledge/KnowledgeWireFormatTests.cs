using System.Text.Json;
using Buteco.Api.KnowledgeDocuments.Entities;
using Buteco.Api.KnowledgeDocuments.Responses;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Inspeciona o TEXTO do JSON, não desserializa para o mesmo tipo: um teste de
/// round-trip passa pela mesma política de nomes e pelo mesmo conversor na ida
/// e na volta, e por isso é cego a enum saindo como inteiro ordinal — defeito
/// que já apareceu duas vezes nesta base (convenção 11/12).
///
/// **Cobertura parcial, e é de propósito:** nesta etapa só <c>Pending</c> tem
/// escritor, então só ele aparece numa resposta real. <c>Indexing</c>,
/// <c>Indexed</c> e <c>Failed</c> ficam verificáveis na etapa de indexação, e é
/// lá que este teste tem de ser estendido (design.md, D10). O conversor é
/// declarado no tipo, não por valor, então o risco residual é pequeno — mas a
/// cobertura desta change não é completa e está dito.
/// </summary>
public class KnowledgeWireFormatTests
{
    private static string SerializeSampleDocument(KnowledgeIndexingStatus status)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var response = new KnowledgeDocumentResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Política de trocas",
            "markdown",
            "# Política\n",
            12,
            status,
            null,
            null,
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        return JsonSerializer.Serialize(response, options);
    }

    [Fact]
    public void IndexingStatus_IsSerializedAsString_NotOrdinal()
    {
        var json = SerializeSampleDocument(KnowledgeIndexingStatus.Pending);

        using var document = JsonDocument.Parse(json);
        var status = document.RootElement.GetProperty("indexingStatus");

        Assert.Equal(JsonValueKind.String, status.ValueKind);
        Assert.Equal("Pending", status.GetString());
    }

    [Fact]
    public void SourceType_IsSerializedAsString()
    {
        var json = SerializeSampleDocument(KnowledgeIndexingStatus.Pending);

        using var document = JsonDocument.Parse(json);
        var sourceType = document.RootElement.GetProperty("sourceType");

        Assert.Equal(JsonValueKind.String, sourceType.ValueKind);
        Assert.Equal("markdown", sourceType.GetString());
    }

    [Fact]
    public void ContentLengthBytes_IsExposedUnderTheKeyThatNamesItsUnit()
    {
        var json = SerializeSampleDocument(KnowledgeIndexingStatus.Pending);

        using var document = JsonDocument.Parse(json);

        Assert.True(
            document.RootElement.TryGetProperty("contentLengthBytes", out _),
            $"chave 'contentLengthBytes' ausente no JSON: {json}");
        Assert.False(
            document.RootElement.TryGetProperty("contentLength", out _),
            "o nome sem unidade não pode voltar: a validação do teto é em bytes UTF-8");
    }
}
