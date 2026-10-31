using System.Globalization;
using System.Threading.RateLimiting;

namespace Buteco.Api.Users.Login;

/// <summary>
/// O limite global de verificações de senha no <c>POST /auth/session</c> (design.md da
/// change usuarios-locais-login-emissor, D4). Global, e não por IP: o <c>apps/api</c> não
/// vê o IP do cliente (sem <c>ForwardedHeaders</c>, decisão da #179), e por IP é do proxy de
/// fora (#221). Duas camadas, encadeadas:
/// <list type="bullet">
/// <item><b>concorrência de 4, com fila de 16:</b> cada verificação custa ~19 MiB de
/// memória, e 4 juntas limitam o Argon2 a ~76 MiB;</item>
/// <item><b>token bucket de 10 por segundo, com rajada de 20:</b> o teto de CPU.</item>
/// </list>
/// Acima disso, <c>429</c> com <c>Retry-After: 1</c>, sem verificar senha. Constantes, e
/// não opção (convenção 2). O bloqueio por e-mail cuida da força bruta numa conta; este
/// cuida da pulverização por muitas contas.
/// </summary>
public static class LoginRateLimiting
{
    public const int ConcurrentVerifications = 4;
    public const int QueueLimit = 16;
    public const int TokensPerSecond = 10;
    public const int Burst = 20;
    public const int RetryAfterSeconds = 1;

    /// <summary>Metadado da rota limitada; o limitador global ignora as outras.</summary>
    public sealed class Marker;

    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };

            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsLimited(context)
                    ? RateLimitPartition.GetConcurrencyLimiter("login", _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = ConcurrentVerifications,
                        QueueLimit = QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    })
                    : RateLimitPartition.GetNoLimiter("outras")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsLimited(context)
                    ? RateLimitPartition.GetTokenBucketLimiter("login", _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = Burst,
                        TokensPerPeriod = TokensPerSecond,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })
                    : RateLimitPartition.GetNoLimiter("outras")));
        });

        return services;
    }

    private static bool IsLimited(HttpContext context) => context.GetEndpoint()?.Metadata.GetMetadata<Marker>() is not null;
}
