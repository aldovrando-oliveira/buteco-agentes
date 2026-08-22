namespace Buteco.Inbox.Options;

// Mesmo shape de Buteco.Api.Options.CorsOptions — apps/inbox nunca tinha
// consumidor via browser antes de frontend-inbox-catalogo-canais, então
// nunca precisou de CORS (design.md dessa change, correção pós-implementação).
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}
