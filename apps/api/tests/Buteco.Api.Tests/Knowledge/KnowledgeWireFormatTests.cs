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
/// **Cobertura agora completa.** A etapa 1 conseguia afirmar só <c>Pending</c>,
/// porque os outros três valores nasceram sem escritor e não tinham como
/// aparecer numa resposta real; D10 daquela change pediu nominalmente que a
/// etapa de indexação estendesse este arquivo. É o que a change
/// knowledge-base-indexacao faz aqui: os quatro valores passam a ter escritor e
/// os quatro são afirmados.
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
            0,
            0,
            null,
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

    // Os QUATRO valores, e não só o que a etapa 1 conseguia produzir. Theory em
    // vez de quatro Facts porque o que se afirma é uma propriedade do tipo —
    // todo valor do enum atravessa como string —, e enumerar os casos aqui faz
    // um valor novo no enum, se algum dia houver, nascer sem cobertura visível.
    [Theory]
    [InlineData(KnowledgeIndexingStatus.Pending, "Pending")]
    [InlineData(KnowledgeIndexingStatus.Indexing, "Indexing")]
    [InlineData(KnowledgeIndexingStatus.Indexed, "Indexed")]
    [InlineData(KnowledgeIndexingStatus.Failed, "Failed")]
    public void EveryIndexingStatus_IsSerializedAsString_NotOrdinal(KnowledgeIndexingStatus status, string expected)
    {
        var json = SerializeSampleDocument(status);

        using var document = JsonDocument.Parse(json);
        var value = document.RootElement.GetProperty("indexingStatus");

        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.Equal(expected, value.GetString());
    }

    [Fact]
    public void IndexingFields_AreExposedOnTheWire()
    {
        var json = SerializeSampleDocument(KnowledgeIndexingStatus.Indexed);

        using var document = JsonDocument.Parse(json);

        foreach (var key in new[] { "fragmentCount", "indexingAttempts", "lastAttemptAt" })
        {
            Assert.True(
                document.RootElement.TryGetProperty(key, out _),
                $"chave '{key}' ausente no JSON: {json}");
        }
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
