using System.Text.RegularExpressions;

namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// Retirada das imagens embutidas do markdown exportado (design.md, D4). Medido na
/// etapa 0: <c>![][image1]</c> no corpo e <c>[image1]: &lt;data:image/png;base64,…&gt;</c>
/// no fim, com a imagem fazendo 99,8% dos bytes. No lugar de cada imagem não fica nada.
/// </summary>
/// <remarks>
/// Só sai imagem cujo destino é URI <c>data:</c>: as definições de referência com
/// <c>data:</c>, as referências de imagem que apontam para elas (cheia, colapsada ou
/// atalho) e as imagens inline com <c>data:</c>, que o CommonMark permite e a rodada não
/// viu. Imagem com URL comum fica. O resto do texto, escapes inclusive, não muda (#121).
/// Quando a imagem ocupava a linha sozinha, a linha sai junto, com uma linha em branco
/// seguinte.
/// </remarks>
public static partial class GoogleDriveMarkdown
{
    [GeneratedRegex(@"(\r?\n)?^[ ]{0,3}\[(?<label>[^\]]+)\]:[ \t]*<?data:[^\s>]*>?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex DataDefinition();

    [GeneratedRegex(@"!\[[^\]]*\]\(\s*<?data:[^)]*\)")]
    private static partial Regex InlineDataImage();

    public static string StripEmbeddedImages(string markdown)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var text = DataDefinition().Replace(markdown, match =>
        {
            labels.Add(match.Groups["label"].Value.Trim());
            return "";
        });

        var images = new List<Regex> { InlineDataImage() };
        if (labels.Count > 0)
        {
            var alternatives = string.Join("|", labels.Select(Regex.Escape));
            // ![alt][label], ![alt][] (colapsada, rótulo = alt) e ![label] (atalho).
            images.Add(new Regex($@"!\[[^\]]*\]\[(?:{alternatives})\]|!\[(?:{alternatives})\](?:\[\])?(?![\[(])", RegexOptions.IgnoreCase));
        }

        foreach (var image in images)
        {
            text = RemoveImages(text, image);
        }

        return text;
    }

    private static string RemoveImages(string text, Regex image)
    {
        // Linha só com imagens: sai a linha e uma linha em branco seguinte.
        var aloneOnLine = new Regex($@"^[ \t]*(?:(?:{image})[ \t]*)+(\r?\n(\r?\n)?|$)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        text = aloneOnLine.Replace(text, "");
        return image.Replace(text, "");
    }
}
