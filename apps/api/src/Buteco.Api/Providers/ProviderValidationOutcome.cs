namespace Buteco.Api.Providers;

/// <summary>
/// Sinal de domínio devolvido por <see cref="ProviderCatalogService.Validate"/>
/// — sem nenhuma referência a <c>Microsoft.AspNetCore.Http.HttpResults</c>
/// (Decision 4 do design.md da change backend-multi-provedor-llm). Só o
/// endpoint sabe mapear isso para <c>ValidationProblem</c>.
/// </summary>
public enum ProviderValidationOutcome
{
    Valid,
    ProviderNotConfigured,
    ModelUnavailable,
}
