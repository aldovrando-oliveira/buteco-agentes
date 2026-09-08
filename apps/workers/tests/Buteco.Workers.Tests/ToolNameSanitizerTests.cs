using Buteco.Workers.Mcp;

namespace Buteco.Workers.Tests;

/// <summary>
/// Primeiro arquivo de teste de <see cref="ToolNameSanitizer"/>, trazido pela
/// change dedupe-global-nome-de-tool (design.md, Decisão 6). Ele é compartilhado
/// pelos dois resolvedores (<see cref="McpToolSetResolver"/> e
/// <c>AgentDelegationToolSetResolver</c>) e é o sítio da truncagem, e até aqui
/// não tinha nenhuma cobertura própria.
/// </summary>
/// <remarks>
/// As duas garantias verificadas em fonte primária (design.md, V4) são o
/// conjunto de caracteres e o limite de 64: <c>FunctionObject.name</c> da
/// especificação OpenAPI publicada pelo OpenAI, na superfície Chat Completions
/// — a que <c>ChatClientResolver.BuildOpenAi</c> usa via
/// <c>GetChatClient(model).AsIChatClient()</c> — declara "Must be a-z, A-Z,
/// 0-9, or contain underscores and dashes, with a maximum length of 64".
/// A regra de caractere inicial NÃO vem de provedor nenhum: é escolha deste
/// repositório, coberta aqui como tal (ver o requisito próprio na spec de
/// agent-tool-namespace).
/// </remarks>
public class ToolNameSanitizerTests
{
    [Theory]
    [InlineData("Zendesk_MCP__search", "Zendesk_MCP__search")]
    [InlineData("busca-de-pedidos", "busca-de-pedidos")]
    [InlineData("Tool123", "Tool123")]
    public void Sanitize_NameAlreadyWithinAllowedCharacterSet_IsUnchanged(string composite, string expected)
    {
        Assert.Equal(expected, ToolNameSanitizer.Sanitize(composite));
    }

    [Theory]
    [InlineData("Zendesk MCP__search", "Zendesk_MCP__search")]
    [InlineData("Zendesk.MCP__search", "Zendesk_MCP__search")]
    [InlineData("Informações Gerais__get_menu_info", "Informa__es_Gerais__get_menu_info")]
    [InlineData("a/b:c d", "a_b_c_d")]
    public void Sanitize_CharacterOutsideAllowedSet_BecomesUnderscore(string composite, string expected)
    {
        Assert.Equal(expected, ToolNameSanitizer.Sanitize(composite));
    }

    [Theory]
    [InlineData("9tool", "_9tool")]
    [InlineData("-tool", "_-tool")]
    public void Sanitize_InitialCharacterNotLetterOrUnderscore_GetsUnderscorePrefix(string composite, string expected)
    {
        // Escolha deste repositório, não exigência de provedor: o schema do
        // OpenAI aceita [a-zA-Z0-9_-] em qualquer posição, dígito inicial
        // incluído (design.md, V4). Mantida para não alterar nomes já expostos.
        Assert.Equal(expected, ToolNameSanitizer.Sanitize(composite));
    }

    [Theory]
    [InlineData("_tool")]
    [InlineData("tool")]
    public void Sanitize_InitialCharacterAlreadyValid_GetsNoPrefix(string composite)
    {
        Assert.Equal(composite, ToolNameSanitizer.Sanitize(composite));
    }

    [Fact]
    public void Sanitize_InitialCharacterSanitizedIntoUnderscore_GetsNoExtraPrefix()
    {
        // A ordem dentro de Sanitize importa e não é óbvia: a substituição de
        // caracteres roda ANTES da checagem de inicial, então um nome que começa
        // com espaço já chega à checagem começando com "_" — que é inicial
        // válida — e não recebe um segundo "_". Verificado contra o código, não
        // suposto: a primeira redação deste teste esperava "__tool" e reprovou.
        Assert.Equal("_tool", ToolNameSanitizer.Sanitize(" tool"));
    }

    [Fact]
    public void Sanitize_EmptyInput_ProducesSingleUnderscore()
    {
        Assert.Equal("_", ToolNameSanitizer.Sanitize(string.Empty));
    }

    [Fact]
    public void Sanitize_NameWellBelowLimit_IsNotTruncated()
    {
        // Par "sem item" da convenção 5, e o caso real: o nome mais longo do
        // banco de dev tem 41 caracteres (design.md, V8).
        var composite = new string('a', 41);

        var sanitized = ToolNameSanitizer.Sanitize(composite);

        Assert.Equal(composite, sanitized);
        Assert.Equal(41, sanitized.Length);
    }

    [Fact]
    public void Sanitize_NameExactlyAtLimit_IsNotTruncated()
    {
        var composite = new string('a', ToolNameSanitizer.MaxToolNameLength);

        var sanitized = ToolNameSanitizer.Sanitize(composite);

        Assert.Equal(composite, sanitized);
        Assert.Equal(ToolNameSanitizer.MaxToolNameLength, sanitized.Length);
    }

    [Fact]
    public void Sanitize_NameAboveLimit_IsTruncatedToLimit()
    {
        var composite = new string('a', ToolNameSanitizer.MaxToolNameLength + 20);

        var sanitized = ToolNameSanitizer.Sanitize(composite);

        Assert.Equal(ToolNameSanitizer.MaxToolNameLength, sanitized.Length);
        Assert.Equal(new string('a', ToolNameSanitizer.MaxToolNameLength), sanitized);
    }

    [Fact]
    public void Sanitize_LongMcpCompositeName_LosesTheSeparatorToTruncation()
    {
        // A brecha que a change dedupe-global-nome-de-tool documenta (design.md,
        // V5): um McpServer.Name com >= 64 caracteres é truncado ANTES do "__",
        // então o nome resultante não contém mais o separador, e dois nomes
        // longos com o mesmo prefixo de 64 colidem entre si.
        var serverName = new string('s', ToolNameSanitizer.MaxToolNameLength);

        var sanitized = ToolNameSanitizer.Sanitize($"{serverName}__search");

        Assert.Equal(ToolNameSanitizer.MaxToolNameLength, sanitized.Length);
        Assert.DoesNotContain("__", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_MaxToolNameLength_MatchesTheVerifiedProviderLimit()
    {
        // Prende o número que até esta change vivia só num comentário, sem
        // fonte e sem teste. Fonte primária em design.md, V4.
        Assert.Equal(64, ToolNameSanitizer.MaxToolNameLength);
    }
}
