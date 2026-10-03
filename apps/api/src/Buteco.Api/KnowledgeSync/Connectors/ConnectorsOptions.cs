namespace Buteco.Api.KnowledgeSync.Connectors;

/// <summary>
/// Endereço do <c>apps/connectors</c> (design.md da change criacao-base-sincronizada,
/// D1). <b>Opcional:</b> ausente ou vazio, o processo sobe e só o cadastro de base
/// <c>Synced</c> é recusado, com <c>connectors-not-configured</c>. É o estado de
/// produção até a #119 implantar o <c>apps/connectors</c>. Presente e inválido, o
/// boot falha (<see cref="ConnectorsConfigurationValidation"/>).
/// </summary>
public sealed class ConnectorsOptions
{
    public const string SectionName = "Connectors";

    public string? BaseUrl { get; set; }

    /// <summary>Vazio conta como ausente: é o que a interpolação <c>${VAR:-}</c> do compose entrega.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
