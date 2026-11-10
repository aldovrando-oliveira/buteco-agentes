using System.Data.Common;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Buteco.Api.ExternalApplications;
using Buteco.Api.Infrastructure;
using Buteco.Api.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O host das aplicações externas (change aplicacoes-externas-credencial), com dois pontos trocáveis
/// que só o teste usa:
/// <list type="bullet">
/// <item><see cref="ClientIds"/>: o gerador de <c>client_id</c> com um valor forçado, para o caso do
/// prefixo reservado (2.2 do tasks.md);</item>
/// <item><see cref="Failures"/>: um interceptor que, armado, falha o próximo comando que bate com um
/// texto, para o caso da desativação numa transação (5.6, rodada 3).</item>
/// </list>
/// E os passos que os casos repetem: o Admin pelo fluxo real do emissor, criar a aplicação pela rota,
/// pedir token por <c>client_credentials</c> e ler o payload do token.
/// </summary>
public class ExternalApplicationsFixture : LocalUsersFixture
{
    public SwitchableClientIdGenerator ClientIds { get; } = new();

    public ArmedCommandFailure Failures { get; } = new();

    /// <summary>A fila das tasks, para afirmar que a chamada A2A recusada não publicou nada.</summary>
    public FakeTaskJobPublisher TaskJobPublisher { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private SignedInUser? _admin;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IExternalApplicationClientIdGenerator>();
            services.AddSingleton<IExternalApplicationClientIdGenerator>(ClientIds);

            services.RemoveAll<ITaskJobPublisher>();
            services.AddSingleton<ITaskJobPublisher>(TaskJobPublisher);

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options
                .UseButecoAgentsNpgsql(PostgresConnectionString)
                .AddInterceptors(Failures));
        });
    }

    /// <summary>Um Admin com sessão pelo fluxo real, o mesmo para a classe.</summary>
    public async Task<SignedInUser> AdminAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return _admin ??= await SignedInUser.CreateAsync(this, "aplicacoes-admin");
        }
        finally
        {
            _gate.Release();
        }
    }

    public HttpClient As(SignedInUser user)
    {
        var client = SignedInUser.Anonymous(this);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return client;
    }

    public async Task<HttpClient> AdminClientAsync() => As(await AdminAsync());

    public HttpClient WithToken(string token)
    {
        var client = SignedInUser.Anonymous(this);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public sealed record Created(Guid Id, string ClientId, string Secret, Guid SecretId, JsonElement Body);

    /// <summary>Cria pela rota, com a senha do Admin; falha o teste se não for <c>201</c>.</summary>
    public async Task<Created> CreateAsync(string? name = null, string? description = null)
    {
        var admin = await AdminAsync();
        var response = await As(admin).PostAsJsonAsync("/applications/", new
        {
            nome = name ?? $"Aplicação {Guid.NewGuid():N}"[..30],
            descricao = description,
            senha = admin.Password,
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Created, body.ToString());
        var secret = body.GetProperty("segredo");
        return new Created(
            body.GetProperty("id").GetGuid(),
            body.GetProperty("clientId").GetString()!,
            secret.GetProperty("valor").GetString()!,
            secret.GetProperty("id").GetGuid(),
            body);
    }

    public Task<HttpResponseMessage> TokenAsync(string clientId, string secret, string? scope = "buteco-api") =>
        SignedInUser.Anonymous(this).PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = clientId,
                [Parameters.ClientSecret] = secret,
            }.Concat(scope is null ? [] : [KeyValuePair.Create(Parameters.Scope, scope)])));

    /// <summary>O access token; falha o teste se o emissor não o emitir.</summary>
    public async Task<string> AccessTokenAsync(string clientId, string secret, string? scope = "buteco-api")
    {
        var response = await TokenAsync(clientId, secret, scope);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
    }

    /// <summary>O erro OAuth da resposta de token (<c>invalid_client</c>, <c>invalid_request</c>…).</summary>
    public static async Task<string?> TokenErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.TryGetProperty(Parameters.Error, out var error) ? error.GetString() : null;
    }

    public static JsonElement Payload(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

/// <summary>O gerador de produção, com um valor forçado quando <see cref="Forced"/> não é nulo.</summary>
public sealed class SwitchableClientIdGenerator : IExternalApplicationClientIdGenerator
{
    private readonly ExternalApplicationClientId _real = new();

    public string? Forced { get; set; }

    public string Next() => Forced ?? _real.Next();
}

/// <summary>Armado, falha o próximo comando cujo SQL contém <see cref="Armed"/>, e se desarma.</summary>
public sealed class ArmedCommandFailure : DbCommandInterceptor
{
    private string? _armed;

    public string? Armed
    {
        get => Volatile.Read(ref _armed);
        set => Volatile.Write(ref _armed, value);
    }

    public int Fired { get; private set; }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Fire(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Fire(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Fire(DbCommand command)
    {
        if (Armed is { } text && command.CommandText.Contains(text, StringComparison.Ordinal) &&
            Interlocked.CompareExchange(ref _armed, null, text) == text)
        {
            Fired++;
            throw new SimulatedFailure();
        }
    }

    private sealed class SimulatedFailure() : DbException("Falha simulada do banco no meio da desativação.");
}
