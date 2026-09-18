using System.Globalization;
using Buteco.Inbox.Messages.Queries.GetMessagePeriodSummary;
using Buteco.Inbox.Messages.Responses;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Buteco.Inbox.Messages.Endpoints;

/// <summary>
/// <c>GET /messages/summary</c> — contagem de mensagens de entrada recebidas num
/// intervalo.
/// </summary>
/// <remarks>
/// <b>Esta é a PRIMEIRA rota de nível superior sob <c>/messages</c> do
/// <c>apps/inbox</c></b> (design.md, D2), e a observação é o inverso da que está
/// em <see cref="Contacts.Endpoints.SessionSummaryEndpoints"/>: lá o prefixo
/// <c>/sessions</c> <b>já existia</b> e a rota não inaugurava namespace. Aqui
/// inaugura — <see cref="MessageEndpoints"/> só registra
/// <c>/sessions/{sessionId:guid}/messages</c>, aninhado sob <c>/sessions</c>, e
/// nenhuma das sete registradoras de <c>Program.cs:132-138</c> abre
/// <c>/messages</c>.
///
/// <b>E inaugurar prefixo não cria caso novo de autenticação.</b>
/// <c>Program.cs:139-146</c> classifica toda rota como autenticada <b>por
/// padrão</b> e falha o startup para rota não classificada; a lista passada a
/// <c>ValidateRouteAuthenticationClassification</c> é só das <b>anônimas</b>.
/// Esta rota não a toca, e se alguém a esquecer o startup reprova sozinho.
///
/// A frase acima existe porque o silêncio seria pior que a redundância: quem
/// chegar aqui vindo de <c>SessionSummaryEndpoints</c> encontra lá um parágrafo
/// dizendo "o prefixo já existe" e poderia transportar a conclusão sem reconferir.
///
/// <b>Sem parágrafo sobre ordem de registro</b>, ao contrário do vizinho: lá havia
/// rota irmã (<c>/sessions/{sessionId:guid}/messages</c>) sob o mesmo prefixo e a
/// defesa era sobre literal × parâmetro. Aqui não há rota irmã sob
/// <c>/messages</c> — não há nada contra o que defender a ordem.
/// </remarks>
public static class MessageSummaryEndpoints
{
    private const string FromParameter = "from";

    private const string ToParameter = "to";

    private const string MissingBoundMessage = "O limite do período é obrigatório.";

    private const string MalformedBoundMessage =
        "Informe um instante válido em ISO 8601 (ex.: 2026-09-10T00:00:00Z). Sem deslocamento de fuso, o valor é interpretado como UTC.";

    private const string InvertedPeriodMessage = "O fim do período não pode ser anterior ao início.";

    public static IEndpointRouteBuilder MapMessageSummaryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/messages/summary", GetMessagePeriodSummaryAsync);

        return app;
    }

    // from/to entram como string?, NÃO como DateTimeOffset?, e isso é decisão
    // herdada (design.md, D8; medida na change anterior): com o tipo de data na
    // assinatura, um valor malformado falha no BINDING, antes deste método rodar,
    // e o ASP.NET Core devolve um 400 com corpo próprio — forma diferente do
    // ValidationProblem de todo o resto da casa. A rota teria duas formas de erro
    // conforme o defeito fosse "malformado" ou "incoerente".
    private static async Task<Results<Ok<MessagePeriodSummaryResponse>, ValidationProblem>> GetMessagePeriodSummaryAsync(
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

        // Só depois de os DOIS parsearem (design.md, D8). Comparar valores não
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
            new GetMessagePeriodSummaryQuery(parsedFrom.Value, parsedTo.Value), cancellationToken);

        return TypedResults.Ok(summary);
    }

    // AssumeUniversal | AdjustToUniversal não é detalhe de estilo, e os dois têm
    // papéis DIFERENTES — medido por mutação na change anterior, não suposto:
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
    // fica invisível.
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
