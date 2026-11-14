using System.Security.Claims;
using System.Text.Json;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Flow;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users;
using Buteco.Api.Users.Federated;
using Buteco.Api.Users.Login;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenIddict.Client.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Identity.Federated;

/// <summary>
/// O login federado (design.md da change provedores-federados-google, D1, D5 e D7): o início, o retorno
/// do provedor com a decisão do Buteco, a rota que lê o aviso da recusa e a lista anônima dos provedores.
///
/// <list type="bullet">
/// <item><b>o retorno decide só pelo par</b> (issuer fixo do tipo, <c>sub</c>), nunca pelo e-mail; o
/// e-mail só serve, sem vínculo e verificado, para escolher o motivo da recusa;</item>
/// <item><b>a entrada grava a sessão pendente</b> (o <c>buteco-sessao</c> sem o claim das 2 etapas) e manda
/// ao <c>/entrar</c>, nunca ao <c>/connect/authorize</c>: a navegação que volta do provedor começou em outro
/// site, e o cookie <c>Strict</c> só acompanha o pedido seguinte que parte do painel (D5);</item>
/// <item><b>nenhuma recusa grava sessão</b>, e o log nunca leva e-mail, <c>sub</c>, código nem token.</item>
/// </list>
/// </summary>
public static partial class FederatedLoginEndpoints
{
    public static readonly EventId RefusedEvent = new(1198, "FederatedLoginRefused");
    public static readonly EventId FailedEvent = new(1199, "FederatedLoginFailed");
    public static readonly EventId ConnectedEvent = new(1200, "FederatedIdentityConnected");
    public static readonly EventId DisconnectedEvent = new(1201, "FederatedIdentityDisconnected");

    public const string NotConfiguredMessage = "Provedor não configurado.";

    public const string LoggerCategory = "Buteco.Api.Identity.Federated.FederatedLogin";

    public sealed record StartRequest(string? Retorno);

    public sealed record LoginProvider(string Chave, string Tipo);

    public sealed record LoginProvidersResponse(IReadOnlyList<LoginProvider> Provedores);

    public static IEndpointRouteBuilder MapFederatedLoginEndpoints(this IEndpointRouteBuilder app)
    {
        Anonymous(app.MapPost(FederatedLogin.StartPath, StartAsync)).WithMetadata(new LoginRateLimiting.Marker());
        Anonymous(app.MapGet(FederatedLogin.CallbackPath, CallbackAsync));
        Anonymous(app.MapGet(FederatedLogin.ProvidersPath, ListForLogin));
        Anonymous(app.MapPost(FederatedLogin.RefusalPath, ReadRefusal));
        return app;
    }

    private static RouteHandlerBuilder Anonymous(RouteHandlerBuilder route) =>
        route.AllowAnonymous().WithMetadata(new AnonymousRouteClassification(AnonymousRouteReason.AuthEntryPoint));

