namespace Buteco.Connectors.Connectors;

public static class ConnectorRegistrationExtensions
{
    /// <summary>
    /// Registra um conector completo sob <paramref name="key"/>: os dois contratos keyed
    /// e a conta.
    /// </summary>
    public static IServiceCollection AddConnector(
        this IServiceCollection services,
        string key,
        string accountEmail,
        Func<IServiceProvider, IFolderNavigator> navigator,
        Func<IServiceProvider, IFolderContentSource> contentSource)
    {
        services.AddKeyedSingleton(key, (provider, _) => navigator(provider));
        services.AddKeyedSingleton(key, (provider, _) => contentSource(provider));
        services.AddSingleton(new ConnectorAccount(key, accountEmail));
        return services;
    }

    /// <summary>
    /// Checagem de integridade no startup (convenção 8, forma
    /// <c>IServiceCollection</c>, antes do <c>Build()</c>, porque é ali que as chaves
    /// keyed são enumeráveis): toda chave com um dos três registros tem os outros dois,
    /// e no máximo uma conta. Sem isto, um conector incompleto só apareceria na
    /// primeira chamada do contrato que falta, que para <see cref="IFolderContentSource"/>
    /// é o ciclo da #105, longe de quem registrou.
    /// </summary>
    public static void ValidateConnectorRegistrations(this IServiceCollection services)
    {
        var navigatorKeys = KeysFor<IFolderNavigator>(services);
        var contentSourceKeys = KeysFor<IFolderContentSource>(services);
        var accountKeys = services
            .Where(descriptor => descriptor.ServiceType == typeof(ConnectorAccount) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance is ConnectorAccount account
                ? account.Key
                : throw new InvalidOperationException(
                    $"{nameof(ConnectorAccount)} precisa ser registrado como instância, para a chave ser conferida no boot."))
            .ToList();

        var registrations = new (string Name, IReadOnlyCollection<object> Keys)[]
        {
            (nameof(IFolderNavigator), navigatorKeys),
            (nameof(IFolderContentSource), contentSourceKeys),
            (nameof(ConnectorAccount), accountKeys.Cast<object>().ToHashSet()),
        };

        var allKeys = navigatorKeys.Union(contentSourceKeys).Union(accountKeys);

        var problems = allKeys
            .SelectMany(key => registrations
                .Where(registration => !registration.Keys.Contains(key))
                .Select(registration => $"'{key}' não tem {registration.Name} registrado"))
            .ToList();

        problems.AddRange(accountKeys
            .GroupBy(key => key)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}' tem {group.Count()} registros de {nameof(ConnectorAccount)}"));

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registro de conectores incompleto: {string.Join("; ", problems)}.");
        }
    }

    private static HashSet<object> KeysFor<TService>(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(TService) && descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ServiceKey!)
            .ToHashSet();
}
