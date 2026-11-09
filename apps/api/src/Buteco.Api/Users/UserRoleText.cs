namespace Buteco.Api.Users;

/// <summary>
/// Os dois vocabulários do papel (design.md da change papeis-no-token, D3, tabela de
/// correspondência, decisão do dono de 08/11/2026):
/// <list type="bullet">
/// <item><b>banco e claim <c>role</c>:</b> <c>admin</c>, <c>operator</c>, <c>reader</c>, em
/// inglês como o <c>status</c> (<see cref="UserStatusText"/>). Os três apps leem o claim por
/// estes textos;</item>
/// <item><b>JSON (<c>papel</c>):</b> <c>admin</c>, <c>operador</c>, <c>leitor</c>, em português
/// como o <c>estado</c> das respostas.</item>
/// </list>
/// Valor fora dos três lança citando o valor, em vez de virar um dos papéis: um papel lido
/// errado é acesso concedido ou tirado em silêncio.
/// <para>
/// O <c>role</c> <c>operator</c> e o subject <c>operator</c> do token do painel
/// (<c>ServiceScopeAuthorizationHandler.OperatorSubject</c>) são homônimos e são claims
/// diferentes: o subject diz que o token é do painel; o papel diz o que a pessoa pode.
/// </para>
/// </summary>
public static class UserRoleText
{
    public const string Admin = "admin";
    public const string Operator = "operator";
    public const string Reader = "reader";

    public const string WireAdmin = "admin";
    public const string WireOperator = "operador";
    public const string WireReader = "leitor";

    public static string Write(UserRole role) => role switch
    {
        UserRole.Admin => Admin,
        UserRole.Operator => Operator,
        UserRole.Reader => Reader,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Papel de usuário sem texto."),
    };

    public static UserRole Read(string value) =>
        TryRead(value) ?? throw new InvalidOperationException($"Papel de usuário desconhecido: '{value}'.");

    /// <summary>O papel do claim, ou nulo para ausente ou desconhecido (o token sem papel recebe <c>401</c>).</summary>
    public static UserRole? TryRead(string? value) => value switch
    {
        Admin => UserRole.Admin,
        Operator => UserRole.Operator,
        Reader => UserRole.Reader,
        _ => null,
    };

    public static string WriteWire(UserRole role) => role switch
    {
        UserRole.Admin => WireAdmin,
        UserRole.Operator => WireOperator,
        UserRole.Reader => WireReader,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Papel de usuário sem texto."),
    };

    /// <summary>O papel de um corpo JSON, ou nulo para ausente ou fora dos três (a rota responde <c>400</c>).</summary>
    public static UserRole? TryReadWire(string? value) => value switch
    {
        WireAdmin => UserRole.Admin,
        WireOperator => UserRole.Operator,
        WireReader => UserRole.Reader,
        _ => null,
    };
}
