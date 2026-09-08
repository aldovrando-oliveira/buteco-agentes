using System.Text;

namespace Buteco.Workers.Mcp;

/// <summary>
/// Sanitização de caracteres/tamanho de nome de tool exposta ao LLM,
/// extraída de <see cref="McpToolSetResolver"/> (design.md, Decision 3
/// daquela change) para ser reaproveitada também pela resolução de tools
/// de delegação (ver design.md da change apps-workers-delegacao-execucao,
/// Decision 9).
/// </summary>
public static class ToolNameSanitizer
{
    /// <summary>
    /// Limite de caracteres do nome de tool exposto ao LLM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fonte primária: <c>FunctionObject.name</c> da especificação OpenAPI
    /// publicada pelo OpenAI (<c>openai/openai-openapi</c>) — "Must be a-z,
    /// A-Z, 0-9, or contain underscores and dashes, with a maximum length of
    /// 64". Isso é da superfície <b>Chat Completions</b>, que é a que
    /// <c>ChatClientResolver.BuildOpenAi</c> usa, via
    /// <c>GetChatClient(model).AsIChatClient()</c>. A Responses API do mesmo
    /// provedor permite 128 (<c>FunctionToolParam.name</c>): se algum dia a
    /// resolução migrar para lá, este limite fica conservador sem motivo — a
    /// nota existe para que a migração não herde o número sem saber de onde ele
    /// veio.
    /// </para>
    /// <para>
    /// Gemini declara 128 no documento de descoberta oficial
    /// (<c>FunctionDeclaration.name</c>), com um conjunto de caracteres que é
    /// superconjunto deste. O limite do <b>Anthropic não foi verificado em fonte
    /// primária</b>: nem a documentação de tool use nem o tipo <c>ToolParam</c>
    /// do SDK publicado declaram comprimento ou padrão. Portanto os 64 são o
    /// piso verificado de <b>dois</b> provedores, não dos três — ver R8 no
    /// design.md da change dedupe-global-nome-de-tool.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Acrescenta <paramref name="suffix"/> a <paramref name="baseName"/>
    /// encurtando a base o quanto for preciso para o resultado caber em
    /// <see cref="MaxToolNameLength"/>.
    /// </summary>
    /// <remarks>
    /// A ordem importa e é o ponto da Decisão 4 do design.md desta change:
    /// sanitizar, truncar, e só então deduplicar — com o sufixo cabendo dentro
    /// do limite. Truncar <i>depois</i> de sufixar cortaria o próprio sufixo,
    /// que é a única coisa distinguindo os dois nomes, reintroduzindo a colisão
    /// que o dedupe acabou de resolver.
    /// </remarks>
    public static string AppendSuffixWithinLimit(string baseName, string suffix)
    {
        var roomForBase = MaxToolNameLength - suffix.Length;
        if (roomForBase <= 0)
        {
            // Sufixo sozinho já não cabe — só alcançável com um sufixo absurdo
            // (>= 64 colisões seguidas no mesmo nome), mas o corte tem de existir
            // para a garantia de comprimento não ter exceção.
            return suffix[^MaxToolNameLength..];
        }

        return baseName.Length <= roomForBase
            ? baseName + suffix
            : baseName[..roomForBase] + suffix;
    }
}
