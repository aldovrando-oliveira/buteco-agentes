using System.Reflection;
using Buteco.Api.KnowledgeDocuments.Extraction;
using Buteco.Api.KnowledgeSync;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// Os códigos de recusa de conteúdo vão para a lista de ignorados da base
/// sincronizada (#105), que aceita só a forma de <see cref="SyncCode"/> (D1 da change
/// catalogo-base-sincronizada). Lidos por reflexão para que uma constante nova entre
/// na verificação sem ninguém lembrar de acrescentá-la aqui.
/// </summary>
public class KnowledgeContentRefusalCodesTests
{
    private static readonly string[] DeclaredCodes = typeof(KnowledgeContentRefusalCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void DeclaredCodes_AreTheFourContentRefusals()
    {
        Assert.Equal(
            ["empty-content", "null-character", "too-large", "unsupported-source-type"],
            DeclaredCodes.Order());
    }

    [Fact]
    public void EveryDeclaredCode_HasTheShapeOfACode()
    {
        Assert.All(DeclaredCodes, code => Assert.True(SyncCode.IsValid(code), $"'{code}' não tem a forma de código."));
    }
}
