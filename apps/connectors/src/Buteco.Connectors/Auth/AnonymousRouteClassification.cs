namespace Buteco.Connectors.Auth;

// Metadata anexada a cada endpoint anônimo via .WithMetadata(...), junto com
// .AllowAnonymous(), documentando o motivo. Conferida no boot por
// RouteAuthenticationExtensions. Cópia do apps/inbox com o único motivo que este
// app usa: o /health.
public enum AnonymousRouteReason
{
    HealthProbe,
}

public sealed class AnonymousRouteClassification(AnonymousRouteReason reason)
{
    public AnonymousRouteReason Reason { get; } = reason;
}
