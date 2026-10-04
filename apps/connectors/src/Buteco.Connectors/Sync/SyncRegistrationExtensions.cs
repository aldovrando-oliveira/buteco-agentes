using Buteco.Connectors.Auth;
using Buteco.Connectors.Options;
using Microsoft.Extensions.Options;

namespace Buteco.Connectors.Sync;

public static class SyncRegistrationExtensions
{
    /// <summary>
    /// O ciclo de sincronização (design.md da change ciclo-de-sincronizacao). As peças são
    /// registradas sempre, porque o "Sincronizar agora" responde mesmo sem o endereço
    /// (<c>sync-not-configured</c>). O agendamento só entra com <c>Api:BaseUrl</c>
    /// configurada (D2), lida aqui, antes do <c>Build()</c>, como a credencial do Google.
    /// </summary>
    public static IServiceCollection AddKnowledgeSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));

        // Transient: AddHttpMessageHandler<T> não registra o handler no contêiner.
        services.AddTransient<ServiceTokenDelegatingHandler>();
        services.AddHttpClient(SyncApiClient.HttpClientName, (provider, client) =>
            {
                var api = provider.GetRequiredService<IOptions<ApiOptions>>().Value;
                if (api.IsConfigured)
                {
                    client.BaseAddress = new Uri(api.BaseUrl!);
                }

                client.Timeout = SyncApiClient.Timeout;
            })
            .AddHttpMessageHandler<ServiceTokenDelegatingHandler>();

        services.AddSingleton<ISyncApiClient, SyncApiClient>();
        services.AddSingleton<RefusalMemory>();
        services.AddSingleton<SyncInProgress>();
        services.AddSingleton<KnowledgeBaseSyncCycle>();
        services.AddSingleton<SyncRound>();
        services.AddSingleton<ISyncRound>(provider => provider.GetRequiredService<SyncRound>());

        var configured = configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>()?.IsConfigured ?? false;
        if (configured)
        {
            services.AddHostedService<SyncSchedulerService>();
        }

        return services;
    }
}
