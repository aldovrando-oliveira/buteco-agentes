namespace Buteco.Api.Tests.Support;

/// <summary>
/// As classes que só leem a composição do host construído (a <c>EndpointDataSource</c>) e dividem uma
/// <see cref="ApiFactoryFixture"/>, e com ela um Postgres só (change nginx-prefixo-applications, D2). Nenhuma das
/// duas lê ou grava estado de banco: o contêiner existe porque o host não sobe sem ele. Classes da mesma
/// collection não rodam em paralelo entre si, e os casos delas são sub-segundo.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiHostCompositionCollection : ICollectionFixture<ApiFactoryFixture>
{
    public const string Name = "Api host composition";
}
