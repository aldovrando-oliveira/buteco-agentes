using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;

namespace Buteco.Api.Identity.Validation;

// Tabela fechada client_id → subject do token do emissor (design.md da change
// usuarios-locais-login-emissor, D3.3; o mesmo desenho da #208 D6). O subject sai daqui, e
// nunca do sub do token: o sub é o id do usuário, e um usuário ou cliente com id
// "operator" não pode virar o operador. Cliente fora da tabela autentica sem
// NameIdentifier e recebe 403 do ServiceScopeAuthorizationHandler.
//
// Todo subject de destino precisa existir na tabela de subjects do apps/api; quem confere
// é IssuerTokenStartupValidation.ValidateIssuerClientSubjects, no boot.
public static class IssuerClientSubjects
{
    public static IReadOnlyDictionary<string, string> Table { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [IdentityClients.PanelClientId] = ServiceScopeAuthorizationHandler.OperatorSubject,
    };
}
