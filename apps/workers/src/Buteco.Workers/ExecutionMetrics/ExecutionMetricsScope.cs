using System.ClientModel;
using Buteco.Workers.ExecutionMetrics.Entities;
using Microsoft.Extensions.AI;

namespace Buteco.Workers.ExecutionMetrics;

/// <summary>
/// Acumulador, em memória, das métricas de UMA execução de task — as chamadas ao
/// provedor e os resultados de delegação —, alcançável de qualquer ponto do fluxo
/// assíncrono dela por um <see cref="AsyncLocal{T}"/> (design.md da change
/// <c>metricas-execucao-coleta</c>, D2).
/// </summary>
/// <remarks>
/// <para>
/// <b>POR QUE <c>AsyncLocal</c> E NÃO UM PARÂMETRO.</b> Quem mede a requisição é
/// <c>LlmCallDurationChatClient</c>, e ele é compartilhado por
/// <c>(provider, model)</c> durante a vida do processo — não sabe de qual task é
/// a chamada. As duas saídas óbvias estão fechadas: construir um wrapper por
/// execução, carregando o <c>taskId</c>, reintroduz o vazamento de pool que
/// <c>fix-vazamento-httpclient-chat</c> corrigiu (<c>DelegatingChatClient.Dispose()</c>
/// descarta o inner em cascata); injetar um coletor por construtor custaria os
/// 14 harness de teste que registram <c>AgentExecutionService</c>, sem factory
/// compartilhada, e quebraria em runtime, não em compilação. O fluxo de
/// <c>ExecuteAsync</c> até <c>GetResponseAsync</c> é inteiramente
/// <c>await</c>ado — inclusive a chamada de compactação e as tools —, então o
/// valor alcança todo mundo sem que nenhum construtor mude.
/// </para>
///
/// <para>
/// <b><see cref="Begin"/> SOBRESCREVE, NUNCA HERDA.</b> Cada execução abre o seu
/// escopo, mesmo que o <c>ExecutionContext</c> do callback já traga um. Hoje não
/// traz — o dispatcher do RabbitMQ roda com o contexto de quando o consumidor foi
/// registrado —, mas se algum dia trouxer, a execução seguinte ainda grava no
/// escopo dela, e não no de quem a publicou (o caso concreto seria uma tool de
/// delegação publicando a task do alvo de dentro do fluxo do Source).
/// </para>
///
/// <para>
/// <b>Sob <c>lock</c></b> porque <c>FunctionInvokingChatClient</c> pode invocar
/// duas tools do mesmo turno em paralelo — duas delegações, por exemplo.
/// </para>
///
/// <para>
/// <b>Fora de qualquer escopo, registrar é no-op</b>, e não erro: o client é usado
/// fora de execução nos testes dele e em qualquer caminho futuro que não seja
/// task. Métrica nunca derruba quem mede.
/// </para>
/// </remarks>
public sealed class ExecutionMetricsScope : IDisposable
{
    private static readonly AsyncLocal<ExecutionMetricsScope?> CurrentScope = new();
    private static readonly AsyncLocal<string?> CurrentPurpose = new();

    private readonly object _gate = new();
    private readonly List<ProviderCall> _providerCalls = [];
    private readonly List<DelegationOutcome> _delegationOutcomes = [];
    private readonly ExecutionMetricsScope? _previous;

    private ExecutionMetricsScope(string taskId, ExecutionMetricsScope? previous)
    {
        TaskId = taskId;
        _previous = previous;
    }

    /// <summary>O escopo da execução em andamento neste fluxo, ou nulo.</summary>
    public static ExecutionMetricsScope? Current => CurrentScope.Value;

    public string TaskId { get; }

    /// <summary>
    /// Abre o escopo da execução <paramref name="taskId"/>, substituindo o que o
    /// fluxo trouxer. O descarte restaura o anterior.
    /// </summary>
    public static ExecutionMetricsScope Begin(string taskId)
    {
        var scope = new ExecutionMetricsScope(taskId, CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>
    /// Marca a finalidade das requisições feitas até o descarte do valor
    /// devolvido. Sem marca, a finalidade é
    /// <see cref="ExecutionMetricsValues.Purpose.Turn"/>.
    /// </summary>
    public static IDisposable MarkPurpose(string purpose)
    {
        var previous = CurrentPurpose.Value;
        CurrentPurpose.Value = purpose;
        return new PurposeRestore(previous);
    }

    /// <summary>
    /// Registra uma requisição ao provedor no escopo corrente. Tokens copiados
    /// de <paramref name="usage"/> SEM normalizar nulo — nulo é "o provedor não
    /// reportou", e zero só entra quando o provedor reportou zero (convenção 13).
    /// </summary>
    public static void RecordProviderCall(
        string provider, string model, double durationMs, UsageDetails? usage, bool failed, int? httpStatus)
    {
        var scope = CurrentScope.Value;
        if (scope is null)
        {
            return;
        }

        var call = new ProviderCall(
            scope.TaskId,
            provider,
            model,
            CurrentPurpose.Value ?? ExecutionMetricsValues.Purpose.Turn,
            durationMs,
            usage?.InputTokenCount,
            usage?.OutputTokenCount,
            usage?.CachedInputTokenCount,
            failed,
            httpStatus);

        lock (scope._gate)
        {
            scope._providerCalls.Add(call);
        }
    }

    /// <summary>Registra o resultado de uma delegação no escopo corrente.</summary>
    public static void RecordDelegation(DelegationOutcome outcome)
    {
        var scope = CurrentScope.Value;
        if (scope is null)
        {
            return;
        }

        lock (scope._gate)
        {
            scope._delegationOutcomes.Add(outcome);
        }
    }

    /// <summary>
    /// Status HTTP da exceção, só quando o SDK o expõe TIPADO (D12):
    /// <see cref="HttpRequestException.StatusCode"/> e
    /// <see cref="ClientResultException.Status"/> (OpenAI, via
    /// <c>System.ClientModel</c>). Qualquer outra exceção — inclusive a
    /// hierarquia própria do SDK da Anthropic — devolve nulo: não se sabe, e o
    /// nulo diz isso em vez de um código inventado.
    /// </summary>
    public static int? HttpStatusOf(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } statusCode } => (int)statusCode,
        ClientResultException { Status: > 0 } clientResult => clientResult.Status,
        _ => null,
    };

    public IReadOnlyList<ProviderCall> ProviderCalls
    {
        get
        {
            lock (_gate)
            {
                return [.. _providerCalls];
            }
        }
    }

    public IReadOnlyList<DelegationOutcome> DelegationOutcomes
    {
        get
        {
            lock (_gate)
            {
                return [.. _delegationOutcomes];
            }
        }
    }

    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = _previous;
        }
    }

    private sealed class PurposeRestore(string? previous) : IDisposable
    {
        public void Dispose() => CurrentPurpose.Value = previous;
    }
}
