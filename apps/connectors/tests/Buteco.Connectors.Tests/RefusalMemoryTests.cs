using Buteco.Connectors.Sync;

namespace Buteco.Connectors.Tests;

/// <summary>
/// A memória de recusas determinísticas (design.md da change ciclo-de-sincronizacao,
/// D14), por ela mesma. O uso dentro do ciclo está em <c>KnowledgeBaseSyncCycleTests</c>.
/// </summary>
public class RefusalMemoryTests
{
    private readonly RefusalMemory _memory = new();
    private readonly Guid _base = Guid.NewGuid();

    [Theory]
    [InlineData("too-large")]
    [InlineData("unsupported-source-type")]
    [InlineData("null-character")]
    [InlineData("empty-content")]
    public void DeterministicRefusal_IsRecalledWithTheSameMarker(string code)
    {
        _memory.Remember(_base, "ref-a", "v1", code, "detalhe");

        Assert.True(_memory.TryRecall(_base, "ref-a", "v1", out var recalledCode, out var detail));
        Assert.Equal(code, recalledCode);
        Assert.Equal("detalhe", detail);
    }

    [Fact]
    public void DifferentMarker_IsNotRecalled()
    {
        _memory.Remember(_base, "ref-a", "v1", "too-large", "1048577");

        Assert.False(_memory.TryRecall(_base, "ref-a", "v2", out _, out _));
    }

    [Theory]
    [InlineData("provider-unavailable")]
    [InlineData("provider-error")]
    [InlineData("rate-limited")]
    [InlineData("file-not-found")]
    [InlineData("download-blocked")]
    [InlineData("sync-api-error")]
    public void NonDeterministicCode_IsNeverStored(string code)
    {
        _memory.Remember(_base, "ref-a", "v1", code, null);

        Assert.Equal(0, _memory.Count);
        Assert.False(_memory.TryRecall(_base, "ref-a", "v1", out _, out _));
    }

    [Fact]
    public void KeepOnly_DropsTheReferencesThatAreNotPresent()
    {
        _memory.Remember(_base, "ref-a", "v1", "too-large", null);
        _memory.Remember(_base, "ref-b", "v1", "too-large", null);
        var other = Guid.NewGuid();
        _memory.Remember(other, "ref-a", "v1", "too-large", null);

        _memory.KeepOnly(_base, new HashSet<string>(["ref-b"], StringComparer.Ordinal));

        Assert.False(_memory.TryRecall(_base, "ref-a", "v1", out _, out _));
        Assert.True(_memory.TryRecall(_base, "ref-b", "v1", out _, out _));
        Assert.True(_memory.TryRecall(other, "ref-a", "v1", out _, out _));
    }

    [Fact]
    public void KeepOnlyBases_DropsTheBasesThatLeft()
    {
        var other = Guid.NewGuid();
        _memory.Remember(_base, "ref-a", "v1", "too-large", null);
        _memory.Remember(other, "ref-a", "v1", "too-large", null);

        _memory.KeepOnlyBases(new HashSet<Guid>([other]));

        Assert.Equal(0, _memory.CountFor(_base));
        Assert.Equal(1, _memory.CountFor(other));
    }

    [Fact]
    public void References_AreComparedAsTheyCame()
    {
        _memory.Remember(_base, "AbC", "v1", "too-large", null);

        Assert.False(_memory.TryRecall(_base, "abc", "v1", out _, out _));
    }
}
