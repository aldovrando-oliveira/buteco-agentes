namespace Buteco.Inbox.Auth;

// Duplicado deliberadamente de
// apps/connectors/src/Buteco.Connectors/Auth/IssuerClientSubjects.cs
// — sem libs/ (design.md da change validacao-jwks-inbox-connectors, D10). Qualquer
// mudança aqui precisa ser espelhada lá, e vice-versa; o que difere de propósito é a
// audience e a tabela de clientes.

// Tabela fechada client_id → subject do token do emissor (change
// validacao-jwks-inbox-connectors, D6). O subject sai daqui, e nunca do sub do
// token: o sub é o id do cliente ou do usuário, e um cliente ou usuário com id
// "operator" não pode virar o operador. Cliente fora da tabela autentica sem
// NameIdentifier e recebe 403 da SubjectAuthorization.
//
// Todo subject de destino precisa existir na SubjectAuthorization; quem confere é
// IssuerTokenStartupValidation.ValidateIssuerClientSubjects, no boot.
public static class IssuerClientSubjects
{
    // O cliente do painel, que a #180 cadastra como público (authorization code +
    // PKCE). Mesmo texto no apps/connectors.
    public const string PanelClientId = "buteco-painel";

    public static IReadOnlyDictionary<string, string> Table { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PanelClientId] = SubjectAuthorization.OperatorSubject,
    };
}
