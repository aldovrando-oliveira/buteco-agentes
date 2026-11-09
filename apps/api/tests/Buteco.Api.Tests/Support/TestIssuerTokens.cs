using System.Runtime.CompilerServices;
using System.Security.Claims;
using Buteco.Api.Auth;
using Buteco.Api.Identity.Clients;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Validation;
using Buteco.Api.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Access tokens do emissor para as fixtures e os testes de subject (design.md da change
/// fixtures-token-emissor, D1), sem HTTP e sem Argon2: a mesma geração no processo do
/// <c>ServiceAccessTokenIssuer</c> (servicos-token-emissor, H9), pela
/// <see cref="IOpenIddictServerFactory"/>, com o principal pronto e sem entrada no banco.
/// <para>
/// <b>Separado do <c>ServiceAccessTokenIssuer</c> de produção</b>, que continua estreito
/// (decisão do dono, 02/11/2026): este aceita qualquer cliente e existe só no projeto de teste.
/// </para>
/// <para>
/// <b>Relógio de parede, e não o <see cref="TimeProvider"/> do host.</b> O <c>JwtBearer</c> do
/// <c>apps/api</c> confere o <c>exp</c> pelo relógio de parede, e fixtures fixam o
/// <see cref="TimeProvider"/> no passado (<c>InsightsEndpointsTests</c>, 23/09/2026), que o
/// OpenIddict usaria para carimbar. <b>Cache por host</b>, regerado quando falta menos de
/// <see cref="RenewalMargin"/> para vencer.
/// </para>
/// <para>
/// <b>Espera o anel do host carregar</b>, sem dispará-lo: quem carrega é o aquecimento (ou o
/// primeiro pedido, pelo gate), e os testes do gate contam as cargas. Um host derivado por
/// <c>WithWebHostBuilder</c> tem anel próprio, que o aquecimento carrega logo depois de subir.
/// Banco sem migrar nunca carrega: quem sobe assim não pede token antes de migrar
/// (<see cref="ApiFactoryFixture"/>).
/// </para>
/// </summary>
public static class TestIssuerTokens
{
    /// <summary>
    /// O <c>sub</c> do token do painel nas fixtures: não é id de usuário, então as rotas de
    /// <c>CurrentUser</c> respondem <c>403</c>: é o operador sem usuário.
    /// </summary>
    public const string PanelSubject = "fixture-sem-usuario";

    // Duas horas, e não os 15 min de produção: o token serve a classe inteira, e a duração
    // não é o que estes casos provam (D1).
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan RingLoadTimeout = TimeSpan.FromSeconds(20);

    private static readonly ConditionalWeakTable<IServiceProvider, Dictionary<(string, string, string?), (string Token, DateTimeOffset ExpiresAt)>> Cache = new();

    /// <summary>O token padrão das fixtures: <c>buteco-painel</c>, com <see cref="PanelSubject"/>.</summary>
    // O token do painel leva papel (design.md da change papeis-no-token, D3): sem ele, os três apps
    // respondem 401. O padrão é admin, o poder que todo caso de antes da change supunha; quem testa
    // outro papel, ou a ausência dele (null), o diz.
    public static string Panel(IServiceProvider services, string? role = UserRoleText.Admin) =>
        ForClient(services, IdentityClients.PanelClientId, PanelSubject, role);

    /// <summary>
    /// Token do cliente que a <see cref="IssuerClientSubjects"/> de produção mapeia para
    /// <paramref name="subject"/>, com o <c>sub</c> de um <c>client_credentials</c> (o
    /// <c>client_id</c>); o <c>operator</c> é o <see cref="Panel"/>. Subject que nenhum cliente
    /// produz vira um <c>client_id</c> igual ao texto dele, fora da tabela: o token autentica
    /// sem subject e recebe <c>403</c> (design.md, Q1, decisão do dono).
    /// </summary>
    public static string ForSubject(IServiceProvider services, string subject)
    {
        if (subject == ServiceScopeAuthorizationHandler.OperatorSubject)
        {
            return Panel(services);
        }

        var clientId = IssuerClientSubjects.Table.FirstOrDefault(entry => entry.Value == subject).Key ?? subject;
        return ForClient(services, clientId, clientId);
    }

    /// <summary>
    /// Token de <paramref name="clientId"/> com <c>aud</c> e escopo <c>buteco-api</c>.
    /// </summary>
    public static string ForClient(IServiceProvider services, string clientId, string subject, string? role = null)
    {
        var ring = services.GetRequiredService<IdentityKeyRing>();
        var deadline = DateTime.UtcNow + RingLoadTimeout;
        while (!ring.IsLoaded)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"O anel do emissor não carregou em {RingLoadTimeout.TotalSeconds} s: o banco do host foi migrado?");
            }

            Thread.Sleep(25);
        }

        var entries = Cache.GetValue(services, _ => []);
        var now = TimeProvider.System.GetUtcNow();
        lock (entries)
        {
            if (entries.TryGetValue((clientId, subject, role), out var cached) && now < cached.ExpiresAt - RenewalMargin)
            {
                return cached.Token;
            }
        }

        // ConfigureClient é síncrono; a geração vai a outra thread para não depender do
        // contexto de sincronização de quem chama.
        var generated = Task.Run(() => GenerateAsync(services, clientId, subject, role)).GetAwaiter().GetResult();
        lock (entries)
        {
            entries[(clientId, subject, role)] = generated;
        }

        return generated.Token;
    }

    private static async Task<(string Token, DateTimeOffset ExpiresAt)> GenerateAsync(
        IServiceProvider services, string clientId, string subject, string? role)
    {
        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IOpenIddictServerFactory>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IOpenIddictServerDispatcher>();

        var transaction = await factory.CreateTransactionAsync();
        var options = transaction.Options;
        var now = TimeProvider.System.GetUtcNow();

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, subject);
        identity.SetClaim(Claims.ClientId, clientId);
        if (role is not null)
        {
            identity.SetClaim(Claims.Role, role);
        }

        var principal = new ClaimsPrincipal(identity)
            .SetCreationDate(now)
            .SetExpirationDate(now + Lifetime)
            .SetAudiences(IdentityClients.ApiScope)
            .SetScopes(IdentityClients.ApiScope)
            .SetPresenters(clientId)
            .SetClaim(Claims.Private.Issuer, options.Issuer!.AbsoluteUri);

        var context = new GenerateTokenContext(transaction)
        {
            ClientId = clientId,
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
                $"O OpenIddict recusou o token de teste de {clientId}: {context.Error} {context.ErrorDescription}");
        }

        return (context.Token, now + Lifetime);
    }
}
