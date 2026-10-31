using Buteco.Api.Auth;

namespace Buteco.Api.Identity.Validation;

/// <summary>
/// Checagem de boot da tabela de clientes (design.md da change
/// usuarios-locais-login-emissor, D3.3; convenção 8), sem banco. Todo subject de destino de
/// <see cref="IssuerClientSubjects"/> precisa existir na tabela de subjects do
/// <c>apps/api</c>: o operador ou um subject de serviço do
/// <see cref="ServiceScopeAuthorizationHandler.ServiceRoutes"/>. Sem isso, o cliente
/// receberia 403 em toda rota, em produção, e não no boot.
/// </summary>
public static class IssuerTokenStartupValidation
{
    public static void ValidateIssuerClientSubjects(this IHost host) =>
        ValidateIssuerClientSubjects(IssuerClientSubjects.Table);

    public static void ValidateIssuerClientSubjects(IReadOnlyDictionary<string, string> table)
    {
        var problems = table
            .Where(entry => entry.Value != ServiceScopeAuthorizationHandler.OperatorSubject &&
                !ServiceScopeAuthorizationHandler.ServiceRoutes.ContainsKey(entry.Value))
            .Select(entry => $"cliente '{entry.Key}' aponta para o subject '{entry.Value}', que não existe no apps/api")
            .ToList();

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"Tabela de clientes do token do emissor inválida: {string.Join("; ", problems)}.");
        }
    }
}
