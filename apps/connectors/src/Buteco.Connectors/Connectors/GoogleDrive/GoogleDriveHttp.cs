namespace Buteco.Connectors.Connectors.GoogleDrive;

/// <summary>Os dois <c>HttpClient</c> nomeados do conector Google.</summary>
public static class GoogleDriveHttp
{
    public const string DriveClientName = "google-drive";

    public const string OAuthClientName = "google-oauth";

    public static readonly Uri DriveBaseAddress = new("https://www.googleapis.com/");

    public static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");

    // Fixos, não configuráveis (convenção 2), e diferentes por natureza da chamada
    // (design.md, D2). Medido contra o Drive real (tarefa 10.4): exportar um Google
    // Doc de ~850 KB de markdown levou 16,8 s, 26,1 s e mais de 30 s em três
    // tentativas, então o limite único de 30 s da primeira redação falhava com
    // documento grande. Leitura de metadado, listagem e troca de token continuam
    // curtas, porque a rota de navegação não pode esperar minutos.
    public static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(30);

    // Exportação e download: quatro vezes o maior tempo observado. O limite
    // documentado da exportação é 10 MB, que não foi medido.
    public static readonly TimeSpan ContentTimeout = TimeSpan.FromSeconds(120);
}
