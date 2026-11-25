namespace Buteco.Api.Providers.Vendors;

/// <summary>
/// O critério que a listagem de um provedor aplicou, como texto no fio (convenção 12). Diz
/// o critério com o nome do que o fornecedor informa, e nunca uma conclusão sobre o modelo:
/// nenhum valor afirma "de conversa" (design.md da change modelos-curados-por-provedor, D4,
/// convenção 13).
/// </summary>
public static class VendorModelFilters
{
    /// <summary>OpenAI: a listagem não distingue conversa de embedding, imagem ou áudio.</summary>
    public const string None = "none";

    /// <summary>Anthropic: só itens com <c>lifecycle</c> <c>"active"</c>.</summary>
    public const string LifecycleActive = "lifecycle-active";

    /// <summary>Gemini: só itens cujo <c>supportedGenerationMethods</c> contém <c>generateContent</c>.</summary>
    public const string SupportsGenerateContent = "supports-generate-content";
}

/// <summary>Um modelo que o fornecedor listou, depois do filtro do provedor.</summary>
public sealed record VendorModel(string Id, string? DisplayName);

/// <summary>
/// O que uma página (ou a listagem inteira, na OpenAI) devolveu: os modelos e, quando há,
/// o cursor da página seguinte. Um status diferente de <c>200</c> vem em
/// <see cref="NonSuccessStatus"/>, sem modelos.
/// </summary>
public sealed record VendorPage(IReadOnlyList<VendorModel> Models, string? NextCursor, int? NonSuccessStatus)
{
    public static VendorPage Failed(int status) => new([], null, status);
}

/// <summary>
/// A resposta do fornecedor estava em <c>200</c> e fora do contrato (JSON inválido, campo
/// obrigatório ausente, item da Anthropic sem <c>lifecycle</c>). Vira <c>502</c>
/// <c>vendor-error</c> com <c>vendorStatus</c> <c>null</c> (D4).
/// </summary>
public sealed class VendorContractException(string message) : Exception(message);

public enum VendorFailureKind
{
    /// <summary><c>503</c> <c>vendor-unavailable</c>: sem resposta, erro de conexão ou orçamento estourado.</summary>
    Unavailable,

    /// <summary><c>502</c> <c>vendor-error</c>: resposta não <c>200</c>, ou <c>200</c> fora do contrato.</summary>
    Error,
}

/// <summary>
/// A falha da consulta ao fornecedor. <see cref="VendorStatus"/> é o status que ele devolveu
/// e só existe em <see cref="VendorFailureKind.Error"/>: inteiro na resposta não <c>200</c>,
/// <c>null</c> no <c>200</c> fora do contrato (decisão do dono de 19/11/2026, achado A1).
/// </summary>
public sealed record VendorListingFailure(VendorFailureKind Kind, int? VendorStatus);

/// <summary>O resultado da consulta: o filtro aplicado e os modelos, ou a falha.</summary>
public sealed record VendorModelListing(string Filter, IReadOnlyList<VendorModel> Models, VendorListingFailure? Failure)
{
    public bool Succeeded => Failure is null;
}
