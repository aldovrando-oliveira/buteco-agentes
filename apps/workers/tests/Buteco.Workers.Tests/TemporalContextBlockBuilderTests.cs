using Buteco.Workers.Agents;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Workers.Tests;

/// <summary>
/// Cobre a change apps-workers-contexto-temporal, Seção 5 do tasks.md —
/// testes unitários puros do construtor do bloco de contexto temporal, sem
/// Testcontainers: <see cref="TemporalContextBlockBuilder"/> é uma função
/// pura, testável isoladamente com <see cref="FakeTimeProvider"/> (design.md,
/// Decisão 1). Fuso fixado via <see cref="TimeZoneInfo.CreateCustomTimeZone"/>
/// (offset fixo, não depende da tz database estar presente no host que roda
/// o teste — mesmo motivo por trás de <see cref="TimeZoneInfo.Local"/> nunca
/// ser usado diretamente pelo construtor).
/// </summary>
public class TemporalContextBlockBuilderTests
{
    private static readonly TimeZoneInfo MinusThreeOffset =
        TimeZoneInfo.CreateCustomTimeZone("Test-03:00", TimeSpan.FromHours(-3), "Test -03:00", "Test -03:00");

    private const string NotAUserMessageMarker =
        "[Contexto temporal — não é uma mensagem do usuário, não responda a ele diretamente]";

    [Fact]
    public void Build_KnownInstant_IncludesCorrectPortugueseDayOfWeek()
    {
        // 2026-03-10 é uma terça-feira — verificado de forma independente
        // (date -j -f "%Y-%m-%d" "2026-03-10" "+%A"), não assumido.
        var timeProvider = BuildFakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3)));

        var block = TemporalContextBlockBuilder.Build(timeProvider);

        Assert.Contains("(terça-feira)", block);
    }

    [Fact]
    public void Build_KnownInstant_IncludesIso8601TimestampWithOffset()
    {
        var timeProvider = BuildFakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 5, 30, TimeSpan.FromHours(-3)));

        var block = TemporalContextBlockBuilder.Build(timeProvider);

        Assert.Contains("2026-03-10T10:05:30-03:00", block);
    }

    [Fact]
    public void Build_WithoutMessageInstant_ContainsCollapsedPrecedenceRule()
    {
        var timeProvider = BuildFakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3)));

        var block = TemporalContextBlockBuilder.Build(timeProvider);

        Assert.Contains("se resolve contra o instante acima.", block);
    }

    [Fact]
    public void Build_WithMessageInstant_ContainsFullPrecedenceRule()
    {
        var timeProvider = BuildFakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3)));
        var messageInstant = new DateTimeOffset(2026, 3, 10, 9, 58, 0, TimeSpan.FromHours(-3));

        var block = TemporalContextBlockBuilder.Build(timeProvider, messageInstant);

        Assert.Contains("se resolve contra o", block);
        Assert.Contains("instante da mensagem acima, não contra o instante de processamento", block);
    }

    [Fact]
    public void Build_WithoutMessageInstant_DoesNotPresentSecondInstantOrGapLine()
    {
        var timeProvider = BuildFakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3)));

        var block = TemporalContextBlockBuilder.Build(timeProvider);

        Assert.DoesNotContain("Instante da mensagem", block);
        Assert.DoesNotContain("Defasagem", block);
    }

    [Fact]
    public void Build_WithMessageInstant_PresentsBothInstants()
    {
        var timeProvider = BuildFakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3)));
        var messageInstant = new DateTimeOffset(2026, 3, 10, 9, 58, 0, TimeSpan.FromHours(-3));

        var block = TemporalContextBlockBuilder.Build(timeProvider, messageInstant);

        Assert.Contains("Instante de processamento", block);
        Assert.Contains("Instante da mensagem", block);
    }

    [Fact]
    public void Build_GapBelowThreshold_DoesNotIncludeGapLine()
    {
        var processingInstant = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3));
        var timeProvider = BuildFakeTimeProvider(processingInstant);
        var messageInstant = processingInstant - TimeSpan.FromMinutes(2); // abaixo do limiar de 5 minutos

        var block = TemporalContextBlockBuilder.Build(timeProvider, messageInstant);

        Assert.DoesNotContain("Defasagem", block);
    }

    [Fact]
    public void Build_GapAboveThreshold_IncludesGapLineWithHumanReadableDuration()
    {
        var processingInstant = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3));
        var timeProvider = BuildFakeTimeProvider(processingInstant);
        // 1 dia e 33 minutos de defasagem — acima do limiar de 5 minutos.
        var messageInstant = processingInstant - TimeSpan.FromDays(1) - TimeSpan.FromMinutes(33);

        var block = TemporalContextBlockBuilder.Build(timeProvider, messageInstant);

        Assert.Contains("[Defasagem entre os dois: 1 dia e 33 minutos", block);
    }

    [Fact]
    public void Concatenate_NonEmptyAgentInstructions_PlacesTemporalBlockAfterWithSeparator()
    {
        var result = TemporalContextBlockBuilder.Concatenate("Responda com simpatia.", NotAUserMessageMarker);

        Assert.Equal($"Responda com simpatia.\n\n{NotAUserMessageMarker}", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Concatenate_EmptyOrWhitespaceAgentInstructions_ProducesOnlyTheBlockWithoutOrphanSeparator(string? agentInstructions)
    {
        var result = TemporalContextBlockBuilder.Concatenate(agentInstructions, NotAUserMessageMarker);

        Assert.Equal(NotAUserMessageMarker, result);
        Assert.False(result.StartsWith('\n'), "não deve começar com o separador órfão");
    }

    [Fact]
    public void Build_ThenConcatenate_MarkerAppearsExactlyOnceInFinalInstructions()
    {
        var timeProvider = BuildFakeTimeProvider(new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.FromHours(-3)));
        var block = TemporalContextBlockBuilder.Build(timeProvider);

        var finalInstructions = TemporalContextBlockBuilder.Concatenate("Instruções do operador.", block);

        var occurrences = CountOccurrences(finalInstructions, NotAUserMessageMarker);
        Assert.Equal(1, occurrences);
    }

    private static FakeTimeProvider BuildFakeTimeProvider(DateTimeOffset instant)
    {
        // FakeTimeProvider armazena o DateTimeOffset do construtor como
        // "now" verbatim — GetUtcNow()/GetLocalNow() só calculam certo se
        // esse valor for de fato UTC (offset zero); passar um DateTimeOffset
        // com offset -03:00 direto faz GetLocalNow() aplicar o offset do
        // fuso duas vezes (achado ao rodar este teste, não assumido —
        // convenção 6). ToUniversalTime() normaliza antes de guardar,
        // mantendo o instante que o teste pretende.
        var timeProvider = new FakeTimeProvider(instant.ToUniversalTime());
        timeProvider.SetLocalTimeZone(MinusThreeOffset);
        return timeProvider;
    }

    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }
}
