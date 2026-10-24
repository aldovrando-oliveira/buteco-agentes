namespace Buteco.Api.Identity.Keys;

/// <summary>
/// Middleware antes do <c>UseAuthentication</c> (design.md da change emissor-oidc-api,
/// D6). O OpenIddict monta as opções em toda requisição, de qualquer rota (F5), e sem
/// chave a montagem lança. Enquanto o anel não carregou, este gate tenta carregar e, se
/// não conseguir, responde <c>503</c> com <c>Retry-After</c>, sem guardar a falha.
///
/// <para>
/// Depois da carga o custo por requisição é ler um booleano. Com o emissor desligado,
/// o gate não é registrado.
/// </para>
/// </summary>
public sealed class IdentityKeyRingGate(RequestDelegate next, IdentityKeyRing ring, ILogger<IdentityKeyRingGate> logger)
{
    public static readonly EventId RingUnavailableEvent = new(1182, "IdentityKeyRingUnavailable");

    public const int RetryAfterSeconds = 5;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ring.IsLoaded)
        {
            try
            {
                await ring.EnsureLoadedAsync(context.RequestAborted);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
            {
                logger.LogWarning(
                    RingUnavailableEvent,
                    exception,
                    "Chaves do emissor OIDC indisponíveis; respondendo 503 até a próxima tentativa.");

                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return;
            }
        }

        await next(context);
    }
}
