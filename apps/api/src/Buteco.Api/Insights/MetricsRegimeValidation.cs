using Buteco.Api.Options;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Insights;

/// <summary>
/// Checagem de integridade no startup (convenção 8), chamada depois do
/// <c>Build()</c> e antes do <c>Run()</c>: todo regime de medição declarado pelas
/// rotas de agregação tem instante em <c>Metrics:Regimes</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no boot, e não na primeira consulta</b> (design.md da change
/// <c>recusa-motivo-coleta</c>, D7): regime ausente do mapa <b>não falha</b> — ele
/// desliga o recorte daquele grupo de métricas. <c>RegimeStart</c> devolve
/// <c>null</c>, <c>Later</c> deixa a janela pedida valer inteira, e o período
/// anterior ao início real da coleta passa a responder <c>0</c> — "medi e não achei
/// nada" — onde a verdade é ausência de medição. Número plausível, em silêncio, na
/// rota cujo propósito inteiro é preservar essa distinção (convenção 13).
/// </para>
///
/// <para>
/// <b>Ela cobre os três regimes, não só o novo.</b> O aviso que já existia sobre
/// isso vivia em comentário no <c>appsettings.json</c>, e comentário não reprova
/// nada.
/// </para>
///
/// <para>
/// <b>O que ela NÃO cobre, e é item de fila que continua aberto:</b> chave presente
/// com valor errado. O instante é valor de UM ambiente num arquivo versionado, e
/// um ambiente cujo início real seja posterior ao declarado recebe <c>0</c> onde
/// deveria receber ausência. Esta checagem transforma "esqueci a chave" em falha de
/// boot; "pus a chave do outro ambiente" continua silencioso.
/// </para>
/// </remarks>
public static class MetricsRegimeValidation
{
    public static void ValidateMetricsRegimeConfiguration(this IHost host)
    {
        var regimes = host.Services.GetRequiredService<IOptions<MetricsOptions>>().Value.Regimes;

        var missing = MetricsOptions.DeclaredRegimes
            .Where(regime => !regimes.ContainsKey(regime))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Regime de medição sem instante configurado: {string.Join(", ", missing)}. " +
                $"Declare cada um em '{MetricsOptions.SectionName}:{nameof(MetricsOptions.Regimes)}' com o " +
                "instante do deploy da coleta correspondente — sem ele, a rota de agregação " +
                "responde 0 para período anterior à medição em vez de ausência.");
        }

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        foreach (var regime in MetricsOptions.DeclaredRegimes)
        {
            logger.LogInformation(
                "Regime de medição {MetricsRegime} medindo desde {RegimeStart}", regime, regimes[regime]);
        }
    }
}
