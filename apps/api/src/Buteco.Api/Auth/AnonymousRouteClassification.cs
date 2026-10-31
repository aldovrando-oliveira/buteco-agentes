namespace Buteco.Api.Auth;

// Metadata anexada a cada endpoint anônimo via .WithMetadata(...), junto
// com .AllowAnonymous() — documenta o motivo, checado no startup por
// RouteAuthenticationExtensions (design.md, Decision 4).
public enum AnonymousRouteReason
{
    HealthProbe,
    AuthEntryPoint,
    PublicDiscovery,

    /// <summary>
    /// Encerramento de sessão do emissor: <c>/connect/logout</c> (design.md da change
    /// usuarios-locais-login-emissor, D3.6). Anônimo porque o painel pode chamar com o
    /// access token vencido; quem identifica a sessão é o <c>id_token_hint</c>.
    /// </summary>
    SessionEnd,
}

public sealed class AnonymousRouteClassification(AnonymousRouteReason reason)
{
    public AnonymousRouteReason Reason { get; } = reason;
}
