using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Buteco.Workers.Infrastructure;

// Chamada uma única vez no startup, depois de host.Build() e antes de
// host.Run() — mesmo molde de RouteAuthenticationExtensions
// .ValidateRouteAuthenticationClassification em apps/inbox (design.md da
// change apps-workers-contexto-temporal, Achado 6/Decisão 4): checagem
// síncrona, throw InvalidOperationException, sem bypass. Primeira checagem
// de startup de apps/workers.
public static class TimeZoneStartupValidation
{
    // Compara o valor RESOLVIDO (via TimeProvider registrado no container,
    // não TimeProvider.System direto — para validar exatamente a instância
    // que a aplicação vai usar) contra o valor DECLARADO em TZ, não só a
    // presença da variável. TZ ausente/vazia/inválida resolve para um fuso
    // diferente do declarado (Achado 5/8) e falha aqui — sem caso especial
    // para UTC: TZ=UTC/TZ=Etc/UTC batem naturalmente com o Id resolvido.
    public static void ValidateTimeZoneConfiguration(this IHost host)
    {
        var timeProvider = host.Services.GetRequiredService<TimeProvider>();
        var declaredTimeZone = Environment.GetEnvironmentVariable("TZ");
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
