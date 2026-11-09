using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace ApiConnectorsRoundTrip.Tests.Support;

// Duplicado deliberadamente de tests/InboxOrchestratorRoundTrip.Tests/Support/IssuerUserFlow.cs: os projetos de
// tests/ são independentes, sem referência cruzada de código (convenção 11). Qualquer
// mudança aqui precisa ser espelhada lá.

/// <summary>
/// O fluxo real do usuário contra o TestServer do apps/api (design.md da change
/// usuarios-locais-login-emissor, D5): sessão do emissor, o segundo passo obrigatório (o cadastro
/// das 2 etapas no primeiro login, o código nos seguintes; change verificacao-2-etapas-obrigatoria),
/// authorize com PKCE e troca do código, só por HTTP. O primeiro Admin vem do bootstrap da fixture e o cliente
/// buteco-painel do catálogo. Os dois rodam no aquecimento do apps/api, e o teste os roda
/// de novo antes do login, para não depender do ciclo (parâmetro warmUp).
/// </summary>
public static class IssuerUserFlow
{
    public const string BootstrapAdminEmail = "admin@exemplo.test";

    /// <param name="issuerDatabase">
    /// A conexão do banco do <c>apps/api</c> de onde o token sai (<c>RoundTripFixture.IssuerDatabaseOf</c>).
    /// O Admin do bootstrap cadastra a verificação em 2 etapas no primeiro login naquele banco
    /// (change verificacao-2-etapas-obrigatoria, D1), e os hosts que sobem sobre o mesmo banco o
    /// encontram cadastrado: os códigos de recuperação desse cadastro ficam guardados por banco, e
    /// o login seguinte usa um deles. O código do app no mesmo passo de 30 s seria recusado como
    /// reuso. Os logins de um banco são um de cada vez: dois cadastros simultâneos trocariam o
    /// segredo pendente um do outro.
    /// </param>
    /// <param name="warmUp">
    /// Roda o bootstrap e o catálogo pelos serviços do apps/api, idempotentes. Sem isso, o
    /// teste dependeria do ciclo do aquecimento, e repetir o login até o bootstrap
    /// concluir não serve: cada tentativa antes dele é uma falha de e-mail sem conta, e a
    /// quinta bloqueia o e-mail por 5 minutos (D4).
    /// </param>
    public static async Task<string> AccessTokenAsync(string issuerDatabase, HttpClient api, Func<Task> warmUp, string publicBaseUrl, string password, string scope, string? email = null) =>
        (await TokensAsync(issuerDatabase, api, warmUp, publicBaseUrl, password, scope, email)).AccessToken;

    /// <summary>
    /// Os tokens do fluxo real, com o refresh quando o escopo pede <c>offline_access</c>. Sem
    /// <paramref name="email"/>, o Admin do bootstrap; com ele, outro usuário daquele banco, que cadastra
    /// a própria verificação no primeiro login (change papeis-no-token, 9.1).
    /// </summary>
    public static async Task<Tokens> TokensAsync(string issuerDatabase, HttpClient api, Func<Task> warmUp, string publicBaseUrl, string password, string scope, string? email = null)
    {
        await warmUp();
        email ??= BootstrapAdminEmail;
        var redirectUri = new Uri(new Uri(publicBaseUrl), "/entrar/retorno").AbsoluteUri;
        var origin = new Uri(publicBaseUrl).GetLeftPart(UriPartial.Authority);
        var state = Enrollments.GetOrAdd($"{issuerDatabase}|{email}", _ => new HostState());

        await state.Gate.WaitAsync();
        string cookie;
        try
        {
            if (state.RecoveryCodes.Count is > 0 and <= 2)
            {
                await RegenerateRecoveryCodesAsync(api, origin, redirectUri, email, password, state);
            }

            cookie = await CompleteSessionAsync(api, origin, email, password, state);
        }
        finally
        {
            state.Gate.Release();
        }

        return await TokenAsync(api, cookie, redirectUri, scope);
    }

