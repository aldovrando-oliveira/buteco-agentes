using System.Text;
using Buteco.Api.Identity.Federated;
using Buteco.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Client;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// O host do login federado com a pausa do aceite trocada por uma controlável (design.md da change
/// aceite-convite-google, D4; tasks.md, 2.3): o teste põe um aceite por senha, um link novo, uma edição de
/// e-mail ou outro vínculo entre a leitura do convite e o consumo.
/// </summary>
public sealed class FederatedInvitationFixture : FederatedLoginFixture
{
    public ControllablePause Pause { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<FederatedInvitationPause>(Pause));
    }

    /// <summary>Sem ação armada, não faz nada. Armada, roda a ação uma vez, no convidado pedido, antes do consumo.</summary>
    public sealed class ControllablePause : FederatedInvitationPause
    {
        private Func<Guid, Task>? _action;
        private Guid? _target;

        public void Arm(Guid invitedId, Func<Guid, Task> action)
        {
            _target = invitedId;
            _action = action;
        }

        public override async Task BeforeConsumeAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (_target == userId && Interlocked.Exchange(ref _action, null) is { } action)
            {
                await action(userId);
            }
        }
    }
}

/// <summary>
/// Lê o state token gravado em <c>openiddict_tokens</c> e o decifra com as credenciais de cifra do cliente
/// OIDC (tasks.md, 1.4): o payload é o JWE que o cliente gravou (<c>PersistTokenPayload</c>), e o texto
/// decifrado é o que um vazamento do banco mais as chaves revelaria.
/// </summary>
public static class StateTokenReader
{
    public const string StateType = "urn:openiddict:params:oauth:token-type:state_token";

    public static async Task<IReadOnlyList<string>> IdsAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().AsNoTracking()
            .Where(token => token.Type == StateType).Select(token => token.Id!).ToListAsync();
    }

    /// <summary>O payload decifrado (o JSON das claims) do único state token criado depois de <paramref name="before"/>.</summary>
    public static async Task<string> DecryptedPayloadOfNewAsync(IServiceProvider services, IReadOnlyList<string> before)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = await db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken>().AsNoTracking()
            .Where(token => token.Type == StateType && !before.Contains(token.Id!)).ToListAsync();
        var payload = Assert.Single(created).Payload;
        Assert.False(string.IsNullOrEmpty(payload), "o state token deveria ter o payload gravado");

        var options = services.GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>().CurrentValue;
        var handler = new JsonWebTokenHandler();
        var jws = handler.DecryptToken(new JsonWebToken(payload), new TokenValidationParameters
        {
            TokenDecryptionKeys = options.EncryptionCredentials.Select(credentials => credentials.Key).ToList(),
        });
        return Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(new JsonWebToken(jws).EncodedPayload));
    }
}