    private static async Task<IResult> StartAsync(
        string chave, HttpContext httpContext, SessionRequestGuards guards, FederatedProviderCatalog catalog, CancellationToken cancellationToken)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        StartRequest? request;
        try
        {
            request = await httpContext.Request.ReadFromJsonAsync<StartRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.Problem(title: "Corpo do pedido inválido.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (catalog.Find(chave) is not { } provider)
        {
            return NotConfigured();
        }

        // O retorno só é seguido quando é um authorize, e com exatamente a string recebida, como no
        // /entrar (painel-entra-pelo-emissor, D6); outro valor é descartado.
        var retorno = request?.Retorno is { } value && value.StartsWith($"{IdentityIssuerEndpoints.Authorization}?", StringComparison.Ordinal)
            ? value
            : null;

        return await FederatedChallenge.BeginAsync(httpContext, provider, new Dictionary<string, string?>
        {
            [FederatedLogin.Properties.Intent] = FederatedLogin.Intents.Login,
            [FederatedLogin.Properties.Return] = retorno,
        });
    }

    public static IResult NotConfigured() =>
        TypedResults.Problem(title: NotConfiguredMessage, statusCode: StatusCodes.Status404NotFound);

    private static IResult ListForLogin(FederatedProviderCatalog catalog) =>
        TypedResults.Ok(new LoginProvidersResponse(catalog.Providers.Select(provider => new LoginProvider(provider.Key, provider.Type)).ToList()));

    private static IResult ReadRefusal(HttpContext httpContext, SessionRequestGuards guards, FederatedRefusalCookie refusal)
    {
        if (guards.Reject(httpContext.Request) is { } rejected)
        {
            return rejected;
        }

        // Sem cookie, vencido, adulterado ou já lido: o mesmo 404 vazio (Q6). O cookie sai sempre.
        return refusal.Take(httpContext) is { } content
            ? TypedResults.Ok(content)
            : TypedResults.NotFound();
    }

    /// <summary>
    /// O retorno do provedor, em passthrough: o cliente OIDC confere o state token, a correlação, o PKCE,
    /// o nonce, a assinatura, o <c>aud</c> e o <c>iss</c> dentro do <c>AuthenticateAsync</c>; qualquer falha
    /// ali é a recusa <c>falha</c>. Depois, a intenção do state token escolhe o login ou o conectar.
    /// </summary>
    private static async Task<IResult> CallbackAsync(
        HttpContext httpContext,
        FederatedProviderCatalog catalog,
        AppDbContext dbContext,
        FederatedRefusalCookie refusal,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(LoggerCategory);
        DeleteFlowCookies(httpContext);

        // Sem provedor configurado, o cliente OIDC não existe (nem o esquema dele).
        if (catalog.Providers.Count == 0)
        {
            LogFailed(logger, "(nenhum)", "sem_provedor_configurado");
            return LoginRefusal(FederatedLogin.Refusals.Failure, providerKey: null);
        }

        var result = await httpContext.AuthenticateAsync(OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);

        var items = result.Properties?.Items ?? new Dictionary<string, string?>();
        var intent = Item(items, FederatedLogin.Properties.Intent);
        var key = Item(items, FederatedLogin.Properties.ProviderKey);
        var provider = key is null ? null : catalog.Find(key);

        if (!result.Succeeded || result.Principal is not { } principal || provider is null)
        {
            var error = Item(items, OpenIddictClientAspNetCoreConstants.Properties.Error)
                ?? (provider is null && result.Succeeded ? "provedor_fora_da_configuracao" : "sem_detalhe");
            LogFailed(logger, key ?? "(desconhecido)", error);
            return intent == FederatedLogin.Intents.Connect
                ? AccountRefusal(FederatedLogin.Refusals.Failure, key)
                : LoginRefusal(FederatedLogin.Refusals.Failure, key);
        }

        var identity = ProviderIdentity.From(principal);
        if (identity is null)
        {
            LogFailed(logger, provider.Key, "sem_sub");
            return intent == FederatedLogin.Intents.Connect
                ? AccountRefusal(FederatedLogin.Refusals.Failure, provider.Key)
                : LoginRefusal(FederatedLogin.Refusals.Failure, provider.Key);
        }

        return intent == FederatedLogin.Intents.Connect
            ? await ConnectAsync(provider, identity, items, dbContext, timeProvider, logger, cancellationToken)
            : await LoginAsync(httpContext, provider, identity, items, dbContext, refusal, timeProvider, logger, cancellationToken);
    }

    /// <summary>A decisão do login (spec <c>federated-login</c>, "Decisão do login no retorno"), na ordem.</summary>
    private static async Task<IResult> LoginAsync(
        HttpContext httpContext,
        FederatedProvider provider,
        ProviderIdentity identity,
        IDictionary<string, string?> items,
        AppDbContext dbContext,
        FederatedRefusalCookie refusal,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // 1. O domínio vale para todo retorno, vinculado ou não.
        if (!identity.MatchesHostedDomain(provider.HostedDomain))
        {
            return Refuse(logger, provider, FederatedLogin.Refusals.DomainNotAllowed, userId: null);
        }

        // 2 e 3. O par decide, e só ele.
        var issuer = provider.Issuer.AbsoluteUri;
        var link = await dbContext.UserFederatedIdentities
            .SingleOrDefaultAsync(candidate => candidate.Issuer == issuer && candidate.Subject == identity.Subject, cancellationToken);
        if (link is not null)
        {
            var user = await dbContext.Users.SingleAsync(candidate => candidate.Id == link.UserId, cancellationToken);
            if (!user.IsActive)
            {
                return Refuse(logger, provider, FederatedLogin.Refusals.Deactivated, user.Id);
            }

            link.RecordUse(identity.Email, timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);

            // A sessão pendente (Q1): o segundo passo, ou o cadastro, completa como no login por senha.
            await IssuerSession.SignInAsync(httpContext, user, twoFactorDone: false);
            var step = user.IsTwoFactorEnabled ? SessionEndpoints.CodeStep : SessionEndpoints.EnrollmentStep;
            var target = $"{AuthorizeEndpoint.LoginPath}?federado={Uri.EscapeDataString(provider.Key)}&segundoPasso={step}";
            if (Item(items, FederatedLogin.Properties.Return) is { } retorno)
            {
                target += $"&retorno={Uri.EscapeDataString(retorno)}";
            }

            return Results.Redirect(target);
        }

        // 4. Sem vínculo, o e-mail só conta verificado.
        if (!identity.EmailVerified || identity.Email is null)
        {
            return Refuse(logger, provider, FederatedLogin.Refusals.EmailNotVerified, userId: null);
        }

        // 5, 6 e 7. O desativado sem vínculo cai em sem-acesso: o "Acesso desativado" é só de quem já
        // estava vinculado (decisão do corpo da #189).
        var normalized = EmailNormalization.Normalize(identity.Email);
        var byEmail = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.NormalizedEmail == normalized, cancellationToken);
        var reason = byEmail switch
        {
            { IsInvited: true } => FederatedLogin.Refusals.PendingInvitation,
            { IsActive: true } => FederatedLogin.Refusals.NotConnected,
            _ => FederatedLogin.Refusals.NoAccess,
        };

        refusal.Write(httpContext.Response, reason, provider.Key, identity.Email);
        return Refuse(logger, provider, reason, byEmail is { IsActive: true } or { IsInvited: true } ? byEmail.Id : null);
    }

