using System.Globalization;
using Buteco.Inbox.Contacts.Queries.GetSessionPeriodSummary;
using Buteco.Inbox.Contacts.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Inbox.Contacts.Endpoints;

/// <summary>
/// <c>GET /sessions/summary</c> — contagem de sessões iniciadas num intervalo.
/// </summary>
/// <remarks>
/// Namespace <c>Contacts</c> apesar da rota ser <c>/sessions/…</c>: a pasta
/// segue o domínio, não a URL, e <c>Session</c> vive sob <c>Contacts.Entities</c>
/// — é o que <see cref="ChannelSessionEndpoints"/> já faz, com rota
/// <c>/channels/{id}/sessions</c> (design.md, D10).
///
/// O prefixo <c>/sessions</c> de nível superior não é inaugurado aqui:
/// <c>MessageEndpoints</c> já registra <c>/sessions/{sessionId:guid}/messages</c>.
/// A ordem de registro entre os dois não precisa ser garantida — a restrição
/// <c>:guid</c> da rota vizinha exclui a cadeia literal <c>summary</c>, e o
/// roteamento classifica literal acima de parâmetro de qualquer forma (mesma
/// razão já escrita em <c>KnowledgeBaseEndpoints.cs:29-31</c>, <c>apps/api</c>).
/// </remarks>
public static class SessionSummaryEndpoints
{
    private const string FromParameter = "from";

    private const string ToParameter = "to";

    private const string MissingBoundMessage = "O limite do período é obrigatório.";

    private const string MalformedBoundMessage =
        "Informe um instante válido em ISO 8601 (ex.: 2026-09-10T00:00:00Z). Sem deslocamento de fuso, o valor é interpretado como UTC.";

    private const string InvertedPeriodMessage = "O fim do período não pode ser anterior ao início.";

    public static IEndpointRouteBuilder MapSessionSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sessions/summary", GetSessionPeriodSummaryAsync);

        return app;
    }

    // from/to entram como string?, NÃO como DateTimeOffset?, e isso é decisão
    // (design.md, D6): com o tipo de data na assinatura, um valor malformado
    // falha no BINDING, antes deste método rodar, e o ASP.NET Core devolve um
    // 400 com corpo próprio — forma diferente do ValidationProblem de todo o
    // resto da casa. A rota teria duas formas de erro conforme o defeito fosse
    // "malformado" ou "incoerente", e quem escrevesse o cliente descobriria isso
    // em produção.
    private static async Task<Results<Ok<SessionPeriodSummaryResponse>, ValidationProblem>> GetSessionPeriodSummaryAsync(
        string? from,
        string? to,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        // Um único dicionário acumulado, como ValidateShape já faz
        // (ChannelEndpoints.cs:135): dois parâmetros defeituosos saem numa
        // resposta só, não na primeira que falhar.
        var errors = new Dictionary<string, string[]>();

        var parsedFrom = TryParseBound(from, FromParameter, errors);
        var parsedTo = TryParseBound(to, ToParameter, errors);

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Só depois de os DOIS parsearem (design.md, D7). Comparar valores não
        // parseados não tem sentido, e reportar "intervalo invertido" sobre um
        // valor que o cliente escreveu errado esconde o defeito real.
        if (parsedTo!.Value < parsedFrom!.Value)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [ToParameter] = [InvertedPeriodMessage],
            });
        }

        var summary = await mediator.Send(
            new GetSessionPeriodSummaryQuery(parsedFrom.Value, parsedTo.Value), cancellationToken);

        return TypedResults.Ok(summary);
    }

    // AssumeUniversal | AdjustToUniversal não é detalhe de estilo, e os dois têm
    // papéis DIFERENTES — medido por mutação ao implementar, não suposto:
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
    // Ou seja: a falha não é número errado em silêncio, é 500 — e só num servidor
    // cujo TZ não seja UTC, porque com TZ=UTC o offset local já é 0 e o defeito
    // fica invisível. É o pior formato possível de bug de fuso: passa na máquina
    // de quem escreve se ela estiver em UTC (design.md, D6).
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
