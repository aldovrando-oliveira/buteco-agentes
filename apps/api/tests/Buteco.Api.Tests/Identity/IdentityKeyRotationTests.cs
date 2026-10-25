using System.Text.Json;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// A rotação no mesmo processo (spec <c>identity-signing-keys</c>; design.md da change
/// emissor-oidc-api, D7), dirigida pelo relógio: o <see cref="FakeTimeProvider"/> avança,
/// o tique do <see cref="IdentityKeyRotationService"/> roda a verificação, e o anel
/// recarrega as opções do OpenIddict sem reiniciar.
///
/// <para>
/// Um teste só, em passos, porque cada passo depende do estado do anterior: publicação
/// antecipada, troca, retenção e saída.
/// </para>
/// </summary>
public class IdentityKeyRotationTests(IdentityKeyRotationTests.RotationFixture factory)
    : IClassFixture<IdentityKeyRotationTests.RotationFixture>, IAsyncLifetime
{
    public sealed class RotationFixture : ApiFactoryFixture
    {
        public FakeTimeProvider Clock { get; } = CreateClock();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            });
        }

        private static FakeTimeProvider CreateClock()
        {
            var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

            // A checagem de fuso do boot compara TZ com o fuso deste relógio.
            clock.SetLocalTimeZone(TimeZoneInfo.Local);
            return clock;
        }
    }

    public Task InitializeAsync() => IdentityIssuerTestClient.EnsureAsync(factory.Services);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Rotation_PublishesAhead_SwitchesWithoutRestart_KeepsTheOldKeyFor24Hours()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = null;
        var ring = factory.Services.GetRequiredService<IdentityKeyRing>();

        var first = await TokenKidAsync(client);
        var firstToken = first.Token;
        Assert.Equal([first.Kid], await JwksKidsAsync(client));
        var active = await RowAsync(first.Kid);

        // 1. Faltando menos de 7 dias: a próxima é publicada, e a ativa continua assinando.
        factory.Clock.SetUtcNow(active.SignsUntil - TimeSpan.FromDays(6));
        await WaitForAsync(() => ring.Snapshot!.SigningKeys.Count == 2);

        var next = Assert.Single(await JwksKidsAsync(client), kid => kid != first.Kid);
        Assert.Equal(first.Kid, (await TokenKidAsync(client)).Kid);

        // 2. Passado o início da próxima: ela assina, sem reinício, e a anterior segue no JWKS.
        factory.Clock.SetUtcNow(active.SignsUntil + TimeSpan.FromMinutes(1));
        await WaitForAsync(() => ring.Snapshot!.SigningKid == next);

        Assert.Equal(next, (await TokenKidAsync(client)).Kid);
        Assert.Contains(first.Kid, await JwksKidsAsync(client));

        // 3. O token da anterior, emitido antes da troca, ainda verifica pelo JWKS.
        var jwks = new JsonWebKeySet(await client.GetStringAsync("/.well-known/jwks"));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(firstToken, new TokenValidationParameters
        {
            ValidIssuer = ApiFactoryFixture.PublicBaseUrl + "/",
            ValidAudience = IdentityIssuerTestClient.Resource,
            IssuerSigningKeys = jwks.GetSigningKeys(),

            // O relógio do app andou meses; o que este passo afirma é a assinatura.
            ValidateLifetime = false,
        });
        Assert.True(result.IsValid, result.Exception?.ToString());

        // 4. Passadas 24 h do fim de assinatura, a anterior sai do JWKS.
        factory.Clock.SetUtcNow(active.ValidatesUntil + TimeSpan.FromMinutes(1));
        await WaitForAsync(() => ring.Snapshot!.SigningKeys.All(key => key.KeyId != first.Kid));

        Assert.Equal([next], await JwksKidsAsync(client));
    }

    private static async Task<(string Kid, string Token)> TokenKidAsync(HttpClient client)
    {
        var response = await client.PostAsync("/connect/token", IdentityIssuerTestClient.TokenRequest());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var token = JsonDocument.Parse(body).RootElement.GetProperty(Parameters.AccessToken).GetString()!;
        return (new JsonWebToken(token).Kid, token);
    }

    private static async Task<List<string>> JwksKidsAsync(HttpClient client) =>
        new JsonWebKeySet(await client.GetStringAsync("/.well-known/jwks")).Keys.Select(key => key.Kid).ToList();

    private async Task<IdentityKey> RowAsync(string kid)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdentityKeys.AsNoTracking().SingleAsync(key => key.Kid == kid);
    }

    /// <summary>
    /// O avanço do relógio dispara o tique na hora, mas a verificação roda no
    /// <c>BackgroundService</c>. Espera limitada, em tempo real, pelo efeito.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "a verificação de rotação não produziu o efeito esperado em 15 s");
            await Task.Delay(50);
        }
    }
}
