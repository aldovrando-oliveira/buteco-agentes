using System.Text.RegularExpressions;

namespace Buteco.Api.KnowledgeSync;

/// <summary>
/// A forma de um código de motivo (design.md da change catalogo-base-sincronizada,
/// D1): <c>access-denied</c>, <c>unsupported-type</c>... O conjunto é aberto e
/// pertence ao app que sincroniza, então não há lista aqui; mas uma frase ("Sem
/// acesso à pasta") não é código, e é recusada. O texto exibido é do frontend.
/// </summary>
public static partial class SyncCode
{
    public const int MaxLength = 64;

    /// <summary>
    /// <c>\z</c>, e não <c>$</c>: em .NET o <c>$</c> casa antes de um <c>\n</c>
    /// final, e <c>"access-denied\n"</c> passaria.
    /// </summary>
    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex Shape();

    public static bool IsValid(string? code) =>
        code is { Length: > 0 and <= MaxLength } && Shape().IsMatch(code);

    public const string ShapeDescription =
        "um código em minúsculas, dígitos e hífens (por exemplo access-denied), de até 64 caracteres, nunca uma frase";
}
