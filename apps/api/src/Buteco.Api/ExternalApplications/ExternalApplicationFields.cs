using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Buteco.Api.ExternalApplications;

/// <summary>
/// Nome e descrição de uma aplicação externa, e as respostas de erro das rotas (spec
/// <c>external-applications</c>). Os textos são o contrato que a tela da #188 mostra: o quadro
/// <c>AplicacaoNova</c> (versão 60) diz "Até 80 caracteres, sem repetir o nome de outra aplicação." e
/// "Opcional, até 280 caracteres.".
/// </summary>
public static class ExternalApplicationFields
{
    public const int MaxNameLength = 80;

    public const int MaxDescriptionLength = 280;

    public const string NameRequiredMessage = "Informe o nome.";

    public const string NameTooLongMessage = "O nome tem no máximo 80 caracteres.";

    public const string DescriptionTooLongMessage = "A descrição tem no máximo 280 caracteres.";

    public const string NameInUseMessage = "Já existe uma aplicação com este nome.";

    public const string NotFoundMessage = "Aplicação não encontrada.";

    public const string SecretNotFoundMessage = "Segredo não encontrado.";

    public const string DeactivatedMessage = "Reative a aplicação antes de gerar um segredo.";

    public const string SecretLimitMessage = "Revogue um segredo antes de gerar outro.";

    public const string StateMessage = "O estado precisa ser ativas ou desativadas.";

    private const string NormalizedNameIndexName = "IX_external_applications_normalized_name";

    /// <summary>O nome aparado e a descrição aparada (vazia vira ausente), ou os erros por campo.</summary>
    public static Dictionary<string, string[]>? Validate(string? rawName, string? rawDescription, out string name, out string? description)
    {
        name = rawName?.Trim() ?? "";
        var trimmed = rawDescription?.Trim();
        description = string.IsNullOrEmpty(trimmed) ? null : trimmed;

        var errors = new Dictionary<string, string[]>();
        if (name.Length == 0)
        {
            errors["nome"] = [NameRequiredMessage];
        }
        else if (name.Length > MaxNameLength)
        {
            errors["nome"] = [NameTooLongMessage];
        }

        if (description is { Length: > MaxDescriptionLength })
        {
            errors["descricao"] = [DescriptionTooLongMessage];
        }

        return errors.Count > 0 ? errors : null;
    }

    public static IResult NameInUse() =>
        TypedResults.Problem(title: NameInUseMessage, statusCode: StatusCodes.Status409Conflict);

    public static IResult NotFound() =>
        TypedResults.Problem(title: NotFoundMessage, statusCode: StatusCodes.Status404NotFound);

    public static IResult SecretNotFound() =>
        TypedResults.Problem(title: SecretNotFoundMessage, statusCode: StatusCodes.Status404NotFound);

    public static bool IsNameUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres &&
        postgres.ConstraintName == NormalizedNameIndexName;
}
