using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Buteco.Api.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres não configurado.");

        // UseVector() registra o mapeamento do tipo `vector` do pgvector no
        // provider. Sem ele o EF não sabe traduzir Pgvector.Vector e a
        // migração/consulta falha em runtime, não na compilação.
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        return services;
    }
}
