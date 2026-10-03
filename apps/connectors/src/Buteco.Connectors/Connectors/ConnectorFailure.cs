namespace Buteco.Connectors.Connectors;

/// <summary>
/// Falha de uma operação de conector, como código e detalhe opcional (design.md, D5).
/// A mensagem da exceção é só o código: nada do provedor, e nunca a credencial.
/// </summary>
public sealed class ConnectorFailure : Exception
{
    public ConnectorFailure(string code, string? detail = null)
        : base($"Falha do conector: {code}")
    {
        if (!ConnectorCodes.IsValid(code))
        {
            throw new ArgumentException($"Código fora do formato: '{code}'.", nameof(code));
        }

        Code = code;
        Detail = detail;
    }

    public string Code { get; }

    public string? Detail { get; }
}
