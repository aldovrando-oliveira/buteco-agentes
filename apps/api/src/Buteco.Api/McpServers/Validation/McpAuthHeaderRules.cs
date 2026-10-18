using Buteco.Api.McpServers.Entities;

namespace Buteco.Api.McpServers.Validation;

/// <summary>
/// Regras de entrada do tipo de autenticação, do nome do header customizado e
/// do valor do token (change mcp-autenticacao-header-customizado, D3 e D9).
/// Nenhuma mensagem daqui recebe o valor da credencial: o vazamento que estas
/// regras existem para fechar é justamente o de um segredo dentro de uma
/// mensagem (design.md, V2 e V3).
/// </summary>
public static class McpAuthHeaderRules
{
    // Prefixo, e não lista: o ModelContextProtocol.Core 2.0.0 já usa uma família
    // ABERTA de headers próprios (Mcp-Session-Id, MCP-Protocol-Version,
    // Mcp-Method, Mcp-Name e Mcp-Param-*, McpHttpHeaders decompilado), e uma
    // versão nova pode acrescentar membros que uma lista nossa não acompanharia.
    // Medido (V2): um header customizado com esses nomes é ACEITO pelo SDK e
    // acrescentado ao valor que ele mesmo envia, corrompendo o protocolo.
    private const string McpReservedPrefix = "Mcp-";

    // Headers que o SDK aceita mas que colidem com o protocolo ou com o HTTP:
    // - Accept: medido, o valor é acrescentado ao Accept do SDK (V2);
    // - Last-Event-ID: escrito pelo SDK no GET de retomada (decompilado);
    // - Host: medido, substitui o destino lógico da requisição (V2);
    // - os de conexão: por RFC 9110 §7.6.1, NÃO medidos um a um. São do salto,
    //   não da origem, então um segredo neles não chega ao servidor como
    //   credencial.
    private static readonly HashSet<string> ReservedHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Last-Event-ID",
        "Host",
        "Connection",
        "Keep-Alive",
        "Proxy-Connection",
        "Transfer-Encoding",
        "TE",
        "Trailer",
        "Upgrade",
        "Expect",
    };

    /// <summary>
    /// Aceita só o NOME de um membro definido, sem diferenciar caixa (D9).
    /// <c>Enum.TryParse</c> aceitava <c>"2"</c>, <c>"99"</c> e
    /// <c>"None,BearerToken"</c> (medido, V7), e com o membro novo <c>"2"</c>
    /// passaria a ser <c>CustomHeader</c> em silêncio.
    /// </summary>
    public static bool TryParseAuthType(string? raw, out McpServerAuthType authType)
    {
        authType = McpServerAuthType.None;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var name = Enum.GetNames<McpServerAuthType>()
            .FirstOrDefault(member => string.Equals(member, raw, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return false;
        }

        authType = Enum.Parse<McpServerAuthType>(name);
        return true;
    }

    // Gerada dos membros, não escrita à mão: a mensagem fixa anterior
    // ("Valores aceitos: None, BearerToken.") teria envelhecido com o membro novo.
    public static string InvalidAuthTypeMessage =>
        $"O tipo de autenticação informado é inválido. Valores aceitos: {string.Join(", ", Enum.GetNames<McpServerAuthType>())}.";

    /// <summary>
    /// As três regras do nome do header, na ordem do D3. Devolve a mensagem de
    /// erro, ou nulo quando o nome é aceito. <c>Authorization</c> é aceito de
    /// propósito: o tipo envia o valor cru, e há esquemas reais que não são
    /// <c>Bearer</c> (<c>Token …</c>, <c>ApiKey …</c>).
    /// </summary>
    public static string? ValidateHeaderName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "O nome do header é obrigatório para o tipo de autenticação CustomHeader.";
        }

        // Regra 1 — token HTTP (RFC 9110 §5.6.2): só tchar, ao menos um.
        if (!name.All(IsTokenChar))
        {
            return "O nome do header só pode ter letras, números e os caracteres ! # $ % & ' * + - . ^ _ ` | ~ (sem espaço nem dois-pontos).";
        }

        // Regra 2 — o critério do próprio .NET.
        if (!IsAcceptedAsRequestHeader(name))
        {
            return $"O header {name} não pode ser usado em uma requisição (é um header de conteúdo ou de controle do .NET).";
        }

        // Regra 3 — reservados do protocolo MCP e do HTTP.
        if (name.StartsWith(McpReservedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return "Headers com prefixo Mcp- são do protocolo MCP e não podem levar a credencial.";
        }

        if (ReservedHeaderNames.Contains(name))
        {
            return $"O header {name} é controlado pelo protocolo e não pode levar a credencial.";
        }

        return null;
    }

    /// <summary>
    /// Regra 2 isolada: é EXATAMENTE a chamada que o SDK faz em
    /// <c>StreamableHttpClientSessionTransport.CopyAdditionalHeaders</c>
    /// (decompilado, V2), só que com um valor fictício. Quando ela devolve
    /// <c>false</c>, o SDK lança <c>InvalidOperationException</c> com o VALOR do
    /// header na mensagem — medido com <c>Content-Type</c> e com um nome com
    /// espaço. Usada também por <c>McpConnectionTester</c> antes de montar o
    /// transporte a partir de uma linha salva (D3).
    /// </summary>
    public static bool IsAcceptedAsRequestHeader(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        using var probe = new HttpRequestMessage();
        return probe.Headers.TryAddWithoutValidation(name, "x");
    }

    /// <summary>
    /// Valor do token do tipo CustomHeader: só ASCII visível ou espaço
    /// (0x20–0x7E), sem espaço nas pontas. Medido com SocketsHttpHandler real
    /// (V4): CR/LF no valor é escrito cru e INJETA um header na requisição, e
    /// \n e \0 também saem crus; não ASCII falha só no envio. A mensagem nunca
    /// cita o valor.
    /// </summary>
    public static string? ValidateTokenValue(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        if (value.Any(character => character is < ' ' or > '~'))
        {
            return "A credencial só pode ter caracteres ASCII visíveis ou espaço, sem quebra de linha nem caractere de controle.";
        }

        if (value[0] == ' ' || value[^1] == ' ')
        {
            return "A credencial não pode começar nem terminar com espaço.";
        }

        return null;
    }

    private static bool IsTokenChar(char character) =>
        character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
