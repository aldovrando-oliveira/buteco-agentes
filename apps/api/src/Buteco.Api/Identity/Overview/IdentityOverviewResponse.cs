using System.Text.Json.Serialization;
using Buteco.Api.Identity.Keys;

namespace Buteco.Api.Identity.Overview;

/// <summary>
/// A resposta do <c>GET /identity</c> (design.md da change leitura-identidade-emissor, D2), no
/// contrato do quadro Identidade corrigido em 04/11/2026. Os nomes do fio ficam fixos aqui, e o
/// teste os compara no texto do JSON. Durações em segundos inteiros, com o sufixo no nome.
/// Nenhum campo carrega material de chave.
/// </summary>
public sealed record IdentityOverviewResponse(
    [property: JsonPropertyName("emissor")] IssuerOverview Emissor,
    [property: JsonPropertyName("chavesDeAssinatura")] SigningKeysOverview ChavesDeAssinatura);

public sealed record IssuerOverview(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("discovery")] string Discovery,
    [property: JsonPropertyName("jwks")] string Jwks,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("validadeDoTokenSegundos")] long ValidadeDoTokenSegundos,
    [property: JsonPropertyName("sessaoSemAtividadeSegundos")] long SessaoSemAtividadeSegundos);

public sealed record SigningKeysOverview(
    [property: JsonPropertyName("intervaloDeTrocaSegundos")] long IntervaloDeTrocaSegundos,
    [property: JsonPropertyName("prePublicacaoSegundos")] long PrePublicacaoSegundos,
    [property: JsonPropertyName("retencaoSegundos")] long RetencaoSegundos,
    [property: JsonPropertyName("verificacaoSegundos")] long VerificacaoSegundos,
    [property: JsonPropertyName("chaves")] IReadOnlyList<SigningKeyOverview> Chaves);

public sealed record SigningKeyOverview(
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("criadaEm")] DateTimeOffset CriadaEm,
    [property: JsonPropertyName("assinaDesde")] DateTimeOffset AssinaDesde,
    [property: JsonPropertyName("assinaAte")] DateTimeOffset AssinaAte,
    [property: JsonPropertyName("saiDaPublicacaoEm")] DateTimeOffset SaiDaPublicacaoEm)
{
    public const string InUseState = "em-uso";

    public const string NextState = "proxima";

    public const string RetiringState = "aposentando";

    public static SigningKeyOverview From(IdentityKeyDescription description) => new(
        description.Key.Kid,
        StateOf(description.State),
        description.Key.CreatedAt,
        description.Key.SignsFrom,
        description.Key.SignsUntil,
        description.Key.ValidatesUntil);

    public static string StateOf(IdentityKeyState state) => state switch
    {
        IdentityKeyState.InUse => InUseState,
        IdentityKeyState.Next => NextState,
        IdentityKeyState.Retiring => RetiringState,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
