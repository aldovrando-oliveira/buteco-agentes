using Buteco.Workers.Infrastructure;
using Buteco.Workers.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buteco.Workers.Diagnostics;

/// <summary>
/// Varre periodicamente <c>a2a_tasks</c> e emite a série de leituras de tasks
/// não-terminais envelhecidas — a entrada que falta para decidir capacidade de
/// instâncias (`N ≥ C × D + 1`).
/// </summary>
/// <remarks>
/// <b>PRIMEIRO COMPONENTE PERIÓDICO DE apps/workers.</b> A fila de indexação
/// (<c>Knowledge/Indexing/KnowledgeIndexingConsumer</c>) é consumidor RabbitMQ
/// orientado a evento, não varredura; o único componente orientado a timer do
/// monorepo antes deste é <c>DebounceSweepService</c>, em <c>apps/inbox</c>,
/// cujo comentário ainda diz "único componente orientado a timer/scheduling do
/// projeto". Dele vem a <b>forma</b> (PeriodicTimer + escopo por ciclo +
/// try/catch por ciclo), nunca o intervalo — ver
/// <see cref="TaskDiagnosticsOptions"/>.
///
/// <para>
/// <b>COMO LER A SÉRIE — parte 1: o fator N.</b> A leitura é <b>global</b> (a
/// tabela inteira), então com <c>N</c> instâncias saem <c>N</c> linhas
/// <b>idênticas</b> por tique. Não são <c>N</c> medições: é a mesma medição
/// <c>N</c> vezes, e somar as linhas da série superestima por fator <c>N</c>.
/// É também por isso que esta varredura não fixa a resposta sobre número de
/// instâncias por acidente: o <b>valor</b> reportado não depende de <c>N</c>,
/// só o número de cópias depende.
/// </para>
///
/// <para>
/// <b>COMO LER A SÉRIE — parte 2: qual coluna responde o C.</b> É a de
/// <c>Submitted</c> envelhecido. Sob contenção as instâncias estão ocupadas
/// pelos <b>Sources</b>, cada um esperando, e cada um deixa atrás de si um
/// <b>Target publicado e nunca consumido</b> — um Target faminto por Source
/// delegando, mesma cardinalidade do <c>C</c>. A coluna de <c>Working</c>
/// <b>subestima por construção</b>, e o erro é silencioso: a janela é o timeout
/// de delegação, e é exatamente nesse ponto que o Source desiste, então ele só
/// aparece como envelhecido no intervalo estreito entre desistir e terminar o
/// turno. Quem olhar <c>Working</c> vai ver perto de zero e concluir que não há
/// contenção <b>precisamente quando há</b> (design.md, D10).
/// </para>
///
/// <para>
/// <b>POR QUE PERIÓDICA E NÃO SOB DEMANDA.</b> Detectar no momento de cada
/// expiração seria mais barato — zero serviço novo, zero duplicação por
/// instância. Foi recusado porque sob contenção sustentada <b>não há "momento
/// de cada expiração" que sirva</b>: com <c>C ≥ N</c> toda instância está
/// dentro da espera da delegação, e a observação que interessa é justamente a do
/// período em que nada conclui. E a detecção sob demanda nunca enxerga a task
/// que não tem <b>ninguém</b> esperando por ela. O laço periódico continua
/// girando nesse regime porque o bloqueio do consumidor é <c>await</c> puro
/// (<c>TaskJobConsumer</c> → <c>ExecuteAsync</c>, e a espera da delegação é
/// <c>await Task.Delay(PollInterval, …)</c>): nenhuma thread fica presa. Se
/// aquele caminho passar a bloquear thread, este desenho cai junto.
/// </para>
///
/// <para>
/// <b>ESTE COMPONENTE É TEMPORÁRIO, E TEM SUCESSOR NOMEADO.</b> A change
/// <c>metricas-execucao-coleta</c> vai gravar tempo de fila e origem como
/// métrica de verdade e <b>substitui</b> esta varredura. A duplicação até lá é
/// consciente; quem for fazer aquela change herda esta decisão e precisa
/// reavaliá-la, não descobri-la depois.
/// </para>
/// </remarks>
public sealed class NonTerminalTaskDetectorService(
    IServiceScopeFactory scopeFactory,
    NonTerminalTaskDetector detector,
    IOptions<TaskDiagnosticsOptions> diagnosticsOptions,
    IOptions<AgentDelegationToolOptions> delegationOptions,
    ILogger<NonTerminalTaskDetectorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = diagnosticsOptions.Value.SweepInterval;

        // Emitido SEMPRE, no boot: é o que torna "nenhum achado" distinguível
        // de "a varredura não está rodando". Sem ele, silêncio seria ambíguo, e
        // ambiguidade num instrumento de diagnóstico é o defeito que ele existe
        // para não ter.
        logger.LogInformation(
            "Varredura de tasks não-terminais ativa: janela de {Window} (derivada do timeout de delegação), intervalo de {SweepInterval}.",
            delegationOptions.Value.Timeout,
            interval);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepAsync(stoppingToken);
        }
    }

    /// <summary>
    /// Um ciclo. O <c>try</c> abre <b>antes</b> da abertura do escopo e da
    /// consulta, não só em volta da consulta.
    /// </summary>
    /// <remarks>
    /// Convenção 4, e a cláusula já mordeu duas vezes nesta base
    /// (<c>DebounceSweepService</c>,
    /// <c>PushNotificationEndpoints.DeliverResponseAsync</c>): um
    /// <c>try/catch</c> correto existe, mas a chamada que mais realisticamente
    /// falha — a consulta ao banco — está posicionada fora dele, e a exceção
    /// escapa antes de chegar à proteção. Aqui a resolução do escopo e a
    /// consulta estão as duas dentro.
    /// </remarks>
    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var report = await detector.DetectAsync(dbContext, delegationOptions.Value.Timeout, cancellationToken);

            if (!report.HasFindings)
            {
                logger.LogDebug(
                    "Nenhuma task não-terminal além da janela de {Window}.",
                    report.Window);
                return;
            }

            // Descreve o que foi observado — estado, idade e janela — e nada
            // além disso. Nenhuma palavra de veredito ("travada", "presa"),
            // porque um turno com várias chamadas de tool de delegação
            // ultrapassa a janela legitimamente e o sistema não distingue os
            // dois casos (D3/convenção 13).
            logger.LogWarning(
                "Tasks não-terminais além da janela de {Window}: {CountsByState}. " +
                "Mais antiga: {OldestTaskId}, com {OldestAge} desde a última transição de estado.",
                report.Window,
                string.Join(", ", report.CountsByState.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")),
                report.OldestTaskId,
                report.OldestAge);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao varrer tasks não-terminais; o próximo ciclo segue normalmente.");
        }
    }
}
