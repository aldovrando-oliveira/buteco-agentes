namespace Buteco.Inbox.Tests.Support;

/// <summary>
/// As classes que dividem uma <see cref="InboxFactoryFixture"/>, e com ela um Postgres só, para ler a composição do
/// host construído (change nginx-prefixo-applications, D2). O <c>NginxRouteCoverageTests</c> só lê a
/// <c>EndpointDataSource</c> e não depende de estado de banco; o <c>RoleMatrixTests</c> faz requisições com ids que
/// não existem e não depende do que a outra classe faz. Classes da mesma collection não rodam em paralelo entre si.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InboxHostCompositionCollection : ICollectionFixture<InboxFactoryFixture>
{
    public const string Name = "Inbox host composition";
}
