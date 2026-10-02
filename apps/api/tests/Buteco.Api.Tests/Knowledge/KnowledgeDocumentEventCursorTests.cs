using System.Buffers.Binary;
using System.Buffers.Text;
using Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocumentEvents;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Os ramos de recusa de <see cref="KnowledgeDocumentEventCursor.TryDecode"/>,
/// sem banco nem host. Cada caso afirma, além do <c>false</c>, <b>qual</b> ramo
/// ele atinge — direto contra a BCL —, porque três entradas que caem no mesmo
/// ramo dão a impressão de cobrir três defeitos e cobrem um.
///
/// <para>
/// Os valores de entrada são os mesmos do cenário HTTP
/// <c>KnowledgeDocumentCatalogTests.DocumentEvents_WithMalformedCursor_ReturnValidationProblemOnCursor</c>,
/// e é aqui que se confere que eles exercitam o ramo que o comentário de lá diz.
/// </para>
/// </summary>
public class KnowledgeDocumentEventCursorTests
{
    // --- ramo da captura: TryDecodeFromChars LANÇA, apesar do "Try" ----------

    [Theory]
    [InlineData("abc$")]  // caractere fora do alfabeto base64url
    [InlineData("abcde")] // comprimento com resto 1 por 4, que base64url não representa
    [InlineData("nao-e-um-cursor")] // 15 caracteres: os 2 bits finais de "r" não são zero, forma não canônica
    public void InputRejectedByTheDecoder_ThrowsInTheBcl_AndIsRefusedByTheCursor(string value)
    {
        Assert.Throws<FormatException>(() => Base64Url.TryDecodeFromChars(value, new byte[64], out _));

        Assert.False(KnowledgeDocumentEventCursor.TryDecode(value, out _));
    }

    // --- ramo do tamanho: decodifica, mas não para 24 bytes ------------------

    [Theory]
    [InlineData("nao-e-um-cursoo", 11)] // como "nao-e-um-cursor", mas com "o", de bits finais zero
    [InlineData("AAAA", 3)]
    [InlineData("", 0)]
    public void InputThatDecodesToTheWrongLength_IsRefusedByTheCursor(string value, int decodedLength)
    {
        Assert.Equal(decodedLength, Base64Url.DecodeFromChars(value).Length);

        Assert.False(KnowledgeDocumentEventCursor.TryDecode(value, out _));
    }

    // --- ramo da faixa: 24 bytes, ticks fora de DateTimeOffset ----------------

    /// <summary>
    /// A conferência pedida antes de adotar os dois valores no cenário HTTP: eles
    /// decodificam para exatamente 24 bytes (passam pelo ramo do tamanho) e os
    /// ticks nos 8 primeiros, em big-endian, são os informados — um abaixo e um
    /// acima da faixa de <see cref="DateTimeOffset"/>. Sem esta conferência, um
    /// valor calculado errado cairia no ramo do tamanho e o cenário passaria
    /// verde sem nunca tocar a faixa.
    /// </summary>
    [Fact]
    public void RangeCases_DecodeTo24Bytes_WithTheStatedTicks()
    {
        var belowRange = Base64Url.DecodeFromChars("__________8RERERIiIzM0REVVVVVVVV");
        var aboveRange = Base64Url.DecodeFromChars("K8oodfQ3QAARERERIiIzM0REVVVVVVVV");

        Assert.Equal(24, belowRange.Length);
        Assert.Equal(24, aboveRange.Length);
        Assert.Equal(-1L, BinaryPrimitives.ReadInt64BigEndian(belowRange.AsSpan(0, 8)));
        Assert.Equal(DateTimeOffset.MaxValue.UtcTicks + 1, BinaryPrimitives.ReadInt64BigEndian(aboveRange.AsSpan(0, 8)));
    }

    [Theory]
    [InlineData("__________8RERERIiIzM0REVVVVVVVV")] // ticks = -1
    [InlineData("K8oodfQ3QAARERERIiIzM0REVVVVVVVV")] // ticks = DateTimeOffset.MaxValue.UtcTicks + 1
    public void TicksOutsideTheDateTimeOffsetRange_AreRefusedByTheCursor(string value)
    {
        Assert.False(KnowledgeDocumentEventCursor.TryDecode(value, out _));
    }

    // --- o caminho feliz, para os ramos acima não passarem por recusar tudo ---

    [Fact]
    public void EncodedCursor_RoundTrips()
    {
        var original = new KnowledgeDocumentEventCursor(
            new DateTimeOffset(2026, 10, 2, 5, 9, 36, TimeSpan.Zero).AddTicks(1230),
            Guid.Parse("2ad6f1e1-3515-4835-9b48-c21945acb764"));

        Assert.True(KnowledgeDocumentEventCursor.TryDecode(original.Encode(), out var decoded));
        Assert.Equal(original, decoded);
    }
}