    public sealed record Tokens(string AccessToken, string? RefreshToken);

    /// <summary>A renovação pelo <c>/connect/token</c>, como o painel faz.</summary>
    public static async Task<Tokens> RefreshAsync(HttpClient api, string refreshToken)
    {
        var token = await api.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "buteco-painel",
            ["refresh_token"] = refreshToken,
        }));
        var body = await token.Content.ReadAsStringAsync();
        if (token.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Renovação falhou: {(int)token.StatusCode} {body}");
        }

        var root = JsonDocument.Parse(body).RootElement;
        return new Tokens(root.GetProperty("access_token").GetString()!, root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null);
    }

    /// <summary>
    /// Convida <paramref name="email"/> com <paramref name="papel"/> (<c>admin</c>, <c>operador</c>,
    /// <c>leitor</c>) pelo token de um Admin, e aceita o convite com <paramref name="newPassword"/>, pelas
    /// rotas reais (change papeis-no-token, 9.1). O login seguinte é o <see cref="TokensAsync"/> com o e-mail.
    /// </summary>
    public static async Task InviteAndAcceptAsync(HttpClient api, string publicBaseUrl, string adminAccessToken, string adminPassword, string email, string papel, string newPassword)
    {
        var origin = new Uri(publicBaseUrl).GetLeftPart(UriPartial.Authority);
        var invite = new HttpRequestMessage(HttpMethod.Post, "/users/invitations")
        {
            Content = JsonContent.Create(new { nome = "Pessoa do acordo", email, papel, senha = adminPassword }),
        };
        invite.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminAccessToken);
        var created = await api.SendAsync(invite);
        var createdBody = await created.Content.ReadAsStringAsync();
        if (created.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException($"Convite falhou: {(int)created.StatusCode} {createdBody}");
        }

        var link = JsonDocument.Parse(createdBody).RootElement.GetProperty("link").GetString()!;
        var accept = await PostAsync(api, "/auth/invitation/accept", origin, null, new { token = link[(link.IndexOf('#') + 1)..], senha = newPassword });
        if (accept.StatusCode != HttpStatusCode.NoContent)
        {
            throw new InvalidOperationException($"Aceite falhou: {(int)accept.StatusCode} {await accept.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>
    /// Os códigos de recuperação acabam depois de 10 logins no mesmo banco (um teste de acordo faz
    /// mais que isso). Com 2 ou menos, um login a mais com o escopo <c>buteco-api</c> e
    /// <c>POST /account/two-factor/recovery-codes</c> com um deles trazem 10 novos.
    /// </summary>
    private static async Task RegenerateRecoveryCodesAsync(HttpClient api, string origin, string redirectUri, string email, string password, HostState state)
    {
        var accessToken = (await TokenAsync(api, await CompleteSessionAsync(api, origin, email, password, state), redirectUri, "openid buteco-api")).AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Post, "/account/two-factor/recovery-codes")
        {
            Content = JsonContent.Create(new { codigo = state.RecoveryCodes.Dequeue() }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var response = await api.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Regeneração dos códigos falhou: {(int)response.StatusCode} {body}");
        }

        state.RecoveryCodes.Clear();
        foreach (var code in JsonDocument.Parse(body).RootElement.GetProperty("codigosRecuperacao").EnumerateArray())
        {
            state.RecoveryCodes.Enqueue(code.GetString()!);
        }
    }

    /// <summary>Authorize com PKCE e troca do código, com o cookie da sessão completa.</summary>
    private static async Task<Tokens> TokenAsync(HttpClient api, string cookie, string redirectUri, string scope)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = "buteco-painel",
            ["response_type"] = "code",
            ["scope"] = scope,
            ["redirect_uri"] = redirectUri,
            ["state"] = "estado-do-acordo",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        var authorize = new HttpRequestMessage(HttpMethod.Get, url);
        authorize.Headers.Add("Cookie", cookie);
        var redirect = await api.SendAsync(authorize);
        var location = redirect.Headers.Location;
        if (redirect.StatusCode != HttpStatusCode.Redirect || location is null || !location.IsAbsoluteUri ||
            !QueryHelpers.ParseQuery(location.Query).TryGetValue("code", out var code))
        {
            throw new InvalidOperationException($"Authorize sem código: {(int)redirect.StatusCode} {location}");
        }

        var token = await api.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "buteco-painel",
            ["code"] = code.ToString(),
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        }));
        var body = await token.Content.ReadAsStringAsync();
        if (token.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Troca do código falhou: {(int)token.StatusCode} {body}");
        }

        var root = JsonDocument.Parse(body).RootElement;
        return new Tokens(root.GetProperty("access_token").GetString()!, root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null);
    }

    private sealed class HostState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public Queue<string> RecoveryCodes { get; } = new();
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HostState> Enrollments = new();

    /// <summary>
    /// Senha e o segundo passo: o cadastro no primeiro login do host (<c>"cadastro"</c>), ou um
    /// código de recuperação guardado nos seguintes (<c>"totp"</c>). Devolve o cookie da sessão completa.
    /// </summary>
    private static async Task<string> CompleteSessionAsync(HttpClient api, string origin, string email, string password, HostState state)
    {
        var login = await PostAsync(api, "/auth/session", origin, null, new { email, senha = password });
        var loginBody = await login.Content.ReadAsStringAsync();
        if (login.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Login falhou: {(int)login.StatusCode} {loginBody}");
        }

        var pending = CookieOf(login);
        var step = JsonDocument.Parse(loginBody).RootElement.GetProperty("segundoPasso").GetString();
        if (step == "totp")
        {
            if (!state.RecoveryCodes.TryDequeue(out var recovery))
            {
                throw new InvalidOperationException($"{email} já tem a verificação, e não há código de recuperação guardado para este banco.");
            }

            var second = await PostAsync(api, "/auth/session/second-factor", origin, pending, new { codigo = recovery });
            return second.StatusCode == HttpStatusCode.NoContent
                ? CookieOf(second)
                : throw new InvalidOperationException($"Segundo passo falhou: {(int)second.StatusCode} {await second.Content.ReadAsStringAsync()}");
        }

        var setup = await PostAsync(api, "/auth/session/enrollment/setup", origin, pending, new { });
        if (setup.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Setup do cadastro falhou: {(int)setup.StatusCode} {await setup.Content.ReadAsStringAsync()}");
        }

        var secret = Base32Decode(JsonDocument.Parse(await setup.Content.ReadAsStringAsync()).RootElement.GetProperty("segredo").GetString()!);
        var confirm = await PostAsync(api, "/auth/session/enrollment/confirm", origin, CookieOf(setup), new { codigo = TotpNow(secret) });
        var confirmBody = await confirm.Content.ReadAsStringAsync();
        if (confirm.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Confirmação do cadastro falhou: {(int)confirm.StatusCode} {confirmBody}");
        }

        state.RecoveryCodes.Clear();
        foreach (var code in JsonDocument.Parse(confirmBody).RootElement.GetProperty("codigosRecuperacao").EnumerateArray())
        {
            state.RecoveryCodes.Enqueue(code.GetString()!);
        }

        return CookieOf(confirm);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient api, string path, string origin, string? cookie, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", origin);
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return api.SendAsync(request);
    }

    private static string CookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

    /// <summary>O código de 6 dígitos do RFC 6238 (HMAC-SHA1, 30 s, T0 = 0) no relógio do sistema, que é o do apps/api destes testes.</summary>
    private static string TotpNow(byte[] secret)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Base32 do RFC 4648, para ler o segredo que o <c>setup</c> devolve.</summary>
    private static byte[] Base32Decode(string text)
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

    /// <summary>O PBKDF2 de Auth:OperatorPasswordHash, o mesmo formato das fixtures do apps/api.</summary>
    public static string LegacyPasswordHash(string password)
    {
        const int iterations = 100_000;
        var salt = "fixed-test-salt-not-for-production"u8.ToArray();
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
