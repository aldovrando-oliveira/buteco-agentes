using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.Users.Management;

/// <summary>
/// Nome e e-mail de um usuário, e o <c>409</c> do e-mail em uso (design.md da change
/// rotas-conta-usuarios, D5). Dois consumidores: a edição (<c>PUT /users/{id}</c>) e o convite
/// (design.md da change convites-redefinicao-senha, D5), com as mesmas regras e a mesma resposta,
/// inclusive na corrida pelo índice único.
/// </summary>
public static class UserFields
{
    public const int MaxNameLength = 200;

    public const int MaxEmailLength = 254;

    public const string EmailInUseMessage = "Já existe um usuário com este e-mail.";

    private const string NormalizedEmailIndexName = "IX_users_normalized_email";

    /// <summary>Os dois sem espaços nas pontas, ou os erros por campo.</summary>
    public static Dictionary<string, string[]>? Validate(string? rawName, string? rawEmail, out string name, out string email)
    {
        name = rawName?.Trim() ?? "";
        email = rawEmail?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (name.Length is 0 or > MaxNameLength)
        {
            errors["nome"] = [$"O nome precisa ter de 1 a {MaxNameLength} caracteres."];
        }

        if (email.Length > MaxEmailLength || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        {
            errors["email"] = ["Informe um e-mail válido."];
        }

        return errors.Count > 0 ? errors : null;
    }

    public static IResult EmailInUse() =>
        TypedResults.Problem(title: EmailInUseMessage, statusCode: StatusCodes.Status409Conflict);

    public static IResult NotFound() =>
        TypedResults.Problem(title: "Usuário não encontrado.", statusCode: StatusCodes.Status404NotFound);

    public static bool IsNormalizedEmailUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        postgres.ConstraintName == NormalizedEmailIndexName;
}
