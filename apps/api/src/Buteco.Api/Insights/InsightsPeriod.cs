using System.Globalization;

namespace Buteco.Api.Insights;

/// <summary>
/// Interpretação e validação da janela de período da rota de agregação.
///
/// <para>
/// <b>Separado do endpoint</b> porque é a única parte do contrato de janela que
/// a change B (<c>/insights/agents/{id}</c>) herda literalmente: ela reusa esta
/// classe e não redecide nada sobre limites, formato ou inversão.
/// </para>
/// </summary>
public static class InsightsPeriod
{
    public const string FromParameter = "from";

    public const string ToParameter = "to";

    public const string MissingBoundMessage = "O limite do período é obrigatório.";

    public const string MalformedBoundMessage =
        "Informe um instante válido em ISO 8601 (ex.: 2026-09-10T00:00:00Z). Sem deslocamento de fuso, o valor é interpretado como UTC.";

    public const string InvertedPeriodMessage = "O fim do período não pode ser anterior ao início.";

    /// <summary>
    /// Devolve os dois limites normalizados, ou os erros de validação — nunca os
    /// dois. Limites <b>inclusivos</b> nas duas pontas.
    /// </summary>
    public static bool TryResolve(
        string? from,
        string? to,
        out DateTimeOffset resolvedFrom,
        out DateTimeOffset resolvedTo,
        out Dictionary<string, string[]> errors)
    {
        resolvedFrom = default;
        resolvedTo = default;

        // Um único dicionário acumulado: dois parâmetros defeituosos saem numa
        // resposta só, não na primeira que falhar. Mesmo idioma de
        // MessageSummaryEndpoints (apps/inbox).
        errors = [];

        var parsedFrom = TryParseBound(from, FromParameter, errors);
        var parsedTo = TryParseBound(to, ToParameter, errors);

        if (errors.Count > 0)
        {
            return false;
        }

        // Só depois de os DOIS parsearem. Comparar valores não parseados não tem
        // sentido, e reportar "intervalo invertido" sobre um valor que o cliente
        // escreveu errado esconde o defeito real.
        if (parsedTo!.Value < parsedFrom!.Value)
        {
            errors[ToParameter] = [InvertedPeriodMessage];
            return false;
        }

        resolvedFrom = parsedFrom.Value;
        resolvedTo = parsedTo.Value;
        return true;
    }

    // AssumeUniversal | AdjustToUniversal não é detalhe de estilo, e os dois têm
    // papéis DIFERENTES:
    //
    //   AssumeUniversal  decide QUAL INSTANTE um valor sem deslocamento de fuso
    //                    ("2026-09-10") significa. Sem ele, o valor recebe o
    //                    offset LOCAL do processo.
    //   AdjustToUniversal normaliza o resultado para offset 0. Sem ele, um
    //                    DateTimeOffset com offset != 0 chega ao Npgsql, que o
    //                    RECUSA: "Cannot write DateTimeOffset with Offset=-03:00:00
    //                    to PostgreSQL type 'timestamp with time zone', only
    //                    offset 0 (UTC) is supported." (ArgumentException → 500).
    //
    // O comentário gêmeo em apps/inbox chama isso de "o pior formato possível de
    // bug de fuso", porque só aparece em servidor cujo TZ não seja UTC — e
    // apps/api ERA um servidor UTC até esta change lhe entregar TZ. O defeito
    // passou de inalcançável a alcançável exatamente aqui, e é por isso que o
    // guarda dele (InsightsEndpointsTests) só vale depois da tarefa 1.5.
    private static DateTimeOffset? TryParseBound(string? raw, string parameterName, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Ausente e malformado caem no MESMO ValidationProblem, de propósito:
            // a rota não tem duas formas de corpo conforme o tipo do defeito.
            errors[parameterName] = [MissingBoundMessage];
            return null;
        }

        if (!DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value))
        {
            errors[parameterName] = [MalformedBoundMessage];
            return null;
        }

        return value;
    }
}
