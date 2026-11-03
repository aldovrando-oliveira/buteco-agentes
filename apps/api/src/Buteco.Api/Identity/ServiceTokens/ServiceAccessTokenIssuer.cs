using System.Security.Claims;
using Buteco.Api.Identity.Clients;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Identity.ServiceTokens;

/// <summary>
/// Gera, no próprio processo, o access token que o <c>apps/api</c> manda ao
/// <c>apps/connectors</c> (design.md da change servicos-token-emissor, D1 e D2): sem HTTP e
/// sem credencial guardada, pela API de geração do OpenIddict, com a mesma credencial e os
/// mesmos handlers do <c>/connect/token</c>.
///
/// <para>
/// <b>O principal vem pronto daqui</b> (H9): a geração só assina o que recebe. O que o
/// <c>PrepareAccessTokenPrincipal</c> do OpenIddict põe no sign-in (criação, expiração,
/// issuer, audiences, escopos, presenter, <c>client_id</c>) é posto aqui, nos mesmos claims.
/// </para>
/// <para>
/// <b>Cache:</b> o token serve até faltar <see cref="RenewalMargin"/> para vencer. Sem
/// pedido único: a geração é em memória e idempotente, e duas simultâneas custam duas
/// assinaturas e nenhum efeito (D2). Sem espera de falha: o <c>IdentityKeyRingGate</c>
/// responde <c>503</c> antes da rota que chama o <c>apps/connectors</c>, então o anel já
/// carregou quando a chamada sai (H12).
/// </para>
/// </summary>
public sealed class ServiceAccessTokenIssuer(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ServiceAccessTokenIssuer> logger)
{
    // Constantes, e não parâmetros (decisão do dono, 02/11/2026): nenhum caminho de produção
    // gera, sem credencial, token de outro cliente ou para outro app.
    private const string ClientId = "buteco-servico-api";
    private const string Audience = IdentityClients.ConnectorsScope;
    private const string Scope = IdentityClients.ConnectorsScope;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    // A mesma margem do painel (#217) e do ServiceAccessTokenSource dos serviços (D3).
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(1);

    private readonly Lock _gate = new();
    private CachedToken? _cached;
    private bool _announced;

    public async ValueTask<string> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_cached is { } cached && timeProvider.GetUtcNow() < cached.ExpiresAt - RenewalMargin)
            {
                return cached.Token;
            }
        }

        var generatedAt = timeProvider.GetUtcNow();
        var token = await GenerateAsync();

        bool announce;
        lock (_gate)
        {
            _cached = new CachedToken(token, generatedAt + Lifetime);
            announce = !_announced;
            _announced = true;
        }

        // Linha do primeiro token e do primeiro depois de um 401 do destino: é por ela que a
        // conferência pós-deploy vê os três emissores (D2, D8). Nunca o token.
        if (announce)
        {
            logger.LogInformation(
                "Token de serviço {ClientId} gerado no processo, válido até {ExpiresAt:O}.",
                ClientId,
                generatedAt + Lifetime);
        }

        return token;
    }

    /// <summary>
    /// Descarta o token em cache se ele ainda for <paramref name="token"/>: um <c>401</c>
    /// atrasado de um token já trocado não derruba o novo. Interno, e não público: a
    /// superfície pública não recebe parâmetro além do <c>CancellationToken</c> (D2).
    /// </summary>
    internal void Invalidate(string token)
    {
        lock (_gate)
        {
            if (_cached?.Token == token)
            {
                _cached = null;
                _announced = false;
            }
        }
    }

    private async Task<string> GenerateAsync()
    {

        await using var scope = scopeFactory.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IOpenIddictServerFactory>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOpenIddictServerDispatcher>();

        var transaction = await factory.CreateTransactionAsync();
        var options = transaction.Options;
        var now = options.TimeProvider.GetUtcNow();

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, ClientId);
        identity.SetClaim(Claims.ClientId, ClientId);

        var principal = new ClaimsPrincipal(identity)
            .SetCreationDate(now)
            .SetExpirationDate(now + Lifetime)
            .SetAudiences(Audience)
            .SetScopes(Scope)
            // O AttachDefaultPresenters do sign-in põe o cliente como presenter, e o
            // PrepareAccessTokenPrincipal o mantém: o token do /connect/token sai com oi_prst
            // (medido na tarefa 1.2).
            .SetPresenters(ClientId)
            .SetClaim(Claims.Private.Issuer, options.Issuer!.AbsoluteUri);

        var context = new GenerateTokenContext(transaction)
        {
            ClientId = ClientId,
            CreateTokenEntry = false,
            IsReferenceToken = false,
            PersistTokenPayload = false,
            Principal = principal,
            TokenFormat = TokenFormats.Private.JsonWebToken,
            TokenType = TokenTypeIdentifiers.AccessToken,
        };

        await dispatcher.DispatchAsync(context);

        if (context.IsRejected || string.IsNullOrEmpty(context.Token))
        {
            throw new InvalidOperationException(
                $"A geração do token de serviço {ClientId} foi recusada pelo OpenIddict: {context.Error} {context.ErrorDescription}");
        }

        return context.Token;
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
}
