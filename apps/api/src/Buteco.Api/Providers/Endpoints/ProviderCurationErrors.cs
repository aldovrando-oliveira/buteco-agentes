using Buteco.Api.Providers.Vendors;

namespace Buteco.Api.Providers.Endpoints;

/// <summary>
/// As respostas de falha da curadoria (design.md da change modelos-curados-por-provedor, D4 e
/// D6; spec <c>llm-model-curation</c>). Cada uma leva um <c>code</c> estável; só
/// <c>vendor-error</c> leva <c>vendorStatus</c>, ao lado do <c>code</c>: o status que o
/// fornecedor devolveu, ou <c>null</c> ESCRITO quando ele respondeu <c>200</c> fora do
/// contrato (decisão do dono de 19/11/2026, achado A1). O <c>detail</c> é texto para pessoas e
/// nunca é a fonte do número.
/// </summary>
public static class ProviderCurationErrors
{
    public const string ProviderNotFoundCode = "provider-not-found";
    public const string ProviderNotConfiguredCode = "provider-not-configured";
    public const string VendorUnavailableCode = "vendor-unavailable";
    public const string VendorErrorCode = "vendor-error";
    public const string ModelNotListedCode = "model-not-listed";
    public const string ModelNotEnabledCode = "model-not-enabled";

    /// <summary>O nome no fio (convenção 12).</summary>
    public const string VendorStatusMember = "vendorStatus";

    public static IResult ModelRequired() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["model"] = ["Informe o modelo."] });

    public static IResult ProviderNotFound() =>
        Problem(StatusCodes.Status404NotFound, "Provedor desconhecido.", ProviderNotFoundCode);

    public static IResult ProviderNotConfigured() =>
        Problem(StatusCodes.Status409Conflict, "A chave deste provedor não está no ambiente do servidor.", ProviderNotConfiguredCode);

    public static IResult ModelNotListed() =>
        Problem(StatusCodes.Status422UnprocessableEntity, "O fornecedor não lista este modelo para a chave deste ambiente.", ModelNotListedCode);

    public static IResult ModelNotEnabled() =>
        Problem(StatusCodes.Status404NotFound, "Este modelo não está habilitado para o provedor.", ModelNotEnabledCode);

    public static IResult VendorFailure(VendorListingFailure failure) => failure.Kind switch
    {
        VendorFailureKind.Unavailable =>
            Problem(StatusCodes.Status503ServiceUnavailable, "O fornecedor não respondeu.", VendorUnavailableCode),
        VendorFailureKind.Error => TypedResults.Problem(
            title: "O fornecedor recusou a consulta ou respondeu fora do esperado.",
            statusCode: StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = VendorErrorCode,
                [VendorStatusMember] = failure.VendorStatus,
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Kind, "Falha de fornecedor inesperada."),
    };

    private static IResult Problem(int status, string title, string code) =>
        TypedResults.Problem(title: title, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
