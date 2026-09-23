using Buteco.Api.Options;
using Microsoft.Extensions.Options;

namespace Buteco.Api.Infrastructure;

/// <summary>
/// Checagem de integridade no startup (convenção 8), chamada depois do
/// <c>Build()</c> e antes do <c>Run()</c>.
///
/// <para>
/// <b>É CÓPIA de <c>TimeZoneStartupValidation</c> de <c>apps/workers</c>, e a
/// duplicação é consciente</b> (design.md, D2): os apps não referenciam código
/// um do outro, e extrair para <c>libs/</c> uma checagem de boot de quinze
/// linhas seria abstração antes do terceiro consumidor (convenção 2). Quem mudar
/// esta muda a outra — o mesmo arranjo que as entidades de métrica já têm.
/// </para>
///
/// <para>
/// <b>Ela subsume a checagem de "o nome resolve".</b> Não há um segundo guarda
/// só para nome inválido porque este já o cobre: <c>TZ</c> ausente, vazia,
/// inválida ou com o prefixo POSIX <c>:</c> resolve para um fuso DIFERENTE do
/// declarado, e a comparação reprova. Inventar uma segunda forma para o mesmo
/// propósito é o que diverge na primeira manutenção.
/// </para>
///
/// <para>
/// <b>Por que no boot, e não na primeira consulta:</b> um nome digitado errado
/// chega intacto até o <c>AT TIME ZONE</c> da agregação e falha ali, como
/// <b>500 no painel</b>, numa requisição de operador — muito depois e muito mais
/// longe da causa.
/// </para>
/// </summary>
public static class TimeZoneStartupValidation
{
    /// <summary>
    /// Compara o valor RESOLVIDO (via <see cref="TimeProvider"/> registrado no
    /// contêiner, não <c>TimeProvider.System</c> direto — para validar exatamente
    /// a instância que a aplicação vai usar) contra o valor DECLARADO em
    /// <see cref="MetricsOptions.TimeZone"/>, que vem de <c>TZ</c>.
    /// </summary>
    public static void ValidateTimeZoneConfiguration(this IHost host)
    {
        var timeProvider = host.Services.GetRequiredService<TimeProvider>();
        var declaredTimeZone = host.Services.GetRequiredService<IOptions<MetricsOptions>>().Value.TimeZone;
        var resolvedTimeZoneId = timeProvider.LocalTimeZone.Id;

        if (declaredTimeZone != resolvedTimeZoneId)
        {
            throw new InvalidOperationException(
                $"Fuso horário não configurado corretamente: TZ='{declaredTimeZone}' " +
                $"não corresponde ao fuso resolvido '{resolvedTimeZoneId}'. Defina TZ com " +
                "o nome IANA canônico do fuso desejado, sem o prefixo POSIX ':'.");
        }

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation(
            "Fuso horário do sistema: {TimeZoneId}, offset atual: {Offset}",
            resolvedTimeZoneId,
            timeProvider.GetLocalNow().Offset);
    }
}