    /// <summary>O retorno do conectar (spec <c>federated-login</c>, "Conectar o provedor em Minha conta").</summary>
    private static async Task<IResult> ConnectAsync(
        FederatedProvider provider,
        ProviderIdentity identity,
        IDictionary<string, string?> items,
        AppDbContext dbContext,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!identity.MatchesHostedDomain(provider.HostedDomain))
        {
            LogRefused(logger, provider.Key, FederatedLogin.Refusals.DomainNotAllowed, null);
            return AccountRefusal(FederatedLogin.Refusals.DomainNotAllowed, provider.Key);
        }

        var user = Guid.TryParse(Item(items, FederatedLogin.Properties.UserId), out var userId)
            ? await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
            : null;
        if (user is not { IsActive: true } || user.SecurityStamp != Item(items, FederatedLogin.Properties.SecurityStamp))
        {
            LogRefused(logger, provider.Key, FederatedLogin.Refusals.SessionExpired, user?.Id);
            return AccountRefusal(FederatedLogin.Refusals.SessionExpired, provider.Key);
        }

        var issuer = provider.Issuer.AbsoluteUri;
        var byPair = await dbContext.UserFederatedIdentities
            .SingleOrDefaultAsync(candidate => candidate.Issuer == issuer && candidate.Subject == identity.Subject, cancellationToken);
        if (byPair is not null && byPair.UserId != user.Id)
        {
            LogRefused(logger, provider.Key, FederatedLogin.Refusals.InUse, user.Id);
            return AccountRefusal(FederatedLogin.Refusals.InUse, provider.Key);
        }

        if (byPair is not null || await dbContext.UserFederatedIdentities.AnyAsync(candidate => candidate.UserId == user.Id && candidate.Issuer == issuer, cancellationToken))
        {
            LogRefused(logger, provider.Key, FederatedLogin.Refusals.AlreadyConnected, user.Id);
            return AccountRefusal(FederatedLogin.Refusals.AlreadyConnected, provider.Key);
        }

