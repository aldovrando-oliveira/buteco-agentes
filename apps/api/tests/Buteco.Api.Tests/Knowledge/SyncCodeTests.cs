using Buteco.Api.KnowledgeSync;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>A forma de código de motivo (design.md da change catalogo-base-sincronizada, D1).</summary>
public class SyncCodeTests
{
    [Theory]
    [InlineData("access-denied")]
    [InlineData("api-not-configured")]
    [InlineData("too-large")]
    [InlineData("export-failed")]
    [InlineData("e403")]
    public void IsValid_AcceptsCodeShapedStrings(string code) => Assert.True(SyncCode.IsValid(code));

    [Theory]
    [InlineData("Sem acesso à pasta")]
    [InlineData("access denied")]
    [InlineData("Access-Denied")]
    [InlineData("access_denied")]
    [InlineData("-access")]
    [InlineData("access-")]
    [InlineData("access--denied")]
    [InlineData("access-denied\n")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_RefusesAnythingElse(string? code) => Assert.False(SyncCode.IsValid(code));

    [Fact]
    public void IsValid_RefusesMoreThan64Characters()
    {
        Assert.True(SyncCode.IsValid(new string('a', 64)));
        Assert.False(SyncCode.IsValid(new string('a', 65)));
    }
}
