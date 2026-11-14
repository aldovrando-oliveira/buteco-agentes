namespace Buteco.Api.Identity.Federated;

/// <summary>
/// Os nomes fixos do login federado (design.md da change provedores-federados-google, D1, D4 e D7):
/// rotas, cookies, prazos, intenções e motivos de recusa. Os motivos são contrato com o painel (#190).
/// </summary>
public static class FederatedLogin
{
    public const string StartPath = "/auth/federated/{chave}/start";

    public const string CallbackPath = "/auth/federated/callback";

    public const string ProvidersPath = "/auth/federated/providers";

    public const string RefusalPath = "/auth/federated/refusal";

    /// <summary>O prefixo do cookie do fluxo; o OpenIddict acrescenta <c>.&lt;nonce&gt;</c>.</summary>
    public const string FlowCookieName = "buteco-fluxo-externo";

    /// <summary>A vida do state token e, por ela, a do cookie do fluxo (D4).</summary>
    public static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Quanto o início do fluxo espera o discovery do provedor (D15, APPLY-11): passado o prazo, 503. O
    /// padrão das tentativas do cliente passava de 15 s; 10 s cabe numa navegação que a pessoa espera.
    /// </summary>
    public static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    public const string RefusalCookieName = "buteco-recusa";

    public static readonly TimeSpan RefusalCookieLifetime = TimeSpan.FromSeconds(120);

    /// <summary>As propriedades do Buteco no state token.</summary>
    public static class Properties
    {
        public const string Intent = "buteco_intencao";
        public const string Return = "buteco_retorno";
        public const string UserId = "buteco_usuario";
        public const string SecurityStamp = "buteco_stamp";
        public const string ProviderKey = "buteco_provedor";
    }

    public static class Intents
    {
        public const string Login = "login";
        public const string Connect = "conectar";
    }

    /// <summary>Os motivos de recusa (spec <c>federated-login</c>).</summary>
    public static class Refusals
    {
        public const string DomainNotAllowed = "dominio-nao-permitido";
        public const string Deactivated = "desativado";
        public const string EmailNotVerified = "email-nao-verificado";
        public const string PendingInvitation = "convite-pendente";
        public const string NotConnected = "nao-conectada";
        public const string NoAccess = "sem-acesso";
        public const string Failure = "falha";
        public const string InUse = "em-uso";
        public const string AlreadyConnected = "ja-conectado";
        public const string SessionExpired = "sessao-expirada";
    }
}
