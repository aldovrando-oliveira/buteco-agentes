using System.Net.Http.Json;
using System.Text.Json;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// As oito rotas do convite e da redefinição (design.md da change convites-redefinicao-senha,
/// D10): as administrativas com o access token de quem chama, e as públicas sem token, em JSON e
/// com a <c>Origin</c> do issuer, como o painel as chama.
/// </summary>
public static class AccessLinkClient
{
    // O papel é obrigatório desde a change papeis-no-token (D5); o padrão é admin, o poder que todo
    // convidado tinha na M1 e que os casos de antes supõem.
    public static Task<HttpResponseMessage> InviteAsync(HttpClient client, SignedInUser caller, string? name, string? email, string? password, string? papel = "admin") =>
        TwoFactorTestClient.SendAsync(client, HttpMethod.Post, "/users/invitations", caller.AccessToken, new { nome = name, email, papel, senha = password });

    public static Task<HttpResponseMessage> RenewAsync(HttpClient client, SignedInUser caller, Guid id, string? password) =>
        TwoFactorTestClient.SendAsync(client, HttpMethod.Post, $"/users/{id}/invitation/renew", caller.AccessToken, new { senha = password });

    public static Task<HttpResponseMessage> CancelAsync(HttpClient client, SignedInUser caller, Guid id) =>
        TwoFactorTestClient.SendAsync(client, HttpMethod.Post, $"/users/{id}/invitation/cancel", caller.AccessToken);

    public static Task<HttpResponseMessage> ResetAsync(HttpClient client, SignedInUser caller, Guid id, string? password) =>
        TwoFactorTestClient.SendAsync(client, HttpMethod.Post, $"/users/{id}/password-reset", caller.AccessToken, new { senha = password });

    public static Task<HttpResponseMessage> LookupInvitationAsync(HttpClient client, string token) =>
        PublicAsync(client, "/auth/invitation", new { token });

    public static Task<HttpResponseMessage> AcceptAsync(HttpClient client, string token, string password) =>
        PublicAsync(client, "/auth/invitation/accept", new { token, senha = password });

    public static Task<HttpResponseMessage> LookupResetAsync(HttpClient client, string token) =>
        PublicAsync(client, "/auth/password-reset", new { token });

    public static Task<HttpResponseMessage> CompleteAsync(HttpClient client, string token, string password) =>
        PublicAsync(client, "/auth/password-reset/complete", new { token, senha = password });

    public static Task<HttpResponseMessage> PublicAsync(HttpClient client, string path, object body, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", origin ?? LocalUserSeed.IssuerOrigin);
        return client.SendAsync(request);
    }

    /// <summary>O link de uma resposta de criar, renovar ou redefinir.</summary>
    public static async Task<string> LinkAsync(HttpResponseMessage response) =>
        (await TwoFactorTestClient.JsonAsync(response)).GetProperty("link").GetString()!;

    /// <summary>O token, que vai depois do <c>#</c>.</summary>
    public static string TokenOf(string link) => link[(link.IndexOf('#') + 1)..];

    public static async Task<string> TokenAsync(HttpResponseMessage response) => TokenOf(await LinkAsync(response));

    public static async Task<Guid> InvitedIdAsync(HttpResponseMessage response) =>
        (await TwoFactorTestClient.JsonAsync(response)).GetProperty("usuario").GetProperty("id").GetGuid();

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await TwoFactorTestClient.JsonAsync(response);
}
