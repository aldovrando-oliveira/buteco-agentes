using System.Text;

namespace Buteco.Workers.Mcp;

/// <summary>
/// Sanitização de caracteres/tamanho de nome de tool exposta ao LLM,
/// extraída de <see cref="McpToolSetResolver"/> (design.md, Decision 3
/// daquela change) para ser reaproveitada também pela resolução de tools
/// de delegação (ver design.md da change apps-workers-delegacao-execucao,
/// Decision 9) — mesma regra, mais restritiva entre os três provedores
/// suportados incondicionalmente (OpenAI: só <c>[a-zA-Z0-9_-]</c>, máximo
/// 64 caracteres).
/// </summary>
public static class ToolNameSanitizer
{
    public const int MaxToolNameLength = 64;

    public static string Sanitize(string composite)
    {
        var sanitized = new StringBuilder(composite.Length);
        foreach (var character in composite)
        {
            sanitized.Append(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        if (sanitized.Length == 0 || !(char.IsAsciiLetter(sanitized[0]) || sanitized[0] == '_'))
        {
            sanitized.Insert(0, '_');
        }

        return sanitized.Length > MaxToolNameLength
            ? sanitized.ToString(0, MaxToolNameLength)
            : sanitized.ToString();
    }
}
