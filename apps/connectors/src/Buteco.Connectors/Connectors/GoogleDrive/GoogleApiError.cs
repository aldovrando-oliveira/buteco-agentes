namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>
/// Uma resposta de erro do Google, reduzida ao que decide o código: o status e o
/// <c>reason</c> de <c>error.errors[0]</c>. A mensagem do Google não é guardada.
/// <see cref="Status"/> zero quer dizer que não houve resposta (rede ou timeout).
/// </summary>
public sealed class GoogleApiError(int status, string? reason) : Exception($"Google respondeu {status} ({reason ?? "sem reason"}).")
{
    public int Status { get; } = status;

    public string? Reason { get; } = reason;
}

/// <summary>Sobre o que a chamada era, porque <c>404 notFound</c> significa coisas diferentes.</summary>
public enum GoogleErrorTarget
{
    Folder,
    File,
}
