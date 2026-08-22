namespace Buteco.Api.Auth;

// Metadata anexada a cada endpoint anônimo via .WithMetadata(...), junto
// com .AllowAnonymous() — documenta o motivo, checado no startup por
// RouteAuthenticationExtensions (design.md, Decision 4).
public enum AnonymousRouteReason
{
    HealthProbe,
    AuthEntryPoint,
    PublicDiscovery,
}

public sealed class AnonymousRouteClassification(AnonymousRouteReason reason)
{
    public AnonymousRouteReason Reason { get; } = reason;
}