        dbContext.UserFederatedIdentities.Add(UserFederatedIdentity.Connect(user.Id, issuer, identity.Subject, identity.Email, timeProvider.GetUtcNow()));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A corrida: outro conectar gravou o mesmo par, ou o mesmo issuer para a pessoa, entre a leitura e a gravação.
            LogRefused(logger, provider.Key, FederatedLogin.Refusals.InUse, user.Id);
            return AccountRefusal(FederatedLogin.Refusals.InUse, provider.Key);
        }

        LogConnected(logger, user.Id, provider.Key);
        return Results.Redirect($"/account?conectado={Uri.EscapeDataString(provider.Key)}");
    }

    private static string? Item(IDictionary<string, string?> items, string key) =>
        items.TryGetValue(key, out var value) ? value : null;

    private static IResult Refuse(ILogger logger, FederatedProvider provider, string reason, Guid? userId)
    {
        LogRefused(logger, provider.Key, reason, userId);
        return LoginRefusal(reason, provider.Key);
    }

    private static IResult LoginRefusal(string reason, string? providerKey) =>
        Results.Redirect($"{AuthorizeEndpoint.LoginPath}?recusa={reason}{ProviderQuery(providerKey)}");

    private static IResult AccountRefusal(string reason, string? providerKey) =>
        Results.Redirect($"/account?recusa={reason}{ProviderQuery(providerKey)}");

    private static string ProviderQuery(string? providerKey) =>
        providerKey is null ? "" : $"&provedor={Uri.EscapeDataString(providerKey)}";

    /// <summary>Apaga os cookies do fluxo que vieram no pedido: aceito ou recusado, nenhum fica (D4).</summary>
    private static void DeleteFlowCookies(HttpContext httpContext)
    {
        foreach (var name in httpContext.Request.Cookies.Keys.Where(name => name.StartsWith($"{FederatedLogin.FlowCookieName}.", StringComparison.Ordinal)))
        {
            httpContext.Response.Cookies.Delete(name, new CookieOptions { Path = FederatedLogin.CallbackPath });
        }
    }

    [LoggerMessage(EventId = 1198, EventName = "FederatedLoginRefused", Level = LogLevel.Warning,
        Message = "Login federado recusado pelo Buteco: provedor {ProviderKey}, motivo {Reason}, usuário {UserId}.")]
    public static partial void LogRefused(ILogger logger, string providerKey, string reason, Guid? userId);

    [LoggerMessage(EventId = 1199, EventName = "FederatedLoginFailed", Level = LogLevel.Warning,
        Message = "Retorno do provedor federado recusado pelo cliente OIDC: provedor {ProviderKey}, erro {Error}.")]
    public static partial void LogFailed(ILogger logger, string providerKey, string error);

    [LoggerMessage(EventId = 1200, EventName = "FederatedIdentityConnected", Level = LogLevel.Information,
        Message = "Conta do provedor {ProviderKey} conectada ao usuário {UserId}.")]
    public static partial void LogConnected(ILogger logger, Guid userId, string providerKey);

    [LoggerMessage(EventId = 1201, EventName = "FederatedIdentityDisconnected", Level = LogLevel.Information,
        Message = "Conta do provedor {ProviderKey} desconectada do usuário {UserId} por {ByUserId}.")]
    public static partial void LogDisconnected(ILogger logger, Guid userId, string providerKey, Guid byUserId);

    /// <summary>O que o Buteco lê do principal do provedor: o <c>sub</c>, o e-mail, a verificação e o <c>hd</c>.</summary>
    private sealed record ProviderIdentity(string Subject, string? Email, bool EmailVerified, string? HostedDomain)
    {
        public static ProviderIdentity? From(ClaimsPrincipal principal)
        {
            var subject = principal.FindFirst(Claims.Subject)?.Value;
            if (string.IsNullOrEmpty(subject))
            {
                return null;
            }

            var email = principal.FindFirst(Claims.Email)?.Value;
            var verified = string.Equals(principal.FindFirst(Claims.EmailVerified)?.Value, "true", StringComparison.OrdinalIgnoreCase);
            return new ProviderIdentity(subject, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), verified, principal.FindFirst("hd")?.Value);
        }

        public bool MatchesHostedDomain(string? configured) =>
            configured is null || string.Equals(HostedDomain, configured, StringComparison.OrdinalIgnoreCase);
    }
}
