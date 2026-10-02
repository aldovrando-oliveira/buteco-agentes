using System.Buffers.Binary;
using System.Buffers.Text;

namespace Buteco.Api.KnowledgeDocuments.Queries.ListKnowledgeDocumentEvents;

/// <summary>
/// Posição na lista de eventos de uma base: o <c>(OccurredAt, Id)</c> do último
/// item entregue (design.md da change historico-documentos-base, D7). Opaco para
/// o cliente, em base64url de 24 bytes — 8 de <c>UtcTicks</c> e 16 do id.
///
/// <para>
/// <b>Não carrega a base</b>, de propósito (D8): a consulta filtra sempre pela
/// base da rota, então um cursor obtido em outra base só desloca a posição
/// dentro da base pedida, nunca abre a porta para a outra.
/// </para>
///
/// <para>
/// O instante vai em ticks, sem arredondar, e é sempre o valor <b>lido do
/// banco</b>: o <c>timestamptz</c> guarda microssegundos, e um cursor montado
/// com o valor em memória (resolução de 100 ns) apontaria para um instante que
/// não existe na tabela.
/// </para>
/// </summary>
public readonly record struct KnowledgeDocumentEventCursor(DateTimeOffset OccurredAt, Guid Id)
{
    private const int EncodedLength = 24;

    public string Encode()
    {
        Span<byte> bytes = stackalloc byte[EncodedLength];
        BinaryPrimitives.WriteInt64BigEndian(bytes[..8], OccurredAt.UtcTicks);
        Id.TryWriteBytes(bytes[8..], bigEndian: true, out _);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>
    /// <c>false</c> para qualquer entrada que não seja um cursor emitido por
    /// <see cref="Encode"/> — nunca lança: cursor malformado é erro do cliente e
    /// vira <c>400</c>, não <c>500</c>.
    /// </summary>
    public static bool TryDecode(string value, out KnowledgeDocumentEventCursor cursor)
    {
        cursor = default;

        Span<byte> bytes = stackalloc byte[EncodedLength + 3];
        int written;
        try
        {
            if (!Base64Url.TryDecodeFromChars(value, bytes, out written))
            {
                return false;
            }
        }
        catch (FormatException)
        {
            // Apesar do "Try", TryDecodeFromChars LANÇA para entrada com
            // comprimento ou caractere inválido em base64url, e para bits finais
            // não canônicos — o false fica só para destino pequeno demais. Cada
            // forma tem caso em KnowledgeDocumentEventCursorTests. Medido pelo
            // cenário de cursor malformado, que respondia 500 antes desta
            // captura.
            return false;
        }

        if (written != EncodedLength)
        {
            return false;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes[..8]);
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }

        cursor = new KnowledgeDocumentEventCursor(
            new DateTimeOffset(ticks, TimeSpan.Zero),
            new Guid(bytes[8..EncodedLength], bigEndian: true));
        return true;
    }
}
