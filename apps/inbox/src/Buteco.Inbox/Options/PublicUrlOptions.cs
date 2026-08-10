namespace Buteco.Inbox.Options;

// Mesmo padrão de Buteco.Api.Options.PublicUrlOptions — URL
// externamente alcançável deste processo, usada para montar o
// pushNotificationConfig.url enviado no SendMessage (design.md,
// Decisão 6).
public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrl";

    public string BaseUrl { get; set; } = "";
}
