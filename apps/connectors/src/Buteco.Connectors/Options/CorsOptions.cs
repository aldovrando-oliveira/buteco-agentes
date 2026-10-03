namespace Buteco.Connectors.Options;

// Mesmo formato de Buteco.Api.Options.CorsOptions e Buteco.Inbox.Options.CorsOptions.
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
