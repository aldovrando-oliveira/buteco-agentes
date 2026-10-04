using System.Reflection;
using Buteco.Connectors.Connectors;
using Buteco.Connectors.Sync;

namespace Buteco.Connectors.Tests;

/// <summary>
/// Os códigos da ligação com o <c>apps/api</c> (design.md da change
/// ciclo-de-sincronizacao, D9) têm a forma que o <c>apps/api</c> aceita como motivo
/// (<c>SyncCode</c>, D1 da #102). Lidos por reflexão, para uma constante nova entrar sem
/// alguém lembrar.
/// </summary>
public class SyncCodesTests
{
    private static readonly string[] Declared = typeof(SyncCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void DeclaredCodes_AreTheFourOfTheDesign() =>
        Assert.Equal(
            ["knowledge-base-not-found", "sync-api-error", "sync-api-unavailable", "sync-not-configured"],
            Declared.Order(StringComparer.Ordinal));

    [Fact]
    public void EveryCode_HasTheShapeOfACode() =>
        Assert.All(Declared, code => Assert.True(ConnectorCodes.IsValid(code), $"'{code}' não tem a forma de código."));

    // api-not-configured é a Drive API desligada; não pode ser reaproveitado aqui.
    [Fact]
    public void NoCode_CollidesWithAConnectorCode()
    {
        var connectorCodes = typeof(ConnectorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.Empty(Declared.Intersect(connectorCodes));
    }
}
