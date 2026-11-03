namespace Buteco.Connectors.Auth;

// Duplicado deliberadamente de
// apps/inbox/src/Buteco.Inbox/Auth/IssuerClientSubjects.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

// Tabela fechada client_id → subject do token do emissor (change
// validacao-jwks-inbox-connectors, D6). O subject sai daqui, e nunca do sub do
// token: o sub é o id do cliente ou do usuário, e um cliente ou usuário com id
// "operator" não pode virar o operador. Cliente fora da tabela autentica sem
// NameIdentifier e recebe 403 do ConnectorsSubjectAuthorizationHandler; o subject
// mapeado segue as mesmas rotas que tem com o token HMAC.
//
// Todo subject de destino precisa ser chave de
// ConnectorsSubjectAuthorizationHandler.SubjectRoutes; quem confere é
// IssuerTokenStartupValidation.ValidateIssuerClientSubjects, no boot.
//
// Duas entradas: o painel (operator) e o cliente de serviço do apps/api (service:api), só
// na rota de descrever pasta (design.md da change clientes-servico-emissor, D3). O token
// dele é assinado no próprio apps/api, sem passar pelo /connect/token (#235); o texto do
// client_id precisa ser o mesmo de lá.
public static class IssuerClientSubjects
{
    // O cliente do painel, que a #180 cadastra como público (authorization code +
    // PKCE). Mesmo texto no apps/inbox.
    public const string PanelClientId = "buteco-painel";

    public const string ApiServiceClientId = "buteco-servico-api";

    public static IReadOnlyDictionary<string, string> Table { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PanelClientId] = ConnectorsSubjectAuthorizationHandler.OperatorSubject,
        [ApiServiceClientId] = ConnectorsSubjectAuthorizationHandler.ApiSubject,
    };
}
