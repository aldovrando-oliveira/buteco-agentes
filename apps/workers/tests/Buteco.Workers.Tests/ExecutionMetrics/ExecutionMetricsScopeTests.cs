using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using Buteco.Workers.ExecutionMetrics;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.Tests.ExecutionMetrics;

/// <summary>
/// Contrato do acumulador de métricas por execução (design.md da change
/// <c>metricas-execucao-coleta</c>, D2). Unitário, sem host nem banco — fora da
/// coleção de hosts de propósito, para não mexer no limiar de carga da suíte.
/// </summary>
public class ExecutionMetricsScopeTests
{
    private static readonly UsageDetails FullUsage = new() { InputTokenCount = 10, OutputTokenCount = 5, CachedInputTokenCount = 3 };

    [Fact]
    public void RecordingOutsideAnyScope_IsIgnored_AndDoesNotThrow()
    {
        Assert.Null(ExecutionMetricsScope.Current);

        ExecutionMetricsScope.RecordProviderCall("openai", "m", 1, FullUsage, failed: false, httpStatus: null);

        Assert.Null(ExecutionMetricsScope.Current);
    }

    /// <summary>
    /// O risco que o <c>AsyncLocal</c> traz, afirmado direto: duas execuções
    /// concorrentes no mesmo processo (é o que os testes de duas instâncias
    /// fazem, e o que um prefetch maior faria em produção) não podem gravar uma
    /// no escopo da outra.
    /// </summary>
    [Fact]
    public async Task TwoConcurrentScopes_DoNotMixTheirCalls()
    {
        var first = RunExecutionAsync("task-a", calls: 3);
        var second = RunExecutionAsync("task-b", calls: 2);

        var (firstCalls, secondCalls) = (await first, await second);

        Assert.Equal(3, firstCalls.Count);
        Assert.All(firstCalls, call => Assert.Equal("task-a", call));
        Assert.Equal(2, secondCalls.Count);
        Assert.All(secondCalls, call => Assert.Equal("task-b", call));

        static async Task<List<string>> RunExecutionAsync(string taskId, int calls)
        {
            using var scope = ExecutionMetricsScope.Begin(taskId);
            for (var index = 0; index < calls; index++)
            {
                await Task.Yield();
                ExecutionMetricsScope.RecordProviderCall("openai", "m", 1, FullUsage, failed: false, httpStatus: null);
            }

            return scope.ProviderCalls.Select(call => call.TaskId).ToList();
        }
    }

    /// <summary>
    /// <c>FunctionInvokingChatClient</c> pode invocar duas tools do mesmo turno
    /// em paralelo — registros concorrentes no MESMO escopo não podem se perder.
    /// </summary>
    [Fact]
    public async Task ParallelRecordsInTheSameScope_AreAllKept()
    {
        using var scope = ExecutionMetricsScope.Begin("task-paralela");

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() =>
            ExecutionMetricsScope.RecordProviderCall("openai", "m", 1, FullUsage, failed: false, httpStatus: null))));

        Assert.Equal(200, scope.ProviderCalls.Count);
    }

    [Fact]
    public async Task CompactionMark_AppliesOnlyWhileTheMarkedCallRuns()
    {
        using var scope = ExecutionMetricsScope.Begin("task-compactacao");

        await MarkedCallAsync();
        ExecutionMetricsScope.RecordProviderCall("openai", "m", 1, FullUsage, failed: false, httpStatus: null);

        Assert.Equal(
            [ExecutionMetricsValues.Purpose.Compaction, ExecutionMetricsValues.Purpose.Turn],
            scope.ProviderCalls.Select(call => call.Purpose));

        static async Task MarkedCallAsync()
        {
            using var _ = ExecutionMetricsScope.MarkPurpose(ExecutionMetricsValues.Purpose.Compaction);
            await Task.Yield();
            ExecutionMetricsScope.RecordProviderCall("openai", "m", 1, FullUsage, failed: false, httpStatus: null);
        }
    }

    // ── Nulo não é zero (convenção 13), nos três estados ─────────────────
    // O guarda é NEGATIVO de propósito: afirmar só `Null` passaria também se
    // alguém trocasse o tipo da coluna por não-anulável e o zero entrasse por
    // outro caminho; afirmar a AUSÊNCIA do zero é o que prende a normalização.

    [Fact]
    public void PartialUsage_KeepsTheUnreportedCounterNull_NotZero()
    {
        using var scope = ExecutionMetricsScope.Begin("task-parcial");

        ExecutionMetricsScope.RecordProviderCall(
            "openai", "m", 1, new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }, failed: false, httpStatus: null);

        var call = Assert.Single(scope.ProviderCalls);
        Assert.Null(call.CachedInputTokens);
        Assert.NotEqual(0, call.CachedInputTokens);
    }

    [Fact]
    public void MissingUsage_KeepsAllThreeCountersNull_NotZero()
    {
        using var scope = ExecutionMetricsScope.Begin("task-sem-uso");

        ExecutionMetricsScope.RecordProviderCall("openai", "m", 1, usage: null, failed: false, httpStatus: null);

        var call = Assert.Single(scope.ProviderCalls);
        Assert.Null(call.InputTokens);
        Assert.Null(call.OutputTokens);
        Assert.Null(call.CachedInputTokens);
        Assert.DoesNotContain(0L, new[] { call.InputTokens, call.OutputTokens, call.CachedInputTokens });
    }

    [Fact]
    public void ReportedZero_IsKeptAsZero()
    {
        using var scope = ExecutionMetricsScope.Begin("task-zero");

        ExecutionMetricsScope.RecordProviderCall(
            "openai", "m", 1, new UsageDetails { InputTokenCount = 10, CachedInputTokenCount = 0 }, failed: false, httpStatus: null);

        Assert.Equal(0, Assert.Single(scope.ProviderCalls).CachedInputTokens);
    }

    // ── Status HTTP só quando tipado (D12) ───────────────────────────────

    public static TheoryData<Exception, int?> StatusCases => new()
    {
        { new HttpRequestException("limite", null, HttpStatusCode.TooManyRequests), 429 },
        { new ClientResultException("credencial", new StubResponse(401)), 401 },
        { new InvalidOperationException("Provedor 'anthropic' não está configurado."), null },
    };

    [Theory]
    [MemberData(nameof(StatusCases))]
    public void HttpStatusOf_ReadsOnlyTypedStatus(Exception exception, int? expected) =>
        Assert.Equal(expected, ExecutionMetricsScope.HttpStatusOf(exception));

    private sealed class StubResponse(int status) : PipelineResponse
    {
        public override int Status => status;

        public override string ReasonPhrase => string.Empty;

        public override Stream? ContentStream { get; set; }

        public override BinaryData Content => BinaryData.Empty;

        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();

        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;

        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(BinaryData.Empty);

        public override void Dispose()
        {
        }
    }
}
