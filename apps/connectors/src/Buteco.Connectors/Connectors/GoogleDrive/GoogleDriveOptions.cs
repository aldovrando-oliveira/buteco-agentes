namespace Buteco.Connectors.Connectors.GoogleDrive;

public sealed class GoogleDriveOptions
{
    public const string SectionName = "GoogleDrive";

    /// <summary>
    /// O arquivo JSON da chave da service account, em base64 numa linha
    /// (<c>base64 -i chave.json | tr -d '\n'</c>). Ausente ou vazio, o provedor não é
    /// registrado (design.md, D3).
    /// </summary>
    public string? ServiceAccountKeyBase64 { get; set; }
}
