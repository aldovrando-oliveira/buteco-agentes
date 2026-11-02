using System.Text;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using Buteco.Api.Users;
using Buteco.Api.Users.Bootstrap;

namespace Buteco.Api.Tests;

/// <summary>
/// As chaves de <c>pg_advisory_xact_lock</c> do <c>apps/api</c> (design.md da change
/// rotas-conta-usuarios, D7): cada uma é a leitura big-endian da sua sigla, e nenhuma se repete.
/// Um lock compartilhado por engano serializaria duas coisas sem relação, e a primeira redação
/// do D7 propôs para a desativação a sigla do bootstrap.
///
/// <para>
/// Verificado contra o defeito (convenção 15), registro no tasks.md, 3.5: com a chave da
/// desativação igual à do bootstrap, <see cref="TheKeysAreDistinct"/> reprova.
/// </para>
/// </summary>
public class AdvisoryLockKeysTests
{
    public static TheoryData<string, long> Keys => new()
    {
        { "BUTECOC1", IdentityClientCatalog.AdvisoryLockKey },
        { "BUTECOD1", UserAdministration.AdvisoryLockKey },
        { "BUTECOK1", IdentityKeyRotationPolicy.AdvisoryLockKey },
        { "BUTECOP1", IdentityTokenPrunePolicy.AdvisoryLockKey },
        { "BUTECOU1", UserBootstrapper.AdvisoryLockKey },
    };

    [Theory]
    [MemberData(nameof(Keys))]
    public void EachKeyIsItsAcronymInBigEndian(string acronym, long key)
    {
        var bytes = Encoding.ASCII.GetBytes(acronym);
        Assert.Equal(System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes), key);
    }

    [Fact]
    public void TheKeysAreDistinct()
    {
        var keys = Keys.Select(row => (long)row[1]).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
