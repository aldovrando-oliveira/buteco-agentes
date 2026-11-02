using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.Infrastructure;
using Buteco.Api.Users.TwoFactor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// As 2 etapas nos testes (design.md da change verificacao-2-etapas-totp, D12): a verificação
/// ligada por semente ou pela API, o código do passo do relógio da fixture, e o login em dois
/// passos. O relógio é o <see cref="LocalUsersFixture.Clock"/>: o código só muda quando o
/// teste o avança, e um código aceito não serve de novo no mesmo passo.
/// </summary>
public static class TwoFactorTestClient
{
    public sealed record Enabled(byte[] Secret, IReadOnlyList<string> RecoveryCodes);

    /// <summary>Liga a verificação direto no banco, como se a pessoa tivesse confirmado o cadastro, sem passo gravado.</summary>
    public static async Task<Enabled> SeedEnabledAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<TwoFactorSecretCipher>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var user = await db.Users.FindAsync(userId) ?? throw new InvalidOperationException("Usuário da semente não existe.");

        var secret = Totp.NewSecret();
        user.StartTwoFactorEnrollment(cipher.Protect(userId, secret), clock.GetUtcNow());
        user.EnableTwoFactor(clock.GetUtcNow(), confirmedStep: 0);
        await db.SaveChangesAsync();
        var codes = await scope.ServiceProvider.GetRequiredService<TwoFactorVerifier>().ReplaceRecoveryCodesAsync(userId, CancellationToken.None);
        return new Enabled(secret, codes);
    }

    public static string CodeNow(byte[] secret, FakeTimeProvider clock, int stepOffset = 0) =>
        Totp.Compute(secret, Totp.StepAt(clock.GetUtcNow()) + stepOffset);

    /// <summary>Avança o relógio um passo inteiro do TOTP: o código seguinte é outro.</summary>
    public static void NextStep(FakeTimeProvider clock) => clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds));

    /// <summary>O primeiro passo do login: a resposta e o valor <c>buteco-sessao=…</c>, se houver.</summary>
    public static async Task<(HttpResponseMessage Response, string? Cookie)> PasswordStepAsync(HttpClient client, string email, string password)
    {
        var response = await LocalUserSeed.PostSessionAsync(client, email, password);
        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.Single().Split(';')[0] : null;
        return (response, cookie);
    }

    public static async Task<(HttpResponseMessage Response, string? Cookie)> SecondFactorAsync(
        HttpClient client, string? cookie, string code, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/session/second-factor")
        {
            Content = JsonContent.Create(new { codigo = code }),
        };
        request.Headers.Add("Origin", origin ?? LocalUserSeed.IssuerOrigin);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        var response = await client.SendAsync(request);
        var newCookie = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.Single().Split(';')[0] : null;
        return (response, newCookie);
    }

    /// <summary>Senha, código e cookie da sessão completa.</summary>
    public static async Task<string> CompleteSessionCookieAsync(HttpClient client, string email, string password, string code)
    {
        var (first, pending) = await PasswordStepAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var (second, complete) = await SecondFactorAsync(client, pending, code);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        return complete!;
    }

    /// <summary>O fluxo inteiro até os tokens, com a sessão já completa.</summary>
    public static async Task<IdentityIssuerUserFlow.Tokens> TokensAsync(HttpClient client, IServiceProvider services, string sessionCookie)
    {
        await IdentityIssuerUserFlow.EnsurePanelClientAsync(services);
        var pkce = IdentityIssuerUserFlow.Pkce.Create();
        var authorize = await IdentityIssuerUserFlow.AuthorizeAsync(client, IdentityIssuerUserFlow.AuthorizeUrl(pkce), sessionCookie);
        return await IdentityIssuerUserFlow.ReadTokensAsync(
            await IdentityIssuerUserFlow.RedeemAsync(client, IdentityIssuerUserFlow.CodeFrom(authorize), pkce.Verifier));
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? bearer, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Authorization = bearer is null ? null : new AuthenticationHeaderValue("Bearer", bearer);
        return client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    public static async Task<string> TitleAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("title").GetString()!;

    public static async Task<(bool Enabled, string Stamp, int Codes)> StateAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.FindAsync(userId);
        return (user!.IsTwoFactorEnabled, user.SecurityStamp, db.UserRecoveryCodes.Count(code => code.UserId == userId));
    }

    /// <summary>Base32 do RFC 4648, para ler o segredo que o <c>setup</c> devolve.</summary>
    public static byte[] DecodeBase32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var symbol in text)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(symbol, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
