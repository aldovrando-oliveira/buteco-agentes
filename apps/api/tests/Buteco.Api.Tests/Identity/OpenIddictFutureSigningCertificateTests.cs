using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Trava da tarefa 3.4 da change emissor-oidc-api: o fato F10 do design, lido na fonte
/// do OpenIddict 7.7.2 e provado aqui antes de qualquer código depender dele. Um
/// certificado X.509 de assinatura com <c>NotBefore</c> no futuro:
/// <list type="bullet">
/// <item>não faz a configuração lançar (<c>ID0088</c> só com TODAS fora da validade);</item>
/// <item>é publicado no JWKS, porque <c>AttachSigningKeys</c> não filtra por validade;</item>
/// <item>nunca assina, porque o comparador de credenciais o põe por último.</item>
/// </list>
/// É sobre a biblioteca, e não sobre o anel do <c>apps/api</c>: nenhum tipo de
/// produção entra aqui, para o teste continuar dizendo a verdade sobre o OpenIddict
/// mesmo se o anel mudar.
/// </summary>
public class OpenIddictFutureSigningCertificateTests
{
    private const string ValidKid = "chave-valida";
    private const string FutureKid = "chave-futura";
    private const string ClientId = "cliente-trava";

    [Fact]
    public async Task FutureCertificate_IsAccepted_PublishedInJwks_AndNeverSigns()
    {
        var now = DateTimeOffset.UtcNow;

        // A futura vai PRIMEIRO, para a ordem de registro não ser o que escolhe.
        var future = SelfSigned(FutureKid, now.AddDays(7), now.AddDays(98));
        var valid = SelfSigned(ValidKid, now.AddMinutes(-5), now.AddDays(91));

        await using var app = await StartIssuerAsync(future, valid);

        // 1. A configuração é aceita: montar as opções roda o PostConfigure inteiro.
        var options = app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        Assert.Equal(ValidKid, options.SigningCredentials[0].Key.KeyId);

        var client = app.GetTestClient();

        // 2. O JWKS publica as duas.
        using var jwks = JsonDocument.Parse(await client.GetStringAsync("/.well-known/jwks"));
        var kids = jwks.RootElement.GetProperty("keys").EnumerateArray()
            .Select(key => key.GetProperty("kid").GetString())
            .ToHashSet();
        Assert.Equal(new HashSet<string?> { ValidKid, FutureKid }, kids);

        // 3. O token sai assinado pela válida, e várias vezes, para não depender de sorte.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [Parameters.GrantType] = GrantTypes.ClientCredentials,
                [Parameters.ClientId] = ClientId,
                [Parameters.ClientSecret] = "qualquer",
            }));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(response.IsSuccessStatusCode, body.ToString());

            var token = new JsonWebToken(body.GetProperty(Parameters.AccessToken).GetString());
            Assert.Equal(ValidKid, token.Kid);
        }
    }

    [Fact]
    public async Task OnlyFutureCertificate_IsRefused_WithId0088()
    {
        // O outro lado do F10, para o teste acima não passar por uma configuração que
        // ignora a validade: com SÓ a futura, o OpenIddict recusa.
        var now = DateTimeOffset.UtcNow;
        var future = SelfSigned(FutureKid, now.AddDays(7), now.AddDays(98));

        await using var app = await StartIssuerAsync(future);

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue);
        // O texto do recurso ID0088 (OpenIddictResources.resx), que não traz o código.
        Assert.StartsWith("When using X.509 signing credentials, at least one of the registered certificates must be valid.", exception.Message);
    }

    private static async Task<WebApplication> StartIssuerAsync(params X509SecurityKey[] signingKeys)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddOpenIddict().AddServer(server =>
        {
            server.SetIssuer(new Uri("https://emissor.test/"));
            server.SetTokenEndpointUris("connect/token");
            server.SetJsonWebKeySetEndpointUris(".well-known/jwks");
            server.AllowClientCredentialsFlow();
            server.DisableAccessTokenEncryption();

            // Sem banco: o modo degradado deixa o cliente ser aceito pelo handler abaixo.
            server.EnableDegradedMode();

            foreach (var key in signingKeys)
            {
                server.AddSigningCredentials(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            }

            server.AddEncryptionKey(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)));

            server.AddEventHandler<ValidateTokenRequestContext>(handler => handler.UseInlineHandler(context =>
            {
                if (context.ClientId != ClientId)
                {
                    context.Reject(Errors.InvalidClient);
                }

                return default;
            }));

            server.AddEventHandler<HandleTokenRequestContext>(handler => handler.UseInlineHandler(context =>
            {
                var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType);
                identity.SetClaim(Claims.Subject, context.ClientId);
                identity.SetDestinations(static _ => [Destinations.AccessToken]);
                context.SignIn(new ClaimsPrincipal(identity));
                return default;
            }));

            server.UseAspNetCore().DisableTransportSecurityRequirement();
        });

        var app = builder.Build();
        app.UseAuthentication();
        await app.StartAsync();
        return app;
    }

    private static X509SecurityKey SelfSigned(string kid, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={kid}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(notBefore, notAfter);
        return new X509SecurityKey(certificate, kid);
    }
}
