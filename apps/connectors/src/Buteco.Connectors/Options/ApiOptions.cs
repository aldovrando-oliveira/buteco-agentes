namespace Buteco.Connectors.Options;

/// <summary>
/// Endereço do <c>apps/api</c>, para o ciclo de sincronização (design.md da change
/// ciclo-de-sincronizacao, D2). Mesmo nome que o <c>apps/inbox</c> usa para o mesmo
/// papel. <b>Opcional:</b> ausente ou vazio, o processo sobe com o ciclo desligado, e a
/// navegação e a descrição de pasta continuam. Presente e inválido, o boot falha
/// (<see cref="Sync.ApiConfigurationValidation"/>).
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public string? BaseUrl { get; set; }

    /// <summary>Vazio conta como ausente: é o que a interpolação <c>${VAR:-}</c> do compose entrega.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);
}
