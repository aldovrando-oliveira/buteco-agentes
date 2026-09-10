using Buteco.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Buteco.Api.Tests.Support;

/// <summary>
/// Arranjo dos guardas de desempate dos catálogos, que ordenam por
/// <c>CreatedAt</c>.
///
/// <para>
/// Sem isto o guarda não vale nada: <c>CreatedAt</c> é <c>private set</c>
/// atribuído a <c>DateTimeOffset.UtcNow</c> no construtor da entidade, então
/// dois registros criados por HTTP em sequência **nunca empatam**, e um guarda
/// que só crie dois e afirme a ordem passa com e sem o <c>ThenBy</c> — o defeito
/// de guarda que a convenção 15 nomeia. O empate tem de ser forçado no banco,
/// depois da criação.
/// </para>
/// </summary>
internal static class CreatedAtTie
{
    /// <summary>
    /// Força todos os <paramref name="ids"/> da tabela informada a compartilhar
    /// o mesmo <c>CreatedAt</c>, e **confere que o empate realmente aconteceu**
    /// lendo de volta do banco — arranjo que silenciosamente não empata deixaria
    /// o guarda verde com e sem a correção.
    /// </summary>
    public static async Task ForceAsync(IServiceProvider services, string table, IReadOnlyList<Guid> ids)
    {
        var instant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var affected = await dbContext.Database.ExecuteSqlRawAsync(
            $"UPDATE {table} SET \"CreatedAt\" = {{0}} WHERE \"Id\" = ANY({{1}})",
            instant,
            ids.ToArray());

        Assert.Equal(ids.Count, affected);

        var distinct = await dbContext.Database
            .SqlQueryRaw<DateTimeOffset>(
                $"SELECT DISTINCT \"CreatedAt\" AS \"Value\" FROM {table} WHERE \"Id\" = ANY({{0}})",
                ids.ToArray())
            .ToListAsync();

        Assert.True(
            distinct.Count == 1,
            $"O arranjo não produziu empate: {distinct.Count} valores distintos de CreatedAt em {table}.");
    }
}
