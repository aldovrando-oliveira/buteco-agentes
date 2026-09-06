namespace Buteco.Api.Tests.Support;

/// <summary>
/// Mesma API, sem url pública configurada — o caso que hoje produz endereço
/// quebrado em silêncio e que a change agente-enderecos-a2a passou a declarar
/// como ausência (design.md, D2).
/// </summary>
public sealed class ApiFactoryWithoutPublicUrlFixture : ApiFactoryFixture
{
    protected override string? ConfiguredPublicBaseUrl => string.Empty;
}
