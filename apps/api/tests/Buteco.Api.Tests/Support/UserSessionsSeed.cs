using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.EntityFrameworkCore.Models;

namespace Buteco.Api.Tests.Support;

/// <summary>Leitura direta das tabelas do OpenIddict, para as asserções sobre sessões (design.md da change rotas-conta-usuarios, D4).</summary>
public static class UserSessionsSeed
{
    public static async Task<IReadOnlyList<string>> AuthorizationStatusesAsync(IServiceProvider services, Guid userId)
    {
        await using var scope = services.CreateAsyncScope();
        var subject = userId.ToString();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<OpenIddictEntityFrameworkCoreAuthorization>()
            .Where(authorization => authorization.Subject == subject)
            .Select(authorization => authorization.Status!)
            .ToListAsync();
    }
}
