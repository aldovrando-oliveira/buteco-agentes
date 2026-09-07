namespace Buteco.Api.KnowledgeDocuments.Extraction;

/// <summary>
/// Extrator de markdown. **Preserva a marcação** — extração aqui é
/// normalização, não conversão para texto puro (design.md, D11).
///
/// A estratégia de fragmentação da etapa de indexação divide por cabeçalho e
/// depende dos `#` sobreviverem: medida contra corpus real, divisão por
/// cabeçalho com merge-up produziu 419 fragmentos de média 1092 caracteres,
/// enquanto "limpar" a marcação e dividir por tamanho destruiria justamente a
/// estrutura que dá o ganho. Um extrator que removesse a marcação quebraria a
/// etapa seguinte sem quebrar nenhum teste desta.
/// </summary>
public sealed class MarkdownSourceExtractor : IKnowledgeSourceExtractor
{
    private const char Bom = '\uFEFF';
    private const char Nul = '\0';

    public ExtractionResult Extract(string rawContent)
    {
        // NUL não é hipótese: uma coluna `text` do Postgres não aceita U+0000
        // (`ERROR: null character not permitted`, verificado contra o Postgres
        // desta stack). Sem esta checagem o erro chegaria ao operador como
        // falha crua de banco no INSERT, em vez de validação com mensagem.
        if (rawContent.Contains(Nul))
        {
            return ExtractionResult.Failure(
                "O conteúdo contém um caractere nulo (U+0000), que não pode ser armazenado como texto.");
        }

        var normalized = rawContent;

        if (normalized.Length > 0 && normalized[0] == Bom)
        {
            normalized = normalized[1..];
        }

        // CRLF antes de CR sozinho: a ordem importa, senão o \r do par vira
        // \n e o \n original sobra, duplicando a quebra.
        normalized = normalized.Replace("\r\n", "\n").Replace('\r', '\n');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ExtractionResult.Failure("O conteúdo do documento é obrigatório e não pode ser vazio.");
        }

        return ExtractionResult.Success(normalized);
    }
}
