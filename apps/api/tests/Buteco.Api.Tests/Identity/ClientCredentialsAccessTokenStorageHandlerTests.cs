using Buteco.Api.Identity;
using Buteco.Api.Identity.Keys;
using Buteco.Api.Identity.Storage;
using Buteco.Api.Infrastructure;
using Buteco.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;
using static OpenIddict.Server.OpenIddictServerHandlers.Protection;

namespace Buteco.Api.Tests.Identity;

/// <summary>
/// Só o access token do grant <c>client_credentials</c> sai sem linha (spec
/// <c>issuer-token-storage</c>; design.md da change limpeza-tokens-openiddict, D1), e o
/// handler fica, nas opções compostas pelo registro real, entre os dois handlers internos
/// de que depende.
/// </summary>
public class ClientCredentialsAccessTokenStorageHandlerTests
{
    public static TheoryData<string, string, bool> Matrix()
    {
        var data = new TheoryData<string, string, bool>();
        string[] types =
        [
            TokenTypeIdentifiers.AccessToken,
            TokenTypeIdentifiers.RefreshToken,
            TokenTypeIdentifiers.IdentityToken,
            TokenTypeIdentifiers.Private.AuthorizationCode,
        ];
        string[] grants = [GrantTypes.ClientCredentials, GrantTypes.AuthorizationCode, GrantTypes.RefreshToken];

        foreach (var type in types)
        {
            foreach (var grant in grants)
            {
                var stored = !(type == TokenTypeIdentifiers.AccessToken && grant == GrantTypes.ClientCredentials);
                data.Add(type, grant, stored);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task OnlyTheClientCredentialsAccessToken_LosesItsEntry(string tokenType, string grantType, bool stored)
    {
        var context = new GenerateTokenContext(new OpenIddictServerTransaction
        {
            Request = new OpenIddictRequest { GrantType = grantType },
            Options = new OpenIddictServerOptions(),
            Logger = NullLogger.Instance,
        })
        {
            TokenType = tokenType,
            CreateTokenEntry = true,
        };

        await new ClientCredentialsAccessTokenStorageHandler().HandleAsync(context);

        Assert.Equal(stored, context.CreateTokenEntry);
    }

    [Fact]
    public void InTheComposedServerOptions_RunsAfterTheCredentials_AndBeforeTheEntryIsCreated()
    {
        var handlers = ComposedHandlers()
            .Where(descriptor => descriptor.ContextType == typeof(GenerateTokenContext))
            .ToList();

        var ours = handlers.IndexOf(ClientCredentialsAccessTokenStorageHandler.Descriptor);
        var credentials = handlers.IndexOf(AttachSecurityCredentials.Descriptor);
        var createEntry = handlers.IndexOf(CreateTokenEntry.Descriptor);

        Assert.True(ours >= 0, "o registro real não adicionou o handler às opções do servidor");
        Assert.True(credentials < ours && ours < createEntry, $"ordem: credenciais {credentials}, handler {ours}, entrada {createEntry}");
    }

    /// <summary>
    /// As opções do servidor pelo registro real (<c>AddIdentityIssuer</c>), menos o
    /// configurer do anel, que exigiria as chaves, e ordenadas como o
    /// <c>OpenIddictServerConfiguration.PostConfigure</c> ordena (l. 541).
    /// </summary>
    private static List<OpenIddictServerHandlerDescriptor> ComposedHandlers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(TestAuthentication.ConfigOverrides)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PublicUrl:BaseUrl"] = ApiFactoryFixture.PublicBaseUrl })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(options => options.UseButecoAgentsNpgsql("Host=localhost"));
        services.AddIdentityIssuer(configuration);

        using var provider = services.BuildServiceProvider();
        var options = new OpenIddictServerOptions();
        foreach (var configurer in provider.GetServices<IConfigureOptions<OpenIddictServerOptions>>()
                     .Where(configurer => configurer is not IdentityKeyRingOptionsConfigurer))
        {
            configurer.Configure(options);
        }

        var handlers = options.Handlers.ToList();
        handlers.Sort(static (left, right) => left.Order.CompareTo(right.Order));
        return handlers;
    }
}
