using Buteco.Api.Knowledge.Indexing;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests.Knowledge;

/// <summary>
/// A janela de D8 (design.md da change indexacao-sem-job-orfao) isolada, com relógio
/// falso. Que a falha da consulta da varredura não a abre está provado no despacho
/// (<c>SweepQueryFailure_Throws_AndDoesNotOpenTheWindow</c>), porque é ele que decide
/// quando chamar <see cref="KnowledgeIndexingDispatchWindow.Open"/>.
/// </summary>
public class KnowledgeIndexingDispatchWindowTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly KnowledgeIndexingRequestSchedule _schedule = new();

    private KnowledgeIndexingDispatchWindow NewWindow() => new(_time, _schedule);

    [Fact]
    public void StartsClosed()
    {
        Assert.False(NewWindow().IsOpen);
    }

    [Fact]
    public void OpensOnFailure_ForThirtySeconds()
    {
        var window = NewWindow();

        window.Open();

        Assert.True(window.IsOpen);
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.True(window.IsOpen);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(window.IsOpen);
    }

    [Fact]
    public void ClosesOnSuccess_BeforeItExpires()
    {
        var window = NewWindow();
        window.Open();
        _time.Advance(TimeSpan.FromSeconds(5));

        window.Close();

        Assert.False(window.IsOpen);
    }

    [Fact]
    public void AFailureAfterExpiry_ReopensForAnotherFullWindow()
    {
        var window = NewWindow();
        window.Open();
        _time.Advance(TimeSpan.FromSeconds(31));
        Assert.False(window.IsOpen);

        window.Open();
        _time.Advance(TimeSpan.FromSeconds(29));

        Assert.True(window.IsOpen);
    }

    [Fact]
    public void WindowLength_IsTheSweepInterval()
    {
        Assert.Equal(_schedule.SweepInterval, _schedule.SkipWindow);
        Assert.Equal(TimeSpan.FromSeconds(30), _schedule.SkipWindow);
        Assert.Equal(TimeSpan.FromSeconds(5), _schedule.RequestDispatchTimeout);
    }
}
