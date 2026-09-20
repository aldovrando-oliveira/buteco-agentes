using A2A;
using Buteco.Workers.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Buteco.Workers.Diagnostics;

/// <summary>
/// Conta as tasks em estado <b>não-terminal</b> cuja última transição de estado
/// é mais antiga que uma janela, agrupadas <b>por estado</b>.
/// </summary>
/// <remarks>
/// <b>Por que por estado, e nunca um total único:</b> `Submitted` envelhecida
/// significa task publicada e <b>nunca consumida</b> por instância nenhuma —
/// contenção, o caso que o `C ≥ N` produz; `Working` envelhecida significa task
/// <b>consumida e ainda em execução</b>. São duas causas diferentes, e um total
/// as apagaria.
///
/// <para>
/// <b>Sem <c>ILogger</c> aqui.</b> A emissão é de
/// <see cref="NonTerminalTaskDetectorService"/>; separar as duas é o que permite
/// afirmar a semântica da consulta em teste direto, sem host — convenção 15,
/// segunda forma (o guarda tem que afirmar a garantia no componente que a
/// correção toca).
/// </para>
///
/// <para>
/// A consulta usa o índice que já existe em <c>a2a_tasks(state)</c>
/// (<c>AppDbContext</c>), então esta change não cria índice nem migration — e
/// portanto não há nada a espelhar no modelo de <c>apps/api</c>.
/// </para>
/// </remarks>
public sealed class NonTerminalTaskDetector(TimeProvider timeProvider)
{
    /// <summary>
    /// Estados não-terminais, escritos como o <c>PostgresTaskStore</c> os grava
    /// (<c>TaskState.ToString()</c>). Fechado de propósito: é o complemento de
    /// <c>Completed/Failed/Rejected/Canceled</c>, e um estado novo no SDK
    /// aparece aqui como ausência, não como falso positivo.
    /// </summary>
    private static readonly string[] NonTerminalStates =
    [
        nameof(TaskState.Submitted),
        nameof(TaskState.Working),
    ];

    public async Task<NonTerminalTaskReport> DetectAsync(
        AppDbContext dbContext, TimeSpan window, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now - window;

        var aged = await dbContext.A2ATasks
            .AsNoTracking()
            .Where(task => NonTerminalStates.Contains(task.State)
                        && task.StatusTimestamp != null
                        && task.StatusTimestamp < cutoff)
            .Select(task => new { task.TaskId, task.State, task.StatusTimestamp })
            .ToListAsync(cancellationToken);

        if (aged.Count == 0)
        {
            return new NonTerminalTaskReport(window, new Dictionary<string, int>(), null, null);
        }

        var countsByState = aged
            .GroupBy(task => task.State)
            .ToDictionary(group => group.Key, group => group.Count());

        var oldest = aged.MinBy(task => task.StatusTimestamp)!;

        return new NonTerminalTaskReport(
            window,
            countsByState,
            oldest.TaskId,
            now - oldest.StatusTimestamp!.Value);
    }
}

/// <summary>
/// O que a varredura observou. <b>Descreve, não julga</b> — não há campo de
/// veredito, e é deliberado: um turno de agente que encadeia várias chamadas de
/// tool de delegação ultrapassa a janela legitimamente, e o sistema não
/// distingue isso de uma task travada (design.md, D3; convenção 13).
/// </summary>
/// <param name="Window">
/// A janela usada. Vai junto do resultado <b>na mesma emissão</b>: contagem sem
/// a janela ao lado não diz sobre o quê foi medida (convenção 22).
/// </param>
/// <param name="CountsByState">
/// Contagem por estado não-terminal. Vazio quando nada envelheceu — e vazio
/// aqui é uma <b>medição que aconteceu e deu zero</b>, não ausência de medição.
/// </param>
public sealed record NonTerminalTaskReport(
    TimeSpan Window,
    IReadOnlyDictionary<string, int> CountsByState,
    string? OldestTaskId,
    TimeSpan? OldestAge)
{
    public bool HasFindings => CountsByState.Count > 0;

    public int CountFor(string state) => CountsByState.TryGetValue(state, out var count) ? count : 0;
}
