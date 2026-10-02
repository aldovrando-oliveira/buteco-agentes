namespace Buteco.Api.Tests.Support;

/// <summary>
/// As classes de teste de migração que dividem o contêiner de
/// <see cref="MigrationPostgresFixture"/> (design.md da change
/// historico-documentos-base, D12). Classes da mesma collection não rodam em
/// paralelo entre si; o custo é pequeno, porque cada caso só migra e lê o
/// schema.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MigrationPostgresCollection : ICollectionFixture<MigrationPostgresFixture>
{
    public const string Name = "Migration Postgres";
}
