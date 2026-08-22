namespace Buteco.Inbox.Auth;

// Metadata anexada a cada endpoint anônimo via .WithMetadata(...), junto
// com .AllowAnonymous() — documenta o motivo, checado no startup por
// RouteAuthenticationExtensions (design.md, Decision 4).
public enum AnonymousRouteReason
{
    HealthProbe,
    ExternalUnauthenticated,

    // POST /internal/push-notifications: já protegida por um token de
    // capacidade próprio (por PendingDispatch), mecanismo pré-existente
    // não tocado por esta change — anônima para ESTE middleware, não
    // realmente aberta (design.md, Decision 4).
    PreExistingAuthMechanism,
}

public sealed class AnonymousRouteClassification(AnonymousRouteReason reason)
{
    public AnonymousRouteReason Reason { get; } = reason;
}
